using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The world's generation rules as assets: zone field, biome field, landmark grid, storeys, and the
    /// zones and landmarks in play. Loaded from Resources/<see cref="ResourcePath"/> at start (F5 edits are
    /// saved on top). Create the default set with Backrooms > World Gen > Create Default Assets.
    /// </summary>
    [CreateAssetMenu(menuName = "Backrooms/World Gen/World", fileName = "World")]
    public class WorldDefinition : ScriptableObject
    {
        public const string ResourcePath = "WorldGen/World";

        [Header("Zones")]
        [Tooltip("Typical distance between zone centres, metres.")] public float zoneSize = 1200f;
        [Tooltip("Width of the transition band along zone edges, metres.")] public float zoneEdge = 60f;
        [Tooltip("The first zone is the spawn zone.")] public List<ZoneDefinition> zones = new List<ZoneDefinition>();

        [Header("Biome density field")]
        public float biomeScale = 7f;
        public int octaves = 3;
        public float warp = 0.7f;
        public float transition = 0.08f;

        [Header("Landmarks")]
        [Tooltip("Landmark grid spacing, in districts (4 x 4 chunks).")] public int landmarkSpacing = 3;
        public List<LandmarkDefinition> landmarks = new List<LandmarkDefinition>();

        [Header("Storeys")]
        public float storeyPitch = 9f;

        public WorldGenSettings ToSettings()
        {
            var s = new WorldGenSettings
            {
                zoneSize = zoneSize, zoneEdge = zoneEdge, biomeScale = biomeScale, octaves = octaves, warp = warp, transition = transition,
                landmarkSpacing = landmarkSpacing, storeyPitch = storeyPitch,
            };
            s.zones.Clear();
            foreach (var z in zones) if (z) s.zones.Add(z.ToRules());
            if (s.zones.Count == 0) s.zones.Add(ZoneRules.Level0());
            s.landmarks.Clear();
            foreach (var l in landmarks) if (l) s.landmarks.Add(l.ToRules());
            return s;
        }
    }
}
