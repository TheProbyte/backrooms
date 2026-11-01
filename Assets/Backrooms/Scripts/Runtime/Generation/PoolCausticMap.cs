using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>
    /// The Poolrooms' caustics: one tile (2.4 m, tileable) of sunlight focused by the water's small waves onto the basin
    /// floor, redrawn every frame while the player is in the Poolrooms (Hidden/Backrooms/PoolCausticSplat: Evan Wallace's
    /// area-ratio method, a 512 x 512 grid refracted onto a 1024 x 1024 map, mipmapped so it averages to 1 in the
    /// distance). The tiles read it under the water and, reflected off sunlit water, on walls and ceilings
    /// (PoolCommon.hlsl, PoolCausticLight / PoolWaterLight). The same waves move the water's surface (PoolWaveSlope).
    /// </summary>
    public class PoolCausticMap : MonoBehaviour
    {
        public const int MapSize = 1024, Grid = 512;
        [Tooltip("Depth of water the map is made for (m): the basins' 0.45 m less the 0.12 m to the surface.")] public float depth = 0.33f;
        [Tooltip("How strongly the waves bend the light (1 = the waves as they are).")] public float gain = 1f;
        [Tooltip("Caustic strength on the tiles under water (0 none).")] [Range(0f, 2f)] public float strength = 1f;
        [Tooltip("Light reflected off sunlit water onto walls and ceilings (0 none).")] [Range(0f, 4f)] public float reflected = 1f;
        [Tooltip("How fast the waves move (1 = real ripples; calmer below).")] [Range(0f, 2f)] public float speed = 0.22f;

        RenderTexture _map;
        Material _mat;
        Mesh _mesh;
        CommandBuffer _cb;
        float _time;
        bool _drawn, _warm;

        void Update()
        {
            var world = PoolWorld.Instance;
            var player = world && world.streamer ? world.streamer.player : null;
            bool active = player && world.IsPool(player.position);
            _time += Time.deltaTime * speed;
            Shader.SetGlobalFloat("_PoolWaveTime", _time);
            if (!Ensure()) { Shader.SetGlobalVector("_PoolCausticParams", Vector4.zero); return; }
            // Drawn while you are in the Poolrooms (and once, so a portal view into them has something).
            if (active || !_drawn) Draw();
            Shader.SetGlobalTexture("_PoolCaustic", _map);
            Shader.SetGlobalVector("_PoolCausticParams", new Vector4(TileSize, 1f, strength, reflected));
        }

        const float TileSize = 2.4f;     // as POOL_WAVE_TILE in PoolCommon.hlsl

        void Draw()
        {
            _mat.SetFloat("_SplatDepth", depth);
            _mat.SetFloat("_SplatGain", gain);
            _cb.Clear();
            _cb.SetRenderTarget(_map);
            _cb.ClearRenderTarget(false, true, Color.clear);
            _cb.DrawMesh(_mesh, Matrix4x4.identity, _mat, 0, 0);
            Graphics.ExecuteCommandBuffer(_cb);
            _drawn = true;
        }

        bool Ensure()
        {
            if (_map) return true;
            var sh = Shader.Find("Hidden/Backrooms/PoolCausticSplat");
            if (!sh || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf)) return false;
            _mat = new Material(sh) { name = "Pool caustic splat", hideFlags = HideFlags.DontSave };
            _map = new RenderTexture(MapSize, MapSize, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear)
            {
                name = "PoolCaustics", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Repeat, anisoLevel = 4, hideFlags = HideFlags.DontSave
            };
            _map.Create();
            _mesh = BuildGrid();
            _cb = new CommandBuffer { name = "Pool caustics" };
            return true;
        }

        /// <summary>The grid over one tile plus a margin (light from just outside the tile lands inside it: the waves are
        /// tileable, so the margin is the neighbouring tiles' water).</summary>
        static Mesh BuildGrid()
        {
            const float margin = 0.08f;
            int n = Grid + 1;
            var v = new Vector3[n * n];
            var uv = new Vector2[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                var t = new Vector2(Mathf.Lerp(-margin, 1f + margin, x / (float)Grid), Mathf.Lerp(-margin, 1f + margin, z / (float)Grid));
                v[z * n + x] = new Vector3(t.x, 0f, t.y);
                uv[z * n + x] = t;
            }
            var idx = new int[Grid * Grid * 6];
            int k = 0;
            for (int z = 0; z < Grid; z++)
            for (int x = 0; x < Grid; x++)
            {
                int a = z * n + x;
                idx[k++] = a; idx[k++] = a + n; idx[k++] = a + 1;
                idx[k++] = a + 1; idx[k++] = a + n; idx[k++] = a + n + 1;
            }
            var m = new Mesh { name = "PoolCausticGrid", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            m.vertices = v; m.uv = uv; m.triangles = idx;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);
            return m;
        }

        void OnDestroy()
        {
            if (_map) { _map.Release(); Destroy(_map); }
            if (_mat) Destroy(_mat);
            if (_mesh) Destroy(_mesh);
            _cb?.Release();
            Shader.SetGlobalVector("_PoolCausticParams", Vector4.zero);
        }
    }
}
