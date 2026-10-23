using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// World generation rules, layered top-down (Hytale World Gen V2 style). Each layer depends only on
    /// the layers above it and on the seed, never on what was generated before:
    ///  1. Zones: very large Voronoi regions (a "level" each) with their own biome table, look, sound,
    ///     power and storeys. Zone edges become transition districts.
    ///  2. Biomes: a 2D density field (fractal noise with domain warp, per chunk) split by each zone's
    ///     biome bands, with rules interpolated across a transition width.
    ///  3. Landmarks: rare set pieces on a sparse grid, reserved before rooms fill in (see <see cref="Landmarks"/>).
    ///  4. Districts: 4 x 4 chunks planned at once (see <see cref="DistrictGenerator"/>).
    ///  5. Rooms and features: the biome's recipes inside each district.
    /// Chunks (14.4 m) are only slices of district output for streaming and meshing.
    ///
    /// Authored as assets (<see cref="WorldDefinition"/>, zones, biomes, landmarks); this class is the
    /// live, editable copy (F5 > World Gen) and is what the generator reads.
    /// Determinism: same seed + same settings + same <see cref="Version"/> = the same world (see <see cref="WorldCode"/>).
    /// </summary>
    [Serializable]
    public class WorldGenSettings
    {
        [Header("Zones (Voronoi regions)")]
        [Tooltip("Typical distance between zone centres, metres.")] public float zoneSize = 1200f;
        [Tooltip("Width of the transition band along zone edges, metres.")] public float zoneEdge = 60f;
        public List<ZoneRules> zones = new List<ZoneRules> { ZoneRules.Level0() };

        [Header("Biome density field")]
        [Tooltip("Size of biome regions, in chunks (14.4 m each) per noise cell.")] public float biomeScale = 7f;
        [Tooltip("Noise octaves: more = more detailed region edges.")] public int octaves = 3;
        [Tooltip("Domain warp: bends region boundaries into organic shapes.")] public float warp = 0.7f;
        [Tooltip("Width of the blend between biomes, in density units.")] public float transition = 0.08f;

        [Header("Landmarks")]
        [Tooltip("Landmark grid spacing, in districts. Each grid cell holds at most one landmark.")] public int landmarkSpacing = 3;
        public List<LandmarkRules> landmarks = new List<LandmarkRules> { LandmarkRules.ColonnadeHall(), LandmarkRules.Atrium() };

        [Header("Storeys")]
        [Tooltip("Height between storeys, metres (above the tallest ceiling).")] public float storeyPitch = 9f;

        /// <summary>Bump when generation code changes how a seed plays out.
        /// 2: fixture spacing, strips, open plan. 3: arches. 4: zones, landmarks, districts, storeys.</summary>
        public const int Version = 6;

        /// <summary>District size in chunks and cells.</summary>
        public const int DistrictChunks = 4, DistrictCells = DistrictChunks * OfficeBiome.ChunkCells;

        public WorldGenSettings Clone()
        {
            var c = (WorldGenSettings)MemberwiseClone();
            c.zones = new List<ZoneRules>();
            foreach (var z in zones) c.zones.Add(z.Clone());
            c.landmarks = new List<LandmarkRules>();
            foreach (var l in landmarks) c.landmarks.Add(l.Clone());
            return c;
        }

        /// <summary>The shipped defaults: the WorldDefinition asset in Resources if present, else code defaults.</summary>
        public static WorldGenSettings Defaults()
        {
            var def = Resources.Load<WorldDefinition>(WorldDefinition.ResourcePath);
            return def ? def.ToSettings() : new WorldGenSettings();
        }

        public int ZoneIndex(string name)
        {
            for (int i = 0; i < zones.Count; i++) if (zones[i].name == name) return i;
            return -1;
        }

        // ---------------- Zones ----------------

        public struct ZoneHit
        {
            public int zone;             // index into zones
            public int neighbour;        // nearest different zone (or -1)
            public float edgeDistance;   // metres to the edge with that zone
            public bool edge;            // inside the transition band
        }

        /// <summary>Zone of a district (evaluated at its centre). Pure function of seed and settings.</summary>
        public ZoneHit ZoneAt(int seed, Vector2Int district)
        {
            float D = DistrictCells * OfficeBiome.CellSize;
            var p = new Vector2((district.x + 0.5f) * D, (district.y + 0.5f) * D);
            float cell = Mathf.Max(100f, zoneSize);
            int gx = Mathf.FloorToInt(p.x / cell), gz = Mathf.FloorToInt(p.y / cell);
            float d1 = float.MaxValue; Vector2 s1 = default; int z1 = 0;
            var sites = new (Vector2 pos, int zone)[25];
            int k = 0;
            for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
            {
                int cx = gx + i, cz = gz + j;
                var s = new Vector2((cx + Jitter(seed, cx, cz, 0)) * cell, (cz + Jitter(seed, cx, cz, 1)) * cell);
                int zone = SiteZone(seed, cx, cz);
                sites[k++] = (s, zone);
                float d = (s - p).sqrMagnitude;
                if (d < d1) { d1 = d; s1 = s; z1 = zone; }
            }
            // Distance to the nearest edge with a different zone (perpendicular bisector).
            float edge = float.MaxValue; int z2 = -1;
            foreach (var (s, zone) in sites)
            {
                if (zone == z1) continue;
                var dir = s - s1;
                float len = dir.magnitude;
                if (len < 1e-3f) continue;
                float dist = ((p - s).sqrMagnitude - (p - s1).sqrMagnitude) / (2f * len);
                if (dist < edge) { edge = dist; z2 = zone; }
            }
            var hit = new ZoneHit { zone = z1, neighbour = z2, edgeDistance = edge, edge = z2 >= 0 && edge < zoneEdge };
            // Zones that may not touch: the band along their edge becomes the transition zone.
            if (hit.edge && !Compatible(z1, z2))
            {
                int t = ZoneIndex(zones[z1].transitionZone);
                if (t < 0) t = ZoneIndex(zones[z2].transitionZone);
                if (t >= 0) hit.zone = t;
            }
            return hit;
        }

        bool Compatible(int a, int b) => zones[a].Allows(zones[b].name) && zones[b].Allows(zones[a].name);

        static float Jitter(int seed, int x, int z, int axis) => 0.15f + 0.7f * Hash(seed ^ 0x2F0E5, x * 2 + axis, z);

        /// <summary>The zone a Voronoi site picks. Sites around the origin always pick the first zone (spawn).</summary>
        int SiteZone(int seed, int x, int z)
        {
            if (zones.Count <= 1 || (x >= -2 && x <= 1 && z >= -2 && z <= 1)) return 0;
            float total = 0f;
            foreach (var zr in zones) total += Mathf.Max(0f, zr.weight);
            if (total <= 0f) return 0;
            float r = Hash(seed ^ 0x5173, x, z) * total;
            for (int i = 0; i < zones.Count; i++)
            {
                r -= Mathf.Max(0f, zones[i].weight);
                if (r < 0f) return i;
            }
            return 0;
        }

        // ---------------- Biomes ----------------

        public struct Sample
        {
            public float density;
            public int zoneIndex;
            public string zone;          // the zone this chunk belongs to (biomes live inside a zone)
            public bool zoneEdge;        // transition district on a zone edge
            public float[] weights;      // per biome band of the zone
            public BiomeRules rules;     // blended rules
            public string Dominant;      // name of the heaviest biome
            public Color mapColor;
        }

        public static Vector2Int DistrictOf(Vector2Int chunk) => new Vector2Int(FloorDiv(chunk.x, DistrictChunks), FloorDiv(chunk.y, DistrictChunks));

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

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

        /// <summary>Zone and blended biome rules of a chunk.</summary>
        public Sample Evaluate(int seed, Vector2Int chunk)
        {
            var hit = ZoneAt(seed, DistrictOf(chunk));
            var zone = zones[Mathf.Clamp(hit.zone, 0, zones.Count - 1)];
            float d = Density(seed, chunk);
            var s = new Sample { density = d, zoneIndex = hit.zone, zone = zone.name, zoneEdge = hit.edge, weights = new float[zone.biomes.Count] };
            if (zone.biomes.Count == 0)
            {
                s.rules = BiomeRules.Offices(); s.Dominant = s.rules.name; s.mapColor = zone.mapColor;
                return s;
            }
            var edgeBand = hit.edge ? zone.Band(zone.edgeBiome) : null;
            if (edgeBand != null)
            {
                int i = zone.biomes.IndexOf(edgeBand);
                s.weights[i] = 1f; s.rules = edgeBand.rules;
            }
            else
            {
                // Bands along the density field, each boundary blended over the transition width.
                float half = Mathf.Max(0.001f, transition * 0.5f);
                var rules = zone.biomes[0].rules;
                float carry = 1f;                       // weight still to hand out above this band
                for (int i = 0; i < zone.biomes.Count; i++)
                {
                    float t = i + 1 < zone.biomes.Count
                        ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(zone.biomes[i].upTo - half, zone.biomes[i].upTo + half, d)) : 1f;
                    s.weights[i] = carry * (i + 1 < zone.biomes.Count ? 1f - t : 1f);
                    if (i + 1 < zone.biomes.Count) { rules = BiomeRules.Lerp(rules, zone.biomes[i + 1].rules, t); carry *= t; }
                }
                s.rules = rules;
            }
            if (zone.power < 0.999f)
            {
                s.rules = s.rules.Clone();
                s.rules.lightsAllOn *= zone.power; s.rules.mixedOnFraction *= zone.power;
            }
            int best = 0;
            for (int i = 1; i < s.weights.Length; i++) if (s.weights[i] > s.weights[best]) best = i;
            s.Dominant = zone.biomes[best].rules.name;
            Color c = Color.black;
            for (int i = 0; i < s.weights.Length; i++) c += zone.biomes[i].rules.mapColor * s.weights[i];
            s.mapColor = c;
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

        internal static float Hash(int seed, int x, int y)
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

        const string PrefsKey = "Backrooms.WorldGen.v2";
        const string LegacyPrefsKey = "Backrooms.WorldGen.v1";

        /// <summary>Saved F5 edits on top of the shipped defaults. Settings saved by older versions are migrated.</summary>
        public static WorldGenSettings Load()
        {
            try
            {
                string json = PlayerPrefs.GetString(PrefsKey, "");
                if (!string.IsNullOrEmpty(json))
                {
                    var s = JsonUtility.FromJson<WorldGenSettings>(json);
                    if (s != null && s.zones != null && s.zones.Count > 0) return s;
                }
                string legacy = PlayerPrefs.GetString(LegacyPrefsKey, "");
                if (!string.IsNullOrEmpty(legacy)) return FromLegacy(JsonUtility.FromJson<LegacyV1>(legacy));
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not load world gen settings: " + e.Message); }
            return Defaults();
        }

        public void Save() { PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this)); PlayerPrefs.Save(); }

        /// <summary>Settings as saved before zones existed (version 3): one implicit zone with three biomes.</summary>
        [Serializable]
        class LegacyV1
        {
            public float biomeScale = 7f; public int octaves = 3; public float warp = 0.7f;
            public float hallwaysBelow = 0.36f, warehouseAbove = 0.66f, transition = 0.08f;
            public BiomeRules hallways = BiomeRules.Hallways(), offices = BiomeRules.Offices(), warehouse = BiomeRules.Warehouse();
        }

        static WorldGenSettings FromLegacy(LegacyV1 old)
        {
            var s = Defaults();
            if (old == null) return s;
            s.biomeScale = old.biomeScale; s.octaves = old.octaves; s.warp = old.warp; s.transition = old.transition;
            var z = s.zones[0];
            void Copy(string name, BiomeRules from, float upTo)
            {
                var band = z.Band(name);
                if (band == null || from == null) return;
                float keepMax = band.rules.maxRoomCells;
                band.rules = from;
                band.rules.maxRoomCells = keepMax;          // did not exist in version 3
                band.upTo = upTo;
            }
            Copy("Hallways", old.hallways, old.hallwaysBelow);
            Copy("Offices", old.offices, old.warehouseAbove);
            Copy("Warehouse", old.warehouse, 1f);
            return s;
        }
    }
}
