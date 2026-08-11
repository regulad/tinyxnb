using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace XnbThumbnail
{
    internal static class Hr
    {
        public const int S_OK = 0;
        public const int E_FAIL = unchecked((int)0x80004005);
        public const int E_INVALIDARG = unchecked((int)0x80070057);
        public const int E_OUTOFMEMORY = unchecked((int)0x8007000E);
        public const int WINCODEC_ERR_BADHEADER = unchecked((int)0x88982F61);
    }

    // Public because it appears in XnbThumbnailProvider.GetThumbnail's signature, and that
    // class must be public for COM. An internal enum there is CS0051.
    public enum WtsAlphaType : uint
    {
        Unknown = 0,
        Rgb = 1,
        Argb = 2, // 32bpp with *premultiplied* alpha
    }

    [ComImport]
    [Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IInitializeWithStream
    {
        [PreserveSig]
        int Initialize(IStream stream, uint grfMode);
    }

    [ComImport]
    [Guid("e357fccd-a995-4576-b01f-234630154e96")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IThumbnailProvider
    {
        [PreserveSig]
        int GetThumbnail(uint cx, out IntPtr phbmp, out WtsAlphaType pdwAlpha);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    internal static class Gdi32
    {
        public const uint BI_RGB = 0;
        public const uint DIB_RGB_COLORS = 0;

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader pbmi, uint usage,
                                                     out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}
