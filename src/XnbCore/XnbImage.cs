using System;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework.Content;
using MonoGame.Framework.Utilities;

namespace XnbCore
{
    /// <summary>Thrown when a file is not an XNB, or is an XNB that holds something other than a Texture2D.</summary>
    public sealed class XnbFormatException : Exception
    {
        public XnbFormatException(string message) : base(message) { }
    }

    /// <summary>A decoded XNB texture: 8-bit RGBA, top-down, tightly packed.</summary>
    public sealed class XnbImage
    {
        public int Width { get; }
        public int Height { get; }
        public int MipLevels { get; }
        public SurfaceFormat Format { get; }
        public char Platform { get; }
        public int Version { get; }
        public bool HiDef { get; }
        public string Compression { get; }

        /// <summary>RGBA8888, premultiplied alpha (as stored by the content pipeline). Length == Width*Height*4.</summary>
        public byte[] PremultipliedRgba { get; }

        internal XnbImage(int w, int h, int mips, SurfaceFormat fmt, char platform, int version,
                          bool hiDef, string compression, byte[] rgba)
        {
            Width = w; Height = h; MipLevels = mips; Format = fmt;
            Platform = platform; Version = version; HiDef = hiDef;
            Compression = compression; PremultipliedRgba = rgba;
        }

        /// <summary>
        /// Straight (non-premultiplied) RGBA, which is what you want when writing a PNG that should
        /// look like the artist's original source file. The content pipeline premultiplies by default.
        /// </summary>
        public byte[] ToStraightRgba()
        {
            byte[] src = PremultipliedRgba;
            byte[] dst = new byte[src.Length];
            for (int i = 0; i < src.Length; i += 4)
            {
                int a = src[i + 3];
                dst[i + 3] = (byte)a;
                if (a == 0)
                {
                    dst[i] = dst[i + 1] = dst[i + 2] = 0;
                }
                else if (a == 255)
                {
                    dst[i] = src[i]; dst[i + 1] = src[i + 1]; dst[i + 2] = src[i + 2];
                }
                else
                {
                    dst[i] = Unpremul(src[i], a);
                    dst[i + 1] = Unpremul(src[i + 1], a);
                    dst[i + 2] = Unpremul(src[i + 2], a);
                }
            }
            return dst;
        }

        private static byte Unpremul(byte c, int a)
        {
            int v = (c * 255 + a / 2) / a;
            return (byte)(v > 255 ? 255 : v);
        }

        public static XnbImage Load(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                return Load(fs);
        }

        /// <summary>Parses an XNB stream and decodes its root Texture2D, mip level 0.</summary>
        public static XnbImage Load(Stream stream)
        {
            byte[] head = new byte[10];
            ReadExactly(stream, head, 0, 10);

            if (head[0] != 'X' || head[1] != 'N' || head[2] != 'B')
                throw new XnbFormatException("Not an XNB file (bad magic).");

            char platform = (char)head[3];
            int version = head[4];
            byte flags = head[5];
            int fileLength = BitConverter.ToInt32(head, 6);

            if (version != 4 && version != 5)
                throw new XnbFormatException("Unsupported XNB format version " + version + ".");

            bool hiDef = (flags & 0x01) != 0;
            bool lzx = (flags & 0x80) != 0;
            bool lz4 = (flags & 0x40) != 0;

            Stream body;
            string compression;
            if (lzx || lz4)
            {
                byte[] szBuf = new byte[4];
                ReadExactly(stream, szBuf, 0, 4);
                int decompressedSize = BitConverter.ToInt32(szBuf, 0);

                if (lzx)
                {
                    // fileLength counts the 10-byte header plus the 4-byte decompressed size.
                    body = new LzxDecoderStream(stream, decompressedSize, fileLength - 14);
                    compression = "LZX";
                }
                else
                {
                    body = new Lz4DecoderStream(stream);
                    compression = "LZ4";
                }
            }
            else
            {
                body = stream;
                compression = "none";
            }

            using (BinaryReader r = new BinaryReader(body, Encoding.UTF8, leaveOpen: true))
            {
                // Type reader manifest. XNB uses the same 7-bit-length string encoding as BinaryWriter,
                // so BinaryReader's own primitives line up exactly.
                int readerCount = Read7Bit(r);
                if (readerCount < 0 || readerCount > 1024)
                    throw new XnbFormatException("Implausible type reader count (" + readerCount + ").");

                string[] readers = new string[readerCount];
                for (int i = 0; i < readerCount; i++)
                {
                    readers[i] = r.ReadString();
                    r.ReadInt32(); // reader version
                }

                Read7Bit(r); // shared resource count

                int typeId = Read7Bit(r);
                if (typeId == 0)
                    throw new XnbFormatException("XNB root object is null.");
                if (typeId > readerCount)
                    throw new XnbFormatException("XNB root type id out of range.");

                string readerName = SimpleName(readers[typeId - 1]);
                if (readerName != "Texture2DReader")
                    throw new XnbFormatException("XNB contains a " + readerName + ", not a Texture2D.");

                SurfaceFormat format = (SurfaceFormat)r.ReadInt32();
                int width = r.ReadInt32();
                int height = r.ReadInt32();
                int levels = r.ReadInt32();

                if (width <= 0 || height <= 0 || (long)width * height > 268435456L)
                    throw new XnbFormatException("Implausible texture dimensions " + width + "x" + height + ".");
                if (levels < 1)
                    throw new XnbFormatException("Texture has no mip levels.");

                int level0Size = r.ReadInt32();
                if (level0Size <= 0 || level0Size > 512 * 1024 * 1024)
                    throw new XnbFormatException("Implausible mip level size.");

                byte[] level0 = r.ReadBytes(level0Size);
                if (level0.Length != level0Size)
                    throw new XnbFormatException("Truncated texture data.");

                byte[] rgba = SurfaceDecoder.ToRgba(level0, width, height, format);
                return new XnbImage(width, height, levels, format, platform, version, hiDef, compression, rgba);
            }
        }

        /// <summary>"Microsoft.Xna...Texture2DReader, Assembly, Version=..." -> "Texture2DReader"</summary>
        private static string SimpleName(string readerTypeName)
        {
            if (string.IsNullOrEmpty(readerTypeName)) return "<unknown>";
            int comma = readerTypeName.IndexOf(',');
            string t = comma >= 0 ? readerTypeName.Substring(0, comma) : readerTypeName;
            int bracket = t.IndexOf('[');
            if (bracket >= 0) t = t.Substring(0, bracket);
            int dot = t.LastIndexOf('.');
            return dot >= 0 ? t.Substring(dot + 1) : t;
        }

        private static int Read7Bit(BinaryReader r)
        {
            int result = 0, shift = 0;
            while (shift < 35)
            {
                byte b = r.ReadByte();
                result |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
            }
            throw new XnbFormatException("Malformed 7-bit encoded integer.");
        }

        private static void ReadExactly(Stream s, byte[] buf, int offset, int count)
        {
            int done = 0;
            while (done < count)
            {
                int n = s.Read(buf, offset + done, count - done);
                if (n <= 0) throw new XnbFormatException("File is too short to be an XNB.");
                done += n;
            }
        }
    }
}
