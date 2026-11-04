using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Generation
{
    /// <summary>
    /// Sun shafts in the Poolrooms' air (2026-11-01, the realism plan 1): pool halls are humid, so every beam from a roof
    /// opening shows. A render pass on the player's camera while it is in the Poolrooms (Hidden/Backrooms/PoolAir): the
    /// traced sun marched along each view ray at a quarter of the resolution, smoothed over frames and laid over the
    /// picture upsampled by depth, before the camcorder's own processing. Added by PoolWorld.
    /// </summary>
    public class PoolAir : MonoBehaviour
    {
        [Tooltip("Scattering of the air everywhere (per metre).")]
        public float density = 0.003f;
        [Tooltip("Extra in the humid layer over the floor and water (per metre, fading over 0.7 m).")]
        public float humid = 0.0025f;
        [Tooltip("How far the march reaches (m).")]
        public float reach = 28f;
        [Range(8, 48)] public int steps = 28;
        [Range(0f, 0.97f)] public float history = 0.9f;

        PoolAirPass _pass;
        bool _hooked;

        void OnEnable()
        {
            var sh = Shader.Find("Hidden/Backrooms/PoolAir");
            if (!sh || !sh.isSupported) { enabled = false; return; }
            _pass = new PoolAirPass(new Material(sh) { hideFlags = HideFlags.DontSave });
            RenderPipelineManager.beginCameraRendering += OnBegin;
            _hooked = true;
        }

        void OnDisable()
        {
            if (_hooked) RenderPipelineManager.beginCameraRendering -= OnBegin;
            _hooked = false;
            _pass?.Dispose();
            _pass = null;
        }

        void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (_pass == null || cam != Camera.main || cam.cameraType != CameraType.Game) return;
            var w = PoolWorld.Instance;
            if (!w || !w.IsPool(cam.transform.position) || density <= 0f) return;
            _pass.Set(density, humid, reach, steps, history);
            cam.GetUniversalAdditionalCameraData().scriptableRenderer?.EnqueuePass(_pass);
        }
    }

    public sealed class PoolAirPass : ScriptableRenderPass
    {
        readonly Material _mat;
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        RTHandle _histA, _histB;
        Matrix4x4 _prevVP;
        bool _havePrev;
        int _frame;
        Vector4 _params;
        float _history;

        static readonly int IdDepth = Shader.PropertyToID("_PoolAirDepth"), IdCur = Shader.PropertyToID("_PoolAirCur"), IdHist = Shader.PropertyToID("_PoolAirHist");
        static readonly int IdInvVP = Shader.PropertyToID("_PoolAirInvVP"), IdPrevVP = Shader.PropertyToID("_PoolAirPrevVP");
        static readonly int IdParams = Shader.PropertyToID("_PoolAirParams"), IdFrame = Shader.PropertyToID("_PoolAirFrame"), IdCam = Shader.PropertyToID("_PoolAirCam");
        static readonly int IdBlit = Shader.PropertyToID("_BlitTexture"), IdScaleBias = Shader.PropertyToID("_BlitScaleBias");

        public PoolAirPass(Material m)
        {
            _mat = m;
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        }

        public void Set(float density, float humid, float reach, int steps, float history)
        {
            _params = new Vector4(density, humid, reach, steps);
            _history = history;
        }

        public void Dispose()
        {
            _histA?.Release(); _histB?.Release();
            _histA = _histB = null;
            if (_mat) Object.Destroy(_mat);
        }

        class PassData
        {
            public Material mat; public int pass; public MaterialPropertyBlock mpb;
            public TextureHandle a, b, c; public int ia, ib, ic;
        }

        static void Bind(MaterialPropertyBlock mpb, int id, TextureHandle h)
        {
            if (id == 0) return;
            mpb.SetTexture(id, h.IsValid() ? (Texture)(RTHandle)h : Texture2D.blackTexture);
        }

        void Draw(RenderGraph rg, string name, int pass, TextureHandle dst, AccessFlags access, TextureHandle a, int ia, TextureHandle b = default, int ib = 0, TextureHandle c = default, int ic = 0)
        {
            using (var builder = rg.AddRasterRenderPass<PassData>(name, out var data))
            {
                data.mat = _mat; data.pass = pass; data.mpb = _mpb;
                data.a = a; data.b = b; data.c = c; data.ia = ia; data.ib = ib; data.ic = ic;
                if (a.IsValid()) builder.UseTexture(a);
                if (b.IsValid()) builder.UseTexture(b);
                if (c.IsValid()) builder.UseTexture(c);
                builder.SetRenderAttachment(dst, 0, access);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData p, RasterGraphContext ctx) =>
                {
                    Bind(p.mpb, p.ia, p.a); Bind(p.mpb, p.ib, p.b); Bind(p.mpb, p.ic, p.c);
                    p.mpb.SetVector(IdScaleBias, new Vector4(1f, 1f, 0f, 0f));
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, p.mat, p.pass, MeshTopology.Triangles, 3, 1, p.mpb);
                });
            }
        }

        public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
        {
            var res = frameData.Get<UniversalResourceData>();
            var camData = frameData.Get<UniversalCameraData>();
            if (res.isActiveTargetBackBuffer) return;
            var depth = res.cameraDepthTexture;
            if (!depth.IsValid()) return;
            var cam = camData.camera;
            var desc = camData.cameraTargetDescriptor;
            int W = Mathf.Max(16, desc.width / 4), H = Mathf.Max(16, desc.height / 4);
            var qDesc = new RenderTextureDescriptor(W, H, GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.None) { msaaSamples = 1, useMipMap = false };
            bool fresh = RenderingUtils.ReAllocateHandleIfNeeded(ref _histA, qDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_PoolAirHistA");
            fresh |= RenderingUtils.ReAllocateHandleIfNeeded(ref _histB, qDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_PoolAirHistB");

            var gpuP = GL.GetGPUProjectionMatrix(camData.GetProjectionMatrix(), true);
            var vp = gpuP * camData.GetViewMatrix();
            _frame++;
            _mpb.SetMatrix(IdInvVP, vp.inverse);
            _mpb.SetMatrix(IdPrevVP, _havePrev && !fresh ? _prevVP : vp);
            _mpb.SetVector(IdParams, _params);
            _mpb.SetVector(IdFrame, new Vector4(_frame % 64, _havePrev && !fresh ? _history : 0f, 1f / W, 1f / H));
            _mpb.SetVector(IdCam, cam.transform.position);
            _prevVP = vp; _havePrev = true;

            var cur = rg.CreateTexture(new TextureDesc(W, H) { format = GraphicsFormat.R16G16B16A16_SFloat, name = "_PoolAirCur", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, clearBuffer = false });
            Draw(rg, "Pool air: march", 0, cur, AccessFlags.WriteAll, depth, IdDepth);
            var hist = rg.ImportTexture(_histA);
            var outT = rg.ImportTexture(_histB);
            Draw(rg, "Pool air: temporal", 1, outT, AccessFlags.WriteAll, cur, IdCur, hist, IdHist, depth, IdDepth);
            if (PoolWorld.CameraUnderwater) Draw(rg, "Pool air: murk", 3, res.activeColorTexture, AccessFlags.ReadWrite, depth, IdDepth);
            Draw(rg, "Pool air: composite", 2, res.activeColorTexture, AccessFlags.ReadWrite, outT, IdBlit, depth, IdDepth);
            (_histA, _histB) = (_histB, _histA);
        }
    }
}
