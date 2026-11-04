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

        /// <summary>A saved world uses this id: the next spawned piece gets a later one.</summary>
        public static void ReserveId(ulong id)
        {
            if ((id >> 56) != 0xDEul) return;
            ulong n = (id >> 16) & 0xFFFFFFFFFFul;
            if (n >= s_next) s_next = n + 1;
        }

        public static List<Entry> Build(OfficeBiomeAssets a, WorldStreamer streamer)
        {
            var list = new List<Entry>();
            list.Add(new Entry
            {
                name = "Spray paint (red)", category = "ITEMS", clearance = 0.75f,
                spawn = (p, r, preview) => SprayCan.Create(p, r).gameObject,
            });
            list.Add(new Entry { name = "Chalk", category = "ITEMS", clearance = 0.7f, spawn = (p, r, pv) => Chalk.Create(p + Vector3.up * 0.01f, r * Quaternion.Euler(0f, 90f, 0f)).gameObject });
            list.Add(new Entry { name = "Blue tape", category = "ITEMS", clearance = 0.75f, spawn = (p, r, pv) => TapeRoll.Create(p, r).gameObject });
            list.Add(new Entry { name = "Desk lamp", category = "ITEMS", clearance = 0.9f, spawn = (p, r, pv) => DeskLamp.Create(p, r).gameObject });
            list.Add(new Entry { name = "Extension cord", category = "ITEMS", clearance = 0.9f, spawn = (p, r, pv) => CordBox.Create(p, r).gameObject });
            list.Add(new Entry { name = "Rubber chicken", category = "ITEMS", clearance = 0.8f, spawn = (p, r, pv) => RubberChicken.Create(p + Vector3.up * 0.08f, r * Quaternion.Euler(0f, 90f, 0f)).gameObject });
            // Lying on its side, the handle toward you.
            list.Add(new Entry { name = "Sledgehammer", category = "ITEMS", clearance = 1f, spawn = (p, r, pv) => Sledgehammer.Create(p + Vector3.up * 0.04f, r * Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, 90f, 0f)).gameObject });
            // Lying on its side, the handle toward you; the nails' open flap away from you.
            list.Add(new Entry { name = "Hammer", category = "ITEMS", clearance = 0.8f, spawn = (p, r, pv) => Hammer.Create(p + Vector3.up * 0.016f, r * Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, 90f, 0f)).gameObject });
            list.Add(new Entry { name = "Box of nails", category = "ITEMS", clearance = 0.7f, spawn = (p, r, pv) => Nails.Create(p, r * Quaternion.Euler(0f, 180f, 0f)).gameObject });
            // Its front toward you; the cassette lying flat, its label up.
            list.Add(new Entry { name = "Boombox", category = "ITEMS", clearance = 0.9f, spawn = (p, r, pv) => Boombox.Create(p + Vector3.up * 0.005f, r * Quaternion.Euler(0f, 180f, 0f)).gameObject });
            list.Add(new Entry { name = "Cassette (lunch break mix)", category = "ITEMS", clearance = 0.7f, spawn = (p, r, pv) => Cassette.Create(p + Vector3.up * 0.005f, r * Quaternion.Euler(0f, 180f, 0f)).gameObject });
            // A backpack, its front toward you (navy, red, forest green: one each).
            for (int c = 0; c < Backpack.Colours.Length; c++)
            {
                int col = c;
                list.Add(new Entry { name = "Backpack (" + Backpack.Colours[c].ToLowerInvariant() + ")", category = "ITEMS", clearance = 0.8f,
                                     spawn = (p, r, pv) => Backpack.Create(p + Vector3.up * 0.005f, r * Quaternion.Euler(0f, 180f, 0f), col).gameObject });
            }
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
            // The floral sofa (round 15, the owner's reference; Tools/Blender/build_floral_sofa.py, Editor/FloralSofaBuilder).
            var floralSofa = a.floralSofaPrefab ? a.floralSofaPrefab : Resources.Load<GameObject>("Furniture/Sofa_Floral");
            if (floralSofa)
                list.Add(new Entry { name = "Floral sofa", category = "FURNITURE", clearance = 1.7f,
                    spawn = (p, r, pv) => Movable(MovableKind.Sofa, p, r, pv, s => s.AddPrefab(floralSofa, Vector3.zero)) });
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
                        s.AddTable(a.tableModulePrefab, a.tableLegPrefab, L);
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
            var palletWood = a.pallet ? a.pallet : a.wood;
            if (palletWood)
                list.Add(new Entry { name = "Pallet", category = "FURNITURE", clearance = 1.2f,
                    spawn = (p, r, pv) => Movable(MovableKind.Pallet, p, r, pv, s => s.materials = new[] { palletWood }) });
            if (a.floorLampPrefab)
                list.Add(new Entry { name = "Floor lamp", category = "ITEMS", clearance = 0.9f, spawn = (p, r, pv) => FloorLamp.Create(p, r).gameObject });
            return list;
        }

        /// <summary>A fixed (not movable) piece: the model and, if given, one box collider. Saved with the world under its entry name.</summary>
        static GameObject Static(string entry, GameObject prefab, Vector3 p, Quaternion r, bool preview, Vector3 box)
        {
            var root = new GameObject(prefab.name);
            root.transform.SetPositionAndRotation(p, r);
            var go = UnityEngine.Object.Instantiate(prefab, root.transform, false);
            MaterialMaps.Apply(go);
            go.transform.localRotation = prefab.transform.localRotation;
            foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
            if (box != Vector3.zero) { var bc = root.AddComponent<BoxCollider>(); bc.size = box; bc.center = Vector3.up * box.y * 0.5f; }
            if (!preview)
            {
                foreach (var rr in root.GetComponentsInChildren<Renderer>()) rr.renderingLayerMask = 0xFFFFFFFFu;
                PoolLitSwitch.Attach(root);
                root.AddComponent<Saves.PlacedThing>().catalogName = entry;
                var mgr = MovablePropManager.Instance;
                if (mgr) root.transform.SetParent(mgr.transform, true);
            }
            return root;
        }
    }
}
