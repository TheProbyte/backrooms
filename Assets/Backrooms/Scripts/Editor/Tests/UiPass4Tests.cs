using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Backrooms.UI;
using NUnit.Framework;

namespace Backrooms.Tests
{
    /// <summary>UI pass 4 (Docs/UiPlan.md U17, U18): the words in one file, every key the code asks for in it; nothing the
    /// player reads under the size floor.</summary>
    public class UiPass4Tests
    {
        static readonly Regex KeyLiteral = new Regex("\"((?:title|line|note|hint|thing|verb|coop)\\.[a-z0-9_]+)\"");

        [Test]
        public void EveryKeyTheCodeUsesIsInTheWordsFile()
        {
            var missing = new List<string>();
            int used = 0;
            foreach (var f in Directory.GetFiles("Assets/Backrooms/Scripts/Runtime", "*.cs", SearchOption.AllDirectories))
                foreach (Match m in KeyLiteral.Matches(File.ReadAllText(f)))
                {
                    used++;
                    if (!Words.Has(m.Groups[1].Value)) missing.Add(Path.GetFileName(f) + ": " + m.Groups[1].Value);
                }
            Assert.Greater(used, 60, "the notices, notes and hints should read from the words file");
            Assert.IsEmpty(missing, string.Join(", ", missing));
        }

        [Test]
        public void TheWordsFileReadsQuotesAndCommas()
        {
            var t = new Dictionary<string, string>();
            Words.Parse("Key,Id,English(en)\n\"a.b\",,\"ONE, TWO\"\n\"c.d\",,\"SAY \"\"HI\"\"\"\n", t);
            Assert.AreEqual("ONE, TWO", t["a.b"]);
            Assert.AreEqual("SAY \"HI\"", t["c.d"]);
            Assert.AreEqual("TALLMORE STORED - +$40 - DUE BACK IN 3:00", Words.Get("title.stored", "TALLMORE", 40, "3:00"));
            Assert.AreEqual("[no.such]", Words.Get("no.such"), "a missing key shows, never blank");
        }

        [Test]
        public void NothingThePlayerReadsIsUnderTheFloor()
        {
            // 17 px of a 1080-line screen is 12.6 px at 1280 x 800 (Valve recommends 12 on the Steam Deck).
            var size = new Regex(@"font-size:\s*(\d+)px");
            foreach (Match m in size.Matches(File.ReadAllText("Assets/Backrooms/Resources/UI/MainMenu.uss")))
                Assert.GreaterOrEqual(int.Parse(m.Groups[1].Value), UiKit.Small, "MainMenu.uss: " + m.Value);
            // The player's F5 (the developer's bench is not the player's).
            foreach (var line in File.ReadAllLines("Assets/Backrooms/Resources/UI/CamcorderMenu.uss"))
                if (line.Contains(".pm-"))
                    foreach (Match m in size.Matches(line))
                        Assert.GreaterOrEqual(int.Parse(m.Groups[1].Value), UiKit.Small, "CamcorderMenu.uss: " + line.Trim());
            // The side panels' and prompts' own sizes.
            var panels = File.ReadAllText("Assets/Backrooms/Scripts/Runtime/UI/SidePanels.cs");
            foreach (Match m in new Regex(@"Txt\([^,]+,\s*[^,]+,\s*(\d+),").Matches(panels))
                Assert.GreaterOrEqual(int.Parse(m.Groups[1].Value), UiKit.Small, "SidePanels: " + m.Value);
            var prompts = File.ReadAllText("Assets/Backrooms/Scripts/Runtime/UI/Prompts.cs");
            foreach (Match m in new Regex(@"RoundToInt\((\d+)f \* s\)").Matches(prompts))
                Assert.GreaterOrEqual(int.Parse(m.Groups[1].Value), UiKit.Small, "Prompts: " + m.Value);
        }
    }
}
