using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The Poolrooms' curves: wide round arches over open spans, rounded inside corners, free-standing curved walls,
    /// and the spiral stair towers that lead to the floor above. All tiled; tiles follow each curve (u = arc length).
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        // ---------------- Arches ----------------

        /// <summary>
        /// One side of one edge of an arched span: the wall face above the curve up to this room's ceiling, and this
        /// side's half of the curved underside. The curve is a half circle on the full width of the span where the room
        /// is tall enough, else a flatter half ellipse springing at 1.8 m; its crown is 0.45 m under the lower ceiling.
        /// Both rooms (and both chunks of a split span) compute the same curve.
        /// </summary>
        void BigArchFaces(bool vertical, int line, int along, int index, int length, float f, Vector3 n, float myCeil, float otherCeil)
        {
            var w = WMask(_archZone ? _archZone : TileMat, 1u);
            float l = line * C;
            float runStart = (along - index) * C + T * 0.5f, runEnd = (along - index + length) * C - T * 0.5f;
            float lower = Mathf.Min(myCeil, otherCeil);
            float apex = Mathf.Max(PoolBiome.HeaderHeight, lower - 0.45f);
            float hw = (runEnd - runStart) * 0.5f, mid = (runStart + runEnd) * 0.5f;
            float spring = Mathf.Max(1.8f, apex - hw), rise = apex - spring;
            float Y(float a) { float u = Mathf.Clamp((a - mid) / hw, -1f, 1f); return spring + rise * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)); }
            Vector3 Slope(float a)
            {
                // Normal of the underside: down at the crown, turning toward the middle of the opening near the piers.
                float u = Mathf.Clamp((a - mid) / hw, -0.999f, 0.999f);
                float dy = -rise * u / (hw * Mathf.Sqrt(1f - u * u));
                var along = vertical ? Vector3.forward : Vector3.right;
                return (along * dy - Vector3.up).normalized;
            }
            Vector3 P(float plane, float at, float y) => vertical ? new Vector3(plane, y, at) : new Vector3(at, y, plane);
            float a0 = Mathf.Max(along * C, runStart), a1 = Mathf.Min((along + 1) * C, runEnd);
            if (a1 - a0 < 0.001f) return;
            const int K = 12;
            for (int k = 0; k < K; k++)
            {
                float s0 = a0 + (a1 - a0) * k / K, s1 = a0 + (a1 - a0) * (k + 1) / K;
                float y0 = Y(s0), y1 = Y(s1);
                if (myCeil > Mathf.Min(y0, y1) + 0.001f)
                    w.Quad(P(f, s0, y0), P(f, s1, y1), P(f, s1, myCeil), P(f, s0, myCeil), n, MeshWriter.MetreUV);
                Vector3 n0 = Slope(s0), n1 = Slope(s1);
                // u along the curve (approximate arc length), v across the wall's thickness.
                float u0 = s0 + (y0 - spring), u1 = s1 + (y1 - spring);
                w.SmoothQuad(P(l, s0, y0), P(f, s0, y0), P(f, s1, y1), P(l, s1, y1), n0, n0, n1, n1,
                    new Vector2(u0, l), new Vector2(u0, l + (f - l)), new Vector2(u1, l + (f - l)), new Vector2(u1, l));
            }
        }

        // ---------------- Rounded inside corners ----------------

        const float FilletRadius = 0.9f;

        /// <summary>
        /// Where two walls of a room meet at a right angle, a quarter-round of tiles from floor to ceiling. Only in room
        /// corners proper (both walls run on past the corner cell), away from stairs, holes, columns and the stair towers.
        /// </summary>
        void BuildPoolFillets()
        {
            int N = _L.N;
            var tile = WMask(TileMat, 1u);
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                var room = _L.RoomAt(x, z);
                if (!room.Walkable || room.spiral || room.noFloor || room.openAbove || Mathf.Min(room.fullRect.width, room.fullRect.height) < 3) continue;
                if (_L.pool[x, z].gallery > 0f || _L.pool[x, z].solid || FeatureOf(PoolFeatureKind.RoundHall, x, z, out _)) continue;
                tile = WMask(ZoneTile(room), 1u);
                if (StairSideOf(x, z) >= 0 || Info(x, z).pillarNear) continue;
                for (int corner = 0; corner < 4; corner++)
                {
                    int sx = (corner & 1) == 0 ? 0 : 1;        // wall on -X (0) or +X (1)
                    int sz = (corner & 2) == 0 ? 2 : 3;        // wall on -Z (2) or +Z (3)
                    if (_L.EdgeOf(x, z, sx) != EdgeKind.Wall || _L.EdgeOf(x, z, sz) != EdgeKind.Wall) continue;
                    if (HoleAt(true, sx == 0 ? x : x + 1, z, out _) || HoleAt(false, sz == 2 ? z : z + 1, x, out _)) continue;
                    // Both walls go on beyond this cell (a corner of the room, not a short stub).
                    int nx = x + (sx == 0 ? 1 : -1), nz = z + (sz == 2 ? 1 : -1);
                    if (!_L.InChunk(nx, z) || !_L.InChunk(x, nz)) continue;
                    if (_L.roomOf[nx, z] != _L.roomOf[x, z] || _L.EdgeOf(nx, z, sz) != EdgeKind.Wall) continue;
                    if (_L.roomOf[x, nz] != _L.roomOf[x, z] || _L.EdgeOf(x, nz, sx) != EdgeKind.Wall) continue;
                    float dx = sx == 0 ? 1f : -1f, dz = sz == 2 ? 1f : -1f;
                    var cornerPt = new Vector3(sx == 0 ? x * C + T * 0.5f : (x + 1) * C - T * 0.5f, 0f, sz == 2 ? z * C + T * 0.5f : (z + 1) * C - T * 0.5f);
                    var centre = cornerPt + new Vector3(dx * FilletRadius, 0f, dz * FilletRadius);
                    float y0 = (FloorAt(x, z) ?? 0f) - 0.04f, y1 = _L.WallTop(room);
                    const int K = 10;
                    Vector3 prev = default, prevN = default; float prevU = 0f;
                    for (int k = 0; k <= K; k++)
                    {
                        float phi = 0.5f * Mathf.PI * k / K;
                        var dir = new Vector3(-dx * Mathf.Cos(phi), 0f, -dz * Mathf.Sin(phi));
                        var p = centre + dir * FilletRadius;
                        float u = FilletRadius * phi + 0.075f;
                        if (k > 0)
                            tile.SmoothQuad(prev + Vector3.up * y0, p + Vector3.up * y0, p + Vector3.up * y1, prev + Vector3.up * y1, prevN, -dir, -dir, prevN,
                                new Vector2(prevU, y0), new Vector2(u, y0), new Vector2(u, y1), new Vector2(prevU, y1));
                        prev = p; prevN = -dir; prevU = u;
                    }
                    // Colliders: three straight pieces along the curve.
                    for (int k = 0; k < 3; k++)
                    {
                        float p0 = 0.5f * Mathf.PI * k / 3f, p1 = 0.5f * Mathf.PI * (k + 1) / 3f;
                        var a = centre + new Vector3(-dx * Mathf.Cos(p0), 0f, -dz * Mathf.Sin(p0)) * FilletRadius;
                        var b = centre + new Vector3(-dx * Mathf.Cos(p1), 0f, -dz * Mathf.Sin(p1)) * FilletRadius;
                        var d = b - a;
                        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                        var outward = (((a + b) * 0.5f) - centre).normalized;
                        _rotBoxes.Add(((a + b) * 0.5f + outward * 0.05f + Vector3.up * ((y0 + y1) * 0.5f), new Vector3(0.1f, y1 - y0, d.magnitude), yaw));
                    }
                }
            }
        }

        // ---------------- Free-standing curved walls ----------------

        void BuildPoolArcs()
        {
            var tile = WMask(TileMat, 1u);
            foreach (var arc in _L.arcs)
            {
                tile = WMask(ZoneAt(Mathf.FloorToInt(arc.centre.x / C), Mathf.FloorToInt(arc.centre.y / C)), 1u);
                float rIn = arc.radius - T * 0.5f, rOut = arc.radius + T * 0.5f;
                var c = new Vector3(arc.centre.x, 0f, arc.centre.y);
                // Floor: the lowest under it (it stands in the basin where there is one).
                float y0 = 0f;
                for (int k = 0; k <= 8; k++)
                {
                    float a = Mathf.Lerp(arc.from, arc.to, k / 8f) * Mathf.Deg2Rad;
                    var p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * arc.radius;
                    y0 = Mathf.Min(y0, FloorAt(Mathf.FloorToInt(p.x / C), Mathf.FloorToInt(p.z / C)) ?? 0f);
                }
                float y1 = arc.top;
                int K = Mathf.Max(6, Mathf.CeilToInt((arc.to - arc.from) / 5f));
                Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); }
                for (int k = 0; k < K; k++)
                {
                    float d0 = Mathf.Lerp(arc.from, arc.to, k / (float)K), d1 = Mathf.Lerp(arc.from, arc.to, (k + 1) / (float)K);
                    Vector3 e0 = Dir(d0), e1 = Dir(d1);
                    float uo0 = rOut * d0 * Mathf.Deg2Rad, uo1 = rOut * d1 * Mathf.Deg2Rad, ui0 = rIn * d0 * Mathf.Deg2Rad, ui1 = rIn * d1 * Mathf.Deg2Rad;
                    // Outer and inner faces.
                    tile.SmoothQuad(c + e0 * rOut + Vector3.up * y0, c + e1 * rOut + Vector3.up * y0, c + e1 * rOut + Vector3.up * y1, c + e0 * rOut + Vector3.up * y1,
                        e0, e1, e1, e0, new Vector2(uo0, y0), new Vector2(uo1, y0), new Vector2(uo1, y1), new Vector2(uo0, y1));
                    tile.SmoothQuad(c + e1 * rIn + Vector3.up * y0, c + e0 * rIn + Vector3.up * y0, c + e0 * rIn + Vector3.up * y1, c + e1 * rIn + Vector3.up * y1,
                        -e1, -e0, -e0, -e1, new Vector2(-ui1, y0), new Vector2(-ui0, y0), new Vector2(-ui0, y1), new Vector2(-ui1, y1));
                    // Top, when it stops short of the ceiling.
                    tile.Quad(c + e0 * rIn + Vector3.up * y1, c + e1 * rIn + Vector3.up * y1, c + e1 * rOut + Vector3.up * y1, c + e0 * rOut + Vector3.up * y1, Vector3.up, MeshWriter.MetreUV);
                    // Collider piece.
                    var a = c + e0 * arc.radius; var b = c + e1 * arc.radius; var dd = b - a;
                    _rotBoxes.Add(((a + b) * 0.5f + Vector3.up * ((y0 + y1) * 0.5f), new Vector3(T, y1 - y0, dd.magnitude + 0.02f), Mathf.Atan2(dd.x, dd.z) * Mathf.Rad2Deg));
                }
                // The two ends.
                foreach (var (deg, sign) in new[] { (arc.from, -1f), (arc.to, 1f) })
                {
                    var e = Dir(deg);
                    var t = new Vector3(-e.z, 0f, e.x) * sign;          // out of the end, along the curve
                    tile.Quad(c + e * rIn + Vector3.up * y0, c + e * rOut + Vector3.up * y0, c + e * rOut + Vector3.up * y1, c + e * rIn + Vector3.up * y1, t, MeshWriter.MetreUV);
                }
            }
        }

        // ---------------- Spiral stair ----------------

        /// <summary>
        /// A spiral stair in a tower room (storey 0): a tiled column from the floor to the ceiling of the well above,
        /// wedge steps winding up round it (16 a turn, 0.18 m each) with a railing on the open side, ending at a bridge
        /// level with the upper floor that leads to the well's door. Built whole by the chunk the tower stands in.
        /// </summary>
        void BuildSpiral(Room room)
        {
            var tile = WMask(TileMat, 1u);
            var rc = room.rect;
            float cx = (rc.xMin + rc.width * 0.5f) * C, cz = (rc.yMin + rc.height * 0.5f) * C;
            var c = new Vector3(cx, 0f, cz);
            float pitch = _L.storeyPitch, top = pitch + PoolBiome.SpiralUpperCeiling;
            float rC = PoolBiome.SpiralColumn, rO = PoolBiome.SpiralOuter;
            int N = Mathf.RoundToInt(pitch / PoolBiome.SpiralRise);
            float rise = pitch / N, step = PoolBiome.SpiralStepDegrees, half = step * 0.5f + 0.6f;
            const float thick = 0.12f;

            // Column.
            int around = Mathf.Max(8, Mathf.RoundToInt(2f * Mathf.PI * rC / PoolBiome.TileSize));
            const int CS = 24;
            for (int i = 0; i < CS; i++)
            {
                float a0 = i * Mathf.PI * 2f / CS, a1 = (i + 1) * Mathf.PI * 2f / CS;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                float u0 = around * PoolBiome.TileSize * i / CS + 0.075f, u1 = around * PoolBiome.TileSize * (i + 1) / CS + 0.075f;
                tile.SmoothQuad(c + d0 * rC, c + d1 * rC, c + d1 * rC + Vector3.up * top, c + d0 * rC + Vector3.up * top, d0, d1, d1, d0,
                    new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, top), new Vector2(u0, top));
            }
            _capsules.Add((c + Vector3.up * top * 0.5f, rC, top));

            Vector3 Dir(float deg) { float a = deg * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); }
            for (int k = 0; k < N; k++)
            {
                float theta = -(N - 1 - k) * step;                 // the top step points at the landing (+X)
                float yTop = (k + 1) * rise, yBot = yTop - thick;
                const int M = 3;
                for (int j = 0; j < M; j++)
                {
                    float t0 = theta - half + 2f * half * j / M, t1 = theta - half + 2f * half * (j + 1) / M;
                    Vector3 e0 = Dir(t0), e1 = Dir(t1);
                    // Tread, underside, and the curved outer edge.
                    tile.Quad(c + e0 * rC + Vector3.up * yTop, c + e1 * rC + Vector3.up * yTop, c + e1 * rO + Vector3.up * yTop, c + e0 * rO + Vector3.up * yTop, Vector3.up, MeshWriter.MetreUV);
                    tile.Quad(c + e0 * rC + Vector3.up * yBot, c + e1 * rC + Vector3.up * yBot, c + e1 * rO + Vector3.up * yBot, c + e0 * rO + Vector3.up * yBot, Vector3.down, MeshWriter.MetreUV);
                    tile.SmoothQuad(c + e0 * rO + Vector3.up * yBot, c + e1 * rO + Vector3.up * yBot, c + e1 * rO + Vector3.up * yTop, c + e0 * rO + Vector3.up * yTop,
                        e0, e1, e1, e0, new Vector2(rO * t0 * Mathf.Deg2Rad, yBot), new Vector2(rO * t1 * Mathf.Deg2Rad, yBot), new Vector2(rO * t1 * Mathf.Deg2Rad, yTop), new Vector2(rO * t0 * Mathf.Deg2Rad, yTop));
                }
                // The two radial faces (the riser below and the back edge above).
                foreach (var (deg, sgn) in new[] { (theta - half, -1f), (theta + half, 1f) })
                {
                    var e = Dir(deg); var tn = new Vector3(-e.z, 0f, e.x) * sgn;
                    tile.Quad(c + e * rC + Vector3.up * yBot, c + e * rO + Vector3.up * yBot, c + e * rO + Vector3.up * yTop, c + e * rC + Vector3.up * yTop, tn, MeshWriter.MetreUV);
                }
                // Collider: a box along the step's middle.
                var mid = Dir(theta);
                float rm = (rC + rO) * 0.5f;
                float yaw = Mathf.Atan2(mid.x, mid.z) * Mathf.Rad2Deg;
                _rotBoxes.Add((c + mid * (rm + 0.05f) + Vector3.up * (yTop - thick * 0.5f), new Vector3(2f * rm * Mathf.Sin(half * Mathf.Deg2Rad) + 0.12f, thick, rO - rC), yaw));

                // Railing on the open side (not at the foot, where you step on, nor at the top, where the bridge leaves).
                if (k >= 3 && k < N - 2)
                {
                    float rr = rO - 0.06f;
                    var b = c + mid * rr + Vector3.up * yTop;
                    tile.Box(b + Vector3.up * 0.47f, new Vector3(0.035f, 0.94f, 0.035f), Quaternion.Euler(0f, yaw, 0f), 1 | 2 | 16 | 32, MeshWriter.MetreUV);
                    var nextDir = Dir(theta + step);
                    var a = c + mid * rr + Vector3.up * (yTop + 0.94f);
                    var z = c + nextDir * rr + Vector3.up * (yTop + rise + 0.94f);
                    var seg = z - a;
                    tile.Box((a + z) * 0.5f, new Vector3(0.06f, 0.05f, seg.magnitude + 0.03f), Quaternion.LookRotation(seg, Vector3.up), 63, MeshWriter.MetreUV);
                    _rotBoxes.Add((c + mid * (rr + 0.02f) + Vector3.up * (yTop + 0.5f), new Vector3(2f * rr * Mathf.Sin(step * 0.5f * Mathf.Deg2Rad) + 0.08f, 1.0f, 0.06f), yaw));
                }
            }

            // Bridge from the column to the well's door, level with the upper floor, railed both sides.
            float x0 = cx + rC - 0.05f, x1 = rc.xMax * C - T * 0.5f;
            float zr = (PoolPlan.LandingRow(new RectInt(rc.xMin, rc.yMin, rc.width, rc.height)) + 0.5f) * C;
            var bridgeC = new Vector3((x0 + x1) * 0.5f, pitch - 0.075f, zr);
            var bridgeS = new Vector3(x1 - x0, 0.15f, C - 0.06f);
            tile.Box(bridgeC, bridgeS, 63, MeshWriter.MetreUV);
            _boxes.Add((bridgeC, bridgeS));
            foreach (float side in new[] { -1f, 1f })
            {
                // The stair arrives from the -Z side: that rail starts past the steps.
                float zz = zr + side * (C * 0.5f - 0.05f), rs = side < 0f ? cx + rO + 0.05f : x0;
                if (x1 - rs < 0.3f) continue;
                for (float xx = rs + 0.2f; xx < x1 - 0.1f; xx += 0.45f)
                    tile.Box(new Vector3(xx, pitch + 0.47f, zz), new Vector3(0.035f, 0.94f, 0.035f), 1 | 2 | 16 | 32, MeshWriter.MetreUV);
                tile.Box(new Vector3((rs + x1) * 0.5f, pitch + 0.965f, zz), new Vector3(x1 - rs, 0.05f, 0.06f), 63, MeshWriter.MetreUV);
                _boxes.Add((new Vector3((rs + x1) * 0.5f, pitch + 0.5f, zz), new Vector3(x1 - rs, 1.0f, 0.06f)));
            }
        }
    }
}
