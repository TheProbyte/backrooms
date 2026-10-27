#if STEAMAUDIO_ENABLED
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// Gives Steam Audio the world's walls. Every loaded chunk's architecture (its wall, post, floor and ceiling collider
    /// boxes) becomes one Steam Audio static mesh, with an acoustic material by what each box is: painted drywall walls,
    /// carpet floors, mineral-fibre drop ceilings. A shut door (<see cref="DoorEdges"/>) adds a wooden slab in its
    /// doorway, removed again when it opens. Meshes come and go with the chunks.
    ///
    /// Sounds of things in the world (items, doors, switches, cords: everything made with ItemModels.Source) go through
    /// Steam Audio's direct path (<see cref="Attach"/>): HRTF, occlusion by these meshes and transmission through them,
    /// so a door creaking in the next room is muffled by the wall, less by a shut door, and not at all through the
    /// doorway. The building's own hum, buzz and ventilation keep their walking-path model (BuildingAudio,
    /// AcousticField), which already handles walls and doors; they are not run through both.
    /// </summary>
    public class SteamAudioWorld : MonoBehaviour
    {
        public WorldStreamer streamer;
        public static int Meshes { get; private set; }
        public static int Triangles { get; private set; }

        readonly Dictionary<Vector3Int, (SteamAudio.StaticMesh mesh, int tris)> _chunks = new Dictionary<Vector3Int, (SteamAudio.StaticMesh, int)>();
        readonly Dictionary<(bool, int, int, int), SteamAudio.StaticMesh> _doors = new Dictionary<(bool, int, int, int), SteamAudio.StaticMesh>();
        readonly List<(Vector3Int coord, GameObject go)> _waiting = new List<(Vector3Int, GameObject)>();
        readonly List<DoorEdges.Edge> _waitingDoors = new List<DoorEdges.Edge>();

        // Acoustic materials: absorption low/mid/high, scattering, transmission low/mid/high.
        static readonly SteamAudio.Material Drywall = Mat(0.12f, 0.06f, 0.04f, 0.05f, 0.056f, 0.04f, 0.004f);
        static readonly SteamAudio.Material Carpet = Mat(0.24f, 0.69f, 0.73f, 0.05f, 0.020f, 0.005f, 0.003f);
        static readonly SteamAudio.Material CeilingTile = Mat(0.50f, 0.70f, 0.80f, 0.10f, 0.050f, 0.020f, 0.010f);
        static readonly SteamAudio.Material Generic = Mat(0.10f, 0.20f, 0.30f, 0.05f, 0.100f, 0.050f, 0.030f);
        static readonly SteamAudio.Material DoorWood = Mat(0.11f, 0.07f, 0.06f, 0.05f, 0.220f, 0.090f, 0.030f);

        static SteamAudio.Material Mat(float al, float am, float ah, float s, float tl, float tm, float th) => new SteamAudio.Material
        {
            absorptionLow = al, absorptionMid = am, absorptionHigh = ah, scattering = s,
            transmissionLow = tl, transmissionMid = tm, transmissionHigh = th,
        };

        static bool Ready => SteamAudio.SteamAudioManager.CurrentScene != null && SteamAudio.SteamAudioManager.Context != null;

        void OnEnable()
        {
            if (!streamer) streamer = GetComponent<WorldStreamer>();
            if (streamer) { streamer.ChunkLoaded += OnChunkLoaded; streamer.ChunkUnloaded += OnChunkUnloaded; streamer.ChunkWallsRebuilt += OnChunkLoaded; }
            DoorEdges.Changed += OnDoor;
            Destruction.WallDamage.Changed += OnWall;
        }

        void OnDisable()
        {
            if (streamer) { streamer.ChunkLoaded -= OnChunkLoaded; streamer.ChunkUnloaded -= OnChunkUnloaded; streamer.ChunkWallsRebuilt -= OnChunkLoaded; }
            DoorEdges.Changed -= OnDoor;
            Destruction.WallDamage.Changed -= OnWall;
            ReleaseAll();
        }

        // ---------------- Broken walls ----------------
        // A broken wall is no longer in its chunks' boxes: what is left of its boards is a mesh of its own, so sound goes
        // through the holes and is stopped (muffled through) by the rest.

        readonly Dictionary<Destruction.WallKey, (SteamAudio.StaticMesh mesh, int tris)> _walls = new Dictionary<Destruction.WallKey, (SteamAudio.StaticMesh, int)>();
        readonly HashSet<Destruction.WallKey> _wallsWaiting = new HashSet<Destruction.WallKey>();

        void OnWall(Destruction.WallKey k) { if (Ready) BuildWall(k); else _wallsWaiting.Add(k); }

        void BuildWall(Destruction.WallKey k)
        {
            if (_walls.TryGetValue(k, out var old)) { _walls.Remove(k); Drop(old.mesh, old.tris); }
            var p = Destruction.WallDamage.Find(k);
            if (p == null) return;
            _v.Clear(); _t.Clear(); _m.Clear();
            float pitch = Destruction.WallDamage.Pitch;
            for (int side = 0; side < 2; side++)
            {
                float w = (side == 0 ? -1f : 1f) * (Destruction.WallSpec.T * 0.5f - Destruction.WallSpec.Board * 0.5f);
                foreach (var f in p.model.boards[side].pieces)
                {
                    if (p.state.removed[side][f.index]) continue;
                    int start = _v.Count;
                    var c = f.cell;
                    Add(k.ToWorld(f.site.x, f.site.y, w, pitch));
                    for (int i = 0; i < c.Length; i++) Add(k.ToWorld(c[i].x, c[i].y, w, pitch));
                    for (int i = 0; i < c.Length; i++)
                    {
                        _t.Add(new SteamAudio.Triangle { index0 = start, index1 = start + 1 + i, index2 = start + 1 + (i + 1) % c.Length });
                        _m.Add(0);
                    }
                }
            }
            if (_t.Count == 0) return;
            var mesh = Make(new[] { Drywall, Carpet, CeilingTile, Generic, DoorWood });
            if (mesh == null) return;
            _walls[k] = (mesh, _t.Count);
            Meshes++; Triangles += _t.Count;
        }

        void Add(Vector3 w) => _v.Add(new Vector3(w.x, w.y, -w.z));          // Steam Audio's frame (z flipped)

        void Update()
        {
            if (!Ready) return;
            if (_waiting.Count > 0)
            {
                foreach (var (c, go) in _waiting) if (go) Build(c, go);
                _waiting.Clear();
            }
            if (_waitingDoors.Count > 0)
            {
                foreach (var e in _waitingDoors) if (DoorEdges.IsShut(e.vertical, e.line, e.along, e.storey)) AddDoor(e);
                _waitingDoors.Clear();
            }
            if (_wallsWaiting.Count > 0)
            {
                foreach (var k in _wallsWaiting) BuildWall(k);
                _wallsWaiting.Clear();
            }
        }

        void OnChunkLoaded(Vector3Int coord, GameObject go)
        {
            if (Ready) Build(coord, go); else _waiting.Add((coord, go));
            // Broken walls on its edges (a saved world's) that have no mesh yet.
            foreach (var kv in Destruction.WallDamage.All)
                if ((kv.Key.Chunk(0) == coord || kv.Key.Chunk(1) == coord) && !_walls.ContainsKey(kv.Key)) OnWall(kv.Key);
        }

        void OnChunkUnloaded(Vector3Int coord, GameObject go)
        {
            // Broken walls with neither side loaded any more go with it.
            List<Destruction.WallKey> gone = null;
            foreach (var kv in _walls)
            {
                var k = kv.Key;
                if (k.Chunk(0) != coord && k.Chunk(1) != coord) continue;
                var other = k.Chunk(0) == coord ? k.Chunk(1) : k.Chunk(0);
                if (other == coord || !streamer || streamer.LayoutAt(other) == null) (gone ??= new List<Destruction.WallKey>()).Add(k);
            }
            if (gone != null) foreach (var k in gone) { var w = _walls[k]; _walls.Remove(k); Drop(w.mesh, w.tris); }
            if (!_chunks.TryGetValue(coord, out var m)) return;
            _chunks.Remove(coord);
            Drop(m.mesh, m.tris);
        }

        void OnDoor(DoorEdges.Edge e, bool shut)
        {
            var key = (e.vertical, e.line, e.along, e.storey);
            if (!shut)
            {
                if (_doors.TryGetValue(key, out var m)) { _doors.Remove(key); Drop(m, 12); }
                return;
            }
            if (Ready) AddDoor(e); else _waitingDoors.Add(e);
        }

        // ---------------- Geometry ----------------

        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<SteamAudio.Triangle> _t = new List<SteamAudio.Triangle>();
        readonly List<int> _m = new List<int>();

        void Build(Vector3Int coord, GameObject go)
        {
            if (_chunks.ContainsKey(coord)) OnChunkUnloaded(coord, go);
            _v.Clear(); _t.Clear(); _m.Clear();
            foreach (var b in go.GetComponents<BoxCollider>())
            {
                if (!b.enabled || b.isTrigger) continue;
                Vector3 size = b.size;
                int mat;
                bool flat = size.y < 0.6f && size.x > 0.5f && size.z > 0.5f;
                float y = b.center.y;
                if (flat && y < 0.2f) mat = 1;                                      // floor slab
                else if (flat) mat = 2;                                             // ceiling
                else if (Mathf.Min(size.x, size.z) <= 0.25f) mat = 0;               // walls, posts, headers
                else mat = 3;
                Box(b.transform, b.center, size, mat);
            }
            if (_t.Count == 0) return;
            var mats = new[] { Drywall, Carpet, CeilingTile, Generic, DoorWood };
            var mesh = Make(mats);
            if (mesh == null) return;
            _chunks[coord] = (mesh, _t.Count);
            Meshes++; Triangles += _t.Count;
        }

        void AddDoor(DoorEdges.Edge e)
        {
            var key = (e.vertical, e.line, e.along, e.storey);
            if (_doors.ContainsKey(key)) return;
            _v.Clear(); _t.Clear(); _m.Clear();
            // The slab in its doorway: 0.96 m wide, 2.05 m high, 4 cm thick, on the cell edge.
            Vector3 c = e.centre; c.y = c.y - 1.05f + 1.03f;
            Vector3 size = e.vertical ? new Vector3(0.04f, 2.05f, 0.96f) : new Vector3(0.96f, 2.05f, 0.04f);
            Box(null, c, size, 4);
            var mesh = Make(new[] { Drywall, Carpet, CeilingTile, Generic, DoorWood });
            if (mesh == null) return;
            _doors[key] = mesh;
            Meshes++; Triangles += 12;
        }

        static readonly int[] Quads = { 0, 1, 3, 2, 4, 6, 7, 5, 0, 4, 5, 1, 2, 3, 7, 6, 0, 2, 6, 4, 1, 5, 7, 3 };

        void Box(Transform t, Vector3 centre, Vector3 size, int mat)
        {
            int start = _v.Count;
            for (int k = 0; k < 8; k++)
            {
                var corner = new Vector3((k & 1) != 0 ? 0.5f : -0.5f, (k & 2) != 0 ? 0.5f : -0.5f, (k & 4) != 0 ? 0.5f : -0.5f);
                Vector3 local = centre + Vector3.Scale(size, corner);
                Vector3 w = t ? t.TransformPoint(local) : local;
                _v.Add(new Vector3(w.x, w.y, -w.z));                                // Steam Audio's frame (z flipped)
            }
            for (int q = 0; q < 24; q += 4)
            {
                _t.Add(new SteamAudio.Triangle { index0 = start + Quads[q], index1 = start + Quads[q + 1], index2 = start + Quads[q + 2] });
                _t.Add(new SteamAudio.Triangle { index0 = start + Quads[q], index1 = start + Quads[q + 2], index2 = start + Quads[q + 3] });
                _m.Add(mat); _m.Add(mat);
            }
        }

        SteamAudio.StaticMesh Make(SteamAudio.Material[] mats)
        {
            try
            {
                var verts = new SteamAudio.Vector3[_v.Count];
                for (int i = 0; i < _v.Count; i++) verts[i] = new SteamAudio.Vector3 { x = _v[i].x, y = _v[i].y, z = _v[i].z };
                var scene = SteamAudio.SteamAudioManager.CurrentScene;
                var mesh = new SteamAudio.StaticMesh(SteamAudio.SteamAudioManager.Context, scene, verts, _t.ToArray(), _m.ToArray(), mats);
                mesh.AddToScene(scene);
                SteamAudio.SteamAudioManager.ScheduleCommitScene();
                return mesh;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Backrooms] Steam Audio geometry failed: {ex.Message}");
                return null;
            }
        }

        void Drop(SteamAudio.StaticMesh mesh, int tris)
        {
            if (mesh == null) return;
            if (Ready) { mesh.RemoveFromScene(SteamAudio.SteamAudioManager.CurrentScene); SteamAudio.SteamAudioManager.ScheduleCommitScene(); }
            mesh.Release();
            Meshes--; Triangles -= tris;
        }

        void ReleaseAll()
        {
            foreach (var m in _chunks.Values) Drop(m.mesh, m.tris);
            foreach (var m in _doors.Values) Drop(m, 12);
            foreach (var m in _walls.Values) Drop(m.mesh, m.tris);
            _chunks.Clear(); _doors.Clear(); _walls.Clear();
        }

        // ---------------- Sources ----------------

        /// <summary>
        /// A sound of a thing in the world goes through Steam Audio: HRTF, occluded by walls (Steam Audio's geometry) and
        /// passing through them muffled. Distance stays Unity's rolloff.
        /// </summary>
        public static void Attach(AudioSource src)
        {
            if (!src || !Application.isPlaying || string.IsNullOrEmpty(AudioSettings.GetSpatializerPluginName())) return;
            if (src.GetComponent<SteamAudio.SteamAudioSource>()) return;
            src.spatialize = true;
            src.spatializePostEffects = true;
            var s = src.gameObject.AddComponent<SteamAudio.SteamAudioSource>();
            s.directBinaural = true;
            s.distanceAttenuation = false;
            s.occlusion = true;
            s.occlusionInput = SteamAudio.OcclusionInput.SimulationDefined;
            s.occlusionType = SteamAudio.OcclusionType.Volumetric;
            s.occlusionRadius = 0.15f;
            s.occlusionSamples = 8;
            s.transmission = true;
            s.transmissionType = SteamAudio.TransmissionType.FrequencyDependent;
            s.transmissionInput = SteamAudio.TransmissionInput.SimulationDefined;
            s.maxTransmissionSurfaces = 2;
        }
    }
}
#endif
