using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// A zone (a "level"): a huge Voronoi region with its own biome table, look, sound, power and storeys.
    /// To add one: create it (Assets > Create > Backrooms > World Gen > Zone), give it biomes and a
    /// weight above 0, and add it to the World asset's zone list. Rebuild nothing: it is read at start.
    /// </summary>
    [CreateAssetMenu(menuName = "Backrooms/World Gen/Zone", fileName = "Zone")]
    public class ZoneDefinition : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public BiomeDefinition biome;
            [Tooltip("Density (0..1) up to which this biome applies. Ordered; the last one takes the rest.")]
            public float upTo = 1f;
        }

        public string zoneName = "Level 0";
        public Color mapColor = new Color(0.86f, 0.74f, 0.36f);
        [Tooltip("Relative chance a zone region picks this zone (0 = only where forced, e.g. the spawn zone).")]
        public float weight = 1f;

        [Header("Biomes along the density field")]
        public List<Entry> biomes = new List<Entry>();
        [Tooltip("Biome for districts on this zone's edges (the transition pieces).")]
        public BiomeDefinition edgeBiome;

        [Header("Neighbours")]
        [Tooltip("Zones allowed to touch this one (empty = any).")] public List<ZoneDefinition> neighbours = new List<ZoneDefinition>();
        [Tooltip("Zone placed along edges with zones this one may not touch.")] public ZoneDefinition transitionZone;

        [Header("Look and sound")]
        public bool overrideFog;
        public Color fogColor = new Color(0.16f, 0.15f, 0.11f);
        public float fogDensity = 0.05f;
        public string ambience = "office";
        [Range(0f, 1f)] public float power = 1f;

        [Header("Vertical profile")]
        public int minStorey = 0, maxStorey = 1;

        public ZoneRules ToRules()
        {
            var z = new ZoneRules
            {
                name = zoneName, mapColor = mapColor, weight = weight,
                edgeBiome = edgeBiome ? edgeBiome.rules.name : "",
                transitionZone = transitionZone ? transitionZone.zoneName : "",
                overrideFog = overrideFog, fogColor = fogColor, fogDensity = fogDensity, ambience = ambience, power = power,
                minStorey = minStorey, maxStorey = maxStorey,
            };
            foreach (var e in biomes)
                if (e.biome) z.biomes.Add(new BiomeBand { rules = e.biome.ToRules(), upTo = e.upTo });
            foreach (var n in neighbours) if (n) z.neighbours.Add(n.zoneName);
            return z;
        }
    }
}
