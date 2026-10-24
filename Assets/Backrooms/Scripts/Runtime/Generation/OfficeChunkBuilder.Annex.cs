using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>Where an elevator stands, in the owning ground-storey chunk's metres (see <see cref="ElevatorSystem"/>).</summary>
    public class ElevatorSpec
    {
        public ulong id;
        public Vector3 door;          // centre of the doorway in the wall plane, at the ground storey's floor
        public Vector3 outward;       // unit, from the shaft into the lobby
        public float pitch;           // height of the upper storey
        public int buttonSide;        // which side of the door the call button is on (+1 / -1, along the system's right axis)
    }

    /// <summary>Stairs and elevator wing of an atrium (see <see cref="AtriumAnnex"/>): steps, sloped ceiling, collision, elevator data.</summary>
    public partial class OfficeChunkBuilder
    {
        readonly List<ElevatorSpec> _elevators = new List<ElevatorSpec>();

        static Vector2 StairUV(Vector3 w, Vector3 n) =>
            Mathf.Abs(n.y) > 0.5f ? MeshWriter.FloorUV(w, n) : new Vector2((w.x + w.z) / 4f, w.y / 4f);

        void BuildAnnex()
        {
            _elevators.Clear();
            if (_L.storey != 0) return;
            foreach (var lm in _L.landmarks)
            {
                if (!AtriumAnnex.TryGet(lm, _L.storeyPitch, out var a)) continue;
                int ox = lm.district.x * WorldGenSettings.DistrictCells - _L.coord.x * OfficeBiome.ChunkCells;
                int oz = lm.district.y * WorldGenSettings.DistrictCells - _L.coord.y * OfficeBiome.ChunkCells;
                if (a.stairs) BuildStairs(a, ox, oz);
                if (a.elevator) CollectElevator(lm, a, ox, oz);
            }
        }

        /// <summary>Steps (visual boxes and collision) and the sloping ceiling of the stair hall, cell by cell so each chunk builds its own part.</summary>
        void BuildStairs(AtriumAnnex.Plan a, int ox, int oz)
        {
            Vector3 AP(float u, float v, float y) { var p = a.f.Point(u, v); return new Vector3(p.x + ox * C, y, p.y + oz * C); }
            int frontBit = a.f.ux.x != 0 ? 1 : 16;                                    // the riser faces -u
            float start = a.StairStart, end = a.StairEnd, tread = a.Tread, riser = a.Riser;
            var ceilMat = _a.ceilingPlaster ? _a.ceilingPlaster : _a.ceilingTile;
            for (int cu = 0; cu < a.HallCells; cu++)
            for (int cv = 0; cv < AtriumAnnex.BandS; cv++)
            {
                var dc = a.f.Cell(cu, cv);
                int lx = dc.x + ox, lz = dc.y + oz;
                if (!_L.InChunk(lx, lz)) continue;
                uint cm = CellMask(lx, lz);
                float u0 = cu * C, u1 = u0 + C, v0 = cv * C, v1 = v0 + C;

                float lo = Mathf.Max(u0, start), hi = Mathf.Min(u1, end);
                if (hi - lo > 0.002f)
                {
                    int first = Mathf.FloorToInt((lo - start) / tread + 0.001f);
                    int last = Mathf.Min(a.Steps - 1, Mathf.CeilToInt((hi - start) / tread - 0.001f) - 1);
                    for (int i = first; i <= last; i++)
                    {
                        float s = start + i * tread, e = s + tread;
                        float ca = Mathf.Max(s, u0), cb = Mathf.Min(e, u1);
                        if (cb - ca < 0.002f) continue;
                        float yb = i * riser, yt = (i + 1) * riser;
                        Vector3 p0 = AP(ca, v0, yb), p1 = AP(cb, v1, yt);
                        Vector3 centre = (p0 + p1) * 0.5f, size = new Vector3(Mathf.Abs(p1.x - p0.x), Mathf.Abs(p1.y - p0.y), Mathf.Abs(p1.z - p0.z));
                        WMask(_a.carpet, cm).Box(centre, size, 8 | (ca <= s + 0.001f ? frontBit : 0), StairUV);
                        _boxes.Add((new Vector3(centre.x, yt * 0.5f, centre.z), new Vector3(size.x, yt, size.z)));
                    }
                }

                // Handrail along the outer wall of this cell, parallel to the stairs, with a bracket per cell.
                if (hi - lo > 0.002f)
                {
                    float vr = cv == 0 ? 0.1f : 2f * C - 0.1f, vWall = cv == 0 ? 0f : 2f * C;
                    float k = riser / tread;
                    Vector3 pa = AP(lo, vr, (lo - start) * k + 0.95f), pb = AP(hi, vr, (hi - start) * k + 0.95f);
                    Vector3 dir = pb - pa;
                    WMask(_a.lampMetal, cm).Box((pa + pb) * 0.5f, new Vector3(0.04f, 0.04f, dir.magnitude + 0.04f), Quaternion.LookRotation(dir.normalized), 63, MeshWriter.MetreUV);
                    float um = (lo + hi) * 0.5f, ym = (um - start) * k + 0.95f;
                    Vector3 b0 = AP(um - 0.02f, vWall, ym - 0.05f), b1 = AP(um + 0.02f, vr, ym + 0.01f);
                    WMask(_a.lampMetal, cm).Box((b0 + b1) * 0.5f, new Vector3(Mathf.Abs(b1.x - b0.x), Mathf.Abs(b1.y - b0.y), Mathf.Abs(b1.z - b0.z)), 63, MeshWriter.MetreUV);
                }

                // Sloping ceiling: the normal ceiling at the foot, rising with the stairs to meet the upper storey's ceiling.
                float y0 = a.CeilingAt(u0), y1 = a.CeilingAt(u1);
                Vector3 A = AP(u0, v0, y0), B = AP(u1, v0, y1), Cc = AP(u1, v1, y1), D = AP(u0, v1, y0);
                Vector3 n = Vector3.Cross(B - A, D - A).normalized;
                if (n.y > 0f) n = -n;
                WMask(ceilMat, cm).Quad(A, B, Cc, D, n, MeshWriter.MetreUV);
            }
        }

        void CollectElevator(in LandmarkPlacement lm, in AtriumAnnex.Plan a, int ox, int oz)
        {
            // Owned by the chunk the shaft's centre lies in (the door line may lie on a chunk border).
            var sc = a.f.Point((a.length - 1) * C, 3.5f * C);
            float cx = sc.x + ox * C, cz = sc.y + oz * C;
            if (cx < 0f || cz < 0f || cx >= OfficeBiome.ChunkSize || cz >= OfficeBiome.ChunkSize) return;
            var dp = a.f.Point((a.length - AtriumAnnex.ShaftCells) * C, 3.5f * C);
            var outDir = a.f.Dir(-1, 0);
            _elevators.Add(new ElevatorSpec
            {
                id = AtriumAnnex.ElevatorId(lm), door = new Vector3(dp.x + ox * C, 0f, dp.y + oz * C),
                outward = new Vector3(outDir.x, 0f, outDir.y), pitch = _L.storeyPitch, buttonSide = (lm.seed & 1) == 0 ? 1 : -1,
            });
        }
    }
}
