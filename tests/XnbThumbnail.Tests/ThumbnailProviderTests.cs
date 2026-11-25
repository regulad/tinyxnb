using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace XnbThumbnail.Tests
{
    /// <summary>
    /// The provider in-process, driven as Explorer's thumbnail cache drives it: the file's bytes
    /// arrive as a Shell memory IStream through IInitializeWithStream.Initialize, then
    /// IThumbnailProvider.GetThumbnail(cx) returns an HBITMAP, which is read back with GDI.
    /// </summary>
    [TestClass]
    public sealed class ThumbnailProviderTests
    {
        internal static int Thumbnail(byte[] xnb, uint cx, out IntPtr hbmp, out WtsAlphaType alpha)
        {
            XnbThumbnailProvider provider = new XnbThumbnailProvider();
            IStream stream = Native.SHCreateMemStream(xnb, (uint)xnb.Length);
            Assert.IsNotNull(stream, "SHCreateMemStream failed.");
            try
            {
                Assert.AreEqual(Native.S_OK, provider.Initialize(stream, 0 /* STGM_READ */));
                return provider.GetThumbnail(cx, out hbmp, out alpha);
            }
            finally
            {
                Marshal.ReleaseComObject(stream);
            }
        }

        private static Dib ThumbnailDib(byte[] xnb, uint cx)
        {
            IntPtr hbmp;
            WtsAlphaType alpha;
            int hr = Thumbnail(xnb, cx, out hbmp, out alpha);
            Assert.AreEqual(Native.S_OK, hr, $"GetThumbnail returned 0x{hr:X8}.");
            try
            {
                Assert.AreEqual(WtsAlphaType.Argb, alpha, "The thumbnail must declare premultiplied alpha.");
                Dib dib = Dib.Read(hbmp);
                Assert.IsTrue(dib.IsDibSection, "The HBITMAP must be a DIB section.");
                Assert.AreEqual(32, dib.BitsPerPixel);
                return dib;
            }
            finally
            {
                Native.DeleteObject(hbmp);
            }
        }

        /// <summary>Asserts that a DIB holds exactly <paramref name="rgba"/>, swizzled to BGRA.</summary>
        internal static void AssertPixels(byte[] rgba, Dib dib)
        {
            Assert.AreEqual(rgba.Length, dib.Bgra.Length);
            for (int i = 0; i < rgba.Length; i += 4)
            {
                int p = i / 4;
                string at = $"pixel ({p % dib.Width}, {p / dib.Width})";
                Assert.AreEqual(rgba[i + 2], dib.Bgra[i], $"blue at {at}");
                Assert.AreEqual(rgba[i + 1], dib.Bgra[i + 1], $"green at {at}");
                Assert.AreEqual(rgba[i], dib.Bgra[i + 2], $"red at {at}");
                Assert.AreEqual(rgba[i + 3], dib.Bgra[i + 3], $"alpha at {at}");
            }
        }

        [TestMethod]
        public void Color4x4IsPixelExact()
        {
            Dib dib = ThumbnailDib(Samples.Color4x4Xnb(), 4);

            Assert.AreEqual(4, dib.Width);
            Assert.AreEqual(4, dib.Height);
            AssertPixels(Samples.Color4x4Rgba, dib);
        }

        [TestMethod]
        public void Color4x4UpscalesByNearestNeighbour()
        {
            Dib dib = ThumbnailDib(Samples.Color4x4Xnb(), 16);

            Assert.AreEqual(16, dib.Width);
            Assert.AreEqual(16, dib.Height);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    int s = ((y / 4) * 4 + x / 4) * 4;
                    var px = dib.Pixel(x, y);
                    Assert.AreEqual((Samples.Color4x4Rgba[s + 2], Samples.Color4x4Rgba[s + 1], Samples.Color4x4Rgba[s], Samples.Color4x4Rgba[s + 3]),
                                    px, $"pixel ({x}, {y})");
                }
            }
        }

        [TestMethod]
        public void Bc1BlockDecodesToKnownColours()
        {
            Dib dib = ThumbnailDib(Samples.Bc1Xnb(), 4);

            Assert.AreEqual(4, dib.Width);
            Assert.AreEqual(4, dib.Height);
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    var c = Samples.Bc1Row[x];
                    Assert.AreEqual((c.B, c.G, c.R, (byte)255), dib.Pixel(x, y), $"pixel ({x}, {y})");
                }
            }
        }

        [TestMethod]
        public void DownscalesToFitAndKeepsAspect()
        {
            Dib dib = ThumbnailDib(Samples.SolidXnb(64, 32, 10, 200, 30), 16);

            Assert.AreEqual(16, dib.Width);
            Assert.AreEqual(8, dib.Height);
            // A box filter over a solid colour is that colour.
            Assert.AreEqual(((byte)30, (byte)200, (byte)10, (byte)255), dib.Pixel(7, 3));
        }

        [TestMethod]
        public void NonXnbIsRejectedWithBadHeader()
        {
            IntPtr hbmp;
            WtsAlphaType alpha;
            int hr = Thumbnail(Samples.NotXnb(), 256, out hbmp, out alpha);

            Assert.AreEqual(Native.WINCODEC_ERR_BADHEADER, hr, $"GetThumbnail returned 0x{hr:X8}.");
            Assert.AreEqual(IntPtr.Zero, hbmp);
        }

        [TestMethod]
        public void NonTextureXnbIsRejectedWithBadHeader()
        {
            IntPtr hbmp;
            WtsAlphaType alpha;
            int hr = Thumbnail(Samples.SpriteFontXnb(), 256, out hbmp, out alpha);

            Assert.AreEqual(Native.WINCODEC_ERR_BADHEADER, hr, $"GetThumbnail returned 0x{hr:X8}.");
            Assert.AreEqual(IntPtr.Zero, hbmp);
        }
    }
}
