using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A soft dark patch under a small item lying on something, instead of its shadow-map shadow (Backrooms/ContactShadow).
    /// In the fixtures' shadow maps a chalk stick or a plug is only a few texels: it flickered and swam as the lights
    /// handed over. This follows the item, sits on the surface under it (floor, shelf, table), is sized to the item's
    /// footprint and fades as the item rises off the surface. Hidden while the item is held. Added by WorldItem.
    /// </summary>
    public class ContactShadow : MonoBehaviour
    {
        static Material s_mat;
        static Mesh s_quad;
        WorldItem _item;
        Transform _shadow;
        MeshRenderer _r;
        readonly RaycastHit[] _hits = new RaycastHit[6];
        Vector3 _lastPos;
        float _size = 0.1f;
        int _next;

        void Awake()
        {
            _item = GetComponent<WorldItem>();
            if (!s_mat)
            {
                var sh = Shader.Find("Backrooms/ContactShadow");
                if (sh) s_mat = new Material(sh) { name = "Contact shadow", hideFlags = HideFlags.DontSave };
            }
            if (!s_quad)
            {
                s_quad = new Mesh { name = "ContactShadowQuad", hideFlags = HideFlags.DontSave };
                s_quad.SetVertices(new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(-0.5f, 0, 0.5f) });
                s_quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                s_quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                s_quad.RecalculateBounds();
            }
            var go = new GameObject("ContactShadow");
            go.hideFlags = HideFlags.DontSave;
            _shadow = go.transform;
            go.AddComponent<MeshFilter>().sharedMesh = s_quad;
            _r = go.AddComponent<MeshRenderer>();
            _r.sharedMaterial = s_mat;
            _r.shadowCastingMode = ShadowCastingMode.Off; _r.receiveShadows = false;
            _r.lightProbeUsage = LightProbeUsage.Off; _r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _r.renderingLayerMask = 0xFFFFFFFFu;
            _r.enabled = false;
            Measure();
        }

        /// <summary>The item's footprint (its renderers' bounds across, the widest horizontal extent).</summary>
        public void Measure()
        {
            var b = new Bounds(transform.position, Vector3.zero);
            bool any = false;
            foreach (var rr in GetComponentsInChildren<Renderer>())
            {
                if (rr == _r || rr is ParticleSystemRenderer) continue;
                if (!any) { b = rr.bounds; any = true; } else b.Encapsulate(rr.bounds);
            }
            _size = Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 1.35f, 0.05f, 0.6f);
        }

        void OnDestroy() { if (_shadow) Destroy(_shadow.gameObject); }
        void OnDisable() { if (_r) _r.enabled = false; }

        void LateUpdate()
        {
            if (!_item || _item.Held || !s_mat) { if (_r.enabled) _r.enabled = false; return; }
            // Only re-cast when the item has moved (or now and then).
            if ((transform.position - _lastPos).sqrMagnitude < 1e-6f && --_next > 0) return;
            _lastPos = transform.position; _next = 30;
            Measure();
            Vector3 c = transform.position;
            foreach (var rr in GetComponentsInChildren<Renderer>()) if (rr != _r && !(rr is ParticleSystemRenderer)) { c = rr.bounds.center; break; }
            int n = Physics.RaycastNonAlloc(c + Vector3.up * 0.05f, Vector3.down, _hits, 1.2f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; int bi = -1;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].collider.GetComponent<CharacterController>()) continue;
                if (_hits[i].distance < best) { best = _hits[i].distance; bi = i; }
            }
            if (bi < 0) { _r.enabled = false; return; }
            var h = _hits[bi];
            float bottom = c.y;
            foreach (var rr in GetComponentsInChildren<Renderer>()) if (rr != _r && !(rr is ParticleSystemRenderer)) bottom = Mathf.Min(bottom, rr.bounds.min.y);
            float gap = Mathf.Max(0f, bottom - h.point.y);
            float fade = Mathf.Clamp01(1f - gap / 0.35f);
            if (fade <= 0.01f) { _r.enabled = false; return; }
            _shadow.SetPositionAndRotation(h.point + h.normal * 0.002f, Quaternion.FromToRotation(Vector3.up, h.normal) * Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            float s = _size * (1f + gap * 1.5f);
            _shadow.localScale = new Vector3(s, 1f, s);
            var block = s_block ??= new MaterialPropertyBlock();
            block.SetFloat("_Strength", 0.55f * fade);
            _r.SetPropertyBlock(block);
            _r.enabled = true;
        }

        static MaterialPropertyBlock s_block;
    }
}
