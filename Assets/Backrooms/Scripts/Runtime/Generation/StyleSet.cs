using System;
using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// A look for the world (2026-11-06, Docs/StyleRollout.md): the stylised twin of each world material it replaces,
    /// and textures for the materials the game makes in code (the warehouse's concrete). Resources/Style/Overflow.asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Backrooms/Style Set")]
    public class StyleSet : ScriptableObject
    {
        [Serializable]
        public struct Swap
        {
            public Material from, to;
        }

        [Serializable]
        public struct PrefabSwap
        {
            public GameObject from, to;
        }

        [Tooltip("Each world material (OfficeBiomeAssets) and the one drawn in its place in this look.")]
        public List<Swap> materials = new List<Swap>();

        [Tooltip("Each furniture prefab (OfficeBiomeAssets: sofas, armchairs, the office chair, lamp, table, clock) and its remake.")]
        public List<PrefabSwap> prefabs = new List<PrefabSwap>();

        [Header("Warehouse concrete (MaterialMaps.Concrete, made in code from the carpet)")]
        public Texture2D concreteAlbedo;
        public Texture2D concreteNormal;
        public float concreteBump = 0.5f;
        public Color concreteTint = Color.white;
        [Range(0f, 1f)] public float concreteSmoothness = 0.22f;

        public Material To(Material m)
        {
            if (!m) return m;
            for (int i = 0; i < materials.Count; i++)
                if (materials[i].from == m && materials[i].to) return materials[i].to;
            return m;
        }

        public GameObject To(GameObject g)
        {
            if (!g) return g;
            for (int i = 0; i < prefabs.Count; i++)
                if (prefabs[i].from == g && prefabs[i].to) return prefabs[i].to;
            return g;
        }
    }
}
