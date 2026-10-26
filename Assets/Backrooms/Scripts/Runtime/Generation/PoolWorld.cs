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

        Texture2D _map;
        Color32[] _px;
        readonly PoolChunk[] _owner = new PoolChunk[MapChunks * MapChunks];
        bool _dirty;

        void Awake()
        {
            Instance = this;
            if (!streamer) streamer = GetComponent<WorldStreamer>();
            PoolMaterials.Ensure();
            _map = new Texture2D(MapCells, MapCells, TextureFormat.RGBA32, false, true)
            {
                name = "PoolMap", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.DontSave,
            };
            _px = new Color32[MapCells * MapCells];
            _map.SetPixels32(_px); _map.Apply(false);
            Shader.SetGlobalTexture("_PoolMap", _map);
            Shader.SetGlobalVector("_PoolMapParams", new Vector4(MapCells, 0f, 0f, 0f));
            Shader.SetGlobalVector("_PoolRippleRect", Vector4.zero);
            Shader.SetGlobalFloat("_PoolScreenTextures", 0f);
            if (!GetComponent<PoolRipples>()) gameObject.AddComponent<PoolRipples>();
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
            if (c.storey != 0 || c.cells == null) return;
            int N = OfficeBiome.ChunkCells;
            _owner[Mod(c.chunk.y, MapChunks) * MapChunks + Mod(c.chunk.x, MapChunks)] = c;
            int bx = Mod(c.chunk.x, MapChunks) * N, bz = Mod(c.chunk.y, MapChunks) * N;
            for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
                _px[(bz + z) * MapCells + bx + x] = c.cells[z * N + x];
            _dirty = true;
        }

        /// <summary>An unloaded chunk leaves its cells empty (no stale walls under a later chunk), unless another took them.</summary>
        public void Clear(PoolChunk c)
        {
            if (c.storey != 0) return;
            int slot = Mod(c.chunk.y, MapChunks) * MapChunks + Mod(c.chunk.x, MapChunks);
            if (_owner[slot] != c) return;
            _owner[slot] = null;
            int N = OfficeBiome.ChunkCells, bx = Mod(c.chunk.x, MapChunks) * N, bz = Mod(c.chunk.y, MapChunks) * N;
            for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
                _px[(bz + z) * MapCells + bx + x] = default;
            _dirty = true;
        }

        void LateUpdate()
        {
            if (!_dirty) return;
            _dirty = false;
            _map.SetPixels32(_px);
            _map.Apply(false);
        }

        // ---------------- Queries ----------------

        OfficeLayout LayoutAt(Vector3 p) => streamer ? streamer.LayoutAt(WorldStreamer.ChunkOf(p)) : null;

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
            if (!L.pool[x, z].basin) return false;
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
            if (L == null || L.style != ZoneStyle.Pool || streamer == null) return;
            int zi = streamer.Settings.ZoneIndex(L.zoneName);
            if (zi < 0) return;
            var zone = streamer.Settings.zones[zi];
            if (!zone.overrideFog) return;
            _fogColor = RenderSettings.fogColor; _fogDensity = RenderSettings.fogDensity; _fog = RenderSettings.fog;
            _swapped = true;
            RenderSettings.fogColor = zone.fogColor; RenderSettings.fogDensity = zone.fogDensity; RenderSettings.fog = true;
        }

        void EndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!_swapped) return;
            _swapped = false;
            RenderSettings.fogColor = _fogColor; RenderSettings.fogDensity = _fogDensity; RenderSettings.fog = _fog;
        }
    }
}
