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
        [Tooltip("World seed when no save is being played (build mode's room test). 0 = random each session.")]
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
        public int QueuedChunks => _queue.Count + _pending.Count + (_upload != null ? 1 : 0);
        public float LastBuildMs { get; private set; }
        public float AverageBuildMs { get; private set; }
        public int ChunksBuilt { get; private set; }
        public float LastUploadMs { get; private set; }
        public float AverageUploadMs { get; private set; }
        readonly List<Vector3Int> _done = new List<Vector3Int>(4);
        IEnumerator<GameObject> _upload;
        OfficeChunkBuilder _uploadBuilder;
        GameObject _uploadRoot;
        Vector3Int _uploadKey;
        int _uploadGeneration;
        float _uploadMs;

        class Chunk { public List<Doorway> doors; public GameObject go; public List<LightSource> lights; public List<Renderer> lenses, faulty; public int generation; public OfficeLayout layout; }

        readonly Dictionary<Vector3Int, Chunk> _chunks = new Dictionary<Vector3Int, Chunk>();

        // ---------------- Sites: small areas kept loaded away from the player ----------------
        // A crawl hole can lead far beyond the load radius (another zone of the world). Only a small patch around its
        // partner is loaded: the chunks within a few metres of it, built like any other chunk but after the player's own
        // rings. Nothing between here and there is loaded.
        readonly Dictionary<object, (Vector3 point, float radius)> _sites = new Dictionary<object, (Vector3, float)>();
        bool _sitesChanged;

        /// <summary>Keeps the chunks within radius of a point loaded (ground storey and the storeys above) until cleared.</summary>
        public void SetSite(object owner, Vector3 point, float radius)
        {
            if (_sites.TryGetValue(owner, out var old) && (old.point - point).sqrMagnitude < 0.01f && Mathf.Abs(old.radius - radius) < 0.01f) return;
            _sites[owner] = (point, radius);
            _sitesChanged = true;
        }

        public void ClearSite(object owner) { if (_sites.Remove(owner)) _sitesChanged = true; }
        public int SiteCount => _sites.Count;

        bool InSite(Vector2Int column)
        {
            if (_sites.Count == 0) return false;
            float S = OfficeBiome.ChunkSize;
            foreach (var site in _sites.Values)
            {
                float dx = Mathf.Max(column.x * S - site.point.x, 0f, site.point.x - (column.x + 1) * S);
                float dz = Mathf.Max(column.y * S - site.point.z, 0f, site.point.z - (column.y + 1) * S);
                if (dx * dx + dz * dz <= site.radius * site.radius) return true;
            }
            return false;
        }

        /// <summary>Should this chunk column be loaded (near the player or in a site)?</summary>
        bool Wanted(Vector2Int column, int radius) => InRadius(column, _center, radius) || InSite(column);

        /// <summary>Is every chunk of the site around this point loaded?</summary>
        public bool SiteReady(Vector3 point, float radius)
        {
            float S = OfficeBiome.ChunkSize;
            int x0 = Mathf.FloorToInt((point.x - radius) / S), x1 = Mathf.FloorToInt((point.x + radius) / S);
            int z0 = Mathf.FloorToInt((point.z - radius) / S), z1 = Mathf.FloorToInt((point.z + radius) / S);
            for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                float dx = Mathf.Max(x * S - point.x, 0f, point.x - (x + 1) * S), dz = Mathf.Max(z * S - point.z, 0f, point.z - (z + 1) * S);
                if (dx * dx + dz * dz > radius * radius) continue;
                if (!_chunks.ContainsKey(Key(new Vector2Int(x, z), 0))) return false;
            }
            return true;
        }
        readonly List<Vector3Int> _queue = new List<Vector3Int>();
        readonly Dictionary<Vector3Int, Task<OfficeChunkBuilder>> _pending = new Dictionary<Vector3Int, Task<OfficeChunkBuilder>>();
        readonly Dictionary<Vector3Int, int> _pendingGen = new Dictionary<Vector3Int, int>();
        Vector2Int _center = new Vector2Int(int.MinValue, int.MinValue);

        Saves.WorldSaver _saver;
        /// <summary>Showing the main menu (a fixed world behind it, no player).</summary>
        public bool MenuMode { get; private set; }

        void Awake()
        {
            // The main menu's world, a save's world, or (testing a room from build mode) a random one.
            bool playing = Saves.GameSession.Slot >= 0 && Saves.GameSession.Data != null;
            MenuMode = !playing && Saves.GameSession.InMenu;
            if (MenuMode) Seed = Menu.MainMenu.MenuSeed;
            else if (playing) Seed = Saves.GameSession.Data.seed;
            else Seed = seed != 0 ? seed : new System.Random().Next(1, int.MaxValue);
            Backrooms.CustomRooms.RoomLibrary.Reload();        // the player's custom rooms join the landmark pool
            Settings = WorldGenSettings.Load();
            _world = new WorldGenerator(Seed, Settings);
            if (!GetComponent<RuntimeIndoorReflections>()) gameObject.AddComponent<RuntimeIndoorReflections>();
            // The Poolrooms: materials before any chunk data is prepared (worker threads only read them), and the map,
            // fog and ripples.
            PoolMaterials.Ensure();
            if (!GetComponent<PoolWorld>()) gameObject.AddComponent<PoolWorld>().streamer = this;
            // Developer menu: items and furniture (L).
            if (!GetComponent<Interaction.ItemsMenu>()) gameObject.AddComponent<Interaction.ItemsMenu>().streamer = this;
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
            // The save being played: moved props are back on the list before any chunk is built. Or the main menu.
            if (playing) { _saver = gameObject.AddComponent<Saves.WorldSaver>(); _saver.RestoreProps(_world); }
            else if (MenuMode) gameObject.AddComponent<Menu.MainMenu>().streamer = this;
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
            CancelUpload();
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
            // Sites, after everything around the player.
            float S = OfficeBiome.ChunkSize;
            foreach (var site in _sites.Values)
            {
                int x0 = Mathf.FloorToInt((site.point.x - site.radius) / S), x1 = Mathf.FloorToInt((site.point.x + site.radius) / S);
                int z0 = Mathf.FloorToInt((site.point.z - site.radius) / S), z1 = Mathf.FloorToInt((site.point.z + site.radius) / S);
                for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    var c = new Vector2Int(x, z);
                    if (InRadius(c, _center, loadRadius) || !InSite(c)) continue;
                    int top = _world.TopStoreyAt(c);
                    for (int s = 0; s <= top; s++) yield return (Key(c, s), loadRadius + 1);
                }
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
            // GeneratedChunkResources releases owned meshes; movable props own their own runtime meshes.
            Destroy(ch.go);
        }

        void Start()
        {
            // Build everything the player could see before the first frame (where the save left them, or the spawn).
            if (!(_saver && _saver.RestorePlayer(player))) PlaceSpawn();
            Refresh(true);
            if (_saver) _saver.RestoreWorld();
        }

        void Update()
        {
            if (!player) return;
            var c = ChunkOf(player.position);
            if (c != _center || _sitesChanged) { _sitesChanged = false; Refresh(false); }

            // Layout and vertex data are prepared on worker threads (a few at a time, nearest first).
            while (_pending.Count < 4 && _queue.Count > 0)
            {
                var coord = _queue[0];
                _queue.RemoveAt(0);
                bool current = _chunks.TryGetValue(coord, out var existing) && existing.generation == _generation;
                if (current || _pending.ContainsKey(coord) || !Wanted(Column(coord), loadRadius)) continue;
                var a = assets; var w = _world;
                _pendingGen[coord] = _generation;
                _pending[coord] = Task.Run(() => Prepare(w, coord, a));
            }

            float start = Time.realtimeSinceStartup;
            if (_upload != null) AdvanceUpload(start);
            if (_upload != null || (Time.realtimeSinceStartup - start) * 1000f >= frameBudgetMs) return;
            // Finished data becomes meshes on the main thread, yielding between individual uploads.
            if (_pending.Count == 0) return;
            var done = _done; done.Clear();
            foreach (var kv in _pending)
            {
                if (!kv.Value.IsCompleted) continue;
                done.Add(kv.Key);
                if (kv.Value.IsFaulted) { Debug.LogException(kv.Value.Exception); continue; }
                int gen = _pendingGen.TryGetValue(kv.Key, out int g) ? g : -1;
                if (gen != _generation) { _queue.Insert(0, kv.Key); continue; }       // settings changed meanwhile
                if (!Wanted(Column(kv.Key), unloadRadius)) continue;
                if (_chunks.TryGetValue(kv.Key, out var old) && old.generation == _generation) continue;
                _uploadKey = kv.Key; _uploadBuilder = kv.Value.Result; _uploadGeneration = _generation; _uploadMs = 0f;
                _upload = _uploadBuilder.InstantiateSteps(transform);
                AdvanceUpload(start);
                break;
            }
            foreach (var k in done) { _pending.Remove(k); _pendingGen.Remove(k); }
        }

        void AdvanceUpload(float frameStart)
        {
            if (_uploadGeneration != _generation || !Wanted(Column(_uploadKey), unloadRadius))
            { var key = _uploadKey; CancelUpload(); if (Wanted(Column(key), loadRadius)) _queue.Add(key); return; }
            float start = Time.realtimeSinceStartup;
            try
            {
                while (_upload.MoveNext())
                {
                    _uploadRoot = _upload.Current;
                    if ((Time.realtimeSinceStartup - frameStart) * 1000f >= frameBudgetMs) return;
                }
                if (_chunks.ContainsKey(_uploadKey)) DestroyNow(_uploadKey);
                Instantiate(_uploadKey, _uploadBuilder, _started, _uploadRoot);
                _uploadRoot = null; _upload.Dispose(); _upload = null;
            }
            catch (System.Exception e) { Debug.LogException(e); CancelUpload(); }
            finally
            {
                _uploadMs += (Time.realtimeSinceStartup - start) * 1000f;
                RecordUpload(_uploadMs, _upload == null);
            }
        }

        void RecordUpload(float ms, bool complete = true)
        {
            LastUploadMs = ms;
            if (complete) AverageUploadMs = Mathf.Lerp(AverageUploadMs <= 0f ? ms : AverageUploadMs, ms, 0.1f);
        }

        void CancelUpload()
        {
            _upload?.Dispose(); _upload = null;
            if (_uploadRoot) Destroy(_uploadRoot);
            _uploadRoot = null;
        }
        void OnDestroy() { CancelUpload(); }

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
            if (_world.CachedChunks >= 300 || _world.CachedLandmarks > 512) _world.Trim(_center, unloadRadius + 2);
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
                if (!Wanted(Column(kv.Key), unloadRadius) || (kv.Key.y > 0 && kv.Key.y > _world.TopStoreyAt(Column(kv.Key)))) drop.Add(kv.Key);
            foreach (var k in drop) UnloadChunk(k);

            // Queue missing chunks nearest first; only initial coverage is built synchronously.
            _queue.Clear();
            foreach (var (c, r) in Wanted())
            {
                if (_chunks.TryGetValue(c, out var have))
                {
                    if (have.generation != _generation && !_pending.ContainsKey(c)) _queue.Add(c);   // stale after a settings change
                    continue;
                }
                if (_upload != null && c == _uploadKey) continue;
                if (immediate) { _pending.Remove(c); _pendingGen.Remove(c); _syncBuild.Add(c); }
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

        void Instantiate(Vector3Int coord, OfficeChunkBuilder built, bool animate, GameObject ready = null)
        {
            ChunksBuilt++;
            // A chunk still stuttering off from an earlier unload is simply kept.
            if (_dying.TryGetValue(coord, out var old))
            {
                if (ready) Destroy(ready);
                _dying.Remove(coord);
                _chunks[coord] = old;
                if (lightManager) { lightManager.Register(coord, old.lights, old.lenses, old.faulty, false); lightManager.RegisterDoorways(coord, old.doors); }
                return;
            }
            var uploadTimer = ready ? null : System.Diagnostics.Stopwatch.StartNew();
            var go = ready ? ready : built.Instantiate(transform);
            var ch = new Chunk { go = go, lights = new List<LightSource>(built.lights),
                lenses = new List<Renderer>(built.litLensRenderers), faulty = new List<Renderer>(built.faultyRenderers),
                generation = _generation, layout = built.Layout, doors = new List<Doorway>(built.doorways) };
            _chunks[coord] = ch;
            // The building is already lit: streamed-in areas are simply on (a strike-on flicker read as
            // distant lights popping in). "animate" is kept for a future power-restoring event.
            if (lightManager) lightManager.Register(coord, ch.lights, ch.lenses, ch.faulty, animate && strikeOnStream);
            if (lightManager) lightManager.RegisterDoorways(coord, ch.doors);
            ChunkLoaded?.Invoke(coord, go);
            if (uploadTimer != null) RecordUpload((float)uploadTimer.Elapsed.TotalMilliseconds);
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
