using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Sentry;
using Tinyxnb;
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
        public const string ClsidString = "7E723B1E-5565-4E10-8F83-9676C8CDF2F0";

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

            // One `thumbnail` transaction per call, the usage count (PRIVACY.md): the requested
            // size and the surface format, never anything about the file itself. Null when
            // diagnostics are off, as in any build but a release, or under DO_NOT_TRACK.
            ITransactionTracer run = StartRun(cx);

            if (_bytes == null) return Done(run, SpanStatus.FailedPrecondition, Hr.E_FAIL);
            if (cx == 0 || cx > 4096) return Done(run, SpanStatus.InvalidArgument, Hr.E_INVALIDARG);

            try
            {
                XnbImage image;
                using (MemoryStream ms = new MemoryStream(_bytes, false))
                    image = XnbImage.Load(ms);

                run?.SetTag("format", image.Format.ToString());

                if (image.Width > MaxSourceEdge || image.Height > MaxSourceEdge)
                    return Done(run, SpanStatus.ResourceExhausted, Hr.E_FAIL);

                int w, h;
                // Explorer composites with premultiplied alpha, which is exactly how the
                // content pipeline already stores the pixels, so no conversion is needed.
                byte[] rgba = Resampler.Fit(image.PremultipliedRgba, image.Width, image.Height, (int)cx, out w, out h);

                IntPtr hbmp = CreateDib(rgba, w, h);
                if (hbmp == IntPtr.Zero) return Done(run, SpanStatus.ResourceExhausted, Hr.E_OUTOFMEMORY);

                phbmp = hbmp;
                pdwAlpha = WtsAlphaType.Argb;
                return Done(run, SpanStatus.Ok, Hr.S_OK);
            }
            catch (XnbFormatException)
            {
                // Not a texture (font, model, data table) or not an XNB at all: let Explorer
                // fall back to the generic icon instead of showing a broken thumbnail. No fault
                // of tinyxnb's, so it is counted but not reported.
                return Done(run, SpanStatus.InvalidArgument, Hr.WINCODEC_ERR_BADHEADER);
            }
            catch (Exception ex)
            {
                SentryDiagnostics.Capture(ex);
                Done(run, SpanStatus.InternalError, Hr.E_FAIL);
                // Failures are rare, and Explorer is waiting on this thumbnail: give the report a
                // moment to leave, as dllhost.exe may exit before the SDK's background worker
                // sends it, but no more.
                SentryDiagnostics.Flush(ErrorFlushTimeout);
                return Hr.E_FAIL;
            }
        }

        /// <summary>How long a failed thumbnail waits for its report to be sent.</summary>
        private static readonly TimeSpan ErrorFlushTimeout = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Sentry, started by the first thumbnail of this process and never before. Nothing
        /// hooks the host process (SentryDiagnostics.Start): only what GetThumbnail catches is
        /// reported. Null when diagnostics are off.
        /// </summary>
        private static readonly Lazy<IDisposable> Diagnostics =
            new Lazy<IDisposable>(SentryDiagnostics.Start, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        private static ITransactionTracer StartRun(uint cx)
        {
            _ = Diagnostics.Value;
            ITransactionTracer run = SentryDiagnostics.StartTransaction("thumbnail", "thumbnail.render");
            run?.SetTag("cx", cx.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return run;
        }

        private static int Done(ITransactionTracer run, SpanStatus status, int hr)
        {
            SentryDiagnostics.Finish(run, status);
            return hr;
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
