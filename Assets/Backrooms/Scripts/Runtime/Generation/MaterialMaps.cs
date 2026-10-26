using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Normal maps for the world materials that had none (Resources/Textures/Generated, made by a script from their albedo
    /// or procedurally: wood, laminate, painted metal, plastic, box print, wall plates...). The project's .mat files are
    /// left as they are: at startup each such material gets an in-memory copy with its normal map, and everything that
    /// builds the world asks <see cref="For"/> for the copy (chunk meshes, props, furniture). The copies use the same
    /// shader keyword (_NORMALMAP) as the carpet and walls, so no new shader variant is needed.
    /// Built on the main thread before any chunk is prepared (WorldStreamer.Awake); <see cref="For"/> is a plain lookup,
    /// safe on the generator's worker threads.
    /// </summary>
    public static class MaterialMaps
    {
        static readonly Dictionary<Material, Material> s_map = new Dictionary<Material, Material>();
        static OfficeBiomeAssets s_for;

        /// <summary>Material name, generated normal map, normal strength.</summary>
        static readonly (string mat, string tex, float scale)[] Table =
        {
            ("M_Pallet", "M_Pallet_Normal", 1.0f), ("M_WoodDark", "M_WoodDark_Normal", 0.8f), ("M_HalfWallCap", "M_HalfWallCap_Normal", 0.7f),
            ("M_TableLaminate", "M_TableLaminate_Normal", 0.6f), ("M_BoxPrint", "M_BoxPrint_Normal", 1.0f), ("M_WallPlate", "M_WallPlate_Normal", 0.8f),
            ("M_Baseboard", "M_Baseboard_Normal", 0.6f), ("M_CeilingGrid", "M_CeilingGrid_Normal", 0.5f), ("M_FixtureHousing", "M_FixtureHousing_Normal", 0.5f),
            ("M_RackBeam", "M_RackBeam_Normal", 0.7f), ("M_LampMetal", "M_LampMetal_Normal", 0.6f), ("M_PropPlastic", "M_PropPlastic_Normal", 0.5f),
            ("M_BoxTape", "M_BoxTape_Normal", 0.6f), ("M_ClockFace", "M_ClockFace_Normal", 0.4f),
        };

        static readonly int BumpMap = Shader.PropertyToID("_BumpMap"), BumpScale = Shader.PropertyToID("_BumpScale");

        public static void Ensure(OfficeBiomeAssets a)
        {
            if (!a || a == s_for) return;
            s_for = a;
            s_map.Clear();
            var byName = new Dictionary<string, (Texture2D tex, float scale)>();
            foreach (var (mat, tex, scale) in Table)
            {
                var t = Resources.Load<Texture2D>("Textures/Generated/" + tex);
                if (t) byName[mat] = (t, scale);
            }
            void Add(Material m)
            {
                if (!m || s_map.ContainsKey(m) || !byName.TryGetValue(m.name, out var n)) return;
                if (!m.HasProperty(BumpMap) || m.GetTexture(BumpMap)) { s_map[m] = m; return; }
                var c = new Material(m) { name = m.name + " (normal map)", hideFlags = HideFlags.DontSave };
                c.SetTexture(BumpMap, n.tex);
                c.SetFloat(BumpScale, n.scale);
                c.EnableKeyword("_NORMALMAP");
                s_map[m] = c;
            }
            foreach (var f in typeof(OfficeBiomeAssets).GetFields())
            {
                var v = f.GetValue(a);
                if (v is Material m) Add(m);
                else if (v is Material[] ms) foreach (var x in ms) Add(x);
                else if (v is GameObject g) AddPrefab(g, Add);
                else if (v is GameObject[] gs) foreach (var x in gs) AddPrefab(x, Add);
            }
        }

        static void AddPrefab(GameObject g, System.Action<Material> add)
        {
            if (!g) return;
            foreach (var r in g.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) add(m);
        }

        /// <summary>The material to draw with: its copy with a normal map, or itself.</summary>
        public static Material For(Material m) => m && s_map.TryGetValue(m, out var c) ? c : m;

        /// <summary>Swaps a built object's materials for their copies (props and furniture made from prefabs).</summary>
        public static void Apply(GameObject go)
        {
            if (s_map.Count == 0 || !go) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++) { var c = For(mats[i]); if (c != mats[i]) { mats[i] = c; changed = true; } }
                if (changed) r.sharedMaterials = mats;
            }
        }
    }
}
