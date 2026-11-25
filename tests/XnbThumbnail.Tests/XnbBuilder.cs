using System;
using System.IO;
using System.Text;
using XnbCore;

namespace XnbThumbnail.Tests
{
    /// <summary>
    /// Builds small uncompressed XNB files in memory, so that no game asset is ever needed or committed.
    ///
    /// Layout, as XNA 4.0 / MonoGame write it and <see cref="XnbImage.Load(Stream)"/> reads it:
    /// <code>
    ///   "XNB"  platform('w')  version(5)  flags(0: Reach, uncompressed)  int32 total file length
    ///   7-bit reader count, then per reader: string type name (7-bit length + UTF-8), int32 version
    ///   7-bit shared resource count (0)
    ///   7-bit type id of the root object (1-based index into the readers; 0 = null)
    ///   Texture2D: int32 SurfaceFormat, int32 width, int32 height, int32 mip count,
    ///              then per mip: int32 byte count, bytes
    /// </code>
    /// </summary>
    internal static class XnbBuilder
    {
        public const string Texture2DReader =
            "Microsoft.Xna.Framework.Content.Texture2DReader, Microsoft.Xna.Framework.Graphics, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553";

        public const string SpriteFontReader =
            "Microsoft.Xna.Framework.Content.SpriteFontReader, Microsoft.Xna.Framework.Graphics, Version=4.0.0.0, Culture=neutral, PublicKeyToken=842cf8be1de50553";

        /// <summary>A Texture2D XNB with one mip level holding <paramref name="level0"/> verbatim.</summary>
        public static byte[] Texture(SurfaceFormat format, int width, int height, byte[] level0)
        {
            return Build(Texture2DReader, w =>
            {
                w.Write((int)format);
                w.Write(width);
                w.Write(height);
                w.Write(1); // mip levels
                w.Write(level0.Length);
                w.Write(level0);
            });
        }

        /// <summary>
        /// An XNB whose root object uses <paramref name="readerTypeName"/>, followed by
        /// <paramref name="body"/>. The handler must refuse anything but a Texture2DReader before
        /// it reads the body, so the body need not be valid for that reader.
        /// </summary>
        public static byte[] Other(string readerTypeName, byte[] body)
        {
            return Build(readerTypeName, w => w.Write(body));
        }

        private static byte[] Build(string readerTypeName, Action<BinaryWriter> writeRoot)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (BinaryWriter w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
                {
                    w.Write(new[] { (byte)'X', (byte)'N', (byte)'B', (byte)'w' });
                    w.Write((byte)5);  // XNA Game Studio 4.0
                    w.Write((byte)0);  // flags: Reach profile, not compressed
                    w.Write(0);        // total length, patched below

                    w.Write7BitEncodedInt(1);
                    w.Write(readerTypeName); // BinaryWriter's 7-bit length prefix is XNB's string encoding
                    w.Write(0);              // reader version
                    w.Write7BitEncodedInt(0); // shared resources
                    w.Write7BitEncodedInt(1); // root object: reader #1

                    writeRoot(w);
                }

                byte[] bytes = ms.ToArray();
                BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 6);
                return bytes;
            }
        }
    }
}
