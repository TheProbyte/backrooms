using System.IO;
using System.Text.RegularExpressions;
using Backrooms.UI;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>The interface kit (Docs/UiPlan.md U1): its fonts ship and load, and Kit.uss says what UiKit.cs says.</summary>
    public class UiKitTests
    {
        [Test]
        public void BothFacesShipAndLoad()
        {
            foreach (var name in new[] { "PixelifySans-Regular", "PixelifySans-Bold", "CourierPrime-Regular", "CourierPrime-Bold" })
                Assert.IsNotNull(Resources.Load<Font>("Fonts/" + name), name + " is not in Resources/Fonts");
            foreach (var licence in new[] { "OFL-PixelifySans", "OFL-CourierPrime" })
                Assert.IsNotNull(Resources.Load<TextAsset>("Fonts/" + licence), licence + ": the licence must ship with the font");
            Assert.AreNotEqual(UiKit.Font(UiKit.Face.Cam), UiKit.Font(UiKit.Face.Paper));
            Assert.AreNotEqual(UiKit.Font(UiKit.Face.Paper), UiKit.Font(UiKit.Face.Paper, true));
        }

        [Test]
        public void StyleSheetColoursMatchTheKit()
        {
            string uss = File.ReadAllText("Assets/Backrooms/Resources/UI/Kit.uss");
            void Same(string name, Color c)
            {
                var m = Regex.Match(uss, "--" + name + @":\s*rgb\((\d+),\s*(\d+),\s*(\d+)\)");
                Assert.IsTrue(m.Success, "--" + name + " missing from Kit.uss");
                var u = new Color32(byte.Parse(m.Groups[1].Value), byte.Parse(m.Groups[2].Value), byte.Parse(m.Groups[3].Value), 255);
                Color32 k = c;
                Assert.AreEqual((k.r, k.g, k.b), (u.r, u.g, u.b), "--" + name + " differs from UiKit");
            }
            Same("choc", UiKit.Choc); Same("choc-raised", UiKit.ChocRaised); Same("choc-hover", UiKit.ChocHover);
            Same("choc-deep", UiKit.ChocDeep); Same("ground", UiKit.Ground); Same("amber", UiKit.Amber);
            Same("orange", UiKit.Orange); Same("gold", UiKit.Gold); Same("avocado", UiKit.Avocado);
            Same("cream", UiKit.Cream); Same("tan", UiKit.Tan); Same("good", UiKit.Good); Same("warn", UiKit.Warn); Same("bad", UiKit.Bad);
        }

        [Test]
        public void NoMenuSheetKeepsItsOwnColours()
        {
            // The menus read the kit's names: a literal colour in their sheets is a colour defined twice.
            foreach (var sheet in new[] { "MainMenu", "CamcorderMenu" })
            {
                string uss = File.ReadAllText("Assets/Backrooms/Resources/UI/" + sheet + ".uss");
                var lit = Regex.Matches(uss, @"rgba?\([^)]*\)");
                Assert.AreEqual(0, lit.Count, sheet + ".uss has literal colours: " + (lit.Count > 0 ? lit[0].Value : ""));
            }
        }

        [Test]
        public void SizeScaleKeepsTheFloor()
        {
            // 12 px at 1280 x 800 (the Steam Deck) is 16.2 px of a 1080-line screen.
            Assert.GreaterOrEqual(UiKit.Small * 800f / 1080f, 12f);
            Assert.Less(UiKit.Small, UiKit.Body); Assert.Less(UiKit.Body, UiKit.Large); Assert.Less(UiKit.Large, UiKit.Title); Assert.Less(UiKit.Title, UiKit.Display);
        }
    }
}
