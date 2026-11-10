using Backrooms.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backrooms.Tests
{
    /// <summary>UI pass 2 (Docs/UiPlan.md U10): TEXT SIZE scales the menus through a run-time copy of the panel settings,
    /// never the asset itself.</summary>
    public class UiPass2Tests
    {
        [Test]
        public void TextSizeScalesTheMenusCopyNotTheAsset()
        {
            int before = GameSettings.TextSize;
            var asset = Resources.Load<PanelSettings>("UI/CamcorderPanel");
            var assetRes = asset.referenceResolution;
            try
            {
                var panel = UiKit.Panel;
                Assert.AreNotSame(asset, panel, "the menus must use a copy");
                GameSettings.TextSize = 0;
                Assert.AreEqual(1f, UiKit.TextScale);
                Assert.AreEqual(new Vector2Int(1920, 1080), panel.referenceResolution);
                GameSettings.TextSize = 2;
                Assert.AreEqual(1.3f, UiKit.TextScale, 1e-4f);
                Assert.Less(panel.referenceResolution.y, 1080, "a smaller reference makes everything larger");
                Assert.AreEqual(assetRes, asset.referenceResolution, "the asset itself is never changed");
                GameSettings.TextSize = 7;
                Assert.AreEqual(2, GameSettings.TextSize, "clamped to the three steps");
            }
            finally { GameSettings.TextSize = before; }
        }
    }
}
