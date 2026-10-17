using System.Runtime.InteropServices;
using Lupe.Core.Camera;
using Lupe.Core.Model;

namespace Lupe.Platform.Windows.Camera;

/// <summary>
/// Interop für die Kamera: Zugriff auf den rohen Pixelspeicher einer <see cref="Windows.Graphics.Imaging.SoftwareBitmap"/>
/// (IMemoryBufferByteAccess aus robuffer.h/MemoryBuffer.h). P/Invoke braucht der Baustein sonst keines.
/// </summary>
internal static class Native
{
    /// <summary>
    /// COM-Schnittstelle hinter <see cref="Windows.Foundation.IMemoryBufferReference"/>: liefert Zeiger und Größe des
    /// gesperrten Puffers. Der Zeiger gilt nur, solange Referenz und BitmapBuffer offen sind.
    /// </summary>
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }
}
