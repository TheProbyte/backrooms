using UnityEngine;
using UnityEngine.UIElements;
using TextCoreFont = UnityEngine.TextCore.Text.FontAsset;

namespace Backrooms.UI
{
    /// <summary>
    /// The game's one interface kit (Docs/UiPlan.md U1, 2026-11-10): its two typefaces, its colours by name and its type
    /// sizes. Every screen reads these instead of choosing its own.
    /// <list type="bullet">
    /// <item><b>Cam</b>, Pixelify Sans: the camcorder's own marks (REC, the counter, the date, notices, LOW LIGHT, the F5
    /// menu, the tape's blue screen).</item>
    /// <item><b>Paper</b>, Courier Prime: the company's paper and panels (menus, settings, the pause menu, side panels,
    /// prompts, slips, the co-op list, build mode).</item>
    /// </list>
    /// The 5 x 7 pixel font on the boards and screens in the world (PixelFont) is not part of this. Both faces ship in
    /// Resources/Fonts with their licences (SIL Open Font License 1.1); Pixelify Sans is the variable font cut into fixed
    /// Regular and Bold files, as Unity reads no variable fonts.
    /// </summary>
    public static partial class UiKit
    {
        public enum Face { Cam, Paper }

        // ---- Colours: the Overflow look's (Docs/StyleSheet.md). Kit.uss carries the same values for UI Toolkit; a test
        // (UiKitTests) keeps the two equal. ----

        static Color C(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);

        /// <summary>The panels' chocolate.</summary>
        public static readonly Color Choc = C(59, 42, 30);
        /// <summary>A raised part of a panel (a button, a card).</summary>
        public static readonly Color ChocRaised = C(84, 61, 43);
        /// <summary>A raised part under the pointer.</summary>
        public static readonly Color ChocHover = C(110, 80, 55);
        /// <summary>Behind a panel (a backdrop, a sunken field).</summary>
        public static readonly Color ChocDeep = C(38, 26, 18);
        /// <summary>The darkest: a full-screen backdrop, and the shadow under cream type.</summary>
        public static readonly Color Ground = C(20, 13, 9);
        /// <summary>The company amber: frames, the selection bar, headings.</summary>
        public static readonly Color Amber = C(240, 162, 30);
        /// <summary>The stripe's three: orange, gold, avocado.</summary>
        public static readonly Color Orange = C(217, 102, 31), Gold = C(232, 178, 58), Avocado = C(124, 138, 46);
        /// <summary>Type on chocolate.</summary>
        public static readonly Color Cream = C(243, 230, 200);
        /// <summary>Quieter type on chocolate (notes, hints, what a value means).</summary>
        public static readonly Color Tan = C(205, 180, 138);
        /// <summary>State: done or safe, running low, wrong or late.</summary>
        public static readonly Color Good = C(168, 184, 72), Warn = C(232, 178, 58), Bad = C(226, 92, 52);
        /// <summary>The camcorder's marks: near white, with a dark shadow.</summary>
        public static readonly Color Osd = C(255, 255, 255, 0.92f), OsdShadow = C(0, 0, 0, 0.75f);

        /// <summary>A client's colour (the tape on its boxes, its card).</summary>
        public static Color Client(int k) => Interaction.Contracts.ClientColours[Mathf.Clamp(k, 0, Interaction.Contracts.ClientColours.Length - 1)];

        // ---- Sizes: pixels of a 1080-line screen. Five steps and a floor: Small is the least any text may be, 12 px at
        // 1280 x 800 (Valve recommends 12 for the Steam Deck, and asks for 9 at the least). ----

        public const int Small = 17, Body = 22, Large = 28, Title = 36, Display = 56;

        /// <summary>TEXT SIZE (Settings, ACCESSIBILITY): 1, 1.15 or 1.3, applied to the menus (UI Toolkit), the IMGUI
        /// screens, the side panels and the prompts. The camcorder's own marks keep their size.</summary>
        public static float TextScale => GameSettings.TextSize == 2 ? 1.3f : GameSettings.TextSize == 1 ? 1.15f : 1f;

        static PanelSettings _panel;

        /// <summary>
        /// The menus' panel settings: a copy of Resources/UI/CamcorderPanel made at run time, so TEXT SIZE can change its
        /// scale without touching the asset (in the editor a change to the asset itself would be saved). Every UI Toolkit
        /// screen uses this.
        /// </summary>
        public static PanelSettings Panel
        {
            get
            {
                if (_panel) return _panel;
                var src = Resources.Load<PanelSettings>("UI/CamcorderPanel");
                if (!src) return null;
                _panel = Object.Instantiate(src);
                _panel.name = "CamcorderPanel (run time)";
                _panel.hideFlags = HideFlags.DontSave;
                ApplyTextScale();
                GameSettings.TextSizeChanged += ApplyTextScale;
                return _panel;
            }
        }

        static void ApplyTextScale()
        {
            if (!_panel) return;
            float k = TextScale;
            _panel.referenceResolution = new Vector2Int(Mathf.RoundToInt(1920f / k), Mathf.RoundToInt(1080f / k));
        }

        /// <summary>The share of the screen's width each black bar beside the 4:3 picture takes (0 without bars): menus sit
        /// inside the picture, not across the join.</summary>
        public static float BarShare => Screen.width > 0 ? Mathf.Clamp01(Camcorder.CamcorderOSD.SideWidth * Screen.height / (1080f * Screen.width)) : 0f;

        // ---- Fonts ----

        static Font _cam, _camBold, _paper, _paperBold;
        static TextCoreFont _camAsset, _paperAsset;

        static Font Load(ref Font f, string name)
        {
            if (!f) f = Resources.Load<Font>("Fonts/" + name);
            if (!f) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");   // the fonts missing: still readable
            return f;
        }

        /// <summary>The font for uGUI and IMGUI text (a bold face is its own file: set it here, not with FontStyle.Bold).</summary>
        public static Font Font(Face face, bool bold = false) => face == Face.Cam
            ? (bold ? Load(ref _camBold, "PixelifySans-Bold") : Load(ref _cam, "PixelifySans-Regular"))
            : (bold ? Load(ref _paperBold, "CourierPrime-Bold") : Load(ref _paper, "CourierPrime-Regular"));

        /// <summary>The face as a UI Toolkit font asset, its bold wired to the real bold file (made once, kept for the
        /// whole run: the menus come back with every load of the world scene).</summary>
        public static TextCoreFont Asset(Face face)
        {
            ref var a = ref (face == Face.Cam ? ref _camAsset : ref _paperAsset);
            if (a) return a;
            var regular = Font(face);
            var bold = Font(face, true);
            a = TextCoreFont.CreateFontAsset(regular);
            if (!a) return null;
            a.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (bold && bold != regular)
            {
                var b = TextCoreFont.CreateFontAsset(bold);
                if (b)
                {
                    b.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    var table = a.fontWeightTable;
                    // (The property hands back the asset's own array: set its entry in place.)
                    if (table != null && table.Length > 7) table[7].regularTypeface = b;
                }
            }
            return a;
        }

        /// <summary>Sets a UI Toolkit tree's typeface and adds the kit's colour names (Kit.uss) to it.</summary>
        public static void Apply(VisualElement root, Face face)
        {
            if (root == null) return;
            var a = Asset(face);
            if (a) root.style.unityFontDefinition = FontDefinition.FromSDFFont(a);
            var sheet = Resources.Load<StyleSheet>("UI/Kit");
            if (sheet && !root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
            root.AddToClassList("kit");   // Kit.uss's names are set on .kit (a sheet's :root never reaches a sub-tree)
        }
    }
}
