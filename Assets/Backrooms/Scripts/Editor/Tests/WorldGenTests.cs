using System.Collections.Generic;
using System.Linq;
using Backrooms.Generation;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>
    /// World generation invariants. Run from Window > General > Test Runner (EditMode).
    /// Each test covers a block of chunks for several seeds with default settings.
    /// </summary>
    public class WorldGenTests
    {
        static readonly int[] Seeds = { 1, 1234, 98765, 424242 };
        /// <summary>The shipped settings (World asset if present, else code defaults).</summary>
        static WorldGenerator Gen(int seed) => new WorldGenerator(seed, WorldGenSettings.Defaults());
        static RectInt Block(int r) => new RectInt(-r, -r, 2 * r + 1, 2 * r + 1);

        static IEnumerable<Vector2Int> Coords(RectInt b)
        {
            for (int x = b.xMin; x < b.xMax; x++) for (int z = b.yMin; z < b.yMax; z++) yield return new Vector2Int(x, z);
        }

        [Test]
        public void ChunksAreIdenticalInAnyOrder()
        {
            foreach (int seed in Seeds)
            {
                var block = Block(3);
                var forward = Gen(seed);
                var expected = Coords(block).ToDictionary(c => c, c => LayoutDigest.Of(forward.Chunk(c)));

                // Reverse order, on a fresh generator.
                var reverse = Gen(seed);
                foreach (var c in Coords(block).Reverse())
                    Assert.AreEqual(expected[c], LayoutDigest.Of(reverse.Chunk(c)), $"seed {seed} chunk {c} differs in reverse order");

                // Shuffled order, in parallel, on another fresh generator.
                var shuffled = Coords(block).OrderBy(c => (c.x * 7919 + c.y * 104729) % 97).ToArray();
                var parallel = Gen(seed);
                var got = new ulong[shuffled.Length];
                System.Threading.Tasks.Parallel.For(0, shuffled.Length, i => got[i] = LayoutDigest.Of(parallel.Chunk(shuffled[i])));
                for (int i = 0; i < shuffled.Length; i++)
                    Assert.AreEqual(expected[shuffled[i]], got[i], $"seed {seed} chunk {shuffled[i]} differs when built in parallel");

                // Uncached rebuild of a single chunk far from the others.
                Assert.AreEqual(expected[new Vector2Int(2, -3)], LayoutDigest.Of(Gen(seed).Build(new Vector2Int(2, -3))));
            }
        }

        [Test]
        public void ChunkBordersAgree()
        {
            foreach (int seed in Seeds)
            {
                var issues = WorldChecks.BorderMismatches(Gen(seed), Block(4));
                Assert.IsEmpty(issues, $"seed {seed}: {issues.Count} border mismatches, first: {issues.FirstOrDefault()}");
            }
        }

        [Test]
        public void EveryRoomIsReachable()
        {
            foreach (int seed in Seeds)
            {
                var issues = WorldChecks.Unreachable(Gen(seed), Block(4));
                Assert.IsEmpty(issues, $"seed {seed}: {issues.Count} unreachable cells, first: {issues.FirstOrDefault()}");
            }
        }

        /// <summary>
        /// Unshadowed lights only reach surfaces carrying their layer. A visible leak is a surface in another
        /// light zone that a light reaches with at least <see cref="LightLayers.VisibleIrradiance"/>.
        /// Across district borders there must be none (neighbouring districts use different colour sets).
        /// Inside a district 7 colours cannot always separate every light in the densest spots, so a few are
        /// tolerated (at most one per ten chunks checked). The light grid (phase 4) removes this limit.
        /// </summary>
        [Test]
        public void NoLightLeaksThroughWalls()
        {
            int total = 0, cross = 0, chunks = 0;
            var report = new System.Text.StringBuilder();
            foreach (int seed in Seeds)
            {
                var block = Block(3);
                chunks += (block.width - 2) * (block.height - 2);
                var issues = WorldChecks.LightLeaks(Gen(seed), block);
                total += issues.Count; cross += issues.Count(i => i.crossDistrict);
                foreach (var i in issues.Take(3)) report.AppendLine($"seed {seed}: {i}");
            }
            TestContext.WriteLine($"{total} visible light leaks in {chunks} chunks ({cross} across district borders)\n{report}");
            Assert.AreEqual(0, cross, $"{cross} light leaks across district borders\n{report}");
            Assert.LessOrEqual(total, chunks / 10, $"{total} light leaks inside districts\n{report}");
        }
    }
}
