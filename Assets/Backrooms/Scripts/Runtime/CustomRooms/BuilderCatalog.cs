using System.Collections.Generic;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Build mode's props catalog (round 17, step C): every piece the builder can place, each model and fabric its own
    /// entry with a rendered picture, in categories. An entry is a prop kind, a style (which model: <see cref="Prop.style"/>)
    /// and a size. Pictures are rendered once, the first time FURNISH is opened, with the developer menu's furniture
    /// (ItemCatalog) in a little studio far below the room.
    /// </summary>
    public static class BuilderCatalog
    {
        public class Item
        {
            public string name, category;
            public PropKind kind;
            public int style;
            public Vector3 size;
            public string source;              // the ItemCatalog entry it is pictured with
            public Texture picture;
        }

        public static readonly string[] Categories = { "ALL", "SEATING", "TABLES", "STORAGE", "LIGHTS", "WALL" };
        static List<Item> _items;
        static bool _pictured;

        /// <summary>Forget the list and its pictures (a new builder session: the pictures went with the last scene).</summary>
        public static void Reset()
        {
            if (_items != null) foreach (var it in _items) if (it.picture is RenderTexture rt && rt) rt.Release();
            _items = null; _pictured = false;
        }

        public static List<Item> Items(OfficeBiomeAssets a)
        {
            if (_items != null) return _items;
            _items = new List<Item>();
            int fabrics = Mathf.Max(1, a.sofaFabrics != null ? a.sofaFabrics.Length : 1);
            string[] fabricNames = { "mustard", "teal", "rose" };
            string Fabric(int f) => f < fabricNames.Length ? fabricNames[f] : "fabric " + (f + 1);
            if (a.sofaPrefabs != null)
                for (int i = 0; i < a.sofaPrefabs.Length; i++)
                {
                    if (!a.sofaPrefabs[i]) continue;
                    string model = i / fabrics == 0 ? "Chesterfield sofa" : "Modern sofa";
                    _items.Add(new Item { name = $"{model}, {Fabric(i % fabrics)}", category = "SEATING", kind = PropKind.Sofa, style = i + 1, size = new Vector3(2.05f, 0.8f, 0.9f), source = $"{model} {i % fabrics + 1}" });
                }
            if (a.floralSofaPrefab)
                _items.Add(new Item { name = "Floral sofa", category = "SEATING", kind = PropKind.Sofa, style = Prop.FloralSofaStyle, size = new Vector3(2.25f, 0.94f, 0.93f), source = "Floral sofa" });
            if (a.armchairPrefabs != null)
                for (int i = 0; i < a.armchairPrefabs.Length; i++)
                    if (a.armchairPrefabs[i]) _items.Add(new Item { name = $"Armchair, {Fabric(i)}", category = "SEATING", kind = PropKind.Armchair, style = i + 1, size = new Vector3(0.95f, 0.85f, 0.9f), source = $"Armchair {i + 1}" });
            _items.Add(new Item { name = "Office chair", category = "SEATING", kind = PropKind.OfficeChair, size = new Vector3(0.6f, 1.0f, 0.6f), source = "Office chair" });
            _items.Add(new Item { name = "Conference table", category = "TABLES", kind = PropKind.ConferenceTable, size = new Vector3(2.4f, 0.74f, 1.1f), source = "Conference table" });
            foreach (var (label, size) in new[] { ("small", new Vector3(0.4f, 0.28f, 0.3f)), ("medium", new Vector3(0.5f, 0.36f, 0.4f)), ("large", new Vector3(0.62f, 0.45f, 0.48f)) })
                _items.Add(new Item { name = $"Box, {label}", category = "STORAGE", kind = PropKind.Box, size = size, source = $"Cardboard box ({label})" });
            _items.Add(new Item { name = "Pallet", category = "STORAGE", kind = PropKind.Pallet, size = new Vector3(1.2f, 1.6f, 1.0f), source = "Pallet" });
            _items.Add(new Item { name = "Storage rack", category = "STORAGE", kind = PropKind.StorageRack, size = new Vector3(2.3f, 2.4f, 0.95f) });
            _items.Add(new Item { name = "Floor lamp", category = "LIGHTS", kind = PropKind.FloorLamp, size = new Vector3(0.4f, 1.6f, 0.4f), source = "Floor lamp" });
            _items.Add(new Item { name = "Wall clock", category = "WALL", kind = PropKind.WallClock, size = new Vector3(0.36f, 2.1f, 0.06f) });
            return _items;
        }

        /// <summary>Renders the pictures (once; from Update, not OnGUI, where a camera renders nothing): each entry's furniture in
        /// a small lit studio, three-quarter view.</summary>
        public static void Picture(OfficeBiomeAssets a)
        {
            if (_pictured) return;
            _pictured = true;
            var items = Items(a);
            var entries = ItemCatalog.Build(a, null);
            var origin = new Vector3(0f, -400f, 0f);
            var studio = new GameObject("CatalogStudio");
            var camGo = new GameObject("CatalogCamera"); camGo.transform.SetParent(studio.transform);
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; cam.fieldOfView = 28f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.16f, 0.17f, 1f);
            var data = cam.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.renderShadows = false;
            Light MakeLight(Vector3 euler, float intensity)
            {
                var go = new GameObject("CatalogLight"); go.transform.SetParent(studio.transform);
                go.transform.rotation = Quaternion.Euler(euler);
                var l = go.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = intensity; l.shadows = LightShadows.None;
                go.AddComponent<UniversalAdditionalLightData>().renderingLayers = 0xFFFFFFFFu;
                return l;
            }
            var lights = new[] { MakeLight(new Vector3(40f, -35f, 0f), 1.5f), MakeLight(new Vector3(15f, 150f, 0f), 0.5f) };
            foreach (var it in items)
            {
                var e = it.source == null ? null : entries.Find(x => x.name == it.source);
                if (e == null) continue;
                GameObject go = null;
                try { go = e.spawn(origin, Quaternion.Euler(0f, 205f, 0f), true); }
                catch (System.Exception ex) { Debug.LogWarning("[Backrooms] Catalog picture for " + it.name + ": " + ex.Message); }
                if (!go) continue;
                foreach (var rb in go.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
                var b = new Bounds(origin, Vector3.zero); bool first = true;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    r.renderingLayerMask = 0xFFFFFFFFu;
                    if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                }
                float radius = Mathf.Max(0.05f, b.extents.magnitude);
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
                cam.transform.position = b.center + Quaternion.Euler(20f, 0f, 0f) * Vector3.back * dist;
                cam.transform.LookAt(b.center);
                var rt = new RenderTexture(192, 150, 24, RenderTextureFormat.ARGB32) { name = "Catalog_" + it.name, antiAliasing = 4 };
                rt.Create();
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                it.picture = rt;
                go.SetActive(false);                  // (gone only at the end of the frame: out of the next picture now)
                Object.Destroy(go);
            }
            foreach (var l in lights) l.enabled = false;              // (gone at the end of the frame: no flash on the room)
            Object.Destroy(studio);
        }
    }
}
