using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The one place every system gets layouts from (streamer, audio, debug tools, tests, the world map).
    /// An instance is an immutable world: a seed plus a frozen copy of the settings.
    ///
    /// Layers, each a pure function of the ones above (so any order, any thread, same result):
    /// zones and biomes (<see cref="WorldGenSettings"/>) -> landmarks (<see cref="Landmarks"/>) ->
    /// district plans (<see cref="DistrictGenerator"/>, 48 x 48 cells per storey) -> chunk slices
    /// (12 x 12 cells, what the streamer meshes). Chunk coordinates are (x, storey, z).
    /// </summary>
    public sealed class WorldGenerator
    {
        public readonly int Seed;
        public readonly WorldGenSettings Settings;

        const int N = OfficeBiome.ChunkCells, DC = WorldGenSettings.DistrictChunks;

        readonly ConcurrentDictionary<Vector3Int, Lazy<OfficeLayout>> _districts = new ConcurrentDictionary<Vector3Int, Lazy<OfficeLayout>>();
        readonly ConcurrentDictionary<Vector3Int, Lazy<OfficeLayout>> _chunks = new ConcurrentDictionary<Vector3Int, Lazy<OfficeLayout>>();
        readonly ConcurrentDictionary<Vector2Int, List<LandmarkPlacement>> _landmarks = new ConcurrentDictionary<Vector2Int, List<LandmarkPlacement>>();

        /// <param name="settings">Copied, so later edits to the original never change this world.</param>
        public WorldGenerator(int seed, WorldGenSettings settings)
        {
            Seed = seed;
            Settings = (settings ?? WorldGenSettings.Defaults()).Clone();
        }

        public WorldGenSettings.Sample Sample(Vector2Int chunk) => Settings.Evaluate(Seed, chunk);

        public static Vector2Int DistrictOf(Vector2Int chunk) => WorldGenSettings.DistrictOf(chunk);

        // ---------------- Districts ----------------

        /// <summary>Plan of district (x, storey, z) (thread-safe; built once per instance).</summary>
        public OfficeLayout District(Vector3Int d) =>
            _districts.GetOrAdd(d, k => new Lazy<OfficeLayout>(() => DistrictGenerator.Generate(Settings, Seed, new Vector2Int(k.x, k.z), k.y))).Value;

        // ---------------- Chunks ----------------

        /// <summary>Ground-storey chunk (x, z).</summary>
        public OfficeLayout Chunk(Vector2Int chunk) => Chunk(new Vector3Int(chunk.x, 0, chunk.y));

        /// <summary>Chunk (x, storey, z): a slice of its district's plan.</summary>
        public OfficeLayout Chunk(Vector3Int c) =>
            _chunks.GetOrAdd(c, k => new Lazy<OfficeLayout>(() => Slice(District(DistrictKey(k)), k))).Value;

        /// <summary>Builds a ground-storey chunk without touching any cache (tests compare this with cached results).</summary>
        public OfficeLayout Build(Vector2Int chunk)
        {
            var c = new Vector3Int(chunk.x, 0, chunk.y);
            var d = DistrictKey(c);
            return Slice(DistrictGenerator.Generate(Settings, Seed, new Vector2Int(d.x, d.z), d.y), c);
        }

        /// <summary>Stable prop id: district, storey and index in the district plan (unique per world).</summary>
        public static ulong PropId(Vector2Int district, int storey, int index)
        {
            unchecked
            {
                ulong h = (ulong)(uint)district.x * 0x9E3779B97F4A7C15ul;
                h ^= (ulong)(uint)district.y * 0xC2B2AE3D27D4EB4Ful + (h << 6) + (h >> 2);
                h ^= (ulong)(uint)storey * 0x165667B19E3779F9ul + (h << 6) + (h >> 2);
                return (h & ~0xFFFFul) | (uint)(index & 0xFFFF);
            }
        }

        static Vector3Int DistrictKey(Vector3Int chunk)
        {
            var d = DistrictOf(new Vector2Int(chunk.x, chunk.z));
            return new Vector3Int(d.x, chunk.y, d.y);
        }

        /// <summary>The 12 x 12 cells of one chunk, cut out of a district plan.</summary>
        public static OfficeLayout Slice(OfficeLayout plan, Vector3Int chunk)
        {
            int cx = chunk.x - plan.coord.x * DC, cz = chunk.z - plan.coord.y * DC;
            int ox = cx * N, oz = cz * N;
            float C = OfficeBiome.CellSize;
            var S = new OfficeLayout(N, new Vector2Int(chunk.x, chunk.z), plan.storey)
            {
                storeyPitch = plan.storeyPitch, zoneName = plan.zoneName,
                rules = plan.chunkRules != null ? plan.chunkRules[cx, cz] : plan.rules,
                biomeName = plan.chunkBiomes != null ? plan.chunkBiomes[cx, cz] : plan.biomeName,
            };
            var chunkRect = new RectInt(ox, oz, N, N);
            var map = new Dictionary<int, int>();
            for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                int pid = plan.roomOf[ox + x, oz + z];
                if (!map.TryGetValue(pid, out int sid))
                {
                    var pr = plan.rooms[pid];
                    var clip = Intersect(pr.rect, chunkRect);
                    var room = pr.CloneFor(S.rooms.Count, new RectInt(clip.x - ox, clip.y - oz, clip.width, clip.height));
                    room.globalId = pr.id;
                    sid = room.id; map[pid] = sid; S.rooms.Add(room);
                }
                S.roomOf[x, z] = sid;
                S.flags[x, z] = plan.flags[ox + x, oz + z];
            }
            for (int x = 0; x <= N; x++)
            for (int z = 0; z < N; z++) { S.vEdges[x, z] = plan.vEdges[ox + x, oz + z]; S.archV[x, z] = plan.archV[ox + x, oz + z]; }
            for (int x = 0; x < N; x++)
            for (int z = 0; z <= N; z++) { S.hEdges[x, z] = plan.hEdges[ox + x, oz + z]; S.archH[x, z] = plan.archH[ox + x, oz + z]; }

            bool Mine(int x, int z) => x >= ox && z >= oz && x < ox + N && z < oz + N;
            foreach (var f in plan.fixtures)
                if (Mine(f.cell.x, f.cell.y)) { var g = f; g.cell -= new Vector2Int(ox, oz); S.fixtures.Add(g); }
            foreach (var p in plan.pillars)
                if (Mine(p.vertex.x, p.vertex.y)) { var g = p; g.vertex -= new Vector2Int(ox, oz); S.pillars.Add(g); }
            foreach (var h in plan.halfWalls)
            {
                int sx = h.alongX ? h.start : h.line, sz = h.alongX ? h.line : h.start;
                if (!Mine(sx, sz)) continue;
                var g = h;
                if (h.alongX) { g.start -= ox; g.line -= oz; } else { g.start -= oz; g.line -= ox; }
                S.halfWalls.Add(g);
            }
            foreach (var p in plan.plates)
                if (Mine(p.cell.x, p.cell.y)) { var g = p; g.cell -= new Vector2Int(ox, oz); S.plates.Add(g); }
            for (int i = 0; i < plan.props.Count; i++)
            {
                var p = plan.props[i];
                int px = Mathf.FloorToInt(p.position.x / C), pz = Mathf.FloorToInt(p.position.y / C);
                if (!Mine(Mathf.Clamp(px, 0, plan.N - 1), Mathf.Clamp(pz, 0, plan.N - 1))) continue;
                var g = p; g.position -= new Vector2(ox * C, oz * C);
                g.id = PropId(plan.coord, plan.storey, i);
                S.props.Add(g);
            }
            foreach (var l in plan.landmarks)
                if (l.rect.Overlaps(chunkRect)) S.landmarks.Add(l);
            return S;
        }

        static RectInt Intersect(RectInt a, RectInt b)
        {
            int x0 = Mathf.Max(a.xMin, b.xMin), z0 = Mathf.Max(a.yMin, b.yMin);
            int x1 = Mathf.Min(a.xMax, b.xMax), z1 = Mathf.Min(a.yMax, b.yMax);
            return new RectInt(x0, z0, Mathf.Max(0, x1 - x0), Mathf.Max(0, z1 - z0));
        }

        /// <summary>The eight surrounding chunks on the same storey (the builder finds them by coordinate).</summary>
        public OfficeLayout[] Neighbours(Vector3Int c)
        {
            var nb = new OfficeLayout[8];
            int k = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                nb[k++] = Chunk(new Vector3Int(c.x + dx, c.y, c.z + dz));
            }
            return nb;
        }

        public OfficeLayout[] Neighbours(Vector2Int c) => Neighbours(new Vector3Int(c.x, 0, c.y));

        // ---------------- Landmarks and storeys ----------------

        /// <summary>Landmarks placed in a district (cached).</summary>
        public List<LandmarkPlacement> LandmarksIn(Vector2Int district) =>
            _landmarks.GetOrAdd(district, d => { var l = new List<LandmarkPlacement>(); Landmarks.InDistrict(Settings, Seed, d, l); return l; });

        /// <summary>
        /// Highest storey worth streaming in this chunk column: storeys above the ground are only seen
        /// through multi-storey volumes, so they load within a chunk of one (and nowhere else).
        /// </summary>
        public int TopStoreyAt(Vector2Int chunk)
        {
            int top = 0;
            var d0 = DistrictOf(chunk);
            var cells = new RectInt(chunk.x * N - N, chunk.y * N - N, 3 * N, 3 * N);      // the chunk plus a one-chunk ring
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                foreach (var l in LandmarksIn(new Vector2Int(d0.x + dx, d0.y + dz)))
                    if (l.storeys - 1 > top && l.WorldCells.Overlaps(cells)) top = l.storeys - 1;
            return top;
        }

        public int CachedChunks => _chunks.Count;
        public int CachedDistricts => _districts.Count;

        /// <summary>Forgets chunks farther than radius (in chunks) from the centre, and districts no longer near.</summary>
        public void Trim(Vector2Int centre, int radius)
        {
            foreach (var k in _chunks.Keys)
                if (Mathf.Abs(k.x - centre.x) > radius || Mathf.Abs(k.z - centre.y) > radius)
                    _chunks.TryRemove(k, out _);
            var dc = DistrictOf(centre);
            int dr = radius / DC + 2;
            foreach (var k in _districts.Keys)
                if (Mathf.Abs(k.x - dc.x) > dr || Mathf.Abs(k.z - dc.y) > dr)
                    _districts.TryRemove(k, out _);
        }
    }
}
