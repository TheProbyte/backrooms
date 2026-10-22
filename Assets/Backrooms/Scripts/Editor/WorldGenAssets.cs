using System.IO;
using Backrooms.Generation;
using UnityEditor;
using UnityEngine;

namespace Backrooms.EditorTools
{
    /// <summary>
    /// Backrooms > World Gen > Create Default Assets: writes the World, Level 0 zone, the three Level 0
    /// biomes and the two landmarks as assets under Resources/WorldGen (from the code defaults). Existing
    /// assets are left alone, so this is safe to run again after adding new defaults.
    /// </summary>
    public static class WorldGenAssets
    {
        const string Root = "Assets/Backrooms/Resources/WorldGen";

        [MenuItem("Backrooms/World Gen/Create Default Assets")]
        public static void CreateDefaults()
        {
            Directory.CreateDirectory(Root + "/Biomes");
            Directory.CreateDirectory(Root + "/Zones");
            Directory.CreateDirectory(Root + "/Landmarks");
            AssetDatabase.Refresh();

            var code = new WorldGenSettings();
            var level0 = code.zones[0];

            T Make<T>(string path, System.Action<T> fill) where T : ScriptableObject
            {
                var existing = AssetDatabase.LoadAssetAtPath<T>(path);
                if (existing) return existing;
                var a = ScriptableObject.CreateInstance<T>();
                fill(a);
                AssetDatabase.CreateAsset(a, path);
                return a;
            }

            var biomes = new BiomeDefinition[level0.biomes.Count];
            for (int i = 0; i < level0.biomes.Count; i++)
            {
                var band = level0.biomes[i];
                biomes[i] = Make<BiomeDefinition>($"{Root}/Biomes/{band.rules.name}.asset", b => b.rules = band.rules.Clone());
            }

            var zone = Make<ZoneDefinition>($"{Root}/Zones/Level0.asset", z =>
            {
                z.zoneName = level0.name; z.mapColor = level0.mapColor; z.weight = level0.weight;
                for (int i = 0; i < biomes.Length; i++) z.biomes.Add(new ZoneDefinition.Entry { biome = biomes[i], upTo = level0.biomes[i].upTo });
                foreach (var b in biomes) if (b.rules.name == level0.edgeBiome) z.edgeBiome = b;
                z.overrideFog = level0.overrideFog; z.fogColor = level0.fogColor; z.fogDensity = level0.fogDensity;
                z.ambience = level0.ambience; z.power = level0.power; z.minStorey = level0.minStorey; z.maxStorey = level0.maxStorey;
            });

            LandmarkDefinition Landmark(LandmarkRules r, string file) => Make<LandmarkDefinition>($"{Root}/Landmarks/{file}.asset", l =>
            {
                l.landmarkName = r.name; l.kind = r.kind; l.width = r.width; l.depth = r.depth; l.chance = r.chance; l.weight = r.weight; l.storeys = r.storeys;
                foreach (var name in r.biomes) foreach (var b in biomes) if (b.rules.name == name) l.biomes.Add(b);
            });
            var colonnade = Landmark(LandmarkRules.ColonnadeHall(), "ColonnadeHall");
            var atrium = Landmark(LandmarkRules.Atrium(), "Atrium");

            var world = Make<WorldDefinition>($"{Root}/World.asset", w =>
            {
                w.zoneSize = code.zoneSize; w.zoneEdge = code.zoneEdge; w.biomeScale = code.biomeScale; w.octaves = code.octaves;
                w.warp = code.warp; w.transition = code.transition; w.landmarkSpacing = code.landmarkSpacing; w.storeyPitch = code.storeyPitch;
                w.zones.Add(zone);
                w.landmarks.Add(colonnade); w.landmarks.Add(atrium);
            });
            AssetDatabase.SaveAssets();
            Selection.activeObject = world;
            Debug.Log($"[Backrooms] World gen assets in {Root}. The World asset is loaded from Resources/{WorldDefinition.ResourcePath}.");
        }
    }
}
