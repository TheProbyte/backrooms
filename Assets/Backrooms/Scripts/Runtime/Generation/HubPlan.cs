using System.Collections.Generic;
using Backrooms.CustomRooms;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The hub (2026-11-07, Docs/HubPlan.md): Overflow's office, the same building in every world. It stands on the ground
    /// storey about 5 km east of the world's start, in a patch of the map (the hub zone, 3 x 3 districts) that is nothing but
    /// the hub sealed in by dark void, so the world's lighting (the light grid covers the ground storey and the one above),
    /// sound and streaming treat it like any other room, and no corridor ever leads there. It is the blueprint
    /// <see cref="OverflowRooms.HubId"/> stamped into the zone's middle district; the chunk builder builds it like any custom
    /// room, in the Overflow look whatever ART STYLE says (zoneName "Overflow": WorldStreamer builds it with the look set to
    /// Overflow). A service lift links it to the world: one cab here, one at the north of the world's spawn hall.
    /// </summary>
    public static class HubPlan
    {
        /// <summary>The district the hub stands in, and the zone round it (districts within one of it) that is the hub's.</summary>
        public static readonly Vector2Int District = new Vector2Int(88, 0);
        /// <summary>The hub's south-west corner in its district (cells): chunks (1..2, 1..2) of the district.</summary>
        public const int OX = 12, OZ = 12;
        public const string ZoneName = "Overflow";
        const float C = OfficeBiome.CellSize;
        const int DC = WorldGenSettings.DistrictChunks;

        /// <summary>Is this district part of the hub zone?</summary>
        public static bool InZone(Vector2Int district) => Mathf.Abs(district.x - District.x) <= 1 && Mathf.Abs(district.y - District.y) <= 1;
        public static bool InZone(Vector3 p) => InZone(WorldGenSettings.DistrictOf(new Vector2Int(Mathf.FloorToInt(p.x / OfficeBiome.ChunkSize), Mathf.FloorToInt(p.z / OfficeBiome.ChunkSize))));
        /// <summary>Is this chunk column in the hub zone?</summary>
        public static bool ChunkInZone(Vector2Int chunk) => InZone(WorldGenSettings.DistrictOf(chunk));
        /// <summary>Is a point in the hub (its zone)?</summary>
        public static bool InHub(Vector3 p) => ChunkInZone(new Vector2Int(Mathf.FloorToInt(p.x / OfficeBiome.ChunkSize), Mathf.FloorToInt(p.z / OfficeBiome.ChunkSize)));

        public static RectInt Rect => new RectInt(OX, OZ, OverflowRooms.HubW, OverflowRooms.HubD);

        /// <summary>The chunks (x, 0, z) the hub stands in.</summary>
        public static IEnumerable<Vector3Int> Chunks()
        {
            int N = OfficeBiome.ChunkCells;
            var r = Rect;
            for (int cx = r.xMin / N; cx <= (r.xMax - 1) / N; cx++)
            for (int cz = r.yMin / N; cz <= (r.yMax - 1) / N; cz++)
                yield return new Vector3Int(District.x * DC + cx, 0, District.y * DC + cz);
        }

        /// <summary>Is this chunk one the hub stands in?</summary>
        public static bool IsHubChunk(Vector3Int k)
        {
            if (k.y != 0) return false;
            foreach (var c in Chunks()) if (c == k) return true;
            return false;
        }

        /// <summary>A point given in hub-blueprint metres (x, z), height above the hub's floor, in the world.</summary>
        public static Vector3 World(float x, float z, float y = 0f) =>
            new Vector3((District.x * WorldGenSettings.DistrictCells + OX) * C + x, y, (District.y * WorldGenSettings.DistrictCells + OZ) * C + z);

        /// <summary>Where player number i (0 the host, 1-3 friends) wakes, and which way they face.</summary>
        public static void WakePose(int i, out Vector3 pos, out float yaw)
        {
            var s = OverflowRooms.WakeSpots[Mathf.Clamp(i, 0, OverflowRooms.WakeSpots.Length - 1)];
            pos = World(s.x, s.y, 0.05f);
            yaw = 0f;
        }

        /// <summary>
        /// The lift cabs' frames: the middle of the cab's floor at its gate, facing out of the cab (into the hall). The hub's
        /// gate is on the line z = 3 of the hub, opening north; the world's on the south line of its shaft, opening south.
        /// </summary>
        public static Pose HubCab => new Pose(World((OverflowRooms.HubDoorFrom + OverflowRooms.HubDoorTo) * 0.5f, 3 * C), Quaternion.identity);
        public static Pose WorldCab
        {
            get
            {
                // In the middle of the current contract's floor (LeasePlan), else district (0, 0) (the world's start).
                var r = OverflowRooms.WorldLiftRect;
                var o = LeasePlan.Current >= 0 ? LeasePlan.DistrictOrigin(LeasePlan.Centre(LeasePlan.Current)) : Vector3.zero;
                float x = r.x * C + (OverflowRooms.WorldDoorFrom + OverflowRooms.WorldDoorTo) * 0.5f, z = r.y * C;
                return new Pose(o + new Vector3(x, 0f, z), Quaternion.Euler(0f, 180f, 0f));
            }
        }

        // ---------------- The plan ----------------

        /// <summary>A district of the hub zone: the hub in its middle district (ground storey), sealed void everywhere else.</summary>
        public static OfficeLayout Generate(WorldGenSettings set, int seed, Vector2Int district, int storey)
        {
            int n = WorldGenSettings.DistrictCells;
            var L = new OfficeLayout(n, district, storey) { storeyPitch = set.storeyPitch, zoneName = ZoneName, biomeName = "Offices" };
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++) L.roomOf[x, z] = -1;
            var rect = Rect;
            var voids = new List<Room>();
            Room Void(RectInt r)
            {
                var room = DistrictGenerator.MakeRoom(L, r, RoomKind.Hallway);
                room.lights = LightMode.Dark; room.custom = true; room.noDoors = true; room.painted = true; room.wallFinish = WallFinish.PaintedPlaster;
                voids.Add(room);
                return room;
            }
            var rooms = new List<Room>();
            LandmarkPlacement placement = default;
            if (district != District || storey != 0) Void(new RectInt(0, 0, n, n));
            else
            {
                Void(new RectInt(0, 0, n, rect.yMin));
                Void(new RectInt(0, rect.yMax, n, n - rect.yMax));
                Void(new RectInt(0, rect.yMin, rect.xMin, rect.height));
                Void(new RectInt(rect.xMax, rect.yMin, n - rect.xMax, rect.height));
                placement = new LandmarkPlacement
                {
                    name = "Overflow", kind = LandmarkKind.Custom, district = district, rect = rect, core = rect, storeys = 1,
                    seed = 0x0F10, blueprint = OverflowRooms.HubId, variant = 0, rot = 0,
                };
                var specs = new List<Landmarks.RoomSpec>();
                CustomRoomStamp.Rooms(placement, specs);
                foreach (var spec in specs)
                {
                    var room = DistrictGenerator.MakeRoom(L, spec.rect, spec.kind);
                    room.ceiling = spec.ceiling; room.painted = spec.painted; room.lights = spec.lights;
                    room.wallFinish = spec.wallFinish; room.floorFinish = spec.floorFinish;
                    room.noDoors = true; room.landmark = "Overflow"; room.custom = true;
                    rooms.Add(room);
                }
                L.landmarks.Add(placement);
            }
            // Walls wherever rooms differ and all round; the void is one open dark space, never reached.
            bool IsVoid(int id) => voids.Exists(v => v.id == id);
            for (int x = 0; x <= n; x++)
            for (int z = 0; z < n; z++)
                L.vEdges[x, z] = x == 0 || x == n || (L.roomOf[x - 1, z] != L.roomOf[x, z] && !(IsVoid(L.roomOf[x - 1, z]) && IsVoid(L.roomOf[x, z]))) ? EdgeKind.Wall : EdgeKind.Open;
            for (int x = 0; x < n; x++)
            for (int z = 0; z <= n; z++)
                L.hEdges[x, z] = z == 0 || z == n || (L.roomOf[x, z - 1] != L.roomOf[x, z] && !(IsVoid(L.roomOf[x, z - 1]) && IsVoid(L.roomOf[x, z]))) ? EdgeKind.Wall : EdgeKind.Open;
            if (rooms.Count > 0)
            {
                // The blueprint's own walls and doorways (nothing leads out: the void counts as unwalkable, being custom).
                CustomRoomStamp.Connect(L, placement, rooms);
                foreach (var r in L.rooms) r.color = r.id;
                CustomRoomStamp.Decorate(L, placement, rooms);
            }
            else foreach (var r in L.rooms) r.color = r.id;
            DistrictGenerator.AssignColors(L);
            for (int i = 0; i < L.props.Count; i++) { var p = L.props[i]; p.id = WorldGenerator.PropId(L.coord, storey, i); L.props[i] = p; }
            return L;
        }
    }
}
