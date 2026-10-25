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
    [DefaultExecutionOrder(10000)]
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
            public float scale;
            public Vector4 clipPlane;
        }
        readonly List<Slot> _slots = new List<Slot>();
        readonly Dictionary<CrawlPortalView, Vector3> _lastLocal = new Dictionary<CrawlPortalView, Vector3>();
        readonly List<(CrawlPortalView v, float d)> _visible = new List<(CrawlPortalView, float)>(16);
        readonly List<CrawlPortalView> _wanted = new List<CrawlPortalView>(MaxViews);
        readonly List<CrawlPortalView> _stale = new List<CrawlPortalView>();
        readonly Plane[] _planes = new Plane[6];
        CamcorderPlayerController _player;
        Camera _main;
        CharacterController _capsule;
        CrawlPortalView _arrival;
        static readonly System.Comparison<(CrawlPortalView v, float d)> ByDistance = (a, b) => a.d.CompareTo(b.d);
        int _rendererIndex = -2;
        readonly Dictionary<Vector3, CrawlPortalView> _ends = new Dictionary<Vector3, CrawlPortalView>();
        int _endRevision = -1;
        const int MaxSpillEnds = 8;
        readonly Vector4[] _spillPosition = new Vector4[MaxSpillEnds], _spillNormal = new Vector4[MaxSpillEnds],
            _spillPartner = new Vector4[MaxSpillEnds], _spillPartnerNormal = new Vector4[MaxSpillEnds];
        readonly List<(CrawlPortalView v, float d)> _spillCandidates = new List<(CrawlPortalView, float)>();
        ComputeShader _lightingCompute;
        RenderTexture _irradiance;
        LightGrid _grid;

        /// <summary>Frame number of the last trip through a hole (the camcorder resets its motion smear on it).</summary>
        public static int LastWarpFrame { get; private set; } = -10;

        public static void Ensure()
        {
            if (_instance) return;
            var go = new GameObject("CrawlPortalSystem");
            _instance = go.AddComponent<CrawlPortalSystem>();
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            foreach (var v in CrawlPortalView.All) if (v) v.SetPassage(null, false);
            Shader.SetGlobalInt("_PortalSpillCount", 0);
            Shader.SetGlobalVector("_PortalClipPlane", Vector4.zero);
            Shader.DisableKeyword("BACKROOMS_PORTAL_CLIP");
            Shader.SetGlobalTexture("_PortalIrradiance", Texture2D.blackTexture);
            if (_irradiance) { _irradiance.Release(); Destroy(_irradiance); _irradiance = null; }
            foreach (var s in _slots) { if (s.cam) Destroy(s.cam.gameObject); if (s.rt) { s.rt.Release(); Destroy(s.rt); } }
            _slots.Clear();
        }
        void OnDestroy() { if (_instance == this) _instance = null; }

        void Update()
        {
            var main = Camera.main;
            if (!main) return;
            if (_main != main || !_player)
            {
                _main = main; _player = main.GetComponentInParent<CamcorderPlayerController>();
                _capsule = _player ? _player.GetComponent<CharacterController>() : null;
            }
            UpdatePassages();
        }

        void UpdatePassages()
        {
            if (!_capsule || !_main) return;
            foreach (var v in CrawlPortalView.All)
            {
                if (!v) continue;
                var local = v.Local(_main.transform.position);
                bool ready = PartnerLoaded(v);
                bool open = ready && v.Fits(_capsule) && local.z > -0.15f && local.z < 1f;
                v.SetPassage(_capsule, open);
            }
        }

        bool PartnerLoaded(CrawlPortalView v)
        {
            if (_endRevision != CrawlPortalView.Revision)
            {
                _ends.Clear();
                foreach (var end in CrawlPortalView.All) if (end) _ends[end.position] = end;
                _endRevision = CrawlPortalView.Revision;
            }
            return _ends.TryGetValue(v.partnerPosition, out var other) && other;
        }

        void UpdateSpill(Camera main)
        {
            if (!_grid) _grid = FindAnyObjectByType<LightGrid>();
            if (!_lightingCompute) _lightingCompute = Resources.Load<ComputeShader>("PortalLighting");
            if (!_grid || !_lightingCompute || !_grid.BindPortalLighting(_lightingCompute, 0))
            { Shader.SetGlobalInt("_PortalSpillCount", 0); return; }
            _spillCandidates.Clear();
            foreach (var v in CrawlPortalView.All)
            {
                if (!v || !PartnerLoaded(v)) continue;
                float d = (main.transform.position - v.position).sqrMagnitude;
                // Include counterpart mouths seen by portal cameras, but cap shader work independently of world size.
                if (d < 48f * 48f) _spillCandidates.Add((v, d));
            }
            _spillCandidates.Sort(ByDistance);
            int count = Mathf.Min(MaxSpillEnds, _spillCandidates.Count);
            for (int i = 0; i < count; i++)
            {
                var v = _spillCandidates[i].v;
                _spillPosition[i] = new Vector4(v.position.x, v.position.y, v.position.z, CrawlPortals.HoleSize);
                _spillNormal[i] = v.normal; _spillPartner[i] = v.partnerPosition; _spillPartnerNormal[i] = v.partnerNormal;
            }
            Shader.SetGlobalVectorArray("_PortalSpillPosition", _spillPosition);
            Shader.SetGlobalVectorArray("_PortalSpillNormal", _spillNormal);
            Shader.SetGlobalVectorArray("_PortalSpillPartner", _spillPartner);
            Shader.SetGlobalVectorArray("_PortalSpillPartnerNormal", _spillPartnerNormal);
            Shader.SetGlobalInt("_PortalSpillCount", count);
            if (count == 0) return;
            if (!_irradiance)
            {
                _irradiance = new RenderTexture(6, MaxSpillEnds, 0, RenderTextureFormat.ARGBHalf)
                { name = "PortalIrradiance", enableRandomWrite = true, filterMode = FilterMode.Point };
                _irradiance.Create();
            }
            _lightingCompute.SetVectorArray("_PortalSpillPosition", _spillPosition);
            _lightingCompute.SetVectorArray("_PortalSpillPartner", _spillPartner);
            _lightingCompute.SetVectorArray("_PortalSpillPartnerNormal", _spillPartnerNormal);
            _lightingCompute.SetInt("_PortalSpillCount", count);
            _lightingCompute.SetTexture(0, "_PortalLightOutput", _irradiance);
            _lightingCompute.Dispatch(0, 1, count, 1);
            Shader.SetGlobalTexture("_PortalIrradiance", _irradiance);
        }

        void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalVector("_PortalClipPlane", Vector4.zero);
            Shader.DisableKeyword("BACKROOMS_PORTAL_CLIP");
            foreach (var slot in _slots)
                if (slot.cam == camera && slot.view)
                {
                    slot.renderedFrames++;
                    slot.view.material.SetFloat("_Fade", 1f);
                }
        }

        void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            foreach (var slot in _slots)
                if (slot.cam == camera && slot.view)
                { Shader.SetGlobalVector("_PortalClipPlane", slot.clipPlane); Shader.EnableKeyword("BACKROOMS_PORTAL_CLIP"); return; }
            Shader.SetGlobalVector("_PortalClipPlane", Vector4.zero);
            Shader.DisableKeyword("BACKROOMS_PORTAL_CLIP");
        }

        void LateUpdate()
        {
            var main = Camera.main;
            if (!main || CrawlPortalView.All.Count == 0) { ReleaseViews(); return; }
            if (_main != main || !_player) { _main = main; _player = main.GetComponentInParent<CamcorderPlayerController>(); }
            TryWarp(main);
            UpdateSpill(main);
            UpdateViews(main);
        }

        // ---------------- Going through ----------------

        void TryWarp(Camera main)
        {
            var player = _player;
            if (!player) return;
            var eye = main.transform.position;
            const float trigger = 0f; // exact shared plane; the portal shader disables near-plane Z clipping
            CrawlPortalView through = null;
            _stale.Clear();
            foreach (var entry in _lastLocal) if (!entry.Key || !CrawlPortalView.All.Contains(entry.Key)) _stale.Add(entry.Key);
            foreach (var v in _stale) _lastLocal.Remove(v);
            foreach (var v in CrawlPortalView.All)
            {
                if (!v || (v.position - eye).sqrMagnitude > 4f) continue;
                var local = v.Local(eye);
                bool returning = false;
                if (v == _arrival)
                {
                    // Reject handheld jitter, but allow an intentional immediate retreat through the mouth.
                    returning = local.z <= trigger && Vector3.Dot(player.PlanarVelocity, v.normal) < -0.02f;
                    if (local.z > 0.08f || returning) _arrival = null;
                    else { _lastLocal[v] = local; continue; }
                }
                bool inHole = Mathf.Abs(local.x) < CrawlPortals.HoleHalf && Mathf.Abs(local.y) < CrawlPortals.HoleHalf;
                bool crossed = _lastLocal.TryGetValue(v, out var prev) && prev.z > trigger && local.z <= trigger && (prev - local).sqrMagnitude < 2.25f;
                if ((returning || crossed) && inHole && v.Fits(_capsule) && PartnerLoaded(v) && local.z > -0.6f) through = v;
                _lastLocal[v] = local;
            }
            if (!through) return;
            var m = through.ToPartner;
            player.Warp(m.MultiplyPoint3x4(player.transform.position), through.YawDelta);
            foreach (var v in CrawlPortalView.All)
                if (v && (v.position - through.partnerPosition).sqrMagnitude < 0.001f) { _arrival = v; break; }
            UpdatePassages();
            LastWarpFrame = Time.frameCount;
            _lastLocal.Clear();
        }

        // ---------------- Views ----------------

        void ReleaseViews()
        {
            foreach (var s in _slots) { if (s.cam) s.cam.enabled = false; if (s.view) s.view.material.SetFloat("_Fade", 0); s.view = null; }
            Shader.SetGlobalInt("_PortalSpillCount", 0);
        }

        void UpdateViews(Camera main)
        {
            var eye = main.transform.position;
            GeometryUtility.CalculateFrustumPlanes(main, _planes);
            var visible = _visible; visible.Clear();
            foreach (var v in CrawlPortalView.All)
            {
                if (!v || !v.quad) continue;
                float d = Vector3.Distance(eye, v.position);
                bool front = Vector3.Dot(eye - v.position, v.normal) > -0.05f;
                bool nearCrossing = d < 1f && Vector3.Dot(main.transform.forward, -v.normal) > 0.05f;
                bool seen = front && d < ViewDistance && (nearCrossing || GeometryUtility.TestPlanesAABB(_planes, v.quad.bounds)) && PartnerLoaded(v) && Unobstructed(eye, v);
                if (seen) visible.Add((v, d));
                else v.material.SetFloat("_Fade", 0f);
            }
            visible.Sort(ByDistance);

            // Keep slots that still show a visible hole (no one-frame black flash), hand the rest out again.
            var want = _wanted; want.Clear();
            for (int i = 0; i < visible.Count && i < MaxViews; i++) want.Add(visible[i].v);
            foreach (var s in _slots) if (s.view && !want.Contains(s.view)) { s.view.material.SetFloat("_Fade", 0f); s.view = null; }
            foreach (var v in want)
            {
                Slot slot = null;
                foreach (var s in _slots) if (s.view == v) { slot = s; break; }
                if (slot == null)
                {
                    foreach (var s in _slots) if (s.view == null) { slot = s; break; }
                    if (slot == null) slot = NewSlot();
                    slot.view = v; slot.renderedFrames = 0;
                }
                Render(slot, main);
            }
            foreach (var v in visible) if (!want.Contains(v.v)) v.v.material.SetFloat("_Fade", 0f);
            foreach (var s in _slots) if (s.view == null && s.cam) s.cam.enabled = false;
        }

        /// <summary>Is any part of the hole's end plane in line of sight (through the opening)? Saves rendering views of holes behind walls.</summary>
        static bool Unobstructed(Vector3 eye, CrawlPortalView v)
        {
            var right = Vector3.Cross(Vector3.up, v.normal);
            float h = CrawlPortals.HoleHalf * 0.8f;
            Vector3 c = v.position + v.normal * 0.05f;
            if ((eye - c).sqrMagnitude < 1.5f) return true;
            for (int i = 0; i < 5; i++)
            {
                var p = i == 0 ? c : c + right * ((i & 1) == 0 ? -h : h) + Vector3.up * (i < 3 ? h : -h);
                if (!Physics.Linecast(eye, p, ~(1 << CrawlPortalView.Layer), QueryTriggerInteraction.Ignore)) return true;
            }
            return false;
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
            cam.useOcclusionCulling = false; // its virtual eye can be inside an unrelated solid wall
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

        public static Matrix4x4 CropProjection(Matrix4x4 projection, Rect viewport)
        {
            var crop = Matrix4x4.identity;
            crop.m00 = 1f / viewport.width; crop.m03 = (1f - 2f * viewport.center.x) / viewport.width;
            crop.m11 = 1f / viewport.height; crop.m13 = (1f - 2f * viewport.center.y) / viewport.height;
            return crop * projection;
        }

        static Rect Viewport(Camera main, CrawlPortalView view)
        {
            var right = Vector3.Cross(Vector3.up, view.normal) * CrawlPortals.HoleHalf;
            var up = Vector3.up * CrawlPortals.HoleHalf;
            Vector2 min = Vector2.one, max = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                var point = main.WorldToViewportPoint(view.position + ((i & 1) == 0 ? -right : right) + (i < 2 ? -up : up));
                if (point.z <= 0.001f) return new Rect(0, 0, 1, 1);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            var pad = new Vector2(2f / Mathf.Max(main.pixelWidth, 1), 2f / Mathf.Max(main.pixelHeight, 1));
            min = Vector2.Max(Vector2.zero, min - pad); max = Vector2.Min(Vector2.one, max + pad);
            return Rect.MinMaxRect(min.x, min.y, Mathf.Max(min.x + 0.001f, max.x), Mathf.Max(min.y + 0.001f, max.y));
        }

        void Render(Slot s, Camera main)
        {
            // Retain screen-space UVs/aspect, but spend pixels in proportion to the opening's angular size.
            // Near crossings reach full resolution; small distant holes cost a fraction of a full camera.
            float distance = Mathf.Max(0.01f, Vector3.Distance(main.transform.position, s.view.position));
            float coverage = CrawlPortals.HoleSize / (2f * distance * Mathf.Tan(main.fieldOfView * Mathf.Deg2Rad * 0.5f));
            float scale = coverage > 0.35f ? 1f : coverage > 0.12f ? 0.5f : 0.25f;
            if (s.scale == 1f && coverage > 0.30f) scale = 1f;
            else if (s.scale == 0.5f && coverage > 0.10f && coverage < 0.40f) scale = 0.5f;
            else if (s.scale == 0.25f && coverage < 0.14f) scale = 0.25f;
            s.scale = scale;
            var viewport = Viewport(main, s.view);
            int w = Mathf.Max(64, Mathf.Min(main.pixelWidth, Mathf.CeilToInt(main.pixelWidth * viewport.width * scale / 32) * 32));
            int h = Mathf.Max(64, Mathf.Min(main.pixelHeight, Mathf.CeilToInt(main.pixelHeight * viewport.height * scale / 32) * 32));
            if (!s.rt || s.rt.width < w || s.rt.height < h || s.rt.width * 0.7f > w || s.rt.height * 0.7f > h)
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
            cam.projectionMatrix = CropProjection(main.projectionMatrix, viewport);
            // Forward+ light clustering assumes an ordinary projection. Clip the destination wall in the
            // world shader instead of shearing the camera near plane (which produces dark triangular light gaps).
            Vector3 n = v.partnerNormal, pos = v.partnerPosition + v.partnerNormal * 0.002f;
            var world = new Vector4(n.x, n.y, n.z, -Vector3.Dot(n, pos));
            cam.enabled = true;
            v.material.SetTexture("_PortalTex", s.rt);
            v.material.SetVector("_PortalRect", new Vector4(viewport.x, viewport.y, viewport.width, viewport.height));
            s.clipPlane = world;
            // Prepared after movement/shake, before URP collects and sorts cameras. Each view renders once
            // through URP's ordinary camera loop; no render request is nested inside another render context.
        }
    }
}
