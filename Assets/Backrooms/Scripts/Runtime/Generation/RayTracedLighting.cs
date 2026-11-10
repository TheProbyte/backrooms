using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Generation
{
    /// <summary>
    /// Hardware ray-traced light on the player's camera (F5 > LIGHT > RAY TRACING; Resources/RayTracedLighting.compute),
    /// with inline ray queries on Direct3D 12 (an RTX or RDNA2+ card; elsewhere everything stays as it was).
    ///
    /// The scene: each frame the renderers round the camera are gathered into an acceleration structure
    /// (<see cref="RayTracingAccelerationStructure.CullInstances"/>, the way HDRP does it): what casts shadows (furniture,
    /// props, doors, people, the player's body and the warehouse dressing's shadow boxes) under one instance mask, what is
    /// seen (walls, floors, ceilings and the rest) under another. Walls stay out of the shadow rays: the light grid's own
    /// line test already stops every fixture at them exactly, penumbras past wall ends included.
    ///
    /// Shadows (MegaLights-style, Epic 2025, with Heitz et al.'s 2018 ratio estimator): each pixel picks a fixture in
    /// proportion to the light it gives there and traces a ray to a random point on its panel; over frames and a small
    /// edge-aware filter that becomes the share of the fixtures' light that reaches the pixel past everything in the way.
    /// Backrooms/Lit lights every fixture as before and multiplies their sum by it (_BRRTShadow). So every fixture, not only
    /// the nearest 16-32, shadows every chair leg, desk and person round it, with the soft-edged, contact-hardening shadow a
    /// panel of that size casts; the fixtures' shadow maps are switched off meanwhile (FixtureLightManager), which also saves
    /// the render thread redrawing the scene into up to 32 of them a frame. The contact shadows and the furniture boxes'
    /// direct-light test are left out where the rays run (they would count the same things twice).
    /// Other cameras (crawl-hole views, the water mirror, reflection captures) keep the boxes.
    ///
    /// Occlusion: one short cosine-weighted ray a pixel against the casters; ScreenLighting takes the smaller of it and its
    /// own screen-space visibility (what is under a desk is hidden from the probes' bounce whether the desk's top is on
    /// screen or not). Reflections: ScreenLighting's reflection rays are traced here instead of marched through the depth
    /// (<see cref="TraceReflections"/>, at its half resolution, into its own blending and filters): a hit takes the last
    /// frame's light where it was on screen, else light worked out from the light grid at the hit; panels and misses are
    /// left to the reflection probes.
    /// </summary>
    public class RayTracedLighting : MonoBehaviour
    {
        public static RayTracedLighting Instance { get; private set; }

        [Tooltip("Ray-traced shadows for every fixture (replaces their shadow maps).")]
        public bool shadows = true;
        [Tooltip("Ray-traced reflections (in place of the screen-space march).")]
        public bool reflections = true;
        [Tooltip("Ray-traced short-range occlusion of the bounce light.")]
        public bool occlusion = true;
        [Range(0.3f, 2f)] public float occlusionRadius = 1f;
        [Range(0f, 1f)] public float occlusionStrength = 0.8f;
        [Tooltip("Brightness of what the reflections hit off screen (the surface colour assumed there).")]
        [Range(0f, 1f)] public float offscreenAlbedo = 0.45f;
        public float reflectionDistance = 20f;
        [Tooltip("Debug: build the scene the rays see but trace nothing (for telling a slow build from slow rays).")]
        public bool debugBuildOnly;
        [Range(1, 4)] public int raysPerPixel = 1;
        [Tooltip("Metres from the camera over which the ray-traced shadows fade out (the fog is thick there).")]
        public float fadeStart = 30f, fadeEnd = 34f;
        [Tooltip("Most frames the shadows are blended over (more: smoother penumbras, slower to follow a moving shadow).")]
        [Range(2, 32)] public int historyFrames = 8;
        [Tooltip("How soft the shadows are: the glowing panels taken this much of their size (1: true size, wide soft penumbras; " +
                 "smaller: crisper, more drawn shadows, the Overflow look's, 2026-11-07).")]
        [Range(0.05f, 1f)] public float penumbra = 0.4f;
        [Tooltip("What is gathered into the scene the rays see: within this many metres of the camera.")]
        public float sceneRadius = 36f;

        /// <summary>The card and graphics API can trace rays from compute shaders (Direct3D 12 on RTX / RDNA2 or newer).</summary>
        public static bool Supported => SystemInfo.supportsInlineRayTracing && SystemInfo.supportsRayTracing;
        /// <summary>True while ray-traced shadows replace the fixtures' shadow maps (read by FixtureLightManager).</summary>
        public static bool ShadowsActive => Instance && Instance.isActiveAndEnabled && Instance.shadows && Instance._ready;
        /// <summary>True while ScreenLighting's reflection rays are traced here (this frame's scene is built).</summary>
        public static bool ReflectionsActive => Instance && Instance.isActiveAndEnabled && Instance.reflections && Instance._ready && !Instance.debugBuildOnly && Instance._builtFrame == Time.frameCount;
        bool Any => shadows || reflections || occlusion;
        /// <summary>This frame's shadows (x) and occlusion (y), for ScreenLighting to bind; null when not worked out this frame.</summary>
        internal RTHandle OutputThisFrame => isActiveAndEnabled && _ready && !debugBuildOnly && _builtFrame == Time.frameCount ? _out : null;

        /// <summary>Frames worked out, and the instances in the scene last frame (for tests and the debug readout).</summary>
        public int Frames { get; private set; }
        public int SceneInstances { get; private set; }
        [Tooltip("Debug: count the instances in the rays' scene each gather (SceneInstances). It stalls the main thread until " +
                 "the render thread has caught up, so leave it off outside a measurement.")]
        public bool countInstances;
        public float CullMs { get; private set; }

        internal const uint MaskCasters = 1u, MaskVisible = 2u;

        ComputeShader _cs;
        int _kTrace, _kTemporal, _kBlur1, _kBlur, _kReflect;
        bool _ready;
        int _builtFrame = -1;
        RayTracingAccelerationStructure _scene;
        RayTracingInstanceCullingConfig _cull;
        readonly RTHandle[] _pos = new RTHandle[2], _hist = new RTHandle[2];
        RTHandle _trace, _out;
        int _w, _h, _cur;
        bool _historyValid;
        Matrix4x4 _prevVP;
        Vector3 _prevPos, _prevFwd;
        TracePass _pass;
        readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();

        static readonly int RTId = Shader.PropertyToID("_BRRT"), RTShadowId = Shader.PropertyToID("_BRRTShadow");

        public static RayTracedLighting Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("RayTracedLighting");
            DontDestroyOnLoad(go);
            return go.AddComponent<RayTracedLighting>();
        }

        void OnEnable()
        {
            Instance = this;
            _ready = false;
            Shader.SetGlobalVector(RTId, Vector4.zero);
            Shader.SetGlobalTexture(RTShadowId, Texture2D.whiteTexture);
            if (!Supported) return;
            _cs = Resources.Load<ComputeShader>("RayTracedLighting");
            if (!_cs) return;
            _kTrace = _cs.FindKernel("Trace"); _kTemporal = _cs.FindKernel("Temporal"); _kBlur1 = _cs.FindKernel("Blur1"); _kBlur = _cs.FindKernel("Blur"); _kReflect = _cs.FindKernel("Reflect");
            if (!_cs.IsSupported(_kTrace)) { Debug.LogWarning("[Backrooms] Ray-traced light: the compute shader is not supported here."); return; }
            _scene ??= new RayTracingAccelerationStructure(new RayTracingAccelerationStructure.Settings(
                RayTracingAccelerationStructure.ManagementMode.Manual, RayTracingAccelerationStructure.RayTracingModeMask.Everything, -1,
                RayTracingAccelerationStructureBuildFlags.PreferFastTrace, RayTracingAccelerationStructureBuildFlags.PreferFastBuild));
            _historyValid = false;
            _radiusNow = 0f;                                              // grow the scene again (GatherScene)
            _cull = MakeCullingConfig();
            _pass = new TracePass(this);
            _ready = true;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RestoreShadowVariants();
            _ready = false;
            Shader.SetGlobalVector(RTId, Vector4.zero);
            Shader.SetGlobalTexture(RTShadowId, Texture2D.whiteTexture);
            // The scene and the textures are kept, and so is Instance (Ensure finds this one again rather than making a second
            // with an empty scene): released in OnDestroy. The render thread runs a frame behind the main thread.
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Release();
            _scene?.Dispose(); _scene = null;
        }

        /// <summary>
        /// What the rays see. Casters (shadow-casting renderers, shadow-only ones included) under <see cref="MaskCasters"/>;
        /// whatever is drawn (casting or not) under <see cref="MaskVisible"/>. Transparent materials are left out.
        /// </summary>
        static RayTracingInstanceCullingConfig MakeCullingConfig()
        {
            var c = new RayTracingInstanceCullingConfig();
            c.flags = RayTracingInstanceCullingFlags.EnableSphereCulling;
            c.subMeshFlagsConfig.opaqueMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
            c.subMeshFlagsConfig.alphaTestedMaterials = RayTracingSubMeshFlags.Enabled | RayTracingSubMeshFlags.ClosestHitOnly;
            c.subMeshFlagsConfig.transparentMaterials = RayTracingSubMeshFlags.Disabled;
            c.transparentMaterialConfig.renderQueueLowerBound = 2501; c.transparentMaterialConfig.renderQueueUpperBound = 5000;
            c.alphaTestedMaterialConfig.renderQueueLowerBound = 2450; c.alphaTestedMaterialConfig.renderQueueUpperBound = 2500;
            var casters = new RayTracingInstanceCullingTest
            {
                allowOpaqueMaterials = true, allowAlphaTestedMaterials = true, allowTransparentMaterials = false, allowVisualEffects = false,
                layerMask = -1, instanceMask = MaskCasters,
                shadowCastingModeMask = (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided) | (1 << (int)ShadowCastingMode.ShadowsOnly),
            };
            var visible = new RayTracingInstanceCullingTest
            {
                allowOpaqueMaterials = true, allowAlphaTestedMaterials = true, allowTransparentMaterials = false, allowVisualEffects = false,
                layerMask = -1, instanceMask = MaskVisible,
                shadowCastingModeMask = (1 << (int)ShadowCastingMode.Off) | (1 << (int)ShadowCastingMode.On) | (1 << (int)ShadowCastingMode.TwoSided),
            };
            c.instanceTests = new[] { casters, visible };
            return c;
        }

        // URP's renderer setting "strip shadows-off variants": with no shadow-mapped light in view it keeps the lit shaders'
        // additional-light-shadow keyword on and binds an empty shadow map, instead of switching to the variants without
        // shadows. With the fixtures' maps off (ray-traced shadows) every camera would otherwise draw with variants the editor
        // has not compiled ahead (LitShaderWarmup: a long compile mid-play lost the Direct3D 12 device, 2026-10-26).
        static readonly System.Reflection.PropertyInfo s_stripShadowsOff = typeof(ScriptableRenderer).GetProperty("stripShadowsOffVariants",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        readonly System.Collections.Generic.Dictionary<ScriptableRenderer, bool> _stripOrig = new System.Collections.Generic.Dictionary<ScriptableRenderer, bool>();

        void KeepShadowVariants(ScriptableRenderer renderer)
        {
            if (renderer == null || s_stripShadowsOff == null || _stripOrig.ContainsKey(renderer)) return;
            _stripOrig[renderer] = (bool)s_stripShadowsOff.GetValue(renderer);
            s_stripShadowsOff.SetValue(renderer, true);
        }

        void RestoreShadowVariants()
        {
            foreach (var kv in _stripOrig) if (kv.Key != null) s_stripShadowsOff.SetValue(kv.Key, kv.Value);
            _stripOrig.Clear();
        }

        void BeginCamera(ScriptableRenderContext context, Camera cam)
        {
            // Off unless this camera works its own out below (the lit shader reads the flag per camera).
            Shader.SetGlobalVector(RTId, Vector4.zero);
            if (!_ready) return;
            var data = cam.GetUniversalAdditionalCameraData();
            var renderer = data ? data.scriptableRenderer : null;
            if (renderer == null) return;
            if (shadows) KeepShadowVariants(renderer);                   // every camera: their fixtures have no maps either
            if (!Any || cam.cameraType != CameraType.Game || cam != Camera.main) return;
            renderer.EnqueuePass(_pass);
        }

        /// <summary>The renderers round the camera, gathered afresh (moved props, doors and the body follow).</summary>
        // The radius gathered this frame. After the rays come on, or a jump of the camera (a teleport, a crawl hole), it grows
        // from a few metres to sceneRadius over about a second: every mesh new to the scene has its own acceleration structure
        // built on the graphics card the frame it joins, and a dense area joining all at once kept the card busy long enough
        // for Windows to reset it (DXGI_ERROR_DEVICE_HUNG, 2026-11-02). Streamed chunks join a few at a time anyway.
        // (2026-11-07) Each new mesh's structure is also a fresh block of graphics memory, and making one holds up the driver's
        // present: the hub's lift arriving (hundreds of meshes in ~20 frames) gave frames of 60-160 ms. So the growth is slower
        // (0.6 m a frame: ~1 s to full), and a known jump (the lift) is warmed up beforehand (Prewarm).
        float _radiusNow;
        Vector3 _lastCentre;
        // Growth in floor area a frame (m^2), not radius: the same number of new meshes a frame however far out it has got.
        const float RadiusStart = 6f, RadiusGrowth = 70f;       // ~1 s from 6 m to 36 m

        float Grow(float r, float area)
        {
            if (r <= 0f) return RadiusStart;
            return Mathf.Min(Mathf.Sqrt(r * r + area / Mathf.PI), sceneRadius);
        }

        // ---- Warming up a place the camera is about to jump to (the hub's lift during its ride) ----
        static Vector3 s_prewarmAt;
        static bool s_prewarmOn;
        static int s_prewarmVersion;
        int _prewarmSeen = -1, _gatherFrame = -100;
        float _prewarmRadius;
        const float PrewarmGrowth = 25f;                        // m^2 a frame: a few meshes at a time (~2.6 s to full)

        /// <summary>
        /// Gathers the renderers round <paramref name="at"/> into the rays' scene as well, growing slowly, so their structures
        /// are built before the camera gets there (the lift's swap): when it jumps into that sphere it starts at its size.
        /// </summary>
        public static void Prewarm(Vector3 at)
        {
            if (s_prewarmOn && (s_prewarmAt - at).sqrMagnitude < 1f) return;
            s_prewarmAt = at; s_prewarmOn = true; s_prewarmVersion++;
        }

        public static void ClearPrewarm() => s_prewarmOn = false;

        /// <summary>Is the warm-up done (or are the rays off, so there is nothing to warm)?</summary>
        public static bool PrewarmReady
        {
            get
            {
                var o = Instance;
                if (!s_prewarmOn || !o || !o.isActiveAndEnabled || Time.frameCount - o._gatherFrame > 3) return true;
                return o._prewarmSeen == s_prewarmVersion && o._prewarmRadius >= o.sceneRadius - 0.01f;
            }
        }

        [Tooltip("The rays' scene is gathered afresh every this many frames (1: every frame). In between, the last one is " +
                 "traced again: what moved in that frame casts its shadow from where it was a frame ago, which the shadows' " +
                 "own blending over several frames hides.")]
        [Range(1, 4)] public int gatherEvery = 2;
        int _gatherSkip;

        void GatherScene(Vector3 centre)
        {
            // (2026-11-09, Docs/ImprovementPlan.md E2.) Clearing the scene and culling every renderer into it again cost
            // 1.4 ms of the main thread a frame here (3,000 instances out of 12,000 renderers). Measured: a renderer that is
            // destroyed leaves the scene by itself, so a frame without a gather traces nothing stale but positions. So it
            // is gathered every other frame, except while it is still growing or being warmed, or after a jump.
            bool settled = _radiusNow >= sceneRadius - 0.01f && !s_prewarmOn && (centre - _lastCentre).sqrMagnitude < 8f * 8f;
            if (gatherEvery > 1 && settled && ++_gatherSkip < gatherEvery)
            {
                _gatherFrame = Time.frameCount;
                _lastCentre = centre;
                return;
            }
            _gatherSkip = 0;
            _watch.Restart();
            _gatherFrame = Time.frameCount;
            if (s_prewarmOn && _prewarmSeen != s_prewarmVersion) { _prewarmSeen = s_prewarmVersion; _prewarmRadius = 0f; }
            if (!s_prewarmOn) _prewarmRadius = 0f;
            if ((centre - _lastCentre).sqrMagnitude > 8f * 8f)
            {
                // A jump into the warmed sphere starts at its size (those meshes are built already); anywhere else from small.
                bool warm = s_prewarmOn && _prewarmRadius > RadiusStart && (centre - s_prewarmAt).sqrMagnitude < 8f * 8f;
                _radiusNow = warm ? _prewarmRadius : 0f;
            }
            _lastCentre = centre;
            _radiusNow = Grow(_radiusNow, RadiusGrowth);
            _cull.sphereCenter = centre;
            _cull.sphereRadius = _radiusNow;
            _scene.ClearInstances();
            _scene.CullInstances(ref _cull);
            if (s_prewarmOn)
            {
                _prewarmRadius = Grow(_prewarmRadius, PrewarmGrowth);
                // Only while it is somewhere else (once the camera is there, the sphere above holds the same meshes).
                if ((s_prewarmAt - centre).sqrMagnitude > (2f * sceneRadius) * (2f * sceneRadius))
                {
                    _cull.sphereCenter = s_prewarmAt;
                    _cull.sphereRadius = _prewarmRadius;
                    _scene.CullInstances(ref _cull);
                }
            }
            // (2026-11-09: asking the scene how many instances it holds is answered by the render thread, so the main
            // thread stopped and waited for it to finish everything queued before: 6.6 ms of every frame in the profile,
            // for a number nothing reads but a debugger. Asked only when someone wants it.)
            if (countInstances) SceneInstances = (int)_scene.GetInstanceCount();
            _watch.Stop();
            CullMs = Mathf.Lerp(CullMs, (float)_watch.Elapsed.TotalMilliseconds, 0.1f);
        }

        LightGrid _grid;
        static readonly int GridParamsId = Shader.PropertyToID("_LGridParams");

        /// <summary>
        /// The light grid's fixtures, tiles, walls and light, bound to the kernels that read them. A compute shader does not
        /// see the grid's global bindings (ScreenLighting binds it the same way); unbound, the tile loops read garbage
        /// counts and hung the graphics card (2026-11-02). Without the player's grid the shadows and off-screen
        /// reflections are left out (its params zeroed).
        /// </summary>
        void BindGrid()
        {
            if (!_grid || !_grid.isActiveAndEnabled || !_grid.bindGlobals)
            {
                _grid = null;
                foreach (var g in FindObjectsByType<LightGrid>(FindObjectsSortMode.None))
                    if (g.isActiveAndEnabled && g.bindGlobals) { _grid = g; break; }
            }
            bool bound = _grid && _grid.BindPortalLighting(_cs, _kTrace) && _grid.BindPortalLighting(_cs, _kReflect);
            if (!bound) _cs.SetVector(GridParamsId, Vector4.zero);
            _cs.SetFloat(PenumbraId, Mathf.Clamp(penumbra, 0.05f, 1f));
        }

        static readonly int PenumbraId = Shader.PropertyToID("_RTPenumbra");

        void Release()
        {
            void R(ref RTHandle h) { h?.Release(); h = null; }
            for (int i = 0; i < 2; i++) { R(ref _pos[i]); R(ref _hist[i]); }
            R(ref _trace); R(ref _out);
            _w = _h = 0;
            _historyValid = false;
        }

        static RTHandle Alloc(int w, int h, GraphicsFormat f, string name) =>
            RTHandles.Alloc(w, h, colorFormat: f, filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, enableRandomWrite: true, name: name);

        void Allocate(int w, int h)
        {
            if (w == _w && h == _h && _out != null) return;
            Release();
            _w = w; _h = h;
            for (int i = 0; i < 2; i++)
            {
                _pos[i] = Alloc(w, h, GraphicsFormat.R32G32B32A32_SFloat, "BRRTPos" + i);
                _hist[i] = Alloc(w, h, GraphicsFormat.R16G16B16A16_SFloat, "BRRTHist" + i);
            }
            _trace = Alloc(w, h, GraphicsFormat.R16G16B16A16_SFloat, "BRRTTrace");
            _out = Alloc(w, h, GraphicsFormat.R16G16_SFloat, "BRRTShadow");
            _historyValid = false;
        }

        class PassData
        {
            public RayTracedLighting o;
            public TextureHandle depth, normals;
            public Matrix4x4 invVP, prevVP;
            public Vector4 camPos, camFwd, prevCam, size, shadow, occ;
        }

        class TracePass : ScriptableRenderPass
        {
            readonly RayTracedLighting _o;

            public TracePass(RayTracedLighting owner)
            {
                _o = owner;
                // Before the screen light's pass (AfterRenderingPrePasses + 2), which traces its reflections in this scene and
                // takes this occlusion; and before the opaque pass that reads the shadows.
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses + 1;
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>();
                var o = _o;
                if (!o || !o._ready || !resources.cameraDepthTexture.IsValid() || !resources.cameraNormalsTexture.IsValid()) return;
                var desc = camera.cameraTargetDescriptor;
                o.Allocate(desc.width, desc.height);
                var cam = camera.camera;
                var pos = cam.transform.position;
                var fwd = cam.transform.forward;
                o.GatherScene(pos);
                o.BindGrid();
                // The depth prepass draws into a texture: its projection is the flipped one where rows start at the top.
                var vp = GL.GetGPUProjectionMatrix(camera.GetProjectionMatrix(), true) * camera.GetViewMatrix();
                bool cut = (pos - o._prevPos).sqrMagnitude > 1.5f * 1.5f || Vector3.Dot(fwd, o._prevFwd) < 0.8f;
                bool hist = o._historyValid && !cut;
                using (var builder = renderGraph.AddUnsafePass<PassData>("Backrooms ray-traced light", out var d))
                {
                    d.o = o;
                    d.depth = resources.cameraDepthTexture;
                    d.normals = resources.cameraNormalsTexture;
                    d.invVP = vp.inverse; d.prevVP = o._prevVP;
                    d.camPos = new Vector4(pos.x, pos.y, pos.z, Time.frameCount);
                    d.camFwd = new Vector4(fwd.x, fwd.y, fwd.z, 0f);
                    d.prevCam = new Vector4(o._prevPos.x, o._prevPos.y, o._prevPos.z, hist ? 1f : 0f);
                    d.size = new Vector4(o._w, o._h, 1f / o._w, 1f / o._h);
                    d.shadow = new Vector4(o.shadows ? Mathf.Clamp(o.raysPerPixel, 1, 4) : 0, o.fadeStart, Mathf.Max(o.fadeEnd, o.fadeStart + 0.5f), Mathf.Clamp(o.historyFrames, 2, 32));
                    d.occ = new Vector4(o.occlusion ? 1f : 0f, Mathf.Clamp(o.occlusionRadius, 0.3f, 2f), Mathf.Clamp01(o.occlusionStrength), 0f);
                    builder.UseTexture(d.depth, AccessFlags.Read);
                    builder.UseTexture(d.normals, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (PassData d, UnsafeGraphContext ctx) => d.o.Execute(CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd), d));
                }
                o._prevVP = vp; o._prevPos = pos; o._prevFwd = fwd;
                o._historyValid = true;
                o._builtFrame = Time.frameCount;
                // The lit shader reads the shadows in this camera's opaque pass (x), ScreenLighting the occlusion (w).
                Shader.SetGlobalVector(RTId, new Vector4(o.shadows ? 1f : 0f, o._w, o._h, o.occlusion ? 1f : 0f));
                o.Frames++;
            }
        }

        static readonly int SceneId = Shader.PropertyToID("_RTScene"), InvVPId = Shader.PropertyToID("_InvVP"), PrevVPId = Shader.PropertyToID("_PrevVP"),
            CamPosId = Shader.PropertyToID("_CamPos"), CamFwdId = Shader.PropertyToID("_CamFwd"), PrevCamId = Shader.PropertyToID("_PrevCam"),
            SizeId = Shader.PropertyToID("_Size"), ShadowParamsId = Shader.PropertyToID("_ShadowParams"), OccParamsId = Shader.PropertyToID("_OccParams"), FlipYId = Shader.PropertyToID("_FlipY"),
            ReversedZId = Shader.PropertyToID("_ReversedZ"), DepthId = Shader.PropertyToID("_CameraDepthTexture"), NormalsId = Shader.PropertyToID("_CameraNormalsTexture"),
            PosId = Shader.PropertyToID("_Pos"), PosRId = Shader.PropertyToID("_PosR"), PrevPosId = Shader.PropertyToID("_PrevPos"),
            TraceId = Shader.PropertyToID("_Trace"), TraceRId = Shader.PropertyToID("_TraceR"), HistId = Shader.PropertyToID("_Hist"),
            HistRId = Shader.PropertyToID("_HistR"), OutId = Shader.PropertyToID("_Out"), FiltId = Shader.PropertyToID("_Filt");

        void Execute(CommandBuffer cmd, PassData d)
        {
            var cs = _cs;
            _cur = 1 - _cur;
            int c = _cur, p = 1 - c;
            cmd.BuildRayTracingAccelerationStructure(_scene);
            if (debugBuildOnly) { cmd.SetGlobalTexture(RTShadowId, Texture2D.whiteTexture); return; }
            cmd.SetComputeMatrixParam(cs, InvVPId, d.invVP);
            cmd.SetComputeMatrixParam(cs, PrevVPId, d.prevVP);
            cmd.SetComputeVectorParam(cs, CamPosId, d.camPos);
            cmd.SetComputeVectorParam(cs, CamFwdId, d.camFwd);
            cmd.SetComputeVectorParam(cs, PrevCamId, d.prevCam);
            cmd.SetComputeVectorParam(cs, SizeId, d.size);
            cmd.SetComputeVectorParam(cs, ShadowParamsId, d.shadow);
            cmd.SetComputeVectorParam(cs, OccParamsId, d.occ);
            cmd.SetComputeFloatParam(cs, FlipYId, SystemInfo.graphicsUVStartsAtTop ? -1f : 1f);
            cmd.SetComputeFloatParam(cs, ReversedZId, SystemInfo.usesReversedZBuffer ? 1f : 0f);
            int gx = (_w + 7) / 8, gy = (_h + 7) / 8;

            cmd.SetRayTracingAccelerationStructure(cs, _kTrace, SceneId, _scene);
            cmd.SetComputeTextureParam(cs, _kTrace, DepthId, d.depth);
            cmd.SetComputeTextureParam(cs, _kTrace, NormalsId, d.normals);
            cmd.SetComputeTextureParam(cs, _kTrace, PosId, _pos[c]);
            cmd.SetComputeTextureParam(cs, _kTrace, TraceId, _trace);
            cmd.DispatchCompute(cs, _kTrace, gx, gy, 1);

            cmd.SetComputeTextureParam(cs, _kTemporal, PosRId, _pos[c]);
            cmd.SetComputeTextureParam(cs, _kTemporal, PrevPosId, _pos[p]);
            cmd.SetComputeTextureParam(cs, _kTemporal, TraceRId, _trace);
            cmd.SetComputeTextureParam(cs, _kTemporal, HistRId, _hist[p]);
            cmd.SetComputeTextureParam(cs, _kTemporal, HistId, _hist[c]);
            cmd.DispatchCompute(cs, _kTemporal, gx, gy, 1);

            // The filter's two passes (2026-11-02): the first into the trace texture (free once blended), the second out.
            cmd.SetComputeTextureParam(cs, _kBlur1, PosRId, _pos[c]);
            cmd.SetComputeTextureParam(cs, _kBlur1, NormalsId, d.normals);
            cmd.SetComputeTextureParam(cs, _kBlur1, HistRId, _hist[c]);
            cmd.SetComputeTextureParam(cs, _kBlur1, FiltId, _trace);
            cmd.DispatchCompute(cs, _kBlur1, gx, gy, 1);

            cmd.SetComputeTextureParam(cs, _kBlur, PosRId, _pos[c]);
            cmd.SetComputeTextureParam(cs, _kBlur, NormalsId, d.normals);
            cmd.SetComputeTextureParam(cs, _kBlur, HistRId, _trace);
            cmd.SetComputeTextureParam(cs, _kBlur, OutId, _out);
            cmd.DispatchCompute(cs, _kBlur, gx, gy, 1);

            cmd.SetGlobalTexture(RTShadowId, _out);
        }

        static readonly int HalfPosRId = Shader.PropertyToID("_HalfPosR"), HalfNormalRId = Shader.PropertyToID("_HalfNormalR"),
            HalfGeoRId = Shader.PropertyToID("_HalfGeoR"), HalfPrevPosId = Shader.PropertyToID("_HalfPrevPos"), ColorHistId = Shader.PropertyToID("_ColorHist"),
            RefTraceId = Shader.PropertyToID("_RefTrace"), HalfSizeId = Shader.PropertyToID("_HalfSize"), RefParamsId = Shader.PropertyToID("_RefParams"),
            RefParams2Id = Shader.PropertyToID("_RefParams2");

        /// <summary>
        /// ScreenLighting's reflection rays, traced in this frame's scene (called from its pass, in place of its depth march):
        /// into its half-size reflection texture, which it then blends over frames, filters and brings up to the full picture.
        /// </summary>
        internal void TraceReflections(CommandBuffer cmd, ScreenLighting.LightData d, RenderTargetIdentifier halfPos, RenderTargetIdentifier halfNormal,
            RenderTargetIdentifier halfGeo, RenderTargetIdentifier prevPos, RenderTargetIdentifier colorHist, RenderTargetIdentifier refTrace,
            int hw, int hh, float brightest)
        {
            var cs = _cs;
            cmd.SetComputeMatrixParam(cs, PrevVPId, d.prevVP);
            cmd.SetComputeVectorParam(cs, CamPosId, d.camPos);
            cmd.SetComputeVectorParam(cs, CamFwdId, d.camFwd);
            cmd.SetComputeVectorParam(cs, HalfSizeId, d.half);
            cmd.SetComputeVectorParam(cs, RefParamsId, new Vector4(d.refl.x, reflectionDistance, d.gi.z, d.refl.w));
            cmd.SetComputeVectorParam(cs, RefParams2Id, new Vector4(brightest, offscreenAlbedo, d.prevCam.w, 0f));
            cmd.SetComputeFloatParam(cs, FlipYId, SystemInfo.graphicsUVStartsAtTop ? -1f : 1f);
            cmd.SetRayTracingAccelerationStructure(cs, _kReflect, SceneId, _scene);
            cmd.SetComputeTextureParam(cs, _kReflect, HalfPosRId, halfPos);
            cmd.SetComputeTextureParam(cs, _kReflect, HalfNormalRId, halfNormal);
            cmd.SetComputeTextureParam(cs, _kReflect, HalfGeoRId, halfGeo);
            cmd.SetComputeTextureParam(cs, _kReflect, HalfPrevPosId, prevPos);
            cmd.SetComputeTextureParam(cs, _kReflect, ColorHistId, colorHist);
            cmd.SetComputeTextureParam(cs, _kReflect, RefTraceId, refTrace);
            cmd.DispatchCompute(cs, _kReflect, (hw + 7) / 8, (hh + 7) / 8, 1);
        }
    }
}
