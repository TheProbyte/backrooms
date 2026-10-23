using System.Collections.Generic;
using Backrooms.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>Receiving side of an opening between two rooms (or a chunk-border opening).</summary>
    public struct Doorway
    {
        public Vector3 position;     // world, on the opening line at floor level, centre of the opening
        public Vector3 inward;       // unit vector into the receiving room
        public uint layer;           // receiving room's rendering-layer bit
        public float height;         // top of the opening
        public float width;          // relative width (1 = one cell)
    }

    public struct LightSource
    {
        public Vector3 position;     // world
        public bool isLamp;          // floor lamp (warm point) vs ceiling troffer (spot + bounce)
        public uint roomLayer;       // rendering-layer bit for bounce light
        public bool dome;            // round fitting on a tall ceiling (longer throw)
        public bool strip;           // hanging cool-white warehouse strip
        public float ceiling;        // ceiling height at the fixture
        public int faultyId;         // index of this tube's own lens renderer when it stutters, else -1
        public Vector3Int chunk;     // owning chunk (x, storey, z), set by the streamer
    }

    /// <summary>
    /// Turns an <see cref="OfficeLayout"/> into a handful of combined meshes (one per material and room
    /// light layer), merged box colliders, and a list of light sources. A typical chunk is ~25 renderers.
    /// Walls are emitted face-by-face from the cell they are seen from, so border walls are built half by
    /// each neighbouring chunk with no overlap, and grid-vertex posts close corners and door jambs.
    /// </summary>
    public partial class OfficeChunkBuilder
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
        public readonly List<Doorway> doorways = new List<Doorway>();
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

        public static uint LayerBit(int color) => LightLayers.LayerBit(color);

        /// <summary>Renderer mask for shadow-only proxies: every layer, so every light's shadows see them.</summary>
        const uint ProxyMask = 0xFFFFFFFFu;
        /// <summary>Shadow-only proxies for walls (see BuildShadowProxies and <see cref="WallShadows"/>).</summary>
        const uint WallProxyMask = 0xFFFFFFFEu;

        /// <summary>Wall shadow proxies of every loaded chunk. Off in light grid mode, on in room-layer mode.</summary>
        public static readonly HashSet<Renderer> WallProxies = new HashSet<Renderer>();
        static bool _wallShadows = true;
        public static bool WallShadows
        {
            get => _wallShadows;
            set
            {
                _wallShadows = value;
                WallProxies.RemoveWhere(r => !r);
                foreach (var r in WallProxies) r.enabled = value;
            }
        }

        uint[,] _cellMask;
        OfficeLayout[] _neighbours;

        /// <summary>Layout of the chunk at (dx, dz) from this one, or null if not supplied.</summary>
        OfficeLayout Neighbour(int dx, int dz)
        {
            if (_neighbours == null) return null;
            var want = new Vector2Int(_L.coord.x + dx, _L.coord.y + dz);
            foreach (var l in _neighbours) if (l != null && l.coord == want) return l;
            return null;
        }

        uint CellMask(int x, int z) => _cellMask[Mathf.Clamp(x, 0, _L.N - 1), Mathf.Clamp(z, 0, _L.N - 1)];

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
        public void BuildData(OfficeLayout layout) => BuildData(layout, null);

        /// <param name="neighbours">Layouts of the surrounding chunks (any may be null): open floors that
        /// continue into them share light across the border.</param>
        public void BuildData(OfficeLayout layout, OfficeLayout[] neighbours)
        {
            _neighbours = neighbours;
            _L = layout; _writers.Clear(); _boxes.Clear(); _rotBoxes.Clear(); lights.Clear(); faultyLenses.Clear(); _instances.Clear(); _movables.Clear(); doorways.Clear();
            _origin = new Vector3(layout.coord.x * OfficeBiome.ChunkSize, layout.BaseY, layout.coord.y * OfficeBiome.ChunkSize);
            _fixtureCells.Clear();
            foreach (var f in layout.fixtures) _fixtureCells.Add(f.cell);

            _cellMask = LightLayers.CellMasks(_L, Neighbour);
            BuildFloorAndCeiling();
            BuildCeilingGrid();
            BuildWalls();
            BuildPosts();
            BuildCrawls();
            BuildPillars();
            BuildHalfWalls();
            BuildShadowProxies();
            BuildDoorways();
            BuildFixtures(lights);
            BuildPlates();
            BuildProps(lights);
            BuildAnnex();
            BuildCeilingColliders();
        }

        /// <summary>Ceilings are solid for the player and for props: nothing can climb into the space above them. Runs of cells of one room per row.</summary>
        void BuildCeilingColliders()
        {
            int n = _L.N;
            for (int z = 0; z < n; z++)
            {
                int start = -1; float h0 = 0f;
                for (int x = 0; x <= n; x++)
                {
                    bool solid = x < n && !_L.Has(x, z, CellFlags.OpenAbove);
                    float h = solid ? _L.CeilingAt(x, z) : 0f;
                    if (start >= 0 && (!solid || Mathf.Abs(h - h0) > 0.001f))
                    {
                        _boxes.Add((new Vector3((start + x) * 0.5f * C, h0 + 0.1f, (z + 0.5f) * C), new Vector3((x - start) * C, 0.2f, C)));
                        start = -1;
                    }
                    if (solid && start < 0) { start = x; h0 = h; }
                }
            }
        }

        /// <summary>Phase 2 (main thread): meshes, renderers and colliders.</summary>
        public GameObject Instantiate(Transform parent)
        {
            var root = new GameObject(_L.storey == 0 ? $"Chunk_{_L.coord.x}_{_L.coord.y}" : $"Chunk_{_L.coord.x}_{_L.coord.y}_s{_L.storey}");
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
                if (mat == _a.lensOn || (_a.domeOn && mat == _a.domeOn) || (_a.stripOn && mat == _a.stripOn) || mat == _a.lampShadeOn) litLensRenderers.Add(r);
                bool architecture = mat == _a.wallpaper || mat == _a.paintedWall || mat == _a.baseboard || mat == _a.halfWallCap;
                bool caster = !architecture && mat != _a.lensOn && mat != _a.lensOff && mat != _a.ceilingGrid && mat != _a.carpet && mat != _a.ceilingTile && mat != _a.lampShadeOn
                              && mat != _a.domeOn && mat != _a.domeOff && mat != _a.ceilingPlaster && mat != _a.stripOn && mat != _a.stripOff
                              && mat != _a.cardboard && mat != _a.boxTape && mat != _a.boxPrint && mat != _a.rackUpright && mat != _a.rackBeam && mat != _a.pallet;   // warehouse dressing: proxies cast the shadows
                r.shadowCastingMode = mask == ProxyMask || mask == WallProxyMask ? ShadowCastingMode.ShadowsOnly : caster ? ShadowCastingMode.On : ShadowCastingMode.Off;
                if (mask == WallProxyMask)
                {
                    if (WallProxies.Count > 4000) WallProxies.RemoveWhere(x => !x);   // unloaded chunks
                    r.renderingLayerMask = ProxyMask;
                    r.enabled = WallShadows;
                    WallProxies.Add(r);
                }
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
                go.transform.localRotation = pi.rotation * pi.prefab.transform.localRotation;   // keep the model's own axis fix
                go.transform.localScale = pi.scale;
                var col = go.GetComponent<Collider>();
                if (col && pi.prefab.GetComponent<BoxCollider>() && pi.scale == Vector3.one) { /* keep prefab collider */ }
                else if (col) Object.Destroy(col);
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    r.renderingLayerMask = pi.mask;
                    if (pi.noShadow) r.shadowCastingMode = ShadowCastingMode.Off;
                    if (!pi.litShade) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) if (mats[i] == _a.lampShadeOff) mats[i] = _a.lampShadeOn;
                    r.sharedMaterials = mats;
                    litLensRenderers.Add(r);
                }
            }

            // Movable props (skipped if they have been moved: they live with MovablePropManager now).
            var moved = MovablePropManager.Instance;
            foreach (var s in _movables)
                if (!moved || !moved.IsMoved(s.id))
                    MovableProp.Spawn(s, root.transform, s.position, Quaternion.Euler(0f, s.yaw, 0f));

            // Colliders: one floor slab, merged axis-aligned boxes, and rotated prop boxes.
            AddFloorColliders(root);
            foreach (var e in _elevators) ElevatorSystem.Spawn(root.transform, e);
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

        /// <summary>One floor slab for the chunk, or one per row run of floored cells where a well cuts through.</summary>
        void AddFloorColliders(GameObject root)
        {
            int n = _L.N;
            bool any = false;
            for (int x = 0; x < n && !any; x++) for (int z = 0; z < n; z++) if (_L.Has(x, z, CellFlags.NoFloor)) { any = true; break; }
            if (!any)
            {
                var floor = root.AddComponent<BoxCollider>();
                floor.center = new Vector3(OfficeBiome.ChunkSize * 0.5f, -0.25f, OfficeBiome.ChunkSize * 0.5f);
                floor.size = new Vector3(OfficeBiome.ChunkSize, 0.5f, OfficeBiome.ChunkSize);
                return;
            }
            for (int z = 0; z < n; z++)
            {
                int start = -1;
                for (int x = 0; x <= n; x++)
                {
                    bool solid = x < n && !_L.Has(x, z, CellFlags.NoFloor);
                    if (solid && start < 0) start = x;
                    if (!solid && start >= 0)
                    {
                        var b = root.AddComponent<BoxCollider>();
                        b.center = new Vector3((start + x) * 0.5f * C, -0.25f, (z + 0.5f) * C);
                        b.size = new Vector3((x - start) * C, 0.5f, C);
                        start = -1;
                    }
                }
            }
        }

        // ---------------- Floor / ceiling / grid ----------------

        Material WallMat(Room r) => r.painted && _a.paintedWall ? _a.paintedWall : _a.wallpaper;

        /// <summary>One floor and one ceiling quad per room (world-space UVs keep textures continuous).</summary>
        void BuildFloorAndCeiling()
        {
            // Per cell (world-space UVs keep textures continuous), so doorway cells can take light from
            // both rooms.
            for (int x = 0; x < _L.N; x++)
            for (int z = 0; z < _L.N; z++)
            {
                var r = _L.RoomAt(x, z);
                uint m = CellMask(x, z);
                float x0 = x * C, z0 = z * C, x1 = x0 + C, z1 = z0 + C, h = r.ceiling;
                if (!_L.Has(x, z, CellFlags.NoFloor))
                {
                    WMask(_a.carpet, m).Quad(new Vector3(x0, 0, z0), new Vector3(x0, 0, z1), new Vector3(x1, 0, z1), new Vector3(x1, 0, z0), Vector3.up, MeshWriter.FloorUV);
                    // Beside a void (a well, a stair hall, an elevator shaft) the wall faces below sit half a wall thickness
                    // out from the cell edge: cover that strip so no slit shows from above.
                    float e = T * 0.5f;
                    var fm = WMask(_a.carpet, m);
                    if (x > 0 && _L.Has(x - 1, z, CellFlags.NoFloor)) fm.Quad(new Vector3(x0 - e, 0, z0), new Vector3(x0 - e, 0, z1), new Vector3(x0, 0, z1), new Vector3(x0, 0, z0), Vector3.up, MeshWriter.FloorUV);
                    if (x < _L.N - 1 && _L.Has(x + 1, z, CellFlags.NoFloor)) fm.Quad(new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), new Vector3(x1 + e, 0, z1), new Vector3(x1 + e, 0, z0), Vector3.up, MeshWriter.FloorUV);
                    if (z > 0 && _L.Has(x, z - 1, CellFlags.NoFloor)) fm.Quad(new Vector3(x0, 0, z0 - e), new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), new Vector3(x1, 0, z0 - e), Vector3.up, MeshWriter.FloorUV);
                    if (z < _L.N - 1 && _L.Has(x, z + 1, CellFlags.NoFloor)) fm.Quad(new Vector3(x0, 0, z1), new Vector3(x0, 0, z1 + e), new Vector3(x1, 0, z1 + e), new Vector3(x1, 0, z1), Vector3.up, MeshWriter.FloorUV);
                }
                if (_L.Has(x, z, CellFlags.OpenAbove)) continue;     // the storey above closes it
                var ceil = r.Tall && _a.ceilingPlaster ? _a.ceilingPlaster : _a.ceilingTile;
                WMask(ceil, m).Quad(new Vector3(x0, h, z0), new Vector3(x1, h, z0), new Vector3(x1, h, z1), new Vector3(x0, h, z1), Vector3.down,
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
                if (r.Tall || r.openAbove) continue;
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
                EdgeFaces(_L.hEdges[x, z], false, z, x);      // hEdges[x, z] lies on line Z = z, along X = x
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
                if (room.noFloor && e != EdgeKind.Wall) continue;     // a well's open sides are railings, built separately; walls around a stair or shaft void are real
                float sign = s == 0 ? -1f : 1f;               // face normal points into the viewing cell
                float f = l + sign * T * 0.5f;
                Vector3 n = vertical ? new Vector3(sign, 0, 0) : new Vector3(0, 0, sign);
                Vector3 A(float pos, float at) => vertical ? new Vector3(pos, 0, at) : new Vector3(at, 0, pos);
                float myCeil = _L.WallTop(room);

                uint cm = CellMask(cx, cz);
                if (e == EdgeKind.Wall)
                {
                    WallFace(room, cm, A(f, a0), A(f, a1), 0f, myCeil, n);
                    if (!room.noFloor) Baseboard(cm, A(f, a0), A(f, a1), n);
                    continue;
                }

                // Passable edge: where does the opening stop? (Both chunks of a border compute the same.)
                var v = ViewOpening(_L, Neighbour, vertical, line, along, s);
                if (v.arched)
                {
                    ArchFaces(room, cm, vertical, l, f, a0, a1, n, myCeil);
                    continue;
                }
                float top = v.top;
                if (myCeil <= top + 0.001f) continue;

                WallFace(room, cm, A(f, a0), A(f, a1), top, myCeil, n);
                // This side's half of the underside.
                Vector3 u0 = A(l, a0) + Vector3.up * top, u1 = A(l, a1) + Vector3.up * top;
                Vector3 v0 = A(f, a0) + Vector3.up * top, v1 = A(f, a1) + Vector3.up * top;
                WMask(WallMat(room), cm).Quad(u0, v0, v1, u1, Vector3.down, MeshWriter.WallUV);
                if (!v.otherKnown && room.Tall)
                {
                    // Neighbour unknown and may have no header: close the space between its ceiling and our header.
                    WallFace(room, cm, A(l, a0), A(l, a1), top, OfficeBiome.CeilingHeight + 0.05f, -n);
                }
            }
        }

        /// <summary>How a passable edge looks from one side. Pure function of the layouts.</summary>
        public struct OpeningView
        {
            public EdgeKind kind;        // this edge, combined with the neighbouring chunk's copy on a border
            public bool otherKnown;      // the far side's ceiling is known
            public float myCeil, otherCeil;
            public float top;            // top of the opening (header, lower ceiling or arch apex)
            public bool arched;
        }

        /// <summary>
        /// The opening on an edge seen from side s (0 = the cell at line-1, 1 = the cell at line).
        /// vertical: edge on line X = line at z = along; else line Z = line at x = along.
        /// On a chunk border the neighbouring chunk's layout (if given) supplies the far ceiling and its
        /// copy of the edge, so both chunks build exactly the same opening.
        /// </summary>
        public static OpeningView ViewOpening(OfficeLayout L, System.Func<int, int, OfficeLayout> neighbour, bool vertical, int line, int along, int s)
        {
            int N = L.N;
            int cx = vertical ? (s == 0 ? line - 1 : line) : along, cz = vertical ? along : (s == 0 ? line - 1 : line);
            int ox = vertical ? (s == 0 ? line : line - 1) : along, oz = vertical ? along : (s == 0 ? line : line - 1);
            var v = new OpeningView { kind = vertical ? L.vEdges[line, along] : L.hEdges[along, line], myCeil = L.CeilingAt(cx, cz) };
            if (L.InChunk(ox, oz)) { v.otherKnown = true; v.otherCeil = L.CeilingAt(ox, oz); }
            else if (neighbour != null)
            {
                int dx = ox < 0 ? -1 : ox >= N ? 1 : 0, dz = oz < 0 ? -1 : oz >= N ? 1 : 0;
                var nl = neighbour(dx, dz);
                if (nl != null)
                {
                    v.otherKnown = true;
                    v.otherCeil = nl.CeilingAt(ox - dx * N, oz - dz * N);
                    var theirs = vertical ? nl.vEdges[line - dx * N, along] : nl.hEdges[along, line - dz * N];
                    v.kind = Combine(v.kind, theirs);
                }
            }
            if (!v.otherKnown) v.otherCeil = OfficeBiome.CeilingHeight;
            bool header = v.kind == EdgeKind.DoorHeader || (!v.otherKnown && v.myCeil > OfficeBiome.CeilingHeight + 0.01f);
            v.top = header ? OfficeBiome.DoorHeaderHeight : Mathf.Min(v.myCeil, v.otherCeil);
            v.arched = v.kind == EdgeKind.DoorHeader && v.otherKnown && IsArchedEdge(L, vertical, line, along);
            if (v.arched) v.top = ArchApex;
            return v;
        }

        /// <summary>Both chunks' copies of a border edge as one: a header on either side wins.</summary>
        public static EdgeKind Combine(EdgeKind a, EdgeKind b)
        {
            if (a == EdgeKind.Wall || b == EdgeKind.Wall) return EdgeKind.Wall;
            if (a == EdgeKind.DoorHeader || b == EdgeKind.DoorHeader) return EdgeKind.DoorHeader;
            if (a == EdgeKind.Door || b == EdgeKind.Door) return EdgeKind.Door;
            return EdgeKind.Open;
        }

        /// <summary>Apex of an arched doorway: a little above the flat 2.1 m header, as in older offices.</summary>
        public const float ArchApex = 2.3f;

        /// <summary>Decided once per edge by the district plan (interior headed doorways only).</summary>
        bool IsArched(bool vertical, int line, int along) => IsArchedEdge(_L, vertical, line, along);

        public static bool IsArchedEdge(OfficeLayout _L, bool vertical, int line, int along) =>
            vertical ? _L.archV[line, along] : _L.archH[along, line];

        /// <summary>
        /// One side of a round-topped doorway: wall above the apex, the two spandrels between the curve
        /// and the apex line, and this side's half of the curved underside (intrados).
        /// </summary>
        void ArchFaces(Room room, uint cm, bool vertical, float l, float f, float a0, float a1, Vector3 n, float myCeil)
        {
            // Enough segments that the curve reads as smooth (5 degrees each), with a smooth normal at every
            // vertex of the curved underside instead of one flat normal per facet.
            const int K = 36;
            var w = WMask(WallMat(room), cm);
            Vector3 alongDir = vertical ? Vector3.forward : Vector3.right;
            Vector3 P(float plane, float at, float y) => (vertical ? new Vector3(plane, 0, at) : new Vector3(at, 0, plane)) + Vector3.up * y;
            float r = (a1 - a0) * 0.5f, mid = (a0 + a1) * 0.5f, spring = ArchApex - r;
            float depth = Mathf.Abs(f - l);
            if (myCeil > ArchApex + 0.001f) WallFace(room, cm, P(f, a0, 0), P(f, a1, 0), ArchApex, myCeil, n);

            // Wallpaper wraps around the corner: the face\x27s texture coordinates continue onto the underside in
            // the direction the surface turns (into the opening), so stripes flow through the reveal with no seams.
            Vector2 FaceUV(float at, float y) => MeshWriter.WallUV(P(f, at, y) + w.worldOffset, n);
            Vector3 Inward(float t) => -(alongDir * Mathf.Cos(t) + Vector3.up * Mathf.Sin(t));

            for (int i = 0; i < K; i++)
            {
                float t0 = Mathf.PI * (1f - i / (float)K), t1 = Mathf.PI * (1f - (i + 1) / (float)K);
                float x0 = mid + r * Mathf.Cos(t0), y0 = spring + r * Mathf.Sin(t0);
                float x1 = mid + r * Mathf.Cos(t1), y1 = spring + r * Mathf.Sin(t1);
                // Spandrel strip on the face.
                w.Quad(P(f, x0, y0), P(f, x1, y1), P(f, x1, ArchApex), P(f, x0, ArchApex), n, MeshWriter.WallUV);
                // Underside (intrados): faces the arch\x27s centre, smooth normals, wrapped texture.
                Vector3 n0 = Inward(t0), n1 = Inward(t1);
                Vector2 uf0 = FaceUV(x0, y0), uf1 = FaceUV(x1, y1);
                Vector2 ul0 = FaceUV(x0 + Mathf.Cos(t0) * -depth, y0 + Mathf.Sin(t0) * -depth);
                Vector2 ul1 = FaceUV(x1 + Mathf.Cos(t1) * -depth, y1 + Mathf.Sin(t1) * -depth);
                w.SmoothQuad(P(l, x0, y0), P(f, x0, y0), P(f, x1, y1), P(l, x1, y1), n0, n0, n1, n1, ul0, uf0, uf1, ul1);
            }
        }

        void WallFace(Room room, uint mask, Vector3 a, Vector3 b, float y0, float y1, Vector3 n)
        {
            WMask(WallMat(room), mask).Quad(a + Vector3.up * y0, b + Vector3.up * y0, b + Vector3.up * y1, a + Vector3.up * y1, n, MeshWriter.WallUV);
        }

        void Baseboard(uint mask, Vector3 a, Vector3 b, Vector3 n)
        {
            var w = WMask(_a.baseboard, mask);
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
        /// Square posts at grid vertices: close corners and form door jambs.
        /// Ownership: each of a post's four faces is emitted by the chunk that owns the cell it faces, using
        /// that room's finish and light layer, so posts on chunk borders are assembled from both sides and
        /// always match the wall they belong to. The edges and ceilings around a border vertex are read
        /// from the neighbouring chunk, so a room that continues across a chunk border gets no post. Only
        /// where a neighbour is unknown does a border vertex get a full-height post (a slim mullion),
        /// because a neighbour's wall may end there. Posts rise only to the tallest ceiling around them.
        /// </summary>
        void BuildPosts()
        {
            int N = _L.N;
            for (int x = 0; x <= N; x++)
            for (int z = 0; z <= N; z++)
            {
                // The four cells and four edges around the vertex (possibly in neighbouring chunks).
                float c00 = CeilAt(x - 1, z - 1), c10 = CeilAt(x, z - 1), c01 = CeilAt(x - 1, z), c11 = CeilAt(x, z);
                EdgeKind? up = VEdgeAt(x, z), down = VEdgeAt(x, z - 1), right = HEdgeAt(x, z), left = HEdgeAt(x - 1, z);
                bool unknown = c00 < 0f || c10 < 0f || c01 < 0f || c11 < 0f || up == null || down == null || right == null || left == null;

                float HP = unknown ? OfficeBiome.MaxCeilingHeight : Mathf.Max(Mathf.Max(c00, c10), Mathf.Max(c01, c11));
                bool wall = false, header = false;
                float blockBottom = OfficeBiome.DoorHeaderHeight;
                void Check(EdgeKind? e) { if (e == EdgeKind.Wall) wall = true; else if (e == EdgeKind.DoorHeader) header = true; }
                // Open-plan span between rooms of different heights: a soffit down to the lower ceiling.
                void Soffit(EdgeKind? e, float ca, float cb)
                {
                    if (e != EdgeKind.Open || ca < 0f || cb < 0f || Mathf.Abs(ca - cb) < 0.01f) return;
                    if (!header) blockBottom = Mathf.Min(ca, cb);
                    else blockBottom = Mathf.Min(blockBottom, Mathf.Min(ca, cb));
                    header = true;
                }
                Check(up); Check(down); Check(right); Check(left);
                if (header) blockBottom = OfficeBiome.DoorHeaderHeight;
                Soffit(up, c01, c11);       // vertical edge above the vertex: cells (x-1, z) | (x, z)
                Soffit(down, c00, c10);     // below: (x-1, z-1) | (x, z-1)
                Soffit(right, c10, c11);    // horizontal edge to the right: (x, z-1) | (x, z)
                Soffit(left, c00, c01);     // to the left: (x-1, z-1) | (x-1, z)
                if (unknown)
                {
                    // Our wall runs cover the collider when we have a wall here; otherwise add one (only
                    // the west/south owner does, to avoid duplicates).
                    if (!wall && (x == 0 || z == 0) && x < N && z < N)
                        _boxes.Add((new Vector3(x * C, HP * 0.5f, z * C), new Vector3(T, HP, T)));
                    wall = true;
                }
                if (!wall && !header) continue;

                // (face bit, cell it faces)
                var faces = new (int bit, int cx, int cz)[] { (1, x - 1, z), (2, x, z), (16, x, z - 1), (32, x, z) };
                foreach (var (bit, cx, cz) in faces)
                {
                    if (!_L.InChunk(cx, cz)) continue;          // the neighbouring chunk emits this face
                    var room = _L.RoomAt(cx, cz);
                    uint pm = CellMask(cx, cz);
                    var w = WMask(WallMat(room), pm);
                    // A face is only ever seen from the cell it faces, so it stops at that room's ceiling
                    // (nothing pokes up into a lower room's plenum or above the ceilings when seen from above).
                    float FH = _L.WallTop(room);
                    if (wall)
                    {
                        w.Box(new Vector3(x * C, FH * 0.5f, z * C), new Vector3(T, FH, T), bit, MeshWriter.WallUV);
                        WMask(_a.baseboard, pm).Box(new Vector3(x * C, BB * 0.5f, z * C), new Vector3(T + BD * 2f, BB, T + BD * 2f), bit, MeshWriter.MetreUV);
                    }
                    else
                    {
                        // Between headed openings: the block above the header line, no post to the floor.
                        float hy = blockBottom;
                        if (FH > hy + 0.001f) w.Box(new Vector3(x * C, (hy + FH) * 0.5f, z * C), new Vector3(T, FH - hy, T), bit, MeshWriter.WallUV);
                    }
                }
                // Undersides / tops, owned by the cell at (x, z) when present.
                if (x < N && z < N)
                {
                    var room = _L.RoomAt(x, z);
                    if (wall) WMask(_a.baseboard, CellMask(x, z)).Box(new Vector3(x * C, BB * 0.5f, z * C), new Vector3(T + BD * 2f, BB, T + BD * 2f), 8, MeshWriter.MetreUV);
                    else
                    {
                        float hy = blockBottom;
                        float top = Mathf.Max(Mathf.Max(c00, c10), Mathf.Max(c01, c11));
                        if (top > hy + 0.001f)
                            WMask(WallMat(room), CellMask(x, z)).Box(new Vector3(x * C, (hy + top) * 0.5f, z * C), new Vector3(T, top - hy, T), 4, MeshWriter.WallUV);
                    }
                }
            }
        }

        /// <summary>Wall top of the cell's room, here or in a neighbouring chunk; -1 if unknown.</summary>
        float CeilAt(int x, int z)
        {
            int N = _L.N;
            if (_L.InChunk(x, z)) return _L.WallTop(_L.RoomAt(x, z));
            int dx = x < 0 ? -1 : x >= N ? 1 : 0, dz = z < 0 ? -1 : z >= N ? 1 : 0;
            var nl = Neighbour(dx, dz);
            return nl == null ? -1f : nl.WallTop(nl.RoomAt(x - dx * N, z - dz * N));
        }

        /// <summary>Edge on the line X = x at row z (z may be in the chunk below or above); null if unknown.</summary>
        EdgeKind? VEdgeAt(int x, int z)
        {
            int N = _L.N;
            if (z >= 0 && z < N) return _L.vEdges[x, z];
            int dz = z < 0 ? -1 : 1;
            var nl = Neighbour(0, dz);
            return nl?.vEdges[x, z - dz * N];
        }

        /// <summary>Edge on the line Z = z at column x (x may be in the chunk left or right); null if unknown.</summary>
        EdgeKind? HEdgeAt(int x, int z)
        {
            int N = _L.N;
            if (x >= 0 && x < N) return _L.hEdges[x, z];
            int dx = x < 0 ? -1 : 1;
            var nl = Neighbour(dx, 0);
            return nl?.hEdges[x - dx * N, z];
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
                if (hw.railing) { Railing(hw); continue; }
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

        /// <summary>Balcony railing along a well's edge: steel balusters every 0.3 m and a handrail, 1.05 m high.</summary>
        void Railing(HalfWall hw)
        {
            const float height = 1.05f, rail = 0.05f, post = 0.025f;
            float a0 = hw.start * C, a1 = (hw.start + hw.length) * C, line = hw.line * C;
            Vector3 P(float along, float y) => hw.alongX ? new Vector3(along, y, line) : new Vector3(line, y, along);
            Vector3 Size(float along, float y, float across) => hw.alongX ? new Vector3(along, y, across) : new Vector3(across, y, along);
            var metal = W(_a.lampMetal, hw.roomColor);
            metal.Box(P((a0 + a1) * 0.5f, height - rail * 0.5f), Size(a1 - a0, rail, rail * 1.4f), 63, MeshWriter.MetreUV);
            metal.Box(P((a0 + a1) * 0.5f, 0.12f), Size(a1 - a0, 0.03f, 0.03f), 63, MeshWriter.MetreUV);
            for (float a = a0; a <= a1 + 0.001f; a += 0.3f)
                metal.Box(P(a, (height - rail) * 0.5f), Size(post, height - rail, post), 1 | 2 | 16 | 32, MeshWriter.MetreUV);
            _boxes.Add((P((a0 + a1) * 0.5f, height * 0.5f), Size(a1 - a0, height, 0.08f)));
        }

        // ---------------- Doorways ----------------

        void BuildDoorways() => ComputeDoorways(_L, doorways, Neighbour);

        /// <summary>
        /// Every passable edge between two zones (and every border opening), seen from each side: where the
        /// doorway light for the receiving room goes. World positions.
        /// </summary>
        public static void ComputeDoorways(OfficeLayout L, List<Doorway> into, System.Func<int, int, OfficeLayout> neighbour = null)
        {
            int n = L.N;
            var origin = new Vector3(L.coord.x * OfficeBiome.ChunkSize, L.BaseY, L.coord.y * OfficeBiome.ChunkSize);
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                var room = L.RoomAt(x, z);
                if (room.kind == RoomKind.Crawl) continue;
                for (int side = 0; side < 4; side++)
                {
                    var e = L.EdgeOf(x, z, side);
                    if (OfficeLayout.IsSolid(e)) continue;
                    int nx = x + (side == 0 ? -1 : side == 1 ? 1 : 0), nz = z + (side == 2 ? -1 : side == 3 ? 1 : 0);
                    bool inChunk = L.InChunk(nx, nz);
                    if (inChunk && L.RoomAt(nx, nz).color == room.color) continue;          // same zone: already shared light
                    if (!inChunk && neighbour != null)
                    {
                        int dx = nx < 0 ? -1 : nx >= n ? 1 : 0, dz = nz < 0 ? -1 : nz >= n ? 1 : 0;
                        var nl = neighbour(dx, dz);
                        if (nl != null && nl.RoomAt(nx - dx * n, nz - dz * n).color == room.color) continue;
                    }
                    Vector3 c = new Vector3((x + 0.5f) * C, 0f, (z + 0.5f) * C);
                    Vector3 outDir = side == 0 ? Vector3.left : side == 1 ? Vector3.right : side == 2 ? Vector3.back : Vector3.forward;
                    float top = e == EdgeKind.DoorHeader || e == EdgeKind.Door ? OfficeBiome.DoorHeaderHeight
                              : inChunk ? Mathf.Min(room.ceiling, L.CeilingAt(nx, nz)) : OfficeBiome.DoorHeaderHeight;
                    into.Add(new Doorway
                    {
                        position = origin + c + outDir * (C * 0.5f), inward = -outDir, layer = LayerBit(room.color),
                        height = top, width = 1f,
                    });
                }
            }
        }

        // ---------------- Shadow proxies ----------------

        /// <summary>
        /// Walls are drawn as single-sided faces, which leak light at corners and baseboards in shadow
        /// maps. Shadows come instead from closed boxes matching the collision (walls, posts, pillars,
        /// half walls, crawl fills), drawn shadow-only on every light layer. Visible walls cast nothing.
        /// </summary>
        void BuildShadowProxies()
        {
            if (!_a.baseboard) return;
            var w = WMask(_a.baseboard, ProxyMask);
            var walls = WMask(_a.baseboard, WallProxyMask);
            // Inset each box slightly behind the visible faces: a caster exactly on a lit surface shadows
            // itself (stepped bands on walls). Only thin dimensions are inset, so corners stay overlapped.
            const float inset = 0.025f;
            foreach (var (c, sz) in _boxes)
            {
                Vector3 z = sz;
                if (z.x < 0.6f) z.x = Mathf.Max(0.04f, z.x - inset * 2f);
                if (z.z < 0.6f) z.z = Mathf.Max(0.04f, z.z - inset * 2f);
                // Walls, corner posts and door headers (thin boxes that are tall or high up) go in their own
                // mesh. In light grid mode the shader stops real lights at walls exactly (LGrid_LightVisible),
                // so these are switched off there: their coarse shadow maps only added stepped bands and
                // slivers of light in corners. Pillars, racks, lamps and half walls keep casting.
                bool wallLike = Mathf.Min(sz.x, sz.z) <= 0.2f && (sz.y >= 1.5f || c.y - sz.y * 0.5f >= 1.9f);
                (wallLike ? walls : w).Box(c, z, 63, MeshWriter.MetreUV);
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
                else if (f.strip) lens = W(f.on ? _a.stripOn ?? _a.lensOn : _a.stripOff ?? _a.lensOff, f.roomColor);
                else lens = W(f.on ? (f.dome ? _a.domeOn ?? _a.lensOn : _a.lensOn) : (f.dome ? _a.domeOff ?? _a.lensOff : _a.lensOff), f.roomColor);

                if (f.strip)
                {
                    // Warehouse batten: white steel channel with two bare tubes, hung on two chains.
                    var fr = _L.RoomAt(f.cell.x, f.cell.y);
                    float ceil = fr.role == AnnexRole.StairHall ? h + 0.45f : fr.ceiling;      // stair hall battens hang from the sloping ceiling
                    var hw = W(_a.fixtureHousing, f.roomColor);
                    hw.Box(c + Vector3.up * 0.03f, new Vector3(1.24f, 0.06f, 0.16f), 63, MeshWriter.MetreUV);
                    lens.Box(c + new Vector3(0, -0.012f, 0.04f), new Vector3(1.18f, 0.028f, 0.028f), 1 | 2 | 4 | 16 | 32, MeshWriter.MetreUV);
                    lens.Box(c + new Vector3(0, -0.012f, -0.04f), new Vector3(1.18f, 0.028f, 0.028f), 1 | 2 | 4 | 16 | 32, MeshWriter.MetreUV);
                    float drop = ceil - (h + 0.06f);
                    if (drop > 0.02f)
                        for (int k = -1; k <= 1; k += 2)
                            W(_a.lampMetal, f.roomColor).Box(c + new Vector3(k * 0.45f, 0.06f + drop * 0.5f, 0), new Vector3(0.012f, drop, 0.012f), 1 | 2 | 16 | 32, MeshWriter.MetreUV);
                }
                else if (f.dome)
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
                        position = _origin + c + (f.dome ? Vector3.down * 0.07f : f.strip ? Vector3.down * 0.03f : Vector3.zero),
                        isLamp = false, dome = f.dome, strip = f.strip, ceiling = h, roomLayer = LayerBit(f.roomColor),
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
            public bool noShadow;         // shadows come from a few proxy boxes instead of the full mesh
        }

        readonly List<PropInstance> _instances = new List<PropInstance>();

        void Place(GameObject prefab, Vector3 at, float yaw, int color, Vector3? scale = null, bool litShade = false, bool noShadow = false)
        {
            _instances.Add(new PropInstance { prefab = prefab, position = at, rotation = Quaternion.Euler(0f, yaw, 0f),
                scale = scale ?? Vector3.one, mask = LayerBit(color), litShade = litShade, noShadow = noShadow });
        }

        /// <summary>A shadow-only box for a prop (rendering detailed props into every shadow map is costly).</summary>
        void ProxyBox(Vector3 at, float yaw, Vector3 localCenter, Vector3 size)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            WMask(_a.baseboard, ProxyMask).Box(at + rot * localCenter, size, rot, 63, MeshWriter.MetreUV);
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
                    case PropKind.StorageRack: Rack(p, at); break;
                    case PropKind.Box: BoxProp(p, at); break;
                    case PropKind.Pallet: Pallet(p, at, rot); break;
                }
            }
        }

        /// <summary>Props the player can move (built in Instantiate, see Interaction.MovableProp).</summary>
        readonly List<MovableSpec> _movables = new List<MovableSpec>();

        MovableSpec Movable(ulong id, MovableKind kind, Vector3 at, float yaw, int color)
        {
            var s = new MovableSpec { id = id, kind = kind, renderMask = LayerBit(color), proxyMaterial = _a.baseboard, position = _origin + at, yaw = yaw };
            _movables.Add(s);
            return s;
        }

        /// <summary>Loose cardboard box (movable).</summary>
        void BoxProp(Prop p, Vector3 at)
        {
            var s = Movable(p.id, MovableKind.Box, at, p.yaw, p.roomColor);
            s.size = p.size; s.variant = p.variant;
            s.cardboard = true;                                   // built at spawn from CardboardBoxGeometry
            s.materials = new[] { BoardMat, TapeMat, PrintMat };
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
                    var rng = new System.Random(DistrictGenerator.Hash(_L.coord.x, _L.coord.y, (int)(at.x * 31), (int)(at.z * 17)));
                    int fabric = rng.Next(fabricCount);
                    // Movable: the prop builds its own colliders and shadow proxies (Interaction.MovableProp).
                    if (arm)
                        Movable(p.id, MovableKind.Armchair, at, p.yaw, col).AddPrefab(_a.armchairPrefabs[fabric % _a.armchairPrefabs.Length], Vector3.zero);
                    else
                    {
                        int models = Mathf.Max(1, _a.sofaPrefabs.Length / fabricCount);
                        int model = rng.Next(models);
                        Movable(p.id, MovableKind.Sofa, at, p.yaw, col).AddPrefab(_a.sofaPrefabs[model * fabricCount + fabric], Vector3.zero);
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
                    var table = Movable(p.id, MovableKind.Table, at, p.yaw, col);
                    table.size = new Vector3(L, 0.74f, 1.1f);
                    table.AddPrefab(_a.tableModulePrefab, Vector3.zero, 0f, new Vector3(L, 1f, 1f));
                    for (int i = -1; i <= 1; i += 2) table.AddPrefab(_a.tableLegPrefab, new Vector3(i * L * 0.32f, 0, 0));
                    int chair = 0;
                    var rng = new System.Random(DistrictGenerator.Hash(_L.coord.x, _L.coord.y, (int)(p.position.x * 100), (int)(p.position.y * 100)));
                    int perSide = Mathf.Max(1, Mathf.FloorToInt(L / 0.75f));
                    for (int side = -1; side <= 1; side += 2)
                    for (int i = 0; i < perSide; i++)
                    {
                        if (rng.NextDouble() < 0.15) continue;
                        float cx = -L * 0.5f + L * (i + 0.5f) / perSide + (float)(rng.NextDouble() - 0.5) * 0.12f;
                        float cz = side * (0.55f + 0.2f + (float)rng.NextDouble() * 0.25f);
                        float yaw = p.yaw + (side < 0 ? 0f : 180f) + (float)(rng.NextDouble() - 0.5) * 30f;
                        var cpos = at + rot * new Vector3(cx, 0, cz);
                        Movable(MovableSpec.SubId(p.id, chair++), MovableKind.OfficeChair, cpos, yaw, col).AddPrefab(_a.officeChairPrefab, Vector3.zero);
                    }
                    break;
                }
                case PropKind.Box:
                    BoxProp(p, at);
                    break;
                case PropKind.WallClock:
                    if (_a.wallClockPrefab) Place(_a.wallClockPrefab, new Vector3(at.x, p.size.y, at.z), p.yaw, col, null, false, true);
                    break;
                case PropKind.StorageRack:
                    Rack(p, at);
                    break;
                case PropKind.Pallet:
                    Pallet(p, at, rot);
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
            var rng = new System.Random(DistrictGenerator.Hash(_L.coord.x, _L.coord.y, (int)(p.position.x * 100), (int)(p.position.y * 100)));
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
