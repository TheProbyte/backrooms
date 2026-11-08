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
    /// <item>Storage rooms: a room cleared out and lined with empty shelving (solid shelves: boxes go on them), flagged
    /// <see cref="Room.leaseStorage"/>: where Store contracts' boxes must end up.</item>
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
                return;
            }
            // (About six of each in the core's 16 districts, three more in each wing.)
            const double pStore = 0.38, pSeam = 0.38;
            if (rng.NextDouble() < pStore) Storage(L, rng);
            if (rng.NextDouble() < pSeam) Seam(L, rng);
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
            best.leaseStorage = true;
            Light(L, best);
            // Cleared out: what the offices left goes (pinned fittings and plates stay).
            L.props.RemoveAll(p => !p.pinned && rc.Contains(new Vector2Int(Mathf.FloorToInt(p.position.x / C), Mathf.FloorToInt(p.position.y / C))));
            // Empty shelving along the walls (a gap by each door), solid shelves to put the boxes on.
            var size = new Vector3(1.1f, 2.0f, 0.46f);
            for (int s = 0; s < 4; s++)
            {
                bool alongZ = s < 2;
                int n = alongZ ? rc.height : rc.width;
                int fixedC = s == 0 ? rc.xMin : s == 1 ? rc.xMax - 1 : s == 2 ? rc.yMin : rc.yMax - 1;
                int a0 = alongZ ? rc.yMin : rc.xMin;
                for (int i = 0; i < n; i++)
                {
                    int x = alongZ ? fixedC : a0 + i, z = alongZ ? a0 + i : fixedC;
                    if (L.EdgeOf(x, z, s) != EdgeKind.Wall || DistrictGenerator.CellHasDoor(L, x, z)) continue;
                    var pos = OnWall(x, z, s, size.z + 0.02f, 0.5f);
                    L.props.Add(new Prop { kind = PropKind.Shelving, position = pos, yaw = Yaw(s), size = size, variant = rng.Next(1 << 16) | StorageShelf, roomColor = best.color, pinned = true });
                }
            }
            // A STORAGE sign on the first free wall cell.
            for (int s = 0; s < 4; s++)
            {
                int x = s == 1 ? rc.xMax - 1 : rc.xMin + rc.width / 2, z = s == 3 ? rc.yMax - 1 : s == 2 ? rc.yMin : rc.yMin + rc.height / 2;
                if (s == 0) x = rc.xMin;
                if (L.EdgeOf(x, z, s) != EdgeKind.Wall || DistrictGenerator.CellHasDoor(L, x, z)) continue;
                var pos = OnWall(x, z, s, 0f, 0.5f);               // on the wall over the shelving's top
                L.props.Add(new Prop { kind = PropKind.Fitting, model = "LeaseStorageSign", position = pos, yaw = Yaw(s), elevation = 2.08f, pinned = true, roomColor = best.color });
                break;
            }
        }

        // ---------------- Seams ----------------

        static void Seam(OfficeLayout L, System.Random rng)
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
            // The tear in the middle of its cell's wall; room cleared in front of it (where a chute would go, and boxes).
            var at = OnWall(cx, cz, side, 0f, 0.5f);
            var along = Right(side);
            var fwd = Fwd(side);
            var mid = at + fwd * C;
            L.props.RemoveAll(p => !p.pinned && Mathf.Abs(Vector2.Dot(p.position - mid, along)) < 1.9f && Mathf.Abs(Vector2.Dot(p.position - mid, fwd)) < 1.3f);
            int kind = rng.Next(Kinds.Length);
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
