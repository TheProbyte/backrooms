using UnityEngine;

namespace Backrooms.Camcorder
{
    /// <summary>F5 > LIGHT: fine tuning of how the building is lit. Every control is a look setting, saved with the display options.</summary>
    public partial class CamcorderVisualMenu
    {
        void BuildLighting()
        {
            var gc = GraphicsController.Ensure();
            GraphicsOptions O() => gc.Options;

            Section("OVERALL", "How bright the fixtures are and how far their light travels. The camcorder's auto-iris evens out some of a change, so judge by the contrast between bright and dark areas.");
            Slider("Light strength", "Brightness of every ceiling fixture and lamp, near and far.",
                0.3f, 2f, "0.00", "x", () => O().lightStrength, v => { O().lightStrength = v; O().Save(); });
            Slider("Light reach", "How far each fixture's light travels before it fades out. Lower gives tighter pools with darker gaps between lights; higher fills rooms.",
                0.6f, 1.5f, "0.00", "x", () => O().lightReach, v => { O().lightReach = v; O().Save(); });
            Slider("High-ceiling boost", "Extra output for fixtures hung high above the floor (tall halls, the atrium well), which would otherwise be dim from the distance. Lower makes big spaces moodier and darker in the middle.",
                0f, 0.6f, "0.00", "", () => O().tallBoost, v => { O().tallBoost = v; O().Save(); });

            Section("DARKNESS", "Deepen the dark parts without touching the bright ones.");
            Slider("Dark spots", "Makes weakly lit areas (between lights, far corners, the middle of big rooms) fall away darker, while lit pools keep their brightness.",
                0f, 1f, "0.00", "", () => O().darkSpots, v => { O().darkSpots = v; O().Save(); });
            Slider("Fill light", "The soft light bounced up from lit floors onto ceilings and upper walls. Lower gives darker ceilings and a harder look.",
                0f, 2f, "0.00", "x", () => O().fillLight, v => { O().fillLight = v; O().Save(); });
            Slider("Shadow darkness", "How dark shadows from nearby lights are. Below 1 lets a little light into shadows.",
                0f, 1f, "0.00", "", () => O().shadowDarkness, v => { O().shadowDarkness = v; O().Save(); });

            Section("DISTANCE", "How far-away lights are drawn. Per pixel makes distant light pools as crisp as near ones, with no change as you walk closer.");
            Toggle("Per-pixel distant light", "Every fixture's light is worked out at each pixel (still blocked by walls) instead of being read from a coarse grid. Removes the blocky look of far pools. Costs a little speed.",
                () => O().perPixelFar, v => { O().perPixelFar = v; O().Save(); });
            Slider("Distant crisp lights", "How many more fixtures, beyond the shadow-casting ones, are real lights. Walls still stop them. 0 uses the soft light grid for everything far away. Costs a little speed.",
                0f, 120f, "0", "", () => O().farLights, v => { O().farLights = Mathf.RoundToInt(v); O().Save(); }, true);

            Section("FLICKER", "Faulty ceiling tubes that stutter now and then.");
            Slider("Flicker", "How often faulty tubes stutter and how deep the dips go. 0 turns it off; 1 is the original amount.",
                0f, 1.5f, "0.00", "x", () => O().flicker, v => { O().flicker = v; O().Save(); });

            Section("SHAPE", "How light pools look on walls and floors.");
            Slider("Pool smoothing", "Rounds off the boxy look of light pools from distant fixtures (they are stored on a 0.6 m grid). 0 is the raw grid; higher is rounder and softer.",
                0f, 1f, "0.00", "", () => O().poolSmoothing, v => { O().poolSmoothing = v; O().Save(); });
            Slider("Light wrap", "How far light bends around surfaces that face slightly away from it. Lower gives crisper falloff on walls.",
                0f, 1f, "0.00", "", () => O().softness, v => { O().softness = v; O().Save(); });

            Slider("Room fill", "Bounced light that evens out the walls, like global illumination. 0 = walls are lit mostly by pools under each fixture. 1 = walls glow almost as evenly as the ceiling above them.",
                0f, 1f, "0.00", "", () => O().roomFill, v => { O().roomFill = v; O().Save(); });
            Slider("Fixture spread", "How broadly a ceiling light throws its light. Low: a lamp-like cone that pools on nearby walls. High: a diffuse panel that washes the whole room.",
                0f, 1f, "0.00", "", () => O().fixtureSpread, v => { O().fixtureSpread = v; O().Save(); });
            var row = BtnRow(_list);
            Button(row, "EVEN & PALE", () => { O().ApplyEvenLighting(); O().Save(); RefreshAll(); });
            Button(row, "RESET LIGHTING", () => { O().ResetLighting(); O().Save(); RefreshAll(); });
        }
    }
}
