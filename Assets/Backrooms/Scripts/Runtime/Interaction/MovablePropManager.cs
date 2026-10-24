using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Remembers every prop that has been moved, on top of the generated world (the world itself always
    /// comes from the seed; this is the list of changes).
    ///  - A moved prop leaves its chunk: the chunk that generated it no longer spawns it, and it lives
    ///    under this object instead.
    ///  - When the chunk it now stands in unloads, its pose is stored and the object destroyed; when that
    ///    chunk loads again, it is rebuilt there.
    /// Records are per world (seed + settings). Saving them to disk is not done yet; they last a session.
    /// The same records are meant for anything else that moves things while you are away (creatures).
    /// </summary>
    public class MovablePropManager : MonoBehaviour
    {
        public static MovablePropManager Instance { get; private set; }
        public WorldStreamer streamer;

        class Record
        {
            public MovableSpec spec;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3Int chunk;
            public MovableProp live;
        }

        readonly Dictionary<ulong, Record> _moved = new Dictionary<ulong, Record>();
        WorldGenerator _world;

        public int MovedCount => _moved.Count;
        public bool IsMoved(ulong id) => _moved.ContainsKey(id);

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnEnable()
        {
            Instance = this;
            if (!streamer) streamer = FindAnyObjectByType<WorldStreamer>();
            if (streamer) { streamer.ChunkLoaded += OnChunkLoaded; streamer.ChunkUnloaded += OnChunkUnloaded; }
        }

        void OnDisable()
        {
            if (streamer) { streamer.ChunkLoaded -= OnChunkLoaded; streamer.ChunkUnloaded -= OnChunkUnloaded; }
        }

        void Update()
        {
            // A new seed or settings is a new world: its props have never been touched.
            if (streamer && streamer.World != _world)
            {
                _world = streamer.World;
                foreach (var r in _moved.Values) if (r.live) Destroy(r.live.gameObject);
                _moved.Clear();
            }
        }

        public Vector3Int ChunkKey(Vector3 p)
        {
            var c = WorldStreamer.ChunkOf(p);
            float pitch = streamer && streamer.Settings != null ? streamer.Settings.storeyPitch : 9f;
            int storey = Mathf.Max(0, Mathf.FloorToInt((p.y + 0.5f) / pitch));
            return new Vector3Int(c.x, storey, c.y);
        }

        /// <summary>The prop has been disturbed: it now belongs to the moved list and to no chunk.</summary>
        public void Detach(MovableProp p)
        {
            if (_moved.ContainsKey(p.Spec.id)) return;
            p.transform.SetParent(transform, true);
            _moved[p.Spec.id] = new Record { spec = p.Spec, live = p };
            Store(p);
        }

        /// <summary>Saves where the prop is now.</summary>
        public void Store(MovableProp p)
        {
            if (!_moved.TryGetValue(p.Spec.id, out var r)) return;
            r.position = p.transform.position;
            r.rotation = p.transform.rotation;
            r.chunk = ChunkKey(r.position);
        }

        void OnChunkLoaded(Vector3Int coord, GameObject go)
        {
            foreach (var r in _moved.Values)
                if (!r.live && r.chunk == coord)
                    r.live = MovableProp.Spawn(r.spec, transform, r.position, r.rotation);
        }

        void OnChunkUnloaded(Vector3Int coord, GameObject go)
        {
            foreach (var r in _moved.Values)
            {
                if (!r.live || r.live.held) continue;
                var p = r.live;
                if (ChunkKey(p.transform.position) != coord) continue;
                p.Freeze();
                Store(p);
                Destroy(p.gameObject);
                r.live = null;
            }
        }
    }
}
