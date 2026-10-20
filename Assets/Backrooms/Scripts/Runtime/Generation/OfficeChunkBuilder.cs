using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    public struct LightSource
    {
        public Vector3 position;     // world
        public bool isLamp;          // floor lamp (warm point) vs ceiling troffer (spot + bounce)
        public uint roomLayer;       // rendering-layer bit for bounce light
        public bool dome;            // round fitting on a tall ceiling (longer throw)
        public float ceiling;        // ceiling height at the fixture
        public int faultyId;         // index of this tube's own lens renderer when it stutters, else -1
        public Vector2Int chunk;     // owning chunk (set by the streamer)
    }

    /// <summary>
    /// Turns an <see cref="OfficeLayout"/> into a handful of combined meshes (one per material and room
    /// light layer), merged box colliders, and a list of light sources. A typical chunk is ~25 renderers.
    /// Walls are emitted face-by-face from the cell they are seen from, so border walls are built half by
    /// each neighbouring chunk with no overlap, and grid-vertex posts close corners and door jambs.
    /// </summary>
    public class OfficeChunkBuilder
    {
        const float C = OfficeBiome.CellSize, H = OfficeBiome.CeilingHeight, T = OfficeBiome.WallThickness;
        const float BB = OfficeBiome.BaseboardHeight, BD = OfficeBiome.BaseboardDepth;

        readonly OfficeBiomeAssets _a;
        readonly Dictionary<(Material, uint), MeshWriter> _writers = new Dictionary<(Material, uint), MeshWriter>(new KeyComparer());
        readonly List<(Vector3 c, Vector3 s)> _boxes = new List<(Vector3, Vector3)>();
        readonly List<(Vector3 c, Vector3 s, float yaw)> _rotBoxes = new List<(Vector3, Vector3, float)>();
        readonly HashSet<Vector2Int> _fixtureCells = new HashSet<Vector2Int>();
        OfficeLayout _L;
        Vector3 _origin;

        public OfficeLayout Layout => _L;
        public readonly List<LightSource> lights = new List<LightSource>();
        readonly List<(MeshWriter w, bool dome)> faultyLenses = new List<(MeshWriter, bool)>();

        /// <summary>After Instantiate: renderers whose emission the light manager animates.</summary>
        public readonly List<Renderer> litLensRenderers = new List<Renderer>();
        public readonly List<Renderer> faultyRenderers = new List<Renderer>();

        /// <summary>Material keys compared by reference so the data phase never touches the native engine.</summary>
        class KeyComparer : IEqualityComparer<(Material, uint)>
        {
            public bool Equals((Material, uint) a, (Material, uint) b) => ReferenceEquals(a.Item1, b.Item1) && a.Item2 == b.Item2;
            public int GetHashCode((Material, uint) k) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.Item1) * 31 + (int)k.Item2;
        }

        public OfficeChunkBuilder(OfficeBiomeAssets assets) { _a = assets; }

        public static uint LayerBit(int color) => color < 0 ? 1u : (1u | (1u << (color + 1)));

        MeshWriter W(Material m, int color) => WMask(m, LayerBit(color));

        MeshWriter WMask(Material m, uint mask)
        {
            if (!_writers.TryGetValue((m, mask), out var w))
            {
                w = new MeshWriter { worldOffset = _origin };
                _writers[(m, mask)] = w;
            }
            return w;
        }

        /// <summary>
        /// Phase 1 (thread-safe, no engine calls): layout and all vertex/collider data. Run on a worker thread.
        /// </summary>
        public void BuildData(OfficeLayout layout)
        {
            _L = layout; _writers.Clear(); _boxes.Clear(); _rotBoxes.Clear(); lights.Clear(); faultyLenses.Clear(); _instances.Clear();
            _origin = new Vector3(layout.coord.x * OfficeBiome.ChunkSize, 0f, layout.coord.y * OfficeBiome.ChunkSize);
            _fixtureCells.Clear();
            foreach (var f in layout.fixtures) _fixtureCells.Add(f.cell);

            BuildFloorAndCeiling();
            BuildCeilingGrid();
            BuildWalls();
            BuildPosts();
            BuildCrawls();
            BuildPillars();
            BuildHalfWalls();
            BuildFixtures(lights);
            BuildPlates();
            BuildProps(lights);
        }

        /// <summary>Phase 2 (main thread): meshes, renderers and colliders.</summary>
        public GameObject Instantiate(Transform parent)
        {
            var root = new GameObject($"Chunk_{_L.coord.x}_{_L.coord.y}");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = _origin;
            root.isStatic = true;

            foreach (var kv in _writers)
            {
                var (mat, mask) = kv.Key;
                if (kv.Value.VertexCount == 0) continue;
                var go = new GameObject(mat.name + "_" + mask.ToString("X2"));
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = kv.Value.ToMesh(go.name);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.renderingLayerMask = mask;
                if (mat == _a.lensOn || (_a.domeOn && mat == _a.domeOn) || mat == _a.lampShadeOn) litLensRenderers.Add(r);
                bool caster = mat != _a.lensOn && mat != _a.lensOff && mat != _a.ceilingGrid && mat != _a.carpet && mat != _a.ceilingTile && mat != _a.lampShadeOn
                              && mat != _a.domeOn && mat != _a.domeOff && mat != _a.ceilingPlaster;
                r.shadowCastingMode = caster ? ShadowCastingMode.On : ShadowCastingMode.Off;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }

            for (int i = 0; i < faultyLenses.Count; i++)
            {
                var (w, dome) = faultyLenses[i];
                var go = new GameObject("FaultyLens_" + i);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = w.ToMesh(go.name);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = dome && _a.domeOn ? _a.domeOn : _a.lensOn;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                faultyRenderers.Add(r);
            }

            // Prop instances (shared meshes from the Blender prefabs). Colliders come from _rotBoxes/_boxes.
            foreach (var pi in _instances)
            {
                if (!pi.prefab) continue;
                var go = Object.Instantiate(pi.prefab, root.transform, false);
                go.transform.localPosition = pi.position;
                go.transform.localRotation = pi.rotation;
                go.transform.localScale = pi.scale;
                var col = go.GetComponent<Collider>();
                if (col && pi.prefab.GetComponent<BoxCollider>() && pi.scale == Vector3.one) { /* keep prefab collider */ }
                else if (col) Object.Destroy(col);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    r.renderingLayerMask = pi.mask;
                    if (!pi.litShade) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] == _a.lampShadeOff) mats[i] = _a.lampShadeOn;
                    r.sharedMaterials = mats;
                    litLensRenderers.Add(r);
                }
            }

            // Colliders: one floor slab, merged axis-aligned boxes, and rotated prop boxes.
            var floor = root.AddComponent<BoxCollider>();
            floor.center = new Vector3(OfficeBiome.ChunkSize * 0.5f, -0.25f, OfficeBiome.ChunkSize * 0.5f);
            floor.size = new Vector3(OfficeBiome.ChunkSize, 0.5f, OfficeBiome.ChunkSize);
            foreach (var (c, s) in _boxes)
            {
                var b = root.AddComponent<BoxCollider>();
                b.center = c; b.size = s;
            }
            foreach (var (c, s, yaw) in _rotBoxes)
            {
                var go = new GameObject("PropCollider");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = c;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                go.AddComponent<BoxCollider>().size = s;
            }
            return root;
        }

        public GameObject Build(OfficeLayout layout, Transform parent, List<LightSource> outLights)
        {
            BuildData(layout);
            outLights.AddRange(lights);
            return Instantiate(parent);
        }

        // ---------------- Floor / ceiling / grid ----------------

        Material WallMat(Room r) => r.painted && _a.paintedWall ? _a.paintedWall : _a.wallpaper;

        /// <summary>One floor and one ceiling quad per room (world-space UVs keep textures continuous).</summary>
        void BuildFloorAndCeiling()
        {
            foreach (var r in _L.rooms)
            {
                float x0 = r.rect.xMin * C, z0 = r.rect.yMin * C, x1 = r.rect.xMax * C, z1 = r.rect.yMax * C, h = r.ceiling;
                W(_a.carpet, r.color).Quad(new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), new Vector3(x1, 0, z0), Vector3.up, MeshWriter.FloorUV);
                var ceil = r.Tall && _a.ceilingPlaster ? _a.ceilingPlaster : _a.ceilingTile;
                W(ceil, r.color).Quad(new Vector3(x0, h, z0), new Vector3(x1, h, z0), new Vector3(x1, h, z1), new Vector3(x0, h, z1), Vector3.down,
                    r.Tall ? (MeshWriter.UVProjector)MeshWriter.MetreUV : MeshWriter.CeilingUV);
            }
        }

        /// <summary>
        /// Suspended grid per room with an acoustic-tile ceiling: main runners along X every 0.6 m (offset
        /// 0.3), cross tees along Z every 0.6 m, broken where a troffer sits. Runs end inside the walls.
        /// </summary>
        void BuildCeilingGrid()
        {
            const float gw = 0.024f, gd = 0.012f, off = OfficeBiome.TileGridOffset;
            float y = H - gd * 0.5f;
            foreach (var r in _L.rooms)
            {
                if (r.Tall) continue;
                var w = W(_a.ceilingGrid, r.color);
                float x0 = r.rect.xMin * C, x1 = r.rect.xMax * C, z0 = r.rect.yMin * C, z1 = r.rect.yMax * C;
                for (float z = z0 + off; z < z1; z += 0.6f)
                    w.Box(new Vector3((x0 + x1) * 0.5f, y, z), new Vector3(x1 - x0, gd, gw), 4 | 16 | 32, MeshWriter.MetreUV);
                for (int k = 0; k <= r.rect.width * 2; k++)
                {
                    float x = x0 + k * 0.6f;
                    bool midColumn = (k & 1) == 1;          // x = cell + 0.6 crosses troffer lenses
                    int cellX = r.rect.xMin + k / 2;
                    float start = z0;
                    for (int cz = r.rect.yMin; cz <= r.rect.yMax; cz++)
                    {
                        bool cut = cz < r.rect.yMax && midColumn && _fixtureCells.Contains(new Vector2Int(cellX, cz));
                        if (cut || cz == r.rect.yMax)
                        {
                            float end = cz == r.rect.yMax ? z1 : cz * C + off;
                            if (end - start > 0.01f) w.Box(new Vector3(x, y - 0.0005f, (start + end) * 0.5f), new Vector3(gw, gd, end - start), 1 | 2 | 4, MeshWriter.MetreUV);
                            start = cz * C + off + 0.6f;
                        }
                    }
                }
            }
        }

        // ---------------- Walls ----------------

        /// <summary>
        /// Walls and soffits, emitted from each cell they are seen from.
        /// Solid edges: a face from the floor to that room's ceiling. Passable edges: the opening top is
        /// the door header (2.1 m), or the lower of the two ceilings; the taller side gets a soffit face
        /// down to the opening top plus its half of the underside. On a chunk border the neighbour's
        /// ceiling is unknown, so a tall room always closes its border openings with a header and a face
        /// on the border line, guaranteeing nothing can be seen above a neighbour's lower ceiling.
        /// </summary>
        void BuildWalls()
        {
            int N = _L.N;
            for (int x = 0; x <= N; x++)
            for (int z = 0; z < N; z++)
                EdgeFaces(_L.vEdges[x, z], true, x, z);
            for (int x = 0; x < N; x++)
            for (int z = 0; z <= N; z++)
                EdgeFaces(_L.hEdges[x, z], false, x, z);
            BuildWallColliders();
        }

        /// <summary>vertical: edge on line X = line between (line-1, along) and (line, along); else line Z = line.</summary>
        void EdgeFaces(EdgeKind e, bool vertical, int line, int along)
        {
            float l = line * C, a0 = along * C + T * 0.5f, a1 = (along + 1) * C - T * 0.5f;
            for (int s = 0; s < 2; s++)
            {
                int cx = vertical ? (s == 0 ? line - 1 : line) : along;
                int cz = vertical ? along : (s == 0 ? line - 1 : line);
                if (!_L.InChunk(cx, cz)) continue;
                var room = _L.RoomAt(cx, cz);
                float sign = s == 0 ? -1f : 1f;               // face normal points into the viewing cell
                float f = l + sign * T * 0.5f;
                Vector3 n = vertical ? new Vector3(sign, 0, 0) : new Vector3(0, 0, sign);
                Vector3 A(float pos, float at) => vertical ? new Vector3(pos, 0, at) : new Vector3(at, 0, pos);
                float myCeil = room.ceiling;

                if (e == EdgeKind.Wall)
                {
                    WallFace(room, A(f, a0), A(f, a1), 0f, myCeil, n);
                    Baseboard(room.color, A(f, a0), A(f, a1), n);
                    continue;
                }

                // Passable edge: where does the opening stop?
                int ox = vertical ? (s == 0 ? line : line - 1) : along;
                int oz = vertical ? along : (s == 0 ? line : line - 1);
                bool otherKnown = _L.InChunk(ox, oz);
                float otherCeil = otherKnown ? _L.CeilingAt(ox, oz) : OfficeBiome.CeilingHeight;
                bool header = e == EdgeKind.DoorHeader || (!otherKnown && room.Tall);
                float top = header ? OfficeBiome.DoorHeaderHeight : Mathf.Min(myCeil, otherCeil);
                if (myCeil <= top + 0.001f) continue;

                WallFace(room, A(f, a0), A(f, a1), top, myCeil, n);
                // This side's half of the underside.
                Vector3 u0 = A(l, a0) + Vector3.up * top, u1 = A(l, a1) + Vector3.up * top;
                Vector3 v0 = A(f, a0) + Vector3.up * top, v1 = A(f, a1) + Vector3.up * top;
                W(WallMat(room), room.color).Quad(u0, v0, v1, u1, Vector3.down, MeshWriter.WallUV);
                if (!otherKnown && room.Tall)
                {
                    // Neighbour may have no header: close the space between its ceiling and our header.
                    WallFace(room, A(l, a0), A(l, a1), top, OfficeBiome.CeilingHeight + 0.05f, -n);
                }
            }
        }

        void WallFace(Room room, Vector3 a, Vector3 b, float y0, float y1, Vector3 n)
        {
            W(WallMat(room), room.color).Quad(a + Vector3.up * y0, b + Vector3.up * y0, b + Vector3.up * y1, a + Vector3.up * y1, n, MeshWriter.WallUV);
        }

        void Baseboard(int color, Vector3 a, Vector3 b, Vector3 n)
        {
            var w = W(_a.baseboard, color);
            Vector3 fa = a + n * BD, fb = b + n * BD;
            w.Quad(fa, fb, fb + Vector3.up * BB, fa + Vector3.up * BB, n, MeshWriter.MetreUV);
            w.Quad(fa + Vector3.up * BB, fb + Vector3.up * BB, b + Vector3.up * BB, a + Vector3.up * BB, Vector3.up, MeshWriter.MetreUV);
        }

        /// <summary>Merges runs of solid wall edges into single colliders. Border walls get half thickness.</summary>
        void BuildWallColliders()
        {
            int N = _L.N;
            const float HC = OfficeBiome.MaxCeilingHeight;
            for (int x = 0; x <= N; x++)
            {
                int start = -1;
                for (int z = 0; z <= N; z++)
                {
                    bool solid = z < N && _L.vEdges[x, z] == EdgeKind.Wall;
                    if (solid && start < 0) start = z;
                    if (!solid && start >= 0)
                    {
                        float z0 = start * C - T * 0.5f, z1 = z * C + T * 0.5f;
                        float cxPos = x * C, th = T;
                        if (x == 0) { cxPos += T * 0.25f; th = T * 0.5f; }
                        else if (x == N) { cxPos -= T * 0.25f; th = T * 0.5f; }
                        _boxes.Add((new Vector3(cxPos, HC * 0.5f, (z0 + z1) * 0.5f), new Vector3(th, HC + 0.4f, z1 - z0)));
                        start = -1;
                    }
                }
            }
            for (int z = 0; z <= N; z++)
            {
                int start = -1;
                for (int x = 0; x <= N; x++)
                {
                    bool solid = x < N && _L.hEdges[x, z] == EdgeKind.Wall;
                    if (solid && start < 0) start = x;
                    if (!solid && start >= 0)
                    {
                        float x0 = start * C - T * 0.5f, x1 = x * C + T * 0.5f;
                        float czPos = z * C, th = T;
                        if (z == 0) { czPos += T * 0.25f; th = T * 0.5f; }
                        else if (z == N) { czPos -= T * 0.25f; th = T * 0.5f; }
                        _boxes.Add((new Vector3((x0 + x1) * 0.5f, HC * 0.5f, czPos), new Vector3(x1 - x0, HC + 0.4f, th)));
                        start = -1;
                    }
                }
            }
        }

        /// <summary>
        /// Square posts at grid vertices touching any wall: close corners and form door jambs. They always
        /// reach the tallest possible ceiling (hidden above lower ceilings), so no slit can open next to a
        /// taller neighbouring room. Their light layers combine every in-chunk room around the vertex.
        /// </summary>
        void BuildPosts()
        {
            int N = _L.N;
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                // Interior posts rise to the tallest ceiling around them (never into a lower room's
                // plenum); posts on the chunk border can't see the neighbour, so they always go full height.
                float HP = OfficeBiome.MaxCeilingHeight;
                if (x > 0 && z > 0)
                    HP = Mathf.Max(Mathf.Max(_L.CeilingAt(x - 1, z - 1), _L.CeilingAt(x, z - 1)),
                                   Mathf.Max(_L.CeilingAt(x - 1, z), _L.CeilingAt(x, z)));
                bool wall = false, header = false;
                // Headers: explicit door headers, or border openings closed by a tall room (see EdgeFaces).
                void Check(EdgeKind e, bool border, int cx, int cz)
                {
                    if (e == EdgeKind.Wall) wall = true;
                    else if (e == EdgeKind.DoorHeader) header = true;
                    else if (e == EdgeKind.Door && border && _L.InChunk(cx, cz) && _L.RoomAt(cx, cz).Tall) header = true;
                }
                Check(_L.vEdges[x, z], x == 0, x, z);
                if (z > 0) Check(_L.vEdges[x, z - 1], x == 0, x, z - 1);
                Check(_L.hEdges[x, z], z == 0, x, z);
                if (x > 0) Check(_L.hEdges[x - 1, z], z == 0, x - 1, z);
                if (!wall && !header) continue;
                // On the chunk border the neighbour may have a wall ending at this vertex, expecting our post
                // to close its end. So border posts between openings stand full height (a slim mullion), with
                // their own collider since no wall run of ours covers them.
                if (!wall && (x == 0 || z == 0))
                {
                    wall = true;
                    _boxes.Add((new Vector3(x * C, HP * 0.5f, z * C), new Vector3(T, HP, T)));
                }

                // Each face takes the finish and light layer of the room it faces, so a post reads as part
                // of the wall on both sides (painted on one side, wallpaper on the other).
                Room Facing(int ax, int az, int bx, int bz)
                {
                    if (_L.InChunk(ax, az)) return _L.RoomAt(ax, az);
                    if (_L.InChunk(bx, bz)) return _L.RoomAt(bx, bz);
                    return _L.RoomAt(Mathf.Clamp(x, 0, N - 1), Mathf.Clamp(z, 0, N - 1));
                }
                var faces = new (int bit, Room room)[]
                {
                    (1, Facing(x - 1, z, x - 1, z - 1)), (2, Facing(x, z, x, z - 1)),
                    (16, Facing(x, z - 1, x - 1, z - 1)), (32, Facing(x, z, x - 1, z)),
                };
                foreach (var (bit, room) in faces)
                {
                    var w = W(WallMat(room), room.color);
                    if (wall)
                    {
                        w.Box(new Vector3(x * C, HP * 0.5f, z * C), new Vector3(T, HP, T), bit, MeshWriter.WallUV);
                        W(_a.baseboard, room.color).Box(new Vector3(x * C, BB * 0.5f, z * C), new Vector3(T + BD * 2f, BB, T + BD * 2f), bit | (bit == 1 ? 8 : 0), MeshWriter.MetreUV);
                    }
                    else
                    {
                        // Between openings: the block above the opening tops, no post down to the floor.
                        float hy = OfficeBiome.DoorHeaderHeight;
                        w.Box(new Vector3(x * C, (hy + HP) * 0.5f, z * C), new Vector3(T, HP - hy, T), bit | (bit == 1 ? 4 : 0), MeshWriter.WallUV);
                    }
                }
            }
        }

        // ---------------- Crawl spaces ----------------

        void BuildCrawls()
        {
            float fill = (C - T - OfficeBiome.CrawlGap) * 0.5f;
            foreach (var r in _L.rooms)
            {
                if (r.kind != RoomKind.Crawl) continue;
                var rc = r.rect;
                bool alongX = rc.width > 1;
                var w = W(WallMat(r), r.color);
                if (alongX)
                {
                    float x0 = rc.xMin * C, x1 = rc.xMax * C, zA = rc.yMin * C + T * 0.5f, zB = rc.yMax * C - T * 0.5f;
                    AddSolid(w, new Vector3((x0 + x1) * 0.5f, H * 0.5f, zA + fill * 0.5f), new Vector3(x1 - x0, H, fill), r.color);
                    AddSolid(w, new Vector3((x0 + x1) * 0.5f, H * 0.5f, zB - fill * 0.5f), new Vector3(x1 - x0, H, fill), r.color);
                }
                else
                {
                    float z0 = rc.yMin * C, z1 = rc.yMax * C, xA = rc.xMin * C + T * 0.5f, xB = rc.xMax * C - T * 0.5f;
                    AddSolid(w, new Vector3(xA + fill * 0.5f, H * 0.5f, (z0 + z1) * 0.5f), new Vector3(fill, H, z1 - z0), r.color);
                    AddSolid(w, new Vector3(xB - fill * 0.5f, H * 0.5f, (z0 + z1) * 0.5f), new Vector3(fill, H, z1 - z0), r.color);
                }
            }
        }

        void AddSolid(MeshWriter w, Vector3 c, Vector3 s, int color)
        {
            w.Box(c, s, 1 | 2 | 16 | 32, MeshWriter.WallUV);
            _boxes.Add((c, s));
        }

        // ---------------- Pillars & half walls ----------------

        void BuildPillars()
        {
            foreach (var p in _L.pillars)
            {
                var room = _L.RoomAt(Mathf.Min(p.vertex.x, _L.N - 1), Mathf.Min(p.vertex.y, _L.N - 1));
                float h = room.ceiling;
                var c = new Vector3(p.vertex.x * C, h * 0.5f, p.vertex.y * C);
                W(WallMat(room), p.roomColor).Box(c, new Vector3(p.size, h, p.size), 1 | 2 | 16 | 32, MeshWriter.WallUV);
                W(_a.baseboard, p.roomColor).Box(new Vector3(c.x, BB * 0.5f, c.z), new Vector3(p.size + BD * 2f, BB, p.size + BD * 2f), 1 | 2 | 8 | 16 | 32, MeshWriter.MetreUV);
                _boxes.Add((c, new Vector3(p.size, h, p.size)));
            }
        }

        void BuildHalfWalls()
        {
            const float ht = OfficeBiome.HalfWallThickness, hh = OfficeBiome.HalfWallHeight, cap = 0.025f;
            foreach (var hw in _L.halfWalls)
            {
                float a0 = hw.start * C, a1 = (hw.start + hw.length) * C, line = hw.line * C;
                Vector3 c = hw.alongX ? new Vector3((a0 + a1) * 0.5f, hh * 0.5f, line) : new Vector3(line, hh * 0.5f, (a0 + a1) * 0.5f);
                Vector3 s = hw.alongX ? new Vector3(a1 - a0, hh, ht) : new Vector3(ht, hh, a1 - a0);
                var hwRoom = hw.alongX ? _L.RoomAt(Mathf.Min(hw.start, _L.N - 1), Mathf.Min(hw.line, _L.N - 1))
                                       : _L.RoomAt(Mathf.Min(hw.line, _L.N - 1), Mathf.Min(hw.start, _L.N - 1));
                W(WallMat(hwRoom), hw.roomColor).Box(c, s, 1 | 2 | 16 | 32, MeshWriter.WallUV);
                Vector3 capS = s + new Vector3(0.02f, 0f, 0.02f); capS.y = cap;
                W(_a.halfWallCap, hw.roomColor).Box(new Vector3(c.x, hh + cap * 0.5f, c.z), capS, 63, MeshWriter.MetreUV);
                Vector3 bbS = s + new Vector3(BD * 2f, 0f, BD * 2f); bbS.y = BB;
                W(_a.baseboard, hw.roomColor).Box(new Vector3(c.x, BB * 0.5f, c.z), bbS, 1 | 2 | 8 | 16 | 32, MeshWriter.MetreUV);
                _boxes.Add((new Vector3(c.x, (hh + cap) * 0.5f, c.z), new Vector3(s.x, hh + cap, s.z)));
            }
        }

        // ---------------- Fixtures ----------------

        void BuildFixtures(List<LightSource> lights)
        {
            // Grid cut-outs must be known before the ceiling grid is written, so collect first.
            foreach (var f in _L.fixtures)
            {
                float h = f.height > 0f ? f.height : H;
                var c = new Vector3(f.cell.x * C + C * 0.5f, h, f.cell.y * C + C * 0.5f);
                // Faulty tubes get their own lens mesh so their emission can stutter independently.
                MeshWriter lens;
                if (f.faulty) { lens = new MeshWriter { worldOffset = _origin }; faultyLenses.Add((lens, f.dome)); }
                else lens = W(f.on ? (f.dome ? _a.domeOn ?? _a.lensOn : _a.lensOn) : (f.dome ? _a.domeOff ?? _a.lensOff : _a.lensOff), f.roomColor);

                if (f.dome)
                {
                    // Round surface-mounted fitting: shallow drum with a glowing diffuser.
                    lens.Frustum(c + Vector3.down * 0.07f, 0.2f, 0.23f, 0.07f, 16, true, MeshWriter.MetreUV);
                    lens.Disc(c + Vector3.down * 0.07f, 0.2f, 16, Vector3.down, MeshWriter.MetreUV);
                }
                else
                {
                    lens.Box(c + Vector3.down * 0.004f, new Vector3(1.17f, 0.006f, 0.57f), 4 | 1 | 2 | 16 | 32, MeshWriter.MetreUV);
                    var hw = W(_a.fixtureHousing, f.roomColor);
                    const int m = 1 | 2 | 4 | 16 | 32;
                    hw.Box(c + new Vector3(0, -0.009f, 0.291f), new Vector3(1.2f, 0.016f, 0.018f), m, MeshWriter.MetreUV);
                    hw.Box(c + new Vector3(0, -0.009f, -0.291f), new Vector3(1.2f, 0.016f, 0.018f), m, MeshWriter.MetreUV);
                    hw.Box(c + new Vector3(0.591f, -0.009f, 0), new Vector3(0.018f, 0.016f, 0.6f), m, MeshWriter.MetreUV);
                    hw.Box(c + new Vector3(-0.591f, -0.009f, 0), new Vector3(0.018f, 0.016f, 0.6f), m, MeshWriter.MetreUV);
                }
                if (f.on)
                    lights.Add(new LightSource
                    {
                        position = _origin + c + (f.dome ? Vector3.down * 0.07f : Vector3.zero),
                        isLamp = false, dome = f.dome, ceiling = h, roomLayer = LayerBit(f.roomColor),
                        faultyId = f.faulty ? faultyLenses.Count - 1 : -1,
                    });
            }
        }

        // ---------------- Outlets and switches ----------------

        void BuildPlates()
        {
            const float depth = 0.006f;
            foreach (var p in _L.plates)
            {
                float x0 = p.cell.x * C, z0 = p.cell.y * C;
                Vector3 n, pos;
                switch (p.side)
                {
                    case 0: n = Vector3.right; pos = new Vector3(x0 + T * 0.5f, p.height, z0 + p.t * C); break;
                    case 1: n = Vector3.left; pos = new Vector3(x0 + C - T * 0.5f, p.height, z0 + p.t * C); break;
                    case 2: n = Vector3.forward; pos = new Vector3(x0 + p.t * C, p.height, z0 + T * 0.5f); break;
                    default: n = Vector3.back; pos = new Vector3(x0 + p.t * C, p.height, z0 + C - T * 0.5f); break;
                }
                Vector3 right = Vector3.Cross(Vector3.up, n);   // horizontal axis on the wall
                float hw = 0.035f, hh = 0.0575f;
                Vector3 f = pos + n * depth;
                // Atlas: left half outlet, right half switch.
                float u0 = p.isSwitch ? 0.5f : 0f, u1 = u0 + 0.5f;
                var w = W(_a.wallPlate, p.roomColor);
                w.Quad(f - right * hw - Vector3.up * hh, f + right * hw - Vector3.up * hh, f + right * hw + Vector3.up * hh, f - right * hw + Vector3.up * hh, n,
                    new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 1), new Vector2(u0, 1));
                // Thin edges so the plate catches light at grazing angles.
                w.Quad(pos - right * hw + Vector3.up * hh, f - right * hw + Vector3.up * hh, f + right * hw + Vector3.up * hh, pos + right * hw + Vector3.up * hh, Vector3.up,
                    new Vector2(u0, 1), new Vector2(u0, 1), new Vector2(u0, 1), new Vector2(u0, 1));
                w.Quad(pos - right * hw - Vector3.up * hh, pos + right * hw - Vector3.up * hh, f + right * hw - Vector3.up * hh, f - right * hw - Vector3.up * hh, Vector3.down,
                    new Vector2(u0, 0), new Vector2(u0, 0), new Vector2(u0, 0), new Vector2(u0, 0));
            }
        }

        // ---------------- Props ----------------

        /// <summary>Blender prop instances: shared meshes, one GameObject each, created on the main thread.</summary>
        public struct PropInstance
        {
            public GameObject prefab;
            public Vector3 position;     // chunk-local
            public Quaternion rotation;
            public Vector3 scale;
            public uint mask;
            public bool litShade;
        }

        readonly List<PropInstance> _instances = new List<PropInstance>();

        void Place(GameObject prefab, Vector3 at, float yaw, int color, Vector3? scale = null, bool litShade = false)
        {
            _instances.Add(new PropInstance { prefab = prefab, position = at, rotation = Quaternion.Euler(0f, yaw, 0f),
                scale = scale ?? Vector3.one, mask = LayerBit(color), litShade = litShade });
        }

        void BuildProps(List<LightSource> lights)
        {
            bool modelled = _a.sofaPrefabs != null && _a.sofaPrefabs.Length > 0 && _a.floorLampPrefab;
            foreach (var p in _L.props)
            {
                var rot = Quaternion.Euler(0f, p.yaw, 0f);
                var at = new Vector3(p.position.x, 0f, p.position.y);
                if (modelled) { PlaceModelled(p, at, rot, lights); continue; }
                switch (p.kind)
                {
                    case PropKind.Sofa: Sofa(p, at, rot); break;
                    case PropKind.FloorLamp: Lamp(p, at, lights); break;
                    case PropKind.ConferenceTable: Table(p, at, rot); break;
                }
            }
        }

        void PlaceModelled(Prop p, Vector3 at, Quaternion rot, List<LightSource> lights)
        {
            int col = p.roomColor;
            switch (p.kind)
            {
                case PropKind.Sofa:
                {
                    // Variant picks the model and fabric; small rooms and every fourth seat get an armchair.
                    int fabricCount = _a.sofaFabrics.Length;
                    bool arm = _a.armchairPrefabs != null && _a.armchairPrefabs.Length > 0 && (p.variant % 4 == 3);
                    var rng = new System.Random(OfficeLayoutGenerator.Hash(_L.coord.x, _L.coord.y, (int)(at.x * 31), (int)(at.z * 17)));
                    int fabric = rng.Next(fabricCount);
                    if (arm)
                    {
                        Place(_a.armchairPrefabs[fabric % _a.armchairPrefabs.Length], at, p.yaw, col);
                    }
                    else
                    {
                        int models = Mathf.Max(1, _a.sofaPrefabs.Length / fabricCount);
                        int model = rng.Next(models);
                        Place(_a.sofaPrefabs[model * fabricCount + fabric], at, p.yaw, col);
                    }
                    break;
                }
                case PropKind.FloorLamp:
                    Place(_a.floorLampPrefab, at, p.variant * 37f, col, null, p.lit);
                    if (p.lit) lights.Add(new LightSource { position = _origin + at + Vector3.up * 1.5f, isLamp = true, roomLayer = LayerBit(col), faultyId = -1 });
                    break;
                case PropKind.ConferenceTable:
                {
                    float L = p.size.x;
                    Place(_a.tableModulePrefab, at, p.yaw, col, new Vector3(L, 1f, 1f));
                    for (int i = -1; i <= 1; i += 2)
                        Place(_a.tableLegPrefab, at + rot * new Vector3(i * L * 0.32f, 0, 0), p.yaw, col);
                    _rotBoxes.Add((at + Vector3.up * 0.37f, new Vector3(L, 0.74f, 1.1f), p.yaw));
                    var rng = new System.Random(OfficeLayoutGenerator.Hash(_L.coord.x, _L.coord.y, (int)(p.position.x * 100), (int)(p.position.y * 100)));
                    int perSide = Mathf.Max(1, Mathf.FloorToInt(L / 0.75f));
                    for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < perSide; i++)
                    {
                        if (rng.NextDouble() < 0.15) continue;
                        float cx = -L * 0.5f + L * (i + 0.5f) / perSide + (float)(rng.NextDouble() - 0.5) * 0.12f;
                        float cz = side * (0.55f + 0.2f + (float)rng.NextDouble() * 0.25f);
                        float yaw = p.yaw + (side < 0 ? 0f : 180f) + (float)(rng.NextDouble() - 0.5) * 30f;
                        var cpos = at + rot * new Vector3(cx, 0, cz);
                        Place(_a.officeChairPrefab, cpos, yaw, col);
                    }
                    break;
                }
                case PropKind.WallClock:
                    if (_a.wallClockPrefab) Place(_a.wallClockPrefab, new Vector3(at.x, p.size.y, at.z), p.yaw, col);
                    break;
            }
        }

        void Part(Material m, int color, Vector3 at, Quaternion rot, Vector3 localCenter, Vector3 size, int mask = 63)
        {
            W(m, color).Box(at + rot * localCenter, size, rot, mask, MeshWriter.MetreUV);
        }

        void Sofa(Prop p, Vector3 at, Quaternion rot)
        {
            var fabrics = _a.sofaFabrics;
            var fab = fabrics[p.variant % fabrics.Length];
            float L = p.size.x, D = p.size.z, arm = 0.16f;
            int col = p.roomColor;
            const int noBottom = 1 | 2 | 8 | 16 | 32;
            // Legs.
            for (int i = 0; i < 4; i++)
            {
                float lx = (i % 2 == 0 ? -1 : 1) * (L * 0.5f - 0.08f), lz = (i < 2 ? -1 : 1) * (D * 0.5f - 0.08f);
                Part(_a.wood, col, at, rot, new Vector3(lx, 0.045f, lz), new Vector3(0.05f, 0.09f, 0.05f), noBottom);
            }
            Part(fab, col, at, rot, new Vector3(0, 0.19f, 0), new Vector3(L, 0.2f, D));                                    // base
            int cushions = L > 2.05f ? 3 : 2;
            float cw = (L - arm * 2f) / cushions;
            for (int i = 0; i < cushions; i++)
            {
                float cx = -L * 0.5f + arm + cw * (i + 0.5f);
                Part(fab, col, at, rot, new Vector3(cx, 0.355f, 0.07f), new Vector3(cw - 0.012f, 0.13f, D - 0.24f), noBottom);  // seat
                Part(fab, col, at, rot, new Vector3(cx, 0.6f, -D * 0.5f + 0.2f), new Vector3(cw - 0.012f, 0.36f, 0.14f), noBottom); // back cushion
            }
            Part(fab, col, at, rot, new Vector3(0, 0.56f, -D * 0.5f + 0.065f), new Vector3(L, 0.54f, 0.13f), noBottom);      // back frame
            Part(fab, col, at, rot, new Vector3(-L * 0.5f + arm * 0.5f, 0.42f, 0.0f), new Vector3(arm, 0.26f, D), noBottom);  // arms
            Part(fab, col, at, rot, new Vector3(L * 0.5f - arm * 0.5f, 0.42f, 0.0f), new Vector3(arm, 0.26f, D), noBottom);
            _rotBoxes.Add((at + Vector3.up * 0.4f, new Vector3(L, 0.8f, D), p.yaw));
        }

        void Lamp(Prop p, Vector3 at, List<LightSource> lights)
        {
            int col = p.roomColor;
            W(_a.lampMetal, col).Box(at + Vector3.up * 0.012f, new Vector3(0.28f, 0.024f, 0.28f), 1 | 2 | 8 | 16 | 32, MeshWriter.MetreUV);
            W(_a.lampMetal, col).Box(at + Vector3.up * 0.75f, new Vector3(0.022f, 1.45f, 0.022f), 1 | 2 | 16 | 32, MeshWriter.MetreUV);
            var shade = p.lit ? _a.lampShadeOn : _a.lampShadeOff;
            var sw = W(shade, col);
            sw.Frustum(at + Vector3.up * 1.36f, 0.23f, 0.16f, 0.30f, 12, true, MeshWriter.MetreUV);
            sw.Frustum(at + Vector3.up * 1.36f, 0.23f, 0.16f, 0.30f, 12, false, MeshWriter.MetreUV);
            _boxes.Add((at + Vector3.up * 0.8f, new Vector3(0.3f, 1.6f, 0.3f)));
            if (p.lit) lights.Add(new LightSource { position = _origin + at + Vector3.up * 1.48f, isLamp = true, roomLayer = LayerBit(col), faultyId = -1 });
        }

        void Table(Prop p, Vector3 at, Quaternion rot)
        {
            int col = p.roomColor;
            float L = p.size.x, D = p.size.z, top = p.size.y;
            Part(_a.laminate, col, at, rot, new Vector3(0, top - 0.02f, 0), new Vector3(L, 0.04f, D));
            for (int i = -1; i <= 1; i += 2)
                Part(_a.wood, col, at, rot, new Vector3(i * L * 0.32f, (top - 0.04f) * 0.5f, 0), new Vector3(0.1f, top - 0.04f, D * 0.55f), 1 | 2 | 16 | 32);
            _rotBoxes.Add((at + Vector3.up * top * 0.5f, new Vector3(L, top, D), p.yaw));

            // Chairs pushed in at uneven angles; a couple are missing.
            var rng = new System.Random(OfficeLayoutGenerator.Hash(_L.coord.x, _L.coord.y, (int)(p.position.x * 100), (int)(p.position.y * 100)));
            int perSide = Mathf.Max(1, Mathf.FloorToInt(L / 0.8f));
            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < perSide; i++)
            {
                if (rng.NextDouble() < 0.18) continue;
                float cx = -L * 0.5f + L * (i + 0.5f) / perSide + (float)(rng.NextDouble() - 0.5) * 0.12f;
                float cz = side * (D * 0.5f + 0.18f + (float)rng.NextDouble() * 0.25f);
                float yaw = p.yaw + (side < 0 ? 0f : 180f) + (float)(rng.NextDouble() - 0.5) * 24f;
                Chair(col, at + rot * new Vector3(cx, 0, cz), Quaternion.Euler(0, yaw, 0), yaw);
            }
        }

        void Chair(int col, Vector3 at, Quaternion rot, float yaw)
        {
            const int noBottom = 1 | 2 | 8 | 16 | 32;
            Part(_a.lampMetal, col, at, rot, new Vector3(0, 0.012f, 0), new Vector3(0.5f, 0.024f, 0.5f), noBottom);  // base
            Part(_a.lampMetal, col, at, rot, new Vector3(0, 0.23f, 0), new Vector3(0.04f, 0.42f, 0.04f), 1 | 2 | 16 | 32);
            Part(_a.chairFabric, col, at, rot, new Vector3(0, 0.47f, 0), new Vector3(0.47f, 0.07f, 0.46f));            // seat
            Part(_a.chairFabric, col, at, rot, new Vector3(0, 0.78f, -0.22f), new Vector3(0.45f, 0.42f, 0.06f));       // back
            _rotBoxes.Add((at + Vector3.up * 0.5f, new Vector3(0.5f, 1.0f, 0.5f), yaw));
        }
    }
}
