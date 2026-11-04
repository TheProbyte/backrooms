using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The Poolrooms' three materials (tiles, water, sky), made from the shaders in Resources/Shaders. Created on the main
    /// thread before any chunk is built (WorldStreamer.Awake): chunk data is prepared on worker threads, which only read
    /// these references.
    /// </summary>
    public static class PoolMaterials
    {
        public static Material Tile { get; private set; }
        public static Material Water { get; private set; }
        public static Material Sky { get; private set; }
        /// <summary>Darker accent tiles: the border round a pool, the band at its waterline, lane lines' ends.</summary>
        public static Material Accent { get; private set; }
        /// <summary>Tile colours by room (2026-10-31): 0 sage (the usual), 1 cream, 2 deeper mint, 3 pale aqua.</summary>
        public static Material[] Zones { get; private set; }
        public static Material Chrome { get; private set; }
        public static Material PlasticWhite { get; private set; }
        public static Material PlasticBlue { get; private set; }
        /// <summary>A ladder's bumpers (black rubber) and treads (pale grey non-slip plastic).</summary>
        public static Material Rubber { get; private set; }
        /// <summary>The fittings (2026-11-01, the realism plan 5): dark marker tiles, the dark of openings, an underwater light's
        /// dead lens and a lit one, a vent grille's grey metal, rust tears and rings (multiplied over the tiles).</summary>
        public static Material Marker { get; private set; }
        public static Material Dark { get; private set; }
        public static Material LensDead { get; private set; }
        public static Material Glow { get; private set; }
        public static Material Grille { get; private set; }
        public static Material StainTear { get; private set; }
        public static Material StainRing { get; private set; }
        public static Material GlassBlock { get; private set; }
        public static Material Tread { get; private set; }
        /// <summary>The water park (2026-11-01): falling water (Backrooms/PoolSheet), red and yellow gelcoat.</summary>
        public static Material Sheet { get; private set; }
        public static Material PlasticRed { get; private set; }
        public static Material PlasticYellow { get; private set; }
        /// <summary>Painted (galvanised) steel: the water park's towers and supports.</summary>
        public static Material Steel { get; private set; }
        /// <summary>Bleacher planks: extruded aluminium, satin (2026-11-04).</summary>
        public static Material Aluminium { get; private set; }
        /// <summary>Changing stall partitions: solid HDPE plastic, a pale grey, its orange-peel finish not shiny (2026-11-04).</summary>
        public static Material Partition { get; private set; }

        static readonly System.Collections.Generic.Dictionary<Material, Material> s_baked = new System.Collections.Generic.Dictionary<Material, Material>();

        /// <summary>
        /// A modelled fitting's baked material (BakedMaterials, on Backrooms/Lit) as the Poolrooms draw it: a PoolLit copy
        /// with the same colour texture, gloss and metalness (PoolLit has no normal maps; the tiles' light is what matters
        /// there). Main thread or worker: made once a material, from the main thread's first use (FittingModels loads).
        /// </summary>
        public static Material ForBaked(Material m)
        {
            if (!m) return null;
            if (s_baked.TryGetValue(m, out var p) && p) return p;
            p = Make("Backrooms/PoolLit", m.name + " (pool)");
            if (!p) return m;
            if (m.HasProperty("_BaseMap")) { p.SetTexture("_BaseMap", m.GetTexture("_BaseMap")); p.SetTextureScale("_BaseMap", Vector2.one); }
            p.SetColor("_BaseColor", m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white);
            p.SetFloat("_Smoothness", m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0.4f);
            p.SetFloat("_Metallic", m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f);
            s_baked[m] = p;
            return p;
        }

        /// <summary>Makes the PoolLit copies of the Poolrooms' modelled fittings on the main thread (the chunk builder runs on workers).</summary>
        public static void PrepareBaked()
        {
            foreach (var n in new[] { "LifeguardChair", "PoolShower", "LadderTread", "StallBracket", "CoatHook", "BleacherCap", "BleacherBracket" })
            {
                var parts = FittingModels.Get(n);
                if (parts == null) continue;
                foreach (var part in parts) ForBaked(part.material);
            }
        }

        /// <summary>The PoolLit copy made by <see cref="PrepareBaked"/> (null if there is none yet: never made off the main thread).</summary>
        public static Material BakedCopy(Material m) => m && s_baked.TryGetValue(m, out var p) ? p : null;

        public static void Ensure()
        {
            if (Tile && Water && Sky && Accent) return;
            Tile = Make("Backrooms/PoolTile", "Pool tiles");
            Water = Make("Backrooms/PoolWater", "Pool water");
            Sky = Make("Backrooms/PoolSky", "Pool sky");
            Accent = Make("Backrooms/PoolTile", "Pool accent tiles");
            if (Accent) { Accent.SetColor("_TileColor", new Color(0.17f, 0.42f, 0.4f)); Accent.SetColor("_DeckColor", new Color(0.17f, 0.42f, 0.4f)); Accent.SetColor("_GroutColor", new Color(0.55f, 0.62f, 0.6f)); Accent.SetFloat("_Chips", 0.06f); }
            Zones = new Material[4];
            Zones[0] = Tile;
            // Grout by room too (2026-11-01, the review): a warmer, darker joint in the cream rooms, a grey one in the mint.
            Zones[1] = Zone("cream", new Color(0.88f, 0.86f, 0.78f), new Color(0.9f, 0.88f, 0.8f), new Color(0.66f, 0.62f, 0.54f));
            Zones[2] = Zone("mint", new Color(0.66f, 0.82f, 0.72f), new Color(0.84f, 0.86f, 0.78f), new Color(0.6f, 0.64f, 0.62f));
            Zones[3] = Zone("aqua", new Color(0.72f, 0.85f, 0.85f), new Color(0.86f, 0.88f, 0.84f), new Color(0.78f, 0.82f, 0.8f));
            // Polished stainless / chrome reflects about 55-60 % head on (its F0), slightly cool; at 0.86 it read as white
            // plastic (2026-11-01, the owner: the rails "could use work visually").
            Chrome = Lit("Pool chrome", new Color(0.6f, 0.615f, 0.63f), 0.93f, 1f);
            Rubber = Lit("Pool rubber", new Color(0.05f, 0.05f, 0.055f), 0.35f, 0f);
            Tread = Lit("Pool ladder tread", new Color(0.78f, 0.8f, 0.8f), 0.4f, 0f);
            Marker = Lit("Pool marker tiles", new Color(0.05f, 0.07f, 0.09f), 0.88f, 0f);
            Dark = Lit("Pool dark opening", new Color(0.02f, 0.025f, 0.025f), 0.2f, 0f);
            LensDead = Lit("Pool light lens (dead)", new Color(0.16f, 0.19f, 0.19f), 0.95f, 0f);
            Grille = Lit("Pool grille", new Color(0.62f, 0.64f, 0.63f), 0.45f, 0f);
            Glow = Make("Backrooms/PoolGlow", "Pool light lens (lit)");
            StainTear = Make("Backrooms/PoolStain", "Pool rust tear");
            StainRing = Make("Backrooms/PoolStain", "Pool rust ring");
            if (StainRing) StainRing.SetFloat("_Ring", 1f);
            GlassBlock = Make("Backrooms/PoolGlassBlock", "Pool glass block");
            PlasticWhite = Lit("Pool plastic (white)", new Color(0.9f, 0.9f, 0.86f), 0.62f, 0f);
            PlasticBlue = Lit("Pool plastic (blue)", new Color(0.13f, 0.38f, 0.68f), 0.88f, 0f);      // glossy gelcoat
            PlasticRed = Lit("Pool plastic (red)", new Color(0.66f, 0.1f, 0.08f), 0.86f, 0f);
            PlasticYellow = Lit("Pool plastic (yellow)", new Color(0.86f, 0.64f, 0.12f), 0.86f, 0f);
            Sheet = Make("Backrooms/PoolSheet", "Pool falling water");
            Steel = Lit("Pool painted steel", new Color(0.58f, 0.62f, 0.64f), 0.5f, 0.55f);
            Aluminium = Lit("Pool aluminium", new Color(0.74f, 0.75f, 0.76f), 0.55f, 0.9f);
            Partition = Lit("Pool stall partition (HDPE)", new Color(0.78f, 0.79f, 0.78f), 0.28f, 0f);
        }

        static Material Zone(string name, Color wall, Color deck, Color? grout = null)
        {
            var m = Make("Backrooms/PoolTile", "Pool tiles (" + name + ")");
            if (m) { m.SetColor("_TileColor", wall); m.SetColor("_DeckColor", deck); if (grout.HasValue) m.SetColor("_GroutColor", grout.Value); }
            return m ? m : Tile;
        }

        static Material Lit(string name, Color c, float smooth, float metal)
        {
            var m = Make("Backrooms/PoolLit", name);
            if (m) { m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal); }
            return m;
        }

        static Material Make(string shader, string name)
        {
            var s = Shader.Find(shader);
            if (!s) { Debug.LogWarning("[Backrooms] Missing shader " + shader); return null; }
            return new Material(s) { name = name, hideFlags = HideFlags.DontSave };
        }
    }

    /// <summary>On a Poolrooms chunk: its cells for the shaders' map (written into PoolWorld when the chunk appears).</summary>
    public class PoolChunk : MonoBehaviour
    {
        public Vector2Int chunk;
        public int storey;
        public Color32[] cells, cells2;
        /// <summary>Jets, skimmers and grilles (local: xyz, w kind 0/1/2), and the water park's: 3 a lazy river's jet, 4 a wave
        /// pool's chambers (dir: toward the beach, its length half their width), 5 a mushroom's falling ring (dir.x its radius,
        /// dir.y the fall), 6 where an arch's stream lands (dir: the way it came). The way each pushes or faces.</summary>
        public Vector4[] fittings;
        public Vector3[] fittingDirs;
        /// <summary>Lane ropes' shares of this chunk (local, on the water: from xy to zw in x-z) and their tied ends (bits 0, 1).</summary>
        public Vector4[] ropes;
        public byte[] ropeEnds;
        /// <summary>Every loaded Poolrooms chunk (PoolCirculation and PoolSounds read their fittings).</summary>
        public static readonly HashSet<PoolChunk> All = new HashSet<PoolChunk>();

        public void Set(Vector2Int c, int s, Color32[] data, Color32[] data2, Vector4[] fit = null, Vector3[] dirs = null)
        { chunk = c; storey = s; cells = data; cells2 = data2; fittings = fit; fittingDirs = dirs; }

        /// <summary>A fitting's world position and direction.</summary>
        public Vector3 FittingAt(int i) { var v = fittings[i]; return transform.TransformPoint(new Vector3(v.x, v.y, v.z)); }

        void OnEnable() { All.Add(this); if (PoolWorld.Instance) PoolWorld.Instance.Write(this); }
        void OnDisable() => All.Remove(this);
        void OnDestroy() { All.Remove(this); if (PoolWorld.Instance) PoolWorld.Instance.Clear(this); }
    }
}
