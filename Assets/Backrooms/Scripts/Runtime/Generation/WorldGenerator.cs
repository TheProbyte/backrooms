using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The one place every system gets chunk layouts from (streamer, audio, debug tools, tests, the world
    /// map). An instance is an immutable world: a seed plus a frozen copy of the settings. Layouts are
    /// pure functions of (seed, settings, coordinate), so the cache only saves time; asking in any order,
    /// from any thread, gives the same result.
    /// </summary>
    public sealed class WorldGenerator
    {
        public readonly int Seed;
        public readonly WorldGenSettings Settings;

        readonly ConcurrentDictionary<Vector2Int, Lazy<OfficeLayout>> _chunks = new ConcurrentDictionary<Vector2Int, Lazy<OfficeLayout>>();

        /// <param name="settings">Copied, so later edits to the original never change this world.</param>
        public WorldGenerator(int seed, WorldGenSettings settings)
        {
            Seed = seed;
            Settings = (settings ?? new WorldGenSettings()).Clone();
        }

        public WorldGenSettings.Sample Sample(Vector2Int chunk) => Settings.Evaluate(Seed, chunk);

        /// <summary>Layout of one chunk (thread-safe; built once per instance).</summary>
        public OfficeLayout Chunk(Vector2Int chunk) =>
            _chunks.GetOrAdd(chunk, c => new Lazy<OfficeLayout>(() => Build(c))).Value;

        /// <summary>Builds a layout without touching the cache (tests use this to compare orders).</summary>
        public OfficeLayout Build(Vector2Int chunk)
        {
            var s = Sample(chunk);
            return OfficeLayoutGenerator.Generate(Seed, chunk, OfficeBiome.ChunkCells, s.rules, s.Dominant);
        }

        public int CachedChunks => _chunks.Count;

        /// <summary>Forgets layouts farther than radius (in chunks) from the centre.</summary>
        public void Trim(Vector2Int centre, int radius)
        {
            foreach (var k in _chunks.Keys)
                if (Mathf.Abs(k.x - centre.x) > radius || Mathf.Abs(k.y - centre.y) > radius)
                    _chunks.TryRemove(k, out _);
        }

        /// <summary>The eight surrounding layouts in the order the chunk builder expects (row by row, z then x).</summary>
        public OfficeLayout[] Neighbours(Vector2Int c)
        {
            var nb = new OfficeLayout[8];
            int k = 0;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                nb[k++] = Chunk(new Vector2Int(c.x + dx, c.y + dz));
            }
            return nb;
        }
    }
}
