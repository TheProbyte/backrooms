using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// A rare set piece placed on the landmark grid before rooms fill in. The kind picks the builder in
    /// <see cref="Landmarks"/>; add a new kind there to make a new shape. Add the asset to the World asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Backrooms/World Gen/Landmark", fileName = "Landmark")]
    public class LandmarkDefinition : ScriptableObject
    {
        public string landmarkName = "Colonnade hall";
        public LandmarkKind kind = LandmarkKind.ColonnadeHall;
        [Tooltip("Footprint in cells (1.2 m).")] public int width = 10, depth = 12;
        [Tooltip("Chance that a landmark grid cell holds this landmark (the World asset's spacing sets the grid).")]
        [Range(0f, 1f)] public float chance = 0.35f;
        [Tooltip("Relative weight against other landmarks allowed at the same spot.")] public float weight = 1f;
        [Tooltip("Zones it may appear in (empty = any).")] public List<ZoneDefinition> zones = new List<ZoneDefinition>();
        [Tooltip("Biomes it may appear in (empty = any).")] public List<BiomeDefinition> biomes = new List<BiomeDefinition>();
        [Tooltip("Storeys it spans from the ground storey up.")] [Range(1, 2)] public int storeys = 1;

        public LandmarkRules ToRules()
        {
            var r = new LandmarkRules { name = landmarkName, kind = kind, width = width, depth = depth, chance = chance, weight = weight, storeys = storeys };
            r.zones.Clear(); r.biomes.Clear();
            foreach (var z in zones) if (z) r.zones.Add(z.zoneName);
            foreach (var b in biomes) if (b) r.biomes.Add(b.rules.name);
            return r;
        }
    }
}
