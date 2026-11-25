using System.Text;
using XnbCore;

namespace XnbThumbnail.Tests
{
    /// <summary>The synthetic textures the tests encode, and what the handler must return for them.</summary>
    internal static class Samples
    {
        /// <summary>
        /// A 4x4 <c>Color</c> (RGBA8) texture of sixteen distinct pixels, with opaque, translucent
        /// and fully transparent ones. Its alpha is premultiplied, as the content pipeline stores
        /// it, and Explorer composites premultiplied alpha, so the handler passes it through as is.
        /// </summary>
        public static readonly byte[] Color4x4Rgba = BuildColor4x4();

        public static byte[] Color4x4Xnb()
        {
            return XnbBuilder.Texture(SurfaceFormat.Color, 4, 4, Color4x4Rgba);
        }

        private static byte[] BuildColor4x4()
        {
            byte[] rgba = new byte[4 * 4 * 4];
            for (int i = 0; i < 16; i++)
            {
                // Alpha steps down the rows: 255, 170, 85, 0. Channels stay <= alpha, as a
                // premultiplied pixel's must.
                byte a = (byte)(255 - (i / 4) * 85);
                rgba[i * 4] = (byte)(a * (i % 4) / 3);
                rgba[i * 4 + 1] = (byte)(a * (3 - i % 4) / 3);
                rgba[i * 4 + 2] = (byte)(a / 2);
                rgba[i * 4 + 3] = a;
            }
            return rgba;
        }

        /// <summary>
        /// One BC1 (DXT1) block: c0 = pure red (0xF800), c1 = pure blue (0x001F). c0 &gt; c1, so the
        /// block uses four-colour mode, whose two interpolated colours are (2*c0 + c1)/3 and
        /// (c0 + 2*c1)/3. Every row's indices are 0, 1, 2, 3 (0b11_10_01_00 = 0xE4).
        /// </summary>
        public static byte[] Bc1Xnb()
        {
            byte[] block = { 0x00, 0xF8, 0x1F, 0x00, 0xE4, 0xE4, 0xE4, 0xE4 };
            return XnbBuilder.Texture(SurfaceFormat.Dxt1, 4, 4, block);
        }

        /// <summary>The colours of each row of <see cref="Bc1Xnb"/>, as (r, g, b), opaque.</summary>
        public static readonly (byte R, byte G, byte B)[] Bc1Row =
        {
            (255, 0, 0),   // c0
            (0, 0, 255),   // c1
            (170, 0, 85),  // (2*255 + 0)/3, 0, (2*0 + 255)/3
            (85, 0, 170),  // (255 + 2*0)/3, 0, (0 + 2*255)/3
        };

        /// <summary>A solid, opaque <c>Color</c> texture.</summary>
        public static byte[] SolidXnb(int width, int height, byte r, byte g, byte b)
        {
            byte[] rgba = new byte[width * height * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b; rgba[i + 3] = 255;
            }
            return XnbBuilder.Texture(SurfaceFormat.Color, width, height, rgba);
        }

        /// <summary>Opaque quadrant colours: top left, top right, bottom left, bottom right.</summary>
        public static readonly (byte R, byte G, byte B)[] QuadrantColors =
        {
            (255, 0, 0), (0, 255, 0), (255, 255, 255), (0, 0, 255),
        };

        /// <summary>
        /// A square <c>Color</c> texture of four opaque quadrants (<see cref="QuadrantColors"/>).
        /// Quadrant centres survive any reasonable resampling, so the Shell's own scaling of the
        /// thumbnail can't make a check of them flaky.
        /// </summary>
        public static byte[] QuadrantsXnb(int edge)
        {
            byte[] rgba = new byte[edge * edge * 4];
            int half = edge / 2;
            for (int y = 0; y < edge; y++)
            {
                for (int x = 0; x < edge; x++)
                {
                    (byte R, byte G, byte B) c = QuadrantColors[Quadrant(x < half, y < half)];
                    int o = (y * edge + x) * 4;
                    rgba[o] = c.R; rgba[o + 1] = c.G; rgba[o + 2] = c.B; rgba[o + 3] = 255;
                }
            }
            return XnbBuilder.Texture(SurfaceFormat.Color, edge, edge, rgba);
        }

        /// <summary>Index into <see cref="QuadrantColors"/> for a quadrant.</summary>
        public static int Quadrant(bool left, bool top)
        {
            if (top) return left ? 0 : 1;
            return left ? 2 : 3;
        }

        /// <summary>Bytes that are not an XNB at all.</summary>
        public static byte[] NotXnb()
        {
            return Encoding.ASCII.GetBytes("This is a text file with an .xnb name, not an XNB.");
        }

        /// <summary>A well-formed XNB header whose root object is a SpriteFont.</summary>
        public static byte[] SpriteFontXnb()
        {
            return XnbBuilder.Other(XnbBuilder.SpriteFontReader, new byte[64]);
        }
    }
}
