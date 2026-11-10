using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// 5 x 7 block capitals, digits and a little punctuation, drawn into a pixel buffer (top row first): screens painted
    /// while the game runs (the contracts board, the pay window's display). The letters are FittingMaterials' and the
    /// Blender scripts' (bake_kit.py GLYPHS).
    /// </summary>
    public static class PixelFont
    {
        static readonly string[] Glyphs =
        {
            "A01110100011000111111100011000110001", "B11110100011000111110100011000111110", "C01110100011000010000100001000101110",
            "D11110100011000110001100011000111110", "E11111100001000011110100001000011111", "F11111100001000011110100001000010000",
            "G01110100011000010111100011000101111", "H10001100011000111111100011000110001", "I01110001000010000100001000010001110",
            "J00111000100001000010000101001001100", "K10001100101010011000101001001010001", "L10000100001000010000100001000011111",
            "M10001110111010110101100011000110001", "N10001100011100110101100111000110001", "O01110100011000110001100011000101110",
            "P11110100011000111110100001000010000", "Q01110100011000110001101011001001101", "R11110100011000111110101001001010001",
            "S01111100001000001110000010000111110", "T11111001000010000100001000010000100", "U10001100011000110001100011000101110",
            "V10001100011000110001100010101000100", "W10001100011000110101101011010101010", "X10001100010101000100010101000110001",
            "Y10001100010101000100001000010000100", "Z11111000010001000100010001000011111",
            "001110100011001110101110011000101110", "100100011000010000100001000010001110", "201110100010000100010001000100011111",
            "311111000100010000010000011000101110", "400010001100101010010111110001000010", "511111100001111000001000011000101110",
            "600110010001000011110100011000101110", "711111000010001000100010000100001000", "801110100011000101110100011000101110",
            "901110100011000101111000010001001100",
            "-00000000000000011111000000000000000", ":00000011000110000000011000110000000", ".00000000000000000000000000110001100",
            "/00001000010001000100010001000010000", "$00100011111010001110001011111000100", ">10000010000010000010001000100010000",
            "(00010001000100001000010000010000010", ")01000001000001000010000100010001000", "#01010010101111101010111110101001010",
            "%11001110010001000100010001001110011", "!00100001000010000100001000000000100", "'00100001000100000000000000000000000",
            ",00000000000000000000000000110000100", "?01110100010000100010001000000000100", "+00000001000010011111001000010000000",
        };

        static bool Dot(char c, int col, int row)
        {
            foreach (var g in Glyphs)
                if (g[0] == c) return g[1 + row * 5 + col] == '1';
            return false;
        }

        /// <summary>Width in pixels of a line at this dot size.</summary>
        public static int Width(string s, int dot) => s.Length == 0 ? 0 : s.Length * 6 * dot - dot;

        /// <summary>Draws a line, its top left at (x, y) (y down from the top), dot pixels a dot.</summary>
        public static void Draw(Color32[] px, int w, int h, string s, int x, int y, int dot, Color32 c)
        {
            int cx = x;
            foreach (char ch0 in s)
            {
                char ch = char.ToUpperInvariant(ch0);
                if (ch != ' ')
                    for (int row = 0; row < 7; row++)
                    for (int col = 0; col < 5; col++)
                    {
                        if (!Dot(ch, col, row)) continue;
                        for (int dy = 0; dy < dot; dy++)
                        for (int dx = 0; dx < dot; dx++)
                        {
                            int px0 = cx + col * dot + dx, py0 = y + row * dot + dy;
                            if (px0 < 0 || py0 < 0 || px0 >= w || py0 >= h) continue;
                            px[(h - 1 - py0) * w + px0] = c;            // (textures go bottom row first)
                        }
                    }
                cx += 6 * dot;
            }
        }

        public static void DrawCentred(Color32[] px, int w, int h, string s, int cx, int y, int dot, Color32 c) =>
            Draw(px, w, h, s, cx - Width(s, dot) / 2, y, dot, c);

        public static void Fill(Color32[] px, int w, int h, int x0, int y0, int x1, int y1, Color32 c)
        {
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y1); y++)
            for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x1); x++)
                px[(h - 1 - y) * w + x] = c;
        }
    }
}
