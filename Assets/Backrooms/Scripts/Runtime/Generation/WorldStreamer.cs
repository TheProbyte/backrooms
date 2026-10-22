using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Keeps a square of chunks generated around the player. New chunks are only ever created at the
    /// outer ring, at least two full chunks (~29 m) away and beyond the fog, so the player never sees
    /// geometry appear. Work is spread over frames with a time budget; distant chunks are unloaded.
    ///
    /// Chunks are (x, storey, z). The ground storey loads everywhere in range; upper storeys only load
    /// in columns near a multi-storey volume (see <see cref="WorldGenerator.TopStoreyAt"/>), the only
    /// places they can be seen from. Chunks are slices of district plans (see <see cref="WorldGenerator"/>).
    /// </summary>
    public class WorldStreamer : MonoBehaviour
    {
        [Tooltip("World seed. 0 = random each session.")]
        public int seed = 0;
        [Tooltip("Chunks kept loaded in each direction around the player's chunk.")]
        [Range(2, 5)] public int loadRadius = 3;
        [Tooltip("Chunks beyond this distance are destroyed (must exceed load radius).")]
        [Range(3, 7)] public int unloadRadius = 4;
        [Tooltip("Milliseconds per frame spent building chunks while streaming.")]
        [Range(1f, 16f)] public float frameBudgetMs = 4f;

        public OfficeBiomeAssets assets;
        public Transform player;
        public FixtureLightManager lightManager;
        [Tooltip("Flicker newly streamed areas on (off: the world is already lit).")]
        public bool strikeOnStream = false;

        public int Seed { get; private set; }
        public int LoadedChunks => _chunks.Count;

        /// <summary>Raised after a chunk's GameObject is built (e.g. to register its geometry with an audio engine).</summary>
        public event System.Action<Vector3Int, GameObject> ChunkLoaded;
        /// <summary>Raised just before a chunk's GameObject is destroyed.</summary>
        public event System.Action<Vector3Int, GameObject> ChunkUnloaded;

        /// <summary>Live world-gen rules (edited by F5 > World Gen). Call <see cref="Regenerate"/> after changes.</summary>
        public WorldGenSettings Settings { get; private set; }
        WorldGenerator _world;               // immutable world (seed + frozen settings) handed to worker threads
        /// <summary>The world currently being streamed (layouts for any chunk, loaded or not).</summary>
        public WorldGenerator World => _world;
        int _generation;                     // bumps on every regenerate; stale chunks get rebuilt
        Vector2Int? _frozen;                 // chunk the player stood in when settings changed (kept as is)
        public Vector2Int? FrozenChunk => _frozen;

        // Stats for the World Gen tab.
        public int QueuedChunks => _queue.Count + _pending.Count;
        public float LastBuildMs { get; private set; }
        public float AverageBuildMs { get; private set; }
        public int ChunksBuilt { get; private set; }

        class Chunk { public List<Doorway> doors; public GameObject go; public List<LightSource> lights; public List<Renderer> lenses, faulty; public int generation; public OfficeLayout layout; }

        readonly Dictionary<Vector3Int, Chunk> _chunks = new Dictionary<Vector3Int, Chunk>();
        readonly List<Vector3Int> _queue = new List<Vector3Int>();
        readonly Dictionary<Vector3Int, Task<OfficeChunkBuilder>> _pending = new Dictionary<Vector3Int, Task<OfficeChunkBuilder>>();
        readonly Dictionary<Vector3Int, int> _pendingGen = new Dictionary<Vector3Int, int>();
        Vector2Int _center = new Vector2Int(int.MinValue, int.MinValue);

        void Awake()
        {
            Seed = seed != 0 ? seed : new System.Random().Next(1, int.MaxValue);
            Settings = WorldGenSettings.Load();
            _world = new WorldGenerator(Seed, Settings);
            // Movable props: the list of moved ones, and the player's free hand.
            if (!GetComponentInChildren<Interaction.MovablePropManager>())
            {
                var m = new GameObject("MovedProps").AddComponent<Interaction.MovablePropManager>();
                m.transform.SetParent(transform, false);
                m.streamer = this;
            }
            if (player && player.GetComponent<CamcorderPlayerController>() && !player.GetComponent<Interaction.PropGrabber>())
                player.gameObject.AddComponent<Interaction.PropGrabber>();
            if (player && player.GetComponent<CamcorderPlayerController>() && !player.GetComponent<Interaction.FirstPersonBody>())
                player.gameObject.AddComponent<Interaction.FirstPersonBody>();
        }

        /// <summary>The ground-storey chunk's layout as currently built (null if not loaded).</summary>
        public OfficeLayout LayoutAt(Vector2Int chunk) => LayoutAt(new Vector3Int(chunk.x, 0, chunk.y));

        /// <summary>The chunk's layout as currently built (null if not loaded). Coordinates are (x, storey, z).</summary>
        public OfficeLayout LayoutAt(Vector3Int chunk) => _chunks.TryGetValue(chunk, out var c) ? c.layout : null;

        /// <summary>
        /// Rebuilds every loaded chunk from the current <see cref="Settings"/>, except the chunk the player
        /// is standing in (so they are never dropped inside a wall). Chunks are swapped one by one as their
        /// replacements finish; district edges always line up because openings don't depend on settings.
        /// </summary>
        public void Regenerate()
        {
            _world = new WorldGenerator(Seed, Settings);
            _generation++;
            _frozen = player ? ChunkOf(player.position) : (Vector2Int?)null;
            if (_frozen.HasValue && _chunks.TryGetValue(Key(_frozen.Value, 0), out var keep)) keep.generation = _generation;
            Requeue();
        }

        /// <summary>New seed: a different world. Everything is rebuilt and the player is moved to the new spawn.</summary>
        public void SetSeed(int newSeed)
        {
            Seed = newSeed;
            _world = new WorldGenerator(Seed, Settings);
            _generation++;
            _frozen = null;
            foreach (var k in new List<Vector3Int>(_chunks.Keys)) DestroyNow(k);
            _pending.Clear(); _pendingGen.Clear(); _queue.Clear();
            PlaceSpawn();
            Refresh(true);
        }

        static Vector3Int Key(Vector2Int c, int storey) => new Vector3Int(c.x, storey, c.y);
        static Vector2Int Column(Vector3Int k) => new Vector2Int(k.x, k.z);

        /// <summary>Every chunk that should be loaded, nearest ring first (ground storey before upper storeys).</summary>
        IEnumerable<(Vector3Int key, int ring)> Wanted()
        {
            for (int r = 0; r <= loadRadius; r++)
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                var c = new Vector2Int(_center.x + dx, _center.y + dz);
                int top = _world.TopStoreyAt(c);
                for (int s = 0; s <= top; s++) yield return (Key(c, s), r);
            }
        }

        void Requeue()
        {
            _queue.Clear();
            foreach (var (k, _) in Wanted())
            {
                if (_chunks.TryGetValue(k, out var ch) && ch.generation == _generation) continue;
                _queue.Add(k);
            }
        }

        void DestroyNow(Vector3Int coord)
        {
            if (!_chunks.TryGetValue(coord, out var ch)) return;
            _chunks.Remove(coord);
            if (lightManager) { lightManager.Unregister(coord); lightManager.UnregisterDoorways(coord); }
            DestroyChunkObject(coord, ch);
        }

        void DestroyChunkObject(Vector3Int coord, Chunk ch)
        {
            ChunkUnloaded?.Invoke(coord, ch.go);
            // Only meshes generated at runtime (negative instance ids); prop meshes are shared assets.
#pragma warning disable CS0618
            foreach (var mf in ch.go.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh && mf.sharedMesh.GetInstanceID() < 0) Destroy(mf.sharedMesh);
#pragma warning restore CS0618
            Destroy(ch.go);
        }

        void Start()
        {
            // Build everything the player could see before the first frame.
            PlaceSpawn();
            Refresh(true);
        }

        void Update()
        {
            if (!player) return;
            var c = ChunkOf(player.position);
            if (c != _center) Refresh(false);

            // Layout and vertex data are prepared on worker threads (a few at a time, nearest first).
            while (_pending.Count < 4 && _queue.Count > 0)
            {
                var coord = _queue[0];
                _queue.RemoveAt(0);
                bool current = _chunks.TryGetValue(coord, out var existing) && existing.generation == _generation;
                if (current || _pending.ContainsKey(coord) || !InRadius(Column(coord), _center, loadRadius)) continue;
                var a = assets; var w = _world;
                _pendingGen[coord] = _generation;
                _pending[coord] = Task.Run(() => Prepare(w, coord, a));
            }

            // Finished data becomes meshes on the main thread, within the frame budget.
            if (_pending.Count == 0) return;
            float start = Time.realtimeSinceStartup;
            var done = new List<Vector3Int>();
            foreach (var kv in _pending)
            {
                if (!kv.Value.IsCompleted) continue;
                done.Add(kv.Key);
                if (kv.Value.IsFaulted) { Debug.LogException(kv.Value.Exception); continue; }
                int gen = _pendingGen.TryGetValue(kv.Key, out int g) ? g : -1;
                if (gen != _generation) { _queue.Insert(0, kv.Key); continue; }       // settings changed meanwhile
                if (!InRadius(Column(kv.Key), _center, unloadRadius)) continue;
                if (_chunks.TryGetValue(kv.Key, out var old))
                {
                    if (old.generation == _generation) continue;
                    DestroyNow(kv.Key);                                                  // live settings swap
                    Instantiate(kv.Key, kv.Value.Result, false);
                }
                else Instantiate(kv.Key, kv.Value.Result, _started);
                if ((Time.realtimeSinceStartup - start) * 1000f > frameBudgetMs) break;
            }
            foreach (var k in done) { _pending.Remove(k); _pendingGen.Remove(k); }
        }

        OfficeChunkBuilder Prepare(WorldGenerator world, Vector3Int coord, OfficeBiomeAssets assets)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var b = new OfficeChunkBuilder(assets);
            // Neighbouring layouts (deterministic, cached by the world) so borders and light agree.
            b.BuildData(world.Chunk(coord), world.Neighbours(coord));
            float ms = (float)sw.Elapsed.TotalMilliseconds;
            LastBuildMs = ms; AverageBuildMs = Mathf.Lerp(AverageBuildMs <= 0f ? ms : AverageBuildMs, ms, 0.1f);
            return b;
        }

        /// <summary>Drops cached layouts far from the player.</summary>
        void TrimLayoutCache()
        {
            if (_world.CachedChunks >= 300) _world.Trim(_center, unloadRadius + 2);
        }

        public static Vector2Int ChunkOf(Vector3 world) =>
            new Vector2Int(Mathf.FloorToInt(world.x / OfficeBiome.ChunkSize), Mathf.FloorToInt(world.z / OfficeBiome.ChunkSize));

        static bool InRadius(Vector2Int a, Vector2Int b, int r) => Mathf.Abs(a.x - b.x) <= r && Mathf.Abs(a.y - b.y) <= r;

        void Refresh(bool immediate)
        {
            _center = ChunkOf(player.position);

            // Unload far chunks, and upper storeys no longer near a volume.
            var drop = new List<Vector3Int>();
            foreach (var kv in _chunks)
                if (!InRadius(Column(kv.Key), _center, unloadRadius) || (kv.Key.y > 0 && kv.Key.y > _world.TopStoreyAt(Column(kv.Key)))) drop.Add(kv.Key);
            foreach (var k in drop) UnloadChunk(k);

            // Queue missing chunks nearest first; the inner rings are always built synchronously.
            _queue.Clear();
            foreach (var (c, r) in Wanted())
            {
                if (_chunks.TryGetValue(c, out var have))
                {
                    if (have.generation != _generation && !_pending.ContainsKey(c)) _queue.Add(c);   // stale after a settings change
                    continue;
                }
                if (immediate || r <= 1) { _pending.Remove(c); _pendingGen.Remove(c); _syncBuild.Add(c); }
                else if (!_pending.ContainsKey(c)) _queue.Add(c);
            }
            // Chunks needed right now are prepared in parallel on all cores, then instantiated in order.
            if (_syncBuild.Count > 0)
            {
                var built = new OfficeChunkBuilder[_syncBuild.Count];
                var a = assets; var w = _world;
                Parallel.For(0, _syncBuild.Count, i => built[i] = Prepare(w, _syncBuild[i], a));
                for (int i = 0; i < built.Length; i++) Instantiate(_syncBuild[i], built[i], _started);
                _syncBuild.Clear();
            }
            TrimLayoutCache();
        }

        readonly List<Vector3Int> _syncBuild = new List<Vector3Int>();

        void Instantiate(Vector3Int coord, OfficeChunkBuilder built, bool animate)
        {
            ChunksBuilt++;
            // A chunk still stuttering off from an earlier unload is simply kept.
            if (_dying.TryGetValue(coord, out var old))
            {
                _dying.Remove(coord);
                _chunks[coord] = old;
                if (lightManager) { lightManager.Register(coord, old.lights, old.lenses, old.faulty, false); lightManager.RegisterDoorways(coord, old.doors); }
                return;
            }
            var go = built.Instantiate(transform);
            var ch = new Chunk { go = go, lights = new List<LightSource>(built.lights),
                lenses = new List<Renderer>(built.litLensRenderers), faulty = new List<Renderer>(built.faultyRenderers),
                generation = _generation, layout = built.Layout, doors = new List<Doorway>(built.doorways) };
            _chunks[coord] = ch;
            // The building is already lit: streamed-in areas are simply on (a strike-on flicker read as
            // distant lights popping in). "animate" is kept for a future power-restoring event.
            if (lightManager) lightManager.Register(coord, ch.lights, ch.lenses, ch.faulty, animate && strikeOnStream);
            if (lightManager) lightManager.RegisterDoorways(coord, ch.doors);
            ChunkLoaded?.Invoke(coord, go);
        }

        readonly Dictionary<Vector3Int, Chunk> _dying = new Dictionary<Vector3Int, Chunk>();
        bool _started;

        void LateUpdate() => _started = true;

        void UnloadChunk(Vector3Int coord)
        {
            if (!_chunks.TryGetValue(coord, out var ch)) return;
            _chunks.Remove(coord);
            float delay = lightManager && strikeOnStream ? lightManager.BeginPowerDown(coord) : 0f;
            _dying[coord] = ch;
            StartCoroutine(DestroyLater(coord, ch, delay));
        }

        System.Collections.IEnumerator DestroyLater(Vector3Int coord, Chunk ch, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (!_dying.TryGetValue(coord, out var still) || still != ch) yield break;   // resurrected
            _dying.Remove(coord);
            if (lightManager) { lightManager.Unregister(coord); lightManager.UnregisterDoorways(coord); }
            DestroyChunkObject(coord, ch);
        }

        /// <summary>Puts the player in a lit, walkable cell of chunk (0, 0), facing the most open direction.</summary>
        void PlaceSpawn()
        {
            if (!player) return;
            var layout = _world.Chunk(Vector2Int.zero);
            float C = OfficeBiome.CellSize;
            Vector2Int best = new Vector2Int(layout.N / 2, layout.N / 2);
            float bestScore = float.MinValue;
            var lit = new HashSet<Vector2Int>();
            foreach (var f in layout.fixtures) if (f.on) lit.Add(f.cell);
            for (int x = 1; x < layout.N - 1; x++)
            for (int z = 1; z < layout.N - 1; z++)
            {
                var room = layout.RoomAt(x, z);
                if (!room.Walkable || room.noFloor || room.kind == RoomKind.Conference || room.kind == RoomKind.Lounge) continue;
                if (BlockedCell(layout, x, z)) continue;
                float score = room.rect.width * room.rect.height + (lit.Contains(new Vector2Int(x, z)) ? 40f : 0f)
                            - Vector2.Distance(new Vector2(x, z), room.rect.center) * 2f;
                if (score > bestScore) { bestScore = score; best = new Vector2Int(x, z); }
            }
            var room0 = layout.RoomAt(best.x, best.y);
            float yaw = room0.rect.width >= room0.rect.height ? 90f : 0f;
            var pos = new Vector3((best.x + 0.5f) * C, 0.05f, (best.y + 0.5f) * C);
            var ctrl = player.GetComponent<CamcorderPlayerController>();
            if (ctrl) ctrl.Teleport(pos, yaw); else player.position = pos;
        }

        static bool BlockedCell(OfficeLayout L, int x, int z)
        {
            // Never spawn inside or right next to furniture.
            foreach (var p in L.props)
                if (Mathf.Abs(p.position.x / OfficeBiome.CellSize - (x + 0.5f)) < 1.6f && Mathf.Abs(p.position.y / OfficeBiome.CellSize - (z + 0.5f)) < 1.6f) return true;
            foreach (var p in L.pillars) if (Mathf.Abs(p.vertex.x - (x + 0.5f)) < 1f && Mathf.Abs(p.vertex.y - (z + 0.5f)) < 1f) return true;
            foreach (var h in L.halfWalls)
            {
                if (h.alongX && (h.line == z || h.line == z + 1) && x >= h.start - 1 && x < h.start + h.length + 1) return true;
                if (!h.alongX && (h.line == x || h.line == x + 1) && z >= h.start - 1 && z < h.start + h.length + 1) return true;
            }
            return false;
        }
    }
}
