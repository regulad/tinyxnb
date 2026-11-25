using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XnbThumbnail.Tests
{
    /// <summary>An HBITMAP's pixels, read back the way the Shell would: 32bpp BGRA, top-down.</summary>
    internal sealed class Dib
    {
        public int Width { get; }
        public int Height { get; }
        public int BitsPerPixel { get; }
        public bool IsDibSection { get; }
        public byte[] Bgra { get; }

        private Dib(int width, int height, int bpp, bool isDibSection, byte[] bgra)
        {
            Width = width; Height = height; BitsPerPixel = bpp; IsDibSection = isDibSection; Bgra = bgra;
        }

        /// <summary>Reads <paramref name="hbmp"/> with GetObject and GetDIBits. Does not delete it.</summary>
        public static Dib Read(IntPtr hbmp)
        {
            Assert.AreNotEqual(IntPtr.Zero, hbmp, "No HBITMAP was returned.");

            Native.DIBSECTION ds;
            int size = Native.GetObject(hbmp, Marshal.SizeOf<Native.DIBSECTION>(), out ds);
            Assert.IsTrue(size > 0, "GetObject failed: not a valid HBITMAP.");
            // GetObject fills a whole DIBSECTION only for a DIB section; for a device-dependent
            // bitmap it returns sizeof(BITMAP).
            bool isDibSection = size == Marshal.SizeOf<Native.DIBSECTION>();

            int w = ds.dsBm.bmWidth, h = Math.Abs(ds.dsBm.bmHeight);
            Native.BITMAPINFOHEADER bi = new Native.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // ask for top-down rows, whatever the source's orientation
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Native.BI_RGB,
            };
            byte[] bgra = new byte[w * h * 4];

            IntPtr dc = Native.CreateCompatibleDC(IntPtr.Zero);
            try
            {
                int lines = Native.GetDIBits(dc, hbmp, 0, (uint)h, bgra, ref bi, Native.DIB_RGB_COLORS);
                Assert.AreEqual(h, lines, "GetDIBits did not copy every scan line.");
            }
            finally
            {
                Native.DeleteDC(dc);
            }

            return new Dib(w, h, ds.dsBm.bmBitsPixel, isDibSection, bgra);
        }

        /// <summary>The (b, g, r, a) of one pixel.</summary>
        public (byte B, byte G, byte R, byte A) Pixel(int x, int y)
        {
            int o = (y * Width + x) * 4;
            return (Bgra[o], Bgra[o + 1], Bgra[o + 2], Bgra[o + 3]);
        }
    }
}
