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
            // The client's card beside it (2026-11-09): what they offer and where your work with them stands.
            if (this.kind <= 3) SeamCard.Attach(this, mask);
            MakeGlow();
        }

        // ---------------- The other side's light (2026-11-09, Docs/ImprovementPlan.md V2) ----------------
        // A seam was a thin silver rectangle on a plain wall; behind it is the strongest contrast in the game (the client's
        // room is cool, white and blue) and nothing of it showed on this side. Now the other side's light leaks through:
        // a cool line round the tape's edges and across the carpet in front (as light shows under a shut door: the same
        // shader), a faint cool cast on the walls near it (a small real light), and the client's colour on a flag standing
        // out from the wall, so a seam can be picked out from the far end of a corridor. From the client's side (kind 4)
        // it is the Backrooms' yellow that leaks the other way.

        static Material s_spill;
        static readonly Dictionary<int, Material> s_flags = new Dictionary<int, Material>();
        readonly List<Renderer> _glow = new List<Renderer>();
        MaterialPropertyBlock _mpb;
        Light _leak;
        float _nextGlow;
        Color _glowColour;

        void MakeGlow()
        {
            if (!s_spill)
            {
                var sh = Shader.Find("Backrooms/LightSpill");
                if (!sh) return;
                s_spill = new Material(sh) { name = "SeamLightSpill", hideFlags = HideFlags.DontSave };
            }
            _mpb = new MaterialPropertyBlock();
            // (Tuned in play 2026-11-09: a pale blue on the mustard wall only read as white, so the leak is a saturated
            // blue and strong; the lamp sits low, like light under a door.)
            _glowColour = kind == 4 ? new Color(1f, 0.74f, 0.3f) : new Color(0.22f, 0.55f, 1f);
            float hw = NoclipDoors.Width * 0.5f, top = NoclipDoors.Height;
            const float z = 0.006f, out_ = 0.34f;
            // (Each strip: its first two corners along the tape's edge, the other two where the light has faded.)
            Strip("GlowFloor", new Vector3(-hw - 0.2f, 0.005f, 0.01f), new Vector3(hw + 0.2f, 0.005f, 0.01f), new Vector3(hw + 0.2f, 0.005f, 1.6f), new Vector3(-hw - 0.2f, 0.005f, 1.6f));
            Strip("GlowLeft", new Vector3(-hw, 0f, z), new Vector3(-hw, top, z), new Vector3(-hw - out_, top, z), new Vector3(-hw - out_, 0f, z));
            Strip("GlowRight", new Vector3(hw, top, z), new Vector3(hw, 0f, z), new Vector3(hw + out_, 0f, z), new Vector3(hw + out_, top, z));
            Strip("GlowTop", new Vector3(-hw, top, z), new Vector3(hw, top, z), new Vector3(hw, top + out_, z), new Vector3(-hw, top + out_, z));
            var lg = new GameObject("SeamLeak");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = new Vector3(0f, 0.35f, 0.3f);
            _leak = lg.AddComponent<Light>();
            _leak.type = LightType.Point; _leak.range = 3.4f; _leak.intensity = 0f;
            _leak.color = kind == 4 ? _glowColour : new Color(0.3f, 0.6f, 1f);
            _leak.shadows = LightShadows.None; _leak.renderingLayerMask = (int)_mask;
            if (kind <= 3)
            {
                // The client's flag: a stiff tab of their tape standing out from the wall above head height.
                var flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
                flag.name = "SeamFlag";
                Destroy(flag.GetComponent<Collider>());
                flag.transform.SetParent(transform, false);
                flag.transform.localPosition = new Vector3(hw + 0.07f, 1.98f, 0.15f);
                flag.transform.localScale = new Vector3(0.012f, 0.2f, 0.3f);
                var fr = flag.GetComponent<MeshRenderer>();
                fr.sharedMaterial = Flag(kind);
                fr.shadowCastingMode = ShadowCastingMode.Off; fr.renderingLayerMask = _mask;
            }
            UpdateGlow(true);
        }

        void Strip(string name, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { a, b, c, d };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = s_spill;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _glow.Add(r);
        }

        static Material Flag(int kind)
        {
            if (s_flags.TryGetValue(kind, out var m) && m) return m;
            var sh = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(sh) { name = "SeamFlag" + kind, hideFlags = HideFlags.DontSave };
            m.SetColor("_BaseColor", Contracts.ClientColours[Mathf.Clamp(kind, 0, 3)]);
            m.SetFloat("_Smoothness", 0.3f);
            return s_flags[kind] = m;
        }

        /// <summary>The leak breathes a little (light seen through a gap), and goes when the seam is shut.</summary>
        void UpdateGlow(bool force)
        {
            if (_mpb == null || (!force && Time.time < _nextGlow)) return;
            _nextGlow = Time.time + 0.1f;
            float t = Time.time * 0.7f + _seed * 20f;
            float breathe = 0.86f + 0.1f * Mathf.Sin(t) + 0.04f * Mathf.Sin(t * 3.7f);
            float k = breathe * (1f - _shut);
            for (int i = 0; i < _glow.Count; i++)
            {
                var r = _glow[i];
                if (!r) continue;
                bool on = k > 0.02f;
                if (r.enabled != on) r.enabled = on;
                if (!on) continue;
                _mpb.SetColor("_Color", _glowColour * ((i == 0 ? 2.4f : 2.1f) * (kind == 4 ? 0.5f : 1f) * k));
                r.SetPropertyBlock(_mpb);
            }
            if (_leak) { _leak.intensity = (kind == 4 ? 0.3f : 0.55f) * k; _leak.enabled = k > 0.02f; }
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
            UpdateGlow(false);
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
