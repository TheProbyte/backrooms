using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Backrooms.CustomRooms
{
    public enum BuilderTool { Select, Wall, Opening, Half, Erase, Floor, Pillar, Light, Prop, Loot }
    public enum BuilderView { Solid, Room, Cutout, WallsDown, Top }
    public enum OpeningKind { Doorway, Arch, Tall, Gap }

    /// <summary>
    /// Build mode: a dark, effect-free workspace for designing a room on the world's 1.2 m cell grid. The room is built with the
    /// world's own layout and chunk builder (so what you see is what the world will generate), edited with a Sims-like set
    /// of tools, and saved as a blueprint the procedural world can place (see <see cref="RoomLibrary"/>, <see cref="CustomRoomStamp"/>).
    /// This part: lifecycle, camera, the live preview and the overlay.
    /// </summary>
    public partial class BuilderApp : MonoBehaviour
    {
        public static BuilderApp Current { get; private set; }

        [Tooltip("The office biome's materials and prefabs (the same asset the world streamer uses).")]
        public OfficeBiomeAssets assets;

        const float C = OfficeBiome.CellSize;

        enum ScreenKind { Library, Editor }
        ScreenKind _screen = ScreenKind.Library;

        RoomBlueprint _bp;
        int _vi;
        RoomVariant V => _bp.variants[Mathf.Clamp(_vi, 0, _bp.variants.Count - 1)];
        bool _dirty;
        string _status = "";
        float _statusUntil;

        Camera _cam;
        Vector3 _target = new Vector3(4f, 1f, 4f);
        float _yaw = 30f, _pitch = 52f, _dist = 14f;
        BuilderTool _tool = BuilderTool.Wall;
        BuilderView _view = BuilderView.Room;
        OpeningKind _opening = OpeningKind.Doorway;
        bool _showGrid = true;

        // Preview
        Transform _previewRoot;
        GameObject _previewObject;
        readonly List<Light> _previewLights = new List<Light>();
        Vector2 _origin;                 // blueprint (0,0) in layout metres
        LandmarkPlacement _placement;
        int _cutHash = -1;
        bool _needRebuild;
        Material _lineMat;

        void Awake()
        {
            Current = this;
            Time.timeScale = 1f;
            // Build mode has no camera effects: switch the camcorder pass off (it is a passthrough while this global is 0).
            Shader.SetGlobalVector("_CamVideo3", new Vector4(0f, 1f, 0f, 0f));
            CamcorderFrameHold.Fps = 0f;
            CamcorderPlayerController.InputBlocked = false;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        void OnEnable() { RenderPipelineManager.endCameraRendering += OnEndCamera; }
        void OnDisable() { RenderPipelineManager.endCameraRendering -= OnEndCamera; }
        void OnDestroy() { if (Current == this) Current = null; }

        void Start()
        {
            SetupCamera();
            RoomLibrary.Reload();
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.29f, 0.22f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;     // indoors: no sky to reflect
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.fog = false;
            var root = new GameObject("Preview");
            root.transform.SetParent(transform, false);
            _previewRoot = root.transform;
            if (BuildSession.Editing != null) { _bp = BuildSession.Editing; _vi = Mathf.Clamp(BuildSession.Variant, 0, _bp.variants.Count - 1); _dirty = BuildSession.Dirty; OpenEditor(false); }
            else ShowLibrary();
        }

        void SetupCamera()
        {
            _cam = Camera.main;
            if (!_cam)
            {
                var go = new GameObject("BuilderCamera") { tag = "MainCamera" };
                _cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.012f, 0.012f, 0.016f);
            _cam.fieldOfView = 50f; _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 300f;
            _cam.cullingMask = ~0;
            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;           // no vignette, bloom or lens: build mode shows the room as it is
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.renderShadows = true;
        }

        public void SetStatus(string s, float seconds = 4f) { _status = s; _statusUntil = Time.unscaledTime + seconds; }

        // ---------------- Screens ----------------

        public void ShowLibrary()
        {
            RoomLibrary.Reload();
            _screen = ScreenKind.Library;
            ClearPreview();
            _thumbCache.Clear();
            BuildSession.Editing = null;
            _modal = Modal.None;
        }

        public void OpenEditor(bool frame = true)
        {
            _screen = ScreenKind.Editor;
            BuildSession.Editing = _bp;
            _undo.Clear(); _redo.Clear();
            _selected = Selection.None;
            _needRebuild = true;
            if (frame) FrameRoom();
        }

        void StartNew()
        {
            _bp = RoomBlueprint.CreateNew("New room");
            _bp.id = RoomLibrary.NewId();
            _vi = 0; _dirty = true; BuildSession.Variant = 0; BuildSession.Dirty = true;
            OpenEditor();
        }

        void StartEdit(string id)
        {
            var src = RoomLibrary.Get(id);
            if (src == null) return;
            _bp = src.Clone();
            _vi = 0; _dirty = false; BuildSession.Variant = 0; BuildSession.Dirty = false;
            OpenEditor();
        }

        void FrameRoom()
        {
            var v = V;
            var o = OriginOf(v);
            _target = new Vector3(o.x + v.width * C * 0.5f, 1f, o.y + v.depth * C * 0.5f);
            _dist = Mathf.Max(v.width, v.depth) * C * 1.35f + 3f;
            _yaw = 25f; _pitch = _view == BuilderView.Top ? 90f : 52f;
        }

        /// <summary>Where the room's south-west corner lies in the preview layout (metres): the room is centred in the corridor ring.</summary>
        static Vector2 OriginOf(RoomVariant v)
        {
            int n = Mathf.Max(OfficeBiome.ChunkCells, Mathf.Max(v.width, v.depth) + 2 * CustomRoomHarness.Margin);
            return new Vector2((n - v.width) / 2, (n - v.depth) / 2) * C;
        }

        // ---------------- Preview ----------------

        void ClearPreview()
        {
            if (_previewObject) Destroy(_previewObject);
            foreach (var l in _previewLights) if (l) Destroy(l.gameObject);
            _previewLights.Clear();
            _previewObject = null;
        }

        bool HidesCeiling => _view != BuilderView.Solid;

        /// <summary>Which edges of the preview are cut down for this view, as a hash (rebuild when it changes).</summary>
        int ComputeCutHash(out List<(bool vertical, int line, int along)> cuts)
        {
            cuts = null;
            if (_view != BuilderView.Cutout) return _view == BuilderView.WallsDown || _view == BuilderView.Top ? 1 : 0;
            cuts = new List<(bool, int, int)>();
            var v = V;
            int n = _layoutN;
            Vector2 centre = _origin + v.SizeMetres * 0.5f;
            var toCam = new Vector2(Mathf.Sin(_yaw * Mathf.Deg2Rad), Mathf.Cos(_yaw * Mathf.Deg2Rad)) * -1f;
            // The camera looks along +yaw; it stands on the opposite side. Walls on the camera's side of the room are cut down.
            var camPos = new Vector2(_cam.transform.position.x, _cam.transform.position.z);
            toCam = (camPos - centre).normalized;
            float radius = v.SizeMetres.magnitude * 0.5f;
            int h = 17;
            for (int x = 0; x <= n; x++)
            for (int z = 0; z < n; z++)
            {
                var m = new Vector2(x * C, (z + 0.5f) * C);
                if (Vector2.Dot(m - centre, toCam) > radius * 0.12f) { cuts.Add((true, x, z)); h = h * 31 + x * 977 + z; }
            }
            for (int x = 0; x < n; x++)
            for (int z = 0; z <= n; z++)
            {
                var m = new Vector2((x + 0.5f) * C, z * C);
                if (Vector2.Dot(m - centre, toCam) > radius * 0.12f) { cuts.Add((false, x, z)); h = h * 31 + x * 653 + z + 7; }
            }
            return h;
        }

        int _layoutN = 12;

        void Rebuild()
        {
            _needRebuild = false;
            ClearPreview();
            if (!assets) { SetStatus("The builder has no office assets assigned."); return; }
            _bp.Validate();
            var L = CustomRoomHarness.BuildLayout(_bp, _vi, 0, out _placement);
            _layoutN = L.N;
            _origin = new Vector2(_placement.rect.x, _placement.rect.y) * C;
            _cutHash = ComputeCutHash(out var cuts);
            ApplyViewToLayout(L, cuts);

            var builder = new OfficeChunkBuilder(assets);
            builder.BuildData(L);
            _previewObject = builder.Instantiate(_previewRoot);
            foreach (var rb in _previewObject.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            foreach (var r in _previewObject.GetComponentsInChildren<Renderer>())
                if (HidesCeiling && IsCeilingMaterial(r.sharedMaterial)) r.enabled = false;
            foreach (var ls in builder.lights) AddPreviewLight(ls);
        }

        void ApplyViewToLayout(OfficeLayout L, List<(bool vertical, int line, int along)> cuts)
        {
            if (_view == BuilderView.WallsDown || _view == BuilderView.Top)
            {
                for (int x = 0; x <= L.N; x++) for (int z = 0; z < L.N; z++) LowerEdge(L, true, x, z);
                for (int x = 0; x < L.N; x++) for (int z = 0; z <= L.N; z++) LowerEdge(L, false, z, x);
            }
            else if (cuts != null)
                foreach (var c in cuts) LowerEdge(L, c.vertical, c.line, c.along);
        }

        /// <summary>Preview only: a wall becomes a half wall, a doorway an opening, so the inside can be seen.</summary>
        static void LowerEdge(OfficeLayout L, bool vertical, int line, int along)
        {
            EdgeKind k = vertical ? L.vEdges[line, along] : L.hEdges[along, line];
            if (k == EdgeKind.Open) return;
            bool outer = vertical ? (line == 0 || line == L.N) : (line == 0 || line == L.N);
            if (outer) return;      // the outer boundary of the preview area stays (nothing lies beyond it)
            if (vertical) { L.vEdges[line, along] = EdgeKind.Open; L.archV[line, along] = false; }
            else { L.hEdges[along, line] = EdgeKind.Open; L.archH[along, line] = false; }
            if (k != EdgeKind.Wall) return;
            int cx = Mathf.Clamp(vertical ? line : along, 0, L.N - 1), cz = Mathf.Clamp(vertical ? along : line, 0, L.N - 1);
            L.halfWalls.Add(new HalfWall { alongX = !vertical, line = line, start = along, length = 1, roomColor = L.roomOf[cx, cz] });
        }

        bool IsCeilingMaterial(Material m)
        {
            if (!m) return false;
            return m == assets.ceilingTile || m == assets.ceilingGrid || m == assets.ceilingPlaster || m == assets.lensOn || m == assets.lensOff
                || m == assets.domeOn || m == assets.domeOff || m == assets.stripOn || m == assets.stripOff || m == assets.fixtureHousing;
        }

        void AddPreviewLight(LightSource ls)
        {
            var go = new GameObject("PreviewLight");
            go.transform.SetParent(_previewRoot, false);
            go.transform.position = ls.position;
            var l = go.AddComponent<Light>();
            if (ls.isLamp)
            {
                l.type = LightType.Point; l.range = 5f; l.intensity = 1.6f; l.color = new Color(1f, 0.82f, 0.55f);
            }
            else
            {
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                l.type = LightType.Spot; l.spotAngle = 150f; l.innerSpotAngle = 110f;
                l.range = ls.dome || ls.strip ? 12f : 8f; l.intensity = ls.dome ? 4.5f : 3.2f; l.color = ls.strip ? new Color(0.9f, 0.95f, 1f) : new Color(1f, 0.95f, 0.8f);
            }
            l.shadows = LightShadows.None;
            go.AddComponent<UniversalAdditionalLightData>().renderingLayers = 0xFFFFFFFFu;
            _previewLights.Add(l);
        }

        // ---------------- Camera ----------------

        void UpdateCamera(bool uiBlocked)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (mouse == null || kb == null) return;
            bool top = _view == BuilderView.Top;
            if (!uiBlocked)
            {
                var d = mouse.delta.ReadValue();
                bool shift = kb.leftShiftKey.isPressed;
                if (mouse.rightButton.isPressed && !shift)
                {
                    _yaw += d.x * 0.25f;
                    if (!top) _pitch = Mathf.Clamp(_pitch - d.y * 0.25f, 8f, 89f);
                }
                if (mouse.middleButton.isPressed || (mouse.rightButton.isPressed && shift)) Pan(-d.x * _dist * 0.0016f, -d.y * _dist * 0.0016f);
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) _dist = Mathf.Clamp(_dist * (1f - Mathf.Sign(scroll) * 0.1f), 2.5f, 90f);
            }
            if (!_textFocus)
            {
                float s = _dist * 0.9f * Time.unscaledDeltaTime;
                float fx = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), fz = (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0);
                if (fx != 0 || fz != 0) Pan(fx * s, fz * s);
                if (kb.qKey.isPressed) _yaw -= 70f * Time.unscaledDeltaTime;
                if (kb.eKey.isPressed) _yaw += 70f * Time.unscaledDeltaTime;
                if (kb.fKey.wasPressedThisFrame) FrameRoom();
            }
            if (top) _pitch = 90f;
            _target.x = Mathf.Clamp(_target.x, -20f, 60f); _target.z = Mathf.Clamp(_target.z, -20f, 60f);
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            _cam.transform.SetPositionAndRotation(_target + rot * new Vector3(0f, 0f, -_dist), rot);
        }

        void Pan(float right, float forward)
        {
            var f = Quaternion.Euler(0f, _yaw, 0f);
            _target += f * new Vector3(right, 0f, forward);
        }

        // ---------------- Overlay (grid, hover, gizmos) ----------------

        void EnsureLineMaterial()
        {
            if (_lineMat) return;
            var sh = Shader.Find("Hidden/Internal-Colored");
            _lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            _lineMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _lineMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _lineMat.SetInt("_Cull", (int)CullMode.Off);
            _lineMat.SetInt("_ZWrite", 0);
        }

        void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _cam || _screen != ScreenKind.Editor || _bp == null) return;
            EnsureLineMaterial();
            _lineMat.SetInt("_ZTest", (int)CompareFunction.LessEqual);
            _lineMat.SetPass(0);
            GL.PushMatrix();
            GL.Viewport(_cam.pixelRect);           // draw only inside the 3D view, never under the panels
            DrawGrid();
            _lineMat.SetInt("_ZTest", (int)CompareFunction.Always);
            _lineMat.SetPass(0);
            DrawGizmos();
            GL.PopMatrix();
            GL.Viewport(new Rect(0, 0, Screen.width, Screen.height));
        }

        void Line(Vector3 a, Vector3 b, Color c) { GL.Color(c); GL.Vertex(a); GL.Vertex(b); }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
        {
            GL.Color(col); GL.Vertex(a); GL.Vertex(b); GL.Vertex(c); GL.Vertex(d);
        }

        /// <summary>Layout metres from blueprint metres (the room is not at the layout's origin).</summary>
        Vector3 W3(float lx, float lz, float y = 0.02f) => new Vector3(_origin.x + lx, y, _origin.y + lz);

        void DrawGrid()
        {
            if (!_showGrid) return;
            var v = V;
            float ext = 3f * C;
            GL.Begin(GL.LINES);
            for (int x = -3; x <= v.width + 3; x++)
            {
                bool edge = x == 0 || x == v.width;
                Line(W3(x * C, -ext), W3(x * C, v.depth * C + ext), edge ? new Color(0.9f, 0.78f, 0.3f, 0.7f) : new Color(1f, 1f, 1f, 0.07f));
            }
            for (int z = -3; z <= v.depth + 3; z++)
            {
                bool edge = z == 0 || z == v.depth;
                Line(W3(-ext, z * C), W3(v.width * C + ext, z * C), edge ? new Color(0.9f, 0.78f, 0.3f, 0.7f) : new Color(1f, 1f, 1f, 0.07f));
            }
            GL.End();
        }
    }
}
