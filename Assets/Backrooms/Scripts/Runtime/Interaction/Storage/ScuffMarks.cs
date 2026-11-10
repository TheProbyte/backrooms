using Backrooms.Saves;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A scuffed box (2026-11-09, decision 5): left unsafe too long while stored, it takes a few dark scrapes along its
    /// sides, so you can see which ones cost you (each takes 5% off its batch's completion pay: Contracts.Batches). Put on
    /// when it is scuffed, and again whenever the box is made (MovableProp.Spawn) while it is on the save's scuffed list.
    /// </summary>
    public class ScuffMarks : MonoBehaviour
    {
        static Material s_mat;

        public static void Attach(MovableProp p)
        {
            var d = GameSession.Data;
            if (!p || d == null || p.Spec.kind != MovableKind.Box || !d.scuffed.Contains(p.Spec.id) || p.GetComponent<ScuffMarks>()) return;
            p.gameObject.AddComponent<ScuffMarks>();
            if (!s_mat)
            {
                var sh = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
                s_mat = new Material(sh) { name = "BoxScuff", hideFlags = HideFlags.DontSave };
                s_mat.SetColor("_BaseColor", new Color(0.22f, 0.17f, 0.12f));
            }
            var s = p.Spec.size;
            var rng = new System.Random((int)(p.Spec.id & 0x7FFFFFFF));
            for (int k = 0; k < 4; k++)
            {
                // Along a side face, a little up from the foot: a long thin scrape at a slight slant.
                int face = rng.Next(4);
                bool alongX = face < 2;
                float sign = face % 2 == 0 ? 1f : -1f;
                float len = (alongX ? s.x : s.z) * (0.3f + 0.4f * (float)rng.NextDouble());
                float off = ((float)rng.NextDouble() - 0.5f) * ((alongX ? s.x : s.z) - len);
                float y = s.y * (0.15f + 0.6f * (float)rng.NextDouble());
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Scuff";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(p.transform, false);
                go.transform.localPosition = alongX ? new Vector3(off, y, sign * (s.z * 0.5f + 0.0015f)) : new Vector3(sign * (s.x * 0.5f + 0.0015f), y, off);
                go.transform.localRotation = Quaternion.Euler(alongX ? 0f : 0f, alongX ? 0f : 90f, ((float)rng.NextDouble() - 0.5f) * 14f);
                go.transform.localScale = new Vector3(len, 0.012f + 0.02f * (float)rng.NextDouble(), 0.002f);
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = s_mat; r.shadowCastingMode = ShadowCastingMode.Off; r.renderingLayerMask = uint.MaxValue;
            }
        }
    }
}
