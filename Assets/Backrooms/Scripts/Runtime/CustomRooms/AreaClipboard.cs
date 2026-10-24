using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>A detached snapshot of a complete painted area, including its boundary edges and dressing.</summary>
    public sealed class AreaClipboard
    {
        readonly RoomVariant _source;
        readonly int _area;
        public readonly RectInt bounds;
        public string Name => _source.rooms[_area].name;

        AreaClipboard(RoomVariant source, int area, RectInt bounds)
        { _source = source; _area = area; this.bounds = bounds; }

        public static AreaClipboard Capture(RoomVariant source, int area)
        {
            int x0 = source.width, z0 = source.depth, x1 = -1, z1 = -1;
            for (int z = 0; z < source.depth; z++)
            for (int x = 0; x < source.width; x++)
                if (source.RoomOf(x, z) == area)
                { x0 = Mathf.Min(x0, x); z0 = Mathf.Min(z0, z); x1 = Mathf.Max(x1, x); z1 = Mathf.Max(z1, z); }
            if (x1 < x0 || area < 0 || area >= source.rooms.Count) return null;
            return new AreaClipboard(source.Clone(), area, new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1));
        }

        bool SourceCell(int x, int z) => _source.InBounds(x, z) && _source.RoomOf(x, z) == _area;
        public bool Cell(int x, int z) => SourceCell(bounds.x + x, bounds.y + z);
        bool SourcePoint(float x, float z) => SourceCell(Mathf.FloorToInt(x / Generation.OfficeBiome.CellSize), Mathf.FloorToInt(z / Generation.OfficeBiome.CellSize));
        bool SourceVertex(int x, int z) => SourceCell(x, z) || SourceCell(x - 1, z) || SourceCell(x, z - 1) || SourceCell(x - 1, z - 1);

        public bool Fits(RoomVariant target, Vector2Int anchor) => anchor.x >= 0 && anchor.y >= 0
            && anchor.x + bounds.width <= target.width && anchor.y + bounds.height <= target.depth;

        /// <summary>Replace only the destination footprint. The caller owns undo and dirty-state handling.</summary>
        public bool Paste(RoomVariant target, Vector2Int anchor, out int newArea)
        {
            newArea = -1;
            if (!Fits(target, anchor)) return false;
            int dx = anchor.x - bounds.x, dz = anchor.y - bounds.y;
            const float c = Generation.OfficeBiome.CellSize;
            bool TargetCell(int x, int z) => SourceCell(x - dx, z - dz);
            bool TargetPoint(float x, float z) => TargetCell(Mathf.FloorToInt(x / c), Mathf.FloorToInt(z / c));
            target.fixtures.RemoveAll(f => TargetCell(f.x, f.z));
            target.props.RemoveAll(p => TargetPoint(p.x, p.z));
            target.loot.RemoveAll(p => TargetPoint(p.x, p.z));
            target.pillars.RemoveAll(p => SourceVertex(p.x - dx, p.z - dz));
            newArea = target.rooms.Count;
            var settings = _source.rooms[_area].Clone();
            settings.name += " copy";
            target.rooms.Add(settings);
            for (int z = bounds.yMin; z < bounds.yMax; z++)
            for (int x = bounds.xMin; x < bounds.xMax; x++)
                if (SourceCell(x, z)) target.cellRoom[(z + dz) * target.width + x + dx] = newArea;

            int Perimeter(int code)
            {
                int kind = EdgeCode.Kind(code);
                return kind == EdgeCode.Open ? EdgeCode.Tall : kind == EdgeCode.Half || kind == EdgeCode.Railing ? EdgeCode.Wall : code;
            }
            for (int x = bounds.xMin; x <= bounds.xMax; x++)
            for (int z = bounds.yMin; z < bounds.yMax; z++)
            {
                if (!SourceCell(x - 1, z) && !SourceCell(x, z)) continue;
                int tx = x + dx, tz = z + dz, code = _source.V(x, z);
                target.SetV(tx, tz, tx == 0 || tx == target.width ? Perimeter(code) : code);
            }
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            for (int z = bounds.yMin; z <= bounds.yMax; z++)
            {
                if (!SourceCell(x, z - 1) && !SourceCell(x, z)) continue;
                int tx = x + dx, tz = z + dz, code = _source.H(x, z);
                target.SetH(tx, tz, tz == 0 || tz == target.depth ? Perimeter(code) : code);
            }
            foreach (var f in _source.fixtures)
                if (SourceCell(f.x, f.z)) target.fixtures.Add(new FixtureDef { x = f.x + dx, z = f.z + dz, on = f.on, faulty = f.faulty });
            foreach (var p in _source.pillars)
                if (SourceVertex(p.x, p.z)) target.pillars.Add(new PillarDef { x = p.x + dx, z = p.z + dz, size = p.size });
            foreach (var p in _source.props)
                if (SourcePoint(p.x, p.z)) target.props.Add(new PropDef { kind = p.kind, x = p.x + dx * c, z = p.z + dz * c, yaw = p.yaw, variant = p.variant, lit = p.lit, size = p.size, chance = p.chance });
            foreach (var p in _source.loot)
                if (SourcePoint(p.x, p.z)) target.loot.Add(new LootDef { item = p.item, x = p.x + dx * c, z = p.z + dz * c, chance = p.chance, note = p.note });
            return true;
        }
    }
}
