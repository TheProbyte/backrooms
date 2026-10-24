using System;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Edge codes of a blueprint. Edges lie on the 1.2 m cell grid exactly like the procedural world's
    /// (<see cref="OfficeLayout"/>), so a blueprint is stamped into the world's own layout and built by the world's own
    /// chunk builder. The low three bits are the kind, bit 3 makes a headed doorway round-topped.
    /// </summary>
    public static class EdgeCode
    {
        /// <summary>Nothing: the two cells are one open space.</summary>
        public const int Open = 0;
        /// <summary>A solid wall, floor to ceiling.</summary>
        public const int Wall = 1;
        /// <summary>A tall opening, one cell wide, up to the ceiling (no header).</summary>
        public const int Tall = 2;
        /// <summary>A doorway one cell wide with a 2.1 m header.</summary>
        public const int Header = 3;
        /// <summary>A half-height partition (1.15 m): the two cells stay open to each other above it.</summary>
        public const int Half = 4;
        /// <summary>A slim railing.</summary>
        public const int Railing = 5;
        /// <summary>Added to <see cref="Header"/>: a round-topped arch.</summary>
        public const int Arch = 8;

        public static int Kind(int c) => c & 7;
        public static bool Arched(int c) => (c & Arch) != 0;
        public static bool Passable(int c) => Kind(c) != Wall;
        /// <summary>Edges the player can walk through (openings, doorways and the open space between cells).</summary>
        public static bool Walkthrough(int c) { int k = Kind(c); return k == Open || k == Tall || k == Header; }
        public static string Name(int c)
        {
            switch (Kind(c))
            {
                case Open: return "open";
                case Wall: return "wall";
                case Tall: return "tall opening";
                case Header: return Arched(c) ? "arch" : "doorway";
                case Half: return "half wall";
                case Railing: return "railing";
            }
            return "?";
        }
    }

    [Serializable]
    public class RoomSettings
    {
        public string name = "Room";
        public RoomKind kind = RoomKind.Office;
        [Tooltip("Ceiling height in metres (2.7 is the normal office; taller ceilings get plaster and round lights).")]
        public float ceiling = OfficeBiome.CeilingHeight;
        [Tooltip("Painted plaster walls instead of wallpaper.")] public bool painted;
        public LightMode lights = LightMode.AllOn;

        public RoomSettings Clone() => (RoomSettings)MemberwiseClone();
    }

    [Serializable] public class PillarDef { public int x, z; public float size = 0.6f; }
    [Serializable] public class FixtureDef { public int x, z; public bool on = true; public bool faulty; }
    [Serializable]
    public class PropDef
    {
        public PropKind kind = PropKind.Box;
        public float x, z;            // metres from the blueprint's south-west corner
        public float yaw;
        public int variant;
        public bool lit = true;       // floor lamps
        public Vector3 size = new Vector3(0.6f, 0.5f, 0.5f);
        [Range(0f, 1f)] public float chance = 1f;
    }
    [Serializable]
    public class LootDef
    {
        public string item = "Loot";
        public float x, z;
        [Range(0f, 1f)] public float chance = 1f;
        public string note = "";
    }

    /// <summary>
    /// One layout of a custom room: a grid of cells (1.2 m), per-cell room settings, and the walls, doorways, lights and
    /// dressing on it. A room can have several variants; the world picks one when it places the room.
    /// </summary>
    [Serializable]
    public class RoomVariant
    {
        public string name = "Variant";
        public float weight = 1f;
        public int width = 6, depth = 6;
        public List<RoomSettings> rooms = new List<RoomSettings> { new RoomSettings() };
        /// <summary>Which of <see cref="rooms"/> each cell belongs to; index z * width + x.</summary>
        public int[] cellRoom;
        /// <summary>Edges on the lines X = 0..width between cells (x-1, z) and (x, z); index x * depth + z.</summary>
        public int[] vEdges;
        /// <summary>Edges on the lines Z = 0..depth between cells (x, z-1) and (x, z); index x * (depth + 1) + z.</summary>
        public int[] hEdges;
        public List<PillarDef> pillars = new List<PillarDef>();
        public List<FixtureDef> fixtures = new List<FixtureDef>();
        public List<PropDef> props = new List<PropDef>();
        public List<LootDef> loot = new List<LootDef>();

        public const int MinSize = 2, MaxSize = 24;

        public int V(int x, int z) => vEdges[x * depth + z];
        public int H(int x, int z) => hEdges[x * (depth + 1) + z];
        public void SetV(int x, int z, int code) => vEdges[x * depth + z] = code;
        public void SetH(int x, int z, int code) => hEdges[x * (depth + 1) + z] = code;
        public int RoomOf(int x, int z) => cellRoom[z * width + x];
        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < width && z < depth;
        public RoomSettings RoomAt(int x, int z) => rooms[Mathf.Clamp(RoomOf(x, z), 0, rooms.Count - 1)];

        /// <summary>A simple room: four walls, one doorway in the south wall, a light every two cells.</summary>
        public static RoomVariant CreateDefault(int w = 6, int d = 6, string name = "Variant 1")
        {
            var v = new RoomVariant { name = name, width = Mathf.Clamp(w, MinSize, MaxSize), depth = Mathf.Clamp(d, MinSize, MaxSize) };
            v.Allocate();
            v.WallPerimeter();
            v.SetH(v.width / 2, 0, EdgeCode.Header);          // doorway in the middle of the south wall
            v.AutoFixtures();
            return v;
        }

        void Allocate()
        {
            cellRoom = new int[width * depth];
            vEdges = new int[(width + 1) * depth];
            hEdges = new int[width * (depth + 1)];
        }

        /// <summary>Walls on all four sides (replaces whatever is on the perimeter).</summary>
        public void WallPerimeter()
        {
            for (int z = 0; z < depth; z++) { SetV(0, z, EdgeCode.Wall); SetV(width, z, EdgeCode.Wall); }
            for (int x = 0; x < width; x++) { SetH(x, 0, EdgeCode.Wall); SetH(x, depth, EdgeCode.Wall); }
        }

        /// <summary>Makes the arrays consistent with width and depth (after loading or hand edits) and clamps values.</summary>
        public void Validate()
        {
            width = Mathf.Clamp(width, MinSize, MaxSize); depth = Mathf.Clamp(depth, MinSize, MaxSize);
            if (rooms == null || rooms.Count == 0) rooms = new List<RoomSettings> { new RoomSettings() };
            if (cellRoom == null || cellRoom.Length != width * depth || vEdges == null || vEdges.Length != (width + 1) * depth || hEdges == null || hEdges.Length != width * (depth + 1))
            {
                var old = (cellRoom, vEdges, hEdges);
                Allocate();
                if (old.cellRoom == null) WallPerimeter();
            }
            for (int i = 0; i < cellRoom.Length; i++) cellRoom[i] = Mathf.Clamp(cellRoom[i], 0, rooms.Count - 1);
            for (int i = 0; i < vEdges.Length; i++) vEdges[i] = Mathf.Clamp(vEdges[i], 0, 15);
            for (int i = 0; i < hEdges.Length; i++) hEdges[i] = Mathf.Clamp(hEdges[i], 0, 15);
            foreach (var r in rooms) r.ceiling = Mathf.Clamp(r.ceiling, OfficeBiome.CeilingHeight, OfficeBiome.MaxCeilingHeight);
            fixtures.RemoveAll(f => !InBounds(f.x, f.z));
            pillars.RemoveAll(p => p.x < 0 || p.z < 0 || p.x > width || p.z > depth);
            weight = Mathf.Max(0f, weight);
        }

        /// <summary>
        /// Resizes the grid. Cells outside the old grid copy the nearest old cell; interior walls are kept (and extended
        /// when the room grows); a perimeter wall that moves takes its doorways with it.
        /// </summary>
        public void Resize(int w, int d)
        {
            w = Mathf.Clamp(w, MinSize, MaxSize); d = Mathf.Clamp(d, MinSize, MaxSize);
            if (w == width && d == depth) return;
            var old = Clone();
            width = w; depth = d;
            Allocate();
            for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                cellRoom[z * width + x] = old.cellRoom[Mathf.Min(z, old.depth - 1) * old.width + Mathf.Min(x, old.width - 1)];

            // A perimeter edge can only be a wall or a way out.
            int Perimeter(int c) { int k = EdgeCode.Kind(c); return k == EdgeCode.Tall || k == EdgeCode.Header ? c : EdgeCode.Wall; }
            for (int x = 0; x <= width; x++)
            for (int z = 0; z < depth; z++)
            {
                int oz = Mathf.Min(z, old.depth - 1);
                int code;
                if (x == 0) code = Perimeter(old.V(0, oz));
                else if (x == width) code = Perimeter(old.V(Mathf.Min(width, old.width), oz));
                else code = x < old.width ? old.V(x, oz) : EdgeCode.Open;
                SetV(x, z, code);
            }
            for (int x = 0; x < width; x++)
            for (int z = 0; z <= depth; z++)
            {
                int ox = Mathf.Min(x, old.width - 1);
                int code;
                if (z == 0) code = Perimeter(old.H(ox, 0));
                else if (z == depth) code = Perimeter(old.H(ox, Mathf.Min(depth, old.depth)));
                else code = z < old.depth ? old.H(ox, z) : EdgeCode.Open;
                SetH(x, z, code);
            }
            rooms = old.rooms.ConvertAll(r => r.Clone());
            pillars = old.pillars.FindAll(p => p.x <= width && p.z <= depth);
            props = old.props.FindAll(p => p.x < width * OfficeBiome.CellSize && p.z < depth * OfficeBiome.CellSize);
            loot = old.loot.FindAll(l => l.x < width * OfficeBiome.CellSize && l.z < depth * OfficeBiome.CellSize);
            fixtures = old.fixtures.FindAll(f => f.x < width && f.z < depth);
            // New area gets the usual light spacing.
            var grid = AutoFixtureCells();
            foreach (var c in grid)
                if ((c.x >= old.width || c.y >= old.depth) && !fixtures.Exists(f => f.x == c.x && f.z == c.y))
                    fixtures.Add(new FixtureDef { x = c.x, z = c.y, on = RoomAt(c.x, c.y).lights != LightMode.Dark });
        }

        List<Vector2Int> AutoFixtureCells()
        {
            // The generator's spacing: a troffer every 2 cells, centred in the room.
            var list = new List<Vector2Int>();
            int ox = ((width - 1) % 2 + 1) / 2, oz = ((depth - 1) % 2 + 1) / 2;
            for (int x = ox; x < width; x += 2)
            for (int z = oz; z < depth; z += 2) list.Add(new Vector2Int(x, z));
            return list;
        }

        /// <summary>One ceiling light every two cells, like the generator's own.</summary>
        public void AutoFixtures()
        {
            fixtures.Clear();
            foreach (var c in AutoFixtureCells())
                fixtures.Add(new FixtureDef { x = c.x, z = c.y, on = RoomAt(c.x, c.y).lights != LightMode.Dark });
        }

        public RoomVariant Clone() => JsonUtility.FromJson<RoomVariant>(JsonUtility.ToJson(this));

        /// <summary>Metres: footprint.</summary>
        public Vector2 SizeMetres => new Vector2(width, depth) * OfficeBiome.CellSize;

        /// <summary>Does at least one perimeter edge lead outside (an entrance / exit)?</summary>
        public int CountExits()
        {
            int n = 0;
            for (int z = 0; z < depth; z++) { if (EdgeCode.Walkthrough(V(0, z))) n++; if (EdgeCode.Walkthrough(V(width, z))) n++; }
            for (int x = 0; x < width; x++) { if (EdgeCode.Walkthrough(H(x, 0))) n++; if (EdgeCode.Walkthrough(H(x, depth))) n++; }
            return n;
        }
    }

    /// <summary>A custom room as stored on disk: spawn rules plus one or more layouts (variants).</summary>
    [Serializable]
    public class RoomBlueprint
    {
        public int format = 1;
        public string id = "";
        public string name = "New room";
        [Tooltip("Spawns in the procedural world (Save to game turns this on).")] public bool enabled;
        [Tooltip("Chance that a landmark spot that picks this room really holds it (the world's landmark grid sets how many spots there are).")]
        [Range(0f, 1f)] public float chance = 0.5f;
        [Tooltip("Relative weight against other landmarks and rooms allowed at the same spot.")] public float weight = 1f;
        public List<string> biomes = new List<string>();
        public List<string> zones = new List<string>();
        [Tooltip("The world may turn the room by 90 degree steps.")] public bool allowRotate = true;
        public string notes = "";
        public string modified = "";
        public List<RoomVariant> variants = new List<RoomVariant>();

        public static RoomBlueprint CreateNew(string name = "New room")
        {
            var b = new RoomBlueprint { name = name };
            b.variants.Add(RoomVariant.CreateDefault());
            return b;
        }

        public void Validate()
        {
            if (variants == null) variants = new List<RoomVariant>();
            if (variants.Count == 0) variants.Add(RoomVariant.CreateDefault());
            foreach (var v in variants) v.Validate();
            chance = Mathf.Clamp01(chance); weight = Mathf.Max(0f, weight);
            if (biomes == null) biomes = new List<string>();
            if (zones == null) zones = new List<string>();
            if (string.IsNullOrWhiteSpace(name)) name = "Room";
        }

        public RoomBlueprint Clone() => JsonUtility.FromJson<RoomBlueprint>(JsonUtility.ToJson(this));

        public string ToJson() => JsonUtility.ToJson(this, true);

        public static RoomBlueprint FromJson(string json)
        {
            var b = JsonUtility.FromJson<RoomBlueprint>(json);
            b?.Validate();
            return b;
        }

        /// <summary>The landmark rule the world's generator uses for this room (footprint of the largest variant).</summary>
        public LandmarkRules ToRules()
        {
            int w = 0, d = 0;
            foreach (var v in variants) { w = Mathf.Max(w, Mathf.Max(v.width, v.depth)); d = w; }
            var r = new LandmarkRules { name = id, kind = LandmarkKind.Custom, width = w, depth = d, chance = chance, weight = weight, storeys = 1 };
            r.zones.Clear(); r.biomes.Clear();
            foreach (var z in zones) r.zones.Add(z);
            foreach (var b in biomes) r.biomes.Add(b);
            return r;
        }

        /// <summary>Picks a variant by weight with the caller's dice.</summary>
        public int PickVariant(System.Random rng)
        {
            float total = 0f;
            foreach (var v in variants) total += Mathf.Max(0f, v.weight);
            if (total <= 0f) return rng.Next(variants.Count);
            float pick = (float)rng.NextDouble() * total;
            for (int i = 0; i < variants.Count; i++) { pick -= Mathf.Max(0f, variants[i].weight); if (pick < 0f) return i; }
            return variants.Count - 1;
        }
    }
}
