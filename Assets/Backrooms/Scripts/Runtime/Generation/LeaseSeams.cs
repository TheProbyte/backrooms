using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// A contract floor's working parts (2026-11-07, Docs/ContractFloorsPlan.md), placed by the generator in its districts:
    /// <list type="bullet">
    /// <item>Seams: places where the wall has torn and the real world shows through (the orientation video: "out there, the
    /// walls get thin"), each with an intake chute beside it that goods come out of, or are sent back through. A seam is a
    /// pinned wall fitting "SeamCrack" whose variant is its kind (<see cref="Kinds"/>); Interaction.Seam makes it work.</item>
    /// <item>Old storerooms: a room cleared out, flagged <see cref="Room.leaseStorage"/>, a knocked-over shelf or two lying
    /// in it (since 2026-11-08 storage is wherever you put shelves: Interaction.Shelves).</item>
    /// </list>
    /// More of both on a small floor, fewer a district on a big one (about 3, 5 and 8 seams). Never in the lift's district.
    /// </summary>
    public static class LeaseSeams
    {
        const float C = OfficeBiome.CellSize, T = OfficeBiome.WallThickness;

        /// <summary>The other sides a seam opens onto (variant 0-3): their names on the board and their sound.</summary>
        public static readonly string[] Kinds = { "WAREHOUSE", "PRINT SHOP", "OFFICE SUPPLY", "FURNITURE STORE" };

        /// <summary>The intake chute, beside the crack along the wall (m, to the crack's right as you face it).</summary>
        public const float ChuteOffset = 1.0f;
        /// <summary>The storage shelving's variant bit: empty, with solid shelves (OfficeChunkBuilder).</summary>
        public const int StorageShelf = 1 << 17;

        public static void Place(OfficeLayout L, Vector2Int district, System.Random rng)
        {
            int slot = LeasePlan.FloorOf(district);
            if (slot < 0) return;
            if (LeasePlan.IsCentre(district))
            {
                // The lift's arrival hall: always lit (it is where every shift starts).
                if (L.roomOf[6, 4] >= 0) Light(L, L.RoomAt(6, 4));
                Starter(L);                                         // the starter shelf's wall and its outlet
                return;
            }
            // (About six of each in the core's 16 districts, three more in each wing.)
            const double pStore = 0.38, pSeam = 0.38;
            if (rng.NextDouble() < pStore) Storage(L, rng);
            bool seam = rng.NextDouble() < pSeam;
            // The first order's seam: always there, beside the lift's district, as near the lift as a wall allows.
            if (district == FirstSeamDistrict(slot)) Seam(L, rng, FirstKind, LiftIn(district, slot));
            else if (seam) Seam(L, rng);
        }

        // ---------------- The first seam (2026-11-08) ----------------

        /// <summary>The kind of the first seam: the WAREHOUSE, TALLMORE's (WorkOrders' FIRST STORAGE; clients and kinds
        /// share their numbers).</summary>
        public const int FirstKind = 0;

        /// <summary>
        /// The district with the first seam (the owner, 2026-11-08: "how you supposed to find what the first contract asks
        /// for?"): the one west or south of the lift's (the lift hall is in its south-west corner, so both are close),
        /// picked by the floor. Its seam is a WAREHOUSE on the wall nearest the lift, and the company's tape on the floor
        /// leads there from the lift (Interaction.GuideTape). Every other seam is still found by exploring.
        /// </summary>
        public static Vector2Int FirstSeamDistrict(int slot) => LeasePlan.Centre(slot) + ((slot * 7 + 3) % 2 == 0 ? Vector2Int.left : Vector2Int.down);

        /// <summary>Is this the first seam (the taped one)?</summary>
        public static bool IsFirst(SeamInfo s, int slot)
        {
            float size = WorldGenSettings.DistrictCells * C;
            var d = new Vector2Int(Mathf.FloorToInt(s.position.x / size), Mathf.FloorToInt(s.position.z / size));
            return s.kind == FirstKind && slot >= 0 && d == FirstSeamDistrict(slot);
        }

        /// <summary>Where you step out of the lift (world m).</summary>
        public static Vector3 LiftDoor(int slot)
        {
            var r = CustomRooms.OverflowRooms.WorldLiftRect;
            float x = r.x * C + (CustomRooms.OverflowRooms.WorldDoorFrom + CustomRooms.OverflowRooms.WorldDoorTo) * 0.5f, z = r.y * C - 1.8f;
            return LeasePlan.DistrictOrigin(LeasePlan.Centre(slot)) + new Vector3(x, 0f, z);
        }

        /// <summary>The lift's door in a district's own metres (x, z).</summary>
        static Vector2 LiftIn(Vector2Int district, int slot)
        {
            var p = LiftDoor(slot) - LeasePlan.DistrictOrigin(district);
            return new Vector2(p.x, p.z);
        }

        // ---------------- Storage ----------------

        static void Storage(OfficeLayout L, System.Random rng)
        {
            Room best = null; double bestScore = 0;
            foreach (var r in L.rooms)
            {
                if (r.landmark != null || r.custom || r.home || r.special != GoodRoom.None || r.noFloor || r.ramp != 0 || Mathf.Abs(r.floor) > 0.01f) continue;
                if (r.kind != RoomKind.Office && r.kind != RoomKind.BigOffice && r.kind != RoomKind.Conference && r.kind != RoomKind.Lounge) continue;
                int area = r.rect.width * r.rect.height, minDim = Mathf.Min(r.rect.width, r.rect.height);
                if (area < 12 || minDim < 3 || area > 80) continue;
                double score = area * (0.6 + rng.NextDouble());
                if (score > bestScore) { bestScore = score; best = r; }
            }
            if (best == null) return;
            var rc = best.rect;
            // (2026-11-08, Docs/GameDesign.md section 8: storage is wherever you put shelves, so there are no storage rooms
            // now.) An old storeroom: cleared out, its lights left as they were, a knocked-over shelf or two lying in it
            // (Interaction.Shelves puts them out: FoundSpots) for whoever finds it to stand up again.
            best.leaseStorage = true;
            L.props.RemoveAll(p => !p.pinned && rc.Contains(new Vector2Int(Mathf.FloorToInt(p.position.x / C), Mathf.FloorToInt(p.position.y / C))));
        }

        // ---------------- The starter shelf (2026-11-08) ----------------

        /// <summary>
        /// The lift's district: a wall cell of the arrival hall for the starter shelf and the wall cell beside it, where its
        /// outlet is (the desk lamp plugs in there). Deterministic from the layout, so the generator (which puts the outlet
        /// in) and Interaction.Contracts (which puts the shelf and lamp out) agree. Kept clear of the lift's gate.
        /// </summary>
        static bool StarterCells(OfficeLayout L, out int x, out int z, out int side, out int ox, out int oz)
        {
            x = z = side = ox = oz = 0;
            if (L.roomOf[6, 4] < 0) return false;
            var room = L.RoomAt(6, 4);
            var rc = room.rect;
            var gate = new Vector2((CustomRooms.OverflowRooms.WorldLiftRect.xMin + 1.5f) * C, CustomRooms.OverflowRooms.WorldLiftRect.yMin * C);
            for (int s = 0; s < 4; s++)
            {
                bool alongZ = s < 2;
                int n = alongZ ? rc.height : rc.width;
                int fixedC = s == 0 ? rc.xMin : s == 1 ? rc.xMax - 1 : s == 2 ? rc.yMin : rc.yMax - 1;
                int a0 = alongZ ? rc.yMin : rc.xMin;
                for (int i = 0; i + 1 < n; i++)
                {
                    int cx = alongZ ? fixedC : a0 + i, cz = alongZ ? a0 + i : fixedC;
                    int nx = alongZ ? fixedC : a0 + i + 1, nz = alongZ ? a0 + i + 1 : fixedC;
                    if (L.roomOf[cx, cz] != L.roomOf[6, 4] || L.roomOf[nx, nz] != L.roomOf[6, 4]) continue;
                    if (L.EdgeOf(cx, cz, s) != EdgeKind.Wall || DistrictGenerator.CellHasDoor(L, cx, cz)) continue;
                    if (L.EdgeOf(nx, nz, s) != EdgeKind.Wall || DistrictGenerator.CellHasDoor(L, nx, nz)) continue;
                    if (Vector2.Distance(OnWall(cx, cz, s, 0f, 0.5f), gate) < 4.5f) continue;
                    x = cx; z = cz; side = s; ox = nx; oz = nz;
                    return true;
                }
            }
            return false;
        }

        static void Starter(OfficeLayout L)
        {
            if (!StarterCells(L, out int x, out int z, out int s, out int ox, out int oz)) return;
            var shelfCell = new Vector2Int(x, z);
            var outCell = new Vector2Int(ox, oz);
            // Nothing else on the shelf's wall; the outlet low on the next cell's wall, live.
            L.plates.RemoveAll(p => (p.cell == shelfCell || p.cell == outCell) && p.side == s);
            L.plates.Add(new WallPlate { cell = outCell, side = s, t = 0.5f, height = 0.3f, isSwitch = false, live = true, roomColor = L.RoomAt(x, z).color });
            var mid = OnWall(x, z, s, 0f, 0.5f) + Fwd(s) * 0.8f;
            L.props.RemoveAll(p => !p.pinned && (p.position - mid).sqrMagnitude < 1.6f * 1.6f);
        }

        /// <summary>The starter shelf (its middle at the foot, district metres, against the wall), which way it faces, and its
        /// outlet's place on the wall.</summary>
        public static bool StarterSpot(OfficeLayout L, out Vector2 shelf, out float yaw, out Vector2 outlet)
        {
            shelf = outlet = default; yaw = 0f;
            if (!StarterCells(L, out int x, out int z, out int s, out int ox, out int oz)) return false;
            shelf = OnWall(x, z, s, 0.46f + 0.02f, 0.5f);
            outlet = OnWall(ox, oz, s, 0f, 0.5f);
            yaw = Yaw(s);
            return true;
        }

        /// <summary>Where the knocked-over shelves of a district lie (district metres): one or two in each old storeroom,
        /// lying across its middle, and which way each was facing when it stood. Deterministic from the district.</summary>
        public static void FoundSpots(OfficeLayout L, Vector2Int district, List<(Vector2 at, float yaw)> into)
        {
            var rng = new System.Random(district.x * 7349 + district.y * 2069 + 0x5E1F);
            foreach (var r in L.rooms)
            {
                if (!r.leaseStorage) continue;
                var c = r.rect.center * C + new Vector2((float)(rng.NextDouble() - 0.5), (float)(rng.NextDouble() - 0.5)) * 0.6f;
                float yaw = rng.Next(4) * 90f + (float)(rng.NextDouble() - 0.5) * 30f;
                into.Add((c, yaw));
                // Now and then a second one, fallen beside the first (lying 1.1 m wide, side by side with a gap).
                if (rng.NextDouble() < 0.4 && Mathf.Min(r.rect.width, r.rect.height) >= 4)
                {
                    float a = yaw * Mathf.Deg2Rad;
                    var right = new Vector2(Mathf.Cos(a), -Mathf.Sin(a));
                    into.Add((c + right * 1.35f, yaw + (float)(rng.NextDouble() - 0.5) * 12f));
                }
            }
        }

        // ---------------- Seams ----------------

        /// <param name="kind">Its kind, or -1 for any.</param>
        /// <param name="near">If set, the spot nearest this point (district m) rather than one at random.</param>
        static void Seam(OfficeLayout L, System.Random rng, int kind = -1, Vector2? near = null)
        {
            var spots = new List<(int x, int z, int s, Room r)>();
            foreach (var r in L.rooms)
            {
                if (r.landmark != null || r.custom || r.noFloor || r.kind == RoomKind.Crawl || r.leaseStorage || Mathf.Abs(r.floor) > 0.01f || r.ramp != 0) continue;
                if (Mathf.Min(r.rect.width, r.rect.height) < 3) continue;          // room in front for the boxes
                var rc = r.rect;
                for (int s = 0; s < 4; s++)
                {
                    bool alongZ = s < 2;
                    int n = alongZ ? rc.height : rc.width;
                    int fixedC = s == 0 ? rc.xMin : s == 1 ? rc.xMax - 1 : s == 2 ? rc.yMin : rc.yMax - 1;
                    int a0 = alongZ ? rc.yMin : rc.xMin;
                    for (int i = 0; i + 2 < n; i++)
                    {
                        bool ok = true;
                        for (int k = 0; k < 3 && ok; k++)
                        {
                            int x = alongZ ? fixedC : a0 + i + k, z = alongZ ? a0 + i + k : fixedC;
                            ok = L.EdgeOf(x, z, s) == EdgeKind.Wall && !DistrictGenerator.CellHasDoor(L, x, z);
                        }
                        if (!ok) continue;
                        int mx = alongZ ? fixedC : a0 + i + 1, mz = alongZ ? a0 + i + 1 : fixedC;
                        // The tear is a way through (a noclip door): not on a chunk border (both chunks would own the wall),
                        // and no doorway or plate on the wall's other side.
                        int line = s == 0 ? mx : s == 1 ? mx + 1 : s == 2 ? mz : mz + 1;
                        if (line % OfficeBiome.ChunkCells == 0) continue;
                        var behind = new Vector2Int(mx, mz) + Step(s);
                        if (behind.x >= 0 && behind.y >= 0 && behind.x < L.N && behind.y < L.N && DistrictGenerator.CellHasDoor(L, behind.x, behind.y)) continue;
                        spots.Add((mx, mz, s, r));
                    }
                }
            }
            if (spots.Count == 0) return;
            var (cx, cz, side, room) = spots[rng.Next(spots.Count)];
            if (near.HasValue)
            {
                float best = float.MaxValue;
                foreach (var sp in spots)
                {
                    float d = (OnWall(sp.x, sp.z, sp.s, 0f, 0.5f) - near.Value).sqrMagnitude;
                    if (d < best) { best = d; (cx, cz, side, room) = sp; }
                }
            }
            // The tear in the middle of its cell's wall; room cleared in front of it (where a chute would go, and boxes).
            var at = OnWall(cx, cz, side, 0f, 0.5f);
            var along = Right(side);
            var fwd = Fwd(side);
            var mid = at + fwd * C;
            L.props.RemoveAll(p => !p.pinned && Mathf.Abs(Vector2.Dot(p.position - mid, along)) < 1.9f && Mathf.Abs(Vector2.Dot(p.position - mid, fwd)) < 1.3f);
            int rolled = rng.Next(Kinds.Length);
            if (kind < 0) kind = rolled;
            L.props.Add(new Prop { kind = PropKind.Fitting, model = "SeamCrack", position = at, yaw = Yaw(side), variant = kind, pinned = true, roomColor = room.color });
            // The way through: a noclip door the tear's size, into the room of its kind (SeamPlan).
            var cell = new Vector2Int(cx, cz);
            NoclipDoors.Frame(L, cell, side, out var pos, out var nrm);
            SeamPlan.Tear(kind, out var tp, out var tn);
            L.noclipDoors.Add(new NoclipDoor
            {
                cell = cell, side = side, position = pos, normal = nrm, seam = true, resolved = true,
                partnerDistrict = SeamPlan.District, partnerPosition = tp, partnerNormal = tn,
                id = WorldGenerator.PropId(L.coord, L.storey, 0xE400 + L.noclipDoors.Count),
            });
            L.plates.RemoveAll(p => p.cell == cell && p.side == side);
            // The wall's other face is taped too (NoclipDoorMarking.DuctTape): nothing on it there.
            var back = cell + Step(side);
            L.plates.RemoveAll(p => p.cell == back && p.side == (side ^ 1));
        }

        /// <summary>A tear's back face (the same way through, seen from the room behind): its variant is the kind plus this.</summary>
        public const int BackFace = 8;

        static Vector2Int Step(int side) => side == 0 ? Vector2Int.left : side == 1 ? Vector2Int.right : side == 2 ? Vector2Int.down : Vector2Int.up;

        // ---------------- Helpers ----------------

        /// <summary>Every ceiling light in the room on (its lights were already dealt).</summary>
        static void Light(OfficeLayout L, Room r)
        {
            r.lights = LightMode.AllOn;
            for (int i = 0; i < L.fixtures.Count; i++)
            {
                var f = L.fixtures[i];
                if (!r.rect.Contains(f.cell)) continue;
                f.on = true; f.faulty = false;
                L.fixtures[i] = f;
            }
        }

        /// <summary>On a cell side's wall face, depth deep (a thing's middle that deep off the wall), t along it.</summary>
        static Vector2 OnWall(int x, int z, int side, float depth, float t)
        {
            float off = T * 0.5f + depth * 0.5f + 0.004f;
            return side == 0 ? new Vector2(x * C + off, (z + t) * C) : side == 1 ? new Vector2((x + 1) * C - off, (z + t) * C)
                 : side == 2 ? new Vector2((x + t) * C, z * C + off) : new Vector2((x + t) * C, (z + 1) * C - off);
        }

        static float Yaw(int side) => side == 0 ? 90f : side == 1 ? -90f : side == 2 ? 0f : 180f;
        /// <summary>Out of the wall into the room.</summary>
        static Vector2 Fwd(int side) => side == 0 ? Vector2.right : side == 1 ? Vector2.left : side == 2 ? Vector2.up : Vector2.down;
        /// <summary>To the right of someone facing the wall (layout x, z).</summary>
        static Vector2 Right(int side) { var f = Fwd(side); return new Vector2(f.y, -f.x); }

        // ---------------- What a floor has (for the contracts board) ----------------

        public struct SeamInfo { public ulong id; public int kind; public Vector3 position; public float yaw; }

        /// <summary>The seams and storage rooms (world rectangles, m) of a floor: generates its district plans (thread-safe).</summary>
        public static void Survey(WorldGenerator world, int slot, List<SeamInfo> seams, List<Rect> storage)
        {
            foreach (var d in LeasePlan.Districts(slot))            // (the leased parts only: LeasePlan)
            {
                var L = world.District(new Vector3Int(d.x, 0, d.y));
                var o = LeasePlan.DistrictOrigin(d);
                for (int i = 0; i < L.props.Count; i++)
                {
                    var p = L.props[i];
                    if (p.kind != PropKind.Fitting || p.model != "SeamCrack" || p.variant >= BackFace) continue;
                    seams?.Add(new SeamInfo { id = WorldGenerator.PropId(d, 0, i), kind = p.variant, position = o + new Vector3(p.position.x, 0f, p.position.y), yaw = p.yaw });
                }
                if (storage != null)
                    foreach (var room in L.rooms)
                        if (room.leaseStorage)
                            storage.Add(new Rect(o.x + room.rect.xMin * C, o.z + room.rect.yMin * C, room.rect.width * C, room.rect.height * C));
            }
        }
    }
}
