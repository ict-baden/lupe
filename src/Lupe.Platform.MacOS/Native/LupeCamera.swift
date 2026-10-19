// SPDX-License-Identifier: MIT
// Kamera unter macOS direkt über AVFoundation – ersetzt FlashCap, dessen AVFoundation-Teil Formate vertauscht und
// Objective-C-Objekte zu oft freigibt (falsche Farben, Absturz beim Aufräumen des Autorelease-Pools).
// Wird beim Bauen auf einem Mac zu libLupeCamera.dylib (arm64 + x86_64) übersetzt, siehe Lupe.Platform.MacOS.csproj;
// C#-Seite: Camera/NativeCamera.cs. Alle Bilder kommen als BGRA (kCVPixelFormatType_32BGRA), von oben nach unten.
//
// Speicher: Swift/ARC verwaltet alle Objective-C-Objekte. C# hält nur einen Zeiger auf ein CameraSession-Objekt
// (Unmanaged.passRetained), den lupe_cam_close wieder freigibt.

import AVFoundation
import CoreMedia
import CoreVideo
import Foundation

/// Bild-Rückruf: Kontext, BGRA-Pixel, Breite, Höhe, Bytes je Zeile. Nur während des Aufrufs gültig.
public typealias LupeFrameCallback = @convention(c) (UnsafeMutableRawPointer?, UnsafePointer<UInt8>, Int32, Int32, Int32) -> Void
/// Fehler-Rückruf während der Aufnahme: Kontext, Fehlercode (LUPE_CAM_…).
public typealias LupeErrorCallback = @convention(c) (UnsafeMutableRawPointer?, Int32) -> Void

// Fehlercodes – gleich in NativeCamera.cs
private let ok: Int32 = 0
private let errDenied: Int32 = 1
private let errRestricted: Int32 = 2
private let errNotFound: Int32 = 3
private let errOpen: Int32 = 4
private let errFormat: Int32 = 5
private let errRuntime: Int32 = 6
private let errDisconnected: Int32 = 7

private func videoDevices() -> [AVCaptureDevice] {
    var types: [AVCaptureDevice.DeviceType] = [.builtInWideAngleCamera]
    if #available(macOS 14.0, *) {
        types += [.external, .continuityCamera]
    } else {
        types.append(.externalUnknown)
    }
    return AVCaptureDevice.DiscoverySession(deviceTypes: types, mediaType: .video, position: .unspecified).devices
}

private func device(withId id: UnsafePointer<CChar>) -> AVCaptureDevice? {
    AVCaptureDevice(uniqueID: String(cString: id))
}

private func copyString(_ s: String) -> UnsafeMutablePointer<CChar>? {
    strdup(s)
}

/// Kamera-Berechtigung wie AVAuthorizationStatus: 0 unbestimmt, 1 eingeschränkt, 2 verweigert, 3 erlaubt.
@_cdecl("lupe_cam_authorization")
public func lupe_cam_authorization() -> Int32 {
    Int32(AVCaptureDevice.authorizationStatus(for: .video).rawValue)
}

/// Zeigt bei Bedarf den Systemdialog und wartet auf die Antwort. Rückgabe wie lupe_cam_authorization.
/// Nicht auf dem Haupt-Thread aufrufen.
@_cdecl("lupe_cam_request_access")
public func lupe_cam_request_access() -> Int32 {
    if AVCaptureDevice.authorizationStatus(for: .video) == .notDetermined {
        let done = DispatchSemaphore(value: 0)
        AVCaptureDevice.requestAccess(for: .video) { _ in done.signal() }
        done.wait()
    }
    return lupe_cam_authorization()
}

/// Alle Videokameras als Text: je Kamera eine Zeile „Kennung<TAB>Name“. Mit lupe_cam_free freigeben.
@_cdecl("lupe_cam_list")
public func lupe_cam_list() -> UnsafeMutablePointer<CChar>? {
    let lines = videoDevices().map { d in
        "\(d.uniqueID.replacingOccurrences(of: "\t", with: " "))\t\(d.localizedName.replacingOccurrences(of: "\t", with: " "))"
    }
    return copyString(lines.joined(separator: "\n"))
}

@_cdecl("lupe_cam_free")
public func lupe_cam_free(_ p: UnsafeMutableRawPointer?) {
    free(p)
}

/// Bildgrößen der Kamera als Tripel (Breite, Höhe, höchste Bildrate × 1000) nach `out`, höchstens `capacity` Tripel.
/// Rückgabe: Anzahl der Tripel; -1, wenn es die Kamera nicht gibt.
@_cdecl("lupe_cam_formats")
public func lupe_cam_formats(_ id: UnsafePointer<CChar>, _ out: UnsafeMutablePointer<Int32>, _ capacity: Int32) -> Int32 {
    guard let dev = device(withId: id) else { return -1 }
    var count: Int32 = 0
    for format in dev.formats where count < capacity {
        let dims = CMVideoFormatDescriptionGetDimensions(format.formatDescription)
        let fps = format.videoSupportedFrameRateRanges.map(\.maxFrameRate).max() ?? 0
        let base = Int(count) * 3
        out[base] = dims.width
        out[base + 1] = dims.height
        out[base + 2] = Int32((fps * 1000).rounded())
        count += 1
    }
    return count
}

/// Eine geöffnete Kamera. Bilder kommen auf einer eigenen seriellen Warteschlange.
final class CameraSession: NSObject, AVCaptureVideoDataOutputSampleBufferDelegate {
    private let session = AVCaptureSession()
    private let output = AVCaptureVideoDataOutput()
    private let queue = DispatchQueue(label: "de.ict-baden.lupe.camera")
    private let device: AVCaptureDevice
    private let context: UnsafeMutableRawPointer?
    private let onFrame: LupeFrameCallback
    private let onError: LupeErrorCallback
    private var observers: [NSObjectProtocol] = []
    /// Nur auf `queue` gelesen und geschrieben: nach dem Anhalten keine Rückrufe mehr.
    private var delivering = false

    init(device: AVCaptureDevice, context: UnsafeMutableRawPointer?, onFrame: LupeFrameCallback, onError: LupeErrorCallback) {
        self.device = device
        self.context = context
        self.onFrame = onFrame
        self.onError = onError
        super.init()
    }

    /// Richtet Eingang, Format und Ausgang ein; Rückgabe ist ein Fehlercode.
    func configure(width: Int32, height: Int32) -> Int32 {
        let input: AVCaptureDeviceInput
        do {
            input = try AVCaptureDeviceInput(device: device)
        } catch {
            return errOpen
        }

        session.beginConfiguration()
        defer { session.commitConfiguration() }
        guard session.canAddInput(input) else { return errOpen }
        session.addInput(input)

        output.videoSettings = [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA]
        output.alwaysDiscardsLateVideoFrames = true
        output.setSampleBufferDelegate(self, queue: queue)
        guard session.canAddOutput(output) else { return errFormat }
        session.addOutput(output)

        // Gewünschte Größe mit der höchsten Bildrate; erst nach addInput setzen, sonst stellt die Sitzung sie zurück
        if width > 0 && height > 0 {
            let candidates = device.formats.filter {
                let d = CMVideoFormatDescriptionGetDimensions($0.formatDescription)
                return d.width == width && d.height == height
            }
            let best = candidates.max {
                ($0.videoSupportedFrameRateRanges.map(\.maxFrameRate).max() ?? 0)
                    < ($1.videoSupportedFrameRateRanges.map(\.maxFrameRate).max() ?? 0)
            }
            if let best {
                do {
                    try device.lockForConfiguration()
                    device.activeFormat = best
                    device.unlockForConfiguration()
                } catch {
                    // Dann eben das Standardformat der Kamera
                }
            }
        }

        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: .AVCaptureSessionRuntimeError, object: session, queue: nil) { [weak self] _ in
            self?.report(errRuntime)
        })
        observers.append(center.addObserver(forName: .AVCaptureDeviceWasDisconnected, object: device, queue: nil) { [weak self] _ in
            self?.report(errDisconnected)
        })
        return ok
    }

    private func report(_ code: Int32) {
        queue.async { [self] in
            guard delivering else { return }
            delivering = false
            onError(context, code)
        }
    }

    func start() {
        queue.sync { delivering = true }
        session.startRunning()
    }

    /// Hält an und wartet, bis kein Rückruf mehr läuft. Nicht aus einem Rückruf heraus aufrufen.
    func stop() {
        queue.sync { delivering = false }
        if session.isRunning { session.stopRunning() }
    }

    func close() {
        stop()
        output.setSampleBufferDelegate(nil, queue: nil)
        for o in observers { NotificationCenter.default.removeObserver(o) }
        observers.removeAll()
        session.beginConfiguration()
        for i in session.inputs { session.removeInput(i) }
        for o in session.outputs { session.removeOutput(o) }
        session.commitConfiguration()
        queue.sync {} // letzte eingereihte Arbeit abwarten
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer, from connection: AVCaptureConnection) {
        guard delivering, let pixels = CMSampleBufferGetImageBuffer(sampleBuffer),
              CVPixelBufferGetPixelFormatType(pixels) == kCVPixelFormatType_32BGRA else { return }
        CVPixelBufferLockBaseAddress(pixels, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(pixels, .readOnly) }
        guard let base = CVPixelBufferGetBaseAddress(pixels) else { return }
        onFrame(context, base.assumingMemoryBound(to: UInt8.self),
                Int32(CVPixelBufferGetWidth(pixels)), Int32(CVPixelBufferGetHeight(pixels)),
                Int32(CVPixelBufferGetBytesPerRow(pixels)))
    }
}

/// Öffnet die Kamera (noch ohne Start). `width`/`height` 0 = Standardformat. Fehlercode nach `error`; null bei Fehler.
@_cdecl("lupe_cam_open")
public func lupe_cam_open(_ id: UnsafePointer<CChar>, _ width: Int32, _ height: Int32,
                          _ onFrame: LupeFrameCallback, _ onError: LupeErrorCallback,
                          _ context: UnsafeMutableRawPointer?, _ error: UnsafeMutablePointer<Int32>) -> UnsafeMutableRawPointer? {
    switch AVCaptureDevice.authorizationStatus(for: .video) {
    case .denied: error.pointee = errDenied; return nil
    case .restricted: error.pointee = errRestricted; return nil
    default: break
    }
    guard let dev = device(withId: id) else { error.pointee = errNotFound; return nil }
    let cam = CameraSession(device: dev, context: context, onFrame: onFrame, onError: onError)
    let code = cam.configure(width: width, height: height)
    guard code == ok else {
        cam.close()
        error.pointee = code
        return nil
    }
    error.pointee = ok
    return Unmanaged.passRetained(cam).toOpaque()
}

@_cdecl("lupe_cam_start")
public func lupe_cam_start(_ handle: UnsafeMutableRawPointer) {
    Unmanaged<CameraSession>.fromOpaque(handle).takeUnretainedValue().start()
}

@_cdecl("lupe_cam_stop")
public func lupe_cam_stop(_ handle: UnsafeMutableRawPointer) {
    Unmanaged<CameraSession>.fromOpaque(handle).takeUnretainedValue().stop()
}

/// Hält an, gibt die Kamera frei und löscht den Griff. Danach keine Rückrufe mehr.
@_cdecl("lupe_cam_close")
public func lupe_cam_close(_ handle: UnsafeMutableRawPointer) {
    let cam = Unmanaged<CameraSession>.fromOpaque(handle).takeRetainedValue()
    cam.close()
}
