using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Backrooms.Interaction;

namespace Backrooms.Generation
{
    /// <summary>Two room-bounded cached indoor probes, one time-sliced capture at a time.</summary>
    public sealed class RuntimeIndoorReflections : MonoBehaviour
    {
        public static RuntimeIndoorReflections Instance { get; private set; }
        public WorldStreamer streamer;
        public int CaptureCount { get; private set; }
        public int ActiveProbes { get; private set; }
        public bool Capturing => _capturing != null;
        sealed class Slot
        {
            public ReflectionProbe probe;
            public string key;
            public Bounds bounds;
            public float used, nextCapture;
            public int captureId = -1, revision;
            public bool ready;
        }
        readonly Slot[] _slots = new Slot[2];
        readonly Dictionary<Material, Material> _variants = new Dictionary<Material, Material>();
        readonly Dictionary<Material, Material> _originals = new Dictionary<Material, Material>();
        Slot _capturing;
        int _chunkRevision;
        Vector3Int _lastCell = new Vector3Int(int.MinValue, 0, 0);
        string _currentKey;
        Bounds _currentBounds;
        WorldGenerator _world;
        bool _originalRealtimeProbes, _ownsQuality;

        void OnEnable()
        {
            Instance = this;
            _originalRealtimeProbes = QualitySettings.realtimeReflectionProbes; _ownsQuality = true;
            QualitySettings.realtimeReflectionProbes = true;
            if (!streamer) streamer = GetComponent<WorldStreamer>();
            if (streamer) { streamer.ChunkLoaded += OnLoaded; streamer.ChunkUnloaded += OnUnloaded; if (Application.isPlaying) Prepare(streamer.gameObject); }
        }
        void OnDisable()
        {
            if (streamer) { streamer.ChunkLoaded -= OnLoaded; streamer.ChunkUnloaded -= OnUnloaded; }
            ReleaseProbes(); RestoreMaterials();
            if (_ownsQuality) { QualitySettings.realtimeReflectionProbes = _originalRealtimeProbes; _ownsQuality = false; }
            ActiveProbes = 0; if (Instance == this) Instance = null;
        }
        void OnLoaded(Vector3Int key, GameObject root) { Prepare(root); _chunkRevision++; _lastCell.x = int.MinValue; }
        void OnUnloaded(Vector3Int key, GameObject root) { _chunkRevision++; _lastCell.x = int.MinValue; }

        /// <summary>Shared runtime variants enable reflections without modifying imported/project materials.</summary>
        public void Prepare(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                var materials = renderer.sharedMaterials; bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (!source || _originals.ContainsKey(source) || !source.HasProperty("_EnvironmentReflections")) continue;
                    bool useful = (source.HasProperty("_Smoothness") && source.GetFloat("_Smoothness") >= 0.3f) || (source.HasProperty("_Metallic") && source.GetFloat("_Metallic") > 0.1f);
                    if (!useful || (source.GetFloat("_EnvironmentReflections") > 0.5f && !source.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"))) continue;
                    if (!_variants.TryGetValue(source, out var variant))
                    {
                        variant = new Material(source) { name = source.name + " (runtime indoor reflections)", hideFlags = HideFlags.DontSave };
                        variant.SetFloat("_EnvironmentReflections", 1); variant.DisableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                        _variants[source] = variant; _originals[variant] = source;
                    }
                    materials[i] = variant; changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        Slot MakeSlot(int index)
        {
            var root = new GameObject("Indoor reflection " + index); root.transform.SetParent(transform, false); root.SetActive(false);
            var probe = root.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = 128; probe.hdr = true; probe.boxProjection = true;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor; probe.backgroundColor = Color.black;
            probe.cullingMask = ~(1 << CrawlPortalView.Layer); probe.renderDynamicObjects = true;
            probe.nearClipPlane = 0.1f; probe.blendDistance = 0.03f; probe.intensity = 0;
            root.SetActive(true);
            return _slots[index] = new Slot { probe = probe };
        }

        void LateUpdate()
        {
            if (!streamer || !streamer.player || streamer.World == null) return;
            var graphics = Camcorder.GraphicsController.Instance;
            bool enabledInMenu = !graphics || graphics.Options.indoorReflections;
            if (_capturing != null && _capturing.probe && _capturing.probe.IsFinishedRendering(_capturing.captureId))
            {
                // Unity 6000.4 Forward+ can keep an outdated atlas entry after a scripted capture (UUM-129405).
                if (_capturing.probe.texture) _capturing.probe.texture.IncrementUpdateCount();
                _capturing.ready = true; _capturing.captureId = -1; _capturing = null; CaptureCount++;
            }
            var eye = streamer.player.position + Vector3.up * 1.5f;
            var cell = new Vector3Int(Mathf.FloorToInt(eye.x / OfficeBiome.CellSize), eye.y >= streamer.Settings.storeyPitch ? 1 : 0, Mathf.FloorToInt(eye.z / OfficeBiome.CellSize));
            FollowWorld(streamer.World);
            if (cell != _lastCell)
            {
                _lastCell = cell; _currentKey = null;
                if (Worth(cell) && TryBounds(cell, out _currentBounds)) _currentKey = _world.GetHashCode() + ":" + cell.y + ":" + _currentBounds.ToString("F2");
            }
            int revision = _chunkRevision;
            var grid = streamer.lightManager ? streamer.lightManager.grid : null;
            if (grid) revision = unchecked(revision * 397 ^ grid.OcclusionRevision * 17 ^ grid.LightingRevision);
            Slot current = null;
            foreach (var slot in _slots) if (slot != null && slot.key == _currentKey && _currentKey != null) { current = slot; break; }
            if (enabledInMenu && current == null && _currentKey != null && _capturing == null)
            {
                int replace = _slots[0] == null ? 0 : _slots[1] == null ? 1 : _slots[0].used <= _slots[1].used ? 0 : 1;
                current = _slots[replace] ?? MakeSlot(replace);
                current.key = _currentKey; current.bounds = _currentBounds; current.ready = false; current.nextCapture = 0;
                var position = current.bounds.center; position.y = current.bounds.min.y + Mathf.Min(1.5f, current.bounds.size.y * 0.5f);
                if (Physics.CheckSphere(position, 0.12f, ~0, QueryTriggerInteraction.Ignore)) position = eye;
                current.probe.transform.position = position; current.probe.center = current.bounds.center - position;
                current.probe.size = current.bounds.size; current.probe.farClipPlane = Mathf.Clamp(current.bounds.size.magnitude + 1, 5, 40);
                // No shadows in the capture (2026-10-31): each face rendered every shadow light's map again (16 maps a face), and
                // a capture's frames were the walk's slowest (3-5 ms more on the render thread); at 128 pixels, blurred on
                // laminate, the shadows in a reflection do not show.
                current.probe.shadowDistance = 0f;
                current.probe.intensity = 0;
            }
            if (current != null) current.used = Time.unscaledTime;
            ActiveProbes = 0;
            foreach (var slot in _slots)
            {
                if (slot == null || !slot.probe) continue;
                // Disabling an in-flight time-sliced probe prevents completion and strands the capture queue.
                slot.probe.enabled = enabledInMenu && slot.key != null && (slot == _capturing || slot.bounds.SqrDistance(eye) < 24f * 24f);
                float target = slot.ready && slot.probe.enabled ? 1 : 0;
                slot.probe.intensity = Mathf.MoveTowards(slot.probe.intensity, target, Time.unscaledDeltaTime * 2);
                if (slot.probe.enabled) ActiveProbes++;
            }
            if (!enabledInMenu || _capturing != null || current == null || Time.unscaledTime < current.nextCapture) return;
            if (current.ready && current.revision == revision) return;
            current.captureId = current.probe.RenderProbe();
            // A new room is captured at once; the same room again for a change of its light at most every 30 s (2026-10-27:
            // the lighting revision moves all the time, faulty tubes and the real-light set, and a capture every 4 s, six
            // faces with full lighting and shadows, cost about a tenth of the graphics card for reflections on laminate).
            current.nextCapture = Time.unscaledTime + 30f; current.revision = revision;
            if (current.captureId >= 0) _capturing = current;
        }

        void FollowWorld(WorldGenerator world)
        {
            if (_world == world) return;
            // A previous world's capture cannot finish after its probe is disabled by key invalidation.
            ReleaseProbes(); _world = world; _lastCell.x = int.MinValue; _currentKey = null;
        }

        bool Cell(Vector3Int c, out OfficeLayout layout, out int x, out int z)
        {
            int n = OfficeBiome.ChunkCells;
            int cx = Mathf.FloorToInt(c.x / (float)n), cz = Mathf.FloorToInt(c.z / (float)n);
            layout = streamer.LayoutAt(new Vector3Int(cx, c.y, cz)); x = c.x - cx * n; z = c.z - cz * n;
            return layout != null;
        }
        bool Same(Vector3Int c, int room, Vector2Int district)
        {
            if (!Cell(c, out var layout, out int x, out int z)) return false;
            return WorldGenerator.DistrictOf(layout.coord) == district && layout.RoomAt(x, z).globalId == room;
        }
        bool Open(Vector3Int a, Vector3Int b)
        {
            var owner = new Vector3Int(Mathf.Max(a.x, b.x), a.y, Mathf.Max(a.z, b.z));
            if (!Cell(owner, out var layout, out int x, out int z)) return false;
            return (a.x != b.x ? layout.vEdges[x, z] : layout.hEdges[x, z]) == EdgeKind.Open;
        }

        /// <summary>
        /// Is a reflection worth capturing in this room? Only where something shiny is (a wood or laminate floor, a good room's
        /// counters and fittings, a built room). A capture re-draws the scene once for each of six faces, 2.5-4 ms each on the
        /// render thread (2026-10-31: the walk's slowest frames were all captures), and the carpet-and-wallpaper rooms
        /// most of the building is made of reflect nothing a camcorder can see.
        /// </summary>
        bool Worth(Vector3Int cell)
        {
            if (!Cell(cell, out var layout, out int x, out int z)) return false;
            var room = layout.RoomAt(x, z);
            return room.floorFinish == FloorFinish.DarkWood || room.floorFinish == FloorFinish.Laminate || room.special != GoodRoom.None || room.custom;
        }

        /// <summary>Grow only through matching loaded cells and completely open edges; never enclose a partition.</summary>
        bool TryBounds(Vector3Int cell, out Bounds bounds)
        {
            bounds = default;
            if (!Cell(cell, out var layout, out int x, out int z)) return false;
            var room = layout.RoomAt(x, z); if (room.noFloor || room.kind == RoomKind.Crawl) return false;
            int id = room.globalId; var district = WorldGenerator.DistrictOf(layout.coord);
            int x0 = cell.x, x1 = cell.x, z0 = cell.z, z1 = cell.z;
            bool FitsColumn(int next, int previous)
            {
                for (int zz = z0; zz <= z1; zz++)
                {
                    var c = new Vector3Int(next, cell.y, zz);
                    if (!Same(c, id, district) || !Open(c, new Vector3Int(previous, cell.y, zz))) return false;
                    if (zz > z0 && !Open(c, new Vector3Int(next, cell.y, zz - 1))) return false;
                }
                return true;
            }
            bool FitsRow(int next, int previous)
            {
                for (int xx = x0; xx <= x1; xx++)
                {
                    var c = new Vector3Int(xx, cell.y, next);
                    if (!Same(c, id, district) || !Open(c, new Vector3Int(xx, cell.y, previous))) return false;
                    if (xx > x0 && !Open(c, new Vector3Int(xx - 1, cell.y, next))) return false;
                }
                return true;
            }
            while (x1 - x0 < 23 && FitsColumn(x0 - 1, x0)) x0--;
            while (x1 - x0 < 23 && FitsColumn(x1 + 1, x1)) x1++;
            while (z1 - z0 < 23 && FitsRow(z0 - 1, z0)) z0--;
            while (z1 - z0 < 23 && FitsRow(z1 + 1, z1)) z1++;
            float C = OfficeBiome.CellSize, floor = cell.y * layout.storeyPitch;
            bounds.SetMinMax(new Vector3(x0 * C + 0.03f, floor - 0.03f, z0 * C + 0.03f), new Vector3((x1 + 1) * C - 0.03f, floor + room.ceiling + 0.03f, (z1 + 1) * C - 0.03f));
            return true;
        }

        void ReleaseProbes()
        {
            _capturing = null;
            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i]; _slots[i] = null;
                if (slot == null || !slot.probe) continue;
                slot.probe.enabled = false;
                if (Application.isPlaying) Destroy(slot.probe.gameObject); else DestroyImmediate(slot.probe.gameObject);
            }
        }

        void RestoreMaterials()
        {
            if (_originals.Count > 0 && streamer)
                foreach (var renderer in streamer.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var materials = renderer.sharedMaterials; bool changed = false;
                    for (int i = 0; i < materials.Length; i++) if (materials[i] && _originals.TryGetValue(materials[i], out var source)) { materials[i] = source; changed = true; }
                    if (changed) renderer.sharedMaterials = materials;
                }
            foreach (var material in _variants.Values) if (material) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            _variants.Clear(); _originals.Clear();
        }

        void OnDestroy() { ReleaseProbes(); RestoreMaterials(); }
    }
}
