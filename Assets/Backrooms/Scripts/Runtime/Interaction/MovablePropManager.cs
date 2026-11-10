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
    /// Records are per world (seed + settings); the world's save keeps them (WorldSaver: <see cref="Export"/>, <see cref="Import"/>).
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

        /// <summary>A moved prop as it goes into a save.</summary>
        public struct Saved { public MovableSpec spec; public Vector3 position; public Quaternion rotation; }

        /// <summary>Every moved prop, where it is now.</summary>
        public List<Saved> Export()
        {
            var list = new List<Saved>(_moved.Count);
            foreach (var r in _moved.Values)
            {
                if (r.live) Store(r.live);
                list.Add(new Saved { spec = r.spec, position = r.position, rotation = r.rotation });
            }
            return list;
        }

        /// <summary>Moved props from a save, for this world. Call before its chunks are built: each one appears when the
        /// chunk it stands in loads, and the chunk that generated it leaves it out.</summary>
        public void Import(WorldGenerator world, List<Saved> list)
        {
            _world = world;
            foreach (var s in list)
            {
                if (s.spec == null || _moved.ContainsKey(s.spec.id)) continue;
                _moved[s.spec.id] = new Record { spec = s.spec, position = s.position, rotation = s.rotation, chunk = ChunkKey(s.position) };
            }
        }
        public bool IsMoved(ulong id) => _moved.ContainsKey(id);

        /// <summary>Where a moved prop is now (live or as last stored), and whether someone is carrying it.</summary>
        public bool Where(ulong id, out Vector3 position, out bool held)
        {
            position = default; held = false;
            if (!_moved.TryGetValue(id, out var r)) return false;
            if (r.live) { position = r.live.transform.position; held = r.live.held; }
            else position = r.position;
            return true;
        }

        /// <summary>Where a moved prop is now and how it is turned (live or as last stored).</summary>
        public bool Pose(ulong id, out Vector3 position, out Quaternion rotation)
        {
            position = default; rotation = Quaternion.identity;
            if (!_moved.TryGetValue(id, out var r)) return false;
            if (r.live) { position = r.live.transform.position; rotation = r.live.transform.rotation; }
            else { position = r.position; rotation = r.rotation; }
            return true;
        }

        /// <summary>A moved prop's spec (what it is), or null.</summary>
        public MovableSpec SpecOf(ulong id) => _moved.TryGetValue(id, out var r) ? r.spec : null;

        /// <summary>Gone for good (a contract's box sent through a seam, or its floor given up): out of the world and the save.</summary>
        public void Forget(ulong id)
        {
            // (Co-op: taken out of every player's world. Not while a change from the network is being applied.)
            if (Net.NetHooks.On) Net.NetHooks.PropForgot?.Invoke(id);
            if (!_moved.TryGetValue(id, out var r)) return;
            if (r.live) Destroy(r.live.gameObject);
            _moved.Remove(id);
        }

        /// <summary>
        /// The game itself puts a prop into the world (a lot stacked behind a seam, a kit delivered, a shelf put up): here,
        /// and in co-op in every player's game. (NetPlace alone only records it here: what a change from the network uses.)
        /// </summary>
        public void Place(MovableSpec spec, Vector3 position, Quaternion rotation)
        {
            if (spec == null) return;
            NetPlace(spec, position, rotation);
            if (Net.NetHooks.On) Net.NetHooks.PropPlaced?.Invoke(spec, position, rotation);
        }

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

        /// <summary>
        /// Co-op (2026-11-04): another player moved this prop (it is not in this game's world now, or not yet moved here):
        /// its record, and the prop itself if its chunk is loaded.
        /// </summary>
        public void NetPlace(MovableSpec spec, Vector3 position, Quaternion rotation)
        {
            if (spec == null) return;
            var live = MovableProp.Find(spec.id);
            if (live)
            {
                Detach(live);
                live.transform.SetPositionAndRotation(position, rotation);
                Store(live);
                return;
            }
            if (!_moved.TryGetValue(spec.id, out var r)) _moved[spec.id] = r = new Record { spec = spec };
            r.position = position; r.rotation = rotation; r.chunk = ChunkKey(position);
            if (r.live) { r.live.transform.SetPositionAndRotation(position, rotation); return; }
            if (streamer && streamer.LayoutAt(r.chunk) != null) r.live = MovableProp.Spawn(r.spec, transform, position, rotation);
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
