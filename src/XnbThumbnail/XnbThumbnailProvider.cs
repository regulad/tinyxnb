using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using XnbCore;

namespace XnbThumbnail
{
    /// <summary>
    /// Explorer thumbnail handler for XNB files whose root object is a Texture2D.
    /// Registered against .xnb via the IThumbnailProvider shell extension GUID.
    /// </summary>
    [ComVisible(true)]
    [Guid(ClsidString)]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("XnbShell.ThumbnailProvider")]
    public sealed class XnbThumbnailProvider : IInitializeWithStream, IThumbnailProvider
    {
        public const string ClsidString = "017DC839-2FD6-4917-855E-1807CA2942AF";

        /// <summary>Refuse absurd inputs rather than let dllhost balloon on a malformed header.</summary>
        private const int MaxSourceEdge = 16384;
        private const int MaxStreamBytes = 256 * 1024 * 1024;

        private byte[] _bytes;

        public int Initialize(IStream stream, uint grfMode)
        {
            if (stream == null) return Hr.E_INVALIDARG;
            if (_bytes != null) return unchecked((int)0x800700AA); // HRESULT_FROM_WIN32(ERROR_ALREADY_INITIALIZED)

            try
            {
                _bytes = ReadAll(stream);
                return Hr.S_OK;
            }
            catch
            {
                _bytes = null;
                return Hr.E_FAIL;
            }
        }

        public int GetThumbnail(uint cx, out IntPtr phbmp, out WtsAlphaType pdwAlpha)
        {
            phbmp = IntPtr.Zero;
            pdwAlpha = WtsAlphaType.Unknown;

            if (_bytes == null) return Hr.E_FAIL;
            if (cx == 0 || cx > 4096) return Hr.E_INVALIDARG;

            try
            {
                XnbImage image;
                using (MemoryStream ms = new MemoryStream(_bytes, false))
                    image = XnbImage.Load(ms);

                if (image.Width > MaxSourceEdge || image.Height > MaxSourceEdge)
                    return Hr.E_FAIL;

                int w, h;
                // Explorer composites with premultiplied alpha, which is exactly how the
                // content pipeline already stores the pixels, so no conversion is needed.
                byte[] rgba = Resampler.Fit(image.PremultipliedRgba, image.Width, image.Height, (int)cx, out w, out h);

                IntPtr hbmp = CreateDib(rgba, w, h);
                if (hbmp == IntPtr.Zero) return Hr.E_OUTOFMEMORY;

                phbmp = hbmp;
                pdwAlpha = WtsAlphaType.Argb;
                return Hr.S_OK;
            }
            catch (XnbFormatException)
            {
                // Not a texture (font, model, data table) or not an XNB at all: let Explorer
                // fall back to the generic icon instead of showing a broken thumbnail.
                return Hr.WINCODEC_ERR_BADHEADER;
            }
            catch
            {
                return Hr.E_FAIL;
            }
        }

        private static IntPtr CreateDib(byte[] rgba, int width, int height)
        {
            BitmapInfoHeader bi = new BitmapInfoHeader();
            bi.biSize = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader));
            bi.biWidth = width;
            bi.biHeight = -height; // negative => top-down, matching our buffer
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            bi.biCompression = Gdi32.BI_RGB;

            IntPtr bits;
            IntPtr hbmp = Gdi32.CreateDIBSection(IntPtr.Zero, ref bi, Gdi32.DIB_RGB_COLORS, out bits, IntPtr.Zero, 0);
            if (hbmp == IntPtr.Zero || bits == IntPtr.Zero) return IntPtr.Zero;

            try
            {
                int count = width * height * 4;
                byte[] bgra = new byte[count];
                for (int i = 0; i < count; i += 4)
                {
                    bgra[i] = rgba[i + 2];
                    bgra[i + 1] = rgba[i + 1];
                    bgra[i + 2] = rgba[i];
                    bgra[i + 3] = rgba[i + 3];
                }
                Marshal.Copy(bgra, 0, bits, count);
                return hbmp;
            }
            catch
            {
                Gdi32.DeleteObject(hbmp);
                return IntPtr.Zero;
            }
        }

        private static byte[] ReadAll(IStream stream)
        {
            byte[] chunk = new byte[64 * 1024];
            IntPtr read = Marshal.AllocCoTaskMem(sizeof(int));
            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    while (true)
                    {
                        stream.Read(chunk, chunk.Length, read);
                        int n = Marshal.ReadInt32(read);
                        if (n <= 0) break;
                        ms.Write(chunk, 0, n);
                        if (ms.Length > MaxStreamBytes) throw new IOException("Stream too large.");
                    }
                    return ms.ToArray();
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(read);
            }
        }
    }
}
