using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The inside of a special room behind a crawl space (shell: OfficeChunkBuilder.CrawlSpaces.cs): its light, its
    /// furniture and the useful things lying in it, by template, from the room's own dice:
    ///  - 0 maintenance closet: a steel shelf with supplies, a bare bulb;
    ///  - 1 sealed storage: a shelf on the side, stacked boxes, a bare bulb;
    ///  - 2 old camp: a sleeping bag, a battery lantern left on, a few things, a tally chalked on the wall.
    /// Belongs to its chunk: everything here is made when the chunk loads and goes with it. Things lying here are loot
    /// (WorldItem.lootId): once picked up, they are taken for good (Saves.LootRegistry) and the room does not make them again.
    /// The light (shadowed, so it never shines through the floor into the rooms below) is on only while the camera is near.
    /// </summary>
    public class SpecialRoom : MonoBehaviour
    {
        public static readonly List<SpecialRoom> All = new List<SpecialRoom>();
        public Vector3 min, max, back, vent;
        public int template;
        Light _light;
        float _next;

        public Vector3 Centre => (min + max) * 0.5f;
        public static readonly string[] TemplateNames = { "maintenance closet", "sealed storage", "old camp" };

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public static SpecialRoom Create(Transform chunk, Vector3 min, Vector3 max, Vector3 back, Vector3 vent, int template, int seed, ulong id, OfficeBiomeAssets a)
        {
            var go = new GameObject("SpecialRoom_" + TemplateNames[Mathf.Clamp(template, 0, 2)]);
            go.transform.SetParent(chunk, true);
            go.transform.position = new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f);
            var r = go.AddComponent<SpecialRoom>();
            r.min = min; r.max = max; r.back = back; r.vent = vent; r.template = template;
            r.Dress(new System.Random(seed), id, a);
            return r;
        }

        // Room frame: f from the back wall (the vent) toward the front wall, s across, origin on the floor at the back
        // wall's middle. d: metres from the back wall, x: metres across from the middle.
        Vector3 F => back;
        Vector3 S => Vector3.Cross(Vector3.up, back);
        float Depth => Mathf.Abs(Vector3.Dot(max - min, back));
        float Width => Mathf.Abs(Vector3.Dot(max - min, S));
        Vector3 At(float d, float x, float y = 0f)
        {
            Vector3 c = Centre; c.y = min.y;
            return c + F * (d - Depth * 0.5f) + S * x + Vector3.up * y;
        }

        void Dress(System.Random rng, ulong id, OfficeBiomeAssets a)
        {
            float D = Depth, W = Width;
            float ventX = Vector3.Dot(vent - At(0f, 0f), S);
            var loot = new List<(Vector3 pos, Quaternion rot, string item)>();
            Quaternion facingBack = Quaternion.LookRotation(-F, Vector3.up);
            switch (template)
            {
                case 0:
                {
                    // Maintenance closet: a shelf against the front wall, supplies on it.
                    var shelfPos = At(D - 0.22f, Mathf.Clamp((float)(rng.NextDouble() - 0.5) * (W - 1.1f), -(W - 1.0f) * 0.5f, (W - 1.0f) * 0.5f));
                    Shelf(shelfPos, facingBack);
                    string[] pool = { "spraycan", "chalk", "tape", "extensioncord", "desklamp", "chalk", "tape" };
                    int n = 2 + rng.Next(3);
                    float[] decks = { 0.10f, 0.62f, 1.14f, 1.70f };
                    for (int i = 0; i < n; i++)
                    {
                        float deck = decks[1 + rng.Next(2)];
                        var p = shelfPos + S * ((float)rng.NextDouble() - 0.5f) * 0.65f + Vector3.up * (deck + 0.02f) + F * -0.02f;
                        loot.Add((p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), pool[rng.Next(pool.Length)]));
                    }
                    Bulb(At(D * 0.5f, 0f, max.y - min.y - 0.32f), a);
                    break;
                }
                case 1:
                {
                    // Sealed storage: shelf on a side wall, boxes stacked in the front corners.
                    float side = rng.Next(2) == 0 ? -1f : 1f;
                    var shelfPos = At(D * 0.55f, side * (W * 0.5f - 0.22f));
                    Shelf(shelfPos, Quaternion.LookRotation(-S * side, Vector3.up));
                    for (int i = 0, n = 2 + rng.Next(4); i < n; i++)
                    {
                        float bx = -side * (W * 0.5f - 0.35f - (i % 2) * 0.55f), bd = D - 0.32f - (i / 4) * 0.55f;
                        Box(At(bd, bx, 0.45f * ((i / 2) % 2)), new Vector3(0.6f, 0.45f, 0.5f), (float)rng.NextDouble() * 12f - 6f);
                    }
                    string[] pool = { "tape", "chalk", "spraycan", "desklamp", "extensioncord", "rubberchicken" };
                    for (int i = 0, n = 1 + rng.Next(3); i < n; i++)
                    {
                        var p = shelfPos + F * ((float)rng.NextDouble() - 0.5f) * 0.6f + Vector3.up * (0.62f + 0.52f * rng.Next(2) + 0.02f);
                        loot.Add((p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), pool[rng.Next(pool.Length)]));
                    }
                    Bulb(At(D * 0.4f, 0f, max.y - min.y - 0.32f), a);
                    break;
                }
                default:
                {
                    // Old camp: a sleeping bag along a side wall, a lantern by its head, a few things, a tally on the wall.
                    float side = ventX > 0f ? -1f : 1f;                      // away from the vent
                    var bagPos = At(D * 0.5f + 0.15f, side * (W * 0.5f - 0.45f));
                    Model("SleepingBag", bagPos, Quaternion.LookRotation(-F, Vector3.up), new Vector3(0.78f, 0.3f, 1.74f));
                    var lantern = At(D * 0.5f - 0.95f, side * (W * 0.5f - 0.3f));
                    Lantern(lantern, a);
                    string[] pool = { "rubberchicken", "chalk", "spraycan", "tape", "rubberchicken" };
                    for (int i = 0, n = 1 + rng.Next(3); i < n; i++)
                    {
                        var p = At(0.6f + (float)rng.NextDouble() * (D - 1.2f), -side * (float)rng.NextDouble() * (W * 0.5f - 0.4f), 0.06f);
                        loot.Add((p, Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f), pool[rng.Next(pool.Length)]));
                    }
                    Tally(rng, side);
                    break;
                }
            }
            // A sledgehammer lying along a wall: often in a maintenance closet, sometimes in sealed storage. Its own dice (so
            // what else lies in the room is as it always was), and last on the list (so the others keep their loot ids).
            if (template <= 1)
            {
                var hd = new System.Random(unchecked((int)(id ^ (id >> 29)) ^ 0x51ED6E));
                if (hd.NextDouble() < (template == 0 ? 0.45 : 0.25))
                {
                    float side = hd.Next(2) == 0 ? -1f : 1f, way = hd.Next(2) == 0 ? 1f : -1f;
                    // Its butt is the item's origin: the 0.66 m hammer lies centred along the side wall, clear of the ends.
                    float mid = D * 0.5f + ((float)hd.NextDouble() - 0.5f) * Mathf.Max(0f, D - 1.6f);
                    var at = At(mid - way * 0.33f, side * (W * 0.5f - 0.07f), 0.035f);
                    var along = F * way;
                    loot.Add((at, Quaternion.LookRotation(Vector3.Cross(along, Vector3.up), along), "sledgehammer"));     // on the head's side
                }
            }
            for (int i = 0; i < loot.Count; i++)
            {
                ulong lootId = (id * 0x9E3779B97F4A7C15ul) ^ (ulong)(i + 1) * 0xC2B2AE3D27D4EB4Ful;
                if (lootId == 0) lootId = 1;
                if (Saves.LootRegistry.IsTaken(lootId)) continue;
                var item = ItemFactory.Spawn(loot[i].item, loot[i].pos, loot[i].rot);
                if (!item) continue;
                item.lootId = lootId;
                item.transform.SetParent(transform, true);
            }
        }

        GameObject Model(string resource, Vector3 pos, Quaternion rot, Vector3 size)
        {
            var root = new GameObject(resource);
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(pos, rot);
            ItemModels.Attach(root.transform, resource, size, "ShelfMetal");
            foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            return root;
        }

        void Shelf(Vector3 pos, Quaternion rot)
        {
            var s = Model("UtilityShelf", pos, rot, new Vector3(0.9f, 1.8f, 0.4f));
            // Each deck holds things; the uprights keep you out.
            foreach (float y in new[] { 0.10f, 0.62f, 1.14f, 1.70f })
            {
                var bc = s.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, y - 0.015f, 0f); bc.size = new Vector3(0.9f, 0.03f, 0.4f);
            }
            var back = s.AddComponent<BoxCollider>();
            back.center = new Vector3(0f, 0.9f, -0.19f); back.size = new Vector3(0.9f, 1.8f, 0.02f);
        }

        void Box(Vector3 pos, Vector3 size, float yaw)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = "Box";
            b.transform.SetParent(transform, false);
            b.transform.SetPositionAndRotation(pos + Vector3.up * size.y * 0.5f, Quaternion.LookRotation(F, Vector3.up) * Quaternion.Euler(0f, yaw, 0f));
            b.transform.localScale = size;
            var r = b.GetComponent<Renderer>();
            r.sharedMaterial = SpecialRoomMaterials.Board;
            r.renderingLayerMask = 1u;
        }

        void Bulb(Vector3 pos, OfficeBiomeAssets a)
        {
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(bulb.GetComponent<Collider>());
            bulb.name = "Bulb";
            bulb.transform.SetParent(transform, false);
            bulb.transform.position = pos; bulb.transform.localScale = Vector3.one * 0.065f;
            var br = bulb.GetComponent<Renderer>();
            br.sharedMaterial = a && a.lensOn ? a.lensOn : SpecialRoomMaterials.Ceiling;
            br.shadowCastingMode = ShadowCastingMode.Off;
            var cord = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(cord.GetComponent<Collider>());
            cord.name = "BulbCord";
            cord.transform.SetParent(transform, false);
            float top = max.y;
            cord.transform.position = new Vector3(pos.x, (top + pos.y) * 0.5f + 0.02f, pos.z);
            cord.transform.localScale = new Vector3(0.008f, top - pos.y, 0.008f);
            cord.GetComponent<Renderer>().sharedMaterial = ItemModels.Mat("CordBlack");
            AddLight(pos - Vector3.up * 0.05f, new Color(1f, 0.86f, 0.66f), 1.7f);
        }

        void Lantern(Vector3 pos, OfficeBiomeAssets a)
        {
            var l = Model("Lantern", pos, Quaternion.Euler(0f, 30f, 0f), new Vector3(0.12f, 0.22f, 0.12f));
            foreach (var r in l.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].name == "Item_LanternGlobe" && a && a.lensOn) mats[i] = a.lensOn;
                r.sharedMaterials = mats;
            }
            var bc = l.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, 0.11f, 0f); bc.size = new Vector3(0.12f, 0.22f, 0.12f);
            AddLight(pos + Vector3.up * 0.103f, new Color(1f, 0.9f, 0.76f), 1.1f);
        }

        void AddLight(Vector3 pos, Color c, float intensity)
        {
            var g = new GameObject("RoomLight");
            g.transform.SetParent(transform, false);
            g.transform.position = pos;
            _light = g.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = c; _light.intensity = intensity;
            _light.range = Mathf.Max(Depth, Width) * 1.2f + 0.8f;
            _light.shadows = LightShadows.Soft; _light.shadowNearPlane = 0.05f;
            _light.renderingLayerMask = -1;
            _light.enabled = false;
        }

        /// <summary>Someone counted days here: groups of four strokes and a slash, chalked on the wall by the bag.</summary>
        void Tally(System.Random rng, float side)
        {
            var batch = Marks.NewBatch("Tally", transform);
            Vector3 n = -S * side;                                            // the side wall's face, into the room
            Vector3 wall = At(Depth * 0.5f - 0.2f, side * (Width * 0.5f - 0.002f), 1.05f);
            Vector3 along = F, up = Vector3.up;
            int days = 6 + rng.Next(18);
            float x = 0f;
            for (int i = 0; i < days; i++)
            {
                int k = i % 5;
                if (k < 4)
                {
                    Vector3 a = wall + along * (x + k * 0.03f) + up * ((float)rng.NextDouble() * 0.01f);
                    Marks.Strip(batch, a, a + up * (0.12f + (float)rng.NextDouble() * 0.02f) + along * 0.005f, n, Marks.ChalkWidth, Marks.KindChalk, Marks.ChalkColor);
                }
                else
                {
                    Vector3 a = wall + along * (x - 0.015f) + up * 0.02f;
                    Marks.Strip(batch, a, a + along * 0.13f + up * 0.08f, n, Marks.ChalkWidth, Marks.KindChalk, Marks.ChalkColor);
                    x += 0.2f;
                }
            }
            Marks.Upload(batch);
            batch.go.layer = 0;
        }

        void Update()
        {
            if (!_light || Time.time < _next) return;
            _next = Time.time + 0.25f + Random.value * 0.1f;
            var cam = Camera.main;
            bool near = cam && (cam.transform.position - Centre).sqrMagnitude < 16f * 16f;
            if (_light.enabled != near) _light.enabled = near;
        }
    }
}
