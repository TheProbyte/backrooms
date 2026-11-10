using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Backrooms.StyleLab
{
    /// <summary>
    /// The look being tried out in the style lab (2026-11-05): a stylising pass over the camera's finished picture, the
    /// way the co-op horror games we are looking at do it (light the scene for real, then posterise and outline it):
    /// brightness cut into bands (blended with the real picture), outlines from depth, surface angle and contrast,
    /// optional chunky pixels, a dither, saturation, a shadow tint and an overall tint. Runs in edit mode too, so the
    /// sliders can be tuned in the Game view without pressing Play. Only on cameras that carry this component.
    /// </summary>
    [ExecuteAlways, RequireComponent(typeof(Camera))]
    public class StyleLabLook : MonoBehaviour
    {
        [Header("Pixels")]
        [Tooltip("Size of a picture cell in screen pixels: 1 = full resolution, 3-4 = a low-resolution look.")]
        [Range(1, 8)] public int pixelSize = 1;

        [Header("Colour bands")]
        [Tooltip("How many brightness steps the picture is cut into.")]
        [Range(2, 32)] public float bands = 10f;
        [Tooltip("0 = the real picture, 1 = fully banded.")]
        [Range(0f, 1f)] public float bandBlend = 0.55f;
        [Tooltip("Ordered dither across the band edges (0 = hard edges).")]
        [Range(0f, 1f)] public float dither = 0.25f;

        [Header("Outlines")]
        [Range(0f, 1f)] public float outline = 0.75f;
        public Color outlineColor = new Color(0.12f, 0.08f, 0.05f, 1f);
        [Tooltip("Outline thickness in cells.")]
        [Range(0.5f, 3f)] public float outlineWidth = 1f;
        [Tooltip("How readily a jump in depth draws a line.")]
        [Range(0f, 4f)] public float depthSensitivity = 1.4f;
        [Tooltip("How readily a change in surface direction draws a line.")]
        [Range(0f, 4f)] public float normalSensitivity = 1.1f;
        [Tooltip("How readily a change in brightness draws a line.")]
        [Range(0f, 4f)] public float contrastSensitivity = 0.35f;
        [Tooltip("Outlines fade out by this distance (m), so far walls don't turn to scribble.")]
        public float outlineFade = 18f;

        [Header("Colour")]
        [Range(0f, 2f)] public float saturation = 1.1f;
        [Range(0.5f, 1.5f)] public float contrast = 1.05f;
        public Color tint = Color.white;
        [Tooltip("Colour the dark parts lean toward.")]
        public Color shadowTint = new Color(0.55f, 0.45f, 0.62f, 1f);
        [Range(0f, 1f)] public float shadowTintAmount = 0.35f;

        [Tooltip("Debug: 0 = the look, 1 = depth, 2 = normals.")]
        [Range(0, 2)] public int debugView = 0;

        [Header("Where")]
        [Tooltip("Also show the look in the Scene view.")]
        public bool inSceneView = false;

        StyleLabPass _pass;

        void OnEnable()
        {
            var sh = Shader.Find("Hidden/Backrooms/StyleLab");
            if (!sh || !sh.isSupported) { Debug.LogWarning("StyleLabLook: shader Hidden/Backrooms/StyleLab not found"); return; }
            _pass = new StyleLabPass(CoreUtils.CreateEngineMaterial(sh));
            RenderPipelineManager.beginCameraRendering += OnBegin;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            _pass?.Dispose();
            _pass = null;
        }

        void OnBegin(ScriptableRenderContext ctx, Camera cam)
        {
            if (_pass == null) return;
            bool mine = cam == GetComponent<Camera>();
            bool scene = inSceneView && cam.cameraType == CameraType.SceneView;
            if (!mine && !scene) return;
            _pass.Set(this);
            var data = cam.GetUniversalAdditionalCameraData();
            data?.scriptableRenderer?.EnqueuePass(_pass);
        }
    }

    public sealed class StyleLabPass : ScriptableRenderPass
    {
        readonly Material _mat;
        readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();
        Vector4 _p0, _p1, _p2, _p3;
        Color _outline, _tint, _shadow;

        static readonly int IdBlit = Shader.PropertyToID("_BlitTexture"), IdScaleBias = Shader.PropertyToID("_BlitScaleBias");
        static readonly int IdDepth = Shader.PropertyToID("_SL_Depth"), IdNormals = Shader.PropertyToID("_SL_Normals");
        static readonly int IdP0 = Shader.PropertyToID("_SL_P0"), IdP1 = Shader.PropertyToID("_SL_P1"), IdP2 = Shader.PropertyToID("_SL_P2"), IdP3 = Shader.PropertyToID("_SL_P3");
        static readonly int IdRes = Shader.PropertyToID("_SL_Res"), IdOutline = Shader.PropertyToID("_SL_OutlineColor");
        static readonly int IdTint = Shader.PropertyToID("_SL_Tint"), IdShadow = Shader.PropertyToID("_SL_ShadowTint");

        public StyleLabPass(Material m)
        {
            _mat = m;
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            requiresIntermediateTexture = true;
        }

        public void Set(StyleLabLook l)
        {
            _p0 = new Vector4(l.pixelSize, l.bands, l.bandBlend, l.dither);
            _p1 = new Vector4(l.outline, l.depthSensitivity, l.normalSensitivity, l.contrastSensitivity);
            _p2 = new Vector4(l.outlineFade, l.outlineWidth, l.saturation, l.contrast);
            _p3 = new Vector4(l.shadowTintAmount, l.debugView, 0f, 0f);
            _outline = l.outlineColor; _tint = l.tint; _shadow = l.shadowTint;
        }

        public void Dispose() => CoreUtils.Destroy(_mat);

        class PassData
        {
            public Material mat; public MaterialPropertyBlock mpb;
            public TextureHandle src, depth, normals;
        }

        public override void RecordRenderGraph(RenderGraph rg, ContextContainer frameData)
        {
            var res = frameData.Get<UniversalResourceData>();
            if (res.isActiveTargetBackBuffer) return;
            var src = res.activeColorTexture;
            var desc = rg.GetTextureDesc(src);
            desc.name = "_StyleLabOut"; desc.clearBuffer = false; desc.msaaSamples = MSAASamples.None;
            var dst = rg.CreateTexture(desc);
            _mpb.SetVector(IdP0, _p0); _mpb.SetVector(IdP1, _p1); _mpb.SetVector(IdP2, _p2); _mpb.SetVector(IdP3, _p3);
            _mpb.SetVector(IdRes, new Vector4(desc.width, desc.height, 1f / desc.width, 1f / desc.height));
            _mpb.SetColor(IdOutline, _outline); _mpb.SetColor(IdTint, _tint); _mpb.SetColor(IdShadow, _shadow);
            using (var builder = rg.AddRasterRenderPass<PassData>("Style lab look", out var data))
            {
                data.mat = _mat; data.mpb = _mpb; data.src = src;
                data.depth = res.cameraDepthTexture; data.normals = res.cameraNormalsTexture;
                builder.UseTexture(src);
                if (data.depth.IsValid()) builder.UseTexture(data.depth);
                if (data.normals.IsValid()) builder.UseTexture(data.normals);
                builder.SetRenderAttachment(dst, 0, AccessFlags.WriteAll);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((PassData p, RasterGraphContext ctx) =>
                {
                    p.mpb.SetTexture(IdBlit, (RTHandle)p.src);
                    p.mpb.SetTexture(IdDepth, p.depth.IsValid() ? (Texture)(RTHandle)p.depth : Texture2D.blackTexture);
                    p.mpb.SetTexture(IdNormals, p.normals.IsValid() ? (Texture)(RTHandle)p.normals : Texture2D.blackTexture);
                    p.mpb.SetVector(IdScaleBias, new Vector4(1f, 1f, 0f, 0f));
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, p.mat, 0, MeshTopology.Triangles, 3, 1, p.mpb);
                });
            }
            res.cameraColor = dst;
        }
    }
}
