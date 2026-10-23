using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>One biome inside a zone: its rules and the top of its band on the zone's density field.</summary>
    [Serializable]
    public class BiomeBand
    {
        public BiomeRules rules = new BiomeRules();
        [Tooltip("Density (0..1) up to which this biome applies. Bands are ordered; the last one takes the rest.")]
        public float upTo = 1f;
    }

    /// <summary>
    /// A zone: a very large region (a "level") with its own biome table, look, sound, power and vertical
    /// profile. Zones are placed as Voronoi regions (see <see cref="WorldGenSettings.ZoneAt"/>).
    /// Authored as <see cref="ZoneDefinition"/> assets; this is the plain copy the generator uses.
    /// </summary>
    [Serializable]
    public class ZoneRules
    {
        public string name = "Level 0";
        public Color mapColor = new Color(0.86f, 0.74f, 0.36f);
        [Tooltip("Relative chance that a zone region picks this zone (0 = only where forced, e.g. spawn).")]
        public float weight = 1f;

        [Header("Biomes (ordered along the density field)")]
        public List<BiomeBand> biomes = new List<BiomeBand>();
        [Tooltip("Biome used for districts on this zone's edges (the transition pieces between zones).")]
        public string edgeBiome = "Hallways";

        [Header("Neighbours")]
        [Tooltip("Zones allowed to touch this one directly (empty = any).")]
        public List<string> neighbours = new List<string>();
        [Tooltip("Zone placed along the edge where this zone meets a zone it may not touch.")]
        public string transitionZone = "";

        [Header("Look and sound")]
        [Tooltip("Apply this zone's fog (off: keep the scene's fog, e.g. the owner's tuned look).")]
        public bool overrideFog;
        public Color fogColor = new Color(0.16f, 0.15f, 0.11f);
        public float fogDensity = 0.05f;
        [Tooltip("Ambience preset name for the audio system.")]
        public string ambience = "office";
        [Tooltip("Share of fixtures that have power (scales every biome's working-light chances).")]
        [Range(0f, 1f)] public float power = 1f;

        [Header("Vertical profile")]
        [Tooltip("Lowest storey that exists in this zone.")] public int minStorey = 0;
        [Tooltip("Highest storey that exists in this zone.")] public int maxStorey = 1;

        public ZoneRules Clone()
        {
            var c = (ZoneRules)MemberwiseClone();
            c.biomes = new List<BiomeBand>();
            foreach (var b in biomes) c.biomes.Add(new BiomeBand { rules = b.rules.Clone(), upTo = b.upTo });
            c.neighbours = new List<string>(neighbours);
            return c;
        }

        public BiomeBand Band(string biomeName)
        {
            foreach (var b in biomes) if (b.rules.name == biomeName) return b;
            return null;
        }

        public bool Allows(string other) => neighbours.Count == 0 || neighbours.Contains(other) || other == name;

        /// <summary>The ground level: endless offices, with hallways and warehouse floors on its density field.</summary>
        public static ZoneRules Level0() => new ZoneRules
        {
            name = "Level 0",
            biomes = new List<BiomeBand>
            {
                new BiomeBand { rules = BiomeRules.Hallways(), upTo = 0.36f },
                new BiomeBand { rules = BiomeRules.Offices(), upTo = 0.66f },
                new BiomeBand { rules = BiomeRules.Warehouse(), upTo = 1f },
            },
        };
    }

    public enum LandmarkKind
    {
        /// <summary>Tall painted hall with a double row of columns and arched doorways in the middle of each side.</summary>
        ColonnadeHall,
        /// <summary>Two-storey lightwell: an arcade around a void, with a railed balcony on the storey above.</summary>
        Atrium,
    }

    /// <summary>A rare set piece, placed on a sparse grid and reserved before rooms fill in.</summary>
    [Serializable]
    public class LandmarkRules
    {
        public string name = "Colonnade hall";
        public LandmarkKind kind = LandmarkKind.ColonnadeHall;
        [Tooltip("Footprint in cells (1.2 m).")] public int width = 10, depth = 10;
        [Tooltip("Chance that a landmark cell holds a landmark at all (shared by all landmarks).")]
        [Range(0f, 1f)] public float chance = 0.35f;
        [Tooltip("Relative weight among landmarks allowed at a spot.")] public float weight = 1f;
        [Tooltip("Zones it may appear in (empty = any).")] public List<string> zones = new List<string>();
        [Tooltip("Biomes it may appear in (empty = any).")] public List<string> biomes = new List<string>();
        [Tooltip("Storeys the landmark spans, from the ground storey up.")] public int storeys = 1;

        public LandmarkRules Clone()
        {
            var c = (LandmarkRules)MemberwiseClone();
            c.zones = new List<string>(zones); c.biomes = new List<string>(biomes);
            return c;
        }

        public static LandmarkRules ColonnadeHall() => new LandmarkRules
        {
            name = "Colonnade hall", kind = LandmarkKind.ColonnadeHall, width = 10, depth = 12, chance = 0.35f, weight = 1f,
            biomes = new List<string> { "Offices", "Warehouse" },
        };

        public static LandmarkRules Atrium() => new LandmarkRules
        {
            name = "Atrium", kind = LandmarkKind.Atrium, width = 16, depth = 12, chance = 0.35f, weight = 1f, storeys = 2,
            biomes = new List<string> { "Offices" },
        };
    }

    /// <summary>Where a landmark landed: a rectangle of cells in one district.</summary>
    public struct LandmarkPlacement
    {
        public string name;
        public LandmarkKind kind;
        public Vector2Int district;   // district (x, z)
        public RectInt rect;          // district-local cells (the whole reserved area, atrium plus its annex)
        public RectInt core;          // atrium only: the atrium proper (ring and well); equals rect when there is no annex
        public RectInt annex;         // atrium only: the stairs / elevator wing along one side of the core (width 0 if none)
        public int side;              // which side of the core the annex is on: 0 south, 1 north, 2 west, 3 east
        public int connectors;        // AtriumAnnex.Stairs | AtriumAnnex.Elevator
        public int storeys;           // spans storeys 0 .. storeys - 1
        public int seed;              // for the landmark's own dice
        public RectInt WorldCells => new RectInt(district.x * WorldGenSettings.DistrictCells + rect.x, district.y * WorldGenSettings.DistrictCells + rect.y, rect.width, rect.height);
    }
}
