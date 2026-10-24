using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Builds a small stand-alone world around one custom room, with the same steps the district generator takes for a
    /// landmark (rooms, walls, forced exits, dressing, light colours). The builder uses it for its preview and for Test; the
    /// tests use it to check the stamping. The room sits inside a two-cell corridor ring that its exits lead into, standing
    /// in for the procedural neighbours.
    /// </summary>
    public static class CustomRoomHarness
    {
        public const int Margin = 2;

        public static OfficeLayout BuildLayout(RoomBlueprint bp, int variant, int rot, out LandmarkPlacement placement)
        {
            variant = Mathf.Clamp(variant, 0, bp.variants.Count - 1);
            var v = bp.variants[variant];
            var xf = new CustomRoomStamp.Xf(rot, v.width, v.depth);
            int n = Mathf.Max(OfficeBiome.ChunkCells, Mathf.Max(xf.Width, xf.Depth) + 2 * Margin);
            var L = new OfficeLayout(n, Vector2Int.zero);
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++) L.roomOf[x, z] = -1;

            string id = string.IsNullOrEmpty(bp.id) ? "unsaved" : bp.id;
            int rx = (n - xf.Width) / 2, rz = (n - xf.Depth) / 2;      // centred; at least Margin cells of corridor on every side
            var rect = new RectInt(rx, rz, xf.Width, xf.Depth);
            placement = new LandmarkPlacement
            {
                name = bp.name, kind = LandmarkKind.Custom, district = Vector2Int.zero, rect = rect, core = rect, storeys = 1,
                seed = 0x5EED, blueprint = id, variant = variant, rot = rot,
            };
            var previous = CustomRoomStamp.Override;
            CustomRoomStamp.Override = bp;
            try
            {
                var ring = new List<Room>
                {
                    DistrictGenerator.MakeRoom(L, new RectInt(0, 0, n, rz), RoomKind.Hallway),
                    DistrictGenerator.MakeRoom(L, new RectInt(0, rz + rect.height, n, n - rz - rect.height), RoomKind.Hallway),
                    DistrictGenerator.MakeRoom(L, new RectInt(0, rz, rx, rect.height), RoomKind.Hallway),
                    DistrictGenerator.MakeRoom(L, new RectInt(rx + rect.width, rz, n - rx - rect.width, rect.height), RoomKind.Hallway),
                };
                foreach (var r in ring) r.lights = LightMode.AllOn;

                var rooms = new List<Room>();
                foreach (var spec in Landmarks.Rooms(placement, 0, 9f))
                {
                    var room = DistrictGenerator.MakeRoom(L, spec.rect, spec.kind);
                    room.ceiling = spec.ceiling; room.painted = spec.painted; room.lights = spec.lights;
                    room.noDoors = spec.noDoors; room.landmark = bp.name; room.custom = true;
                    rooms.Add(room);
                }

                // Walls wherever rooms differ, as in a district plan; the ring is one open corridor.
                for (int x = 0; x <= n; x++)
                for (int z = 0; z < n; z++)
                    L.vEdges[x, z] = (x == 0 || x == n || L.roomOf[x - 1, z] != L.roomOf[x, z]) ? EdgeKind.Wall : EdgeKind.Open;
                for (int x = 0; x < n; x++)
                for (int z = 0; z <= n; z++)
                    L.hEdges[x, z] = (z == 0 || z == n || L.roomOf[x, z - 1] != L.roomOf[x, z]) ? EdgeKind.Wall : EdgeKind.Open;
                for (int x = 1; x < n; x++)
                for (int z = 0; z < n; z++)
                    if (!L.rooms[L.roomOf[x - 1, z]].custom && !L.rooms[L.roomOf[x, z]].custom) L.vEdges[x, z] = EdgeKind.Open;
                for (int x = 0; x < n; x++)
                for (int z = 1; z < n; z++)
                    if (!L.rooms[L.roomOf[x, z - 1]].custom && !L.rooms[L.roomOf[x, z]].custom) L.hEdges[x, z] = EdgeKind.Open;

                Landmarks.Connect(L, placement, rooms);
                foreach (var r in L.rooms) r.color = r.id;
                DistrictGenerator.PlaceFixtures(L, new System.Random(7));
                Landmarks.Decorate(L, placement, rooms);
                DistrictGenerator.AssignColors(L);
                for (int i = 0; i < L.props.Count; i++) { var p = L.props[i]; p.id = WorldGenerator.PropId(L.coord, 0, i); L.props[i] = p; }
            }
            finally { CustomRoomStamp.Override = previous; }
            return L;
        }
    }
}
