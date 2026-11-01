using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Generation
{
    /// <summary>
    /// Exact reflections in the Poolrooms' water (2026-11-01, the realism plan 3). All the water of a storey lies at one
    /// height, so one planar reflection is exact: a second render of the player's view mirrored in that plane (oblique
    /// near plane on the surface, so nothing under the water shows in it), at half resolution, which PoolWater reads at
    /// the pixel's own screen position, bent by the ripples. It sees what the cell-map trace cannot (the curved walls,
    /// columns' bases, rails, ladders, props, bleachers) and what screen-space reflection cannot (what is off screen), as
    /// planar water does in Half-Life: Alyx and most indoor-water games. Only while the player's camera is in the
    /// Poolrooms above the water; Graphics > Water reflections switches it off (the trace and screen-space then).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class PoolMirror : MonoBehaviour
    {
        [Range(0.25f, 1f)] public float scale = 0.5f;
        public static bool Rendering { get; private set; }

        Camera _cam;
        RenderTexture _rt;
        static readonly int IdTex = Shader.PropertyToID("_PoolMirrorTex"), IdParams = Shader.PropertyToID("_PoolMirrorParams"), IdPass = Shader.PropertyToID("_PoolMirrorPass");

        static bool Wanted()
        {
            var gc = Camcorder.GraphicsController.Instance;
            return gc == null || gc.Options == null || gc.Options.planarWater;
        }

        void LateUpdate()
        {
            var world = PoolWorld.Instance;
            var main = Camera.main;
            if (!world || !main || !Wanted() || !world.IsPool(main.transform.position)) { Off(); return; }
            float pitch = world.streamer && world.streamer.Settings != null ? world.streamer.Settings.storeyPitch : 9f;
            int storey = world.StoreyOf(main.transform.position.y);
            float h = storey * pitch + PoolBiome.WaterLevel;
            if (main.transform.position.y < h + 0.03f) { Off(); return; }
            // Is any water on screen, and how near (2026-11-01, the review: it rendered every frame in the Poolrooms)?
            float nearest = NearestWater(world, main, h);
            if (nearest > 45f) { Off(); return; }
            scale = nearest > 14f ? 0.35f : 0.5f;
            Ensure(main);
            if (!_cam) { Off(); return; }

            // The mirror camera: the main view reflected in the plane y = h.
            _cam.fieldOfView = main.fieldOfView;
            _cam.aspect = main.aspect;
            _cam.nearClipPlane = main.nearClipPlane;
            _cam.farClipPlane = main.farClipPlane;
            _cam.cullingMask = main.cullingMask;
            _cam.backgroundColor = RenderSettings.fogColor;
            var R = Reflection(h);
            _cam.worldToCameraMatrix = main.worldToCameraMatrix * R;
            var pos = main.transform.position;
            _cam.transform.SetPositionAndRotation(new Vector3(pos.x, 2f * h - pos.y, pos.z), main.transform.rotation);
            // Oblique near plane on the water (a little under it, so the edges meet), in the mirror camera's space.
            var m = _cam.worldToCameraMatrix;
            var cp = m.MultiplyPoint(new Vector3(0f, h - 0.02f, 0f));
            var cn = m.MultiplyVector(Vector3.up).normalized;
            _cam.projectionMatrix = main.CalculateObliqueMatrix(new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cp, cn)));

            Rendering = true;
            Shader.SetGlobalFloat(IdPass, 1f);
            GL.invertCulling = true;
            try { _cam.Render(); }
            finally
            {
                GL.invertCulling = false;
                Shader.SetGlobalFloat(IdPass, 0f);
                Rendering = false;
            }
            Shader.SetGlobalTexture(IdTex, _rt);
            Shader.SetGlobalVector(IdParams, new Vector4(1f, h, 0f, 0f));
        }

        void Off() => Shader.SetGlobalVector(IdParams, Vector4.zero);

        /// <summary>The nearest water seen on a 9 x 6 grid of view rays down to the water's plane (m; far if none).</summary>
        static float NearestWater(PoolWorld world, Camera cam, float h)
        {
            float best = 999f;
            var o = cam.transform.position;
            for (int i = 0; i < 9; i++)
            for (int j = 0; j < 6; j++)
            {
                var ray = cam.ViewportPointToRay(new Vector3((i + 0.5f) / 9f, (j + 0.5f) / 6f, 0f));
                if (ray.direction.y > -0.01f) continue;
                float t = (h - o.y) / ray.direction.y;
                if (t > 45f || t >= best) continue;
                var at = o + ray.direction * t;
                if (world.WaterAt(at, out float lv) && Mathf.Abs(lv - h) < 0.05f) best = t;
            }
            return best;
        }

        void Ensure(Camera main)
        {
            int w = Mathf.Max(64, Mathf.RoundToInt(main.pixelWidth * scale)), hgt = Mathf.Max(64, Mathf.RoundToInt(main.pixelHeight * scale));
            if (!_rt || _rt.width != w || _rt.height != hgt)
            {
                if (_rt) { _rt.Release(); Destroy(_rt); }
                _rt = new RenderTexture(w, hgt, 24, RenderTextureFormat.ARGBHalf) { name = "Pool mirror", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                _rt.Create();
            }
            if (!_cam)
            {
                var go = new GameObject("Pool mirror camera") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(transform, false);
                _cam = go.AddComponent<Camera>();
                _cam.enabled = false;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.allowHDR = true;
                _cam.allowMSAA = false;
                var d = _cam.GetUniversalAdditionalCameraData();
                d.renderPostProcessing = false;
                d.renderShadows = false;
                d.requiresColorOption = CameraOverrideOption.Off;
                d.requiresDepthOption = CameraOverrideOption.Off;
                d.antialiasing = AntialiasingMode.None;
                d.SetRenderer(0);
            }
            _cam.targetTexture = _rt;
        }

        /// <summary>The reflection about the plane y = h.</summary>
        static Matrix4x4 Reflection(float h)
        {
            var r = Matrix4x4.identity;
            r.m11 = -1f; r.m13 = 2f * h;
            return r;
        }

        void OnDestroy()
        {
            Off();
            if (_rt) { _rt.Release(); Destroy(_rt); }
            if (_cam) Destroy(_cam.gameObject);
        }
    }
}
