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
    public partial class LightGrid : MonoBehaviour
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
        [Tooltip("Rounds off the grid's texel steps so distant light pools are soft blobs instead of boxes (0 = plain trilinear).")]
        [Range(0f, 1f)] public float smoothing = 0.5f;
        [Tooltip("Deepens dim areas of grid light: 0 leaves it as is, 1 makes weakly lit spots clearly darker.")]
        [Range(0f, 1f)] public float contrast = 0f;
        [Range(0f, 1f)] [Tooltip("Evens out walls with bounced light (a stand-in for global illumination): 0 = the old low wall fill, 1 = walls almost as lit as the ceiling above them.")] public float roomFill = 0.6f;
        [Tooltip("Evaluate the grid's lights per pixel in the shader (exact pools) instead of reading the 0.6 m texture. Bounce still comes from the grid.")]
        public bool perPixelDirect = true;
        [Tooltip("With per-pixel light on: each fixture is exact per pixel on surfaces within this distance of it, and read from the 0.6 m texture farther away (blending over 2 m). 0 = every fixture exact everywhere (expensive).")]
        public float exactDistance = 4.5f;
        /// <summary>The 0.6 m direct-light texture is in use (texture mode, or the far part of each fixture in split mode).</summary>
        bool TextureDirect => !perPixelDirect || exactDistance > 0f;
        /// <summary>The direct-light pass runs: for the texture, or for the haze channel.</summary>
        bool DirectActive => TextureDirect || HazeOn;
        bool _tracedApplied;
        float Split => perPixelDirect ? Mathf.Max(0f, exactDistance) : 0f;
        float _appliedSplit = -1f;
        [Tooltip("Sample point offset off the surface, metres (half a texel keeps samples on the room's side).")]
        public float normalOffset = 0.3f;

        [Header("Site")]
        [Tooltip("Where the window is centred. Unset: on the player (the main grid). Set: on a far crawl-hole site (the site grid).")]
        public Vector3? siteCentre;
        [Tooltip("Bind this grid's resources as the shader globals every frame (the main grid). The site grid is bound only for the portal views that show its site (BindGlobals).")]
        public bool bindGlobals = true;

        public bool Ready { get; private set; }
        public int DirtyTexelsLastFrame { get; private set; }
        public float LastGpuSubmitMs { get; private set; }
        public int BounceDispatchesLastFrame { get; private set; }
        public int BufferUploadsLastFrame { get; private set; }
        public int LightingRevision { get; private set; }

        RenderTexture _a, _b, _bounceSrc, _bounce0, _bounce1;
        ComputeBuffer _occ;                  // layout occupancy: OccN x OccN cells x 2 storeys, toroidal
        RenderTexture _bounceCur;            // the bounce field the shader samples (the last diffuse step's output)
        readonly uint[] _occData = new uint[OccN * OccN * 2];
        ComputeBuffer _lights, _tiles, _tileLights, _fixtureLookup;
        uint[] _fixtureLookupData = new uint[4];
        LightGridMath.GridLight[] _lightArr = new LightGridMath.GridLight[256];
        int _lightCount;
        readonly uint[] _tileArr = new uint[Tiles * Tiles * 2];
        readonly List<uint> _tileList = new List<uint>(4096);
        readonly List<int>[] _tileBins = new List<int>[Tiles * Tiles];
        Vector2Int _texMin = new Vector2Int(int.MinValue, int.MinValue);
        bool _occDirty = true, _full = true;
        bool _inputsValid, _bounceDirty = true, _wasTexture;
        Vector2Int _binnedMin;
        Vector4 _bounceParameters;
        readonly List<(Vector3Int min, Vector3Int max)> _dirty = new List<(Vector3Int, Vector3Int)>();
        readonly List<(Vector3Int min, Vector3Int max)> _pending = new List<(Vector3Int, Vector3Int)>();
        [Tooltip("Most direct-light texels recomputed per frame (the rest wait for the next frames).")]
        public int texelBudget = GX * GY * GZ / 4;
        /// <summary>Texels still queued for recomputing.</summary>
        public long PendingTexels { get { long v = 0; foreach (var b in _pending) v += Volume(Clip(b.min, b.max)); return v; } }
        /// <summary>The texture only holds the weak far part of each light (per-pixel split): small share changes need not be redrawn at once.</summary>
        public bool CoarseUpdates => perPixelDirect && Split > 0f;
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
            GWindow = Shader.PropertyToID("_LGridWindow"), GShape = Shader.PropertyToID("_LGridShape"), GFill = Shader.PropertyToID("_LGridFillAmt"),
            GLights = Shader.PropertyToID("_LGridLights"), GTiles = Shader.PropertyToID("_LGridTiles"), GTileLights = Shader.PropertyToID("_LGridTileLights"), GFar = Shader.PropertyToID("_LGridFar");

        public static bool Supported => SystemInfo.supportsComputeShaders;

        /// <summary>Bind the live field for a bounded portal irradiance sampling dispatch. Main thread only.</summary>
        public bool BindPortalLighting(ComputeShader shader, int kernel)
        {
            if (!Ready || !isActiveAndEnabled || _texMin.x == int.MinValue) return false;
            shader.SetTexture(kernel, "_LGridA", _a); shader.SetTexture(kernel, "_LGridB", _b);
            shader.SetTexture(kernel, "_LGridBounce", _bounceCur ? _bounceCur : _bounce0);
            shader.SetTexture(kernel, "_LGridWallBounce", _wallCur ? _wallCur : _wall0);
            shader.SetTexture(kernel, "_LGridBounceDirection", _directionCur ? _directionCur : _direction0);
            shader.SetBuffer(kernel, "_LGridOcc", _occ); shader.SetBuffer(kernel, "_LGridLights", _lights);
            shader.SetBuffer(kernel, "_LGridTiles", _tiles); shader.SetBuffer(kernel, "_LGridTileLights", _tileLights);
            shader.SetBuffer(kernel, "_LocalOccBoxes", _occluders); shader.SetBuffer(kernel, "_LocalOccTiles", _occluderTiles);
            shader.SetBuffer(kernel, "_LocalOccIndices", _occluderIndices);
            shader.SetVector("_LocalOccInfo", new Vector4(T * TileTexels, Tiles, OccluderCount, contactOcclusion));
            shader.SetVector("_LGridSize", new Vector4(GX, GY, GZ, OccN));
            shader.SetVector("_LGridParams", new Vector4(1, directGain, _lightCount > 0 ? bounceGain : 0, _pitch));
            shader.SetVector("_LGridWindow", new Vector4(_texMin.x * T, _texMin.y * T, (_texMin.x + GX) * T, (_texMin.y + GZ) * T));
            shader.SetVector("_LGridShape", new Vector4(wrap, normalOffset, smoothing, contrast));
            shader.SetVector("_LGridFar", new Vector4(perPixelDirect ? 1 : 0, TileTexels, Tiles, Split));
            shader.SetFloat("_LGridFillAmt", roomFill);
            BindProbesTo(shader, kernel);
            BindHolesTo(shader, kernel);
            return true;
        }

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
            InitSurfaces(); InitOccluders(); InitHoles();
            _lights = new ComputeBuffer(_lightArr.Length, LightGridMath.GridLight.Stride);
            _tiles = new ComputeBuffer(Tiles * Tiles, 8);
            _tileLights = new ComputeBuffer(8192, 4);
            _fixtureLookup = new ComputeBuffer(4, 4); _fixtureLookup.SetData(new uint[4]);
            if (streamer) { streamer.ChunkLoaded += OnChunkChanged; streamer.ChunkUnloaded += OnChunkUnloaded; }
            DoorEdges.Changed += OnDoorChanged;
            if (bindGlobals)
            {
                Shader.SetGlobalTexture(GA, _a); Shader.SetGlobalTexture(GB, _b); Shader.SetGlobalBuffer(GOcc, _occ);
                Shader.SetGlobalTexture(GBounce, _bounce0);
            }
            _full = true; _occDirty = true; _inputsValid = false; _bounceDirty = true;
            Ready = true;
        }

        void OnDisable()
        {
            Ready = false;
            if (bindGlobals) LightGridGlobals.BindFallbacks();          // never leave the shader pointing at released resources
            if (streamer) { streamer.ChunkLoaded -= OnChunkChanged; streamer.ChunkUnloaded -= OnChunkUnloaded; }
            DoorEdges.Changed -= OnDoorChanged;
            void Rel(RenderTexture r) { if (r) { r.Release(); if (Application.isPlaying) Destroy(r); else DestroyImmediate(r); } }
            Rel(_a); Rel(_b); Rel(_bounceSrc); Rel(_bounce0); Rel(_bounce1);
            _occ?.Release(); _lights?.Release(); _tiles?.Release(); _tileLights?.Release(); _fixtureLookup?.Release();
            _occ = _lights = _tiles = _tileLights = _fixtureLookup = null;
            _encoded.Clear();
            ReleaseSurfaces(); ReleaseOccluders(); ReleaseProbes(); ReleaseHoles();
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
            var r = new RenderTexture(OccN, OccN * 2, 0, RenderTextureFormat.ARGBHalf)
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
            bool upload = !_inputsValid || lights.Count != _lightCount;
            bool lookup = upload;
            bool bins = upload || _binnedMin != _texMin;
            bool bounce = bins;
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                if (i >= _lightCount || !_inputsValid) { upload = bins = bounce = true; continue; }
                var old = _lightArr[i];
                bool geometry = !l.pos.Equals(old.pos) || l.range != old.range;
                lookup |= !l.pos.Equals(old.pos);
                bool illumination = geometry || !l.dir.Equals(old.dir) || l.cosOuter != old.cosOuter || l.cosInner != old.cosInner || l.storey != old.storey || !l.halfSize.Equals(old.halfSize);
                bins |= geometry;
                // Bounce: the light a fixture gives to bounce (power smoothed), with a small tolerance.
                Vector3 bounceNew = l.colorAll * (1f + l.padding.x), bounceOld = old.colorAll * (1f + old.padding.x);
                bounce |= illumination || (bounceNew - bounceOld).sqrMagnitude > 1e-4f * Mathf.Max(bounceNew.sqrMagnitude, bounceOld.sqrMagnitude) + 1e-10f;
                upload |= illumination || !l.color.Equals(old.color) || !l.colorAll.Equals(old.colorAll) || !l.padding.Equals(old.padding);
            }
            _bounceDirty |= bounce;
            if (bounce) LightingRevision++;
            if (!upload && !bins) return;
            if (lights.Count > _lightArr.Length)
            {
                int size = Mathf.NextPowerOfTwo(lights.Count);
                _lightArr = new LightGridMath.GridLight[size];
                _lights.Release(); _lights = new ComputeBuffer(size, LightGridMath.GridLight.Stride);
            }
            _lightCount = lights.Count;
            for (int i = 0; i < lights.Count; i++) _lightArr[i] = lights[i];
            if (lookup)
            {
                _fixtureLookupData = LightGridMath.BuildFixtureLookup(lights, _fixtureLookupData);
                if (_fixtureLookup.count != _fixtureLookupData.Length)
                {
                    _fixtureLookup.Release(); _fixtureLookup = new ComputeBuffer(_fixtureLookupData.Length, 4);
                }
                _fixtureLookup.SetData(_fixtureLookupData); BufferUploadsLastFrame++;
            }
            if (upload && _lightCount > 0) { _lights.SetData(_lightArr, 0, 0, _lightCount); BufferUploadsLastFrame++; }
            _inputsValid = true;
            if (!bins) return;
            _binnedMin = _texMin;

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
            BufferUploadsLastFrame += _tileList.Count > 0 ? 2 : 1;
        }

        /// <summary>Recompute the grid within radius of a point (a light whose share or power changed).</summary>
        /// <summary>Recompute every texel (a light setting that changes every fixture's output, such as strength or reach).</summary>
        public void MarkAllDirty() { _full = true; _bounceDirty = true; }

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
            _bounceDirty = true;
            float S = OfficeBiome.ChunkSize, reach = 18f;
            var centre = new Vector3((c.x + 0.5f) * S, c.y * _pitch + 4f, (c.z + 0.5f) * S);
            MarkDirty(centre, S * 0.75f + reach);
        }

        /// <summary>A door shut or opened: its chunk is encoded again (the edge is a wall or an opening) and the light
        /// around it is drawn again.</summary>
        void OnDoorChanged(DoorEdges.Edge e, bool shut)
        {
            _encoded.Remove(e.chunk);
            _occDirty = true;
            _bounceDirty = true;
            MarkDirty(e.centre, 14f);
        }

        void OnChunkUnloaded(Vector3Int c, GameObject go) { _encoded.Remove(c); OnChunkChanged(c, go); _encoded.Remove(c); }

        // ---------------- Per frame ----------------

        /// <summary>Moves the window, uploads this frame's lights, refreshes occupancy, recomputes dirty boxes and the bounce field.</summary>
        static readonly Unity.Profiling.ProfilerMarker s_pOcc = new Unity.Profiling.ProfilerMarker("LightGrid.Occupancy"),
            s_pLights = new Unity.Profiling.ProfilerMarker("LightGrid.SetLights"), s_pHaze = new Unity.Profiling.ProfilerMarker("LightGrid.Haze"),
            s_pOccluders = new Unity.Profiling.ProfilerMarker("LightGrid.Occluders"), s_pProbes = new Unity.Profiling.ProfilerMarker("LightGrid.Probes"),
            s_pBind = new Unity.Profiling.ProfilerMarker("LightGrid.Bind");

        public void Tick(Vector3 viewer, List<LightGridMath.GridLight> lights)
        {
            if (!Ready || _kDirect < 0) return;
            _pitch = streamer && streamer.Settings != null ? streamer.Settings.storeyPitch : 9f;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            BufferUploadsLastFrame = BounceDispatchesLastFrame = 0;
            if (!_wasTexture && DirectActive) _full = true;      // the texture was not kept up to date
            _wasTexture = DirectActive;
            if (HazeOn != _hazeApplied) { _full = true; _hazeApplied = HazeOn; }   // the haze channel is filled with the texture
            if (tracedBounce != _tracedApplied) { _bounceDirty = true; _tracedApplied = tracedBounce; }
            if (TextureDirect && Split != _appliedSplit) _full = true;   // the texture holds a different share of each light
            _appliedSplit = Split;

            // Follow locomotion rather than handheld camera shake, which can straddle a cell boundary at rest.
            var anchor = siteCentre ?? (streamer && streamer.player ? streamer.player.position : viewer);
            var newMin = new Vector2Int(FloorDiv(Mathf.FloorToInt(anchor.x / C), 1) * 2 - GX / 2, FloorDiv(Mathf.FloorToInt(anchor.z / C), 1) * 2 - GZ / 2);
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
            using (s_pOcc.Auto()) if (_occDirty) { RebuildOccupancy(); _bounceDirty = true; }
            UpdateHoles();
            using (s_pLights.Auto()) SetLights(lights);
            using (s_pHaze.Auto()) TrackHazeLights(lights);
            using (s_pOccluders.Auto()) UpdateOccluders(viewer);
            var bounceParameters = new Vector4(floorAlbedo, bounceSpread, bounceSteps, _pitch);
            if (!_bounceParameters.Equals(bounceParameters)) { _bounceDirty = true; _bounceParameters = bounceParameters; }

            var cellMin = new Vector2Int(_texMin.x / 2, _texMin.y / 2);
            compute.SetInts(IdGridSize, GX, GY, GZ, OccN);
            compute.SetInts(IdTileInfo, TileTexels, Tiles, 0, 0);
            compute.SetVector(IdMisc, new Vector4(_pitch, floorAlbedo, bounceSpread, Split));
            compute.SetVector("_GridMisc2", new Vector4(HazeOn ? 1f : 0f, 0f, 0f, 0f));
            compute.SetInts(IdCellMin, cellMin.x, cellMin.y, 0, 0);

            // Direct light in the dirty boxes, clipped to the window. The work is queued and done within a texel budget
            // per frame: a full refresh (or a burst of changes while the world streams in) is spread over a few frames
            // instead of stalling the GPU in one.
            int texels = 0;
            if (DirectActive)
            {
                compute.SetTexture(_kDirect, IdA, _a); compute.SetTexture(_kDirect, IdB, _b); compute.SetBuffer(_kDirect, IdOcc, _occ);
                compute.SetBuffer(_kDirect, IdLights, _lights); compute.SetBuffer(_kDirect, IdTiles, _tiles); compute.SetBuffer(_kDirect, IdTileLights, _tileLights);
                BindHolesTo(compute, _kDirect);
                _pending.AddRange(_dirty);
                long volume = 0;
                foreach (var (mn, mx) in _pending) volume += Volume(Clip(mn, mx));
                if (_full || volume > (long)GX * GY * GZ || _pending.Count > 96)
                {
                    _pending.Clear();
                    for (int z = 0; z < GZ; z += 16)
                        _pending.Add((new Vector3Int(_texMin.x, 0, _texMin.y + z), new Vector3Int(_texMin.x + GX, GY, _texMin.y + Mathf.Min(GZ, z + 16))));
                    _full = false;
                }
                long budget = Mathf.Max(GX * GY * 4, texelBudget);
                int done = 0;
                while (done < _pending.Count && budget > 0)
                {
                    var (mn, mx) = Clip(_pending[done].min, _pending[done].max);
                    long v = Volume((mn, mx));
                    if (v <= 0) { done++; continue; }
                    if (v <= budget) { texels += Dispatch(mn, mx); budget -= v; done++; continue; }
                    // Part of it: whole z-slices up to the budget, the rest stays queued.
                    int cut = mn.z + Mathf.Max(1, (int)(budget / ((long)(mx.x - mn.x) * (mx.y - mn.y))));
                    texels += Dispatch(mn, new Vector3Int(mx.x, mx.y, cut));
                    _pending[done] = (new Vector3Int(mn.x, mn.y, cut), mx);
                    budget = 0;
                }
                _pending.RemoveRange(0, done);
            }
            else _pending.Clear();
            _dirty.Clear();
            DirtyTexelsLastFrame = texels;

            // Bounce: traced probes, or (the older way) floor light from every powered fixture, then a few diffusion steps.
            if (tracedBounce)
            {
                if (_lightCount > 0 && bounceGain > 0f) using (s_pProbes.Auto()) TickProbes();
            }
            else if (_bounceDirty && _lightCount > 0 && bounceGain > 0f)
            {
                compute.SetBuffer(_kBounceSrc, IdOcc, _occ); compute.SetTexture(_kBounceSrc, IdBounceSrc, _bounceSrc);
                compute.SetBuffer(_kBounceSrc, IdLights, _lights); compute.SetBuffer(_kBounceSrc, IdTiles, _tiles); compute.SetBuffer(_kBounceSrc, IdTileLights, _tileLights);
                BindHolesTo(compute, _kBounceSrc);
                BindBounceSurfaces();
                compute.Dispatch(_kBounceSrc, OccN / 8, OccN / 8, 2);
                BounceDispatchesLastFrame++;
                compute.SetBuffer(_kBounceDiff, IdOcc, _occ); compute.SetTexture(_kBounceDiff, IdBounceSrcIn, _bounceSrc);
                RenderTexture src = _bounceSrc, dst = _bounce0;
                int steps = Mathf.Max(1, bounceSteps);
                for (int i = 0; i < steps; i++)
                {
                    dst = (i & 1) == 0 ? _bounce0 : _bounce1;
                    compute.SetTexture(_kBounceDiff, IdBounceIn, src); compute.SetTexture(_kBounceDiff, IdBounceOut, dst);
                    BindBounceDiffusion(i);
                    compute.Dispatch(_kBounceDiff, OccN / 8, OccN / 8, 2);
                    BounceDispatchesLastFrame++;
                    src = dst;
                }
                _bounceCur = dst;
                _bounceDirty = false;
            }

            // Bind the real resources every frame. Something else (the editor's stand-in binder after a domain reload,
            // another system calling BindFallbacks) can point these globals at empty stand-ins; the shader then sees
            // no walls at all and every wall-aware test (real lights, sample clamps, bounce clamps) silently passes.
            using (s_pBind.Auto()) if (bindGlobals) BindGlobals();
            LastGpuSubmitMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Points the shader globals (light-grid textures, buffers, window, occluders) at this grid. The main grid does it
        /// every frame; the site grid only around the portal cameras that show its site (then the main grid again).
        /// </summary>
        public void BindGlobals()
        {
            if (!Ready) return;
            Shader.SetGlobalTexture(GA, _a); Shader.SetGlobalTexture(GB, _b); Shader.SetGlobalBuffer(GOcc, _occ);
            Shader.SetGlobalTexture(GBounce, _bounceCur ? _bounceCur : _bounce0);
            BindBounceOutputs();
            Shader.SetGlobalVector(GSize, new Vector4(GX, GY, GZ, OccN));
            Shader.SetGlobalVector(GParams, new Vector4(1f, directGain, bounceGain > 0f && _lightCount > 0 ? bounceGain : 0f, _pitch));
            Shader.SetGlobalVector(GWindow, new Vector4(_texMin.x * T, _texMin.y * T, (_texMin.x + GX) * T, (_texMin.y + GZ) * T));
            Shader.SetGlobalVector(GShape, new Vector4(wrap, normalOffset, smoothing, contrast));
            Shader.SetGlobalFloat(GFill, roomFill);
            Shader.SetGlobalBuffer(GLights, _lights); Shader.SetGlobalBuffer(GTiles, _tiles); Shader.SetGlobalBuffer(GTileLights, _tileLights);
            Shader.SetGlobalBuffer("_LGridFixtureLookup", _fixtureLookup);
            Shader.SetGlobalInt("_LGridFixtureMask", _fixtureLookup.count - 1);
            Shader.SetGlobalVector(GFar, new Vector4(perPixelDirect ? 1f : 0f, TileTexels, Tiles, Split));
            BindOccluderGlobals();
            BindProbeGlobals();
            BindHoleGlobals();
        }

        /// <summary>Is a world point inside this grid's window (with a margin, metres)?</summary>
        public bool Covers(Vector3 p, float margin = 0f) =>
            _texMin.x != int.MinValue && p.x >= _texMin.x * T + margin && p.z >= _texMin.y * T + margin &&
            p.x <= (_texMin.x + GX) * T - margin && p.z <= (_texMin.y + GZ) * T - margin;

        /// <summary>The window has been filled since it last moved or the world last changed under it.</summary>
        public bool Settled => Ready && _texMin.x != int.MinValue && !_full && _pending.Count == 0 && !_occDirty;

        (Vector3Int min, Vector3Int max) Clip(Vector3Int min, Vector3Int max) =>
            (Vector3Int.Max(min, new Vector3Int(_texMin.x, 0, _texMin.y)), Vector3Int.Min(max, new Vector3Int(_texMin.x + GX, GY, _texMin.y + GZ)));
        static long Volume((Vector3Int min, Vector3Int max) b) =>
            (long)Mathf.Max(0, b.max.x - b.min.x) * Mathf.Max(0, b.max.y - b.min.y) * Mathf.Max(0, b.max.z - b.min.z);

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
            System.Array.Clear(_surfaceData, 0, _surfaceData.Length);
            _roomSurfaces.Clear();
            if (!streamer) { UploadOccupancy(); return; }
            int N = OfficeBiome.ChunkCells;
            var cellMin = new Vector2Int(_texMin.x / 2, _texMin.y / 2);
            for (int s = 0; s < 2; s++)
            for (int cz = FloorDiv(cellMin.y, N); cz <= FloorDiv(cellMin.y + OccN - 1, N); cz++)
            for (int cx = FloorDiv(cellMin.x, N); cx <= FloorDiv(cellMin.x + OccN - 1, N); cx++)
            {
                var key = new Vector3Int(cx, s, cz);
                var L = streamer.LayoutAt(key);
                if (L == null) continue;
                if (!_encoded.TryGetValue(key, out var enc))
                {
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
                    int index = s * OccN * OccN + Wrap(gz, OccN) * OccN + Wrap(gx, OccN);
                    _occData[index] = enc[z * N + x];
                    _surfaceData[index] = SurfacesFor(L.RoomAt(x, z));
                }
            }
            UploadOccupancy();
        }

        void UploadOccupancy() { _occ.SetData(_occData); _surfaces.SetData(_surfaceData); }

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
