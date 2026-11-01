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
        public static Material Tread { get; private set; }

        public static void Ensure()
        {
            if (Tile && Water && Sky && Accent) return;
            Tile = Make("Backrooms/PoolTile", "Pool tiles");
            Water = Make("Backrooms/PoolWater", "Pool water");
            Sky = Make("Backrooms/PoolSky", "Pool sky");
            Accent = Make("Backrooms/PoolTile", "Pool accent tiles");
            if (Accent) { Accent.SetColor("_TileColor", new Color(0.17f, 0.42f, 0.4f)); Accent.SetColor("_DeckColor", new Color(0.17f, 0.42f, 0.4f)); Accent.SetColor("_GroutColor", new Color(0.55f, 0.62f, 0.6f)); }
            Zones = new Material[4];
            Zones[0] = Tile;
            Zones[1] = Zone("cream", new Color(0.88f, 0.86f, 0.78f), new Color(0.9f, 0.88f, 0.8f));
            Zones[2] = Zone("mint", new Color(0.66f, 0.82f, 0.72f), new Color(0.84f, 0.86f, 0.78f));
            Zones[3] = Zone("aqua", new Color(0.72f, 0.85f, 0.85f), new Color(0.86f, 0.88f, 0.84f));
            // Polished stainless / chrome reflects about 55-60 % head on (its F0), slightly cool; at 0.86 it read as white
            // plastic (2026-11-01, the owner: the rails "could use work visually").
            Chrome = Lit("Pool chrome", new Color(0.6f, 0.615f, 0.63f), 0.93f, 1f);
            Rubber = Lit("Pool rubber", new Color(0.05f, 0.05f, 0.055f), 0.35f, 0f);
            Tread = Lit("Pool ladder tread", new Color(0.78f, 0.8f, 0.8f), 0.4f, 0f);
            PlasticWhite = Lit("Pool plastic (white)", new Color(0.9f, 0.9f, 0.86f), 0.62f, 0f);
            PlasticBlue = Lit("Pool plastic (blue)", new Color(0.22f, 0.5f, 0.78f), 0.7f, 0f);
        }

        static Material Zone(string name, Color wall, Color deck)
        {
            var m = Make("Backrooms/PoolTile", "Pool tiles (" + name + ")");
            if (m) { m.SetColor("_TileColor", wall); m.SetColor("_DeckColor", deck); }
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

        public void Set(Vector2Int c, int s, Color32[] data, Color32[] data2) { chunk = c; storey = s; cells = data; cells2 = data2; }

        void OnEnable() { if (PoolWorld.Instance) PoolWorld.Instance.Write(this); }
        void OnDestroy() { if (PoolWorld.Instance) PoolWorld.Instance.Clear(this); }
    }
}
