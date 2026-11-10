using System.Collections.Generic;
using Backrooms.CustomRooms;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Developer menu "Items and furniture" (L): a grid of every item and furniture piece with a picture of each.
    /// Click one to spawn it in front of you (on the floor, or on the wall you are looking at for a wall clock); the
    /// menu closes. Shift-click to keep it open and spawn more. L or Escape closes it. Added by WorldStreamer.
    /// </summary>
    public class ItemsMenu : MonoBehaviour
    {
        public static ItemsMenu Instance { get; private set; }
        public static bool IsOpen => Instance && Instance._open;
        /// <summary>Open when the frame began (the pause menu leaves Escape to us then).</summary>
        public static bool WasOpen { get; private set; }

        public WorldStreamer streamer;
        bool _open, _prevBlocked;
        CursorLockMode _prevLock;
        List<ItemCatalog.Entry> _entries;
        readonly List<RenderTexture> _thumbs = new List<RenderTexture>();
        Vector2 _scroll;
        string _status = "";
        int _openedFrame = -1;

        void Awake() { Instance = this; if (!streamer) streamer = GetComponent<WorldStreamer>(); }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var t in _thumbs) if (t) { t.Release(); Destroy(t); }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            bool otherMenu = PauseMenu.IsOpen || (CamcorderPlayerController.InputBlocked && !_open);
            if (kb.lKey.wasPressedThisFrame && !otherMenu && (DevTools.On || _open)) SetOpen(!_open);   // a developer's tool (DevTools)
            else if (_open && kb.escapeKey.wasPressedThisFrame && _openedFrame != Time.frameCount) SetOpen(false);
        }

        void LateUpdate() { WasOpen = _open; }

        public void SetOpen(bool open)
        {
            if (open == _open) return;
            _open = open;
            if (open)
            {
                _openedFrame = Time.frameCount;
                if (_entries == null) { _entries = ItemCatalog.Build(streamer ? streamer.assets : null, streamer); RenderThumbnails(); }
                _prevLock = Cursor.lockState;
                _prevBlocked = CamcorderPlayerController.InputBlocked;
                CamcorderPlayerController.InputBlocked = true;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                _status = "";
            }
            else
            {
                CamcorderPlayerController.InputBlocked = _prevBlocked;
                if (_prevLock == CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            }
        }

        // ---------------- Pictures ----------------

        /// <summary>
        /// One picture per entry: each thing is built far below the world, framed and rendered once with two temporary
        /// lights, then removed (lights first, so the frame's own view never sees them).
        /// </summary>
        void RenderThumbnails()
        {
            var studio = new GameObject("ItemThumbnailStudio");
            var origin = new Vector3(0f, -400f, 0f);
            studio.transform.position = origin;
            var camGo = new GameObject("Camera"); camGo.transform.SetParent(studio.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.165f, 0.17f, 1f);
            cam.fieldOfView = 26f; cam.nearClipPlane = 0.02f; cam.farClipPlane = 30f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off; data.requiresDepthOption = CameraOverrideOption.Off;
            Light MakeLight(Vector3 euler, float intensity, Color c)
            {
                var lg = new GameObject("Light"); lg.transform.SetParent(studio.transform, false);
                lg.transform.rotation = Quaternion.Euler(euler);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Directional; l.intensity = intensity; l.color = c; l.shadows = LightShadows.None;
                l.renderingLayerMask = -1;
                return l;
            }
            var key = MakeLight(new Vector3(35f, -35f, 0f), 1.6f, new Color(1f, 0.96f, 0.9f));
            var fill = MakeLight(new Vector3(15f, 140f, 0f), 0.6f, new Color(0.85f, 0.9f, 1f));
            var restoreAmbient = RenderSettings.ambientLight;
            foreach (var e in _entries)
            {
                GameObject go = null;
                try { go = e.spawn(origin, Quaternion.Euler(0f, 205f, 0f), true); }
                catch (System.Exception ex) { Debug.LogWarning("[Backrooms] Thumbnail for " + e.name + ": " + ex.Message); }
                if (!go) { _thumbs.Add(null); continue; }
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                var b = new Bounds(origin, Vector3.zero); bool first = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                }
                float radius = Mathf.Max(0.05f, b.extents.magnitude);
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.05f;
                var dir = Quaternion.Euler(22f, 0f, 0f) * Vector3.back;
                cam.transform.position = b.center + dir * dist;
                cam.transform.LookAt(b.center);
                var rt = new RenderTexture(256, 216, 24, RenderTextureFormat.ARGB32) { name = "Thumb_" + e.name, antiAliasing = 4 };
                rt.Create();
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
                e.thumbnail = rt;
                _thumbs.Add(rt);
                DestroyImmediate(go);
            }
            key.enabled = false; fill.enabled = false;
            RenderSettings.ambientLight = restoreAmbient;
            DestroyImmediate(studio);
        }

        // ---------------- Spawning ----------------

        void Spawn(ItemCatalog.Entry e)
        {
            var player = streamer ? streamer.player : null;
            var cam = Camera.main ? Camera.main.transform : player;
            if (!player || !cam) return;
            Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = player.forward;
            fwd.Normalize();
            if (e.placement == ItemCatalog.Placement.Wall)
            {
                if (Physics.Raycast(cam.position, cam.forward, out var hit, 5f, ~0, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < 0.3f)
                {
                    var p = hit.point + hit.normal * 0.005f;
                    p.y = Mathf.Clamp(p.y, player.position.y + 1.6f, player.position.y + 2.4f);
                    e.spawn(p, Quaternion.LookRotation(hit.normal, Vector3.up), false);
                    _status = $"Hung the {e.name.ToLowerInvariant()} on the wall.";
                }
                else _status = "Look at a wall (within 5 m) to hang it.";
                return;
            }
            // In front, at the thing's clearance, pulled in if a wall is closer; on the floor there.
            float dist = e.clearance;
            Vector3 chest = player.position + Vector3.up * 0.9f;
            if (Physics.SphereCast(chest, 0.25f, fwd, out var block, dist + 0.6f, ~0, QueryTriggerInteraction.Ignore) && !block.collider.transform.IsChildOf(player))
                dist = Mathf.Max(0.5f, block.distance - 0.45f);
            Vector3 at = player.position + fwd * dist;
            if (Physics.Raycast(at + Vector3.up * 1.2f, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore)) at.y = floor.point.y;
            var rot = Quaternion.LookRotation(-fwd, Vector3.up);      // its front toward you
            var go = e.spawn(at + Vector3.up * 0.01f, rot, false);
            _status = go ? $"Spawned: {e.name}." : $"Could not spawn {e.name}.";
        }

        // ---------------- Progression (2026-11-09) ----------------

        /// <summary>
        /// The skip buttons (the owner: "scalable ways for me to skip progression in game - like a menu or something, so I can
        /// skip to when the supply counter is open"), like Satisfactory's Creative Mode: a button per work-order stage (made
        /// from WorkOrders' list, so a new stage gets one), money, and business time. Returns where the items start.
        /// </summary>
        float Progression(Rect r)
        {
            var d = Saves.GameSession.Data;
            var cs = Contracts.Instance;
            if (d == null || !cs) return r.y;
            ImUi.Header(new Rect(r.x, r.y, r.width, 28), "PROGRESSION - " + (WorkOrders.Active ? WorkOrders.StageName : "ALL UNLOCKED") + " - $" + d.pay);
            float x = r.x, y = r.y + 32f, bh = 34f, gap = 8f;
            bool Btn(string label, float bw)
            {
                if (x + bw > r.xMax) { x = r.x; y += bh + gap; }
                bool hit = ImUi.Button(new Rect(x, y, bw, bh), label);
                x += bw + gap;
                return hit;
            }
            for (int s = 1; s <= WorkOrders.Stages; s++)
            {
                bool past = d.woStage >= s;
                string label = WorkOrders.SkipLabel(s) + (past ? " (DONE)" : "");
                if (Btn(label, 262f) && !past) { WorkOrders.SkipTo(s); _status = "Skipped: " + WorkOrders.SkipLabel(s).ToLowerInvariant() + "."; }
            }
            x = r.x; y += bh + gap;
            if (Btn("+$100", 100f)) { cs.DevPay(100); _status = "+$100."; }
            if (Btn("+$1000", 110f)) { cs.DevPay(1000); _status = "+$1000."; }
            if (Btn("+1 MIN BUSINESS TIME", 230f)) { cs.DevSkip(60f); _status = "One minute of business time passes."; }
            if (Btn("+5 MIN BUSINESS TIME", 230f)) { cs.DevSkip(300f); _status = "Five minutes of business time pass."; }
            return y + bh + 14f;
        }

        // ---------------- UI ----------------

        void OnGUI()
        {
            if (!_open || _entries == null) return;
            ImUi.Begin();
            ImUi.Fill(new Rect(0, 0, ImUi.Width, ImUi.Height), new Color(0, 0, 0, 0.55f));
            float w = Mathf.Min(1180f, ImUi.Width - 80f), h = 860f;
            var box = new Rect(ImUi.Width * 0.5f - w * 0.5f, ImUi.Height * 0.5f - h * 0.5f, w, h);
            ImUi.PanelBox(box);
            GUI.Label(new Rect(box.x + 28, box.y + 18, w - 56, 40), "ITEMS AND FURNITURE", ImUi.Title);
            ImUi.Dimmed(new Rect(box.x + 28, box.y + 60, w - 56, 40),
                "Developer menu. Click to spawn in front of you (shift-click to keep the menu open). L or Esc closes it. " +
                "Pick up an item by clicking it; hold the left mouse button to use it, Q to drop it.");

            const float tileW = 168f, tileH = 172f, gap = 12f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((w - 56f + gap) / (tileW + gap)));
            float top = Progression(new Rect(box.x + 28, box.y + 104, w - 56, 0f));
            var view = new Rect(box.x + 28, top, w - 56, box.yMax - 44 - top);
            // Layout: sections, each a header and its rows of tiles.
            var sections = new List<string>();
            foreach (var e in _entries) if (!sections.Contains(e.category)) sections.Add(e.category);
            float contentH = 0f;
            foreach (var s in sections)
            {
                int n = _entries.FindAll(e => e.category == s).Count;
                contentH += 34f + Mathf.CeilToInt(n / (float)cols) * (tileH + gap) + 8f;
            }
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, view.width - 18, contentH));
            float y = 0f;
            var mouse = Event.current.mousePosition;
            foreach (var s in sections)
            {
                ImUi.Header(new Rect(0, y, view.width, 28), s);
                y += 34f;
                int i = 0;
                foreach (var e in _entries)
                {
                    if (e.category != s) continue;
                    var r = new Rect((i % cols) * (tileW + gap), y + (i / cols) * (tileH + gap), tileW, tileH);
                    bool hover = r.Contains(mouse);
                    ImUi.Fill(r, hover ? ImUi.Hover : ImUi.Raised);
                    var pic = new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 44);
                    if (e.thumbnail) GUI.DrawTexture(pic, e.thumbnail, ScaleMode.ScaleAndCrop);
                    else ImUi.Fill(pic, ImUi.Back);
                    GUI.Label(new Rect(r.x + 8, r.yMax - 36, r.width - 16, 32), e.name, ImUi.Small);
                    if (hover) ImUi.Fill(new Rect(r.x, r.yMax - 3, r.width, 3), ImUi.Accent);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                    {
                        bool keep = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
                        Spawn(e);
                        if (!keep) SetOpen(false);
                    }
                    i++;
                }
                y += Mathf.CeilToInt(i / (float)cols) * (tileH + gap) + 8f;
            }
            GUI.EndScrollView();
            if (!string.IsNullOrEmpty(_status)) GUI.Label(new Rect(box.x + 28, box.yMax - 36, w - 56, 28), _status, ImUi.Small);
            ImUi.End();
        }
    }
}
