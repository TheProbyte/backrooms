using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The items' Blender models (Tools/Blender/build_items*.py, Resources/Items/*.fbx) and their materials. Materials are
    /// made once per material slot name, on Backrooms/Lit, in the colour the model was exported with (finish per slot:
    /// metal, glossy plastic, cloth...). A missing model is replaced by a plain box so the item still works.
    /// </summary>
    public static class ItemModels
    {
        static readonly Dictionary<string, Material> s_mats = new Dictionary<string, Material>();
        static Shader s_lit;

        /// <summary>The material for a slot name (the imported colour when there is one).</summary>
        public static Material Mat(string slot, Color? imported = null)
        {
            slot = slot.Replace(" (Instance)", "");
            if (s_mats.TryGetValue(slot, out var m) && m) return m;
            if (!s_lit) s_lit = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(s_lit) { name = "Item_" + slot, hideFlags = HideFlags.DontSave };
            Color c = imported ?? Fallback(slot);
            float metal = 0f, smooth = 0.35f;
            string s = slot.ToLowerInvariant();
            if (s.Contains("metal") || s.Contains("prong")) { metal = 0.9f; smooth = 0.65f; }
            else if (s.Contains("plug") || s.Contains("cord") || s.Contains("socket") || s.Contains("switch")) smooth = 0.55f;
            else if (s.Contains("chicken")) smooth = 0.6f;                         // rubber
            else if (s.Contains("chalk") || s.Contains("core") || s.Contains("bag") || s.Contains("fabric")) smooth = 0.08f;
            else if (s.Contains("tape")) smooth = 0.25f;
            else if (s.Contains("bulb") || s.Contains("globe")) smooth = 0.8f;
            else if (s.Contains("lamp") || s.Contains("shelf") || s.Contains("lantern")) { metal = 0.3f; smooth = 0.5f; }
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            s_mats[slot] = m;
            return m;
        }

        static Color Fallback(string slot)
        {
            switch (slot)
            {
                case "ChalkWhite": return new Color(0.92f, 0.92f, 0.88f);
                case "TapeBlue": case "TapeEdge": return new Color(0.12f, 0.38f, 0.78f);
                case "TapeCore": return new Color(0.55f, 0.42f, 0.28f);
                case "CordOrange": case "SocketFace": return new Color(0.95f, 0.38f, 0.05f);
                case "CordBlack": case "PlugBody": case "SocketSlot": case "ChickenEye": return new Color(0.04f, 0.04f, 0.04f);
                case "ChickenYellow": return new Color(0.95f, 0.78f, 0.12f);
                case "ChickenRed": return new Color(0.75f, 0.06f, 0.05f);
                case "ChickenOrange": return new Color(0.95f, 0.45f, 0.08f);
                case "LampBody": return new Color(0.08f, 0.18f, 0.12f);
                case "LampBulb": case "LanternGlobe": return new Color(0.95f, 0.92f, 0.82f);
                default: return new Color(0.6f, 0.6f, 0.6f);
            }
        }

        /// <summary>
        /// Puts the model under the parent as "Model" (keeping the import's axis fix), with its colliders removed and its
        /// materials replaced. Returns null if the model is missing (then a box of the given size stands in).
        /// </summary>
        public static GameObject Attach(Transform parent, string resource, Vector3 fallbackSize, string fallbackSlot)
        {
            var prefab = Resources.Load<GameObject>("Items/" + resource);
            GameObject m;
            if (prefab)
            {
                m = Object.Instantiate(prefab, parent, false);
                m.transform.localRotation = prefab.transform.localRotation;
                foreach (var c in m.GetComponentsInChildren<Collider>()) Object.Destroy(c);
                foreach (var r in m.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var src = mats[i];
                        Color? col = null;
                        if (src && src.HasProperty("_BaseColor")) col = src.GetColor("_BaseColor");
                        else if (src && src.HasProperty("_Color")) col = src.GetColor("_Color");
                        mats[i] = Mat(src ? src.name : fallbackSlot, col);
                    }
                    r.sharedMaterials = mats;
                    Prepare(r);
                }
            }
            else
            {
                m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(m.GetComponent<Collider>());
                m.transform.SetParent(parent, false);
                m.transform.localScale = fallbackSize;
                m.transform.localPosition = Vector3.up * fallbackSize.y * 0.5f;
                var r = m.GetComponent<Renderer>();
                r.sharedMaterial = Mat(fallbackSlot);
                Prepare(r);
            }
            m.name = "Model";
            return m;
        }

        public static void Prepare(Renderer r)
        {
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.renderingLayerMask = 0xFFFFFFFFu;            // lit by whatever lights the spot it is in
        }

        /// <summary>A new item object: rigidbody, one collider, the model; the caller adds the item component.</summary>
        public static GameObject Body(string name, Vector3 position, Quaternion rotation, float mass, string resource, Vector3 size, string fallbackSlot, bool capsule = false, Vector3? centre = null)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, rotation);
            Attach(go.transform, resource, size, fallbackSlot);
            Vector3 c = centre ?? Vector3.up * size.y * 0.5f;
            if (capsule)
            {
                var cc = go.AddComponent<CapsuleCollider>();
                bool alongZ = size.z >= size.x && size.z >= size.y, alongX = size.x >= size.y && size.x >= size.z;
                cc.direction = alongZ ? 2 : alongX ? 0 : 1;
                cc.height = alongZ ? size.z : alongX ? size.x : size.y;
                cc.radius = Mathf.Min(size.x, size.y, size.z) * 0.5f;
                cc.center = c;
            }
            else
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.size = size; bc.center = c;
            }
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass; rb.linearDamping = 0.1f; rb.angularDamping = 0.4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            return go;
        }

        /// <summary>A looping 3D sound on an item.</summary>
        public static AudioSource Source(GameObject go, float maxDistance = 12f)
        {
            var a = go.AddComponent<AudioSource>();
            a.playOnAwake = false; a.spatialBlend = 1f; a.minDistance = 0.4f; a.maxDistance = maxDistance; a.dopplerLevel = 0f;
            a.rolloffMode = AudioRolloffMode.Logarithmic;
            return a;
        }
    }
}
