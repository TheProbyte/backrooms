using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The game's look: Overflow, the painted one. (The earlier realistic "Classic" look was a setting until 2026-11-09,
    /// when the owner retired it: Docs/DesignHistory.md.) The biome asset still names the old materials in its slots;
    /// <see cref="Biome"/> hands the streamer a copy with the style set's materials in place, so everything made from
    /// them (stained and cracked tiles, odd carpet tiles, troffers, the light grid's surface colours) follows. Saves
    /// name materials by their slot. Remade models sit under Resources/Overflow/ beside the ones not remade yet.
    /// </summary>
    public static class ArtStyle
    {
        public enum Look { Overflow = 1 }

        static StyleSet s_set;
        static readonly Dictionary<OfficeBiomeAssets, OfficeBiomeAssets> s_clones = new Dictionary<OfficeBiomeAssets, OfficeBiomeAssets>();

        /// <summary>(Kept for its callers: there is one look now, so nothing is forced.)</summary>
        public static ForcedLook Forcing(Look look) => default;
        public readonly struct ForcedLook : IDisposable { public void Dispose() { } }

        public static Look Current => Look.Overflow;

        /// <summary>Tells the shaders drawn in code (the Poolrooms' tiles and water) which look it is. Main thread only.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void SetShaderGlobals() => Shader.SetGlobalFloat("_BROverflow", 1f);

        /// <summary>
        /// A Resources asset in the current look: in Overflow its remake under Resources/Overflow/ (written there by the
        /// Blender scripts' <c>--style overflow</c>) when there is one, else the old one. Models remade in the new look sit
        /// beside the old ones this way, and loaders need only call this instead of Resources.Load.
        /// </summary>
        public static T Load<T>(string path) where T : UnityEngine.Object
        {
            if (Current == Look.Overflow)
            {
                var o = Resources.Load<T>("Overflow/" + path);
                if (o) return o;
            }
            return Resources.Load<T>(path);
        }

        /// <summary>
        /// Where a remade model's files are in the current look: "Overflow/" + path when its remake is there (Overflow),
        /// else path. A model's mesh, bake and texel density are then all read from the one place (their UVs match).
        /// </summary>
        public static string Path(string path)
        {
            if (Current == Look.Overflow && Resources.Load<GameObject>("Overflow/" + path)) return "Overflow/" + path;
            return path;
        }

        /// <summary>The current look's style set (null for Classic, or if the asset is missing).</summary>
        public static StyleSet Set
        {
            get
            {
                if (!s_set) s_set = Resources.Load<StyleSet>("Style/Overflow");
                return s_set;
            }
        }

        /// <summary>The hub's materials and prefabs: the world's own copy (one look).</summary>
        public static OfficeBiomeAssets OverflowBiome(OfficeBiomeAssets source, OfficeBiomeAssets worldCopy) =>
            !source || !Application.isPlaying ? source : worldCopy;

        /// <summary>
        /// The biome's materials in the current look: the asset itself (Classic, edit mode), or a copy with the style set's
        /// materials in its slots. Made afresh each time a world loads (the last one's copy is dropped), so it always holds
        /// the materials as they are now, and every material cache keyed by the biome rebuilds for the new look.
        /// </summary>
        public static OfficeBiomeAssets Biome(OfficeBiomeAssets a)
        {
            if (!a || !Application.isPlaying) return a;
            foreach (var kv in s_clones) if (kv.Value == a) { a = kv.Key; break; }   // given a copy: start from its source
            if (s_clones.TryGetValue(a, out var old))
            {
                s_clones.Remove(a);
                if (old) UnityEngine.Object.Destroy(old);
            }
            var set = Set;
            if (!set) return a;
            var c = MakeClone(a, set);
            s_clones[a] = c;
            return c;
        }

        static OfficeBiomeAssets MakeClone(OfficeBiomeAssets a, StyleSet set)
        {
            if (!set) return a;
            var c = UnityEngine.Object.Instantiate(a);
            c.name = a.name + " (Overflow)";
            c.hideFlags = HideFlags.DontSave;
            foreach (var f in typeof(OfficeBiomeAssets).GetFields())
            {
                if (f.FieldType == typeof(Material)) f.SetValue(c, set.To((Material)f.GetValue(a)));
                else if (f.FieldType == typeof(GameObject)) f.SetValue(c, set.To((GameObject)f.GetValue(a)));
                else if (f.FieldType == typeof(GameObject[]) && f.GetValue(a) is GameObject[] gs)
                {
                    var copy = new GameObject[gs.Length];
                    for (int i = 0; i < gs.Length; i++) copy[i] = set.To(gs[i]);
                    f.SetValue(c, copy);
                }
                else if (f.FieldType == typeof(Material[]) && f.GetValue(a) is Material[] ms)
                {
                    var copy = new Material[ms.Length];
                    for (int i = 0; i < ms.Length; i++) copy[i] = set.To(ms[i]);
                    f.SetValue(c, copy);
                }
            }
            return c;
        }
    }
}
