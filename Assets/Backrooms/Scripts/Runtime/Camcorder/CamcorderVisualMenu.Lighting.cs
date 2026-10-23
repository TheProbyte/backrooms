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

            Section("SHAPE", "How light pools look on walls and floors.");
            Slider("Pool smoothing", "Rounds off the boxy look of light pools from distant fixtures (they are stored on a 0.6 m grid). 0 is the raw grid; higher is rounder and softer.",
                0f, 1f, "0.00", "", () => O().poolSmoothing, v => { O().poolSmoothing = v; O().Save(); });
            Slider("Light wrap", "How far light bends around surfaces that face slightly away from it. Lower gives crisper falloff on walls.",
                0f, 1f, "0.00", "", () => O().softness, v => { O().softness = v; O().Save(); });

            var row = BtnRow(_list);
            Button(row, "RESET LIGHTING", () => { O().ResetLighting(); O().Save(); RefreshAll(); });
        }
    }
}
