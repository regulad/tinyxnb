using System;

namespace XnbCore
{
    /// <summary>Values match Microsoft.Xna.Framework.Graphics.SurfaceFormat.</summary>
    public enum SurfaceFormat
    {
        Color = 0,
        Bgr565 = 1,
        Bgra5551 = 2,
        Bgra4444 = 3,
        Dxt1 = 4,
        Dxt3 = 5,
        Dxt5 = 6,
        NormalizedByte2 = 7,
        NormalizedByte4 = 8,
        Rgba1010102 = 9,
        Rg32 = 10,
        Rgba64 = 11,
        Alpha8 = 12,
        Single = 13,
        Vector2 = 14,
        Vector4 = 15,
        HalfSingle = 16,
        HalfVector2 = 17,
        HalfVector4 = 18,
        HdrBlendable = 19,
        Bgr32 = 20,
        Bgra32 = 21,
        ColorSRgb = 30,
        Bgr32SRgb = 31,
        Bgra32SRgb = 32,
        Dxt1SRgb = 33,
        Dxt3SRgb = 34,
        Dxt5SRgb = 35,
        Dxt1a = 70,
    }

    /// <summary>Decodes one mip level of a Texture2D surface into top-down RGBA8888.</summary>
    internal static class SurfaceDecoder
    {
        public static byte[] ToRgba(byte[] src, int width, int height, SurfaceFormat format)
        {
            byte[] dst = new byte[width * height * 4];

            switch (format)
            {
                case SurfaceFormat.Color:
                case SurfaceFormat.ColorSRgb:
                    Expect(src, width * height * 4, format);
                    Buffer.BlockCopy(src, 0, dst, 0, dst.Length); // already RGBA
                    return dst;

                case SurfaceFormat.Bgra32:
                case SurfaceFormat.Bgra32SRgb:
                    Expect(src, width * height * 4, format);
                    for (int i = 0; i < dst.Length; i += 4)
                    {
                        dst[i] = src[i + 2]; dst[i + 1] = src[i + 1];
                        dst[i + 2] = src[i]; dst[i + 3] = src[i + 3];
                    }
                    return dst;

                case SurfaceFormat.Bgr32:
                case SurfaceFormat.Bgr32SRgb:
                    Expect(src, width * height * 4, format);
                    for (int i = 0; i < dst.Length; i += 4)
                    {
                        dst[i] = src[i + 2]; dst[i + 1] = src[i + 1];
                        dst[i + 2] = src[i]; dst[i + 3] = 255;
                    }
                    return dst;

                case SurfaceFormat.Bgr565:
                    Expect(src, width * height * 2, format);
                    for (int p = 0, o = 0; o < dst.Length; p += 2, o += 4)
                    {
                        int v = src[p] | (src[p + 1] << 8);
                        dst[o] = Expand5((v >> 11) & 0x1F);
                        dst[o + 1] = Expand6((v >> 5) & 0x3F);
                        dst[o + 2] = Expand5(v & 0x1F);
                        dst[o + 3] = 255;
                    }
                    return dst;

                case SurfaceFormat.Bgra5551:
                    Expect(src, width * height * 2, format);
                    for (int p = 0, o = 0; o < dst.Length; p += 2, o += 4)
                    {
                        int v = src[p] | (src[p + 1] << 8);
                        dst[o] = Expand5((v >> 10) & 0x1F);
                        dst[o + 1] = Expand5((v >> 5) & 0x1F);
                        dst[o + 2] = Expand5(v & 0x1F);
                        dst[o + 3] = (byte)(((v >> 15) & 1) != 0 ? 255 : 0);
                    }
                    return dst;

                case SurfaceFormat.Bgra4444:
                    Expect(src, width * height * 2, format);
                    for (int p = 0, o = 0; o < dst.Length; p += 2, o += 4)
                    {
                        int v = src[p] | (src[p + 1] << 8);
                        dst[o] = Expand4((v >> 8) & 0xF);
                        dst[o + 1] = Expand4((v >> 4) & 0xF);
                        dst[o + 2] = Expand4(v & 0xF);
                        dst[o + 3] = Expand4((v >> 12) & 0xF);
                    }
                    return dst;

                case SurfaceFormat.Alpha8:
                    Expect(src, width * height, format);
                    for (int p = 0, o = 0; o < dst.Length; p++, o += 4)
                    {
                        dst[o] = dst[o + 1] = dst[o + 2] = 0;
                        dst[o + 3] = src[p];
                    }
                    return dst;

                case SurfaceFormat.Dxt1:
                case SurfaceFormat.Dxt1SRgb:
                    DecodeBlocks(src, dst, width, height, 8, BlockKind.Bc1Opaque);
                    return dst;

                case SurfaceFormat.Dxt1a:
                    DecodeBlocks(src, dst, width, height, 8, BlockKind.Bc1Punchthrough);
                    return dst;

                case SurfaceFormat.Dxt3:
                case SurfaceFormat.Dxt3SRgb:
                    DecodeBlocks(src, dst, width, height, 16, BlockKind.Bc2);
                    return dst;

                case SurfaceFormat.Dxt5:
                case SurfaceFormat.Dxt5SRgb:
                    DecodeBlocks(src, dst, width, height, 16, BlockKind.Bc3);
                    return dst;

                default:
                    throw new XnbFormatException("Unsupported surface format: " + format + ".");
            }
        }

        private enum BlockKind { Bc1Opaque, Bc1Punchthrough, Bc2, Bc3 }

        private static void DecodeBlocks(byte[] src, byte[] dst, int width, int height, int blockBytes, BlockKind kind)
        {
            int bw = (width + 3) / 4;
            int bh = (height + 3) / 4;
            int need = bw * bh * blockBytes;
            if (src.Length < need)
                throw new XnbFormatException("Truncated block-compressed surface (" + src.Length + " < " + need + ").");

            byte[] r = new byte[4], g = new byte[4], b = new byte[4];
            byte[] alpha = new byte[16];
            byte[] aLut = new byte[8];

            for (int by = 0; by < bh; by++)
            {
                for (int bx = 0; bx < bw; bx++)
                {
                    int p = (by * bw + bx) * blockBytes;
                    int colorOffset = p;

                    // --- alpha ---
                    if (kind == BlockKind.Bc2)
                    {
                        colorOffset = p + 8;
                        for (int i = 0; i < 8; i++)
                        {
                            byte v = src[p + i];
                            alpha[i * 2] = Expand4(v & 0xF);
                            alpha[i * 2 + 1] = Expand4(v >> 4);
                        }
                    }
                    else if (kind == BlockKind.Bc3)
                    {
                        colorOffset = p + 8;
                        byte a0 = src[p], a1 = src[p + 1];
                        aLut[0] = a0; aLut[1] = a1;
                        if (a0 > a1)
                        {
                            for (int i = 1; i < 7; i++)
                                aLut[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
                        }
                        else
                        {
                            for (int i = 1; i < 5; i++)
                                aLut[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
                            aLut[6] = 0; aLut[7] = 255;
                        }
                        ulong bits = 0;
                        for (int i = 0; i < 6; i++) bits |= (ulong)src[p + 2 + i] << (8 * i);
                        for (int i = 0; i < 16; i++) alpha[i] = aLut[(int)((bits >> (3 * i)) & 7)];
                    }
                    else
                    {
                        for (int i = 0; i < 16; i++) alpha[i] = 255;
                    }

                    // --- color ---
                    int c0 = src[colorOffset] | (src[colorOffset + 1] << 8);
                    int c1 = src[colorOffset + 2] | (src[colorOffset + 3] << 8);
                    Unpack565(c0, out r[0], out g[0], out b[0]);
                    Unpack565(c1, out r[1], out g[1], out b[1]);

                    // BC2/BC3 always use the four-colour interpolation; only BC1 switches on c0 > c1.
                    bool fourColor = kind == BlockKind.Bc2 || kind == BlockKind.Bc3 || c0 > c1;
                    bool punchthrough = false;

                    if (fourColor)
                    {
                        r[2] = (byte)((2 * r[0] + r[1]) / 3); g[2] = (byte)((2 * g[0] + g[1]) / 3); b[2] = (byte)((2 * b[0] + b[1]) / 3);
                        r[3] = (byte)((r[0] + 2 * r[1]) / 3); g[3] = (byte)((g[0] + 2 * g[1]) / 3); b[3] = (byte)((b[0] + 2 * b[1]) / 3);
                    }
                    else
                    {
                        r[2] = (byte)((r[0] + r[1]) / 2); g[2] = (byte)((g[0] + g[1]) / 2); b[2] = (byte)((b[0] + b[1]) / 2);
                        r[3] = g[3] = b[3] = 0;
                        punchthrough = kind == BlockKind.Bc1Punchthrough;
                    }

                    uint idx = (uint)(src[colorOffset + 4] | (src[colorOffset + 5] << 8) |
                                      (src[colorOffset + 6] << 16) | (src[colorOffset + 7] << 24));

                    for (int py = 0; py < 4; py++)
                    {
                        int y = by * 4 + py;
                        if (y >= height) break;
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px;
                            if (x >= width) continue;

                            int i = py * 4 + px;
                            int sel = (int)((idx >> (2 * i)) & 3);
                            int o = (y * width + x) * 4;

                            byte a = alpha[i];
                            if (punchthrough && sel == 3) a = 0;

                            dst[o] = r[sel];
                            dst[o + 1] = g[sel];
                            dst[o + 2] = b[sel];
                            dst[o + 3] = a;
                        }
                    }
                }
            }
        }

        private static void Unpack565(int v, out byte r, out byte g, out byte b)
        {
            r = Expand5((v >> 11) & 0x1F);
            g = Expand6((v >> 5) & 0x3F);
            b = Expand5(v & 0x1F);
        }

        private static byte Expand4(int v) { return (byte)(v * 17); }                    // 0..15  -> 0..255
        private static byte Expand5(int v) { return (byte)((v << 3) | (v >> 2)); }       // 0..31  -> 0..255
        private static byte Expand6(int v) { return (byte)((v << 2) | (v >> 4)); }       // 0..63  -> 0..255

        private static void Expect(byte[] src, int bytes, SurfaceFormat format)
        {
            if (src.Length < bytes)
                throw new XnbFormatException("Truncated " + format + " surface (" + src.Length + " < " + bytes + ").");
        }
    }
}
