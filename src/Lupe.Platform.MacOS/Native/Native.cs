// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;

namespace Lupe.Platform.MacOS.Native;

// P/Invoke-Signaturen nach den Apple-Headern (macOS-14-SDK, Deklaration jeweils darüber); noch nicht auf einem Mac gelaufen. Grundsätze:
// - C-„bool“ und Carbon-„Boolean“ sind 1 Byte → als byte zurückgeben (0 = falsch).
// - size_t / unsigned long / UniCharCount / ByteCount / ItemCount → nuint; CFIndex / NSInteger → nint.
// - CGFloat ist auf 64-Bit-macOS double; CGRect = 4 × double (arm64: HFA in d0–d3, x64: über den Speicher – beides
//   erledigt der Marshaller für blittbare Strukturen).
// - objc_msgSend wird je Signatur einzeln und nicht variadisch deklariert (so verlangt es Apple für arm64).
// Alle Bibliotheken über ihren absoluten Pfad (liegen seit macOS 11 im dyld-Cache, dlopen findet sie trotzdem).

internal static class Lib
{
    public const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    public const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    public const string ImageIO = "/System/Library/Frameworks/ImageIO.framework/ImageIO";
    public const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    public const string HIToolbox = "/System/Library/Frameworks/Carbon.framework/Frameworks/HIToolbox.framework/HIToolbox";
    public const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    public const string AVFoundation = "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";
    public const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    public const string ObjC = "/usr/lib/libobjc.A.dylib";
    public const string System = "/usr/lib/libSystem.B.dylib";

    /// <summary>Liest eine exportierte Zeiger-Variable (z. B. <c>extern const CFStringRef kX;</c>); 0, wenn es sie nicht gibt.</summary>
    public static nint ReadPointerExport(string library, string symbol)
    {
        if (!NativeLibrary.TryLoad(library, out var handle)) return 0;
        return NativeLibrary.TryGetExport(handle, symbol, out var address) ? Marshal.ReadIntPtr(address) : 0;
    }

    /// <summary>Adresse eines exportierten Symbols (z. B. <c>kCFTypeDictionaryKeyCallBacks</c>); 0, wenn es es nicht gibt.</summary>
    public static nint AddressOfExport(string library, string symbol)
    {
        if (!NativeLibrary.TryLoad(library, out var handle)) return 0;
        return NativeLibrary.TryGetExport(handle, symbol, out var address) ? address : 0;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGRect(double x, double y, double width, double height)
{
    public double X = x, Y = y, Width = width, Height = height;
}

/// <summary>CoreFoundation.</summary>
internal static partial class CF
{
    public const nint kCFNumberSInt64Type = 4;

    // void CFRelease(CFTypeRef cf);
    [LibraryImport(Lib.CoreFoundation)] public static partial void CFRelease(nint cf);

    // CFDataRef CFDataCreate(CFAllocatorRef allocator, const UInt8 *bytes, CFIndex length);
    [LibraryImport(Lib.CoreFoundation)] public static unsafe partial nint CFDataCreate(nint allocator, byte* bytes, nint length);

    // const UInt8 *CFDataGetBytePtr(CFDataRef theData);
    [LibraryImport(Lib.CoreFoundation)] public static partial nint CFDataGetBytePtr(nint data);

    // CFNumberRef CFNumberCreate(CFAllocatorRef allocator, CFNumberType theType, const void *valuePtr);  (CFNumberType = CFIndex)
    [LibraryImport(Lib.CoreFoundation)] public static unsafe partial nint CFNumberCreate(nint allocator, nint type, void* value);

    // CFDictionaryRef CFDictionaryCreate(CFAllocatorRef, const void **keys, const void **values, CFIndex numValues,
    //     const CFDictionaryKeyCallBacks *keyCallBacks, const CFDictionaryValueCallBacks *valueCallBacks);
    [LibraryImport(Lib.CoreFoundation)]
    public static unsafe partial nint CFDictionaryCreate(nint allocator, nint* keys, nint* values, nint count, nint keyCallBacks, nint valueCallBacks);

    /// <summary>CFDictionary mit CFType-Rückrufen (behält Schlüssel und Werte). Freigeben mit <see cref="CFRelease"/>.</summary>
    public static unsafe nint Dictionary(ReadOnlySpan<nint> keys, ReadOnlySpan<nint> values)
    {
        nint keyCallBacks = Lib.AddressOfExport(Lib.CoreFoundation, "kCFTypeDictionaryKeyCallBacks");
        nint valueCallBacks = Lib.AddressOfExport(Lib.CoreFoundation, "kCFTypeDictionaryValueCallBacks");
        fixed (nint* k = keys)
        fixed (nint* v = values)
            return CFDictionaryCreate(0, k, v, keys.Length, keyCallBacks, valueCallBacks);
    }

    public static nint True => Lib.ReadPointerExport(Lib.CoreFoundation, "kCFBooleanTrue");

    public static unsafe nint Number(long value) => CFNumberCreate(0, kCFNumberSInt64Type, &value);
}

/// <summary>CoreGraphics: Bildschirme, Berechtigung Bildschirmaufnahme, Tastatur-Ereignisse, Bitmap-Kontexte.</summary>
internal static partial class CG
{
    // CGEventSourceStateID
    public const int kCGEventSourceStateHIDSystemState = 1;
    // CGEventTapLocation
    public const uint kCGHIDEventTap = 0;
    // CGImageAlphaInfo / CGBitmapInfo
    public const uint kCGImageAlphaPremultipliedFirst = 2;
    public const uint kCGBitmapByteOrder32Little = 2 << 12;
    // CGInterpolationQuality
    public const int kCGInterpolationHigh = 3;

    // CGError CGGetActiveDisplayList(uint32_t maxDisplays, CGDirectDisplayID *activeDisplays, uint32_t *displayCount);
    [LibraryImport(Lib.CoreGraphics)] public static unsafe partial int CGGetActiveDisplayList(uint maxDisplays, uint* displays, uint* count);

    // CGDirectDisplayID CGMainDisplayID(void);
    [LibraryImport(Lib.CoreGraphics)] public static partial uint CGMainDisplayID();

    // CGRect CGDisplayBounds(CGDirectDisplayID display);
    [LibraryImport(Lib.CoreGraphics)] public static partial CGRect CGDisplayBounds(uint display);

    // CGDisplayModeRef CGDisplayCopyDisplayMode(CGDirectDisplayID display);
    [LibraryImport(Lib.CoreGraphics)] public static partial nint CGDisplayCopyDisplayMode(uint display);

    // size_t CGDisplayModeGetPixelWidth(CGDisplayModeRef mode);  / ...Height
    [LibraryImport(Lib.CoreGraphics)] public static partial nuint CGDisplayModeGetPixelWidth(nint mode);
    [LibraryImport(Lib.CoreGraphics)] public static partial nuint CGDisplayModeGetPixelHeight(nint mode);

    // void CGDisplayModeRelease(CGDisplayModeRef mode);
    [LibraryImport(Lib.CoreGraphics)] public static partial void CGDisplayModeRelease(nint mode);

    // bool CGPreflightScreenCaptureAccess(void);  bool CGRequestScreenCaptureAccess(void);  (macOS 10.15+)
    [LibraryImport(Lib.CoreGraphics)] public static partial byte CGPreflightScreenCaptureAccess();
    [LibraryImport(Lib.CoreGraphics)] public static partial byte CGRequestScreenCaptureAccess();

    // CGEventSourceRef CGEventSourceCreate(CGEventSourceStateID stateID);
    [LibraryImport(Lib.CoreGraphics)] public static partial nint CGEventSourceCreate(int stateId);

    // bool CGEventSourceKeyState(CGEventSourceStateID stateID, CGKeyCode key);
    [LibraryImport(Lib.CoreGraphics)] public static partial byte CGEventSourceKeyState(int stateId, ushort key);

    // CGEventRef CGEventCreateKeyboardEvent(CGEventSourceRef source, CGKeyCode virtualKey, bool keyDown);
    [LibraryImport(Lib.CoreGraphics)] public static partial nint CGEventCreateKeyboardEvent(nint source, ushort virtualKey, byte keyDown);

    // void CGEventKeyboardSetUnicodeString(CGEventRef event, UniCharCount stringLength, const UniChar *unicodeString);
    [LibraryImport(Lib.CoreGraphics)] public static unsafe partial void CGEventKeyboardSetUnicodeString(nint evt, nuint length, char* text);

    // void CGEventSetFlags(CGEventRef event, CGEventFlags flags);  (CGEventFlags = uint64_t)
    [LibraryImport(Lib.CoreGraphics)] public static partial void CGEventSetFlags(nint evt, ulong flags);

    // void CGEventPost(CGEventTapLocation tap, CGEventRef event);
    [LibraryImport(Lib.CoreGraphics)] public static partial void CGEventPost(uint tap, nint evt);

    // CGColorSpaceRef CGColorSpaceCreateDeviceRGB(void);  void CGColorSpaceRelease(CGColorSpaceRef);
    [LibraryImport(Lib.CoreGraphics)] public static partial nint CGColorSpaceCreateDeviceRGB();
    [LibraryImport(Lib.CoreGraphics)] public static partial void CGColorSpaceRelease(nint space);

    // CGContextRef CGBitmapContextCreate(void *data, size_t width, size_t height, size_t bitsPerComponent,
    //     size_t bytesPerRow, CGColorSpaceRef space, uint32_t bitmapInfo);
    [LibraryImport(Lib.CoreGraphics)]
    public static unsafe partial nint CGBitmapContextCreate(void* data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow, nint space, uint bitmapInfo);

    // void CGContextRelease(CGContextRef c);
    [LibraryImport(Lib.CoreGraphics)] public static partial void CGContextRelease(nint context);

    // void CGContextSetRGBFillColor(CGContextRef c, CGFloat red, CGFloat green, CGFloat blue, CGFloat alpha);
    [LibraryImport(Lib.CoreGraphics)] public static partial void CGContextSetRGBFillColor(nint context, double r, double g, double b, double a);

    // void CGContextFillRect(CGContextRef c, CGRect rect);
    [LibraryImport(Lib.CoreGraphics)] public static partial void CGContextFillRect(nint context, CGRect rect);

    // void CGContextSetInterpolationQuality(CGContextRef c, CGInterpolationQuality quality);  (enum int32)
    [LibraryImport(Lib.CoreGraphics)] public static partial void CGContextSetInterpolationQuality(nint context, int quality);

    // void CGContextDrawImage(CGContextRef c, CGRect rect, CGImageRef image);
    [LibraryImport(Lib.CoreGraphics)] public static partial void CGContextDrawImage(nint context, CGRect rect, nint image);

    // size_t CGImageGetWidth(CGImageRef image);  size_t CGImageGetHeight(CGImageRef image);  void CGImageRelease(CGImageRef);
    [LibraryImport(Lib.CoreGraphics)] public static partial nuint CGImageGetWidth(nint image);
    [LibraryImport(Lib.CoreGraphics)] public static partial nuint CGImageGetHeight(nint image);
    [LibraryImport(Lib.CoreGraphics)] public static partial void CGImageRelease(nint image);
}

/// <summary>ImageIO: Bilddateien lesen.</summary>
internal static partial class ImageIO
{
    // CGImageSourceRef CGImageSourceCreateWithData(CFDataRef data, CFDictionaryRef options);
    [LibraryImport(Lib.ImageIO)] public static partial nint CGImageSourceCreateWithData(nint data, nint options);

    // size_t CGImageSourceGetCount(CGImageSourceRef isrc);
    [LibraryImport(Lib.ImageIO)] public static partial nuint CGImageSourceGetCount(nint source);

    // CGImageRef CGImageSourceCreateImageAtIndex(CGImageSourceRef isrc, size_t index, CFDictionaryRef options);
    [LibraryImport(Lib.ImageIO)] public static partial nint CGImageSourceCreateImageAtIndex(nint source, nuint index, nint options);

    // CGImageRef CGImageSourceCreateThumbnailAtIndex(CGImageSourceRef isrc, size_t index, CFDictionaryRef options);
    [LibraryImport(Lib.ImageIO)] public static partial nint CGImageSourceCreateThumbnailAtIndex(nint source, nuint index, nint options);

    public static nint Key(string name) => Lib.ReadPointerExport(Lib.ImageIO, name);
}

/// <summary>ApplicationServices (HIServices): Bedienungshilfen-Berechtigung.</summary>
internal static partial class AX
{
    // Boolean AXIsProcessTrusted(void);
    [LibraryImport(Lib.ApplicationServices)] public static partial byte AXIsProcessTrusted();

    // Boolean AXIsProcessTrustedWithOptions(CFDictionaryRef options);
    [LibraryImport(Lib.ApplicationServices)] public static partial byte AXIsProcessTrustedWithOptions(nint options);

    public static nint TrustedCheckOptionPrompt => Lib.ReadPointerExport(Lib.ApplicationServices, "kAXTrustedCheckOptionPrompt");
}

[StructLayout(LayoutKind.Sequential)]
internal struct EventHotKeyID
{
    public uint Signature; // OSType
    public uint Id;
}

[StructLayout(LayoutKind.Sequential)]
internal struct EventTypeSpec
{
    public uint EventClass;
    public uint EventKind;
}

/// <summary>Carbon HIToolbox: globale Tastenkürzel, Tastaturlayout.</summary>
internal static partial class Carbon
{
    public const uint kEventClassKeyboard = 0x6B657962; // 'keyb'
    public const uint kEventHotKeyPressed = 5;
    public const uint kEventParamDirectObject = 0x2D2D2D2D; // '----'
    public const uint typeEventHotKeyID = 0x686B6964; // 'hkid'
    public const int noErr = 0;
    public const int eventHotKeyExistsErr = -9878;
    public const int eventHotKeyInvalidErr = -9879;

    // Modifikatoren (Events.h)
    public const uint cmdKey = 0x0100, shiftKey = 0x0200, optionKey = 0x0800, controlKey = 0x1000;

    // OSStatus RegisterEventHotKey(UInt32 inHotKeyCode, UInt32 inHotKeyModifiers, EventHotKeyID inHotKeyID,
    //     EventTargetRef inTarget, OptionBits inOptions, EventHotKeyRef *outRef);
    [LibraryImport(Lib.HIToolbox)]
    public static unsafe partial int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyID id, nint target, uint options, nint* outRef);

    // OSStatus UnregisterEventHotKey(EventHotKeyRef inHotKey);
    [LibraryImport(Lib.HIToolbox)] public static partial int UnregisterEventHotKey(nint hotKey);

    // EventTargetRef GetApplicationEventTarget(void);
    [LibraryImport(Lib.HIToolbox)] public static partial nint GetApplicationEventTarget();

    // OSStatus InstallEventHandler(EventTargetRef inTarget, EventHandlerUPP inHandler, ItemCount inNumTypes,
    //     const EventTypeSpec *inList, void *inUserData, EventHandlerRef *outRef);
    [LibraryImport(Lib.HIToolbox)]
    public static unsafe partial int InstallEventHandler(nint target, delegate* unmanaged<nint, nint, nint, int> handler, nuint numTypes, EventTypeSpec* list, nint userData, nint* outRef);

    // OSStatus GetEventParameter(EventRef inEvent, EventParamName inName, EventParamType inDesiredType,
    //     EventParamType *outActualType, ByteCount inBufferSize, ByteCount *outActualSize, void *outData);
    [LibraryImport(Lib.HIToolbox)]
    public static unsafe partial int GetEventParameter(nint evt, uint name, uint desiredType, uint* actualType, nuint bufferSize, nuint* actualSize, void* data);

    // TISInputSourceRef TISCopyCurrentKeyboardLayoutInputSource(void);
    [LibraryImport(Lib.HIToolbox)] public static partial nint TISCopyCurrentKeyboardLayoutInputSource();

    // void *TISGetInputSourceProperty(TISInputSourceRef inputSource, CFStringRef propertyKey);
    [LibraryImport(Lib.HIToolbox)] public static partial nint TISGetInputSourceProperty(nint source, nint key);

    // UInt8 LMGetKbdType(void);
    [LibraryImport(Lib.HIToolbox)] public static partial byte LMGetKbdType();

    public static nint PropertyUnicodeKeyLayoutData => Lib.ReadPointerExport(Lib.HIToolbox, "kTISPropertyUnicodeKeyLayoutData");
}

/// <summary>CoreServices (CarbonCore): Tastencode → Zeichen.</summary>
internal static partial class CoreServices
{
    public const ushort kUCKeyActionDisplay = 3;
    public const uint kUCKeyTranslateNoDeadKeysMask = 1;

    // OSStatus UCKeyTranslate(const UCKeyboardLayout *keyLayoutPtr, UInt16 virtualKeyCode, UInt16 keyAction,
    //     UInt32 modifierKeyState, UInt32 keyboardType, OptionBits keyTranslateOptions, UInt32 *deadKeyState,
    //     UniCharCount maxStringLength, UniCharCount *actualStringLength, UniChar unicodeString[]);
    [LibraryImport(Lib.CoreServices)]
    public static unsafe partial int UCKeyTranslate(nint layout, ushort keyCode, ushort keyAction, uint modifierKeyState, uint keyboardType,
        uint options, uint* deadKeyState, nuint maxLength, nuint* actualLength, char* text);
}

/// <summary>Objective-C-Laufzeit (für AVCaptureDevice, NSWorkspace, NSRunningApplication).</summary>
internal static partial class ObjC
{
    // Class objc_getClass(const char *name);  SEL sel_registerName(const char *str);
    [LibraryImport(Lib.ObjC, StringMarshalling = StringMarshalling.Utf8)] public static partial nint objc_getClass(string name);
    [LibraryImport(Lib.ObjC, StringMarshalling = StringMarshalling.Utf8)] public static partial nint sel_registerName(string name);

    // void *objc_autoreleasePoolPush(void);  void objc_autoreleasePoolPop(void *pool);
    [LibraryImport(Lib.ObjC)] public static partial nint objc_autoreleasePoolPush();
    [LibraryImport(Lib.ObjC)] public static partial void objc_autoreleasePoolPop(nint pool);

    // id objc_msgSend(id self, SEL op)
    [LibraryImport(Lib.ObjC, EntryPoint = "objc_msgSend")] public static partial nint SendPtr(nint self, nint sel);
    // id objc_msgSend(id self, SEL op, id arg)  – z. B. +[AVCaptureDevice authorizationStatusForMediaType:] (NSInteger)
    [LibraryImport(Lib.ObjC, EntryPoint = "objc_msgSend")] public static partial nint SendPtrPtr(nint self, nint sel, nint arg);
    // pid_t -[NSRunningApplication processIdentifier]
    [LibraryImport(Lib.ObjC, EntryPoint = "objc_msgSend")] public static partial int SendInt(nint self, nint sel);
    // +[NSRunningApplication runningApplicationWithProcessIdentifier:(pid_t)]
    [LibraryImport(Lib.ObjC, EntryPoint = "objc_msgSend")] public static partial nint SendPtrInt(nint self, nint sel, int arg);
    // BOOL -[NSRunningApplication activateWithOptions:(NSApplicationActivationOptions = NSUInteger)]
    [LibraryImport(Lib.ObjC, EntryPoint = "objc_msgSend")] public static partial byte SendByteNUInt(nint self, nint sel, nuint arg);

    /// <summary>Ruft <paramref name="action"/> in einem eigenen Autorelease-Pool auf (für Aufrufe außerhalb des Haupt-Threads).</summary>
    public static T InPool<T>(Func<T> action)
    {
        nint pool = objc_autoreleasePoolPush();
        try
        {
            return action();
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }
}

/// <summary>libSystem.</summary>
internal static partial class LibSystem
{
    // int pthread_main_np(void);  – 1 auf dem Haupt-Thread
    [LibraryImport(Lib.System)] public static partial int pthread_main_np();

    public static bool IsMainThread => pthread_main_np() == 1;
}
