using Backrooms.Camcorder;
using Backrooms.UI;
using NUnit.Framework;

namespace Backrooms.Tests
{
    /// <summary>UI pass 1 (Docs/UiPlan.md U5, U6): hints read as prompts, and notices queue instead of replacing each other.</summary>
    public class UiPass1Tests
    {
        static string Show(string line)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in Prompts.Parse(line))
                sb.Append(p.plain ? "(" : "[").Append(p.mouse >= 0 ? "M" + p.mouse : p.key).Append(p.tag != null ? " " + p.tag : "").Append("] ").Append(p.verb).Append(" | ");
            return sb.ToString();
        }

        [Test]
        public void EveryHintFormReadsAsKeyAndVerb()
        {
            Assert.AreEqual("[M1] PICK UP CHALK | ", Show("CLICK TO PICK UP CHALK"));
            Assert.AreEqual("[M1 HOLD] PUSH COUCH | [E] SIT | ", Show("HOLD TO PUSH COUCH · E SIT"));
            Assert.AreEqual("[G HOLD] PACK IT BACK INTO ITS CRATE | ", Show("HOLD G TO PACK IT BACK INTO ITS CRATE"));
            Assert.AreEqual("[Q] DROP | [E] SET DOWN | [1-4] POCKET | ", Show("Q DROP · E SET DOWN · 1-4 POCKET"));
            Assert.AreEqual("[M2] PUT UP THE SHELF HERE | [R] TURNS IT | ", Show("RIGHT CLICK: PUT UP THE SHELF HERE · R TURNS IT"));
            Assert.AreEqual("[M0] UP TIPS IT UP | [M0] DOWN LOWERS IT | [M1 LET GO] DROP | ", Show("MOUSE UP TIPS IT UP · DOWN LOWERS IT · LET GO TO DROP"));
            Assert.AreEqual("[E] STAND UP | [W/A/S/D] ROLL | ", Show("E STAND UP · W/A/S/D ROLL"));
            Assert.AreEqual("[M1] PUT DOWN | [M1 HOLD] THROW | [M1 HOLD] ON A BOX TAKE IT TOO | [R] TURN | ",
                Show("CLICK PUT DOWN · HOLD THROW · HOLD ON A BOX TAKE IT TOO · R TURN"));
            // A note has no key; "NO ROOM" is not the key N.
            Assert.AreEqual("(] NO ROOM HERE | [R] TURNS IT | ", Show("NO ROOM HERE · R TURNS IT"));
            Assert.AreEqual("WASD", Prompts.KeyLabel("W/A/S/D"));
        }

        [Test]
        public void TitlesQueueThreeAndDropTheOldest()
        {
            CamcorderOSD.ClearNotices();
            CamcorderOSD.Title("ONE"); CamcorderOSD.Title("TWO"); CamcorderOSD.Title("THREE"); CamcorderOSD.Title("FOUR");
            Assert.AreEqual(3, CamcorderOSD.TitlesWaiting);
            CamcorderOSD.Title("FOUR");          // the same again is not queued twice
            Assert.AreEqual(3, CamcorderOSD.TitlesWaiting);
            CamcorderOSD.ClearNotices();
        }

        [Test]
        public void LinesStackAndTheSameLineRefreshes()
        {
            CamcorderOSD.ClearNotices();
            CamcorderOSD.Notice("ORDERED - SHELVING KIT");
            CamcorderOSD.Notice("2 MORE");
            CamcorderOSD.Notice("2 MORE");
            Assert.AreEqual(2, CamcorderOSD.LinesShowing);
            CamcorderOSD.Notice("PLUGGED IN"); CamcorderOSD.Notice("UNPLUGGED");
            Assert.AreEqual(3, CamcorderOSD.LinesShowing);
            CamcorderOSD.ClearNotices();
        }
    }
}
