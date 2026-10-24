using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Runs the crawl holes: renders the view through the nearest visible holes (a camera placed at the other end, behind its
    /// wall, matching the player's camera and clipped to the far wall's plane), and moves the player through when their eye
    /// crosses a hole. It works just before the frame renders, after all movement and camera shake, so the player never
    /// sees a frame from the wrong side.
    /// </summary>
    public class CrawlPortalSystem : MonoBehaviour
    {
        static CrawlPortalSystem _instance;
        public const int MaxViews = 2;
        public const float ViewDistance = 16f;

        sealed class Slot
        {
            public Camera cam;
            public RenderTexture rt;
            public CrawlPortalView view;
            public int renderedFrames;
        }
        readonly List<Slot> _slots = new List<Slot>();
        readonly Dictionary<CrawlPortalView, Vector3> _lastLocal = new Dictionary<CrawlPortalView, Vector3>();
        int _rendererIndex = -2;

        /// <summary>Frame number of the last trip through a hole (the camcorder resets its motion smear on it).</summary>
        public static int LastWarpFrame { get; private set; } = -10;

        public static void Ensure()
        {
            if (_instance) return;
            var go = new GameObject("CrawlPortalSystem");
            _instance = go.AddComponent<CrawlPortalSystem>();
        }

        void OnEnable() { RenderPipelineManager.beginContextRendering += OnBeginContext; }
        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= OnBeginContext;
            foreach (var s in _slots) { if (s.cam) Destroy(s.cam.gameObject); if (s.rt) { s.rt.Release(); Destroy(s.rt); } }
            _slots.Clear();
        }
        void OnDestroy() { if (_instance == this) _instance = null; }

        void OnBeginContext(ScriptableRenderContext ctx, List<Camera> cameras)
        {
            var main = Camera.main;
            if (!main || CrawlPortalView.All.Count == 0) { ReleaseViews(); return; }
            TryWarp(main);
            UpdateViews(main);
        }

        // ---------------- Going through ----------------

        void TryWarp(Camera main)
        {
            var player = FindFirstObjectByType<CamcorderPlayerController>();
            if (!player) return;
            var eye = main.transform.position;
            float trigger = main.nearClipPlane + 0.03f;          // before the near plane can cut into the opening
            CrawlPortalView through = null;
            foreach (var v in CrawlPortalView.All)
            {
                if (!v || (v.position - eye).sqrMagnitude > 4f) continue;
                var local = v.Local(eye);
                bool inHole = Mathf.Abs(local.x) < CrawlPortals.HoleHalf && Mathf.Abs(local.y) < CrawlPortals.HoleHalf;
                if (_lastLocal.TryGetValue(v, out var prev) && inHole && prev.z > trigger && local.z <= trigger && local.z > -0.6f) through = v;
                _lastLocal[v] = local;
            }
            if (!through) return;
            var m = through.ToPartner;
            player.Warp(m.MultiplyPoint3x4(player.transform.position), through.YawDelta);
            LastWarpFrame = Time.frameCount;
            _lastLocal.Clear();
        }

        // ---------------- Views ----------------

        void ReleaseViews()
        {
            foreach (var s in _slots) { if (s.cam) s.cam.enabled = false; s.view = null; }
        }

        void UpdateViews(Camera main)
        {
            var eye = main.transform.position;
            var planes = GeometryUtility.CalculateFrustumPlanes(main);
            var visible = new List<(CrawlPortalView v, float d)>();
            foreach (var v in CrawlPortalView.All)
            {
                if (!v || !v.quad) continue;
                float d = Vector3.Distance(eye, v.position);
                bool front = Vector3.Dot(eye - v.position, v.normal) > -0.05f;
                bool seen = front && d < ViewDistance && GeometryUtility.TestPlanesAABB(planes, v.quad.bounds);
                if (seen) visible.Add((v, d));
                else v.material.SetFloat("_Fade", 0f);
            }
            visible.Sort((a, b) => a.d.CompareTo(b.d));

            // Keep slots that still show a visible hole (no one-frame black flash), hand the rest out again.
            var want = new List<CrawlPortalView>();
            for (int i = 0; i < visible.Count && i < MaxViews; i++) want.Add(visible[i].v);
            foreach (var s in _slots) if (s.view && !want.Contains(s.view)) { s.view.material.SetFloat("_Fade", 0f); s.view = null; }
            foreach (var v in want)
            {
                var slot = _slots.Find(s => s.view == v);
                if (slot == null)
                {
                    slot = _slots.Find(s => s.view == null) ?? NewSlot();
                    slot.view = v; slot.renderedFrames = 0;
                }
                Render(slot, main);
            }
            foreach (var v in visible) if (!want.Contains(v.v)) v.v.material.SetFloat("_Fade", 0f);
            foreach (var s in _slots) if (s.view == null && s.cam) s.cam.enabled = false;
        }

        Slot NewSlot()
        {
            var go = new GameObject("CrawlPortalCamera");
            go.transform.SetParent(transform, false);
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.depth = -20;                     // before the player's camera
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;   // the player's camera grades the whole picture, portal included
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            int ri = RendererIndex();
            if (ri >= 0) data.SetRenderer(ri);
            var s = new Slot { cam = cam };
            _slots.Add(s);
            return s;
        }

        /// <summary>A renderer without the camcorder pass (so the picture is only processed once, by the player's camera).</summary>
        int RendererIndex()
        {
            if (_rendererIndex != -2) return _rendererIndex;
            _rendererIndex = -1;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (urp && f?.GetValue(urp) is ScriptableRendererData[] list)
                for (int i = 0; i < list.Length; i++) if (list[i] && list[i].name == "Portal_Renderer") _rendererIndex = i;
            if (_rendererIndex < 0) Debug.LogWarning("[Backrooms] Portal_Renderer missing (Backrooms > Set Up Crawl Hole Renderer): crawl holes show the camcorder effect twice.");
            return _rendererIndex;
        }

        void Render(Slot s, Camera main)
        {
            int w = Mathf.Max(64, main.pixelWidth), h = Mathf.Max(64, main.pixelHeight);
            if (!s.rt || s.rt.width != w || s.rt.height != h)
            {
                if (s.rt) { s.rt.Release(); Destroy(s.rt); }
                s.rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "CrawlPortalView" };
            }
            var v = s.view;
            var cam = s.cam;
            var m = v.ToPartner;
            var p = main.transform;
            cam.transform.SetPositionAndRotation(m.MultiplyPoint3x4(p.position), m.rotation * p.rotation);
            cam.fieldOfView = main.fieldOfView; cam.nearClipPlane = main.nearClipPlane; cam.farClipPlane = main.farClipPlane;
            cam.aspect = main.aspect;
            cam.cullingMask = main.cullingMask & ~(1 << CrawlPortalView.Layer);
            cam.targetTexture = s.rt;
            cam.ResetProjectionMatrix();
            // Clip everything behind the far end's wall (its duct and the wall itself) with an oblique near plane.
            Vector3 n = v.partnerNormal, pos = v.partnerPosition + v.partnerNormal * -0.01f;
            var world = new Vector4(n.x, n.y, n.z, -Vector3.Dot(n, pos));
            var camSpace = Matrix4x4.Transpose(cam.cameraToWorldMatrix) * world;
            cam.projectionMatrix = cam.CalculateObliqueMatrix(camSpace);
            cam.enabled = true;
            v.material.SetTexture("_PortalTex", s.rt);
            // The texture holds last frame's view until this camera has rendered once.
            v.material.SetFloat("_Fade", s.renderedFrames > 0 ? 1f : 0f);
            s.renderedFrames++;
        }
    }
}
