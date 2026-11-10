using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Generation
{
    /// <summary>
    /// Contact shadows (F5 > LIGHT > Contact shadows): a render pass of its own on the player's camera, after the depth
    /// prepass and before the opaque pass, that marches toward the two strongest fixtures at each pixel through the camera
    /// depth and writes the result into a screen-sized mask (Resources/Shaders/ContactShadowMask.shader). Backrooms/Lit looks
    /// the mask up for those lights (BackroomsContact). The pass declares the depth texture it reads, as the render graph
    /// requires; the mask is a texture of its own (not a render-graph one), so the lit shader can read it like the light grid.
    /// Other cameras (portal views, the exposure probe, reflection captures, the scene view) get no contact shadows.
    /// </summary>
    public class ContactShadows : MonoBehaviour
    {
        public static ContactShadows Instance { get; private set; }
        [Range(0f, 1f)] public float strength = 0.6f;

        /// <summary>Frames the mask was drawn (for tests).</summary>
        public int Frames { get; private set; }

        Material _material;
        RTHandle _mask;
        MaskPass _pass;
        static readonly int ContactId = Shader.PropertyToID("_BRContact"), MaskId = Shader.PropertyToID("_BRContactMask"),
            ParamsId = Shader.PropertyToID("_BRContactParams");

        public static ContactShadows Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("ContactShadows");
            DontDestroyOnLoad(go);
            return go.AddComponent<ContactShadows>();
        }

        void OnEnable()
        {
            Instance = this;
            var shader = Shader.Find("Hidden/Backrooms/ContactShadowMask");
            if (shader) _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _pass = new MaskPass(this);
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            Shader.SetGlobalVector(ContactId, Vector4.zero);
            _mask?.Release(); _mask = null;
            if (_material) { if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material); }
            if (Instance == this) Instance = null;
        }

        void BeginCamera(ScriptableRenderContext context, Camera cam)
        {
            // Off unless this camera draws its own mask below (the flag is per camera, read by the lit shader).
            Shader.SetGlobalVector(ContactId, Vector4.zero);
            if (strength <= 0.001f || !_material || cam.cameraType != CameraType.Game || cam != Camera.main) return;
            var data = cam.GetUniversalAdditionalCameraData();
            var renderer = data ? data.scriptableRenderer : null;
            if (renderer == null) return;
            renderer.EnqueuePass(_pass);
        }

        /// <summary>The mask at the camera target's size (made again when that changes).</summary>
        RTHandle Mask(int width, int height)
        {
            if (_mask != null && _mask.rt && _mask.rt.width == width && _mask.rt.height == height) return _mask;
            _mask?.Release();
            _mask = RTHandles.Alloc(width, height, colorFormat: GraphicsFormat.R16G16B16A16_SFloat, filterMode: FilterMode.Point,
                wrapMode: TextureWrapMode.Clamp, name: "BRContactMask");
            Shader.SetGlobalTexture(MaskId, _mask.rt);
            return _mask;
        }

        class MaskPass : ScriptableRenderPass
        {
            readonly ContactShadows _owner;
            class PassData { public TextureHandle depth; public Material material; }

            public MaskPass(ContactShadows owner)
            {
                _owner = owner;
                renderPassEvent = RenderPassEvent.AfterRenderingPrePasses + 1;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>();
                if (!_owner || !_owner._material || !resources.cameraDepthTexture.IsValid()) return;
                var desc = camera.cameraTargetDescriptor;
                var mask = _owner.Mask(desc.width, desc.height);
                var target = renderGraph.ImportTexture(mask);
                _owner._material.SetVector(ParamsId, new Vector4(_owner.strength, 1f / desc.width, 1f / desc.height, 0f));
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Backrooms contact shadows", out var data))
                {
                    data.depth = resources.cameraDepthTexture;
                    data.material = _owner._material;
                    builder.UseTexture(data.depth, AccessFlags.Read);
                    builder.SetRenderAttachment(target, 0, AccessFlags.WriteAll);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, d.depth, new Vector4(1f, 1f, 0f, 0f), d.material, 0));
                }
                // The lit shader reads the mask in this camera's opaque pass.
                Shader.SetGlobalVector(ContactId, new Vector4(1f, desc.width, desc.height, 0f));
                _owner.Frames++;
            }
        }
    }
}
