using System;
using System.IO;
using System.IO.Compression;

namespace XnbCore
{
    /// <summary>
    /// Minimal 8-bit RGBA PNG encoder. Deliberately dependency-free so the core library
    /// stays portable and the shell extension pulls in no imaging stack.
    /// </summary>
    public static class PngWriter
    {
        public static void Write(Stream output, int width, int height, byte[] rgba)
        {
            if (rgba == null) throw new ArgumentNullException("rgba");
            if (rgba.Length < width * height * 4) throw new ArgumentException("Pixel buffer too small.", "rgba");

            output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

            byte[] ihdr = new byte[13];
            WriteBigEndian(ihdr, 0, width);
            WriteBigEndian(ihdr, 4, height);
            ihdr[8] = 8;    // bit depth
            ihdr[9] = 6;    // colour type: truecolour + alpha
            ihdr[10] = 0;   // deflate
            ihdr[11] = 0;   // adaptive filtering
            ihdr[12] = 0;   // no interlace
            WriteChunk(output, "IHDR", ihdr);

            // Filter each scanline with Sub or Up, whichever has the lower sum of absolute
            // differences. Cheap heuristic, but it roughly halves output on typical sprites.
            int stride = width * 4;
            byte[] raw = new byte[(stride + 1) * height];
            byte[] sub = new byte[stride];
            byte[] up = new byte[stride];

            for (int y = 0; y < height; y++)
            {
                int srcRow = y * stride;
                int prevRow = srcRow - stride;
                long subCost = 0, upCost = 0;

                for (int x = 0; x < stride; x++)
                {
                    byte left = x >= 4 ? rgba[srcRow + x - 4] : (byte)0;
                    byte above = y > 0 ? rgba[prevRow + x] : (byte)0;
                    sub[x] = (byte)(rgba[srcRow + x] - left);
                    up[x] = (byte)(rgba[srcRow + x] - above);
                    subCost += sub[x] < 128 ? sub[x] : 256 - sub[x];
                    upCost += up[x] < 128 ? up[x] : 256 - up[x];
                }

                int dstRow = y * (stride + 1);
                if (subCost <= upCost)
                {
                    raw[dstRow] = 1;
                    Buffer.BlockCopy(sub, 0, raw, dstRow + 1, stride);
                }
                else
                {
                    raw[dstRow] = 2;
                    Buffer.BlockCopy(up, 0, raw, dstRow + 1, stride);
                }
            }

            byte[] compressed;
            using (MemoryStream ms = new MemoryStream())
            {
                // ZLibStream emits the zlib header and Adler-32 trailer that PNG requires.
                using (ZLibStream z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                    z.Write(raw, 0, raw.Length);
                compressed = ms.ToArray();
            }

            WriteChunk(output, "IDAT", compressed);
            WriteChunk(output, "IEND", new byte[0]);
            output.Flush();
        }

        public static void Write(string path, int width, int height, byte[] rgba)
        {
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                Write(fs, width, height, rgba);
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            byte[] len = new byte[4];
            WriteBigEndian(len, 0, data.Length);
            s.Write(len, 0, 4);

            byte[] typeBytes = new byte[4];
            for (int i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];
            s.Write(typeBytes, 0, 4);
            s.Write(data, 0, data.Length);

            uint crc = Crc32.Compute(typeBytes, data);
            byte[] crcBytes = new byte[4];
            WriteBigEndian(crcBytes, 0, (int)crc);
            s.Write(crcBytes, 0, 4);
        }

        private static void WriteBigEndian(byte[] buf, int offset, int value)
        {
            buf[offset] = (byte)(value >> 24);
            buf[offset + 1] = (byte)(value >> 16);
            buf[offset + 2] = (byte)(value >> 8);
            buf[offset + 3] = (byte)value;
        }

        private static class Crc32
        {
            private static readonly uint[] Table = Build();

            private static uint[] Build()
            {
                uint[] t = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++)
                        c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    t[n] = c;
                }
                return t;
            }

            public static uint Compute(byte[] a, byte[] b)
            {
                uint c = 0xFFFFFFFFu;
                for (int i = 0; i < a.Length; i++) c = Table[(c ^ a[i]) & 0xFF] ^ (c >> 8);
                for (int i = 0; i < b.Length; i++) c = Table[(c ^ b[i]) & 0xFF] ^ (c >> 8);
                return c ^ 0xFFFFFFFFu;
            }
        }
    }
}
