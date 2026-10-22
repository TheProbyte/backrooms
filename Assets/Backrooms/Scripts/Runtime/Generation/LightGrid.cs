using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// A 3D grid of fixture light that follows the player (0.6 m texels, 76.8 x 14.4 x 76.8 m, a clipmap
    /// with toroidal addressing in x and z, so moving only recomputes the newly exposed slabs). It holds the
    /// light of every fixture that is not currently a real URP light, computed on the GPU from the layout:
    /// direct light is blocked by walls and headers (line of sight through the cell edges), and a second
    /// 2D field carries soft bounce light from lit floors, spreading through doorways. The project's lit
    /// shader (Backrooms/Lit) samples both with wall-aware filtering (see Shaders/LightGridSample.hlsl).
    ///
    /// Only dirty boxes are recomputed: newly exposed slabs, the reach of fixtures whose share or power
    /// changed (see <see cref="MarkDirty"/>), and chunks that loaded or unloaded.
    /// </summary>
    public class LightGrid : MonoBehaviour
    {
        public const int GX = 128, GY = 24, GZ = 128, OccN = GX / 2, TileTexels = 8, Tiles = GX / TileTexels;
        const float T = LightGridMath.Texel, C = LightGridMath.Cell;

        public ComputeShader compute;
        public WorldStreamer streamer;

        [Header("Look")]
        [Tooltip("Multiplier on grid light (1 = the same light the real fixture would give).")]
        public float directGain = 1f;
        [Tooltip("Strength of the soft bounce light from lit floors.")]
        public float bounceGain = 1.0f;
        [Tooltip("Share of floor light reflected back up (carpet albedo).")]
        public float floorAlbedo = 0.25f;
        [Tooltip("How far bounce light spreads into neighbouring cells (0..1 per step).")]
        [Range(0f, 1f)] public float bounceSpread = 0.6f;
        [Range(0, 12)] public int bounceSteps = 6;
        [Tooltip("Lambert wrap for grid light (0 = plain Lambert).")]
        [Range(0f, 1f)] public float wrap = 0.15f;
        [Tooltip("Sample point offset off the surface, metres (half a texel keeps samples on the room's side).")]
        public float normalOffset = 0.3f;

        public bool Ready { get; private set; }
        public int DirtyTexelsLastFrame { get; private set; }
        public float LastGpuSubmitMs { get; private set; }

        RenderTexture _a, _b, _bounceSrc, _bounce0, _bounce1;
        ComputeBuffer _occ;                  // layout occupancy: OccN x OccN cells x 2 storeys, toroidal
        readonly uint[] _occData = new uint[OccN * OccN * 2];
        ComputeBuffer _lights, _tiles, _tileLights;
        LightGridMath.GridLight[] _lightArr = new LightGridMath.GridLight[256];
        int _lightCount;
        readonly uint[] _tileArr = new uint[Tiles * Tiles * 2];
        readonly List<uint> _tileList = new List<uint>(4096);
        readonly List<int>[] _tileBins = new List<int>[Tiles * Tiles];
        Vector2Int _texMin = new Vector2Int(int.MinValue, int.MinValue);
        bool _occDirty = true, _full = true;
        readonly List<(Vector3Int min, Vector3Int max)> _dirty = new List<(Vector3Int, Vector3Int)>();
        readonly Dictionary<Vector3Int, uint[]> _encoded = new Dictionary<Vector3Int, uint[]>();
        int _kDirect = -1, _kBounceSrc, _kBounceDiff;
        float _pitch = 9f;

        static readonly int IdA = Shader.PropertyToID("_GridA"), IdB = Shader.PropertyToID("_GridB"), IdOcc = Shader.PropertyToID("_Occ"),
            IdLights = Shader.PropertyToID("_Lights"), IdTiles = Shader.PropertyToID("_Tiles"), IdTileLights = Shader.PropertyToID("_TileLights"),
            IdGridSize = Shader.PropertyToID("_GridSize"), IdBoxMin = Shader.PropertyToID("_BoxMin"), IdBoxSize = Shader.PropertyToID("_BoxSize"),
            IdTileInfo = Shader.PropertyToID("_TileInfo"), IdMisc = Shader.PropertyToID("_GridMisc"), IdCellMin = Shader.PropertyToID("_CellMin"),
            IdBounceSrc = Shader.PropertyToID("_BounceSrc"), IdBounceSrcIn = Shader.PropertyToID("_BounceSrcIn"),
            IdBounceIn = Shader.PropertyToID("_BounceIn"), IdBounceOut = Shader.PropertyToID("_BounceOut");
        static readonly int GA = Shader.PropertyToID("_LGridA"), GB = Shader.PropertyToID("_LGridB"), GBounce = Shader.PropertyToID("_LGridBounce"),
            GOcc = Shader.PropertyToID("_LGridOcc"), GSize = Shader.PropertyToID("_LGridSize"), GParams = Shader.PropertyToID("_LGridParams"),
            GWindow = Shader.PropertyToID("_LGridWindow"), GShape = Shader.PropertyToID("_LGridShape");

        public static bool Supported => SystemInfo.supportsComputeShaders;

        void OnEnable()
        {
            for (int i = 0; i < _tileBins.Length; i++) _tileBins[i] = new List<int>(32);
            if (!Supported || !compute) { Debug.LogWarning("[Backrooms] Light grid unavailable (no compute shader support or shader missing)."); enabled = false; return; }
            _kDirect = compute.FindKernel("Direct");
            _kBounceSrc = compute.FindKernel("BounceSource");
            _kBounceDiff = compute.FindKernel("BounceDiffuse");
            _a = Make3D("LightGridA"); _b = Make3D("LightGridB");
            _bounceSrc = Make2D("LightGridBounceSrc"); _bounce0 = Make2D("LightGridBounce0"); _bounce1 = Make2D("LightGridBounce1");
            _occ = new ComputeBuffer(OccN * OccN * 2, 4);
            _lights = new ComputeBuffer(_lightArr.Length, LightGridMath.GridLight.Stride);
            _tiles = new ComputeBuffer(Tiles * Tiles, 8);
            _tileLights = new ComputeBuffer(8192, 4);
            if (streamer) { streamer.ChunkLoaded += OnChunkChanged; streamer.ChunkUnloaded += OnChunkUnloaded; }
            Shader.SetGlobalTexture(GA, _a); Shader.SetGlobalTexture(GB, _b); Shader.SetGlobalBuffer(GOcc, _occ);
            Shader.SetGlobalTexture(GBounce, _bounce0);
            _full = true; _occDirty = true;
            Ready = true;
        }

        void OnDisable()
        {
            Ready = false;
            LightGridGlobals.BindFallbacks();          // never leave the shader pointing at released resources
            if (streamer) { streamer.ChunkLoaded -= OnChunkChanged; streamer.ChunkUnloaded -= OnChunkUnloaded; }
            void Rel(RenderTexture r) { if (r) { r.Release(); Destroy(r); } }
            Rel(_a); Rel(_b); Rel(_bounceSrc); Rel(_bounce0); Rel(_bounce1);
            _occ?.Release(); _lights?.Release(); _tiles?.Release(); _tileLights?.Release();
            _occ = _lights = _tiles = _tileLights = null;
            _encoded.Clear();
        }

        static RenderTexture Make3D(string name)
        {
            var r = new RenderTexture(GX, GY, 0, RenderTextureFormat.ARGBHalf)
            {
                dimension = UnityEngine.Rendering.TextureDimension.Tex3D, volumeDepth = GZ, enableRandomWrite = true, name = name,
                wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, wrapModeW = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
            };
            r.Create();
            return r;
        }

        static RenderTexture Make2D(string name)
        {
            var r = new RenderTexture(OccN, OccN, 0, RenderTextureFormat.ARGBHalf)
            { enableRandomWrite = true, name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            r.Create();
            return r;
        }

        // ---------------- Inputs ----------------

        /// <summary>
        /// The lights the grid draws this frame: every powered fixture near the player, each with its grid
        /// share (colour) and its full light (colorAll, for bounce). Cheap: only uploads and bins them.
        /// </summary>
        void SetLights(List<LightGridMath.GridLight> lights)
        {
            if (lights.Count > _lightArr.Length)
            {
                int size = Mathf.NextPowerOfTwo(lights.Count);
                _lightArr = new LightGridMath.GridLight[size];
                _lights.Release(); _lights = new ComputeBuffer(size, LightGridMath.GridLight.Stride);
            }
            _lightCount = lights.Count;
            for (int i = 0; i < lights.Count; i++) _lightArr[i] = lights[i];
            _lights.SetData(_lightArr, 0, 0, _lightCount);

            // Bin lights into the window's tiles (absolute tile -> wrapped slot).
            foreach (var b in _tileBins) b.Clear();
            int tminX = FloorDiv(_texMin.x, TileTexels), tminZ = FloorDiv(_texMin.y, TileTexels);
            for (int i = 0; i < _lightCount; i++)
            {
                var L = _lightArr[i];
                int x0 = Mathf.FloorToInt((L.pos.x - L.range) / (T * TileTexels)), x1 = Mathf.FloorToInt((L.pos.x + L.range) / (T * TileTexels));
                int z0 = Mathf.FloorToInt((L.pos.z - L.range) / (T * TileTexels)), z1 = Mathf.FloorToInt((L.pos.z + L.range) / (T * TileTexels));
                for (int tx = Mathf.Max(x0, tminX); tx <= Mathf.Min(x1, tminX + Tiles - 1); tx++)
                for (int tz = Mathf.Max(z0, tminZ); tz <= Mathf.Min(z1, tminZ + Tiles - 1); tz++)
                    _tileBins[Wrap(tz, Tiles) * Tiles + Wrap(tx, Tiles)].Add(i);
            }
            _tileList.Clear();
            for (int t = 0; t < _tileBins.Length; t++)
            {
                _tileArr[t * 2] = (uint)_tileList.Count;
                _tileArr[t * 2 + 1] = (uint)_tileBins[t].Count;
                foreach (int i in _tileBins[t]) _tileList.Add((uint)i);
            }
            if (_tileList.Count > _tileLights.count) { _tileLights.Release(); _tileLights = new ComputeBuffer(Mathf.NextPowerOfTwo(_tileList.Count), 4); }
            _tiles.SetData(_tileArr);
            if (_tileList.Count > 0) _tileLights.SetData(_tileList);
        }

        /// <summary>Recompute the grid within radius of a point (a light whose share or power changed).</summary>
        public void MarkDirty(Vector3 centre, float radius)
        {
            var min = new Vector3Int(Mathf.FloorToInt((centre.x - radius) / T), Mathf.FloorToInt((centre.y - radius) / T), Mathf.FloorToInt((centre.z - radius) / T));
            var max = new Vector3Int(Mathf.CeilToInt((centre.x + radius) / T), Mathf.CeilToInt((centre.y + radius) / T), Mathf.CeilToInt((centre.z + radius) / T));
            _dirty.Add((min, max));
        }

        void OnChunkChanged(Vector3Int c, GameObject go)
        {
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) _encoded.Remove(new Vector3Int(c.x + dx, c.y, c.z + dz));
            _occDirty = true;
            float S = OfficeBiome.ChunkSize, reach = 18f;
            var centre = new Vector3((c.x + 0.5f) * S, c.y * _pitch + 4f, (c.z + 0.5f) * S);
            MarkDirty(centre, S * 0.75f + reach);
        }

        void OnChunkUnloaded(Vector3Int c, GameObject go) { _encoded.Remove(c); OnChunkChanged(c, go); _encoded.Remove(c); }

        // ---------------- Per frame ----------------

        /// <summary>Moves the window, uploads this frame's lights, refreshes occupancy, recomputes dirty boxes and the bounce field.</summary>
        public void Tick(Vector3 viewer, List<LightGridMath.GridLight> lights)
        {
            if (!Ready || _kDirect < 0) return;
            _pitch = streamer && streamer.Settings != null ? streamer.Settings.storeyPitch : 9f;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Window: centred on the viewer, snapped to whole layout cells (two texels).
            var newMin = new Vector2Int(FloorDiv(Mathf.FloorToInt(viewer.x / C), 1) * 2 - GX / 2, FloorDiv(Mathf.FloorToInt(viewer.z / C), 1) * 2 - GZ / 2);
            if (newMin != _texMin)
            {
                if (_texMin.x == int.MinValue || Mathf.Abs(newMin.x - _texMin.x) >= GX || Mathf.Abs(newMin.y - _texMin.y) >= GZ) _full = true;
                else
                {
                    // Newly exposed slabs.
                    if (newMin.x > _texMin.x) _dirty.Add((new Vector3Int(_texMin.x + GX, 0, newMin.y), new Vector3Int(newMin.x + GX, GY, newMin.y + GZ)));
                    if (newMin.x < _texMin.x) _dirty.Add((new Vector3Int(newMin.x, 0, newMin.y), new Vector3Int(_texMin.x, GY, newMin.y + GZ)));
                    if (newMin.y > _texMin.y) _dirty.Add((new Vector3Int(newMin.x, 0, _texMin.y + GZ), new Vector3Int(newMin.x + GX, GY, newMin.y + GZ)));
                    if (newMin.y < _texMin.y) _dirty.Add((new Vector3Int(newMin.x, 0, newMin.y), new Vector3Int(newMin.x + GX, GY, _texMin.y)));
                }
                _texMin = newMin;
                _occDirty = true;
            }
            if (_occDirty) RebuildOccupancy();
            SetLights(lights);

            var cellMin = new Vector2Int(_texMin.x / 2, _texMin.y / 2);
            compute.SetInts(IdGridSize, GX, GY, GZ, OccN);
            compute.SetInts(IdTileInfo, TileTexels, Tiles, 0, 0);
            compute.SetVector(IdMisc, new Vector4(_pitch, floorAlbedo, bounceSpread, 0f));
            compute.SetInts(IdCellMin, cellMin.x, cellMin.y, 0, 0);

            // Direct light in the dirty boxes (clipped to the window; merged into one full pass if large).
            int texels = 0;
            compute.SetTexture(_kDirect, IdA, _a); compute.SetTexture(_kDirect, IdB, _b); compute.SetBuffer(_kDirect, IdOcc, _occ);
            compute.SetBuffer(_kDirect, IdLights, _lights); compute.SetBuffer(_kDirect, IdTiles, _tiles); compute.SetBuffer(_kDirect, IdTileLights, _tileLights);
            if (!_full)
            {
                long volume = 0;
                foreach (var (mn, mx) in _dirty) volume += (long)Mathf.Max(0, mx.x - mn.x) * Mathf.Max(0, mx.y - mn.y) * Mathf.Max(0, mx.z - mn.z);
                if (volume > (long)GX * GY * GZ / 2 || _dirty.Count > 48) _full = true;
            }
            if (_full) { texels += Dispatch(new Vector3Int(_texMin.x, 0, _texMin.y), new Vector3Int(_texMin.x + GX, GY, _texMin.y + GZ)); _full = false; }
            else foreach (var (mn, mx) in _dirty) texels += Dispatch(mn, mx);
            _dirty.Clear();
            DirtyTexelsLastFrame = texels;

            // Bounce: floor light from every powered fixture, then a few diffusion steps.
            if (_lightCount > 0 && bounceGain > 0f)
            {
                compute.SetBuffer(_kBounceSrc, IdOcc, _occ); compute.SetTexture(_kBounceSrc, IdBounceSrc, _bounceSrc);
                compute.SetBuffer(_kBounceSrc, IdLights, _lights); compute.SetBuffer(_kBounceSrc, IdTiles, _tiles); compute.SetBuffer(_kBounceSrc, IdTileLights, _tileLights);
                compute.Dispatch(_kBounceSrc, OccN / 8, OccN / 8, 1);
                compute.SetBuffer(_kBounceDiff, IdOcc, _occ); compute.SetTexture(_kBounceDiff, IdBounceSrcIn, _bounceSrc);
                RenderTexture src = _bounceSrc, dst = _bounce0;
                int steps = Mathf.Max(1, bounceSteps);
                for (int i = 0; i < steps; i++)
                {
                    dst = (i & 1) == 0 ? _bounce0 : _bounce1;
                    compute.SetTexture(_kBounceDiff, IdBounceIn, src); compute.SetTexture(_kBounceDiff, IdBounceOut, dst);
                    compute.Dispatch(_kBounceDiff, OccN / 8, OccN / 8, 1);
                    src = dst;
                }
                Shader.SetGlobalTexture(GBounce, dst);
            }

            Shader.SetGlobalVector(GSize, new Vector4(GX, GY, GZ, OccN));
            Shader.SetGlobalVector(GParams, new Vector4(1f, directGain, bounceGain > 0f && _lightCount > 0 ? bounceGain : 0f, _pitch));
            Shader.SetGlobalVector(GWindow, new Vector4(_texMin.x * T, _texMin.y * T, (_texMin.x + GX) * T, (_texMin.y + GZ) * T));
            Shader.SetGlobalVector(GShape, new Vector4(wrap, normalOffset, 0f, 0f));
            LastGpuSubmitMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        int Dispatch(Vector3Int min, Vector3Int max)
        {
            // Clip to the window.
            min = Vector3Int.Max(min, new Vector3Int(_texMin.x, 0, _texMin.y));
            max = Vector3Int.Min(max, new Vector3Int(_texMin.x + GX, GY, _texMin.y + GZ));
            var size = max - min;
            if (size.x <= 0 || size.y <= 0 || size.z <= 0) return 0;
            compute.SetInts(IdBoxMin, min.x, min.y, min.z, 0);
            compute.SetInts(IdBoxSize, size.x, size.y, size.z, 0);
            compute.Dispatch(_kDirect, (size.x + 3) / 4, (size.y + 3) / 4, (size.z + 3) / 4);
            return size.x * size.y * size.z;
        }

        /// <summary>Packs the loaded layouts under the window into the occupancy texture (storeys 0 and 1).</summary>
        void RebuildOccupancy()
        {
            _occDirty = false;
            System.Array.Clear(_occData, 0, _occData.Length);
            if (!streamer) { UploadOccupancy(); return; }
            int N = OfficeBiome.ChunkCells;
            var cellMin = new Vector2Int(_texMin.x / 2, _texMin.y / 2);
            for (int s = 0; s < 2; s++)
            for (int cz = FloorDiv(cellMin.y, N); cz <= FloorDiv(cellMin.y + OccN - 1, N); cz++)
            for (int cx = FloorDiv(cellMin.x, N); cx <= FloorDiv(cellMin.x + OccN - 1, N); cx++)
            {
                var key = new Vector3Int(cx, s, cz);
                if (!_encoded.TryGetValue(key, out var enc))
                {
                    var L = streamer.LayoutAt(key);
                    if (L == null) continue;
                    enc = new uint[N * N];
                    int ccx = cx, ccz = cz, ss = s;
                    OfficeLayout Nb(int dx, int dz) => streamer.LayoutAt(new Vector3Int(ccx + dx, ss, ccz + dz));
                    for (int x = 0; x < N; x++) for (int z = 0; z < N; z++) enc[z * N + x] = LightGridMath.Encode(L, x, z, Nb);
                    _encoded[key] = enc;
                }
                for (int x = 0; x < N; x++)
                for (int z = 0; z < N; z++)
                {
                    int gx = cx * N + x, gz = cz * N + z;
                    if (gx < cellMin.x || gz < cellMin.y || gx >= cellMin.x + OccN || gz >= cellMin.y + OccN) continue;
                    _occData[s * OccN * OccN + Wrap(gz, OccN) * OccN + Wrap(gx, OccN)] = enc[z * N + x];
                }
            }
            UploadOccupancy();
        }

        void UploadOccupancy() => _occ.SetData(_occData);

        /// <summary>Occupancy of a cell as currently uploaded (0 = outside the window or not loaded).</summary>
        public uint OccupancyAt(int cellX, int cellZ, int storey)
        {
            var cellMin = new Vector2Int(_texMin.x / 2, _texMin.y / 2);
            if (storey < 0 || storey > 1 || cellX < cellMin.x || cellZ < cellMin.y || cellX >= cellMin.x + OccN || cellZ >= cellMin.y + OccN) return 0;
            return _occData[storey * OccN * OccN + Wrap(cellZ, OccN) * OccN + Wrap(cellX, OccN)];
        }

        static int Wrap(int v, int n) { int m = v % n; return m < 0 ? m + n : m; }
        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
    }
}
