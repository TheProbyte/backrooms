using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>
    /// The small things that make a real office (2026-10-27), seeded like the wear (world seed and cell):
    ///  - Ceiling: square supply diffusers and louvred return grilles in place of tiles, sprinkler heads on a 3 m grid,
    ///    a smoke detector here and there (a red light), cracked tiles and tiles with a corner crumbled away (the space
    ///    above shows; crumbs on the floor below).
    ///  - Walls: scuffs at hip and chair height (more in hallways), nail holes, the paler shape where a picture hung (a
    ///    faint frame of dirt round it), wallpaper peeling at a seam by the ceiling or the baseboard, and now and then an
    ///    exit sign over a doorway.
    ///  - Doors (some): black kick marks low on the push side, grime round the handle and along the edge.
    ///  - Carpet: coffee spills (a dark rim, drips), and an aluminium threshold strip where the floor changes.
    /// Marks are Backrooms/FloorWear quads (one mesh a chunk for floors, one for walls, rebuilt with the walls when one is
    /// broken; doors carry their own); objects are a few dozen triangles in the chunk's own meshes.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Overlay quads (Backrooms/FloorWear): position, tint and strength (colour), profile (uv), its inner edge (uv2).</summary>
        sealed class Overlay
        {
            readonly List<Vector3> _v = new List<Vector3>();
            readonly List<Color32> _c = new List<Color32>();
            readonly List<Vector2> _uv = new List<Vector2>(), _in = new List<Vector2>();
            readonly List<int> _i = new List<int>();
            public int Count => _i.Count;
            public void Clear() { _v.Clear(); _c.Clear(); _uv.Clear(); _in.Clear(); _i.Clear(); }

            public void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3, Color32 col, Vector2 inner)
            {
                int i = _v.Count;
                _v.Add(p0); _v.Add(p1); _v.Add(p2); _v.Add(p3);
                _uv.Add(uv0); _uv.Add(uv1); _uv.Add(uv2); _uv.Add(uv3);
                for (int k = 0; k < 4; k++) { _c.Add(col); _in.Add(inner); }
                _i.Add(i); _i.Add(i + 1); _i.Add(i + 2); _i.Add(i); _i.Add(i + 2); _i.Add(i + 3);
            }

            /// <summary>As Quad, with a colour per corner (a strength that changes along a run of wall).</summary>
            public void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector2 uv3,
                             Color32 c0, Color32 c1, Color32 c2, Color32 c3, Vector2 inner)
            {
                int i = _v.Count;
                _v.Add(p0); _v.Add(p1); _v.Add(p2); _v.Add(p3);
                _uv.Add(uv0); _uv.Add(uv1); _uv.Add(uv2); _uv.Add(uv3);
                _c.Add(c0); _c.Add(c1); _c.Add(c2); _c.Add(c3);
                for (int k = 0; k < 4; k++) _in.Add(inner);
                _i.Add(i); _i.Add(i + 1); _i.Add(i + 2); _i.Add(i); _i.Add(i + 2); _i.Add(i + 3);
            }

            /// <summary>A quad centred at c spanning ±ha along a and ±hb along b, its profile running u0..u1 along a, v0..v1 along b.</summary>
            public void Rect(Vector3 c, Vector3 a, Vector3 b, float ha, float hb, Color32 col, Vector2 inner, float u0 = -1f, float u1 = 1f, float v0 = -1f, float v1 = 1f)
                => Quad(c - a * ha - b * hb, c + a * ha - b * hb, c + a * ha + b * hb, c - a * ha + b * hb,
                        new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), col, inner);

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_v); mesh.SetColors(_c); mesh.SetUVs(0, _uv); mesh.SetUVs(1, _in);
                mesh.SetTriangles(_i, 0, true);
                return mesh;
            }
        }

        readonly Overlay _floorWear = new Overlay(), _wallWear = new Overlay();
        /// <summary>Crumbled tile corners (the corner's middle, chunk-local on the floor; its cell): crumbs below if the floor is clear.</summary>
        readonly List<(Vector3 c, int x, int z)> _crumbs = new List<(Vector3, int, int)>();

        static Material WearMaterial()
        {
            if (s_wearMat) return s_wearMat;
            var sh = Shader.Find("Backrooms/FloorWear");
            if (sh) s_wearMat = new Material(sh) { name = "Wear marks", hideFlags = HideFlags.DontSave };
            return s_wearMat;
        }

        void MakeOverlay(Transform root, GeneratedChunkResources resources, Overlay o, string name)
        {
            if (o.Count == 0 || !WearMaterial()) return;
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = resources.Own(o.ToMesh(name));
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = s_wearMat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.renderingLayerMask = 0xFFFFFFFFu;
        }

        /// <summary>The wall marks again after a wall was broken (their wall may be gone): called from ReplaceWalls.</summary>
        void ReplaceWallWear(GameObject root, GeneratedChunkResources resources)
        {
            var old = root.transform.Find("WallWear");
            if (old) Object.Destroy(old.gameObject);
            MakeOverlay(root.transform, resources, _wallWear, "WallWear");
        }

        // ---------------- Ceiling devices and tiles ----------------

        /// <summary>Sprinkler heads on a world grid (every fifth tile across, every third row), and the odd smoke detector.</summary>
        void CeilingDevices(int x, int z, float h, uint m, float tx0, float tz0, int i)
        {
            int gx = GX(x), gz = GZ(z);
            int tile = Mathf.FloorToInt((_origin.x + tx0 + TileSize * 0.5f) / TileSize);
            var c = new Vector3(tx0 + TileSize * 0.5f, h, tz0 + TileSize * 0.5f);
            if (((tile % 5) + 5) % 5 == 2 && ((gz % 3) + 3) % 3 == 1)
            {
                if (Dice(gx, gz, 200 + i) < 0.9f)
                {
                    // Now and then painted over with the ceiling, or missing its ring (the hole round it shows).
                    float v = Dice(gx, gz, 1310 + i);
                    var metal = WMask(_a.lampMetal ? _a.lampMetal : _a.fixtureHousing, m);
                    if (v < 0.07f) Sprinkler(WMask(_a.ceilingTile, m), c, true);
                    else if (v < 0.15f && MaterialMaps.Plenum)
                    {
                        Sprinkler(metal, c, false);
                        Saw(c, Vector3.down, "sprinkler_bare");
                        WMask(MaterialMaps.Plenum, m).Disc(c - Vector3.up * 0.0006f, 0.027f, 12, Vector3.down, MeshWriter.MetreUV);
                    }
                    else Sprinkler(metal, c, true);
                }
                return;
            }
            if (Dice(gx, gz, 210 + i) < 0.012f && _a.fixtureHousing)
                SmokeDetector(WMask(_a.fixtureHousing, m), MaterialMaps.LedRed ? WMask(MaterialMaps.LedRed, m) : null,
                              c + new Vector3((Dice(gx, gz, 211 + i) - 0.5f) * 0.25f, 0f, (Dice(gx, gz, 212 + i) - 0.5f) * 0.25f));
        }

        static void Sprinkler(MeshWriter w, Vector3 p, bool ring)
        {
            var MU = (MeshWriter.UVProjector)MeshWriter.MetreUV;
            if (ring)
            {
                w.Frustum(p - Vector3.up * 0.004f, 0.036f, 0.038f, 0.004f, 12, true, MU);   // the escutcheon
                w.Disc(p - Vector3.up * 0.004f, 0.036f, 12, Vector3.down, MU);
            }
            w.Frustum(p - Vector3.up * 0.034f, 0.007f, 0.011f, 0.03f, 8, true, MU);         // the body
            w.Box(p - Vector3.up * 0.022f, new Vector3(0.026f, 0.024f, 0.004f), 1 | 2 | 4 | 16 | 32, MU);   // the frame
            w.Disc(p - Vector3.up * 0.038f, 0.017f, 10, Vector3.down, MU);                   // the deflector
            w.Disc(p - Vector3.up * 0.036f, 0.017f, 10, Vector3.up, MU);
        }

        static void SmokeDetector(MeshWriter w, MeshWriter led, Vector3 p)
        {
            var MU = (MeshWriter.UVProjector)MeshWriter.MetreUV;
            w.Frustum(p - Vector3.up * 0.034f, 0.062f, 0.064f, 0.034f, 20, true, MU);
            w.Disc(p - Vector3.up * 0.034f, 0.062f, 20, Vector3.down, MU);
            w.Frustum(p - Vector3.up * 0.041f, 0.028f, 0.028f, 0.007f, 14, true, MU);       // the test button
            w.Disc(p - Vector3.up * 0.041f, 0.028f, 14, Vector3.down, MU);
            if (led != null)
            {
                Vector3 l = p + new Vector3(0.045f, -0.0345f, 0f);
                const float s = 0.0028f;
                led.Quad(l + new Vector3(-s, 0f, -s), l + new Vector3(s, 0f, -s), l + new Vector3(s, 0f, s), l + new Vector3(-s, 0f, s), Vector3.down, MU);
            }
        }

        /// <summary>A square four-way supply diffuser in place of a tile: a frame, stepped louvre rings, a centre plaque, dark behind.</summary>
        void SupplyDiffuser(uint m, float tx0, float tz0, float h)
        {
            var c = new Vector3(tx0 + TileSize * 0.5f, h, tz0 + TileSize * 0.5f);
            var dark = WMask(MaterialMaps.Plenum, m);
            dark.Quad(c + new Vector3(-0.3f, 0.04f, -0.3f), c + new Vector3(0.3f, 0.04f, -0.3f), c + new Vector3(0.3f, 0.04f, 0.3f), c + new Vector3(-0.3f, 0.04f, 0.3f), Vector3.down, MeshWriter.MetreUV);
            var metal = WMask(_a.fixtureHousing, m);
            SquareRing(metal, c, 0.3f, 0.266f, h - 0.002f, h - 0.002f);
            for (int k = 0; k < 4; k++)
            {
                float o = 0.258f - 0.054f * k, yo = h - 0.006f - 0.009f * k;
                SquareRing(metal, c, o, o - 0.033f, yo, yo - 0.013f);
            }
            float pc = 0.258f - 0.054f * 4f + 0.01f, yp = h - 0.052f;
            metal.Quad(c + new Vector3(-pc, yp - h, -pc), c + new Vector3(pc, yp - h, -pc), c + new Vector3(pc, yp - h, pc), c + new Vector3(-pc, yp - h, pc), Vector3.down, MeshWriter.MetreUV);
        }

        /// <summary>A louvred return grille in place of a tile: a frame, a row of angled slats, dark behind.</summary>
        void ReturnGrille(uint m, float tx0, float tz0, float h, float turn)
        {
            var c = new Vector3(tx0 + TileSize * 0.5f, h, tz0 + TileSize * 0.5f);
            var dark = WMask(MaterialMaps.Plenum, m);
            dark.Quad(c + new Vector3(-0.3f, 0.04f, -0.3f), c + new Vector3(0.3f, 0.04f, -0.3f), c + new Vector3(0.3f, 0.04f, 0.3f), c + new Vector3(-0.3f, 0.04f, 0.3f), Vector3.down, MeshWriter.MetreUV);
            var metal = WMask(_a.fixtureHousing, m);
            SquareRing(metal, c, 0.3f, 0.268f, h - 0.002f, h - 0.002f);
            bool alongZ = turn < 0.5f;
            var q = Quaternion.AngleAxis(40f, alongZ ? Vector3.forward : Vector3.right);
            for (int k = 0; k < 12; k++)
            {
                float s = -0.24f + k * (0.48f / 11f);
                Vector3 at = c + (alongZ ? new Vector3(s, -0.012f, 0f) : new Vector3(0f, -0.012f, s));
                metal.Box(at, alongZ ? new Vector3(0.003f, 0.024f, 0.53f) : new Vector3(0.53f, 0.024f, 0.003f), q, 1 | 2 | 4 | 16 | 32, MeshWriter.MetreUV);
            }
        }

        /// <summary>A square band between two half-widths round c, outer edge at yo and inner edge at yi, facing down.</summary>
        static void SquareRing(MeshWriter w, Vector3 c, float outer, float inner, float yo, float yi)
        {
            for (int k = 0; k < 4; k++)
            {
                var q = Quaternion.Euler(0f, 90f * k, 0f);
                Vector3 o0 = q * new Vector3(-outer, 0f, -outer), o1 = q * new Vector3(outer, 0f, -outer);
                Vector3 i1 = q * new Vector3(inner, 0f, -inner), i0 = q * new Vector3(-inner, 0f, -inner);
                Vector3 a = new Vector3(c.x + o0.x, yo, c.z + o0.z), b = new Vector3(c.x + o1.x, yo, c.z + o1.z);
                Vector3 d = new Vector3(c.x + i0.x, yi, c.z + i0.z), e = new Vector3(c.x + i1.x, yi, c.z + i1.z);
                Vector3 n = Vector3.Cross(b - a, d - a).normalized;
                if (n.y > 0f) n = -n;
                w.Quad(a, b, e, d, n, MeshWriter.MetreUV);
            }
        }

        /// <summary>
        /// A tile with a corner crumbled away: what is left (fanned from the far corner) with its ragged broken edge and the
        /// fibre showing in it; the space above shows through (a gap).
        /// </summary>
        void CrumbledTile(MeshWriter tileW, MeshWriter edgeW, float tx0, float tz0, float h, int corner, float a, float b, int seed)
        {
            Vector2 C0 = new Vector2(0f, 0f), C1 = new Vector2(TileSize, 0f), C2 = new Vector2(TileSize, TileSize), C3 = new Vector2(0f, TileSize);
            var corners = new[] { C0, C1, C2, C3 };
            Vector2 K = corners[corner], N1 = corners[(corner + 1) % 4], O = corners[(corner + 2) % 4], N2 = corners[(corner + 3) % 4];
            Vector2 P = K + (N1 - K).normalized * a, Q = K + (N2 - K).normalized * b;
            var rng = new System.Random(seed);
            var rag = new List<Vector2> { P };
            Vector2 perp = new Vector2(-(Q - P).y, (Q - P).x).normalized;
            if (Vector2.Dot(perp, K - P) < 0f) perp = -perp;                                 // toward the lost corner
            for (int k = 1; k <= 5; k++)
                rag.Add(Vector2.Lerp(P, Q, k / 6f) + perp * (((float)rng.NextDouble() - 0.3f) * 0.03f));
            rag.Add(Q);
            // What is left: P, N1, O, N2, Q, then back along the ragged edge.
            var poly = new List<Vector2> { P, N1, O, N2 };
            for (int k = rag.Count - 1; k >= 1; k--) poly.Add(rag[k]);
            Vector3 W(Vector2 l) => new Vector3(tx0 + l.x, h, tz0 + l.y);
            for (int k = 0; k < poly.Count; k++)
            {
                Vector2 u = poly[k], v = poly[(k + 1) % poly.Count];
                if (u == O || v == O) continue;
                tileW.Quad(W(O), W(u), W(v), W(v), Vector3.down, MeshWriter.CeilingUV);
            }
            for (int k = 0; k + 1 < rag.Count; k++)
            {
                Vector3 p0 = W(rag[k]), p1 = W(rag[k + 1]);
                Vector2 out2 = new Vector2(-(rag[k + 1] - rag[k]).y, (rag[k + 1] - rag[k]).x).normalized;
                if (Vector2.Dot(out2, K - rag[k]) < 0f) out2 = -out2;
                edgeW.Quad(p0, p1, p1 + Vector3.up * TileThick, p0 + Vector3.up * TileThick, new Vector3(out2.x, 0f, out2.y), MeshWriter.MetreUV);
            }
        }

        // ---------------- Walls ----------------

        /// <summary>The edge a cell's side is on (for Broken / HoleAt).</summary>
        static void EdgeOfSide(int x, int z, int side, out bool vertical, out int line, out int along)
        {
            vertical = side < 2;
            line = side == 0 ? x : side == 1 ? x + 1 : side == 2 ? z : z + 1;
            along = vertical ? z : x;
        }

        /// <summary>
        /// Marks on one wall face (seen from cell x, z): scuffs, nail holes, a picture's ghost, a wallpaper seam peeling.
        /// face: a point on the face at the cell's middle, floor level; along: the face's horizontal axis; into: into the room.
        /// </summary>
        void WallDetails(int x, int z, int side, Vector3 face, Vector3 along, Vector3 into, Room r, uint m, float top)
        {
            int gx = GX(x), gz = GZ(z);
            float reach = C * 0.5f - T * 0.5f - 0.02f;
            Vector3 up = Vector3.up, lift = into * 0.0012f;
            Vector3 P(float a, float y) => face + along * a + up * y + lift;

            // Scuffs: chairs pushed back, bags, hips; a hallway gets most.
            float scuffChance = r.kind == RoomKind.Hallway ? 0.4f : 0.12f;
            if (Dice(gx, gz, 220 + side) < scuffChance)
            {
                int n = 1 + (int)(Dice(gx, gz, 221 + side) * 3f);
                for (int k = 0; k < n; k++)
                {
                    float hip = Dice(gx, gz, 230 + side * 8 + k);
                    float y = hip < 0.5f ? 0.82f + 0.18f * Dice(gx, gz, 240 + side * 8 + k) : 0.6f + 0.14f * Dice(gx, gz, 240 + side * 8 + k);
                    float half = 0.06f + 0.2f * Dice(gx, gz, 250 + side * 8 + k), thick = 0.004f + 0.009f * Dice(gx, gz, 260 + side * 8 + k);
                    float a = Mathf.Lerp(-reach + half, reach - half, Dice(gx, gz, 270 + side * 8 + k));
                    byte alpha = (byte)(110 + 70 * Dice(gx, gz, 280 + side * 8 + k));
                    _wallWear.Rect(P(a, y), along, up, half, thick, new Color32(100, 96, 92, alpha), new Vector2(0.55f, 0.15f));
                }
            }
            // Nail holes (a picture or a calendar was here once).
            if (Dice(gx, gz, 290 + side) < 0.06f)
            {
                int n = Dice(gx, gz, 291 + side) < 0.6f ? 1 : 2;
                float a = Mathf.Lerp(-reach + 0.1f, reach - 0.1f, Dice(gx, gz, 292 + side)), y = 1.45f + 0.35f * Dice(gx, gz, 293 + side);
                for (int k = 0; k < n; k++)
                    _wallWear.Rect(P(a + k * (0.3f + 0.3f * Dice(gx, gz, 294 + side)), y), along, up, 0.004f, 0.004f, new Color32(40, 36, 32, 235), new Vector2(-0.35f, 0f));
            }
            // Where something hung: the wall round it dirtier, so its shape shows paler.
            if (Dice(gx, gz, 300 + side) < 0.035f)
            {
                float w = 0.18f + 0.2f * Dice(gx, gz, 301 + side), hh = 0.14f + 0.16f * Dice(gx, gz, 302 + side);
                float a = Mathf.Lerp(-reach + w + 0.04f, reach - w - 0.04f, Dice(gx, gz, 303 + side)), y = 1.42f + 0.2f * Dice(gx, gz, 304 + side);
                const float band = 0.035f;
                var col = new Color32(196, 189, 176, 150);
                _wallWear.Rect(P(a, y + hh + band * 0.5f), along, up, w + band, band * 0.5f, col, new Vector2(0.92f, 0f), -1f, 1f, 0f, 1f);
                _wallWear.Rect(P(a, y - hh - band * 0.5f), along, up, w + band, band * 0.5f, col, new Vector2(0.92f, 0f), -1f, 1f, 1f, 0f);
                _wallWear.Rect(P(a - w - band * 0.5f, y), along, up, band * 0.5f, hh, col, new Vector2(0f, 0.95f), 1f, 0f, -1f, 1f);
                _wallWear.Rect(P(a + w + band * 0.5f, y), along, up, band * 0.5f, hh, col, new Vector2(0f, 0.95f), 0f, 1f, -1f, 1f);
                _wallWear.Rect(P(a, y + hh - 0.05f), along, up, 0.004f, 0.004f, new Color32(40, 36, 32, 235), new Vector2(-0.35f, 0f));
            }
            // Wallpaper coming away at a seam, by the ceiling or above the baseboard.
            if (Dice(gx, gz, 310 + side) < 0.03f && _a.wallpaper && _a.WallMaterial(r) == _a.wallpaper && _a.paintedWall)
            {
                bool atTop = Dice(gx, gz, 311 + side) < 0.6f;
                float a = Mathf.Lerp(-reach + 0.2f, reach - 0.2f, Dice(gx, gz, 312 + side));
                float dir = Dice(gx, gz, 313 + side) < 0.5f ? -1f : 1f;
                float legY = 0.08f + 0.14f * Dice(gx, gz, 314 + side), legA = 0.05f + 0.1f * Dice(gx, gz, 315 + side);
                float yc = atTop ? top - 0.004f : (NoBaseboard ? 0.004f : BB + 0.004f);
                float sy = atTop ? -1f : 1f;
                float curl = 0.015f + 0.03f * Dice(gx, gz, 316 + side);
                Peel(WMask(_a.wallpaper, m), WMask(_a.paintedWall, m), face, along, into, a, yc, dir * legA, sy * legY, curl);
            }
        }

        /// <summary>
        /// A triangle of wallpaper come away at a seam: its corner (a, y) curls off the wall, held along the far edge; its
        /// back (paper) behind it and the bare wall where it was.
        /// </summary>
        void Peel(MeshWriter paper, MeshWriter bare, Vector3 face, Vector3 along, Vector3 into, float a, float y, float legA, float legY, float curl)
        {
            Vector3 Flat(float u, float v) => face + along * (a + u * legA) + Vector3.up * (y + v * legY);
            // A point of the triangle (u, v >= 0, u + v <= 1): lifted off the wall by how near it is to the free corner.
            Vector3 Curled(float u, float v)
            {
                float free = 1f - (u + v);                       // 1 at the corner, 0 on the held edge
                return Flat(u, v) + into * (0.0008f + curl * Mathf.Pow(free, 1.8f)) + Vector3.up * ((legY > 0f ? 0.3f : -1f) * curl * 0.4f * free * free);
            }
            Vector2 UV(Vector3 p) => MeshWriter.WallUV(p + _origin, -into);
            const int n = 4;
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n - i; j++)
            {
                float u0 = i / (float)n, v0 = j / (float)n, u1 = (i + 1) / (float)n, v1 = (j + 1) / (float)n;
                Tri(u0, v0, u1, v0, u0, v1);
                if (i + j + 1 < n) Tri(u1, v0, u1, v1, u0, v1);
            }
            void Tri(float au, float av, float bu, float bv, float cu, float cv)
            {
                Vector3 A = Curled(au, av), B = Curled(bu, bv), Cc = Curled(cu, cv);
                Vector3 nrm = Vector3.Cross(B - A, Cc - A).normalized;
                if (Vector3.Dot(nrm, into) < 0f) nrm = -nrm;
                paper.Quad(A, B, Cc, Cc, nrm, UV(Flat(au, av)), UV(Flat(bu, bv)), UV(Flat(cu, cv)), UV(Flat(cu, cv)));
                bare.Quad(A - nrm * 0.0004f, B - nrm * 0.0004f, Cc - nrm * 0.0004f, Cc - nrm * 0.0004f, -nrm, MeshWriter.WallUV);
            }
            // Where it came away: the painted board under it.
            Vector3 k0 = Flat(0f, 0f) + into * 0.0004f, k1 = Flat(1f, 0f) + into * 0.0004f, k2 = Flat(0f, 1f) + into * 0.0004f;
            bare.Quad(k0, k1, k2, k2, into, MeshWriter.WallUV);
        }

        /// <summary>An exit sign on the wall over a doorway, on this side: a white box, its face glowing EXIT.</summary>
        void ExitSign(Vector3 edge, Vector3 outDir, uint m)
        {
            if (!MaterialMaps.ExitSign || !_a.fixtureHousing) return;
            Vector3 into = -outDir, right = Vector3.Cross(Vector3.up, outDir);
            float y = OfficeBiome.DoorHeaderHeight + 0.05f + 0.095f;
            Vector3 back = edge - outDir * (T * 0.5f);
            // The modelled sign (Tools/Blender/build_signs.py, 2026-11-04): a thermoplastic housing, its stencil face lit
            // from inside, its test button and AC lamp; one in three a combo with its two emergency lamp heads on top.
            var face = FittingModels.Ready && MaterialMaps.ExitFace ? FittingModels.Get("ExitFace") : null;
            if (face != null)
            {
                var q = Quaternion.LookRotation(into);
                var at = back + Vector3.up * y;
                int gx = Mathf.RoundToInt((_origin.x + at.x) * 4f), gz = Mathf.RoundToInt((_origin.z + at.z) * 4f);
                bool combo = Dice(gx, gz, 3070) < 0.33f;
                if (Model(combo ? "ExitCombo" : "ExitSign", m, at, q))
                {
                    foreach (var part in face)
                        WMask(MaterialMaps.ExitFace, m).AddMesh(part.vertices, part.normals, part.tangents, part.uv, part.triangles, at, q);
                    return;
                }
            }
            WMask(_a.fixtureHousing, m).Box(back + into * 0.0275f + Vector3.up * y, Mathf.Abs(right.x) > 0.5f ? new Vector3(0.32f, 0.19f, 0.055f) : new Vector3(0.055f, 0.19f, 0.32f),
                                            1 | 2 | 4 | 8 | 16 | 32, MeshWriter.MetreUV);
            Vector3 c = back + into * 0.0558f + Vector3.up * y;
            const float hw = 0.145f, hh = 0.08f;
            WMask(MaterialMaps.ExitSign, m).Quad(c - right * hw - Vector3.up * hh, c + right * hw - Vector3.up * hh, c + right * hw + Vector3.up * hh, c - right * hw + Vector3.up * hh,
                into, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        }

        // ---------------- Floor ----------------

        /// <summary>A coffee spill: a round stain with a darker dried rim, sometimes a few drips leading off.</summary>
        void CoffeeSpots(int x, int z)
        {
            int gx = GX(x), gz = GZ(z);
            if (Dice(gx, gz, 320) > 0.018f) return;
            var c = new Vector3((x + 0.2f + 0.6f * Dice(gx, gz, 321)) * C, 0f, (z + 0.2f + 0.6f * Dice(gx, gz, 322)) * C);
            float r = 0.04f + 0.07f * Dice(gx, gz, 323);
            var col = new Color32(150, 116, 84, 200);
            Spot(c, r, col, 0.7f);
            int drips = (int)(Dice(gx, gz, 324) * 4f);
            float ang = Dice(gx, gz, 325) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            for (int k = 0; k < drips; k++)
                Spot(c + dir * (r + 0.05f + 0.07f * k + 0.03f * Dice(gx, gz, 330 + k)), 0.008f + 0.014f * Dice(gx, gz, 340 + k), col, 0.5f);
        }

        void Spot(Vector3 c, float r, Color32 col, float ring)
        {
            c.y = 0.0015f;
            _floorWear.Rect(c, Vector3.right, Vector3.forward, r, r, col, new Vector2(-0.3f, ring));
        }

        /// <summary>
        /// An aluminium threshold strip across an opening where the floor changes (carpet to wood or laminate): drawn once,
        /// by the cell on its + side in the chunk (or, on a chunk border, the cell on the - side's chunk does not).
        /// </summary>
        void Threshold(int x, int z, int side, Vector3 edge, Vector3 along, Room r)
        {
            if (!MaterialMaps.Aluminium) return;
            int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
            Material other;
            if (_L.InChunk(nx, nz))
            {
                if (side == 0 || side == 2 || _L.Has(nx, nz, CellFlags.NoFloor)) return;
                other = _a.FloorMaterial(_L.RoomAt(nx, nz));
            }
            else
            {
                if (side == 1 || side == 3) return;
                int dx = nx < 0 ? -1 : nx >= _L.N ? 1 : 0, dz = nz < 0 ? -1 : nz >= _L.N ? 1 : 0;
                var nl = Neighbour(dx, dz);
                if (nl == null) return;
                int lx = nx - dx * _L.N, lz = nz - dz * _L.N;
                if (nl.Has(lx, lz, CellFlags.NoFloor)) return;
                other = _a.FloorMaterial(nl.RoomAt(lx, lz));
            }
            if (other == _a.FloorMaterial(r)) return;
            var w = WMask(MaterialMaps.Aluminium, CellMask(x, z));
            // To the wall at an end with one (a doorway), else all the way to the next cell's strip: along an open boundary
            // the strips ran short at every cell, a gap every 1.2 m (2026-10-31).
            float half = C * 0.5f - T * 0.5f;
            int ax = Mathf.RoundToInt(along.x), az = Mathf.RoundToInt(along.z);
            float end0 = StripRuns(x, z, side, -ax, -az) ? C * 0.5f : half, end1 = StripRuns(x, z, side, ax, az) ? C * 0.5f : half;
            Vector3 across = Vector3.Cross(Vector3.up, along);
            const float bw = 0.0225f, tw = 0.011f, ht = 0.006f, y0 = 0.0004f;
            Vector3 a0 = edge - along * end0, a1 = edge + along * end1;
            Vector3 Lb(Vector3 p) => p - across * bw + Vector3.up * y0;
            Vector3 Rb(Vector3 p) => p + across * bw + Vector3.up * y0;
            Vector3 Lt(Vector3 p) => p - across * tw + Vector3.up * ht;
            Vector3 Rt(Vector3 p) => p + across * tw + Vector3.up * ht;
            w.Quad(Lt(a0), Rt(a0), Rt(a1), Lt(a1), Vector3.up, MeshWriter.MetreUV);
            w.Quad(Lb(a0), Lt(a0), Lt(a1), Lb(a1), (-across + Vector3.up).normalized, MeshWriter.MetreUV);
            w.Quad(Rt(a0), Rb(a0), Rb(a1), Rt(a1), (across + Vector3.up).normalized, MeshWriter.MetreUV);
            w.Quad(Lb(a0), Rb(a0), Rt(a0), Lt(a0), -along, MeshWriter.MetreUV);
            w.Quad(Lb(a1), Rb(a1), Rt(a1), Lt(a1), along, MeshWriter.MetreUV);
        }

        /// <summary>Does the edge on this side go on, open, into the next cell along (dx, dz), with no wall between the two?</summary>
        bool StripRuns(int x, int z, int side, int dx, int dz)
        {
            int nx = x + dx, nz = z + dz;
            if ((dx == 0 && dz == 0) || !_L.InChunk(nx, nz)) return false;
            int toward = dx < 0 ? 0 : dx > 0 ? 1 : dz < 0 ? 2 : 3;
            return _L.EdgeOf(x, z, toward) != EdgeKind.Wall && _L.EdgeOf(nx, nz, side) != EdgeKind.Wall;
        }

        /// <summary>Crumbs of a crumbled tile on the floor under it, if nothing stands there.</summary>
        void Crumbs()
        {
            if (!MaterialMaps.TileBack) return;
            foreach (var (c, x, z) in _crumbs)
            {
                if (_L.Has(x, z, CellFlags.NoFloor) || !_L.Plain(x, z) || !Clear(c, 0.2f)) continue;
                int gx = GX(x), gz = GZ(z);
                int n = 3 + (int)(Dice(gx, gz, 350) * 4f);
                var bits = WMask(MaterialMaps.TileBack, CellMask(x, z));
                var face = WMask(_a.ceilingTile, CellMask(x, z));
                for (int k = 0; k < n; k++)
                {
                    float s = 0.012f + 0.03f * Dice(gx, gz, 351 + k), th = 0.006f + 0.008f * Dice(gx, gz, 360 + k);
                    var at = c + new Vector3((Dice(gx, gz, 370 + k) - 0.5f) * 0.4f, th * 0.5f, (Dice(gx, gz, 380 + k) - 0.5f) * 0.4f);
                    var q = Quaternion.Euler((Dice(gx, gz, 390 + k) - 0.5f) * 30f, Dice(gx, gz, 400 + k) * 360f, 0f);
                    (k % 2 == 0 ? face : bits).Box(at, new Vector3(s, th, s * (0.6f + 0.5f * Dice(gx, gz, 410 + k))), q, 63, MeshWriter.MetreUV);
                }
            }
        }

        // ---------------- Doors ----------------

        /// <summary>
        /// Some doors are worn (seeded by the door's edge): black kick marks low on one face, grime round the handle and along
        /// the edge people push. A small FloorWear mesh on the leaf, so it swings with it. Main thread (InstantiateDoors).
        /// </summary>
        void DoorMarks(Transform model, DoorSpec d, GeneratedChunkResources resources)
        {
            var leaf = model ? model.Find("Leaf") : null;
            if (!leaf || !WearMaterial()) return;
            int ex = d.edge.line, ez = d.edge.along, salt = d.edge.vertical ? 800 : 900;
            bool kicks = Dice(ex, ez, salt) < 0.35f, grime = Dice(ex, ez, salt + 1) < 0.45f;
            if (!kicks && !grime) return;
            var o = new Overlay();
            // The leaf's own frame (the model's, Blender's): x across (hinge 0 to the free edge at -0.957), y through it
            // (its flat faces at 0 and 0.040; the raised mouldings stand proud of the front one), z up.
            const float front = -0.0007f, back = 0.0400f + 0.0007f;
            Vector3 X = Vector3.right, Z = Vector3.forward;
            if (kicks)
            {
                float fy = Dice(ex, ez, salt + 2) < 0.5f ? front : back;
                int n = 4 + (int)(Dice(ex, ez, salt + 3) * 6f);
                o.Rect(new Vector3(-0.5f, fy, 0.16f), X, Z, 0.3f, 0.12f, new Color32(160, 154, 148, 110), new Vector2(0.3f, 0.3f));
                for (int k = 0; k < n; k++)
                {
                    float ang = (Dice(ex, ez, salt + 10 + k) - 0.5f) * 0.5f;
                    Vector3 a = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)), b = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                    var c = new Vector3(-0.15f - 0.65f * Dice(ex, ez, salt + 20 + k), fy, 0.03f + 0.28f * Dice(ex, ez, salt + 30 + k));
                    o.Rect(c, a, b, 0.015f + 0.045f * Dice(ex, ez, salt + 40 + k), 0.003f + 0.007f * Dice(ex, ez, salt + 50 + k),
                           new Color32(32, 30, 30, (byte)(150 + 80 * Dice(ex, ez, salt + 60 + k))), new Vector2(0.4f, 0.3f));
                }
            }
            if (grime)
                foreach (float fy in new[] { front, back })
                {
                    o.Rect(new Vector3(-0.85f, fy, 0.97f), X, Z, 0.08f, 0.13f, new Color32(150, 140, 128, 150), new Vector2(0.25f, 0.25f));
                    o.Rect(new Vector3(-0.93f, fy, 1.0f), X, Z, 0.03f, 0.26f, new Color32(165, 155, 142, 120), new Vector2(0.4f, 0.5f));
                }
            var go = new GameObject("Wear");
            go.transform.SetParent(leaf, false);
            var mesh = o.ToMesh("DoorWear");
            if (resources) resources.Own(mesh);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = s_wearMat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.renderingLayerMask = 0xFFFFFFFFu;
        }
    }
}
