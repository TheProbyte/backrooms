using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// The camcorder's picture chain as render passes (Shaders/CamcorderPipeline.shader), after URP's post-processing (which
    /// is the lens: glare, distortion, colour fringes, vignette, depth of field). The lens image is exposed onto a recording
    /// raster (640 x 480 for 480 lines), run through the camera head, the recording format and playback, then scaled to the
    /// screen. Everything but the last pass runs at the raster's size, so the cost does not grow with the screen.
    /// On frames that are not recorded (recording frame rate, slow shutter, dropped frames) only the last pass runs, on the
    /// held recording. <see cref="CamcorderRig"/> owns it and sets its values each frame.
    /// </summary>
    public sealed class CamcorderPipeline : ScriptableRenderPass
    {
        const int PIntegrate = 0, PMeter = 1, PSmear = 2, PHead = 3, PEncode = 4, PDisplay = 5;
        public const int MeterWidth = 80, MeterHeight = 60;

        /// <summary>Set to show the plain URP image (debug captures that must not have camcorder noise in them).</summary>
        public static bool Suspended;

        readonly Material _mat;
        readonly MaterialPropertyBlock[] _mpb = new MaterialPropertyBlock[6];
        RTHandle _meter, _headA, _headB, _rec;
        bool _flip, _recValid;
        int _poseFrame = -1;
        Vector3 _prevPos, _angVel, _linVel;
        Quaternion _prevRot;
        bool _havePose;

        // Set by the rig every frame.
        public int lines = 480;
        /// <summary>False on frames that show the held recording.</summary>
        public bool record = true;
        /// <summary>Only meter the image (the camcorder picture is off): the URP image is shown as it is.</summary>
        public bool meterOnly;
        public float exposure = 1f, shutterTime = 1f / 60f, motionBlur = 1f, softness = 0.6f;
        public int frameIndex;
        public bool noiseReduction;
        public bool osd;
        public float osdCurvature;
        public Rect viewRect = new Rect(0, 0, 1, 1);

        /// <summary>The meter (80 x 60, exposed linear light) and the exposure it was rendered with.</summary>
        public RenderTexture Meter => _meter != null ? _meter.rt : null;
        public float MeterExposure { get; private set; } = 1f;
        public int MeterFrame { get; private set; } = -1;
        public Material Material => _mat;
        public Vector2Int RasterSize { get; private set; }

        static readonly int IdBlitTexture = Shader.PropertyToID("_BlitTexture"), IdScaleBias = Shader.PropertyToID("_BlitScaleBias");
        static readonly int IdRaster = Shader.PropertyToID("_CamRaster"), IdFrame = Shader.PropertyToID("_CamFrame"), IdBlur = Shader.PropertyToID("_CamBlur");
        static readonly int IdInvVP = Shader.PropertyToID("_CamInvVPNow"), IdVPOpen = Shader.PropertyToID("_CamVPOpen"), IdPos = Shader.PropertyToID("_CamPosition");
        static readonly int IdDepth = Shader.PropertyToID("_CamDepth"), IdRasterTex = Shader.PropertyToID("_CamRasterTex"), IdSmearTex = Shader.PropertyToID("_CamSmearTex");
        static readonly int IdHeadTex = Shader.PropertyToID("_CamHeadTex"), IdHeadPrev = Shader.PropertyToID("_CamHeadPrev"), IdRecTex = Shader.PropertyToID("_CamRecTex");
        static readonly int IdLowLight = Shader.PropertyToID("_CamLowLight"), IdDisplay = Shader.PropertyToID("_CamDisplay"), IdOsd = Shader.PropertyToID("_CamOSD");
        static readonly int IdViewRect = Shader.PropertyToID("_CamViewRect");

        public CamcorderPipeline(Shader shader)
        {
            _mat = CoreUtils.CreateEngineMaterial(shader);
            for (int i = 0; i < _mpb.Length; i++) _mpb[i] = new MaterialPropertyBlock();
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Depth);
            requiresIntermediateTexture = true;
        }

        public void Dispose()
        {
            _meter?.Release(); _headA?.Release(); _headB?.Release(); _rec?.Release();
            _meter = _headA = _headB = _rec = null;
            CoreUtils.Destroy(_mat);
        }

        class PassData
        {
            public Material mat;
            public int pass;
            public MaterialPropertyBlock mpb;
            public TextureHandle a, b, c, d;
            public int ia, ib, ic, id;
        }

        static void Bind(MaterialPropertyBlock mpb, int id, TextureHandle h)
        {
            if (id == 0) return;
            mpb.SetTexture(id, h.IsValid() ? (Texture)(RTHandle)h : Texture2D.blackTexture);
        }

        void Draw(RenderGraph rg, string name, int pass, TextureHandle dst, TextureHandle a, int ia, TextureHandle b = default, int ib = 0,
                  TextureHandle c = default, int ic = 0, TextureHandle d = default, int id = 0)
        {
            using (var builder = rg.AddRasterRenderPass<PassData>(name, out var data))
            {
                data.mat = _mat; data.pass = pass; data.mpb = _mpb[pass];
                data.a = a; data.b = b; data.c = c; data.d = d; data.ia = ia; data.ib = ib; data.ic = ic; data.id = id;
                if (a.IsValid()) builder.UseTexture(a);
                if (b.IsValid()) builder.UseTexture(b);
                if (c.IsValid()) builder.UseTexture(c);
                if (d.IsValid()) builder.UseTexture(d);
                builder.SetRenderAttachment(dst, 0, AccessFlags.WriteAll);
                builder.SetRenderFunc((PassData p, RasterGraphContext ctx) =>
                {
                    Bind(p.mpb, p.ia, p.a); Bind(p.mpb, p.ib, p.b); Bind(p.mpb, p.ic, p.c); Bind(p.mpb, p.id, p.d);
                    p.mpb.SetVector(IdScaleBias, new Vector4(1f, 1f, 0f, 0f));
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, p.mat, p.pass, MeshTopology.Triangles, 3, 1, p.mpb);
                });
            }
        }

        static RenderTextureDescriptor Desc(int w, int h) =>
            new RenderTextureDescriptor(w, h, GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.None) { msaaSamples = 1, useMipMap = false };

        /// <summary>
        /// The camera's motion over the shutter time: the view at the moment the shutter opened, relative to now (view space),
        /// from the camera's turn and travel over the last frames. Tracked once per frame for the screen camera.
        /// </summary>
        Matrix4x4 OpenFromNow(Camera cam)
        {
            var t = cam.transform;
            Vector3 pos = t.position; Quaternion rot = t.rotation;
            if (_poseFrame != Time.frameCount)
            {
                float dt = Time.deltaTime;
                bool warped = Interaction.CrawlPortalSystem.LastWarpFrame >= Time.frameCount - 1;
                if (_havePose && dt > 1e-4f && !warped)
                {
                    var d = rot * Quaternion.Inverse(_prevRot);
                    d.ToAngleAxis(out float ang, out Vector3 axis);
                    if (ang > 180f) ang -= 360f;
                    Vector3 w = float.IsNaN(axis.x) ? Vector3.zero : axis * (ang / dt);
                    Vector3 v = (pos - _prevPos) / dt;
                    if (v.magnitude > 25f || w.magnitude > 3000f) { _angVel = Vector3.zero; _linVel = Vector3.zero; }   // a teleport, not motion
                    else
                    {
                        float k = 1f - Mathf.Exp(-dt / 0.025f);
                        _angVel = Vector3.Lerp(_angVel, w, k); _linVel = Vector3.Lerp(_linVel, v, k);
                    }
                }
                else { _angVel = Vector3.zero; _linVel = Vector3.zero; }
                _prevPos = pos; _prevRot = rot; _havePose = true; _poseFrame = Time.frameCount;
            }
            float T = shutterTime;
            float spin = _angVel.magnitude;
            var rotOpen = spin > 1e-3f ? Quaternion.AngleAxis(-spin * T, _angVel / spin) * rot : rot;
            var posOpen = pos - _linVel * T;
            var flip = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
            return flip * Matrix4x4.TRS(posOpen, rotOpen, Vector3.one).inverse * Matrix4x4.TRS(pos, rot, Vector3.one) * flip;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var res = frameData.Get<UniversalResourceData>();
            var camData = frameData.Get<UniversalCameraData>();
            if (res.isActiveTargetBackBuffer) return;
            var cam = camData.camera;
            bool capture = cam.targetTexture != null;
            TextureHandle src = res.activeColorTexture;
            TextureHandle depth = res.cameraDepthTexture;

            float aspect = Mathf.Max(0.25f, (float)camData.cameraTargetDescriptor.width / Mathf.Max(1, camData.cameraTargetDescriptor.height));
            int H = Mathf.Clamp(lines, 120, 1440), W = Mathf.Clamp(Mathf.RoundToInt(H * aspect), 64, 4096);
            RasterSize = new Vector2Int(W, H);
            var rasterVec = new Vector4(W, H, 1f / W, 1f / H);

            if (RenderingUtils.ReAllocateHandleIfNeeded(ref _meter, Desc(MeterWidth, MeterHeight), FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CamMeter")) MeterFrame = -1;
            bool fresh = false;
            fresh |= RenderingUtils.ReAllocateHandleIfNeeded(ref _headA, Desc(W, H), FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CamHeadA");
            fresh |= RenderingUtils.ReAllocateHandleIfNeeded(ref _headB, Desc(W, H), FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CamHeadB");
            fresh |= RenderingUtils.ReAllocateHandleIfNeeded(ref _rec, Desc(W, H), FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CamRecorded");
            if (fresh) _recValid = false;

            bool doRecord = capture || record || !_recValid || meterOnly;
            TextureHandle recTex;
            if (doRecord)
            {
                // Integrate: exposure, shutter motion blur, pixel footprint.
                var rasterDesc = new TextureDesc(W, H) { format = GraphicsFormat.R16G16B16A16_SFloat, name = "_CamRaster", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, clearBuffer = false };
                var raster = renderGraph.CreateTexture(rasterDesc);
                var gpuP = GL.GetGPUProjectionMatrix(camData.GetProjectionMatrix(), true);
                var openFromNow = capture ? Matrix4x4.identity : OpenFromNow(cam);
                bool blur = !capture && !meterOnly && motionBlur > 0.001f && depth.IsValid();
                var m = _mpb[PIntegrate];
                m.SetVector(IdRaster, rasterVec);
                m.SetVector(IdFrame, new Vector4(frameIndex, meterOnly ? 1f : exposure, blur ? motionBlur : 0f, meterOnly ? 0f : softness));
                m.SetVector(IdBlur, new Vector4(0.6f, 0.15f, 0f, 0f));
                m.SetMatrix(IdInvVP, gpuP.inverse);
                m.SetMatrix(IdVPOpen, gpuP * openFromNow);
                m.SetVector(IdPos, Vector4.zero);
                Draw(renderGraph, "Camcorder Integrate", PIntegrate, raster, src, IdBlitTexture, blur ? depth : default, IdDepth);

                if (!capture)
                {
                    _mpb[PMeter].SetVector(IdRaster, rasterVec);
                    Draw(renderGraph, "Camcorder Meter", PMeter, renderGraph.ImportTexture(_meter), raster, IdRasterTex);
                    MeterExposure = exposure;
                    MeterFrame = Time.frameCount;
                }
                if (meterOnly) { _recValid = false; return; }

                // Smear: the mean of each column.
                var smearDesc = new TextureDesc(W, 1) { format = GraphicsFormat.R16G16B16A16_SFloat, name = "_CamSmear", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, clearBuffer = false };
                var smear = renderGraph.CreateTexture(smearDesc);
                _mpb[PSmear].SetVector(IdRaster, rasterVec);
                Draw(renderGraph, "Camcorder Smear", PSmear, smear, raster, IdRasterTex);

                // Camera head: noise, white balance, gamma, knee, Y'CbCr; temporal noise reduction against the last frame.
                TextureHandle head, headPrev;
                if (capture)
                {
                    rasterDesc.name = "_CamHeadCapture";
                    head = renderGraph.CreateTexture(rasterDesc);
                    headPrev = default;
                }
                else
                {
                    _flip = !_flip;
                    head = renderGraph.ImportTexture(_flip ? _headA : _headB);
                    headPrev = renderGraph.ImportTexture(_flip ? _headB : _headA);
                }
                bool nr = noiseReduction && !capture && _recValid;
                var hm = _mpb[PHead];
                hm.SetVector(IdRaster, rasterVec);
                hm.SetVector(IdFrame, new Vector4(frameIndex, exposure, 0f, softness));
                var low = _mat.GetVector(IdLowLight);
                hm.SetVector(IdLowLight, nr ? low : new Vector4(low.x, 0f, 0f, 0f));
                Draw(renderGraph, "Camcorder Head", PHead, head, raster, IdRasterTex, smear, IdSmearTex, nr ? headPrev : default, IdHeadPrev);

                // Encode: format bandwidths, edge enhancement, tape and playback.
                if (capture)
                {
                    rasterDesc.name = "_CamRecordedCapture";
                    recTex = renderGraph.CreateTexture(rasterDesc);
                }
                else recTex = renderGraph.ImportTexture(_rec);
                var em = _mpb[PEncode];
                em.SetVector(IdRaster, rasterVec);
                em.SetVector(IdFrame, new Vector4(frameIndex, exposure, 0f, softness));
                Draw(renderGraph, "Camcorder Encode", PEncode, recTex, head, IdHeadTex);
                if (!capture) _recValid = true;
            }
            else recTex = renderGraph.ImportTexture(_rec);

            // Display: the recording scaled to the screen, the viewfinder overlay.
            var outDesc = renderGraph.GetTextureDesc(src);
            outDesc.name = "_CamcorderOut"; outDesc.clearBuffer = false; outDesc.msaaSamples = MSAASamples.None;
            var output = renderGraph.CreateTexture(outDesc);
            var dm = _mpb[PDisplay];
            dm.SetVector(IdRaster, rasterVec);
            var disp = _mat.GetVector(IdDisplay);
            dm.SetVector(IdDisplay, disp);
            dm.SetVector(IdOsd, new Vector4(osd && !capture ? 1f : 0f, osdCurvature, 0f, 0f));
            var vr = capture ? new Rect(0, 0, 1, 1) : viewRect;
            dm.SetVector(IdViewRect, new Vector4(vr.x, vr.y, vr.width, vr.height));
            Draw(renderGraph, "Camcorder Display", PDisplay, output, recTex, IdRecTex);
            res.cameraColor = output;
        }
    }
}
