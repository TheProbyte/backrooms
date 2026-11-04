using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// F5 > PLACES > WEAR AND DETAIL: jump to the nearest of the rarer signs of age (Docs/RealismPlan.md) among the places
    /// built so far (RealismSightings), placed where it is seen: in front of a wall, beside a thing on the floor, below
    /// one on the ceiling. Press again for the next nearest.
    /// </summary>
    public partial class CamcorderVisualMenu
    {
        static readonly (string kind, string label, string what)[] RealismKinds =
        {
            ("strip", "PEELING STRIP", "a strip of wallpaper come away"),
            ("bubbles", "PAPER BUBBLES", "bubbles in the wallpaper"),
            ("seam_open", "OPEN SEAM", "a wallpaper seam opened up"),
            ("patch", "REPAIR PATCH", "a repair patch"),
            ("plate_painted", "PAINTED PLATE", "an outlet or switch painted over"),
            ("plate_crooked", "CROOKED PLATE", "an outlet or switch hanging crooked"),
            ("smudge", "VENT SMUDGE", "a dirty supply vent"),
            ("tile_batch", "ODD TILES", "tiles from another batch"),
            ("tile_hole", "HOLE IN A TILE", "a tile with a hole cut in it"),
            ("tile_dropped", "DROPPED TILE", "a tile with an edge dropped"),
            ("light_askew", "ASKEW LIGHT", "a light hanging askew"),
            ("lens_cracked", "CRACKED LENS", "a cracked light lens"),
            ("lens_yellow", "YELLOWED LENS", "a yellowed light"),
            ("sprinkler_bare", "BARE SPRINKLER", "a sprinkler missing its ring"),
            ("carpet_tiles", "CARPET TILES", "a room in carpet tiles"),
            ("carpet_missing", "MISSING CARPET TILE", "a carpet tile gone"),
            ("carpet_odd", "ODD CARPET TILE", "a replaced carpet tile"),
            ("furniture_ghost", "FURNITURE MARKS", "where furniture stood"),
            ("rust", "RUST RING", "a rust ring on the carpet"),
            ("burn", "CIGARETTE BURN", "a cigarette burn"),
            ("tube_tint", "ODD TUBES", "a light with tubes of another white"),
            // Water, light, and what the office left behind (2026-11-03).
            ("damp", "DAMP CARPET", "a damp patch on the carpet"),
            ("wall_water", "WATER DOWN A WALL", "water run down a wall"),
            ("leak", "LEAK", "a leak dripping from the ceiling"),
            ("mould", "MOULD", "mould in a corner"),
            ("door_light", "LIGHT UNDER A DOOR", "a shut door with light under it"),
            ("tube_ends", "BLACKENED TUBES", "tubes gone black at the ends"),
            ("tube_starter", "TUBE WON'T START", "a tube trying to start"),
            ("extinguisher", "EXTINGUISHER", "a fire extinguisher"),
            ("pull_station", "PULL STATION", "a fire alarm pull station"),
            ("strobe", "ALARM STROBE", "a fire alarm horn strobe"),
            ("door_closer", "DOOR CLOSER", "a door with a closer"),
            ("kick_plate", "KICK PLATE", "a door with a kick plate"),
            ("door_wedge", "DOOR WEDGE", "a door wedged open"),
            ("corner_guard", "CORNER GUARD", "a corner guard"),
            ("thermostat", "THERMOSTAT", "a thermostat"),
            ("wall_vent", "LOW VENT", "a low wall vent"),
            ("fountain", "DRINKING FOUNTAIN", "a drinking fountain"),
            ("floor_box", "FLOOR BOX", "a floor outlet box"),
            ("cable_stubs", "CUT CABLES", "cut cables in a floor box"),
            ("partitions", "PARTITIONS", "cubicle partitions"),
            ("window_filled", "FILLED WINDOW", "a filled-in window"),
            ("wooden_chair", "WOODEN CHAIR", "a wooden chair on its own"),
            ("vending", "VENDING MACHINE", "a drinks vending machine"),
            ("stop_sign", "STOP SIGN", "a stop sign at a warehouse crossing"),
            ("lawn_chair", "LAWN CHAIR", "a plastic lawn chair"),
            ("school_chair", "SCHOOL CHAIRS", "school chairs left in a room"),
            ("cord", "EXTENSION CORD", "an extension cord taped down"),
        };

        void BuildRealismLocations()
        {
            _list = Group("WEAR AND DETAIL", "The rarer signs of age (Docs/RealismPlan.md), among the places built so far. Each puts you where it is seen; press again for the next nearest of the same kind.");
            UnityEngine.UIElements.VisualElement row = null;
            for (int i = 0; i < RealismKinds.Length; i++)
            {
                if (i % 4 == 0) row = BtnRow(_list);
                string kind = RealismKinds[i].kind;
                Button(row, RealismKinds[i].label, () => GoRealism(kind));
            }
        }

        /// <summary>The nearest of a kind (see RealismKinds), the next nearest on each press.</summary>
        public void GoRealism(string kind)
        {
            if (player == null) return;
            string what = kind;
            foreach (var k in RealismKinds) if (k.kind == kind) what = k.what;
            var list = RealismSightings.Of(kind, player.transform.position);
            if (list.Count == 0) { if (_locReadout != null) _locReadout.text = $"No {what} in the places built so far. Walk on and try again."; return; }
            _locCycle.TryGetValue("real:" + kind, out int want);
            int i = want % list.Count;
            _locCycle["real:" + kind] = i + 1;
            var s = list[i];
            View(s, out var stand, out float pitch);
            Go(stand, s.position, pitch, $"{what} {i + 1} of {list.Count}");
        }

        /// <summary>Where to stand to see a sighting, and how far to look down (negative: up).</summary>
        static void View(RealismSightings.Sighting s, out Vector3 stand, out float pitch)
        {
            const float Eye = 1.6f;
            Vector3 p = s.position;
            float floor = FloorBelow(p + Vector3.up * 0.05f, s.facing.y < -0.5f ? 6f : 3f);
            if (Mathf.Abs(s.facing.y) < 0.5f)
            {
                // A wall: in front of it.
                Vector3 n = new Vector3(s.facing.x, 0f, s.facing.z).normalized;
                float back = Clear(new Vector3(p.x, floor + 1f, p.z), n, 2.4f);
                stand = new Vector3(p.x, floor, p.z) + n * back;
                pitch = Mathf.Atan2(floor + Eye - p.y, back) * Mathf.Rad2Deg;
                return;
            }
            // The ceiling or the floor: off to the side with the most room.
            Vector3 best = Vector3.forward; float room = -1f;
            foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                float r = Clear(new Vector3(p.x, floor + 1f, p.z), d, 1.6f);
                if (r > room) { room = r; best = d; }
            }
            float off = Mathf.Max(0.6f, room);
            stand = new Vector3(p.x, floor, p.z) + best * off;
            pitch = Mathf.Atan2(floor + Eye - p.y, off) * Mathf.Rad2Deg;
        }

        /// <summary>The floor under a point (a raycast down), or the point's storey floor.</summary>
        static float FloorBelow(Vector3 p, float reach)
        {
            if (Physics.Raycast(p, Vector3.down, out var hit, reach, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
            return Mathf.Floor(p.y / 9f + 0.01f) * 9f;
        }

        /// <summary>How far one can go from p along d (up to want) before a wall, less a margin.</summary>
        static float Clear(Vector3 p, Vector3 d, float want) =>
            Physics.Raycast(p, d, out var hit, want + 0.35f, ~0, QueryTriggerInteraction.Ignore) ? Mathf.Max(0.3f, hit.distance - 0.35f) : want;
    }
}
