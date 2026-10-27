using System;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Destruction
{
    /// <summary>
    /// What is inside one breakable wall panel, all from the world seed and the layout (never saved): the two gypsum
    /// boards and how each breaks (<see cref="FracturePattern"/>), the studs between the posts (galvanised steel in the
    /// Offices, wood in the older Hallways and the Warehouse's storage), the tracks or plates top and bottom, insulation
    /// batts in some walls, the electrical box behind each outlet and switch with its cable, now and then a copper pipe, and
    /// the baseboard on each side in three lengths.
    /// </summary>
    public sealed class WallPanelModel
    {
        public readonly WallKey key;
        public readonly float height;
        public readonly bool steel, insulated;
        public readonly FracturePattern[] boards = new FracturePattern[2];
        public readonly float[] studs = (float[])WallSpec.StudU.Clone();
        /// <summary>Insulation bays (u ranges between the studs) and rows of batts in each.</summary>
        public readonly List<Vector2> bays = new List<Vector2>();
        public int rows;
        public float rowHeight;
        public readonly List<Plate> plates = new List<Plate>();
        public readonly List<Cable> cables = new List<Cable>();
        /// <summary>A copper pipe (u, radius), or radius 0.</summary>
        public Vector2 pipe;
        /// <summary>Baseboard lengths (u ranges), the same on both sides.</summary>
        public readonly Vector2[] baseboard = new Vector2[3];
        /// <summary>Per side: the wall's finish (as the chunk draws it), the baseboard and the light layer.</summary>
        public readonly Material[] finish = new Material[2];
        public Material baseboardMaterial;
        /// <summary>Per side: does the room have a baseboard (as the chunk draws it: the level's look has one, the room a floor)?</summary>
        public readonly bool[] baseboards = new bool[2];
        public readonly uint[] mask = new uint[2];
        /// <summary>Per side: the room's wall top (above the breakable height the chunk keeps drawing the wall).</summary>
        public readonly float[] wallTop = new float[2];

        public struct Plate { public int side; public float u, v; public bool isSwitch; }
        public struct Cable { public Vector3 a, b; public float radius; }

        public const int BaseboardPieces = 3;
        public const float BaseboardHeight = OfficeBiome.BaseboardHeight, BaseboardDepth = OfficeBiome.BaseboardDepth;

        public WallPanelModel(WallKey key, int worldSeed, float height, bool steel, OfficeLayout[] side, OfficeBiomeAssets assets)
        {
            this.key = key; this.height = height; this.steel = steel;
            int seed = key.Seed(worldSeed);
            var dice = new Dice(seed);
            for (int s = 0; s < 2; s++) boards[s] = new FracturePattern(seed * 31 + s * 7919 + 1, WallSpec.U0, WallSpec.U1, 0f, height);

            // Insulation: whole stretches of wall (groups of five panels) are filled or not.
            int group = Mathf.FloorToInt(key.along / 5f);
            insulated = Dice.Hash(worldSeed, key.line * 2 + (key.vertical ? 1 : 0), group, key.storey) * 0.5f + 0.5f < (steel ? 0.4f : 0.2f);
            float sw = WallSpec.StudWidth(steel);
            float bottom = WallSpec.StudBottom(steel), top = WallSpec.StudTop(steel, height);
            bays.Add(new Vector2(WallSpec.U0, studs[0] - sw * 0.5f));
            bays.Add(new Vector2(studs[0] + sw * 0.5f, studs[1] - sw * 0.5f));
            bays.Add(new Vector2(studs[1] + sw * 0.5f, WallSpec.U1));
            rows = Mathf.Max(1, Mathf.RoundToInt((top - bottom) / 0.3f));
            rowHeight = (top - bottom) / rows;

            for (int i = 0; i < BaseboardPieces; i++)
            {
                float a = Mathf.Lerp(WallSpec.U0, WallSpec.U1, i / (float)BaseboardPieces), b = Mathf.Lerp(WallSpec.U0, WallSpec.U1, (i + 1) / (float)BaseboardPieces);
                baseboard[i] = new Vector2(a, b);
            }

            // The rooms on each side: finish, light layer, wall top; and the plates on this wall.
            for (int s = 0; s < 2; s++)
            {
                var L = side[s];
                if (L == null) continue;
                var c = key.LocalCell(s);
                var room = L.RoomAt(c.x, c.y);
                wallTop[s] = L.WallTop(room);
                mask[s] = L.style == ZoneStyle.Pool ? 1u : LightLayers.LayerBit(room.color);
                if (assets) finish[s] = MaterialMaps.For(assets.WallMaterial(room));
                baseboards[s] = assets && !assets.noBaseboard && !room.noFloor;
                int cellSide = key.CellSide(s);
                foreach (var p in L.plates)
                    if (p.cell == c && p.side == cellSide && p.height < height - 0.1f)
                        plates.Add(new Plate { side = s, u = p.t * WallSpec.C, v = p.height, isSwitch = p.isSwitch });
            }
            if (assets) baseboardMaterial = MaterialMaps.For(assets.baseboard);

            // Cables: from each box up to the top track, beside the box. Wood walls (old wiring) also carry a run along the
            // wall through holes in the studs at outlet height, in some stretches of wall and wherever there is an outlet.
            float cr = steel ? 0.0075f : 0.0055f;
            foreach (var p in plates)
            {
                float u = Mathf.Clamp(p.u + 0.035f, WallSpec.U0 + 0.01f, WallSpec.U1 - 0.01f);
                float w = (p.side == 0 ? -1f : 1f) * (WallSpec.CavityHalf - 0.03f);
                cables.Add(new Cable { a = new Vector3(u, p.v + 0.05f, w), b = new Vector3(u, top, w * 0.4f), radius = cr });
            }
            bool run = !steel && (Dice.Hash(worldSeed, key.line * 2 + (key.vertical ? 1 : 0), Mathf.FloorToInt(key.along / 4f), 99) > 0.4f || plates.Exists(p => !p.isSwitch));
            if (run) cables.Add(new Cable { a = new Vector3(WallSpec.U0, 0.42f, 0f), b = new Vector3(WallSpec.U1, 0.42f, 0f), radius = cr });
            // Now and then a copper water pipe up through a bay.
            if (dice.Value < (steel ? 0.04f : 0.07f))
            {
                var bay = bays[1 + dice.Range(0, 2)];
                pipe = new Vector2(Mathf.Lerp(bay.x, bay.y, dice.Range(0.3f, 0.7f)), 0.011f);
            }
        }

        public bool HasPipe => pipe.y > 0f;
        public bool HasBaseboard(WallPanelState st, int side, int piece) => baseboards[side] && st.BaseboardPresent(side, piece);

        /// <summary>Which bay a point along the wall is in (-1 on a stud or a post).</summary>
        public int BayAt(float u)
        {
            for (int i = 0; i < bays.Count; i++) if (u >= bays[i].x && u <= bays[i].y) return i;
            return -1;
        }

        /// <summary>The stud a point along the wall is on (-1 between studs).</summary>
        public int StudAt(float u, float margin = 0f)
        {
            float half = WallSpec.StudWidth(steel) * 0.5f + margin;
            for (int i = 0; i < studs.Length; i++) if (Mathf.Abs(u - studs[i]) <= half) return i;
            return -1;
        }

        public int BaseboardAt(float u)
        {
            for (int i = 0; i < BaseboardPieces; i++) if (u >= baseboard[i].x - 1e-4f && u <= baseboard[i].y + 1e-4f) return i;
            return -1;
        }

        /// <summary>A plate's electrical box in panel coordinates (centre, half size): flush with the board, 60 mm deep.</summary>
        public void BoxOf(Plate p, out Vector3 centre, out Vector3 half)
        {
            float sign = p.side == 0 ? -1f : 1f;
            const float depth = 0.06f;
            half = new Vector3(0.026f, 0.046f, depth * 0.5f);
            centre = new Vector3(p.u, p.v, sign * (WallSpec.T * 0.5f - depth * 0.5f - 0.0005f));
        }

        // ---------------- Which walls break ----------------

        /// <summary>
        /// Can this wall be broken? Ordinary walls between rooms of Level 0 can; the posts at their ends (corner and jamb
        /// studs), pillars, the Poolrooms' tiled walls, landmarks and their stair halls and shafts, walls round a void,
        /// crawl holes and noclip doors cannot. height: how high it breaks (a normal room's ceiling, or the lower side).
        /// </summary>
        public static bool Breakable(WallKey key, OfficeLayout l0, OfficeLayout l1, out float height)
        {
            height = 0f;
            if (l0 == null || l1 == null) return false;
            if (l0.style == ZoneStyle.Pool || l1.style == ZoneStyle.Pool) return false;
            var layouts = new[] { l0, l1 };
            float top = WallSpec.MaxHeight;
            for (int s = 0; s < 2; s++)
            {
                var L = layouts[s];
                var c = key.LocalCell(s);
                if (!L.InChunk(c.x, c.y)) return false;
                if (L.EdgeOf(c.x, c.y, key.CellSide(s)) != EdgeKind.Wall) return false;
                var room = L.RoomAt(c.x, c.y);
                if (!room.Walkable || room.noFloor || room.spiral || room.role != AnnexRole.None || !string.IsNullOrEmpty(room.landmark)) return false;
                if (L.Has(c.x, c.y, CellFlags.NoFloor)) return false;
                foreach (var p in L.portals) if (p.cell == c && p.side == key.CellSide(s)) return false;
                foreach (var d in L.noclipDoors) if (d.cell == c && d.side == key.CellSide(s)) return false;
                top = Mathf.Min(top, L.WallTop(room));
            }
            if (top < 2f) return false;
            height = top;
            return true;
        }

        /// <summary>Galvanised steel studs (Offices) or wood (the Hallways' older walls and the Warehouse's storage).</summary>
        public static bool SteelFor(OfficeLayout l0, OfficeLayout l1)
        {
            var L = l1 ?? l0;
            if (L == null) return true;
            return !(L.biomeName == "Hallways" || L.biomeName == "Warehouse");
        }
    }

    /// <summary>
    /// What has been done to one wall panel: which pieces of each board are gone or cracked, the baseboards, the studs
    /// (the lengths still standing, and the damage along them), the insulation batts. Saved with the world.
    /// </summary>
    public sealed class WallPanelState
    {
        public readonly WallKey key;
        public readonly bool[][] removed = new bool[2][], cracked = new bool[2][];
        public int baseboardGone;                       // bit (side * 3 + piece)
        public readonly float[] baseboardDamage = new float[6];
        public bool[] insulationGone;                   // [bay * rows + row]
        /// <summary>Per stud: the lengths still standing (v from, to).</summary>
        public readonly List<Vector2>[] studSegments;
        /// <summary>Per stud: damage in 0.45 m lengths (cracks, dents) that has not broken it yet.</summary>
        public readonly float[][] studDamage;
        public int version;

        public const int DamageBins = 6;
        public const float BinHeight = 0.45f;

        public WallPanelState(WallPanelModel m)
        {
            key = m.key;
            for (int s = 0; s < 2; s++) { removed[s] = new bool[m.boards[s].pieces.Count]; cracked[s] = new bool[m.boards[s].pieces.Count]; }
            insulationGone = new bool[m.bays.Count * m.rows];
            studSegments = new List<Vector2>[m.studs.Length];
            studDamage = new float[m.studs.Length][];
            for (int i = 0; i < m.studs.Length; i++)
            {
                studSegments[i] = new List<Vector2> { new Vector2(WallSpec.StudBottom(m.steel), WallSpec.StudTop(m.steel, m.height)) };
                studDamage[i] = new float[DamageBins];
            }
        }

        public bool BaseboardPresent(int side, int piece) => (baseboardGone & (1 << (side * 3 + piece))) == 0;

        public bool InsulationPresent(WallPanelModel m, int bay, int row) => m.insulated && bay >= 0 && row >= 0 && row < m.rows && !insulationGone[bay * m.rows + row];

        /// <summary>Is a stud standing at a height?</summary>
        public bool StudAt(int stud, float v)
        {
            foreach (var s in studSegments[stud]) if (v >= s.x && v <= s.y) return true;
            return false;
        }

        /// <summary>The board on a side is there at a point.</summary>
        public bool BoardAt(WallPanelModel m, int side, Vector2 uv)
        {
            int p = m.boards[side].PieceAt(uv);
            return p >= 0 && !removed[side][p];
        }

        public int RemovedCount(int side) { int n = 0; foreach (var r in removed[side]) if (r) n++; return n; }
        public bool AnyRemoved => RemovedCount(0) > 0 || RemovedCount(1) > 0;

        // ---------------- Saving ----------------

        public static string Bits(bool[] b)
        {
            var bytes = new byte[(b.Length + 7) / 8];
            for (int i = 0; i < b.Length; i++) if (b[i]) bytes[i >> 3] |= (byte)(1 << (i & 7));
            return Convert.ToBase64String(bytes);
        }

        public static void ReadBits(string s, bool[] into)
        {
            if (string.IsNullOrEmpty(s)) return;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(s); } catch { return; }
            for (int i = 0; i < into.Length && (i >> 3) < bytes.Length; i++) into[i] = (bytes[i >> 3] & (1 << (i & 7))) != 0;
        }
    }
}
