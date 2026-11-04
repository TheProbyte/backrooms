using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Generation
{
    /// <summary>
    /// Volumetric light on the player's camera (F5 > LIGHT > AIR > Volumetric light; Resources/VolumetricLight.compute,
    /// Resources/Shaders/VolumetricComposite.shader): the haze lit froxel by froxel (64 x 48 x 48 cells of the view out to
    /// 40 m), each near fixture worked out with the walls, doorways, shut doors and furniture between and a forward-
    /// scattering phase, so light falls through a doorway in a shaft, a lit room's air glows seen from a dark corridor and
    /// the air brightens looking toward a fixture; integrated toward the camera and laid over the opaque picture before
    /// the transparents. It takes the place of Backrooms/Lit's own haze march on this camera (that stays for portal views
    /// and reflection captures). The haze's thickness is the LIGHT tab's Haze; the far air still reads the light grid's
    /// luminance channel, which the grid only fills while haze is on. Not in the Poolrooms (PoolAir has their light).
    /// </summary>
    public class VolumetricLight : MonoBehaviour
    {
        public static VolumetricLight Instance { get; private set; }

        [Range(0f, 1f)] public float haze = 0.25f;           // the LIGHT tab's Haze (the grid's thickness formula)
        [Range(0f, 0.9f)] public float anisotropy = 0.35f;   // forward scattering: how much the air glows looking toward a light
        public float exactRange = 12f;                       // fixtures worked out exactly within this distance of the camera
        public int width = 64, slices = 48;
        public float near = 0.3f, far = 40f;

        public bool Active => isActiveAndEnabled && _cs && _material && haze > 0.001f && _grid && _grid.isActiveAndEnabled;
        /// <summary>Frames drawn (for tests).</summary>
        public int Frames { get; private set; }

        ComputeShader _cs;
        Material _material;
        int _kInject, _kIntegrate;
        RenderTexture[] _vol = new RenderTexture[2];
        RenderTexture _integrated;
        int _cur, _w, _h, _d;
        bool _historyValid;
        Matrix4x4 _prevVP;
        Vector3 _prevPos, _prevFwd;
        LightGrid _grid;
        InjectPass _inject;
        CompositePass _composite;

        static readonly int HazeId = Shader.PropertyToID("_LGridHaze"), IntegratedId = Shader.PropertyToID("_VolIntegrated"),
            DepthParamsId = Shader.PropertyToID("_VolDepth"), DimsId = Shader.PropertyToID("_VolDims"),
            DustLightId = Shader.PropertyToID("_VolLight"), DustVPId = Shader.PropertyToID("_VolDustVP"),
            DustCamId = Shader.PropertyToID("_VolDustCam"), DustFwdId = Shader.PropertyToID("_VolDustFwd");

        public static VolumetricLight Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("VolumetricLight");
            DontDestroyOnLoad(go);
            return go.AddComponent<VolumetricLight>();
        }

        void OnEnable()
        {
            Instance = this;
            _cs = Resources.Load<ComputeShader>("VolumetricLight");
            if (_cs) { _kInject = _cs.FindKernel("Inject"); _kIntegrate = _cs.FindKernel("Integrate"); }
            var shader = Shader.Find("Hidden/Backrooms/VolumetricComposite");
            if (shader) _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _inject = new InjectPass(this);
            _composite = new CompositePass(this);
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RestoreHaze();
            Release();
            if (_material) { if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material); }
            if (Instance == this) Instance = null;
        }

        void Release()
        {
            for (int i = 0; i < 2; i++) if (_vol[i]) { _vol[i].Release(); DestroyImmediate(_vol[i]); _vol[i] = null; }
            if (_integrated) { _integrated.Release(); DestroyImmediate(_integrated); _integrated = null; }
            _w = _h = _d = 0;
            _historyValid = false;
        }

        // The lit shaders' own haze, as the light grid sets it (x extinction, y in-scatter, z march, w on).
        Vector4 HazeVector(bool on)
        {
            float h = Mathf.Clamp01(haze), scatter = 0.02f * h * Mathf.Sqrt(h);
            return h > 0.001f ? new Vector4(0.4f * scatter, scatter, 24f, on ? 1f : 0f) : Vector4.zero;
        }

        void RestoreHaze() { if (haze > 0.001f) Shader.SetGlobalVector(HazeId, HazeVector(true)); }

        void BeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!_grid) _grid = FindAnyObjectByType<LightGrid>();
            bool mine = Active && cam.cameraType == CameraType.Game && cam == Camera.main
                && !(PoolWorld.Instance && PoolWorld.Instance.IsPool(cam.transform.position));
            if (!mine)
            {
                if (Active) RestoreHaze();
                Shader.SetGlobalVector(DustFwdId, Vector4.zero);         // the dust in the air lights itself (AirDust)
                return;
            }
            var data = cam.GetUniversalAdditionalCameraData();
            var renderer = data ? data.scriptableRenderer : null;
            if (renderer == null) return;
            // This camera's air is the froxels': Backrooms/Lit skips its own march here.
            Shader.SetGlobalVector(HazeId, HazeVector(false));
            renderer.EnqueuePass(_inject);
            renderer.EnqueuePass(_composite);
        }

        RenderTexture Make(string name)
        {
            var t = new RenderTexture(_w, _h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex3D, volumeDepth = _d, enableRandomWrite = true, filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, name = name, hideFlags = HideFlags.HideAndDontSave
            };
            t.Create();
            return t;
        }

        void Allocate(float aspect)
        {
            int w = Mathf.Clamp(width, 16, 160), h = Mathf.Clamp(Mathf.RoundToInt(w / Mathf.Max(aspect, 0.3f)), 8, 160), d = Mathf.Clamp(slices, 16, 128);
            if (w == _w && h == _h && d == _d && _integrated) return;
            Release();
            _w = w; _h = h; _d = d;
            _vol[0] = Make("BRVolume0"); _vol[1] = Make("BRVolume1"); _integrated = Make("BRVolumeIntegrated");
        }

        class InjectData
        {
            public VolumetricLight o;
            public Matrix4x4 invVP, prevVP;
            public Vector4 cam, fwd, prevCam, dims, depth, prm, tint;
        }

        static readonly int InvVPId = Shader.PropertyToID("_VolInvVP"), PrevVPId = Shader.PropertyToID("_VolPrevVP"), CamId = Shader.PropertyToID("_VolCam"),
            FwdId = Shader.PropertyToID("_VolFwd"), PrevCamId = Shader.PropertyToID("_VolPrevCam"), ParamsId = Shader.PropertyToID("_VolParams"),
            TintId = Shader.PropertyToID("_VolTint"), FlipId = Shader.PropertyToID("_VolFlipY"), OutId = Shader.PropertyToID("_VolOut"),
            PrevId = Shader.PropertyToID("_VolPrev"), InId = Shader.PropertyToID("_VolIn"), EmissionTintId = Shader.PropertyToID("_BREmissionTint");

        void Execute(UnsafeCommandBuffer cmd, InjectData d)
        {
            _cur = 1 - _cur;
            var cs = _cs;
            cmd.SetComputeMatrixParam(cs, InvVPId, d.invVP);
            cmd.SetComputeMatrixParam(cs, PrevVPId, d.prevVP);
            cmd.SetComputeVectorParam(cs, CamId, d.cam);
            cmd.SetComputeVectorParam(cs, FwdId, d.fwd);
            cmd.SetComputeVectorParam(cs, PrevCamId, d.prevCam);
            cmd.SetComputeVectorParam(cs, DimsId, d.dims);
            cmd.SetComputeVectorParam(cs, DepthParamsId, d.depth);
            cmd.SetComputeVectorParam(cs, ParamsId, d.prm);
            cmd.SetComputeVectorParam(cs, TintId, d.tint);
            cmd.SetComputeFloatParam(cs, FlipId, SystemInfo.graphicsUVStartsAtTop ? -1f : 1f);
            cmd.SetComputeTextureParam(cs, _kInject, OutId, _vol[_cur]);
            cmd.SetComputeTextureParam(cs, _kInject, PrevId, _vol[1 - _cur]);
            cmd.DispatchCompute(cs, _kInject, (_w + 3) / 4, (_h + 3) / 4, (_d + 3) / 4);
            cmd.SetComputeTextureParam(cs, _kIntegrate, InId, _vol[_cur]);
            cmd.SetComputeTextureParam(cs, _kIntegrate, IntegratedId, _integrated);
            cmd.DispatchCompute(cs, _kIntegrate, (_w + 7) / 8, (_h + 7) / 8, 1);
            _historyValid = true;
        }

        class InjectPass : ScriptableRenderPass
        {
            readonly VolumetricLight _o;
            public InjectPass(VolumetricLight owner) { _o = owner; renderPassEvent = RenderPassEvent.BeforeRenderingOpaques; }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var camera = frameData.Get<UniversalCameraData>();
                var o = _o;
                if (!o || !o._cs || !o._grid) return;
                // The light grid's buffers and textures for the inject kernel (main thread, before the dispatch).
                if (!o._grid.BindPortalLighting(o._cs, o._kInject)) return;
                var cam = camera.camera;
                o.Allocate(cam.aspect);
                var view = camera.GetViewMatrix();
                var vp = GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(), true) * view;
                var pos = cam.transform.position;
                var fwd = cam.transform.forward;
                bool cut = (pos - o._prevPos).sqrMagnitude > 1.5f * 1.5f || Vector3.Dot(fwd, o._prevFwd) < 0.8f;
                float near = Mathf.Max(o.near, 0.05f), far = Mathf.Max(o.far, near + 1f);
                var h = o.HazeVector(true);
                var tintV = Shader.GetGlobalVector(EmissionTintId);
                var tint = tintV.w > 0.5f ? new Vector3(tintV.x, tintV.y, tintV.z) : Vector3.one;
                float lum = Mathf.Max(0.2126f * tint.x + 0.7152f * tint.y + 0.0722f * tint.z, 1e-3f);
                tint /= lum;
                using (var builder = renderGraph.AddUnsafePass<InjectData>("Backrooms volumetric light", out var d))
                {
                    d.o = o;
                    d.invVP = vp.inverse; d.prevVP = o._prevVP;
                    d.cam = new Vector4(pos.x, pos.y, pos.z, Time.frameCount);
                    d.fwd = new Vector4(fwd.x, fwd.y, fwd.z, SystemInfo.usesReversedZBuffer ? 1f : 0f);
                    d.prevCam = new Vector4(o._prevPos.x, o._prevPos.y, o._prevPos.z, o._historyValid && !cut ? 1f : 0f);
                    d.dims = new Vector4(o._w, o._h, o._d, 0f);
                    d.depth = new Vector4(near, far, Mathf.Log(far / near), Mathf.Clamp(o.exactRange, 3f, 30f));
                    d.prm = new Vector4(h.x, h.y, Mathf.Clamp(o.anisotropy, 0f, 0.9f), 0.12f);
                    // The grid's luminance is the fixtures' light in white: the old haze's warm tint of it.
                    d.tint = new Vector4(tint.x, tint.y * 0.93f, tint.z * 0.75f, 0f);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (InjectData d, UnsafeGraphContext ctx) => d.o.Execute(ctx.cmd, d));
                }
                o._prevVP = vp; o._prevPos = pos; o._prevFwd = fwd;
                Shader.SetGlobalVector(DepthParamsId, new Vector4(near, far, Mathf.Log(far / near), 0f));
                Shader.SetGlobalVector(DimsId, new Vector4(o._w, o._h, o._d, 0f));
                Shader.SetGlobalTexture(IntegratedId, o._integrated);
                // The froxels' light for the dust in the air (2026-11-01, Backrooms/AirDust): each mote takes the light of
                // the froxel it floats in, so walls, doorways, shut doors and furniture shadow the motes as they do the air.
                // The volume this frame's inject writes (Execute swaps _cur first).
                Shader.SetGlobalTexture(DustLightId, o._vol[1 - o._cur]);
                Shader.SetGlobalMatrix(DustVPId, vp);
                Shader.SetGlobalVector(DustCamId, new Vector4(pos.x, pos.y, pos.z, SystemInfo.graphicsUVStartsAtTop ? -1f : 1f));
                Shader.SetGlobalVector(DustFwdId, new Vector4(fwd.x, fwd.y, fwd.z, 1f));
                o.Frames++;
            }
        }

        class CompositePass : ScriptableRenderPass
        {
            readonly VolumetricLight _o;
            class PassData { public TextureHandle depth; public Material material; }

            public CompositePass(VolumetricLight owner)
            {
                _o = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var o = _o;
                if (!o || !o._material || !o._integrated || !resources.cameraDepthTexture.IsValid() || !resources.activeColorTexture.IsValid()) return;
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Backrooms volumetric composite", out var d))
                {
                    d.depth = resources.cameraDepthTexture;
                    d.material = o._material;
                    builder.UseTexture(d.depth, AccessFlags.Read);
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, d.depth, new Vector4(1f, 1f, 0f, 0f), d.material, 0));
                }
            }
        }
    }
}
