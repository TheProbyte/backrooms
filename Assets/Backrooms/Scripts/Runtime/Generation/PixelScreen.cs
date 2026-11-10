using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The screens in the world, one kit (Docs/UiPlan.md U14, 2026-11-10). The boards, cards, tickets, receipts and the
    /// calendar each laid out their own pixels; these are the shared parts (the company's stripe, a header band, a client's
    /// pictogram, a bar, text cut to fit) and the one rule they are checked against:
    /// <para><b>Readable through the camcorder at the distance it is used.</b> The picture is recorded at
    /// <see cref="RecordedLines"/> lines over a <see cref="FieldOfView"/>° vertical view, so what counts is how many of
    /// those lines a letter covers, not the screen's own pixels. The 5 x 7 font needs a line a row and a little over:
    /// <see cref="ReadableLines"/>. (Xbox's guideline asks 26 px of body height on a 1080p screen for flat UI; there is no
    /// agreed rule for text in the world, so this is measured in the picture itself.)</para>
    /// </summary>
    public static class PixelScreen
    {
        /// <summary>The camcorder's recorded picture (the NTSC preset's 480 lines) and its vertical view (CamcorderSettings).</summary>
        public const float RecordedLines = 480f, FieldOfView = 54.5f;
        /// <summary>A letter covering fewer lines than this blurs into the next.</summary>
        public const float ReadableLines = 7.5f;

        /// <summary>How many recorded lines a letter of the 5 x 7 font at <paramref name="dot"/> covers, on a screen of
        /// <paramref name="texelsPerMetre"/>, seen square-on from <paramref name="distance"/> metres.</summary>
        public static float Lines(int dot, float texelsPerMetre, float distance)
        {
            float letter = 7f * dot / Mathf.Max(1f, texelsPerMetre);
            float seen = 2f * Mathf.Max(0.05f, distance) * Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            return letter / seen * RecordedLines;
        }

        /// <summary>The smallest dot size readable on such a screen at that distance.</summary>
        public static int MinDot(float texelsPerMetre, float distance)
        {
            for (int d = 1; d < 6; d++) if (Lines(d, texelsPerMetre, distance) >= ReadableLines) return d;
            return 6;
        }

        /// <summary>The company's stripe (orange, gold, avocado), three rows of <paramref name="each"/> pixels from y.</summary>
        public static void Stripe(Color32[] px, int w, int h, int y, int each = 2)
        {
            PixelFont.Fill(px, w, h, 0, y, w, y + each, new Color32(217, 102, 31, 255));
            PixelFont.Fill(px, w, h, 0, y + each, w, y + each * 2, new Color32(232, 178, 58, 255));
            PixelFont.Fill(px, w, h, 0, y + each * 2, w, y + each * 3, new Color32(124, 138, 46, 255));
        }

        /// <summary>A band across the top with a title in it.</summary>
        public static void Header(Color32[] px, int w, int h, int height, Color32 band, string title, int dot, Color32 ink, bool centred = true)
        {
            PixelFont.Fill(px, w, h, 0, 0, w, height, band);
            int y = (height - 7 * dot) / 2;
            if (centred) PixelFont.DrawCentred(px, w, h, title, w / 2, y, dot, ink);
            else PixelFont.Draw(px, w, h, title, 10, y, dot, ink);
        }

        /// <summary>The text cut (with nothing added) to fit a width at a dot size.</summary>
        public static string Fit(string text, int dot, int width)
        {
            if (string.IsNullOrEmpty(text)) return "";
            while (text.Length > 1 && PixelFont.Width(text, dot) > width) text = text.Substring(0, text.Length - 1);
            return text;
        }

        /// <summary>The text in at most two lines of a width, broken at a space (the second cut to fit).</summary>
        public static (string, string) TwoLines(string text, int dot, int width)
        {
            if (string.IsNullOrEmpty(text)) return ("", "");
            if (PixelFont.Width(text, dot) <= width) return (text, "");
            int cut = text.Length;
            while (cut > 0)
            {
                cut = text.LastIndexOf(' ', cut - 1);
                if (cut <= 0) break;
                if (PixelFont.Width(text.Substring(0, cut), dot) <= width) return (text.Substring(0, cut), Fit(text.Substring(cut + 1), dot, width));
            }
            return (Fit(text, dot, width), "");
        }

        /// <summary>A bar: a track and a fill (0..1) from the left.</summary>
        public static void Bar(Color32[] px, int w, int h, int x, int y, int width, int height, float fill, Color32 ink, Color32 track)
        {
            PixelFont.Fill(px, w, h, x, y, x + width, y + height, track);
            int f = Mathf.RoundToInt(Mathf.Clamp01(fill) * width);
            if (f > 0) PixelFont.Fill(px, w, h, x, y, x + f, y + height, ink);
        }

        /// <summary>A client's pictogram (the marks on their boxes: CardboardBoxGeometry.MarkShapes), size pixels square, its
        /// top-left at (x, y) in the screen's rows-down coordinates.</summary>
        public static void Mark(Color32[] px, int w, int h, int client, int x, int y, int size, Color32 ink)
        {
            var shapes = CardboardBoxGeometry.MarkShapes[Mathf.Clamp(client, 0, CardboardBoxGeometry.MarkShapes.Length - 1)];
            for (int j = 0; j < size; j++)
                for (int i = 0; i < size; i++)
                {
                    // The pixel's centre in -1..1, y up (the screens are drawn rows down).
                    float u = (i + 0.5f) / size * 2f - 1f, v = 1f - (j + 0.5f) / size * 2f;
                    bool hit = false;
                    foreach (var q in shapes) if (InQuad(u, v, q)) { hit = true; break; }
                    if (!hit) continue;
                    int px0 = x + i, py0 = y + j;
                    if (px0 < 0 || py0 < 0 || px0 >= w || py0 >= h) continue;
                    // (The screens' arrays run bottom row first, as PixelFont's Fill and Draw do.)
                    px[(h - 1 - py0) * w + px0] = ink;
                }
        }

        static bool InQuad(float x, float y, float[] q)
        {
            int sign = 0;
            for (int i = 0; i < 4; i++)
            {
                float ax = q[i * 2], ay = q[i * 2 + 1], bx = q[(i + 1) % 4 * 2], by = q[(i + 1) % 4 * 2 + 1];
                float c = (bx - ax) * (y - ay) - (by - ay) * (x - ax);
                int s = c > 0 ? 1 : c < 0 ? -1 : 0;
                if (s == 0) continue;
                if (sign == 0) sign = s; else if (s != sign) return false;
            }
            return true;
        }
    }
}
