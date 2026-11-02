using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Generation
{
    /// <summary>
    /// Screen-space light on the player's camera (F5 > LIGHT > SCREEN LIGHT; Resources/ScreenLighting.compute): after the
    /// depth prepass and before the opaque pass, at half resolution, from this frame's depth and normals and the last frame's
    /// lit picture:
    /// - near bounce and occlusion with a visibility bitmask (Therrien et al. 2023): what hides part of a surface's
    ///   hemisphere within about 1.5 m gives its own light from the last frame, so a corner between two lit walls is lit
    ///   by them rather than darkened as plain AO does, and light passes behind thin things. The rest of the hemisphere
    ///   keeps the traced probe bounce (LightGrid.Probes), which is what the lit shader scales by the visibility;
    /// - glossy reflections: one GGX ray a pixel marched through the depth (stochastic screen-space reflections), the
    ///   probes where it finds nothing;
    /// - both blended over frames, filtered edge-aware and brought back up to the full picture.
    /// The visibility also stands in for URP's SSAO (switched off on this camera's renderer while this runs): it is bound
    /// as URP's own occlusion texture, so every shader that reads SSAO (the Poolrooms' too) gets it, without a new variant.
    /// Backrooms/Lit reads the bounce and reflections (BackroomsGlobalIllumination, a uniform branch on _BRScreen).
    /// Other cameras (portal views, the water mirror, reflection captures, the scene view) get none of it.
    /// </summary>
    public class ScreenLighting : MonoBehaviour
    {
        public static ScreenLighting Instance { get; private set; }

        [Range(0f, 2f)] public float bounce = 1f;            // near bounce found on screen (0: none)
        [Range(0f, 1f)] public float occlusion = 1f;         // how much the visibility darkens the probe bounce
        [Range(0f, 1f)] public float reflections = 1f;       // glossy reflections found on screen (0: probes only)
        [Range(0f, 1f)] public float reflectionOcclusion = 1f;
        [Range(0.5f, 3f)] public float radius = 1.5f;        // metres searched round each point
        public float thickness = 0.25f;                      // what is behind a visible surface, assumed (m)
        public int slices = 2, steps = 8, reflectionSteps = 28;
        public float reflectionDistance = 14f;
        public float smoothestSkipped = 0.45f;               // smoothness below which reflections are left to the probes
        public float brightestBounce = 6f;                   // the brightest last-frame light a bounce or reflection takes

        public bool Active => isActiveAndEnabled && _cs && (bounce > 0.001f || occlusion > 0.001f || reflections > 0.001f);
        /// <summary>Frames worked out (for tests).</summary>
        public int Frames { get; private set; }

        ComputeShader _cs;
        int _kPrepare, _kGI, _kShare, _kRefl, _kTemporal, _kBlur, _kUp;
        readonly RTHandle[] _pos = new RTHandle[2], _giHist = new RTHandle[2], _refHist = new RTHandle[2];
        RTHandle _nrm, _geo, _hidden, _giTrace, _refTrace, _giBlur, _refBlur, _giOut, _refOut, _aoOut, _color;
        int _w, _h, _hw, _hh, _cur;
        bool _historyValid;
        Matrix4x4 _prevVP;
        Vector3 _prevPos, _prevFwd;
        LightPass _pass;
        LightGrid _grid;
        bool _gridBound;
        HistoryPass _historyPass;

        static readonly int ScreenId = Shader.PropertyToID("_BRScreen"), ScreenSizeId = Shader.PropertyToID("_BRScreenSize"),
            ScreenGIId = Shader.PropertyToID("_BRScreenGI"), ScreenReflId = Shader.PropertyToID("_BRScreenRefl"),
            SSAOTexId = Shader.PropertyToID("_ScreenSpaceOcclusionTexture"), SSAOParamId = Shader.PropertyToID("_AmbientOcclusionParam");
        static GlobalKeyword s_ssaoKeyword;

        public static ScreenLighting Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("ScreenLighting");
            DontDestroyOnLoad(go);
            return go.AddComponent<ScreenLighting>();
        }

        void OnEnable()
        {
            Instance = this;
            _cs = Resources.Load<ComputeShader>("ScreenLighting");
            if (_cs)
            {
                _kPrepare = _cs.FindKernel("Prepare"); _kGI = _cs.FindKernel("TraceGI"); _kShare = _cs.FindKernel("ProbeShare"); _kRefl = _cs.FindKernel("TraceRefl");
                _kTemporal = _cs.FindKernel("Temporal"); _kBlur = _cs.FindKernel("Blur"); _kUp = _cs.FindKernel("Upsample");
            }
            s_ssaoKeyword = GlobalKeyword.Create("_SCREEN_SPACE_OCCLUSION");
            _pass = new LightPass(this);
            _historyPass = new HistoryPass(this);
            Shader.SetGlobalTexture(ScreenGIId, Texture2D.blackTexture);
            Shader.SetGlobalTexture(ScreenReflId, Texture2D.blackTexture);
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            Shader.SetGlobalVector(ScreenId, Vector4.zero);
            Release();
            if (Instance == this) Instance = null;
        }

        void Release()
        {
            void R(ref RTHandle h) { h?.Release(); h = null; }
            for (int i = 0; i < 2; i++) { R(ref _pos[i]); R(ref _giHist[i]); R(ref _refHist[i]); }
            R(ref _nrm); R(ref _geo); R(ref _hidden); R(ref _giTrace); R(ref _refTrace); R(ref _giBlur); R(ref _refBlur); R(ref _giOut); R(ref _refOut); R(ref _aoOut); R(ref _color);
            _w = _h = 0;
            _historyValid = false;
        }

        void BeginCamera(ScriptableRenderContext context, Camera cam)
        {
            // Off unless this camera works its own out below (the lit shader reads the flag per camera).
            Shader.SetGlobalVector(ScreenId, Vector4.zero);
            if (!Active || cam.cameraType != CameraType.Game || cam != Camera.main) return;
            var data = cam.GetUniversalAdditionalCameraData();
            var renderer = data ? data.scriptableRenderer : null;
            if (renderer == null) return;
            renderer.EnqueuePass(_pass);
            renderer.EnqueuePass(_historyPass);
        }

        static RTHandle Alloc(int w, int h, GraphicsFormat f, string name, bool mips = false) =>
            RTHandles.Alloc(w, h, colorFormat: f, filterMode: FilterMode.Bilinear, wrapMode: TextureWrapMode.Clamp, enableRandomWrite: !mips,
                useMipMap: mips, autoGenerateMips: false, name: name);

        void Allocate(int w, int h)
        {
            if (w == _w && h == _h && _aoOut != null) return;
            Release();
            _w = w; _h = h; _hw = (w + 1) / 2; _hh = (h + 1) / 2;
            const GraphicsFormat F4 = GraphicsFormat.R16G16B16A16_SFloat, P4 = GraphicsFormat.R32G32B32A32_SFloat;
            for (int i = 0; i < 2; i++)
            {
                _pos[i] = Alloc(_hw, _hh, P4, "BRScreenPos" + i);
                // Full floats: the frame count rides with the visibility (count x 2 + visibility), and in half floats a
                // long history's small steps rounded away (the visibility stuck where it had been).
                _giHist[i] = Alloc(_hw, _hh, P4, "BRScreenGIHist" + i);
                _refHist[i] = Alloc(_hw, _hh, F4, "BRScreenRefHist" + i);
            }
            _nrm = Alloc(_hw, _hh, F4, "BRScreenNormal");
            _geo = Alloc(_hw, _hh, F4, "BRScreenPlane");
            _hidden = Alloc(_hw, _hh, F4, "BRScreenHidden");
            _giTrace = Alloc(_hw, _hh, F4, "BRScreenGITrace"); _refTrace = Alloc(_hw, _hh, F4, "BRScreenRefTrace");
            _giBlur = Alloc(_hw, _hh, F4, "BRScreenGIBlur"); _refBlur = Alloc(_hw, _hh, F4, "BRScreenRefBlur");
            _giOut = Alloc(w, h, F4, "BRScreenGI"); _refOut = Alloc(w, h, F4, "BRScreenRefl");
            _aoOut = Alloc(w, h, GraphicsFormat.R16_SFloat, "BRScreenAO");
            _color = Alloc(_hw, _hh, GraphicsFormat.B10G11R11_UFloatPack32, "BRScreenColorHist", true);
            _historyValid = false;
        }

        class LightPass : ScriptableRenderPass
        {
            readonly ScreenLighting _o;

            public LightPass(ScreenLighting owner)
            {
                _o = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses + 2;
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>();
                var o = _o;
                if (!o || !o._cs || !resources.cameraDepthTexture.IsValid() || !resources.cameraNormalsTexture.IsValid()) return;
                var desc = camera.cameraTargetDescriptor;
                o.Allocate(desc.width, desc.height);
                var cam = camera.camera;
                var view = camera.GetViewMatrix();
                // The depth prepass always draws into a texture, so its projection is the flipped one where rows start at the top.
                var proj = GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(), true);
                var vp = proj * view;
                var pos = cam.transform.position;
                var fwd = cam.transform.forward;
                bool cut = (pos - o._prevPos).sqrMagnitude > 1.5f * 1.5f || Vector3.Dot(fwd, o._prevFwd) < 0.8f;
                bool hist = o._historyValid && !cut;
                float near = SystemInfo.usesReversedZBuffer ? 1f : 0f;
                float pxPerM = o._hh / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                bool reflOn = o.reflections > 0.001f;
                // The light grid's probes, for what the bounce takes away (main thread, before the dispatch).
                if (!o._grid) o._grid = FindAnyObjectByType<LightGrid>();
                o._gridBound = o._grid && o._grid.isActiveAndEnabled && o._grid.BindPortalLighting(o._cs, o._kShare);

                using (var builder = renderGraph.AddUnsafePass<LightData>("Backrooms screen light", out var d))
                {
                    d.o = o;
                    d.depth = resources.cameraDepthTexture;
                    d.normals = resources.cameraNormalsTexture;
                    d.invVP = vp.inverse; d.vp = vp; d.prevVP = o._prevVP;
                    d.camPos = new Vector4(pos.x, pos.y, pos.z, Time.frameCount);
                    d.camFwd = new Vector4(fwd.x, fwd.y, fwd.z, near);
                    d.prevCam = new Vector4(o._prevPos.x, o._prevPos.y, o._prevPos.z, hist ? 1f : 0f);
                    d.full = new Vector4(o._w, o._h, 1f / o._w, 1f / o._h);
                    d.half = new Vector4(o._hw, o._hh, 1f / o._hw, 1f / o._hh);
                    d.gi = new Vector4(o.radius, o.thickness, pxPerM, Mathf.Clamp(o.steps, 2, 24));
                    d.gi2 = new Vector4(Mathf.Clamp(o.slices, 1, 4), o.brightestBounce, Mathf.Clamp01(o.occlusion), o.bounce > 0.001f ? 1f : 0f);
                    d.refl = new Vector4(reflOn ? 1f : 0f, o.reflectionDistance, Mathf.Clamp(o.reflectionSteps, 8, 64), o.smoothestSkipped);
                    d.screen = new Vector4(1f, o.bounce, o.reflections, o.reflectionOcclusion);
                    builder.UseTexture(d.depth, AccessFlags.Read);
                    builder.UseTexture(d.normals, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (LightData d, UnsafeGraphContext ctx) => d.o.Execute(ctx.cmd, d));
                }
                o._prevVP = vp; o._prevPos = pos; o._prevFwd = fwd;
                // The lit shader reads the results in this camera's opaque pass.
                Shader.SetGlobalVector(ScreenId, new Vector4(1f, o.bounce, o.reflections, o.reflectionOcclusion));
                Shader.SetGlobalVector(ScreenSizeId, new Vector4(o._w, o._h, 1f / o._w, 1f / o._h));
                o.Frames++;
            }
        }

        static readonly int InvVPId = Shader.PropertyToID("_InvVP"), VPId = Shader.PropertyToID("_VP"), PrevVPId = Shader.PropertyToID("_PrevVP"),
            CamPosId = Shader.PropertyToID("_CamPos"), CamFwdId = Shader.PropertyToID("_CamFwd"), PrevCamId = Shader.PropertyToID("_PrevCam"),
            FullSizeId = Shader.PropertyToID("_FullSize"), HalfSizeId = Shader.PropertyToID("_HalfSize"), GIParamsId = Shader.PropertyToID("_GIParams"),
            GIParams2Id = Shader.PropertyToID("_GIParams2"), RefParamsId = Shader.PropertyToID("_RefParams"), FlipYId = Shader.PropertyToID("_FlipY"),
            ReversedZId = Shader.PropertyToID("_ReversedZ"),
            DepthId = Shader.PropertyToID("_CameraDepthTexture"), NormalsId = Shader.PropertyToID("_CameraNormalsTexture"), ColorHistId = Shader.PropertyToID("_ColorHist"),
            HalfPosId = Shader.PropertyToID("_HalfPos"), HalfNormalId = Shader.PropertyToID("_HalfNormal"), HalfPosRId = Shader.PropertyToID("_HalfPosR"),
            HalfNormalRId = Shader.PropertyToID("_HalfNormalR"), PrevPosId = Shader.PropertyToID("_PrevPos"),
            HalfGeoId = Shader.PropertyToID("_HalfGeo"), HalfGeoRId = Shader.PropertyToID("_HalfGeoR"),
            GIHiddenId = Shader.PropertyToID("_GIHidden"), GITraceId = Shader.PropertyToID("_GITrace"), RefTraceId = Shader.PropertyToID("_RefTrace"), GITraceRId = Shader.PropertyToID("_GITraceR"),
            RefTraceRId = Shader.PropertyToID("_RefTraceR"), GIHistId = Shader.PropertyToID("_GIHist"), RefHistId = Shader.PropertyToID("_RefHist"),
            GIHistRId = Shader.PropertyToID("_GIHistR"), RefHistRId = Shader.PropertyToID("_RefHistR"), GIBlurId = Shader.PropertyToID("_GIBlur"),
            RefBlurId = Shader.PropertyToID("_RefBlur"), GIBlurRId = Shader.PropertyToID("_GIBlurR"), RefBlurRId = Shader.PropertyToID("_RefBlurR"),
            GIOutId = Shader.PropertyToID("_GIOut"), RefOutId = Shader.PropertyToID("_RefOut"), AOOutId = Shader.PropertyToID("_AOOut"),
            RTOutId = Shader.PropertyToID("_BRRTShadow");

        void Execute(UnsafeCommandBuffer cmd, LightData d)
        {
            var cs = _cs;
            _cur = 1 - _cur;
            int c = _cur, p = 1 - c;
            RTHandle depth = d.depth, normals = d.normals;
            cmd.SetComputeMatrixParam(cs, InvVPId, d.invVP);
            cmd.SetComputeMatrixParam(cs, VPId, d.vp);
            cmd.SetComputeMatrixParam(cs, PrevVPId, d.prevVP);
            cmd.SetComputeVectorParam(cs, CamPosId, d.camPos);
            cmd.SetComputeVectorParam(cs, CamFwdId, d.camFwd);
            cmd.SetComputeVectorParam(cs, PrevCamId, d.prevCam);
            cmd.SetComputeVectorParam(cs, FullSizeId, d.full);
            cmd.SetComputeVectorParam(cs, HalfSizeId, d.half);
            cmd.SetComputeVectorParam(cs, GIParamsId, d.gi);
            cmd.SetComputeVectorParam(cs, GIParams2Id, d.gi2);
            cmd.SetComputeVectorParam(cs, RefParamsId, d.refl);
            cmd.SetComputeFloatParam(cs, FlipYId, SystemInfo.graphicsUVStartsAtTop ? -1f : 1f);
            cmd.SetComputeFloatParam(cs, ReversedZId, SystemInfo.usesReversedZBuffer ? 1f : 0f);
            int hx = (_hw + 7) / 8, hy = (_hh + 7) / 8, fx = (_w + 7) / 8, fy = (_h + 7) / 8;

            cmd.SetComputeTextureParam(cs, _kPrepare, DepthId, depth);
            cmd.SetComputeTextureParam(cs, _kPrepare, NormalsId, normals);
            cmd.SetComputeTextureParam(cs, _kPrepare, HalfPosId, _pos[c]);
            cmd.SetComputeTextureParam(cs, _kPrepare, HalfNormalId, _nrm);
            cmd.SetComputeTextureParam(cs, _kPrepare, HalfGeoId, _geo);
            cmd.DispatchCompute(cs, _kPrepare, hx, hy, 1);

            for (int i = 0; i < 2; i++)
            {
                int k = i == 0 ? _kGI : _kRefl;
                cmd.SetComputeTextureParam(cs, k, HalfPosRId, _pos[c]);
                cmd.SetComputeTextureParam(cs, k, HalfNormalRId, _nrm);
                cmd.SetComputeTextureParam(cs, k, HalfGeoRId, _geo);
                cmd.SetComputeTextureParam(cs, k, PrevPosId, _pos[p]);
                cmd.SetComputeTextureParam(cs, k, ColorHistId, _color);
            }
            cmd.SetComputeTextureParam(cs, _kGI, GITraceId, _giTrace);
            cmd.SetComputeTextureParam(cs, _kGI, GIHiddenId, _hidden);
            cmd.DispatchCompute(cs, _kGI, hx, hy, 1);
            if (_gridBound)
            {
                cmd.SetComputeTextureParam(cs, _kShare, HalfPosRId, _pos[c]);
                cmd.SetComputeTextureParam(cs, _kShare, HalfNormalRId, _nrm);
                cmd.SetComputeTextureParam(cs, _kShare, GIHiddenId, _hidden);
                cmd.SetComputeTextureParam(cs, _kShare, GITraceId, _giTrace);
                cmd.DispatchCompute(cs, _kShare, hx, hy, 1);
            }
            // With hardware ray tracing the reflection rays are traced in the real scene (RayTracedLighting, built this frame
            // before this pass), not marched through the depth; the same blending and filters follow.
            var rt = RayTracedLighting.Instance;
            if (d.refl.x > 0.5f && RayTracedLighting.ReflectionsActive)
                rt.TraceReflections(CommandBufferHelpers.GetNativeCommandBuffer(cmd), d, _pos[c], _nrm, _geo, _pos[p], _color, _refTrace, _hw, _hh, brightestBounce);
            else
            {
                cmd.SetComputeTextureParam(cs, _kRefl, RefTraceId, _refTrace);
                cmd.DispatchCompute(cs, _kRefl, hx, hy, 1);
            }

            cmd.SetComputeTextureParam(cs, _kTemporal, HalfPosRId, _pos[c]);
            cmd.SetComputeTextureParam(cs, _kTemporal, PrevPosId, _pos[p]);
            cmd.SetComputeTextureParam(cs, _kTemporal, GITraceRId, _giTrace);
            cmd.SetComputeTextureParam(cs, _kTemporal, RefTraceRId, _refTrace);
            cmd.SetComputeTextureParam(cs, _kTemporal, GIHistRId, _giHist[p]);
            cmd.SetComputeTextureParam(cs, _kTemporal, RefHistRId, _refHist[p]);
            cmd.SetComputeTextureParam(cs, _kTemporal, GIHistId, _giHist[c]);
            cmd.SetComputeTextureParam(cs, _kTemporal, RefHistId, _refHist[c]);
            cmd.DispatchCompute(cs, _kTemporal, hx, hy, 1);

            cmd.SetComputeTextureParam(cs, _kBlur, HalfPosRId, _pos[c]);
            cmd.SetComputeTextureParam(cs, _kBlur, HalfNormalRId, _nrm);
            cmd.SetComputeTextureParam(cs, _kBlur, GIHistRId, _giHist[c]);
            cmd.SetComputeTextureParam(cs, _kBlur, RefHistRId, _refHist[c]);
            cmd.SetComputeTextureParam(cs, _kBlur, GIBlurId, _giBlur);
            cmd.SetComputeTextureParam(cs, _kBlur, RefBlurId, _refBlur);
            cmd.DispatchCompute(cs, _kBlur, hx, hy, 1);

            cmd.SetComputeTextureParam(cs, _kUp, DepthId, depth);
            cmd.SetComputeTextureParam(cs, _kUp, NormalsId, normals);
            cmd.SetComputeTextureParam(cs, _kUp, HalfPosRId, _pos[c]);
            cmd.SetComputeTextureParam(cs, _kUp, HalfNormalRId, _nrm);
            cmd.SetComputeTextureParam(cs, _kUp, GIBlurRId, _giBlur);
            cmd.SetComputeTextureParam(cs, _kUp, RefBlurRId, _refBlur);
            cmd.SetComputeTextureParam(cs, _kUp, GIOutId, _giOut);
            cmd.SetComputeTextureParam(cs, _kUp, RefOutId, _refOut);
            cmd.SetComputeTextureParam(cs, _kUp, AOOutId, _aoOut);
            // The ray-traced occlusion (RayTracedLighting, before this pass), bound explicitly: a global that was never set
            // left the kernel unbound and Unity skipped it. White (nothing hidden) when there is none this frame.
            var rtOut = rt ? rt.OutputThisFrame : null;
            if (rtOut != null) cmd.SetComputeTextureParam(cs, _kUp, RTOutId, rtOut);
            else cmd.SetComputeTextureParam(cs, _kUp, RTOutId, Texture2D.whiteTexture);
            cmd.DispatchCompute(cs, _kUp, fx, fy, 1);

            // For the opaque pass: the bounce and reflections for Backrooms/Lit, and the visibility as URP's own SSAO
            // (its keyword and texture; direct light untouched, as the SSAO settings had it).
            cmd.SetGlobalTexture(ScreenGIId, _giOut);
            cmd.SetGlobalTexture(ScreenReflId, _refOut);
            cmd.SetGlobalTexture(SSAOTexId, _aoOut);
            cmd.SetGlobalVector(SSAOParamId, new Vector4(occlusion > 0.001f ? 1f : 0f, 0f, 0f, 0f));
            cmd.SetKeyword(s_ssaoKeyword, occlusion > 0.001f);
        }

        internal class LightData
        {
            public ScreenLighting o;
            public TextureHandle depth, normals;
            public Matrix4x4 invVP, vp, prevVP;
            public Vector4 camPos, camFwd, prevCam, full, half, gi, gi2, refl, screen;
        }

        class HistoryPass : ScriptableRenderPass
        {
            readonly ScreenLighting _o;
            class PassData { public ScreenLighting o; public TextureHandle color; }

            public HistoryPass(ScreenLighting owner)
            {
                _o = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var o = _o;
                if (!o || o._color == null || !resources.activeColorTexture.IsValid()) return;
                using (var builder = renderGraph.AddUnsafePass<PassData>("Backrooms screen light history", out var d))
                {
                    d.o = o;
                    d.color = resources.activeColorTexture;
                    builder.UseTexture(d.color, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData d, UnsafeGraphContext ctx) =>
                    {
                        var native = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                        Blitter.BlitCameraTexture(native, d.color, d.o._color, 0f, true);
                        native.GenerateMips(d.o._color);
                        d.o._historyValid = true;
                    });
                }
            }
        }
    }
}
