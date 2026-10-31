using System.Collections.Generic;
using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Nails driven in with the <see cref="Hammer"/> (owner, 2026-10-30): each one a 2-inch wire nail whose head stays flush
    /// with what it went into. Nails in the building stand where they are (world space); nails in a prop are kept in the
    /// prop's own space (by its id), so they go where it goes, come back when it comes back, and hold it: a prop nailed into
    /// a wall or the floor is stuck there (<see cref="MovableProp.NailDown"/>), two props nailed together are joined (for
    /// this session). Saved with the world (<see cref="Export"/>, <see cref="Import"/>). Also the nail's mesh, its ghost, and
    /// the sounds of driving and pulling one.
    /// </summary>
    public static class NailWork
    {
        public const float Length = 0.05f, ShankRadius = 0.0015f, HeadRadius = 0.0045f;

        public struct Nail
        {
            public ulong prop;            // 0: in the building (world space); else the prop's id (prop space)
            public Vector3 position;      // where it went in (the head, flush)
            public Vector3 direction;     // out of the surface, along the nail toward its head
        }

        static readonly List<Nail> s_nails = new List<Nail>();
        static readonly List<GameObject> s_worldViews = new List<GameObject>();
        static GameObject s_root;

        public static IReadOnlyList<Nail> All => s_nails;

        // ---------------- Driving and pulling ----------------

        /// <summary>A nail driven in at a point (world), its head out along dir. In a prop if prop is given.</summary>
        public static void Add(Vector3 at, Vector3 dir, MovableProp prop)
        {
            var n = new Nail { position = at, direction = dir.normalized };
            if (prop)
            {
                n.prop = prop.Spec.id;
                n.position = prop.transform.InverseTransformPoint(at);
                n.direction = prop.transform.InverseTransformDirection(dir).normalized;
                s_nails.Add(n);
                View(prop.transform, n.position, n.direction);
            }
            else
            {
                s_nails.Add(n);
                s_worldViews.Add(View(Root, at, n.direction));
            }
        }

        /// <summary>The nail whose head is nearest a point (world), within r; its index, or -1.</summary>
        public static int Near(Vector3 at, float r, out Vector3 head, out Vector3 dir, out MovableProp prop)
        {
            head = dir = Vector3.zero; prop = null;
            int best = -1; float bd = r;
            for (int i = 0; i < s_nails.Count; i++)
            {
                var n = s_nails[i];
                Vector3 p = n.position, d = n.direction; MovableProp mp = null;
                if (n.prop != 0)
                {
                    mp = MovableProp.Find(n.prop);
                    if (!mp) continue;
                    p = mp.transform.TransformPoint(p); d = mp.transform.TransformDirection(d);
                }
                float dist = Vector3.Distance(p, at);
                if (dist < bd) { bd = dist; best = i; head = p; dir = d; prop = mp; }
            }
            return best;
        }

        /// <summary>Pulls a nail out (the claw): gone from the list and the view; a prop with no nails left in it comes free.</summary>
        public static void Remove(int i)
        {
            if (i < 0 || i >= s_nails.Count) return;
            var n = s_nails[i];
            s_nails.RemoveAt(i);
            if (n.prop == 0)
            {
                // Its view: the one standing where it was.
                for (int k = 0; k < s_worldViews.Count; k++)
                    if (s_worldViews[k] && (s_worldViews[k].transform.position - n.position).sqrMagnitude < 1e-6f) { Object.Destroy(s_worldViews[k]); s_worldViews.RemoveAt(k); break; }
                return;
            }
            var mp = MovableProp.Find(n.prop);
            if (!mp) return;
            foreach (Transform c in mp.transform)
                if (c.name == "Nail" && (c.localPosition - n.position).sqrMagnitude < 1e-6f) { Object.Destroy(c.gameObject); break; }
            if (!s_nails.Exists(x => x.prop == n.prop)) mp.Unnail();
        }

        /// <summary>A prop has been made (its chunk loaded, or brought back): its nails go back in it.</summary>
        public static void Attach(MovableProp mp)
        {
            if (!mp || s_nails.Count == 0) return;
            ulong id = mp.Spec.id;
            foreach (var n in s_nails) if (n.prop == id) View(mp.transform, n.position, n.direction);
        }

        // ---------------- Saving ----------------

        public static List<Saves.NailData> Export()
        {
            var list = new List<Saves.NailData>(s_nails.Count);
            foreach (var n in s_nails) list.Add(new Saves.NailData { prop = n.prop == 0 ? "" : n.prop.ToString("X16"), position = n.position, direction = n.direction });
            return list;
        }

        public static void Import(List<Saves.NailData> list)
        {
            Clear();
            if (list == null) return;
            foreach (var d in list)
            {
                ulong id = 0;
                if (!string.IsNullOrEmpty(d.prop) && !ulong.TryParse(d.prop, System.Globalization.NumberStyles.HexNumber, null, out id)) continue;
                var n = new Nail { prop = id, position = d.position, direction = d.direction.sqrMagnitude > 1e-6f ? d.direction.normalized : Vector3.up };
                s_nails.Add(n);
                if (id == 0) s_worldViews.Add(View(Root, n.position, n.direction));
                else { var mp = MovableProp.Find(id); if (mp) View(mp.transform, n.position, n.direction); }
            }
        }

        public static void Clear()
        {
            s_nails.Clear();
            foreach (var v in s_worldViews) if (v) Object.Destroy(v);
            s_worldViews.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_nails.Clear(); s_worldViews.Clear(); s_root = null; s_mesh = null; s_steel = null; s_ghost = null; s_ring = null; s_drive = null; s_pull = null; }

        // ---------------- The nail ----------------

        static Transform Root
        {
            get
            {
                if (!s_root) s_root = new GameObject("Nails");
                return s_root.transform;
            }
        }

        static Mesh s_mesh, s_ringMesh;
        static Material s_steel, s_ghost, s_ring;

        /// <summary>A 2-inch wire nail along +Y: its point at y = -Length, its head's top at 0 (so placed at the head, flush).</summary>
        public static Mesh Mesh
        {
            get
            {
                if (s_mesh) return s_mesh;
                var w = new Generation.MeshWriter();
                const int seg = 8;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    Vector3 r0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), r1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    // Shank, tapering to a point over its last 4 mm.
                    float tipY = -Length, taper = -Length + 0.004f;
                    w.Quad(r0 * ShankRadius + Vector3.up * taper, r1 * ShankRadius + Vector3.up * taper, r1 * ShankRadius + Vector3.up * -0.0012f, r0 * ShankRadius + Vector3.up * -0.0012f, (r0 + r1).normalized, Generation.MeshWriter.MetreUV);
                    w.Quad(Vector3.up * tipY, Vector3.up * tipY, r1 * ShankRadius + Vector3.up * taper, r0 * ShankRadius + Vector3.up * taper, ((r0 + r1).normalized - Vector3.up * 0.4f).normalized, Generation.MeshWriter.MetreUV);
                    // Head: its rim and its top.
                    w.Quad(r0 * HeadRadius + Vector3.up * -0.0012f, r1 * HeadRadius + Vector3.up * -0.0012f, r1 * HeadRadius, r0 * HeadRadius, (r0 + r1).normalized, Generation.MeshWriter.MetreUV);
                    w.Quad(r0 * HeadRadius, r1 * HeadRadius, Vector3.zero, Vector3.zero, Vector3.up, Generation.MeshWriter.MetreUV);
                    w.Quad(r1 * HeadRadius + Vector3.up * -0.0012f, r0 * HeadRadius + Vector3.up * -0.0012f, r0 * ShankRadius + Vector3.up * -0.0012f, r1 * ShankRadius + Vector3.up * -0.0012f, Vector3.down, Generation.MeshWriter.MetreUV);
                }
                s_mesh = w.ToMesh("Nail");
                return s_mesh;
            }
        }

        /// <summary>A thin flat ring (the aim's mark on the surface), in the XZ plane, about 2 cm across the middle.</summary>
        public static Mesh RingMesh
        {
            get
            {
                if (s_ringMesh) return s_ringMesh;
                var w = new Generation.MeshWriter();
                const int seg = 32; const float r0 = 0.016f, r1 = 0.021f;
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    w.Quad(d0 * r0, d1 * r0, d1 * r1, d0 * r1, Vector3.up, Generation.MeshWriter.MetreUV);
                }
                s_ringMesh = w.ToMesh("NailRing");
                return s_ringMesh;
            }
        }

        public static Material Steel
        {
            get
            {
                if (s_steel) return s_steel;
                var src = Destruction.DemolitionMaterials.Steel;
                s_steel = src ? new Material(src) { name = "Nail steel", hideFlags = HideFlags.DontSave } : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (s_steel.HasProperty("_BaseColor")) s_steel.SetColor("_BaseColor", new Color(0.55f, 0.56f, 0.58f));
                return s_steel;
            }
        }

        static Material Unlit(Color c, string name)
        {
            var m = new Material(Shader.Find("Sprites/Default")) { name = name, hideFlags = HideFlags.DontSave, color = c };
            m.renderQueue = 3100;
            return m;
        }

        /// <summary>The aim's see-through nail, and its ring: white where it will go, red where it cannot.</summary>
        public static Material Ghost => s_ghost ? s_ghost : (s_ghost = Unlit(new Color(1f, 1f, 1f, 0.6f), "Nail ghost"));
        public static Material RingMaterial => s_ring ? s_ring : (s_ring = Unlit(new Color(1f, 0.85f, 0.3f, 0.9f), "Nail ring"));

        /// <summary>A nail's object: under a parent, its head at a point (the parent's space), pointing in along -dir.</summary>
        public static GameObject View(Transform parent, Vector3 localHead, Vector3 localDir)
        {
            var go = new GameObject("Nail");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localHead;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, localDir);
            go.AddComponent<MeshFilter>().sharedMesh = Mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Steel;
            r.renderingLayerMask = 0xFFFFFFFFu;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // ---------------- Sounds ----------------

        static AudioClip[] s_drive;
        static AudioClip s_pull;

        /// <summary>The hammer on a nail head: a hard ring of steel on steel over the thud of what it goes into.</summary>
        public static AudioClip Drive(int i)
        {
            if (s_drive == null) { s_drive = new AudioClip[3]; for (int k = 0; k < 3; k++) s_drive[k] = MakeDrive(k); }
            return s_drive[((i % 3) + 3) % 3];
        }

        static AudioClip MakeDrive(int v)
        {
            const int rate = 44100; int n = rate / 3;
            var d = new float[n]; var rng = new System.Random(310 + v);
            float lp = 0f, f0 = 2900f + v * 260f, f1 = 4700f + v * 330f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.25f;
                float ring = (Mathf.Sin(Mathf.PI * 2f * f0 * t) * 0.6f + Mathf.Sin(Mathf.PI * 2f * f1 * t) * 0.3f) * Mathf.Exp(-t * 38f);
                float thud = Mathf.Sin(Mathf.PI * 2f * (110f + v * 12f) * t) * Mathf.Exp(-t * 30f) + lp * Mathf.Exp(-t * 60f) * 0.7f;
                d[i] = ring * 0.55f + thud * 0.8f;
            }
            Normalise(d, 0.9f);
            var c = AudioClip.Create("NailDrive" + v, n, 1, rate, false); c.SetData(d, 0);
            return c;
        }

        /// <summary>A nail drawn out with the claw: a short squeal of steel in wood.</summary>
        public static AudioClip Pull
        {
            get
            {
                if (s_pull) return s_pull;
                const int rate = 44100; int n = rate * 2 / 5;
                var d = new float[n]; var rng = new System.Random(77);
                float lp = 0f, ph = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)rate, k = t / (n / (float)rate);
                    ph += Mathf.PI * 2f * (900f + 700f * k + 80f * Mathf.Sin(t * 70f)) / rate;
                    lp += ((float)rng.NextDouble() * 2f - 1f - lp) * 0.3f;
                    float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k * 1.1f));
                    d[i] = (Mathf.Sin(ph) * 0.45f + lp * 0.35f) * env;
                }
                Normalise(d, 0.7f);
                s_pull = AudioClip.Create("NailPull", n, 1, rate, false); s_pull.SetData(d, 0);
                return s_pull;
            }
        }

        static void Normalise(float[] d, float peak)
        {
            float m = 1e-5f; foreach (var x in d) m = Mathf.Max(m, Mathf.Abs(x));
            for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
        }
    }
}
