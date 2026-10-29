using System.Collections.Generic;
using Backrooms.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>The colliders a chunk was built with from its box list (walls, posts, headers, fixed props), so they can be swapped.</summary>
    public sealed class ChunkBoxes : MonoBehaviour
    {
        public readonly List<BoxCollider> boxes = new List<BoxCollider>();
    }

    /// <summary>
    /// Rebuilding a loaded chunk's walls (a wall panel in it was broken: the panel draws itself from then on). A fresh
    /// builder prepares the chunk again off the main thread; only its walls, baseboards and shadow boxes, and the colliders
    /// that changed, replace the chunk's. Everything else in it (lights, props, doors, items) is left alone.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        void MakeRenderer(Transform root, GeneratedChunkResources resources, Material mat, uint mask, MeshWriter writer)
        {
            var go = new GameObject(mat.name + "_" + mask.ToString("X2"));
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = resources.Own(writer.ToMesh(go.name));
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.renderingLayerMask = mask;
            if (mat == _a.lensOn || mat == MaterialMaps.TrofferOn || (_a.domeOn && mat == _a.domeOn) || (_a.stripOn && mat == _a.stripOn) || mat == _a.lampShadeOn) litLensRenderers.Add(r);
            bool architecture = mat == _a.wallpaper || mat == _a.paintedWall || mat == _a.referenceCream || mat == _a.referenceOlive || mat == _a.baseboard || mat == _a.halfWallCap;
            bool caster = !architecture && mat != _a.lensOn && mat != _a.lensOff && !MaterialMaps.IsTroffer(mat) && !MaterialMaps.IsCeilingVariant(mat) && mat != _a.ceilingGrid && mat != _a.carpet && mat != _a.ceilingTile && mat != _a.lampShadeOn
                          && mat != _a.domeOn && mat != _a.domeOff && mat != _a.ceilingPlaster && mat != _a.stripOn && mat != _a.stripOff
                          && mat != _a.referenceCarpet && mat != _a.cardboard && mat != _a.boxTape && mat != _a.boxPrint && mat != _a.rackUpright && mat != _a.rackBeam && mat != _a.pallet
                          && mat != _a.fixtureHousing                 // light fittings: black blotches on the wall or header beside them
                          && !WallPlateModels.IsPlate(mat);   // warehouse dressing: proxies cast the shadows
            r.shadowCastingMode = mask == ProxyMask || mask == WallProxyMask ? ShadowCastingMode.ShadowsOnly : caster ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (Pool) r.shadowCastingMode = ShadowCastingMode.Off;      // the Poolrooms' sun is traced in the shader, not shadow-mapped
            if (mask == WallProxyMask)
            {
                if (WallProxies.Count > 4000) WallProxies.RemoveWhere(x => !x);   // unloaded chunks
                r.renderingLayerMask = ProxyMask;
                r.enabled = WallShadows;
                WallProxies.Add(r);
            }
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>Meshes that a broken wall changes: the walls themselves, baseboards, and the shadow boxes (drawn in the baseboard's material).</summary>
        bool WallWriter(Material m) =>
            m == MaterialMaps.For(_a.wallpaper) || m == MaterialMaps.For(_a.paintedWall) || m == MaterialMaps.For(_a.referenceCream) ||
            m == MaterialMaps.For(_a.referenceOlive) || m == MaterialMaps.For(_a.baseboard) || m == MaterialMaps.For(_a.halfWallCap) ||
            m == _a.wallpaper || m == _a.baseboard || (MaterialMaps.PaintFresh && m == MaterialMaps.PaintFresh);

        /// <summary>After <see cref="BuildData"/> for the same chunk: puts this builder's walls and colliders into the loaded chunk.</summary>
        public void ReplaceWalls(GameObject root)
        {
            if (!root) return;
            var resources = root.GetComponent<GeneratedChunkResources>();
            if (!resources) resources = root.AddComponent<GeneratedChunkResources>();
            var keep = new HashSet<string>();
            var children = new Dictionary<string, Transform>();
            foreach (Transform t in root.transform) if (!children.ContainsKey(t.name)) children[t.name] = t;
            foreach (var kv in _writers)
            {
                var (mat, mask) = kv.Key;
                if (!WallWriter(mat)) continue;
                string name = mat.name + "_" + mask.ToString("X2");
                keep.Add(name);
                children.TryGetValue(name, out var child);
                if (kv.Value.VertexCount == 0) { if (child) Object.Destroy(child.gameObject); continue; }
                if (child && child.TryGetComponent<MeshFilter>(out var mf))
                {
                    var old = mf.sharedMesh;
                    mf.sharedMesh = resources.Own(kv.Value.ToMesh(name));
                    resources.Drop(old);
                }
                else MakeRenderer(root.transform, resources, mat, mask, kv.Value);
            }
            // Wall meshes that are now empty (named as the writers name them: material, then the light mask in hex).
            foreach (var kv in children)
            {
                if (keep.Contains(kv.Key) || !kv.Value.TryGetComponent<MeshRenderer>(out var r) || !r.sharedMaterial) continue;
                string prefix = r.sharedMaterial.name + "_";
                if (!kv.Key.StartsWith(prefix) || !uint.TryParse(kv.Key.Substring(prefix.Length), System.Globalization.NumberStyles.HexNumber, null, out _)) continue;
                if (WallWriter(r.sharedMaterial)) Object.Destroy(kv.Value.gameObject);
            }

            ReplaceWallWear(root, resources);                    // marks on a wall now broken go with it

            // Colliders: keep the ones that are the same, swap the rest.
            var have = root.GetComponent<ChunkBoxes>();
            if (!have) return;
            var want = new List<(Vector3 c, Vector3 s)>(_boxes);
            var stay = new List<BoxCollider>(have.boxes.Count);
            foreach (var b in have.boxes)
            {
                if (!b) continue;
                int found = -1;
                for (int i = 0; i < want.Count; i++)
                    if ((want[i].c - b.center).sqrMagnitude < 1e-8f && (want[i].s - b.size).sqrMagnitude < 1e-8f) { found = i; break; }
                if (found >= 0) { want.RemoveAt(found); stay.Add(b); }
                else Object.Destroy(b);
            }
            foreach (var (c, s) in want)
            {
                var b = root.AddComponent<BoxCollider>();
                b.center = c; b.size = s;
                stay.Add(b);
            }
            have.boxes.Clear(); have.boxes.AddRange(stay);
        }
    }
}
