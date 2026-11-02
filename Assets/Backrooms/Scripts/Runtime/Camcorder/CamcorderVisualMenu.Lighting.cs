using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// F5 > LIGHT: the look of the building's light. Built-in looks at the top (and "my look", kept once you save it), then
    /// every control: colour, the fixtures, bounce light, shadows, haze, the camera's exposure, the Poolrooms, distance and
    /// flicker. Every control is a look setting, saved with the display options; moving one leaves the preset marked "changed".
    /// </summary>
    public partial class CamcorderVisualMenu
    {
        Button[] _lookButtons;
        Button _myLookLoad;

        void BuildLighting()
        {
            var gc = GraphicsController.Ensure();
            GraphicsOptions O() => gc.Options;
            CamcorderSettings S() => rig.Settings;
            // A look control: write, save, and the preset is no longer what is shown.
            Action<float> Look(Action<float> set) => v => { set(v); O().lookPreset = -1; O().Save(); MarkLook(); };

            Section("LOOK", "Ready-made looks for the whole building's light. Pick one, then fine-tune with the controls below; SAVE MY LOOK keeps your own.");
            var row = BtnRow(_list);
            _lookButtons = new Button[GraphicsOptions.LookNames.Length];
            for (int i = 0; i < GraphicsOptions.LookNames.Length; i++)
            {
                int idx = i;
                _lookButtons[i] = Button(row, GraphicsOptions.LookNames[i], () => { O().ApplyLook(idx); O().Save(); RefreshAll(); MarkLook(); });
                AttachTip(_lookButtons[i], GraphicsOptions.LookNames[i], GraphicsOptions.LookTips[i]);
                if (i == 2) row = BtnRow(_list);
            }
            var mine = BtnRow(_list);
            var save = Button(mine, "SAVE MY LOOK", () => { O().SaveMyLook(); O().lookPreset = -2; O().Save(); MarkLook(); });
            AttachTip(save, "Save my look", "Keeps every light setting below as your own look (one slot). Quality settings are not part of a look.");
            _myLookLoad = Button(mine, "MY LOOK", () => { if (O().LoadMyLook()) { O().Save(); RefreshAll(); MarkLook(); } });
            AttachTip(_myLookLoad, "My look", "Back to the look you saved.");
            Button(mine, "RESET LIGHTING", () => { O().ResetLighting(); O().Save(); RefreshAll(); MarkLook(); });
            MarkLook();

            Section("COLOUR", "The colour of the light itself. The camcorder's white balance (CAMERA tab) then shifts the whole picture.");
            Slider("Light colour", "Colour temperature of every fixture, in kelvin. Low is warm and yellow like old tubes and lamps; high is cold and blue-white. 4500 is the fixtures as built. Lamps and strip lights shift with the tubes.",
                2700f, 6500f, "0", " K", () => O().colourTemp, Look(v => O().colourTemp = v));
            Slider("Tube tint", "Fluorescent tubes are rarely neutral: positive goes green (tired, cheap tubes), negative magenta.",
                -1f, 1f, "0.00", "", () => O().colourTint, Look(v => O().colourTint = v));
            Slider("Panel glow", "How bright the fittings themselves look when you see them (the lens, the tubes, a lamp's shade). It does not change the light they throw.",
                0.3f, 2.5f, "0.00", "x", () => O().panelGlow, Look(v => O().panelGlow = v));

            Section("FIXTURES", "How bright the fixtures are and how they throw their light. The camcorder's auto-iris evens out some of a change, so judge by the contrast between bright and dark areas.");
            Slider("Light strength", "Brightness of every ceiling fixture and lamp, near and far.",
                0.3f, 2f, "0.00", "x", () => O().lightStrength, Look(v => O().lightStrength = v));
            Slider("Light reach", "How far each fixture's light travels before it fades out. Lower gives tighter pools with darker gaps between lights; higher fills rooms.",
                0.6f, 1.5f, "0.00", "x", () => O().lightReach, Look(v => O().lightReach = v));
            Slider("High-ceiling boost", "Extra output for fixtures hung high above the floor (tall halls, the atrium well), which would otherwise be dim from the distance. Lower makes big spaces moodier and darker in the middle.",
                0f, 0.6f, "0.00", "", () => O().tallBoost, Look(v => O().tallBoost = v));
            Slider("Fixture spread", "How broadly a ceiling light throws its light. Low: a lamp-like cone that pools on nearby walls. High: a diffuse panel that washes the whole room.",
                0f, 1f, "0.00", "", () => O().fixtureSpread, Look(v => O().fixtureSpread = v));
            Toggle("Measured light shape (IES)", "Ceiling fixtures throw light the way the real ones do, from photometric (IES) files: a prismatic panel brightest straight down and still lighting the tops of the walls, a dome and bare tubes wider. Off uses the adjustable cone (Fixture spread).",
                () => O().photometric, v => { O().photometric = v; O().Save(); });
            Toggle("Rectangular panels", "Ceiling panels light the room as the glowing rectangles they are: soft right under and beside them, with long rectangular reflections on glossy floors. Off treats each as a point (cheaper, harder light on walls right beside a panel).",
                () => O().areaLights, v => { O().areaLights = v; O().Save(); });
            Slider("Panel size", "0 treats a fitting as a point. 1 is the visible panel or tubes, which softens near-wall hotspots and widens reflections.",
                0f, 1f, "0.00", "", () => O().panelSize, Look(v => O().panelSize = v));
            Slider("Light wrap", "How far light bends around surfaces that face slightly away from it. Lower gives crisper falloff on walls.",
                0f, 1f, "0.00", "", () => O().softness, Look(v => O().softness = v));

            Section("BOUNCE", "Light that comes off the floor, walls and furniture and lights everything else: what makes a room feel lit rather than spot-lit.");
            Toggle("Traced bounce", "Bounced light is traced through the rooms: rays from points on a 1.2 m grid find the lit floors, walls, ceilings and furniture around them, so light spills through doorways, a lit room glows into a dark one, and shut doors and furniture block it. Off: the older, cheaper spread of floor light.",
                () => O().tracedBounce, v => { O().tracedBounce = v; O().Save(); });
            Slider("Bounce strength", "How much bounced light there is overall. Lower gives darker ceilings and corners and a harder look.",
                0f, 2.5f, "0.00", "x", () => O().fillLight, Look(v => O().fillLight = v));
            Slider("Colour bleed", "How much the surfaces colour the light they bounce: yellow walls and brown carpet warm the whole room. 0 bounces plain grey light.",
                0f, 2f, "0.00", "x", () => O().colourBleed, Look(v => O().colourBleed = v));
            Slider("Second bounce", "Traced bounce: how much of the bounced light bounces again, filling deep corners and rooms off the lit ones.",
                0f, 1f, "0.00", "", () => O().multiBounce, Look(v => O().multiBounce = v));
            Slider("Room fill", "Older bounce field only: evens out the walls. 0 = walls are lit mostly by pools under each fixture, 1 = walls glow almost as evenly as the ceiling.",
                0f, 1f, "0.00", "", () => O().roomFill, Look(v => O().roomFill = v));

            Section("SCREEN LIGHT", "Light worked out from what the camera sees, on top of the traced bounce: the bounce between things close together, reflections in glossy surfaces, and reflections kept out of corners.");
            Toggle("Screen light", "Bounce, occlusion and reflections found in the picture itself (half resolution, blended over frames). Off: the older ambient occlusion and the reflection probes alone.",
                () => O().screenLight, v => { O().screenLight = v; O().Save(); });
            Slider("Near bounce", "What hides part of a surface lights it with its own light: the floor glows up the bottom of a lit wall, a desk's top lights the underside of a shelf, and a corner between lit walls stays lit instead of going dark as plain AO makes it. 0 keeps just the occlusion.",
                0f, 2f, "0.00", "x", () => O().screenBounce, Look(v => O().screenBounce = v));
            Slider("Screen reflections", "Glossy surfaces (lino, laminate, metal, glass, wet floors) reflect what is really beside them, found in the picture: sharp on smooth surfaces, blurred on rougher ones. Where nothing is found the room's reflection probe is used. 0: probes only.",
                0f, 1f, "0.00", "", () => O().screenReflections, Look(v => O().screenReflections = v));
            Slider("Reflection occlusion", "Keeps the room's cached reflection out of corners, under furniture and inside cupboards, where it would glow.",
                0f, 1f, "0.00", "", () => O().reflectionOcclusion, Look(v => O().reflectionOcclusion = v));

            Section("RAY TRACING", Generation.RayTracedLighting.Supported
                ? "Light traced with the graphics card's ray tracing hardware, against the real shapes of everything round you."
                : "Needs a graphics card with ray tracing (RTX, Radeon RX 6000 or newer) running Direct3D 12. Not available here: the settings below are used instead.");
            Toggle("Ray-traced shadows", "Every ceiling light casts real shadows of every chair leg, desk, box, door and person round it, near or far, soft-edged as a panel's shadow is and sharp where things touch the floor. Replaces the shadow maps of the nearest lights and the contact shadows.",
                () => O().rayTracedShadows, v => { O().rayTracedShadows = v; O().Save(); });
            Toggle("Ray-traced reflections", "Glossy floors, metal, glass and tiles reflect what is really there, traced through the room: what is behind you and off the edge of the picture too, and the room's cached reflection no longer shows through a chair or a pillar. Replaces the reflections found in the picture.",
                () => O().rayTracedReflections, v => { O().rayTracedReflections = v; O().Save(); });
            Toggle("Ray-traced occlusion", "Bounced light is hidden by what really stands over and beside a surface: under desks and chairs, behind cabinets, inside shelves, whether you can see the thing doing the hiding or not. Works with the screen light (the darker of the two is kept). Needs Ambient occlusion on.",
                () => O().rayTracedOcclusion, v => { O().rayTracedOcclusion = v; O().Save(); });

            Section("SURFACES", "How the materials themselves take the light.");
            Toggle("Surface detail", "Carpet pile, ceiling tile fissures, the wallpaper's emboss, plaster and fabric have real depth: close up the texture shifts with its relief as you move, deep spots stay darker in bounced light and lose the light first when it comes in low (a ceiling light raking down a wall brings out its texture), and carpet and upholstery catch a soft sheen toward the light.",
                () => O().surfaceDetail, v => { O().surfaceDetail = v; O().Save(); });

            Section("SHADOWS & DARKNESS", "Deepen the dark parts and shape the shadows of furniture, items and you.");
            Slider("Shadow darkness", "How dark shadows from nearby lights are. Below 1 lets a little light into shadows.",
                0f, 1f, "0.00", "", () => O().shadowDarkness, Look(v => O().shadowDarkness = v));
            Slider("Shadow softness", "Shadows go soft the farther they fall from what casts them and stay sharp where things touch, as under a real panel light. 0 = the plain, even softness.",
                0f, 2f, "0.00", "x", () => O().shadowSoftness, Look(v => O().shadowSoftness = v));
            Slider("Contact shadows", "Short, sharp shadows right where things meet: a cup on a table, your hand on a wall, chair legs on the carpet, the small things shadow maps miss. Worked out from the picture's depth for the two lights that light each spot most.",
                0f, 1f, "0.00", "", () => O().contactShadows, Look(v => O().contactShadows = v));
            Slider("Furniture contact", "Soft darkening of bounce and distant light under and beside furniture (up to 160 nearby boxes, follows moved props). 0 disables it.",
                0f, 1f, "0.00", "", () => O().contactOcclusion, Look(v => O().contactOcclusion = v));
            Slider("Dark spots", "Makes weakly lit areas (between lights, far corners, the middle of big rooms) fall away darker, while lit pools keep their brightness.",
                0f, 1f, "0.00", "", () => O().darkSpots, Look(v => O().darkSpots = v));

            Section("AIR", "Dust and haze in the air, lit by the lights: glow round fixtures, lit air in bright rooms, dark rooms stay clear.");
            Slider("Haze", "How thick the air is. Light hangs in it near the fixtures and in lit rooms; distant rooms soften. 0 is perfectly clear.",
                0f, 1f, "0.00", "", () => O().haze, Look(v => O().haze = v));
            Toggle("Volumetric light", "The air is lit in 3D, cell by cell of the view: light from a lit room falls through the doorway in a shaft, furniture shadows the air under a lamp, and the air glows looking toward a fixture. Off: the older, flatter haze.",
                () => O().volumetricLight, v => { O().volumetricLight = v; O().Save(); });
            Slider("Glow toward lights", "Volumetric light: dust and haze scatter light mostly onward, so the air brightens round a fixture you look toward and shafts show best looking along them. 0 scatters evenly all round.",
                0f, 0.8f, "0.00", "", () => O().hazeGlow, Look(v => O().hazeGlow = v));
            Slider("Settled dust", "Dust settled on everything that faces up: desks and tables, the tops of baseboards, door headers, boxes, light fittings. Patchy, never on the floor itself. 0 is spotless.",
                0f, 2f, "0.00", "x", () => O().surfaceDust, v => { O().surfaceDust = v; O().Save(); });
            Slider("Dust in the air", "Motes drifting in the air, seen only where a ceiling light shines down through them. Very faint. 0 is clean air.",
                0f, 3f, "0.00", "x", () => O().airDust, v => { O().airDust = v; O().Save(); });

            Section("CAMERA", "The camcorder's own response (the same settings as the CAMERA tab, saved with the camera).");
            Slider("Exposure bias", "How bright the camera wants the picture, in stops. The auto-iris then aims for that.",
                -2f, 2f, "0.00", " EV", () => S().exposureBias, v => S().exposureBias = v);
            Slider("Contrast", "Separation between darks and brights in the picture.", -30f, 50f, "0", "", () => S().contrast, v => S().contrast = v);

            Section("POOLROOMS", "The Poolrooms have their own light: sun through the roof openings, sky and the bounce off the tiles.");
            Slider("Sun", "Brightness of the sunlight that falls through the openings.", 0f, 2f, "0.00", "x", () => O().poolSun, Look(v => O().poolSun = v));
            Slider("Sky", "The soft light of the sky everywhere, through the openings.", 0f, 2f, "0.00", "x", () => O().poolSky, Look(v => O().poolSky = v));
            Slider("Tile bounce", "Light bounced off sunlit tiles and water onto the walls and ceilings.", 0f, 2f, "0.00", "x", () => O().poolBounce, Look(v => O().poolBounce = v));
            Slider("Sun warmth", "Positive warms the sun to late afternoon, negative cools it.", -1f, 1f, "0.00", "", () => O().poolWarmth, Look(v => O().poolWarmth = v));

            Section("DISTANCE", "How far-away lights are drawn. Per pixel makes distant light pools as crisp as near ones, with no change as you walk closer.");
            Toggle("Per-pixel distant light", "Every fixture's light is worked out at each pixel (still blocked by walls) instead of being read from a coarse grid. Removes the blocky look of far pools. Costs a little speed.",
                () => O().perPixelFar, v => { O().perPixelFar = v; O().Save(); });
            Slider("Exact light radius", "With per-pixel light on: each fixture is worked out exactly on surfaces within this distance of it (its pool, its edges on walls); its weak light farther away is read from the soft light grid. Larger is slower; 0 = exact everywhere (slowest).",
                0f, 12f, "0.0", " m", () => O().exactLightRadius, v => { O().exactLightRadius = v; O().Save(); });
            Slider("Distant crisp lights", "How many more fixtures, beyond the shadow-casting ones, are real lights. Walls still stop them. 0 uses the soft light grid for everything far away. Costs a little speed.",
                0f, 120f, "0", "", () => O().farLights, v => { O().farLights = Mathf.RoundToInt(v); O().Save(); }, true);
            Slider("Pool smoothing", "Rounds off the boxy look of light pools from distant fixtures (they are stored on a 0.6 m grid). 0 is the raw grid; higher is rounder and softer.",
                0f, 1f, "0.00", "", () => O().poolSmoothing, Look(v => O().poolSmoothing = v));
            Toggle("Indoor reflections", "Two local room-bounded reflection probes, captured one at a time across frames. Useful on metal and laminate; captures exclude portal surfaces. These are cached reflections, so moved objects and power changes take a moment to refresh.",
                () => O().indoorReflections, v => { O().indoorReflections = v; O().Save(); });

            Section("FLICKER", "Faulty ceiling tubes that stutter now and then.");
            Slider("Flicker", "How often faulty tubes stutter and how deep the dips go. 0 turns it off; 1 is the original amount.",
                0f, 1.5f, "0.00", "x", () => O().flicker, Look(v => O().flicker = v));
        }

        /// <summary>Highlights the look in use (none once a control has moved).</summary>
        void MarkLook()
        {
            if (_lookButtons == null) return;
            var o = GraphicsController.Instance ? GraphicsController.Instance.Options : null;
            int cur = o != null ? o.lookPreset : 0;
            for (int k = 0; k < _lookButtons.Length; k++) _lookButtons[k].EnableInClassList("cc-selected", k == cur);
            if (_myLookLoad != null)
            {
                _myLookLoad.SetEnabled(GraphicsOptions.HasMyLook);
                _myLookLoad.EnableInClassList("cc-selected", cur == -2);
            }
        }
    }
}
