using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Keeps a square of chunks generated around the player. New chunks are only ever created at the
    /// outer ring, at least two full chunks (~29 m) away and beyond the fog, so the player never sees
    /// geometry appear. Work is spread over frames with a time budget; distant chunks are unloaded.
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

        public int Seed { get; private set; }
        public int LoadedChunks => _chunks.Count;

        class Chunk { public GameObject go; public List<LightSource> lights; }

        readonly Dictionary<Vector2Int, Chunk> _chunks = new Dictionary<Vector2Int, Chunk>();
        readonly List<Vector2Int> _queue = new List<Vector2Int>();
        readonly Dictionary<Vector2Int, Task<OfficeChunkBuilder>> _pending = new Dictionary<Vector2Int, Task<OfficeChunkBuilder>>();
        Vector2Int _center = new Vector2Int(int.MinValue, int.MinValue);

        void Awake()
        {
            Seed = seed != 0 ? seed : new System.Random().Next(1, int.MaxValue);
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
            while (_pending.Count < 3 && _queue.Count > 0)
            {
                var coord = _queue[0];
                _queue.RemoveAt(0);
                if (_chunks.ContainsKey(coord) || _pending.ContainsKey(coord) || !InRadius(coord, _center, loadRadius)) continue;
                int s = Seed; var a = assets;
                _pending[coord] = Task.Run(() => Prepare(s, coord, a));
            }

            // Finished data becomes meshes on the main thread, within the frame budget.
            if (_pending.Count == 0) return;
            float start = Time.realtimeSinceStartup;
            var done = new List<Vector2Int>();
            foreach (var kv in _pending)
            {
                if (!kv.Value.IsCompleted) continue;
                done.Add(kv.Key);
                if (kv.Value.IsFaulted) { Debug.LogException(kv.Value.Exception); continue; }
                if (!_chunks.ContainsKey(kv.Key) && InRadius(kv.Key, _center, unloadRadius)) Instantiate(kv.Key, kv.Value.Result);
                if ((Time.realtimeSinceStartup - start) * 1000f > frameBudgetMs) break;
            }
            foreach (var k in done) _pending.Remove(k);
        }

        static OfficeChunkBuilder Prepare(int seed, Vector2Int coord, OfficeBiomeAssets assets)
        {
            var b = new OfficeChunkBuilder(assets);
            b.BuildData(OfficeLayoutGenerator.Generate(seed, coord, OfficeBiome.ChunkCells));
            return b;
        }

        public static Vector2Int ChunkOf(Vector3 world) =>
            new Vector2Int(Mathf.FloorToInt(world.x / OfficeBiome.ChunkSize), Mathf.FloorToInt(world.z / OfficeBiome.ChunkSize));

        static bool InRadius(Vector2Int a, Vector2Int b, int r) => Mathf.Abs(a.x - b.x) <= r && Mathf.Abs(a.y - b.y) <= r;

        void Refresh(bool immediate)
        {
            _center = ChunkOf(player.position);

            // Unload far chunks.
            var drop = new List<Vector2Int>();
            foreach (var kv in _chunks) if (!InRadius(kv.Key, _center, unloadRadius)) drop.Add(kv.Key);
            foreach (var k in drop) UnloadChunk(k);

            // Queue missing chunks nearest first; the inner rings are always built synchronously.
            _queue.Clear();
            for (int r = 0; r <= loadRadius; r++)
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                var c = new Vector2Int(_center.x + dx, _center.y + dz);
                if (_chunks.ContainsKey(c)) continue;
                if (immediate || r <= 1) { _pending.Remove(c); Instantiate(c, Prepare(Seed, c, assets)); }
                else if (!_pending.ContainsKey(c)) _queue.Add(c);
            }
        }

        void Instantiate(Vector2Int coord, OfficeChunkBuilder built)
        {
            var go = built.Instantiate(transform);
            var lights = new List<LightSource>(built.lights);
            _chunks[coord] = new Chunk { go = go, lights = lights };
            if (lightManager) lightManager.Register(coord, lights);
        }

        void UnloadChunk(Vector2Int coord)
        {
            if (!_chunks.TryGetValue(coord, out var ch)) return;
            if (lightManager) lightManager.Unregister(coord);
            foreach (var mf in ch.go.GetComponentsInChildren<MeshFilter>()) Destroy(mf.sharedMesh);
            Destroy(ch.go);
            _chunks.Remove(coord);
        }

        /// <summary>Puts the player in a lit, walkable cell of chunk (0, 0), facing the most open direction.</summary>
        void PlaceSpawn()
        {
            if (!player) return;
            var layout = OfficeLayoutGenerator.Generate(Seed, Vector2Int.zero, OfficeBiome.ChunkCells);
            float C = OfficeBiome.CellSize;
            Vector2Int best = new Vector2Int(layout.N / 2, layout.N / 2);
            float bestScore = float.MinValue;
            var lit = new HashSet<Vector2Int>();
            foreach (var f in layout.fixtures) if (f.on) lit.Add(f.cell);
            for (int x = 1; x < layout.N - 1; x++)
            for (int z = 1; z < layout.N - 1; z++)
            {
                var room = layout.RoomAt(x, z);
                if (!room.Walkable || room.kind == RoomKind.Conference || room.kind == RoomKind.Lounge) continue;
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
