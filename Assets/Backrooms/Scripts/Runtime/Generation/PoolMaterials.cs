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

        public static void Ensure()
        {
            if (Tile && Water && Sky) return;
            Tile = Make("Backrooms/PoolTile", "Pool tiles");
            Water = Make("Backrooms/PoolWater", "Pool water");
            Sky = Make("Backrooms/PoolSky", "Pool sky");
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
        public Color32[] cells;

        public void Set(Vector2Int c, int s, Color32[] data) { chunk = c; storey = s; cells = data; }

        void OnEnable() { if (PoolWorld.Instance) PoolWorld.Instance.Write(this); }
        void OnDestroy() { if (PoolWorld.Instance) PoolWorld.Instance.Clear(this); }
    }
}
