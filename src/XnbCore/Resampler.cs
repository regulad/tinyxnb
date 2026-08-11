using System;

namespace XnbCore
{
    /// <summary>
    /// Fits an RGBA image inside a square of <c>maxEdge</c> pixels, preserving aspect ratio.
    /// Downscales with a box filter (correct averaging, no ringing) and upscales with
    /// nearest-neighbour, which keeps game sprites looking like sprites rather than mush.
    /// </summary>
    public static class Resampler
    {
        public static byte[] Fit(byte[] rgba, int srcW, int srcH, int maxEdge, out int dstW, out int dstH)
        {
            if (maxEdge < 1) maxEdge = 1;

            double scale = Math.Min((double)maxEdge / srcW, (double)maxEdge / srcH);
            dstW = Math.Max(1, (int)Math.Round(srcW * scale));
            dstH = Math.Max(1, (int)Math.Round(srcH * scale));

            if (dstW == srcW && dstH == srcH)
                return rgba;

            return scale < 1.0
                ? Box(rgba, srcW, srcH, dstW, dstH)
                : Nearest(rgba, srcW, srcH, dstW, dstH);
        }

        private static byte[] Nearest(byte[] src, int sw, int sh, int dw, int dh)
        {
            byte[] dst = new byte[dw * dh * 4];
            for (int y = 0; y < dh; y++)
            {
                int sy = (int)((long)y * sh / dh);
                int srcRow = sy * sw * 4;
                int dstRow = y * dw * 4;
                for (int x = 0; x < dw; x++)
                {
                    int sx = (int)((long)x * sw / dw);
                    Buffer.BlockCopy(src, srcRow + sx * 4, dst, dstRow + x * 4, 4);
                }
            }
            return dst;
        }

        private static byte[] Box(byte[] src, int sw, int sh, int dw, int dh)
        {
            byte[] dst = new byte[dw * dh * 4];
            for (int y = 0; y < dh; y++)
            {
                int y0 = (int)((long)y * sh / dh);
                int y1 = (int)((long)(y + 1) * sh / dh);
                if (y1 <= y0) y1 = y0 + 1;

                for (int x = 0; x < dw; x++)
                {
                    int x0 = (int)((long)x * sw / dw);
                    int x1 = (int)((long)(x + 1) * sw / dw);
                    if (x1 <= x0) x1 = x0 + 1;

                    long r = 0, g = 0, b = 0, a = 0;
                    int n = 0;
                    for (int sy = y0; sy < y1; sy++)
                    {
                        int row = sy * sw * 4;
                        for (int sx = x0; sx < x1; sx++)
                        {
                            int o = row + sx * 4;
                            r += src[o]; g += src[o + 1]; b += src[o + 2]; a += src[o + 3];
                            n++;
                        }
                    }

                    int d = (y * dw + x) * 4;
                    dst[d] = (byte)(r / n);
                    dst[d + 1] = (byte)(g / n);
                    dst[d + 2] = (byte)(b / n);
                    dst[d + 3] = (byte)(a / n);
                }
            }
            return dst;
        }
    }
}
