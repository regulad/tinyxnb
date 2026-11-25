using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace XnbThumbnail.Tests
{
    /// <summary>The Win32 and Shell calls the tests stand in for Explorer with.</summary>
    internal static class Native
    {
        public const int S_OK = 0;
        public const int WINCODEC_ERR_BADHEADER = unchecked((int)0x88982F61);

        public static readonly Guid ClsidXnbThumbnailProvider = new Guid(XnbThumbnailProvider.ClsidString);
        public static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
        public static readonly Guid IID_IClassFactory = new Guid("00000001-0000-0000-C000-000000000046");
        public static readonly Guid IID_IInitializeWithStream = new Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f");
        public static readonly Guid IID_IThumbnailProvider = new Guid("e357fccd-a995-4576-b01f-234630154e96");
        public static readonly Guid IID_IShellItemImageFactory = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        // ---- streams -------------------------------------------------------------------------

        /// <summary>
        /// The read-only memory IStream the Shell itself hands to stream-based handlers. Returns
        /// a runtime-callable wrapper, so the provider's reads cross into native code.
        /// </summary>
        [DllImport("shlwapi.dll", EntryPoint = "SHCreateMemStream")]
        public static extern IStream SHCreateMemStream(byte[] pInit, uint cbInit);

        /// <summary>The same stream as a raw IStream*, for calling through vtables. Release it when done.</summary>
        [DllImport("shlwapi.dll", EntryPoint = "SHCreateMemStream")]
        public static extern IntPtr SHCreateMemStreamPtr(byte[] pInit, uint cbInit);

        // ---- GDI -----------------------------------------------------------------------------

        public const uint BI_RGB = 0;
        public const uint DIB_RGB_COLORS = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
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

        [StructLayout(LayoutKind.Sequential)]
        public struct DIBSECTION
        {
            public BITMAP dsBm;
            public BITMAPINFOHEADER dsBmih;
            public uint dsBitfields0;
            public uint dsBitfields1;
            public uint dsBitfields2;
            public IntPtr dshSection;
            public uint dsOffset;
        }

        [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
        public static extern int GetObject(IntPtr h, int c, out DIBSECTION pv);

        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits,
                                           ref BITMAPINFOHEADER lpbmi, uint usage);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr ho);

        // ---- Shell ---------------------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
        }

        [Flags]
        public enum SIIGBF
        {
            ResizeToFit = 0x00,
            BiggerSizeOk = 0x01,
            MemoryOnly = 0x02,
            IconOnly = 0x04,
            ThumbnailOnly = 0x08,
            InCacheOnly = 0x10,
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
    }
}
