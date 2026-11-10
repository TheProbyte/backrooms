using System.IO;
using System.Text.RegularExpressions;
using Backrooms.Generation;
using Backrooms.Interaction;
using NUnit.Framework;

namespace Backrooms.Tests
{
    /// <summary>
    /// UI pass 3 (Docs/UiPlan.md U14): every screen in the world readable through the camcorder at the distance it is used,
    /// counted in the recorded picture's lines (Generation.PixelScreen).
    /// </summary>
    public class UiPass3Tests
    {
        static void Readable(string what, int texels, float width, int dot, float distance)
        {
            float lines = PixelScreen.Lines(dot, texels / width, distance);
            Assert.GreaterOrEqual(lines, PixelScreen.ReadableLines, $"{what}: its {dot}-dot text covers {lines:F1} lines from {distance} m");
        }

        [Test]
        public void EveryScreenInTheWorldIsReadableWhereItIsUsed()
        {
            Readable("the hub's boards", Contracts.BoardTexels, Contracts.BoardWidth, Contracts.BoardSmallestDot, Contracts.BoardReadAt);
            Readable("a seam card", SeamCard.Texels, SeamCard.Width, SeamCard.SmallestDot, SeamCard.ReadAt);
            Readable("a shelf ticket", ShelfTicket.Texels, ShelfTicket.Width, ShelfTicket.SmallestDot, ShelfTicket.ReadAt);
            Readable("a receipt", ReceiptPrinters.ReceiptTexels, ReceiptPrinters.ReceiptWidth, ReceiptPrinters.SmallestDot, ReceiptPrinters.ReceiptReadAt);
            Readable("the calendar", ReceiptPrinters.CalendarTexels, ReceiptPrinters.CalendarWidth, ReceiptPrinters.SmallestDot, ReceiptPrinters.CalendarReadAt);
        }

        [Test]
        public void TheRuleTellsReadableFromNot()
        {
            // The boards' old 1-dot lines from 3 m blurred (about 5 lines); 2 dots read (about 10).
            Assert.Less(PixelScreen.Lines(1, 480f / 2.3f, 3f), PixelScreen.ReadableLines);
            Assert.Greater(PixelScreen.Lines(2, 480f / 2.3f, 3f), PixelScreen.ReadableLines);
            Assert.AreEqual(2, PixelScreen.MinDot(480f / 2.3f, 3f));
            // Nearer reads smaller.
            Assert.Greater(PixelScreen.Lines(1, 300f, 1f), PixelScreen.Lines(1, 300f, 2f));
        }

        [Test]
        public void TheHubBoardsDrawNothingSmallerThanTwoDots()
        {
            // A Draw call whose dot argument is 1 (followed by the colour).
            var oneDot = new Regex(@"PixelFont\.Draw\w*\([^;]*?,\s*1\s*,\s*[A-Za-z(]");
            foreach (var f in new[] { "Interaction/Storage/Supplies.cs", "Interaction/Improvements.cs" })
            {
                var src = File.ReadAllText("Assets/Backrooms/Scripts/Runtime/" + f);
                int a = src.IndexOf("void Draw()");
                Assert.Greater(a, 0, f);
                var m = oneDot.Match(src, a);
                Assert.IsFalse(m.Success, f + ": " + (m.Success ? m.Value : ""));
            }
            var c = File.ReadAllText("Assets/Backrooms/Scripts/Runtime/Interaction/Contracts.cs");
            int from = c.IndexOf("void DrawBoardClock()"), to = c.IndexOf("public void Changed()");
            Assert.Greater(to, from);
            var hit = oneDot.Match(c.Substring(from, to - from));
            Assert.IsFalse(hit.Success, "the work board: " + (hit.Success ? hit.Value : ""));
        }
    }
}
