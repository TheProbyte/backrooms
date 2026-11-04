using System.Collections.Generic;
using System.Linq;
using Backrooms.Generation;
using UnityEditor;
using UnityEngine;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Backrooms > World Map: top-down view of the generated world (zones, biomes, districts, rooms, light
    /// layers, light field) with the consistency checks from the tests drawn on top. In play mode it shows
    /// the streamed world around the player; in edit mode any seed with the saved settings.
    /// </summary>
    public class WorldMapWindow : EditorWindow
    {
        [MenuItem("Backrooms/World Map")]
        static void Open() => GetWindow<WorldMapWindow>("World Map");

        int _seed = 1;
        Vector2Int _centre;
        int _radius = 4, _px = 6;
        WorldMapRaster.Mode _mode = WorldMapRaster.Mode.Rooms;
        bool _followPlayer = true;
        Texture2D _tex;
        WorldGenerator _gen;
        RectInt _shown;
        readonly List<WorldChecks.Issue> _issues = new List<WorldChecks.Issue>();
        string _summary = "";
        Vector2 _scroll;
        bool _dirty = true;

        /// <summary>Light-field sampler supplied by the light grid when it exists (play mode).</summary>
        public static WorldMapRaster.CellValue LightFieldProvider;

        void OnEnable() { wantsMouseMove = true; EditorApplication.update += Tick; }
        void OnDisable() { EditorApplication.update -= Tick; if (_tex) DestroyImmediate(_tex); }

        float _nextPlayRefresh;
        void Tick()
        {
            if (!Application.isPlaying || !_followPlayer) return;
            if (EditorApplication.timeSinceStartup < _nextPlayRefresh) return;
            _nextPlayRefresh = (float)EditorApplication.timeSinceStartup + 1f;
            _dirty = true; Repaint();
        }

        WorldGenerator Source()
        {
            if (Application.isPlaying)
            {
                var ws = FindAnyObjectByType<WorldStreamer>();
                if (ws && ws.World != null)
                {
                    if (_followPlayer && ws.player) _centre = WorldStreamer.ChunkOf(ws.player.position);
                    return ws.World;
                }
            }
            if (_gen == null || _gen.Seed != _seed) _gen = new WorldGenerator(_seed, WorldGenSettings.Load());
            return _gen;
        }

        void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                _mode = (WorldMapRaster.Mode)EditorGUILayout.EnumPopup(_mode, GUILayout.Width(110));
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                    _seed = EditorGUILayout.IntField("Seed", _seed, GUILayout.Width(200));
                _centre = EditorGUILayout.Vector2IntField(GUIContent.none, _centre, GUILayout.Width(140));
                _radius = EditorGUILayout.IntSlider(_radius, 1, 12, GUILayout.Width(150));
                _px = EditorGUILayout.IntSlider(_px, 1, 12, GUILayout.Width(150));
                if (Application.isPlaying) _followPlayer = GUILayout.Toggle(_followPlayer, "Follow", GUILayout.Width(60));
            }
            if (EditorGUI.EndChangeCheck()) { _dirty = true; _issues.Clear(); _summary = ""; }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run checks", GUILayout.Width(100))) RunChecks();
                if (GUILayout.Button("Clear", GUILayout.Width(60))) { _issues.Clear(); _summary = ""; _dirty = true; }
                GUILayout.Label(_summary, EditorStyles.miniLabel);
            }

            if (_dirty || !_tex) Redraw();
            if (!_tex) return;

            var area = GUILayoutUtility.GetRect(position.width, position.height - 90);
            _scroll = GUI.BeginScrollView(area, _scroll, new Rect(0, 0, _tex.width, _tex.height));
            var r = new Rect(0, 0, _tex.width, _tex.height);
            GUI.DrawTexture(r, _tex);
            HoverInfo(r);
            GUI.EndScrollView();
        }

        void Redraw()
        {
            _dirty = false;
            var gen = Source();
            _shown = new RectInt(_centre.x - _radius, _centre.y - _radius, 2 * _radius + 1, 2 * _radius + 1);
            var marks = _issues.Select(i => i.cell).Concat(_issues.Select(i => i.from)).ToList();
            var buf = WorldMapRaster.Draw(gen, _shown, _mode, _px, marks, LightFieldProvider);
            int W = _shown.width * OfficeBiome.ChunkCells * _px, H = _shown.height * OfficeBiome.ChunkCells * _px;
            if (!_tex || _tex.width != W || _tex.height != H)
            {
                if (_tex) DestroyImmediate(_tex);
                _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            }
            _tex.SetPixels32(buf);
            _tex.Apply();
        }

        void RunChecks()
        {
            var gen = Source();
            var block = new RectInt(_centre.x - _radius, _centre.y - _radius, 2 * _radius + 1, 2 * _radius + 1);
            _issues.Clear();
            var b = WorldChecks.BorderMismatches(gen, block);
            var u = WorldChecks.Unreachable(gen, block);
            var l = WorldChecks.LightLeaks(gen, block);
            _issues.AddRange(b); _issues.AddRange(u); _issues.AddRange(l);
            _summary = $"{b.Count} border mismatches, {u.Count} unreachable cells, {l.Count} light leaks ({l.Count(i => i.crossChunk)} across chunk borders)";
            foreach (var i in _issues.Take(20)) Debug.Log("[WorldMap] " + i);
            _dirty = true;
        }

        void HoverInfo(Rect r)
        {
            var e = Event.current;
            if (!r.Contains(e.mousePosition)) return;
            int N = OfficeBiome.ChunkCells;
            int gx = _shown.xMin * N + Mathf.FloorToInt(e.mousePosition.x / _px);
            int gz = _shown.yMin * N + Mathf.FloorToInt((_tex.height - e.mousePosition.y) / _px);
            var chunk = new Vector2Int(Mathf.FloorToInt(gx / (float)N), Mathf.FloorToInt(gz / (float)N));
            var gen = Source();
            var L = gen.Chunk(chunk);
            int lx = gx - chunk.x * N, lz = gz - chunk.y * N;
            if (!L.InChunk(lx, lz)) return;
            var room = L.RoomAt(lx, lz);
            var s = gen.Sample(chunk);
            string text = $"cell {gx},{gz}  chunk {chunk.x},{chunk.y}  local {lx},{lz}\n{s.zone} / {L.biomeName}  room {room.id} {room.kind} {room.rect.width}x{room.rect.height}" +
                          $"  ceiling {room.ceiling:0.0} m  lights {room.lights}  colour {room.color}";
            var size = EditorStyles.helpBox.CalcSize(new GUIContent(text));
            GUI.Label(new Rect(e.mousePosition.x + 14, e.mousePosition.y + 10, size.x + 8, size.y + 4), text, EditorStyles.helpBox);
            if (e.type == EventType.MouseMove) Repaint();
        }
    }
}
