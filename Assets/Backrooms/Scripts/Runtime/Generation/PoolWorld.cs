using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>
    /// Runtime side of the Poolrooms. Keeps the shaders' cell map of every loaded Poolrooms chunk (a small toroidal
    /// texture by world cell: walls, ceilings, openings, water, baked bounce; see PoolCommon.hlsl), sets the light
    /// (sun, sky, ambient), swaps in the zone's fog for each camera that looks at the Poolrooms (the player's, or a portal
    /// view into them from the Backrooms), and answers "is there water here".
    /// Added by WorldStreamer.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PoolWorld : MonoBehaviour
    {
        public static PoolWorld Instance { get; private set; }

        /// <summary>Map size in cells: 12 chunks each way, more than the loaded square (9 chunks at most).</summary>
        public const int MapCells = 144;
        /// <summary>Storeys in the map, stacked as rows of the texture (the Poolrooms have two).</summary>
        public const int MapStoreys = 2;
        const int MapChunks = MapCells / OfficeBiome.ChunkCells;

        public WorldStreamer streamer;

        [Header("Light")]
        public Color sunColor = new Color(1f, 0.92f, 0.78f);
        [Tooltip("Sun brightness against the ambient (the camera's exposure follows the scene).")]
        public float sunIntensity = 9f;
        [Tooltip("Light everywhere: sky scattered in through the openings.")]
        public Color ambient = new Color(0.40f, 0.46f, 0.46f);
        [Tooltip("Bounce at full baked strength (warm, off sunlit tiles).")]
        public Color bounce = new Color(1.05f, 0.98f, 0.86f);
        public float bounceGain = 2.6f;
        public Color skyZenith = new Color(0.55f, 0.75f, 1f);
        public Color skyHorizon = new Color(0.95f, 0.97f, 1f);
        public float skyIntensity = 10f;

        [Header("Clouds (2026-11-01, the realism plan 2)")]
        [Tooltip("How much of the sky the passing clouds cover (0 none).")]
        [Range(0f, 1f)] public float cloudCover = 0.35f;
        [Tooltip("How much of the sun a cloud takes (the sky still lights the rooms).")]
        [Range(0f, 1f)] public float cloudShadow = 0.72f;
        [Tooltip("The clouds' drift (m/s, x and z).")]
        public Vector2 wind = new Vector2(4f, 1.6f);
        /// <summary>How much sun reaches the rooms round the player now (1 clear .. 1 - cloudShadow under a cloud).</summary>
        public float SunNow { get; private set; } = 1f;
        /// <summary>The player's camera is under the water (crawling or crouched in a deep pool).</summary>
        public static bool CameraUnderwater { get; private set; }
        bool _camInPool, _camSeen;

        Texture2D _map, _map2;
        Color32[] _px, _px2;
        readonly PoolChunk[] _owner = new PoolChunk[MapChunks * MapChunks * MapStoreys];
        float Pitch => streamer && streamer.Settings != null ? streamer.Settings.storeyPitch : 9f;

        /// <summary>Storey of a height (a basin floor sits up to 0.6 m under its storey's line).</summary>
        public int StoreyOf(float y) => Mathf.Clamp(Mathf.FloorToInt((y + 0.6f) / Pitch), 0, MapStoreys - 1);
        bool _dirty;

        void Awake()
        {
            Instance = this;
            if (!streamer) streamer = GetComponent<WorldStreamer>();
            PoolMaterials.Ensure();
            _map = new Texture2D(MapCells, MapCells * MapStoreys, TextureFormat.RGBA32, false, true)
            {
                name = "PoolMap", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave,
            };
            _px = new Color32[MapCells * MapCells * MapStoreys];
            _map.SetPixels32(_px); _map.Apply(false);
            Shader.SetGlobalTexture("_PoolMap", _map);
            // The second map: depth, gallery, lanes and drains, round openings (only read where the first says so).
            _map2 = new Texture2D(MapCells, MapCells * MapStoreys, TextureFormat.RGBA32, false, true)
            {
                name = "PoolMap2", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave,
            };
            _px2 = new Color32[MapCells * MapCells * MapStoreys];
            _map2.SetPixels32(_px2); _map2.Apply(false);
            Shader.SetGlobalTexture("_PoolMap2", _map2);
            Shader.SetGlobalVector("_PoolMapParams", new Vector4(MapCells, Pitch, MapStoreys, 0f));
            Shader.SetGlobalVector("_PoolRippleRect", Vector4.zero);
            Shader.SetGlobalFloat("_PoolScreenTextures", 0f);
            if (!GetComponent<PoolRipples>()) gameObject.AddComponent<PoolRipples>();
            if (!GetComponent<PoolCausticMap>()) gameObject.AddComponent<PoolCausticMap>();
            if (!GetComponent<PoolSplashes>()) gameObject.AddComponent<PoolSplashes>();
            if (!GetComponent<PoolBodies>()) gameObject.AddComponent<PoolBodies>();
            if (!GetComponent<PoolWetPrints>()) gameObject.AddComponent<PoolWetPrints>();
            if (!GetComponent<PoolAir>()) gameObject.AddComponent<PoolAir>();
            if (!GetComponent<PoolMirror>()) gameObject.AddComponent<PoolMirror>();
            if (!GetComponent<Audio.PoolSounds>()) gameObject.AddComponent<Audio.PoolSounds>();
            ApplyLight();
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            if (Instance == this) Instance = null;
            if (_map) Destroy(_map);
            if (_map2) Destroy(_map2);
        }

        void OnValidate() { if (Application.isPlaying) ApplyLight(); }

        public void ApplyLight()
        {
            Shader.SetGlobalVector("_PoolSunColor", (Vector4)(sunColor.linear * sunIntensity));
            Shader.SetGlobalVector("_PoolAmbient", (Vector4)ambient.linear);
            Shader.SetGlobalVector("_PoolBounce", (Vector4)(bounce.linear * bounceGain));
            Shader.SetGlobalVector("_PoolSkyZenith", (Vector4)(skyZenith.linear * skyIntensity));
            Shader.SetGlobalVector("_PoolSkyHorizon", (Vector4)(skyHorizon.linear * skyIntensity));
        }

        static int Mod(int a, int n) => ((a % n) + n) % n;

        public void Write(PoolChunk c)
        {
            if (c.storey < 0 || c.storey >= MapStoreys || c.cells == null) return;
            int N = OfficeBiome.ChunkCells;
            _owner[(c.storey * MapChunks + Mod(c.chunk.y, MapChunks)) * MapChunks + Mod(c.chunk.x, MapChunks)] = c;
            int bx = Mod(c.chunk.x, MapChunks) * N, bz = Mod(c.chunk.y, MapChunks) * N + c.storey * MapCells;
            for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                _px[(bz + z) * MapCells + bx + x] = c.cells[z * N + x];
                _px2[(bz + z) * MapCells + bx + x] = c.cells2 != null ? c.cells2[z * N + x] : default;
            }
            _dirty = true;
        }

        /// <summary>An unloaded chunk leaves its cells empty (no stale walls under a later chunk), unless another took them.</summary>
        public void Clear(PoolChunk c)
        {
            if (c.storey < 0 || c.storey >= MapStoreys) return;
            int slot = (c.storey * MapChunks + Mod(c.chunk.y, MapChunks)) * MapChunks + Mod(c.chunk.x, MapChunks);
            if (_owner[slot] != c) return;
            _owner[slot] = null;
            int N = OfficeBiome.ChunkCells, bx = Mod(c.chunk.x, MapChunks) * N, bz = Mod(c.chunk.y, MapChunks) * N + c.storey * MapCells;
            for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            { _px[(bz + z) * MapCells + bx + x] = default; _px2[(bz + z) * MapCells + bx + x] = default; }
            _dirty = true;
        }

        void LateUpdate()
        {
            Shader.SetGlobalVector("_PoolMapParams", new Vector4(MapCells, Pitch, MapStoreys, 0f));
            Clouds();
            CameraState();
            if (!_dirty) return;
            _dirty = false;
            _map.SetPixels32(_px);
            _map.Apply(false);
            _map2.SetPixels32(_px2);
            _map2.Apply(false);
        }

        // ---------------- The camera: under the water, misting over ----------------

        /// <summary>
        /// Under the water (2026-11-01, the realism plan 11): the shaders draw the murk, the surface from below and the beams
        /// in the water (_PoolUnderwater); coming up puts water on the lens. Coming into the Poolrooms from the cool
        /// Backrooms mists the lens over (plan 9).
        /// </summary>
        void CameraState()
        {
            var cam = Camera.main;
            if (!cam) return;
            var p = cam.transform.position;
            bool under = WaterAt(p, out float level) && p.y < level - 0.005f;
            if (CameraUnderwater && !under) { Camcorder.LensDrops.Add(5, 0.9f); Camcorder.LensFog.Fog(0.3f, 4f); }
            CameraUnderwater = under;
            Shader.SetGlobalVector("_PoolUnderwater", new Vector4(under ? 1f : 0f, level, 0f, 0f));
            bool inPool = IsPool(p);
            if (inPool && !_camInPool && _camSeen) Camcorder.LensFog.Fog(0.85f, 11f);
            _camInPool = inPool; _camSeen = true;
        }

        // ---------------- Clouds ----------------

        /// <summary>
        /// The clouds' drift for the shaders (PoolCommon.hlsl, PoolCloudShade), and the bounce dimmed with the sun round the
        /// player: under a cloud the sunlit floors that light the rooms go grey too.
        /// </summary>
        void Clouds()
        {
            var drift = wind * Time.time;
            Shader.SetGlobalVector("_PoolCloud", new Vector4(drift.x, drift.y, cloudCover, cloudShadow));
            var p = streamer && streamer.player ? streamer.player.position : Vector3.zero;
            float shade = 0f;
            for (int i = 0; i < 5; i++)
            {
                var q = p + new Vector3(i == 1 ? 6f : i == 2 ? -6f : 0f, 0f, i == 3 ? 6f : i == 4 ? -6f : 0f);
                shade += CloudShade(q, drift);
            }
            SunNow = Mathf.Lerp(SunNow, shade / 5f, 1f - Mathf.Exp(-Time.deltaTime / 1.5f));
            Shader.SetGlobalVector("_PoolBounce", (Vector4)(bounce.linear * bounceGain * Mathf.Lerp(0.6f, 1f, Mathf.InverseLerp(1f - cloudShadow, 1f, SunNow))));
        }

        /// <summary>As PoolCommon.hlsl's PoolCloudShade (the same noise).</summary>
        public float CloudShade(Vector3 pw) => CloudShade(pw, wind * Time.time);

        float CloudShade(Vector3 pw, Vector2 drift)
        {
            if (cloudShadow <= 0f) return 1f;
            var L = PoolBiome.SunDirection;
            var deck = new Vector2(pw.x + L.x / L.y * (300f - pw.y), pw.z + L.z / L.y * (300f - pw.y));
            var s = (deck + drift) * 0.004f;
            float n = Noise(s) * 0.55f + Noise(s * 2.03f + new Vector2(17.1f, 17.1f)) * 0.3f + Noise(s * 4.1f + new Vector2(5.3f, 5.3f)) * 0.15f;
            float a = 1f - cloudCover, t = Mathf.Clamp01((n - a) / 0.2f);
            return 1f - cloudShadow * t * t * (3f - 2f * t);
        }

        static float Frac(float x) => x - Mathf.Floor(x);
        static Vector2 Hash22(Vector2 p)
        {
            float qx = Frac(p.x * 0.1031f), qy = Frac(p.y * 0.1030f), qz = Frac(p.x * 0.0973f);
            float d = qx * (qy + 33.33f) + qy * (qz + 33.33f) + qz * (qx + 33.33f);
            qx += d; qy += d; qz += d;
            return new Vector2(Frac((qx + qy) * qz), Frac((qx + qz) * qy));
        }
        static float Noise(Vector2 p)
        {
            float ix = Mathf.Floor(p.x), iy = Mathf.Floor(p.y), fx = p.x - ix, fy = p.y - iy;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash22(new Vector2(ix, iy)).x, b = Hash22(new Vector2(ix + 1f, iy)).x, c = Hash22(new Vector2(ix, iy + 1f)).x, d = Hash22(new Vector2(ix + 1f, iy + 1f)).x;
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        // ---------------- Queries ----------------

        OfficeLayout LayoutAt(Vector3 p)
        {
            if (!streamer) return null;
            var c = WorldStreamer.ChunkOf(p);
            return streamer.LayoutAt(new Vector3Int(c.x, StoreyOf(p.y), c.y)) ?? streamer.LayoutAt(c);
        }

        /// <summary>A cell of the shaders' map, decoded (as PoolCommon.hlsl's PoolLoad).</summary>
        public PoolCellInfo MapCell(int x, int z, int storey)
        {
            int index = (Mod(z, MapCells) + Mathf.Clamp(storey, 0, MapStoreys - 1) * MapCells) * MapCells + Mod(x, MapCells);
            var v = _px[index];
            var v2 = (v.b & 128) != 0 ? _px2[index] : default;
            return new PoolCellInfo
            {
                valid = (v.r & 64) != 0, west = (byte)(v.r & 3), south = (byte)((v.r >> 2) & 3), basin = (v.r & 16) != 0,
                pillar = (v.r & 32) != 0, pillarNear = (v.r & 128) != 0, ceiling = v.g * 0.1f,
                sky = (SkylightShape)(v.b & 7), joined = (v.b >> 3) & 15,
                depth = v2.r * 0.005f, gallery = v2.g * 0.05f, lanes = (byte)(v2.b & 3), drain = (v2.b & 4) != 0,
                discR = (v2.b >> 4) * 0.3f, discX = (((v2.a & 15) - 8) * 0.5f) * OfficeBiome.CellSize, discZ = (((v2.a >> 4) - 8) * 0.5f) * OfficeBiome.CellSize,
            };
        }

        sealed class MapCells_ : IPoolCells
        {
            public PoolWorld w; public int storey;
            public PoolCellInfo Get(int x, int z) => w.MapCell(x, z, storey);
        }
        readonly MapCells_ _cells = new MapCells_();

        /// <summary>Sunlight at a world point (0 shade .. 1 sun), from the loaded map (as the shaders see it).</summary>
        public float SunAt(Vector3 p)
        {
            int s = StoreyOf(p.y);
            _cells.w = this; _cells.storey = s;
            return PoolSun.Visibility(_cells, new Vector3(p.x, p.y - s * Pitch, p.z));
        }

        /// <summary>Is this point in a loaded Poolrooms chunk?</summary>
        public bool IsPool(Vector3 p)
        {
            var L = LayoutAt(p);
            return L != null && L.style == ZoneStyle.Pool;
        }

        /// <summary>Is there water at this point's cell, and at what height is its surface?</summary>
        public bool WaterAt(Vector3 p, out float level)
        {
            level = 0f;
            var L = LayoutAt(p);
            if (L == null || L.pool == null) return false;
            float C = OfficeBiome.CellSize;
            int x = Mathf.Clamp(Mathf.FloorToInt(p.x / C) - L.coord.x * L.N, 0, L.N - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(p.z / C) - L.coord.y * L.N, 0, L.N - 1);
            if (!L.WaterAtPoint(p.x - L.coord.x * L.N * C, p.z - L.coord.y * L.N * C)) return false;
            level = L.BaseY + PoolBiome.WaterLevel;
            return true;
        }

        // ---------------- Per camera: fog, screen textures ----------------

        bool _swapped;
        Color _fogColor; float _fogDensity; bool _fog;

        void BeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            Shader.SetGlobalFloat("_PoolScreenTextures", cam.cameraType == CameraType.Game && cam == Camera.main ? 1f : 0f);
            var L = LayoutAt(cam.transform.position);
            bool pool = L != null && L.style == ZoneStyle.Pool;
            Shader.SetGlobalFloat("_PoolLitBlend", pool ? 1f : 0f);
            if (!pool || streamer == null) return;
            int zi = streamer.Settings.ZoneIndex(L.zoneName);
            if (zi < 0) return;
            var zone = streamer.Settings.zones[zi];
            if (!zone.overrideFog) return;
            _fogColor = RenderSettings.fogColor; _fogDensity = RenderSettings.fogDensity; _fog = RenderSettings.fog;
            _swapped = true;
            RenderSettings.fogColor = zone.fogColor; RenderSettings.fogDensity = zone.fogDensity; RenderSettings.fog = true;
            // Far off, everything fades into the fog's colour before the loaded world ends, and what lies beyond it (nothing)
            // is that colour too: it was black (2026-10-31, the owner: "far away shows as black").
            float end = streamer.loadRadius * OfficeBiome.ChunkCells * OfficeBiome.CellSize - 1f;
            Shader.SetGlobalVector("_PoolFar", new Vector4(end * farFadeStart, end, 0f, 0f));
            _camFlags = cam.clearFlags; _camBack = cam.backgroundColor; _camSwapped = cam;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = zone.fogColor;
        }

        [Tooltip("Where the far fade starts, as a share of the distance the loaded world is sure to reach.")]
        [Range(0.3f, 0.95f)] public float farFadeStart = 0.6f;
        Camera _camSwapped;
        CameraClearFlags _camFlags;
        Color _camBack;

        void EndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (_camSwapped == cam) { cam.clearFlags = _camFlags; cam.backgroundColor = _camBack; _camSwapped = null; }
            Shader.SetGlobalVector("_PoolFar", Vector4.zero);
            if (!_swapped) return;
            _swapped = false;
            RenderSettings.fogColor = _fogColor; RenderSettings.fogDensity = _fogDensity; RenderSettings.fog = _fog;
        }
    }
}
