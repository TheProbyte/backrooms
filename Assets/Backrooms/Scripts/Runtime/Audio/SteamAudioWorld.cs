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
            if (streamer) { streamer.ChunkLoaded += OnChunkLoaded; streamer.ChunkUnloaded += OnChunkUnloaded; }
            DoorEdges.Changed += OnDoor;
        }

        void OnDisable()
        {
            if (streamer) { streamer.ChunkLoaded -= OnChunkLoaded; streamer.ChunkUnloaded -= OnChunkUnloaded; }
            DoorEdges.Changed -= OnDoor;
            ReleaseAll();
        }

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
        }

        void OnChunkLoaded(Vector3Int coord, GameObject go)
        {
            if (Ready) Build(coord, go); else _waiting.Add((coord, go));
        }

        void OnChunkUnloaded(Vector3Int coord, GameObject go)
        {
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
            _chunks.Clear(); _doors.Clear();
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
