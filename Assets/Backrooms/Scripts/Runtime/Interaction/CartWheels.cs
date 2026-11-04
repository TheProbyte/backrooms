using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The carts' layouts (Resources/Fittings/Cart_Layout.json, Tools/Blender/build_carts.py, 2026-11-04): each cart's
    /// collision boxes, decks, handle and casters, and where each kind of fork carries its wheel's axle.
    /// </summary>
    public static class CartLayouts
    {
        [System.Serializable] class CartJson { public float[] boxes_flat, decks_flat, handle_flat, casters_flat, wheels_flat; public float mass; }
        [System.Serializable] class AxlesJson { public float[] Caster4Swivel, Caster4Rigid, Caster5Swivel, Caster5Rigid; }
        [System.Serializable] class FileJson { public CartJson UtilityCart, JanitorCart, PlatformTruck; public AxlesJson axles; }

        public class Caster
        {
            public Vector3 mount;          // a fork: its top plate's middle; a big wheel (size 8): its axle
            public bool swivel;
            public int size;               // 4, 5 (casters) or 8 (a wheel on a fixed axle)
        }

        public class Cart
        {
            public readonly List<(Vector3 centre, Vector3 size)> boxes = new List<(Vector3, Vector3)>();
            public readonly List<Bounds> decks = new List<Bounds>();
            public readonly List<Caster> casters = new List<Caster>();
            public Vector3 handleA, handleB;
            public float mass = 12f;
        }

        static Dictionary<MovableKind, Cart> s_carts;
        static readonly Dictionary<string, Vector3> s_axles = new Dictionary<string, Vector3>();

        public static bool IsCart(MovableKind k) => k == MovableKind.UtilityCart || k == MovableKind.JanitorCart || k == MovableKind.PlatformTruck;

        /// <summary>The model's name in Resources/Fittings.</summary>
        public static string Model(MovableKind k) => k.ToString();

        // Blender -> Unity, as the item and fitting models import: (x, y, z) -> (-x, z, -y); sizes (x, z, y).
        static Vector3 U(float[] a, int i) => new Vector3(-a[i], a[i + 2], -a[i + 1]);
        static Vector3 Sz(float[] a, int i) => new Vector3(a[i], a[i + 2], a[i + 1]);

        static void Read()
        {
            if (s_carts != null) return;
            s_carts = new Dictionary<MovableKind, Cart>();
            var t = Resources.Load<TextAsset>("Fittings/Cart_Layout");
            var j = t ? JsonUtility.FromJson<FileJson>(t.text) : null;
            if (j == null) return;
            void One(MovableKind k, CartJson c)
            {
                if (c == null || c.boxes_flat == null) return;
                var cart = new Cart { mass = c.mass > 0f ? c.mass : 12f };
                for (int i = 0; i + 5 < c.boxes_flat.Length; i += 6) cart.boxes.Add((U(c.boxes_flat, i), Sz(c.boxes_flat, i + 3)));
                // The box that rolls (the casters' footprint, its foot on the floor) goes first: MovableProp gives the first box
                // the casters' friction.
                int roll = 0; float lowest = float.MaxValue;
                for (int i = 0; i < cart.boxes.Count; i++)
                {
                    var (bc, bs) = cart.boxes[i];
                    float foot = bc.y - bs.y * 0.5f, top = bc.y + bs.y * 0.5f;
                    if (foot < 0.01f && top < lowest) { lowest = top; roll = i; }
                }
                var r = cart.boxes[roll]; cart.boxes.RemoveAt(roll); cart.boxes.Insert(0, r);
                if (c.decks_flat != null)
                    for (int i = 0; i + 5 < c.decks_flat.Length; i += 6)
                    {
                        Vector3 a = U(c.decks_flat, i), b = U(c.decks_flat, i + 3);
                        var bd = new Bounds((a + b) * 0.5f, Vector3.zero); bd.Encapsulate(a); bd.Encapsulate(b);
                        cart.decks.Add(bd);
                    }
                if (c.handle_flat != null && c.handle_flat.Length >= 6) { cart.handleA = U(c.handle_flat, 0); cart.handleB = U(c.handle_flat, 3); }
                if (c.casters_flat != null)
                    for (int i = 0; i + 4 < c.casters_flat.Length; i += 5)
                        cart.casters.Add(new Caster { mount = U(c.casters_flat, i), swivel = c.casters_flat[i + 3] > 0.5f, size = Mathf.RoundToInt(c.casters_flat[i + 4]) });
                if (c.wheels_flat != null)
                    for (int i = 0; i + 3 < c.wheels_flat.Length; i += 4)
                        cart.casters.Add(new Caster { mount = U(c.wheels_flat, i), swivel = false, size = Mathf.RoundToInt(c.wheels_flat[i + 3]) });
                s_carts[k] = cart;
            }
            One(MovableKind.UtilityCart, j.UtilityCart);
            One(MovableKind.JanitorCart, j.JanitorCart);
            One(MovableKind.PlatformTruck, j.PlatformTruck);
            if (j.axles != null)
            {
                void Ax(string n, float[] v) { if (v != null && v.Length >= 3) s_axles[n] = U(v, 0); }
                Ax("Caster4Swivel", j.axles.Caster4Swivel); Ax("Caster4Rigid", j.axles.Caster4Rigid);
                Ax("Caster5Swivel", j.axles.Caster5Swivel); Ax("Caster5Rigid", j.axles.Caster5Rigid);
            }
        }

        public static Cart Get(MovableKind k) { Read(); return s_carts.TryGetValue(k, out var c) ? c : null; }

        /// <summary>Where a fork carries its wheel's axle (fork frame).</summary>
        public static Vector3 Axle(string fork) { Read(); return s_axles.TryGetValue(fork, out var a) ? a : new Vector3(0f, -0.08f, 0f); }
    }

    /// <summary>
    /// A cart's wheels (2026-11-04; the owner chose a utility cart, a janitor cart and a platform trolley): the casters'
    /// forks and wheels as their own parts on the cart (Resources/Fittings), and how a cart on casters behaves. Pushed with
    /// both hands like any heavy thing (PropGrabber.Furniture), it rolls: its casters' box slides on the floor with almost
    /// no friction (MovableProp), the fixed wheels at its far end will not slide sideways (a force at their axle cancels the
    /// sideways slip, up to what the tyres' grip could), so it steers from the handle end about them as a real cart does,
    /// and it slows a little as it rolls (rolling resistance). The swivel casters at the handle end trail the way they
    /// move (each turned so its wheel follows its axis, quicker the faster it rolls), every wheel turns as far as it rolls.
    /// It rumbles as it rolls, quieter and duller on carpet than on concrete or tile, and rattles as such a cart does (the
    /// plastic cart's trays, the janitor cart's bottles, the trolley's steel). What stands on its decks goes with it (its
    /// speed eased toward the deck's under it), so a box rides it across a room instead of skating off as it starts.
    /// </summary>
    public class CartWheels : MonoBehaviour
    {
        class Wheel
        {
            public Transform fork, wheel;
            public bool swivel;
            public float radius;
            public Vector3 mount;      // cart frame
            public float yaw, spin;
        }

        MovableProp _prop;
        Rigidbody _rb;
        CartLayouts.Cart _cart;
        MovableKind _kind;
        readonly List<Wheel> _wheels = new List<Wheel>();
        AudioSource _roll, _rattle;
        AudioLowPassFilter _rollLp;
        Audio.SoundRoute _route;
        float _nextFloor, _floorGain = 1f, _floorCut = 4000f;
        bool _soft = true;
        float _rollCut = 4000f;
        float _speed;

        /// <summary>The speed it rolls at now (m/s, for tests).</summary>
        public float Speed => _speed;

        public static void Attach(MovableProp mp)
        {
            if (!mp || !CartLayouts.IsCart(mp.Spec.kind) || mp.GetComponent<CartWheels>()) return;
            var c = mp.gameObject.AddComponent<CartWheels>();
            c._prop = mp; c._rb = mp.Body; c._kind = mp.Spec.kind;
            c._cart = CartLayouts.Get(mp.Spec.kind);
            c.Build();
        }

        void Build()
        {
            if (_cart == null) return;
            uint mask = _prop.Spec.renderMask;
            foreach (var c in _cart.casters)
            {
                var w = new Wheel { swivel = c.swivel, mount = c.mount };
                if (c.size == 8)
                {
                    w.radius = 0.1016f;
                    w.wheel = FittingModels.Spawn("Wheel8", transform, mask, c.mount, Quaternion.identity);
                }
                else
                {
                    string fork = $"Caster{c.size}{(c.swivel ? "Swivel" : "Rigid")}";
                    w.radius = c.size == 5 ? 0.0635f : 0.0508f;
                    w.fork = FittingModels.Spawn(fork, transform, mask, c.mount, Quaternion.identity);
                    if (w.fork) w.wheel = FittingModels.Spawn("Wheel" + c.size, w.fork, mask, CartLayouts.Axle(fork), Quaternion.identity);
                    // a little turned to start with, as casters are left
                    if (c.swivel) w.yaw = Mathf.Repeat(c.mount.x * 997f + c.mount.z * 313f, 1f) * 360f;
                }
                foreach (var t in new[] { w.fork, w.wheel })
                    if (t) foreach (var r in t.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = ShadowCastingMode.On; }
                _wheels.Add(w);
                Pose(w);
            }
            // Its sounds: the wheels' rumble and its own rattle, from under the middle.
            var go = new GameObject("CartSound");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            _roll = ItemModels.Source(go, 18f);
            _roll.loop = true;
            _rollLp = go.AddComponent<AudioLowPassFilter>();
            _rollLp.cutoffFrequency = 4000f;
            var go2 = new GameObject("CartRattle");
            go2.transform.SetParent(transform, false);
            go2.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            _rattle = ItemModels.Source(go2, 16f);
            _rattle.loop = true;
            CartSounds.Make();
            _route = Audio.SoundRoute.Add(_roll); Audio.SoundRoute.Add(_rattle);        // round corners through doorways
            _roll.clip = CartSounds.Roll;
            _rattle.clip = _kind == MovableKind.PlatformTruck ? CartSounds.Steel : _kind == MovableKind.JanitorCart ? CartSounds.Bottles : CartSounds.Plastic;
        }

        void Pose(Wheel w)
        {
            if (w.fork) w.fork.localRotation = Quaternion.Euler(0f, w.yaw, 0f);
            if (w.wheel) w.wheel.localRotation = Quaternion.Euler(w.spin, 0f, 0f);
        }

        bool Upright => transform.up.y > 0.8f;

        void FixedUpdate()
        {
            if (!_rb || _rb.isKinematic || _cart == null || !Upright) return;
            float dt = Time.fixedDeltaTime, m = _rb.mass;
            // The fixed wheels hold the cart's far end from sliding sideways (up to the tyres' grip).
            int fixedN = 0;
            foreach (var w in _wheels) if (!w.swivel) fixedN++;
            if (fixedN > 0)
            {
                Vector3 side = transform.right;
                float grip = 0.85f * m * 9.81f * 0.5f / fixedN;
                foreach (var w in _wheels)
                {
                    if (w.swivel) continue;
                    Vector3 p = transform.TransformPoint(new Vector3(w.mount.x, 0.02f, w.mount.z));
                    float lat = Vector3.Dot(_rb.GetPointVelocity(p), side);
                    float f = Mathf.Clamp(-lat * m * 0.35f / fixedN / dt, -grip, grip);
                    _rb.AddForceAtPosition(side * f, p);
                }
            }
            // Rolling resistance: more on carpet (the tyres sink into its pile) than on a hard floor.
            Vector3 v = _rb.linearVelocity; v.y = 0f;
            float sp = v.magnitude;
            float crr = _soft ? 0.04f : 0.013f;
            if (sp > 0.005f) _rb.AddForce(-v / sp * Mathf.Min(sp * m / dt, crr * m * 9.81f));
            // What stands on its decks rides with it.
            if (sp > 0.03f || _rb.angularVelocity.sqrMagnitude > 0.01f) Riders(dt);
        }

        static readonly Collider[] s_riders = new Collider[24];

        void Riders(float dt)
        {
            foreach (var d in _cart.decks)
            {
                Vector3 c = transform.TransformPoint(d.center + Vector3.up * 0.08f);
                Vector3 half = d.extents + new Vector3(0.02f, 0.1f, 0.02f);
                int n = Physics.OverlapBoxNonAlloc(c, half, s_riders, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var rb = s_riders[i].attachedRigidbody;
                    if (!rb || rb == _rb) continue;
                    // A thing standing still on it (frozen where it was left): it goes with the cart now.
                    if (rb.isKinematic) { var still = rb.GetComponent<MovableProp>(); if (still && !still.Pinned) still.Wake(); continue; }
                    var item = rb.GetComponent<WorldItem>();
                    if (item && item.Held) continue;
                    var prop = rb.GetComponent<MovableProp>();
                    if (prop && prop.held) continue;
                    Vector3 want = _rb.GetPointVelocity(rb.worldCenterOfMass);
                    Vector3 rel = want - rb.linearVelocity; rel.y = 0f;
                    if (rel.sqrMagnitude > 1.5f * 1.5f) continue;                 // knocked off hard: it goes
                    rb.AddForce(rel * Mathf.Min(1f, dt * 9f), ForceMode.VelocityChange);
                }
            }
        }

        void Update()
        {
            if (_cart == null) return;
            float dt = Time.deltaTime;
            bool moving = _rb && !_rb.isKinematic;
            Vector3 v = moving ? _rb.linearVelocity : Vector3.zero;
            v.y = 0f;
            _speed = Mathf.Lerp(_speed, moving && Upright ? v.magnitude : 0f, 1f - Mathf.Exp(-dt / 0.08f));
            if (moving)
                foreach (var w in _wheels)
                {
                    Vector3 mountW = transform.TransformPoint(w.mount);
                    Vector3 pv = _rb.GetPointVelocity(mountW);
                    Vector3 local = transform.InverseTransformDirection(pv);
                    float s = new Vector2(local.x, local.z).magnitude;
                    if (w.swivel && s > 0.03f)
                    {
                        // Turned so its wheel trails its axis the way it moves.
                        float target = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                        w.yaw = Mathf.MoveTowardsAngle(w.yaw, target, (60f + 1400f * s) * dt);
                    }
                    Vector3 dir = w.fork ? w.fork.forward : transform.forward;
                    float roll = Vector3.Dot(pv, dir);
                    w.spin = Mathf.Repeat(w.spin + roll / Mathf.Max(0.02f, w.radius) * Mathf.Rad2Deg * dt, 360f);
                    Pose(w);
                }
            UpdateSound(dt);
        }

        void UpdateSound(float dt)
        {
            if (!_roll) return;
            if (Time.time >= _nextFloor && (_speed > 0.02f || _nextFloor == 0f))
            {
                _nextFloor = Time.time + 0.4f;
                var f = Audio.Surfaces.At(transform.position + Vector3.up * 0.1f, transform);
                _soft = f == Audio.ProceduralSounds.Floor.Carpet;
                _floorGain = _soft ? 0.32f : 1f;
                _floorCut = _soft ? 900f : 5200f;
            }
            float s = Mathf.Clamp01(_speed / 1.6f);
            float vol = s > 0.01f ? Mathf.Lerp(0.05f, 0.55f, s) * _floorGain : 0f;
            float k = 1f - Mathf.Exp(-dt / 0.12f);
            _roll.volume = Mathf.Lerp(_roll.volume, vol, k);
            _roll.pitch = 0.75f + 0.45f * s;
            // (the floor's cut, through the route's filter when it has one: one filter a voice)
            _rollCut = Mathf.Lerp(_rollCut, _floorCut, k);
            if (_route) _route.baseCutoff = _rollCut; else if (_rollLp) _rollLp.cutoffFrequency = _rollCut;
            float rv = s > 0.01f ? Mathf.Lerp(0f, 0.32f, s * s + 0.15f * s) * Mathf.Lerp(0.45f, 1f, _floorGain) : 0f;
            _rattle.volume = Mathf.Lerp(_rattle.volume, rv, k);
            _rattle.pitch = 0.9f + 0.2f * s;
            bool want = _roll.volume > 0.003f;
            if (want && !_roll.isPlaying) { _roll.time = Random.Range(0f, _roll.clip.length * 0.9f); _roll.Play(); _rattle.Play(); }
            else if (!want && _roll.isPlaying) { _roll.Stop(); _rattle.Stop(); }
        }
    }

    /// <summary>The carts' sounds, made in code: the wheels' rumble, and the rattles of a plastic cart, bottles, steel.</summary>
    public static class CartSounds
    {
        public static AudioClip Roll, Plastic, Bottles, Steel;
        const int Rate = 44100;

        static AudioClip Loop(string name, float[] d) { var c = AudioClip.Create(name, d.Length, 1, Rate, false); c.SetData(d, 0); return c; }

        public static void Make()
        {
            if (Roll) return;
            var r = new System.Random(5531);
            float N() => (float)(r.NextDouble() * 2.0 - 1.0);
            int n = Rate * 3;
            // The wheels: a low rumble (rubber over the floor's grain) with the bumps of their flat spots and the floor's
            // joints, a little swish of the bearings.
            {
                var d = new float[n];
                float lp1 = 0f, lp2 = 0f, hp = 0f;
                float bump = 0f;
                for (int i = 0; i < n; i++)
                {
                    float x = N();
                    lp1 += (x - lp1) * 0.035f; lp2 += (lp1 - lp2) * 0.05f;
                    hp += (x - hp) * 0.2f;
                    if (r.NextDouble() < 9.0 / Rate) bump = 0.6f + 0.4f * (float)r.NextDouble();
                    bump *= 0.9985f;
                    float t = i / (float)Rate;
                    d[i] = lp2 * 5.5f + lp2 * bump * 4f * Mathf.Sin(t * 2f * Mathf.PI * 70f) + (x - hp) * 0.015f;
                }
                Fade(d);
                Roll = Loop("CartRoll", d);
            }
            Plastic = Loop("CartPlastic", Rattle(r, 0.28f, 900f, 2200f, 14f, 0.02f));
            Bottles = Loop("CartBottles", Rattle(r, 0.22f, 1800f, 3400f, 9f, 0.12f));
            Steel = Loop("CartSteel", Rattle(r, 0.3f, 1300f, 4200f, 22f, 0.35f));
        }

        /// <summary>A rattle: short knocks of things against each other at random, each a few rings of its own.</summary>
        static float[] Rattle(System.Random r, float level, float f0, float f1, float rate, float ring)
        {
            int n = Rate * 3;
            var d = new float[n];
            var hits = new List<(int at, float f, float a, float decay)>();
            int k = 0;
            while (k < n)
            {
                k += (int)(Rate / rate * (0.3 + 1.4 * r.NextDouble()));
                if (k >= n) break;
                hits.Add((k, Mathf.Lerp(f0, f1, (float)r.NextDouble()), level * (0.3f + 0.7f * (float)r.NextDouble()), Mathf.Lerp(900f, 120f, ring)));
            }
            foreach (var (at, f, a, decay) in hits)
            {
                int len = Mathf.Min(n - at, (int)(Rate * 0.08f));
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)Rate;
                    float env = Mathf.Exp(-t * decay);
                    d[at + i] += a * env * (Mathf.Sin(t * 2f * Mathf.PI * f) * 0.7f + Mathf.Sin(t * 2f * Mathf.PI * f * 1.53f) * 0.3f)
                               + a * Mathf.Exp(-t * 2500f) * (float)(r.NextDouble() * 2 - 1) * 0.6f;
                }
            }
            Fade(d);
            return d;
        }

        /// <summary>The loop's ends faded into each other so it repeats without a click.</summary>
        static void Fade(float[] d)
        {
            int f = Rate / 20;
            for (int i = 0; i < f; i++)
            {
                float t = i / (float)f;
                float a = d[i], b = d[d.Length - f + i];
                d[d.Length - f + i] = b * (1f - t) + a * t;
            }
        }
    }
}
