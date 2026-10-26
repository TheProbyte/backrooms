using System;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Everything that can be spawned from the developer menu (<see cref="ItemsMenu"/>): the items and every piece of
    /// furniture the world generates, built the same way the chunk builder builds them. Furniture becomes a moved
    /// prop (MovablePropManager), so it stays where it is put through chunk unloads.
    /// </summary>
    public static class ItemCatalog
    {
        public enum Placement { Floor, Wall }

        public class Entry
        {
            public string name, category;
            public Placement placement;
            /// <summary>Clearance in front of the player, metres (about half the thing's size plus room to stand).</summary>
            public float clearance = 1f;
            /// <summary>Builds it at a point facing a direction; preview: only for the menu's picture (not kept).</summary>
            public Func<Vector3, Quaternion, bool, GameObject> spawn;
            public Texture thumbnail;
        }

        static ulong s_next = 1;
        static ulong NextId() => 0xDE00000000000000ul | (s_next++ << 16);

        public static List<Entry> Build(OfficeBiomeAssets a, WorldStreamer streamer)
        {
            var list = new List<Entry>();
            list.Add(new Entry
            {
                name = "Spray paint (red)", category = "ITEMS", clearance = 0.75f,
                spawn = (p, r, preview) => SprayCan.Create(p, r).gameObject,
            });
            if (!a) return list;

            uint MaskAt(Vector3 p)
            {
                var L = streamer ? streamer.LayoutAt(WorldStreamer.ChunkOf(p)) : null;
                if (L == null) return 1u;
                int x = Mathf.Clamp(Mathf.FloorToInt(p.x / OfficeBiome.CellSize) - L.coord.x * L.N, 0, L.N - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt(p.z / OfficeBiome.CellSize) - L.coord.y * L.N, 0, L.N - 1);
                return L.style == ZoneStyle.Pool ? 1u : LightLayers.LayerBit(L.RoomAt(x, z).color);
            }

            GameObject Movable(MovableKind kind, Vector3 p, Quaternion r, bool preview, Action<MovableSpec> fill)
            {
                var spec = new MovableSpec { id = NextId(), kind = kind, renderMask = preview ? 0xFFFFFFFFu : MaskAt(p), proxyMaterial = a.baseboard, position = p, yaw = r.eulerAngles.y };
                fill(spec);
                var mgr = MovablePropManager.Instance;
                var prop = MovableProp.Spawn(spec, !preview && mgr ? mgr.transform : null, p, r);
                if (!preview && mgr) mgr.Detach(prop);             // owned by the moved list from the start
                return prop.gameObject;
            }

            // Sofas: every model in every fabric (the prefab list is [model * fabricCount + fabric]).
            if (a.sofaPrefabs != null)
            {
                int fabrics = Mathf.Max(1, a.sofaFabrics != null ? a.sofaFabrics.Length : 1);
                for (int i = 0; i < a.sofaPrefabs.Length; i++)
                {
                    var prefab = a.sofaPrefabs[i];
                    if (!prefab) continue;
                    string model = i / fabrics == 0 ? "Chesterfield sofa" : "Modern sofa";
                    list.Add(new Entry { name = $"{model} {i % fabrics + 1}", category = "FURNITURE", clearance = 1.6f,
                        spawn = (p, r, pv) => Movable(MovableKind.Sofa, p, r, pv, s => s.AddPrefab(prefab, Vector3.zero)) });
                }
            }
            if (a.armchairPrefabs != null)
                for (int i = 0; i < a.armchairPrefabs.Length; i++)
                {
                    var prefab = a.armchairPrefabs[i];
                    if (!prefab) continue;
                    list.Add(new Entry { name = $"Armchair {i + 1}", category = "FURNITURE", clearance = 1.2f,
                        spawn = (p, r, pv) => Movable(MovableKind.Armchair, p, r, pv, s => s.AddPrefab(prefab, Vector3.zero)) });
                }
            if (a.officeChairPrefab)
                list.Add(new Entry { name = "Office chair", category = "FURNITURE", clearance = 1f,
                    spawn = (p, r, pv) => Movable(MovableKind.OfficeChair, p, r, pv, s => s.AddPrefab(a.officeChairPrefab, Vector3.zero)) });
            if (a.tableModulePrefab && a.tableLegPrefab)
                list.Add(new Entry { name = "Conference table", category = "FURNITURE", clearance = 1.7f,
                    spawn = (p, r, pv) => Movable(MovableKind.Table, p, r, pv, s =>
                    {
                        const float L = 2.0f;
                        s.size = new Vector3(L, 0.74f, 1.1f);
                        s.AddPrefab(a.tableModulePrefab, Vector3.zero, 0f, new Vector3(L, 1f, 1f));
                        for (int i = -1; i <= 1; i += 2) s.AddPrefab(a.tableLegPrefab, new Vector3(i * L * 0.32f, 0, 0));
                    }) });
            var board = a.cardboard ? a.cardboard : a.wood;
            if (board)
                foreach (var (label, size) in new[] { ("small", new Vector3(0.4f, 0.28f, 0.3f)), ("medium", new Vector3(0.5f, 0.36f, 0.4f)), ("large", new Vector3(0.62f, 0.45f, 0.48f)) })
                    list.Add(new Entry { name = $"Cardboard box ({label})", category = "FURNITURE", clearance = 0.9f,
                        spawn = (p, r, pv) => Movable(MovableKind.Box, p, r, pv, s =>
                        {
                            s.size = size; s.variant = UnityEngine.Random.Range(1, 1000) * 8 + 1;       // never the heavy paper-filled kind
                            s.cardboard = true;
                            s.materials = new[] { board, a.boxTape ? a.boxTape : board, a.boxPrint ? a.boxPrint : board };
                        }) });
            if (a.floorLampPrefab)
                list.Add(new Entry { name = "Floor lamp", category = "FURNITURE", clearance = 0.9f,
                    spawn = (p, r, pv) => Static(a.floorLampPrefab, p, r, pv, new Vector3(0.45f, 1.7f, 0.45f)) });
            if (a.wallClockPrefab)
                list.Add(new Entry { name = "Wall clock", category = "FURNITURE", placement = Placement.Wall,
                    spawn = (p, r, pv) => Static(a.wallClockPrefab, p, r, pv, Vector3.zero) });
            return list;
        }

        /// <summary>A fixed (not movable) piece: the model and, if given, one box collider.</summary>
        static GameObject Static(GameObject prefab, Vector3 p, Quaternion r, bool preview, Vector3 box)
        {
            var root = new GameObject(prefab.name);
            root.transform.SetPositionAndRotation(p, r);
            var go = UnityEngine.Object.Instantiate(prefab, root.transform, false);
            go.transform.localRotation = prefab.transform.localRotation;
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            if (box != Vector3.zero) { var bc = root.AddComponent<BoxCollider>(); bc.size = box; bc.center = Vector3.up * box.y * 0.5f; }
            if (!preview)
            {
                foreach (var rr in root.GetComponentsInChildren<Renderer>()) rr.renderingLayerMask = 0xFFFFFFFFu;
                PoolLitSwitch.Attach(root);
                var mgr = MovablePropManager.Instance;
                if (mgr) root.transform.SetParent(mgr.transform, true);
            }
            return root;
        }
    }
}
