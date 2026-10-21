using System;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// World generation rules, Hytale World Gen V2 style:
    /// a 2D <b>density field</b> (fractal noise with domain warp, sampled per chunk) is split by
    /// <b>delimiters</b> into biomes, and each biome's rules are <b>interpolated</b> across a transition
    /// band so areas change gradually. Biome order along the field is Hallways -> Offices -> Warehouse,
    /// so corridors never butt straight into a warehouse.
    ///
    /// Determinism: a chunk depends only on (seed, chunk coordinate, these settings). Same seed + same
    /// settings + same game version = the same world for anyone (see <see cref="WorldCode"/>).
    /// </summary>
    [Serializable]
    public class WorldGenSettings
    {
        [Header("Biome density field")]
        [Tooltip("Size of biome regions, in chunks (14.4 m each) per noise cell.")] public float biomeScale = 7f;
        [Tooltip("Noise octaves: more = more detailed region edges.")] public int octaves = 3;
        [Tooltip("Domain warp: bends region boundaries into organic shapes.")] public float warp = 0.7f;

        [Header("Delimiters (density 0..1)")]
        [Tooltip("Density below this is Hallways.")] public float hallwaysBelow = 0.36f;
        [Tooltip("Density above this is Warehouse. Between is Offices.")] public float warehouseAbove = 0.66f;
        [Tooltip("Width of the blend between biomes, in density units.")] public float transition = 0.08f;

        [Header("Biomes")]
        public BiomeRules hallways = BiomeRules.Hallways();
        public BiomeRules offices = BiomeRules.Offices();
        public BiomeRules warehouse = BiomeRules.Warehouse();

        public const int Version = 2;   // bump when generation code changes how a seed plays out (2: fixture spacing, strips, open plan)

        public WorldGenSettings Clone()
        {
            var c = (WorldGenSettings)MemberwiseClone();
            c.hallways = hallways.Clone(); c.offices = offices.Clone(); c.warehouse = warehouse.Clone();
            return c;
        }

        // ---------------- Field ----------------

        public struct Sample
        {
            public float density;
            public float wHallways, wOffices, wWarehouse;
            public BiomeRules rules;
            public string zone;          // the zone this chunk belongs to (biomes live inside a zone)
            public string Dominant => wWarehouse >= wOffices && wWarehouse >= wHallways ? "Warehouse" : wHallways >= wOffices ? "Hallways" : "Offices";
        }

        /// <summary>Biome density at a chunk, 0..1. Pure function of seed and settings.</summary>
        public float Density(int seed, Vector2 chunk)
        {
            float s = Mathf.Max(0.5f, biomeScale);
            Vector2 p = chunk / s;
            if (warp > 0f)
            {
                float wx = Fbm(seed + 101, p * 0.7f + new Vector2(5.2f, 1.3f), 2) - 0.5f;
                float wz = Fbm(seed + 202, p * 0.7f + new Vector2(9.7f, 4.1f), 2) - 0.5f;
                p += new Vector2(wx, wz) * warp * 2f;
            }
            float d = Fbm(seed, p, Mathf.Clamp(octaves, 1, 6));
            // Spread values toward 0..1 (fbm clusters around 0.5).
            return Mathf.Clamp01((d - 0.5f) * 1.9f + 0.5f);
        }

        /// <summary>
        /// Zone layer (above biomes). A zone is a whole "level" with its own biome set, delimiters, look and
        /// sound; biomes are picked inside it by the density field below. Only one zone exists so far, so
        /// every chunk is in it. A later zone will add a very low-frequency zone field here (a few hundred
        /// chunks per cell) and connecting rules at zone borders; chunk generation already takes the
        /// evaluated rules as input, so adding zones does not change the chunk pipeline.
        /// </summary>
        public string ZoneAt(int seed, Vector2Int chunk) => "Level 0";

        public Sample Evaluate(int seed, Vector2Int chunk)
        {
            float d = Density(seed, chunk);
            float half = Mathf.Max(0.001f, transition * 0.5f);
            // Weight of "above the lower delimiter" and "above the upper delimiter".
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hallwaysBelow - half, hallwaysBelow + half, d));
            float b = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(warehouseAbove - half, warehouseAbove + half, d));
            var s = new Sample { density = d, wHallways = 1f - a, wOffices = a * (1f - b), wWarehouse = b, zone = ZoneAt(seed, chunk) };
            var low = BiomeRules.Lerp(hallways, offices, a);
            s.rules = BiomeRules.Lerp(low, warehouse, b);
            return s;
        }

        /// <summary>Short code that identifies seed + settings + version, for sharing a world.</summary>
        public string WorldCode(int seed)
        {
            string json = JsonUtility.ToJson(this);
            uint h = 2166136261u;
            foreach (char ch in json) { h ^= ch; h *= 16777619u; }
            return $"{seed}-{h & 0xFFFFFF:X6}-v{Version}";
        }

        // ---------------- Noise ----------------

        static float Hash(int seed, int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static float Value(int seed, Vector2 p)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y);
            float fx = p.x - x0, fy = p.y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = Hash(seed, x0, y0), b = Hash(seed, x0 + 1, y0), c = Hash(seed, x0, y0 + 1), d = Hash(seed, x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(int seed, Vector2 p, int oct)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < oct; i++)
            {
                sum += amp * Value(seed + i * 977, p);
                norm += amp; amp *= 0.5f; p *= 2.03f;
            }
            return sum / norm;
        }

        // ---------------- Persistence ----------------

        const string PrefsKey = "Backrooms.WorldGen.v1";

        public static WorldGenSettings Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(PrefsKey, "");
                if (!string.IsNullOrEmpty(json)) return JsonUtility.FromJson<WorldGenSettings>(json);
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not load world gen settings: " + e.Message); }
            return new WorldGenSettings();
        }

        public void Save() { PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this)); PlayerPrefs.Save(); }
    }
}
