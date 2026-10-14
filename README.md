# Lupe

**Read QR codes and barcodes on Windows – from a webcam, any part of the screen, an image file or the clipboard.**

Lupe finds every code in the picture (QR, Data Matrix, Aztec, PDF417, Code 128, EAN/UPC and more), copies the content to the clipboard, types it into the window you were working in, or opens it as a link if it matches one of your link rules. A global shortcut (Win+Shift+Q) scans a screen region from anywhere.

> [!NOTE]
> Full documentation with screenshots is in progress. The application UI is in German.

## Download

Every push to `main` updates the pre-release **[latest](https://github.com/ict-baden/lupe/releases/tag/latest)**:

- `Lupe-…-x64.msi` – installer for all users
- `Lupe.exe` – single file, no installation, no .NET runtime required
- `Lupe-…-win-x64-framework.zip` – small variant, requires the .NET 10 Desktop Runtime

## Build

```powershell
dotnet build Lupe.sln
dotnet test Lupe.sln
```

---

Part of the ICT-Baden open-source tools. A rewrite of [Barcode-Reader](https://github.com/pieewiee/Barcode-Reader).
