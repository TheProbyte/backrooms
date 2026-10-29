using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// Sounds from real physics contacts (2026-10-28), as physically based game audio does: every hit is heard from where
    /// it happened, as loud as its energy (the effective mass and the speed along the contact normal), with two layers:
    /// the struck thing's own ring (its material, pitched by its size) and the answer of what it struck (carpet, a hard
    /// floor, concrete, tile, drywall, metal, or another thing's own ring). Soft surfaces damp the ring: a can dropped on
    /// carpet is a muffled tick, on concrete a bright clink. Things sliding along the floor scrape (dragged over carpet,
    /// scraped over a hard floor, rolling on casters), as loud and as high as they slip.
    ///
    /// Attached to every loose item (WorldItem) and movable piece of furniture (MovableProp); the sledgehammer, the rubber
    /// chicken and wall debris keep their own sounds. Through Steam Audio (occluded by walls), and listed in the sound
    /// settings (Physics).
    /// </summary>
    [DisallowMultipleComponent]
    public class ImpactSounder : MonoBehaviour
    {
        public ImpactBody body = ImpactBody.Plastic;
        [Tooltip("Size of the thing (m): smaller rings higher.")] public float size = 0.3f;
        public bool casters;
        public bool resolved;

        internal Rigidbody rb;
        internal float quietUntil, slip, slipTarget, lastStay, lastHit, stayStep = -1f, nextSurface;
        internal ImpactSurface slipSurface;
        internal int id;
        static int s_next;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            id = ++s_next;
            quietUntil = Time.time + 1.5f;                 // not while it settles into place after loading
        }

        void Start()
        {
            if (resolved) return;
            resolved = true;
            // Loose items: by what they are.
            var item = GetComponent<Interaction.WorldItem>();
            if (item)
            {
                switch (item.itemId)
                {
                    case "sledgehammer": case "rubberchicken": enabled = false; Destroy(this); return;   // their own sounds
                    case "spraycan": body = ImpactBody.Can; size = 0.2f; break;
                    case "chalk": body = ImpactBody.Chalk; size = 0.08f; break;
                    case "tape": body = ImpactBody.Tape; size = 0.12f; break;
                    case "desklamp": body = ImpactBody.LampMetal; size = 0.4f; break;
                    default: body = ImpactBody.Plastic; size = 0.15f; break;       // the cord reel, plugs
                }
                return;
            }
            var prop = GetComponent<Interaction.MovableProp>();
            if (prop && prop.Spec != null)
            {
                var b = prop.Shape != null ? prop.Shape.bounds.size : Vector3.one;
                size = Mathf.Max(b.x, Mathf.Max(b.y, b.z));
                casters = prop.Shape != null && prop.Shape.rolls;
                switch (prop.Spec.kind)
                {
                    case Interaction.MovableKind.Sofa: case Interaction.MovableKind.Armchair: body = ImpactBody.Soft; break;
                    case Interaction.MovableKind.OfficeChair: body = ImpactBody.Chair; break;
                    case Interaction.MovableKind.Table: body = ImpactBody.Wood; break;
                    case Interaction.MovableKind.Box: body = ImpactBody.Cardboard; break;
                }
            }
        }

        void OnCollisionEnter(Collision c) => ImpactAudio.Hit(this, c);

        void OnCollisionStay(Collision c)
        {
            // Sliding along the floor: how fast the contact slips (the thing's own point velocity, minus the other's).
            if (!rb || rb.isKinematic || c.contactCount == 0) return;
            var ct = c.GetContact(0);
            if (Mathf.Abs(ct.normal.y) < 0.55f) return;
            Vector3 v = rb.GetPointVelocity(ct.point);
            if (c.rigidbody) v -= c.rigidbody.GetPointVelocity(ct.point);
            v -= ct.normal * Vector3.Dot(v, ct.normal);
            // The fastest contact of this physics step (callbacks come per step, not per frame).
            if (Time.fixedTime != stayStep) { stayStep = Time.fixedTime; slipTarget = 0f; }
            slipTarget = Mathf.Max(slipTarget, v.magnitude);
            if (Time.time >= nextSurface) { nextSurface = Time.time + 0.3f; slipSurface = ImpactAudio.SurfaceOf(c.collider, ct.point, ct.normal, out _); }
            lastStay = Time.time;
            ImpactAudio.Sliding(this);
        }
    }

    public static class ImpactAudio
    {
        const int Voices = 12, Loops = 3, Variants = 3;
        static GameObject s_root;
        static readonly List<AudioSource> s_voices = new List<AudioSource>();
        static readonly AudioSource[] s_loops = new AudioSource[Loops];
        static readonly ImpactSounder[] s_loopOwner = new ImpactSounder[Loops];
        static readonly HashSet<ImpactSounder> s_sliding = new HashSet<ImpactSounder>();
        static int s_voice, s_frame, s_frameCount;
        static AudioClip[,] s_body, s_surface;
        static AudioClip[] s_scrape;

        /// <summary>A contact began: the thing's ring and what it struck, as loud as the hit.</summary>
        public static void Hit(ImpactSounder me, Collision c)
        {
            if (!me.enabled || !me.rb || c.contactCount == 0 || Time.time < me.quietUntil) return;
            var ct = c.GetContact(0);
            float vn = Mathf.Abs(Vector3.Dot(c.relativeVelocity, ct.normal));
            if (vn < 0.35f) return;
            // The player's own capsule (walked into it): the body sounds already cover that.
            if (c.collider.GetComponentInParent<CamcorderPlayerController>()) return;
            var other = c.collider.GetComponentInParent<ImpactSounder>();
            if (other && other.id < me.id && Time.time - other.lastHit < 0.05f) return;       // the pair is heard once
            if (Time.time - me.lastHit < 0.07f) return;
            me.lastHit = Time.time;
            // Energy of the hit: the lighter of the two (a static wall gives nothing) at the speed along the normal.
            float m = me.rb.mass;
            if (c.rigidbody && !c.rigidbody.isKinematic) m = Mathf.Min(m, c.rigidbody.mass);
            float energy = 0.5f * m * vn * vn;
            // A tap (0.01 J) is faint, a can dropped from the hand (4 J) clear, furniture slammed (50 J and up) the loudest.
            float loud = Mathf.Clamp01((Mathf.Log10(Mathf.Max(energy, 1e-5f)) + 2f) / 3.7f);
            if (loud < 0.02f) return;
            var ear = Listener();
            if (ear && (ct.point - ear.position).sqrMagnitude > 28f * 28f) return;
            if (Time.frameCount != s_frame) { s_frame = Time.frameCount; s_frameCount = 0; }
            if (++s_frameCount > 4) return;                                                     // a pile landing: a few, not dozens
            Ensure();
            float vol = SoundCatalog.Volume(SoundIds.Impacts) * 0.35f;
            ImpactSurface surface = SurfaceOf(c.collider, ct.point, ct.normal, out var otherBody);
            // The thing's own ring, damped by a soft surface.
            float damp = surface == ImpactSurface.Carpet ? 0.45f : surface == ImpactSurface.Soft ? 0.3f : 1f;
            float pitch = Mathf.Clamp(Mathf.Pow(0.3f / Mathf.Max(0.05f, me.size), 0.3f), 0.7f, 1.45f) * Random.Range(0.95f, 1.05f);
            Play(s_body[(int)me.body, Random.Range(0, Variants)], ct.point, loud * damp * vol, pitch);
            // What it struck.
            if (otherBody.HasValue && other)
            {
                float op = Mathf.Clamp(Mathf.Pow(0.3f / Mathf.Max(0.05f, other.size), 0.3f), 0.7f, 1.45f) * Random.Range(0.95f, 1.05f);
                Play(s_body[(int)otherBody.Value, Random.Range(0, Variants)], ct.point, loud * 0.8f * vol, op);
            }
            else Play(s_surface[(int)surface, Random.Range(0, Variants)], ct.point, loud * (surface == ImpactSurface.Carpet ? 0.55f : 0.9f) * vol, Random.Range(0.94f, 1.06f));
        }

        /// <summary>
        /// Set down by hand (round 11, PropGrabber.Place): a soft knock, the thing's own ring and the surface's, as for a
        /// gentle hit. (Placed, it does not move, so no contact makes a sound.)
        /// </summary>
        public static void SetDown(ImpactSounder me, Collider on, Vector3 point, Vector3 normal)
        {
            if (!me || !me.enabled) return;
            Ensure();
            me.lastHit = Time.time;
            float vol = SoundCatalog.Volume(SoundIds.Impacts) * 0.35f;
            ImpactSurface surface = SurfaceOf(on, point, normal, out var otherBody);
            float damp = surface == ImpactSurface.Carpet ? 0.45f : surface == ImpactSurface.Soft ? 0.3f : 1f;
            const float loud = 0.32f;
            float pitch = Mathf.Clamp(Mathf.Pow(0.3f / Mathf.Max(0.05f, me.size), 0.3f), 0.7f, 1.45f) * Random.Range(0.97f, 1.03f);
            Play(s_body[(int)me.body, Random.Range(0, Variants)], point, loud * damp * vol, pitch);
            if (otherBody.HasValue) Play(s_body[(int)otherBody.Value, Random.Range(0, Variants)], point, loud * 0.7f * vol, Random.Range(0.95f, 1.05f));
            else Play(s_surface[(int)surface, Random.Range(0, Variants)], point, loud * (surface == ImpactSurface.Carpet ? 0.5f : 0.8f) * vol, Random.Range(0.95f, 1.05f));
        }

        /// <summary>What a collider is, for the sound of striking it (a world surface, or a sounding thing's material).</summary>
        public static ImpactSurface SurfaceOf(Collider col, Vector3 point, Vector3 normal, out ImpactBody? body)
        {
            body = null;
            if (!col) return ImpactSurface.Carpet;
            var other = col.GetComponentInParent<ImpactSounder>();
            if (other && other.enabled)
            {
                body = other.body;
                return other.body == ImpactBody.Soft ? ImpactSurface.Soft : ImpactSurface.HardFloor;
            }
            if (col.GetComponentInParent<Interaction.Door>()) return ImpactSurface.HardFloor;
            string n = col.name;
            var parent = col.transform.parent;
            if (n.Contains("Rack") || (parent && (parent.name.Contains("Rack") || parent.name == "DuctColliders"))) return ImpactSurface.Metal;
            if (normal.y > 0.55f)
            {
                switch (Surfaces.At(point + Vector3.up * 0.05f, null))
                {
                    case ProceduralSounds.Floor.Carpet: return ImpactSurface.Carpet;
                    case ProceduralSounds.Floor.Wood: return ImpactSurface.HardFloor;
                    case ProceduralSounds.Floor.Concrete: return ImpactSurface.Concrete;
                    case ProceduralSounds.Floor.Metal: return ImpactSurface.Metal;
                    default: return ImpactSurface.Tile;                                        // tile, the pools' water's edge
                }
            }
            return ImpactSurface.Drywall;                                                     // walls, posts, the ceiling
        }

        // ---------------- Scraping ----------------

        internal static void Sliding(ImpactSounder s) { if (s.slipTarget > 0.1f) { s_sliding.Add(s); Ensure(); } }

        sealed class Runner : MonoBehaviour
        {
            readonly List<ImpactSounder> _order = new List<ImpactSounder>();

            void Update()
            {
                float dt = Time.deltaTime;
                var ear = Listener();
                _order.Clear();
                foreach (var s in s_sliding)
                {
                    if (!s || !s.rb) continue;
                    float target = Time.time - s.lastStay < 0.08f ? s.slipTarget : 0f;
                    s.slip = Mathf.MoveTowards(s.slip, target, dt * (target > s.slip ? 6f : 3f));
                    if (s.slip > 0.02f) _order.Add(s);
                }
                s_sliding.RemoveWhere(s => !s || (s.slip <= 0.02f && Time.time - s.lastStay > 0.5f));
                if (ear) _order.Sort((a, b) => (a.transform.position - ear.position).sqrMagnitude.CompareTo((b.transform.position - ear.position).sqrMagnitude));
                float vol = SoundCatalog.Volume(SoundIds.Scrapes) * 0.35f;
                for (int i = 0; i < Loops; i++)
                {
                    var src = s_loops[i];
                    if (!src) continue;
                    var s = i < _order.Count ? _order[i] : null;
                    if (s != s_loopOwner[i])
                    {
                        s_loopOwner[i] = s;
                        if (s)
                        {
                            int kind = s.casters ? 2 : s.slipSurface == ImpactSurface.Carpet ? 0 : 1;
                            if (src.clip != s_scrape[kind]) { src.clip = s_scrape[kind]; src.time = Random.Range(0f, 1.5f); }
                            if (!src.isPlaying) src.Play();
                        }
                    }
                    if (!s) { src.volume = Mathf.MoveTowards(src.volume, 0f, dt * 3f); if (src.volume <= 0f && src.isPlaying) src.Stop(); continue; }
                    src.transform.position = s.transform.position;
                    float mass = Mathf.Clamp(Mathf.Sqrt(s.rb.mass / 20f), 0.25f, 1.2f);
                    float want = Mathf.Pow(Mathf.Clamp01(s.slip / 1.1f), 0.7f) * mass * vol;
                    src.volume = Mathf.MoveTowards(src.volume, want, dt * 4f);
                    src.pitch = (s.casters ? 0.7f : 0.85f) + 0.4f * Mathf.Clamp01(s.slip / 1.5f);
                }
            }
        }

        // ---------------- Voices ----------------

        static Transform Listener()
        {
            var cam = Camera.main;
            return cam ? cam.transform : null;
        }

        static void Ensure()
        {
            if (s_root) return;
            s_root = new GameObject("ImpactAudio");
            s_root.AddComponent<Runner>();
            s_voices.Clear();
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("Impact" + i);
                go.transform.SetParent(s_root.transform, false);
                s_voices.Add(Interaction.ItemModels.Source(go, 18f));
            }
            for (int i = 0; i < Loops; i++)
            {
                var go = new GameObject("Scrape" + i);
                go.transform.SetParent(s_root.transform, false);
                var a = Interaction.ItemModels.Source(go, 14f);
                a.loop = true; a.volume = 0f;
                s_loops[i] = a; s_loopOwner[i] = null;
            }
            if (s_body == null)
            {
                int nb = System.Enum.GetValues(typeof(ImpactBody)).Length, ns = System.Enum.GetValues(typeof(ImpactSurface)).Length;
                s_body = new AudioClip[nb, Variants]; s_surface = new AudioClip[ns, Variants];
                for (int b = 0; b < nb; b++) for (int v = 0; v < Variants; v++) s_body[b, v] = ProceduralSounds.ImpactBodyClip((ImpactBody)b, v);
                for (int b = 0; b < ns; b++) for (int v = 0; v < Variants; v++) s_surface[b, v] = ProceduralSounds.ImpactSurfaceClip((ImpactSurface)b, v);
                s_scrape = new[] { ProceduralSounds.ScrapeLoop(0), ProceduralSounds.ScrapeLoop(1), ProceduralSounds.ScrapeLoop(2) };
            }
        }

        static void Play(AudioClip clip, Vector3 at, float volume, float pitch)
        {
            if (!clip || volume < 0.005f) return;
            AudioSource src = null;
            for (int i = 0; i < s_voices.Count; i++)
            {
                var a = s_voices[(s_voice + i) % s_voices.Count];
                if (a && !a.isPlaying) { src = a; s_voice = (s_voice + i + 1) % s_voices.Count; break; }
            }
            if (!src) { src = s_voices[s_voice]; s_voice = (s_voice + 1) % s_voices.Count; }
            if (!src) return;
            src.transform.position = at;
            src.pitch = pitch;
            src.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_root = null; s_voices.Clear(); s_sliding.Clear();
            for (int i = 0; i < Loops; i++) { s_loops[i] = null; s_loopOwner[i] = null; }
        }
    }
}
