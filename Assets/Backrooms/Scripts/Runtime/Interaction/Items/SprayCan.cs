using System.Collections.Generic;
using Backrooms.Audio;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A can of red spray paint (model: Tools/Blender/build_spraycan.py, 2026-11-04: a 400 ml tinplate aerosol can, an
    /// invented maker's wrap). Held in the free hand; hold the left mouse button to spray where you are looking. The spray
    /// is traced as rays in a cone (about 4.5 degrees: a hand's span off, a fan some 6 cm across, as a can's at the 25 cm
    /// the labels ask); each ray that reaches a surface within 1.8 m lays a little paint there (<see cref="SprayPaint"/>),
    /// so the pattern is the cone's: close a tight line, at a hand's span an even stroke, from far a wide fine dusting
    /// (less of it arrives: it dries and drifts in the air). Held still and close, the paint builds up until it runs.
    /// The mist (2026-11-04): a fast, faint jet along the rays that stops where they land, and a little haze of overspray
    /// that hangs where it hit and drifts; lit by what is behind it (dark in a dark room).
    /// The can holds <see cref="Lifespan"/> seconds of spraying; it sputters near the end and then only hisses.
    /// </summary>
    public class SprayCan : WorldItem, IHandGrip, IGripRound
    {
        /// <summary>Seconds of spraying in a full can (a 400 ml can lasts about this long in one go).</summary>
        public static float Lifespan = 45f;
        public const float Radius = 0.0325f, Height = 0.2075f;
        /// <summary>The actuator's orifice (Tools/Blender/build_spraycan.py: its front, -Y there, +Z here).</summary>
        static readonly Vector3 NozzleLocal = new Vector3(0f, 0.2012f, 0.0094f);

        [NotTuned, Range(0f, 1f)] public float paint = 1f;
        [Tooltip("Rays of spray traced per second at full flow.")] public float dropletsPerSecond = 900f;
        [Tooltip("Spread of the spray cone (standard deviation, degrees).")] public float spread = 4.5f;
        public float range = 1.8f;

        Transform _model, _nozzle;
        ParticleSystem _mist, _haze;
        int _rayCount;
        AudioSource _hiss, _clank;
        float _use, _hissLevel, _acc, _sputter;
        readonly RaycastHit[] _hits = new RaycastHit[8];
        static readonly Dictionary<string, Material> s_mats = new Dictionary<string, Material>();
        static Material s_mist;

        public override Vector3 HoldOffset => new Vector3(0.16f, -0.24f, 0.36f);
        public override Vector3 UseOffset => new Vector3(0.12f, -0.15f, 0.43f);
        // Palm on the back-right of the can (its back outward), fingers round its front along the surface, thumb up and
        // round the other side (mirrored for a left hand by the grabber). The fingers used to point at the can's axis and
        // go through it.
        public override Vector3 GripLocal => new Vector3((Radius + 0.002f) * 0.7071f, 0.1f, -(Radius + 0.002f) * 0.7071f);
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(new Vector3(0.7f, -0.05f, 0.7f), new Vector3(0.7071f, 0f, -0.7071f));
        /// <summary>Round 10: taken from any side round its middle (IGripRound).</summary>
        public void GripAxis(out Vector3 point, out Vector3 axis) { point = Vector3.zero; axis = Vector3.up; }
        public override float GripCurl => 0.95f;
        public float FingerCurl(int finger) => 0.95f;
        public float ThumbCurl => 0.8f;
        public float ThumbOpposition => 0.7f;
        public override float UseBlend => _use;
        public bool Spraying { get; private set; }
        public override bool Pocketable => true;
        public override string Status => paint <= 0f ? "empty" : $"paint {Mathf.CeilToInt(paint * 100f)} %";

        /// <summary>A new full can lying at a point.</summary>
        public static SprayCan Create(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("SprayCan");
            go.transform.SetPositionAndRotation(position, rotation);
            string path = Generation.ArtStyle.Path("Items/SprayCan");          // (its remake in the new look, if there is one)
            var model = Resources.Load<GameObject>(path);
            if (model)
            {
                var m = Instantiate(model, go.transform, false);
                m.name = "Model";
                m.transform.localRotation = model.transform.localRotation;     // keep the import's axis fix
                foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
                // Its baked colour (Tools/Blender/build_items.py with bake_kit.py, 2026-11-04): label, wear, grime.
                var albedo = Resources.Load<Texture2D>(path + "_Albedo");
                float density = albedo ? Generation.BakedMaterials.Density(path) : 0f;
                foreach (var r in m.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var fin = Mat(mats[i] ? mats[i].name.Replace(" (Instance)", "") : "CanRed");
                        mats[i] = albedo ? Generation.BakedMaterials.FromTemplate(fin, albedo, density) : fin;
                    }
                    r.sharedMaterials = mats;
                    r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    r.renderingLayerMask = 0xFFFFFFFFu;            // lit by whatever lights the spot it is in
                }
            }
            else
            {
                var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(p.GetComponent<Collider>());
                p.transform.SetParent(go.transform, false);
                p.transform.localScale = new Vector3(Radius * 2f, Height * 0.5f, Radius * 2f);
                p.transform.localPosition = Vector3.up * Height * 0.5f;
                p.GetComponent<Renderer>().sharedMaterial = Mat("CanRed");
            }
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = Radius; col.height = Height; col.center = Vector3.up * Height * 0.5f; col.direction = 1;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f; rb.linearDamping = 0.1f; rb.angularDamping = 0.4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var can = go.AddComponent<SprayCan>();
            can.itemId = "spraycan"; can.displayName = "Spray paint (red)";
            Generation.PoolLitSwitch.Attach(go);
            return can;
        }

        static Material Mat(string key)
        {
            if (s_mats.TryGetValue(key, out var m) && m) return m;
            var shader = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(shader) { name = "Item_" + key, hideFlags = HideFlags.DontSave };
            Color c; float metal = 0f, smooth = 0.5f;
            switch (key)
            {
                case "CanMetal": c = new Color(0.78f, 0.78f, 0.8f); metal = 0.9f; smooth = 0.7f; break;
                case "CanCap": c = new Color(0.9f, 0.9f, 0.88f); smooth = 0.45f; break;
                case "CanLabel": c = new Color(0.04f, 0.04f, 0.045f); smooth = 0.4f; break;
                default: c = new Color(0.62f, 0.03f, 0.025f); smooth = 0.75f; break;
            }
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            ItemModels.AddDetail(m, key);
            s_mats[key] = m;
            return m;
        }

        protected override void Awake()
        {
            base.Awake();
            _model = transform.Find("Model");
            // The nozzle: the orifice in the front of the actuator on top of the can.
            _nozzle = new GameObject("Nozzle").transform;
            _nozzle.SetParent(transform, false);
            _nozzle.localPosition = NozzleLocal;

            _hiss = ItemModels.Source(gameObject, 12f);
            _hiss.clip = SoundBank.SprayHiss; _hiss.loop = true; _hiss.playOnAwake = false; _hiss.volume = 0f;
            _hiss.spatialBlend = 1f; _hiss.minDistance = 0.4f; _hiss.maxDistance = 12f; _hiss.dopplerLevel = 0f;
            _clank = ItemModels.Source(gameObject, 10f);
            _clank.playOnAwake = false; _clank.spatialBlend = 1f; _clank.minDistance = 0.4f; _clank.maxDistance = 10f;
            BuildMist();
        }

        void BuildMist()
        {
            if (!s_mist)
            {
                var sh = Shader.Find("Backrooms/SprayMist");
                if (sh) s_mist = new Material(sh) { name = "Spray mist", hideFlags = HideFlags.DontSave };
            }
            // The jet: fine paint leaving the nozzle fast, a streak each, gone where its ray lands (emitted by Emit).
            _mist = Particles("Mist", 700, true);
            {
                var size = _mist.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 2.2f));
                var col = _mist.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0.0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.8f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var pr = _mist.GetComponent<ParticleSystemRenderer>();
                pr.renderMode = ParticleSystemRenderMode.Stretch;
                pr.velocityScale = 0.008f; pr.lengthScale = 1.2f;
            }
            // The overspray: a faint haze where it lands, hanging a second or two and drifting.
            _haze = Particles("Haze", 260, false);
            {
                var size = _haze.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 2.6f));
                var col = _haze.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.45f, 0.55f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var limit = _haze.limitVelocityOverLifetime;
                limit.enabled = true; limit.dampen = 0.08f; limit.limit = 0.08f;
                var noise = _haze.noise;
                noise.enabled = true; noise.strength = 0.05f; noise.frequency = 0.6f; noise.scrollSpeed = 0.2f;
            }
        }

        ParticleSystem Particles(string name, int max, bool fast)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.playOnAwake = false;
            main.gravityModifier = fast ? 0.02f : -0.004f;
            main.startLifetime = 1f; main.startSpeed = 0f; main.startSize = 0.01f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = s_mist;
            pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;
            pr.sortingFudge = fast ? 0f : 10f;
            ps.Play();
            return ps;
        }

        protected override void OnPickedUp()
        {
            // Shake it: the ball rattles.
            _clank.PlayOneShot(SoundBank.CanRattle(Random.Range(0, 4)), 0.5f * SoundCatalog.Volume(SoundIds.SprayCan));
        }

        protected override void OnDropped()
        {
            Spraying = false; _use = 0f;
        }

        /// <summary>
        /// Co-op (2026-11-04): another player's can, in their hand as this game shows it: the jet, the haze and the hiss
        /// while they spray, aimed where they look. Their paint comes as paint (SprayPaint's patches); nothing is painted or
        /// used up here.
        /// </summary>
        public void RemoteSpray(bool on, Vector3 eye, Vector3 look, float dt)
        {
            if (!on && _hissLevel <= 0.001f && _use <= 0f) return;
            Vector3 aim = eye + look * range;
            if (Physics.Raycast(eye, look, out var h, range + 0.6f, ~0, QueryTriggerInteraction.Ignore)) aim = h.point;
            _visualOnly = true;
            try { Hold(on, aim, dt); }
            finally { _visualOnly = false; }
        }

        bool _visualOnly;

        public override void Hold(bool use, Vector3 aimAt, float dt)
        {
            bool spray = use && paint > 0f;
            _use = Mathf.MoveTowards(_use, use ? 1f : 0f, dt * 6f);
            Spraying = spray && _use > 0.6f;
            float flow = 0f;
            if (Spraying)
            {
                if (!_visualOnly) paint = Mathf.Max(0f, paint - dt / Mathf.Max(1f, Lifespan));
                // The last of it: spits and sputters.
                if (paint < 0.1f)
                {
                    _sputter += dt * 9f;
                    flow = Mathf.Clamp01(Mathf.PerlinNoise(_sputter, 0.3f) * 2.2f - 0.7f) * Mathf.Lerp(0.25f, 1f, paint / 0.1f);
                }
                else flow = 1f;
                Emit(aimAt, flow, dt);
            }
            if (_nozzle && Spraying) _nozzle.rotation = Quaternion.LookRotation(aimAt - _nozzle.position, Vector3.up);

            // Hiss with the flow; an empty can still gives a weak gasp of propellant when pressed.
            float want = Spraying ? Mathf.Lerp(0.08f, 0.35f, flow) : 0f;
            if (use && paint <= 0f) want = 0.05f;
            _hissLevel = Mathf.MoveTowards(_hissLevel, want, dt * 3f);
            _hiss.volume = _hissLevel * SoundCatalog.Volume(SoundIds.SprayCan);
            _hiss.pitch = 1f + (flow - 1f) * 0.15f;
            if (_hissLevel > 0.001f && !_hiss.isPlaying) _hiss.Play();
            else if (_hissLevel <= 0.001f && _hiss.isPlaying) _hiss.Stop();
        }

        // Paint a ray carries (coats over a square metre): a full can's flow, calibrated so a stroke at 25 cm, moved at
        // about 35 cm a second, lays a coat and a half at its middle (a bold stripe, soft and speckled at its edges); two
        // passes on top of each other at once start to run, as heavy coats do.
        const float RayPaint = 3.4e-5f;
        const int Splats = 4;
        static readonly Color MistColour = new Color(0.78f, 0.1f, 0.08f, 0.2f), HazeColour = new Color(0.85f, 0.32f, 0.3f, 0.05f);

        void Emit(Vector3 aimAt, float flow, float dt)
        {
            _acc += dropletsPerSecond * flow * dt;
            Vector3 from = _nozzle.position;
            Vector3 dir0 = (aimAt - from).normalized;
            var paintSys = SprayPaint.Instance;
            Vector3 a = Vector3.Cross(dir0, Mathf.Abs(dir0.y) > 0.9f ? Vector3.right : Vector3.up).normalized, b = Vector3.Cross(dir0, a);
            float sigma = spread * Mathf.Deg2Rad;
            var ep = new ParticleSystem.EmitParams();
            while (_acc >= 1f)
            {
                _acc -= 1f;
                _rayCount++;
                // Gaussian spread around the aim (Box-Muller).
                float u1 = Mathf.Max(1e-5f, Random.value), u2 = Random.value;
                float rr = Mathf.Sqrt(-2f * Mathf.Log(u1)) * sigma, th = u2 * Mathf.PI * 2f;
                Vector3 dir = (dir0 + a * (Mathf.Cos(th) * rr) + b * (Mathf.Sin(th) * rr)).normalized;
                int n = Physics.RaycastNonAlloc(from, dir, _hits, range, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue; int bi = -1;
                for (int i = 0; i < n; i++)
                {
                    var c = _hits[i].collider;
                    if (_hits[i].distance >= best || c.transform.IsChildOf(transform) || c.GetComponent<CharacterController>()) continue;
                    best = _hits[i].distance; bi = i;
                }
                float reach = bi >= 0 ? best : range;
                // The jet: one streak in two rays, from the nozzle to where the ray ends.
                if (_mist && _rayCount % 2 == 0)
                {
                    float speed = Random.Range(5f, 8f);
                    ep.position = from + dir * 0.004f;
                    ep.velocity = dir * speed;
                    // (lives till it reaches where its ray ends, and a frame or two more: hidden in the wall then)
                    ep.startLifetime = reach / speed + 0.04f;
                    ep.startSize = Random.Range(0.005f, 0.011f);
                    ep.startColor = MistColour;
                    _mist.Emit(ep, 1);
                }
                if (bi < 0) continue;
                var hit = _hits[bi];
                // The overspray: now and then a puff of haze where it lands, drifting off the surface.
                if (_haze && _rayCount % 14 == 0)
                {
                    ep.position = hit.point + hit.normal * 0.02f;
                    ep.velocity = hit.normal * Random.Range(0.05f, 0.18f) + Random.insideUnitSphere * 0.06f;
                    ep.startLifetime = Random.Range(1.2f, 2.6f);
                    ep.startSize = Random.Range(0.03f, 0.07f) * (0.6f + hit.distance);
                    ep.startColor = HazeColour;
                    _haze.Emit(ep, 1);
                }
                // Paint goes on the building (props move; their paint would be left floating).
                if (_visualOnly) continue;                         // (another player's spray: their paint comes from them)
                if (hit.collider.GetComponentInParent<MovableProp>() || hit.collider.GetComponentInParent<WorldItem>()) continue;
                float d = hit.distance;
                // Each ray's own spot: a few millimetres, wider from further (the droplets drift); less arrives from far.
                float s = 0.0022f + d * 0.004f;
                float coats = RayPaint / (2f * Mathf.PI * s * s) * (1f - 0.45f * d / range) * Random.Range(0.85f, 1.15f);
                // Laid as a few splats spread as the cone spreads there (a ray stands for many droplets: one spot a ray
                // left the stroke a grain of separate spots); a far ray's paint the wider (it drifts).
                Vector3 ta = Vector3.Cross(hit.normal, Mathf.Abs(hit.normal.y) > 0.9f ? Vector3.forward : Vector3.up).normalized, tb = Vector3.Cross(hit.normal, ta);
                float sc = d * sigma * 0.55f;
                for (int k = 0; k < Splats; k++)
                {
                    float g1 = Mathf.Sqrt(-2f * Mathf.Log(Mathf.Max(1e-5f, Random.value))), g2 = Random.value * Mathf.PI * 2f;
                    Vector3 o = (ta * Mathf.Cos(g2) + tb * Mathf.Sin(g2)) * (g1 * sc);
                    paintSys.Deposit(hit.point + o, hit.normal, s, coats / Splats);
                }
            }
        }

        void OnDestroy()
        {
            if (_hiss) _hiss.Stop();
        }
    }
}
