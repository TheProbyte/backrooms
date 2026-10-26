using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Runs the crawl holes (see <see cref="CrawlPortalView"/>): tracks the player going into and out of each duct, renders
    /// the view through the window at the far end of the nearest visible ducts (a camera at the partner hole matching the
    /// player's camera, clipped to the partner's wall), and moves the player out of the partner hole when their eye crosses
    /// a window. It works late in the frame, after all movement and camera shake, so no frame is seen from the wrong side.
    /// While the player is at or in a duct it also reports where they are about to come out (<see cref="TryGetLookahead"/>)
    /// so the lighting there is already live when they arrive.
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
        static readonly System.Comparison<(CrawlPortalView v, float d)> ByDistance = (a, b) => a.d.CompareTo(b.d);
        int _rendererIndex = -2;
        readonly Dictionary<Vector3, CrawlPortalView> _ends = new Dictionary<Vector3, CrawlPortalView>();
        int _endRevision = -1;

        static bool _haveLookahead;
        static Vector3 _lookahead;

        /// <summary>Frame number of the last trip through a hole (the camcorder resets its motion smear on it).</summary>
        public static int LastWarpFrame { get; private set; } = -10;

        /// <summary>Where the player will come out if they carry on through the duct they are in or at (a point just outside the partner hole).</summary>
        public static bool TryGetLookahead(out Vector3 point) { point = _lookahead; return _haveLookahead && _instance; }

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
            Shader.SetGlobalInt("_PortalSpillCount", 0);   // the ducts carry the light between the rooms now
        }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            foreach (var v in CrawlPortalView.All) if (v) { v.SetPassage(null, false); v.SetInside(false); }
            Shader.SetGlobalVector("_PortalClipPlane", Vector4.zero);
            Shader.DisableKeyword("BACKROOMS_PORTAL_CLIP");
            foreach (var s in _slots) { if (s.cam) Destroy(s.cam.gameObject); if (s.rt) { s.rt.Release(); Destroy(s.rt); } }
            _slots.Clear();
            _haveLookahead = false;
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

        /// <summary>Seal and duct colliders: open to the player only at or in the hole.</summary>
        void UpdatePassages()
        {
            if (!_capsule || !_main) return;
            var eye = _main.transform.position;
            foreach (var v in CrawlPortalView.All)
            {
                if (!v) continue;
                var local = v.Local(eye);
                bool atMouth = v.Fits(_capsule) && local.z > -0.15f && local.z < 1f;
                v.SetPassage(_capsule, v.Inside || atMouth);
                bool near = v.Inside || (local.z > -0.05f && (eye - v.position).sqrMagnitude < 9f);
                v.SetDuctCollision(near, !PartnerLoaded(v));
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

        void OnEndCamera(ScriptableRenderContext context, Camera camera)
        {
            Shader.SetGlobalVector("_PortalClipPlane", Vector4.zero);
            Shader.DisableKeyword("BACKROOMS_PORTAL_CLIP");
            foreach (var slot in _slots)
                if (slot.cam == camera && slot.view) slot.view.material.SetFloat("_Fade", 1f);
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
            _haveLookahead = false;
            if (!main || CrawlPortalView.All.Count == 0) { ReleaseViews(); return; }
            if (_main != main || !_player) { _main = main; _player = main.GetComponentInParent<CamcorderPlayerController>(); }
            TryWarp(main);
            UpdateViews(main);
        }

        // ---------------- Going in, along and out ----------------

        void TryWarp(Camera main)
        {
            var player = _player;
            if (!player) return;
            var eye = main.transform.position;
            float L = CrawlPortals.DuctLength;
            // Closer than this the camera's near plane cuts off the opening's mask and the window, so both hand-overs
            // happen just before the eye reaches the plane (the trip maps exactly, so it lands just as far behind the partner's opening).
            float m = main.nearClipPlane + 0.03f;
            CrawlPortalView through = null;
            _stale.Clear();
            foreach (var entry in _lastLocal) if (!entry.Key || !CrawlPortalView.All.Contains(entry.Key)) _stale.Add(entry.Key);
            foreach (var v in _stale) _lastLocal.Remove(v);
            foreach (var v in CrawlPortalView.All)
            {
                if (!v) continue;
                if (!v.Inside && (v.position - eye).sqrMagnitude > 9f) { _lastLocal.Remove(v); continue; }
                var local = v.Local(eye);
                if (_lastLocal.TryGetValue(v, out var prev) && (prev - local).sqrMagnitude < 2.25f)
                {
                    // In through the opening, or back out of it.
                    if (!v.Inside && prev.z > m && local.z <= m && CrawlPortalView.InAperture(local, 0.05f)) v.SetInside(true);
                    else if (v.Inside && local.z > m + 0.1f) v.SetInside(false);
                    // Out through the window at the far end.
                    if (v.Inside && prev.z > -L + m && local.z <= -L + m && CrawlPortalView.InAperture(local, 0.05f) && PartnerLoaded(v)) through = v;
                }
                // Moved away some other way (a teleport, a menu jump): no longer in the duct.
                if (v.Inside && through != v && (local.z < -L - 0.8f || !CrawlPortalView.InAperture(local, 0.6f))) v.SetInside(false);
                _lastLocal[v] = local;
                if (v.Inside) { _lookahead = v.partnerPosition + v.partnerNormal * 1.2f; _haveLookahead = true; }
                else if (!_haveLookahead && local.z > 0f && local.z < 3.5f && CrawlPortalView.InAperture(local, 1.5f))
                { _lookahead = v.partnerPosition + v.partnerNormal * 1.2f; _haveLookahead = true; }
            }
            if (!through) return;
            player.Warp(through.ToPartner.MultiplyPoint3x4(player.transform.position), through.YawDelta);
            through.SetInside(false);
            _lastLocal.Clear();
            // The eye lands just behind the partner's opening: the player is in that hole's mouth, crawling out.
            var landed = main.transform.position;
            foreach (var v in CrawlPortalView.All)
                if (v && (v.position - through.partnerPosition).sqrMagnitude < 0.001f) { v.SetInside(true); _lastLocal[v] = v.Local(landed); break; }
            LastWarpFrame = Time.frameCount;
            UpdatePassages();
            _lookahead = through.partnerPosition + through.partnerNormal * 1.2f; _haveLookahead = true;
        }

        // ---------------- Views through the far ends ----------------

        void ReleaseViews()
        {
            foreach (var s in _slots) { if (s.cam) s.cam.enabled = false; if (s.view) s.view.material.SetFloat("_Fade", 0); s.view = null; }
        }

        void UpdateViews(Camera main)
        {
            var eye = main.transform.position;
            GeometryUtility.CalculateFrustumPlanes(main, _planes);
            var visible = _visible; visible.Clear();
            float L = CrawlPortals.DuctLength;
            foreach (var v in CrawlPortalView.All)
            {
                if (!v || !v.quad) continue;
                var local = v.Local(eye);
                float d = Vector3.Distance(eye, v.position);
                // The window is only ever seen along the duct: from in front of the opening, or from inside.
                bool front = v.Inside || local.z > 0f;
                bool nearWindow = Mathf.Abs(local.z + L) < 1f && Vector3.Dot(main.transform.forward, -v.normal) > 0.05f;
                bool seen = front && d < ViewDistance && PartnerLoaded(v) &&
                            (nearWindow || GeometryUtility.TestPlanesAABB(_planes, v.quad.bounds)) && (v.Inside || HoleVisible(eye, v));
                if (seen) visible.Add((v, v.Inside ? -1f : d));
                else v.material.SetFloat("_Fade", 0f);
            }
            visible.Sort(ByDistance);

            // Keep slots that still show a wanted view (no reallocation flicker), hand the rest out again.
            var want = _wanted; want.Clear();
            for (int i = 0; i < visible.Count && i < MaxViews; i++) want.Add(visible[i].v);
            foreach (var s in _slots) if (s.view && !want.Contains(s.view)) { s.view.material.SetFloat("_Fade", 0f); s.view = null; }
            for (int i = 0; i < want.Count; i++)
            {
                var v = want[i];
                Slot slot = null;
                foreach (var s in _slots) if (s.view == v) { slot = s; break; }
                if (slot == null)
                {
                    foreach (var s in _slots) if (s.view == null) { slot = s; break; }
                    if (slot == null) slot = NewSlot();
                    slot.view = v;
                }
                Render(slot, main, i == 0 && (v.Inside || Vector3.Distance(eye, v.position) < 2.5f));
            }
            foreach (var v in visible) if (!want.Contains(v.v)) v.v.material.SetFloat("_Fade", 0f);
            foreach (var s in _slots) if (s.view == null && s.cam) s.cam.enabled = false;
        }

        /// <summary>Is the opening in line of sight (some part of it)? Saves rendering views of holes behind walls or furniture.</summary>
        static bool HoleVisible(Vector3 eye, CrawlPortalView v)
        {
            var right = Vector3.Cross(Vector3.up, v.normal);
            float h = CrawlPortals.HoleHalf * 0.85f;
            Vector3 c = v.position + v.normal * 0.03f;
            if ((eye - c).sqrMagnitude < 6.25f) return true;
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
            cam.useOcclusionCulling = false; // its virtual eye is behind the partner's wall
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;   // the player's camera grades the whole picture, window included
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
            var centre = view.ExitPosition;
            Vector2 min = Vector2.one, max = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                var point = main.WorldToViewportPoint(centre + ((i & 1) == 0 ? -right : right) + (i < 2 ? -up : up));
                if (point.z <= 0.001f) return new Rect(0, 0, 1, 1);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            var pad = new Vector2(2f / Mathf.Max(main.pixelWidth, 1), 2f / Mathf.Max(main.pixelHeight, 1));
            min = Vector2.Max(Vector2.zero, min - pad); max = Vector2.Min(Vector2.one, max + pad);
            return Rect.MinMaxRect(min.x, min.y, Mathf.Max(min.x + 0.001f, max.x), Mathf.Max(min.y + 0.001f, max.y));
        }

        void Render(Slot s, Camera main, bool shadows)
        {
            // Retain screen-space UVs/aspect, but spend pixels in proportion to the window's angular size.
            float distance = Mathf.Max(0.01f, Vector3.Distance(main.transform.position, s.view.ExitPosition));
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
            // Each camera re-renders the shadow maps of the lights it sees: only worth it for the window you are at.
            var data = cam.GetUniversalAdditionalCameraData();
            if (data.renderShadows != shadows) data.renderShadows = shadows;
            // Forward+ light clustering assumes an ordinary projection. Clip the partner's wall (and everything behind it)
            // in the shaders instead of shearing the near plane (which produces dark triangular light gaps).
            Vector3 n = v.partnerNormal, pos = v.partnerPosition + v.partnerNormal * 0.002f;
            s.clipPlane = new Vector4(n.x, n.y, n.z, -Vector3.Dot(n, pos));
            cam.enabled = true;
            v.material.SetTexture("_PortalTex", s.rt);
            v.material.SetVector("_PortalRect", new Vector4(viewport.x, viewport.y, viewport.width, viewport.height));
        }
    }
}
