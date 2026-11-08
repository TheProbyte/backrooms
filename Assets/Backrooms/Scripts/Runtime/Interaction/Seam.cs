using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A seam (2026-11-07, LeaseSeams, Docs/ContractFloorsPlan.md): a place where the wall gives onto somewhere in the real
    /// world. It looks like any noclip door (the owner, 2026-11-07: "more like noclip zones even visually"): a door-sized patch
    /// of solid-looking wall, marked out in silver duct tape (NoclipDoorMarking.DuctTape) with Overflow's printed label on
    /// it saying where it goes. The wall there can be walked through (a noclip door, NoclipTravel) into that place's room
    /// (SeamPlan, SeamRooms), where the job's boxes wait; from the room, the way back is taped the same (kind 4). Put an ear
    /// to it and the place is behind it, muffled. From closing time the seams shut (NoclipPassage.SeamsShut): solid, and
    /// the sound gone thin.
    /// <para>An intake chute can be fitted to a seam (<see cref="Fit"/>, the SeamChute model): it pulls the job's boxes
    /// through by itself and slides them out onto the floor (an improvement bought at the hub, Interaction.Improvements).</para>
    /// </summary>
    public class Seam : MonoBehaviour
    {
        /// <summary>The kinds' labels: what the place is (LeaseSeams.Kinds) and, from a place's side (4), the way back.</summary>
        static readonly string[] Labels = { "WAREHOUSE", "PRINT SHOP", "OFFICE SUPPLY", "FURNITURE STORE", "BACKROOMS" };

        public static readonly List<Seam> All = new List<Seam>();

        public int kind;
        public ulong id;
        public bool fitted;
        AudioSource _hum, _fx;
        float _nextBox, _seed, _shut;
        uint _mask;
        BoxCollider _blocker;

        static readonly Dictionary<int, AudioClip> s_ambience = new Dictionary<int, AudioClip>();
        static readonly Dictionary<int, Material> s_labels = new Dictionary<int, Material>();
        static AudioClip s_thunk;

        /// <summary>Where a fitted chute stands: beside the doorway along the wall.</summary>
        Vector3 ChuteLocal => new Vector3(LeaseSeams.ChuteOffset, 0f, 0f);

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        public void Init(int kind, ulong id, uint mask)
        {
            this.kind = Mathf.Clamp(kind, 0, Labels.Length - 1);
            this.id = id;
            _mask = mask;
            _seed = (id % 997) / 997f;
            // Overflow's label, stuck on the tape across the doorway at chest height.
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "SeamLabel";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            q.transform.localPosition = new Vector3(0f, 1.42f, 0.0035f);
            q.transform.localRotation = Quaternion.Euler(0f, 180f, (_seed - 0.5f) * 5f);        // (a primitive quad faces -z; stuck on a touch crooked)
            q.transform.localScale = new Vector3(0.42f, 0.147f, 1f);
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = Label(this.kind);
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.renderingLayerMask = mask;
            // The other side, muffled.
            _hum = Source("SeamSound", new Vector3(0f, 1.3f, 0.05f), 9f);
            _hum.clip = Ambience(this.kind); _hum.loop = true; _hum.volume = 0.26f; _hum.minDistance = 1.0f;
            _hum.time = _seed * _hum.clip.length;
            _hum.Play();
            _fx = Source("SeamFx", new Vector3(0f, 0.8f, 0.2f), 14f);
            // The wall's colliders leave the doorway open (a noclip door): shut, it is solid again.
            var bl = new GameObject("SeamShut");
            bl.transform.SetParent(transform, false);
            _blocker = bl.AddComponent<BoxCollider>();
            _blocker.center = new Vector3(0f, NoclipDoors.Height * 0.5f, -(OfficeBiome.WallThickness * 0.5f + 0.004f));
            _blocker.size = new Vector3(NoclipDoors.Width + 0.1f, NoclipDoors.Height + 0.1f, OfficeBiome.WallThickness);
            _blocker.enabled = NoclipPassage.SeamsShut;
        }

        /// <summary>
        /// An intake chute fitted to this seam: the SeamChute model beside the doorway. On the job's source seam it pulls the
        /// job's boxes through from the place's pickup pallet and slides them out one every few seconds.
        /// </summary>
        public void Fit(bool quiet = false)
        {
            if (fitted || kind > 3) return;
            fitted = true;
            FittingModels.Spawn("SeamChute", transform, _mask, ChuteLocal, Quaternion.identity);
            var solid = new GameObject("ChuteSolid");
            solid.transform.SetParent(transform, false);
            var box = solid.AddComponent<BoxCollider>();
            box.center = ChuteLocal + new Vector3(0f, 0.98f, 0.12f); box.size = new Vector3(0.72f, 0.14f, 0.24f);
            if (!quiet) _fx.PlayOneShot(Thunk(), 0.8f);
        }

        AudioSource Source(string name, Vector3 local, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            var a = ItemModels.Source(go, range);
            Audio.SoundRoute.Add(a);
            return a;
        }

        // ---------------- Working ----------------

        void Start()
        {
            // Fitted with a chute already (an improvement bought on an earlier night)?
            if (kind <= 3 && Improvements.HasChute(id)) Fit(true);
        }

        void Update()
        {
            // Shut from closing time: solid, and the place behind it goes quiet.
            _shut = Mathf.MoveTowards(_shut, NoclipPassage.SeamsShut ? 1f : 0f, Time.deltaTime * 0.5f);
            if (_blocker.enabled != NoclipPassage.SeamsShut) _blocker.enabled = NoclipPassage.SeamsShut;
            _hum.volume = Mathf.Lerp(0.26f, 0.03f, _shut);
            if (fitted && !NoclipPassage.SeamsShut && Time.time >= _nextBox)
            {
                _nextBox = Time.time + 3f;
                var cs = Contracts.Instance;
                if (cs && cs.RoleOf(id) == 1 && cs.TakeFromStack(id, out var boxId, out int index)) SendBox(boxId, index);
            }
        }

        /// <summary>A fitted chute: one of the job's boxes from the place's pallet, out of the chute and onto the floor.</summary>
        void SendBox(ulong boxId, int index)
        {
            var mgr = MovablePropManager.Instance;
            if (!mgr) return;
            var at = transform.TransformPoint(ChuteLocal + new Vector3(0f, 0.42f, 0.1f));
            var rot = transform.rotation * Quaternion.Euler(0f, 90f * (index % 2), 0f);
            var prop = MovableProp.Find(boxId);
            if (!prop)
            {
                var spec = Contracts.BoxSpec(boxId, at, rot.eulerAngles.y);
                if (spec == null) return;
                prop = MovableProp.Spawn(spec, mgr.transform, at, rot);
                mgr.Detach(prop);
            }
            int k = index % 6, layer = index / 6;
            var rest = ChuteLocal + new Vector3((k % 3 - 1) * 0.56f, 0.005f + layer * 0.37f, 0.62f + (k / 3) * 0.46f);   // (a box's origin is its foot)
            StartCoroutine(Slide(prop, at, transform.TransformPoint(rest), rot));
            _fx.PlayOneShot(Thunk(), 0.8f);
        }

        System.Collections.IEnumerator Slide(MovableProp prop, Vector3 from, Vector3 to, Quaternion rot)
        {
            // Out of the mouth and down the tray, then a drop and a little settle onto the floor.
            var lip = transform.TransformPoint(ChuteLocal + new Vector3(0f, 0.22f, 0.45f));
            const float T = 0.75f;
            for (float t = 0f; t < T && prop; t += Time.deltaTime)
            {
                float u = t / T;
                var p = u < 0.55f ? Vector3.Lerp(from, lip, u / 0.55f) : Vector3.Lerp(lip, to, Mathf.SmoothStep(0f, 1f, (u - 0.55f) / 0.45f));
                var r = rot * Quaternion.Euler(Mathf.Sin(u * Mathf.PI) * 18f, 0f, 0f);
                prop.transform.SetPositionAndRotation(p, r);
                if (prop.Body) { prop.Body.position = p; prop.Body.rotation = r; }
                yield return null;
            }
            if (!prop) yield break;
            prop.transform.SetPositionAndRotation(to, rot);
            if (prop.Body) { prop.Body.position = to; prop.Body.rotation = rot; }
            MovablePropManager.Instance?.Store(prop);
        }

        // ---------------- The label ----------------

        /// <summary>Overflow's printed label (a sticker on the tape): the company's band, what is through it, a warning.</summary>
        static Material Label(int kind)
        {
            if (s_labels.TryGetValue(kind, out var m) && m) return m;
            const int W = 120, H = 42;
            var px = new Color32[W * H];
            var paper = new Color32(240, 234, 216, 255);
            var choc = new Color32(58, 36, 24, 255);
            for (int i = 0; i < px.Length; i++) px[i] = paper;
            PixelFont.Fill(px, W, H, 0, 0, W, 13, new Color32(240, 162, 30, 255));
            PixelFont.Fill(px, W, H, 0, 13, W, 15, new Color32(217, 102, 31, 255));
            PixelFont.DrawCentred(px, W, H, "OVERFLOW", W / 2, 2, 1, choc);
            string what = Labels[Mathf.Clamp(kind, 0, Labels.Length - 1)];
            PixelFont.DrawCentred(px, W, H, what, W / 2, 18, PixelFont.Width(what, 2) <= W - 6 ? 2 : 1, choc);
            PixelFont.DrawCentred(px, W, H, kind == 4 ? "STAFF ONLY" : "SEAM - KEEP CLEAR", W / 2, 33, 1, new Color32(170, 60, 30, 255));
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SeamLabel" + kind };
            tex.SetPixels32(px); tex.Apply(false, true);
            var sh = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(sh) { name = "SeamLabel" + kind };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.35f);
            return s_labels[kind] = m;
        }

        // ---------------- Sounds (made here, muffled through the wall) ----------------

        static AudioClip Ambience(int kind)
        {
            if (s_ambience.TryGetValue(kind, out var c)) return c;
            int sr = 22050, n = sr * 8;
            var d = new float[n];
            var rng = new System.Random(17 + kind);
            float lp = 0f, lp2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                float s = 0f;
                switch (kind)
                {
                    case 0:     // a warehouse: a deep hum, a forklift's reversing beeps now and then, pallets set down
                        s = 0.35f * Mathf.Sin(2f * Mathf.PI * 55f * t) + 0.2f * noise;
                        if (t % 8f > 2f && t % 8f < 4.4f && (t * 2f) % 1f < 0.45f) s += 0.5f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 1040f * t));
                        if (Mathf.Abs(t - 6.1f) < 0.05f) s += noise * 2.5f * (1f - Mathf.Abs(t - 6.1f) / 0.05f);
                        break;
                    case 1:     // a print shop: the press, chunk-chunk twice a second, and the paper
                    {
                        float ph = (t * 2f) % 1f;
                        s = 0.15f * noise + (ph < 0.08f ? noise * 1.6f * (1f - ph / 0.08f) + Mathf.Sin(2f * Mathf.PI * 90f * t) * (1f - ph / 0.08f) : 0f);
                        s += 0.15f * Mathf.Sin(2f * Mathf.PI * 120f * t);
                        break;
                    }
                    case 2:     // an office supplier: a busy shop, a phone ringing
                        s = 0.25f * noise * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 0.7f * t));
                        if (t % 8f > 1f && t % 8f < 3f && (t % 0.4f) < 0.2f) s += 0.35f * Mathf.Sin(2f * Mathf.PI * 440f * t) * Mathf.Sin(2f * Mathf.PI * 20f * t);
                        break;
                    case 4:     // the Backrooms, from the other side: the tubes' hum
                        s = 0.4f * Mathf.Sin(2f * Mathf.PI * 120f * t) + 0.18f * Mathf.Sin(2f * Mathf.PI * 240f * t) + 0.08f * noise;
                        break;
                    default:    // a furniture store: easy listening from somewhere, a floor polisher
                    {
                        float[] chord = { 261.6f, 329.6f, 392f, 493.9f };
                        float k = Mathf.Floor(t / 2f) % 2f == 0f ? 1f : 0.8909f;
                        foreach (var f in chord) s += 0.12f * Mathf.Sin(2f * Mathf.PI * f * k * t);
                        s += 0.15f * noise;
                        break;
                    }
                }
                // Through the wall: low-passed twice, and quieter.
                lp += (s - lp) * 0.08f; lp2 += (lp - lp2) * 0.12f;
                d[i] = lp2 * 0.5f;
            }
            // Loop cleanly: cross-fade the end into the start.
            int fade = sr / 4;
            for (int i = 0; i < fade; i++) { float u = i / (float)fade; d[i] = d[i] * u + d[n - fade + i] * (1f - u); }
            c = AudioClip.Create("SeamAmbience" + kind, n - fade, 1, sr, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(d, trimmed, n - fade);
            c.SetData(trimmed, 0);
            return s_ambience[kind] = c;
        }

        static AudioClip Thunk()
        {
            if (s_thunk) return s_thunk;
            int sr = 22050, n = sr / 2;
            var d = new float[n];
            var rng = new System.Random(3);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                d[i] = (Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.7f + (float)(rng.NextDouble() * 2 - 1) * 0.4f * Mathf.Exp(-t * 40f)) * Mathf.Exp(-t * 9f);
            }
            s_thunk = AudioClip.Create("ChuteThunk", n, 1, sr, false);
            s_thunk.SetData(d, 0);
            return s_thunk;
        }
    }
}
