using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Which look the world is built in: Classic (the realistic materials) or Overflow (the new stylised ones). Chosen when a
    /// world loads: <see cref="Biome"/> hands the streamer a copy of the biome's material list with the style set's
    /// materials in place, so everything made from them (stained and cracked tiles, odd carpet tiles, troffers, the light
    /// grid's surface colours) follows. Saves name materials by their slot, so they load in either look.
    /// Saved in PlayerPrefs; <c>-art-style classic|overflow</c> on the command line picks one for that run only (the
    /// benchmark's A/B).
    /// </summary>
    public static class ArtStyle
    {
        public enum Look { Classic = 0, Overflow = 1 }

        const string Key = "Backrooms.ArtStyle";
        static bool s_read;
        static Look s_look;
        static StyleSet s_set;
        static readonly Dictionary<OfficeBiomeAssets, OfficeBiomeAssets> s_clones = new Dictionary<OfficeBiomeAssets, OfficeBiomeAssets>();

        public static Look Current
        {
            get { if (!s_read) Read(); return s_look; }
            set { s_read = true; s_look = value; PlayerPrefs.SetInt(Key, (int)value); PlayerPrefs.Save(); SetShaderGlobals(); }
        }


        /// <summary>Tells the shaders drawn in code (the Poolrooms' tiles and water) which look it is. Main thread only.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void SetShaderGlobals() => Shader.SetGlobalFloat("_BROverflow", Current == Look.Overflow ? 1f : 0f);

        static void Read()
        {
            s_read = true;
            s_look = (Look)Mathf.Clamp(PlayerPrefs.GetInt(Key, (int)Look.Overflow), 0, 1);
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-art-style");
            if (i >= 0 && i + 1 < args.Length)
                s_look = args[i + 1].Equals("classic", StringComparison.OrdinalIgnoreCase) ? Look.Classic : Look.Overflow;
        }

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
                if (Current != Look.Overflow) return null;
                if (!s_set) s_set = Resources.Load<StyleSet>("Style/Overflow");
                return s_set;
            }
        }

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
            s_clones[a] = c;
            return c;
        }
    }
}
