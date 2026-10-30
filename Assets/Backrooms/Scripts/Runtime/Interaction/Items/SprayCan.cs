using System.Collections.Generic;
using Backrooms.Audio;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A can of red spray paint (model: Tools/Blender/build_items.py, Resources/Items/SprayCan.fbx). Held in the free
    /// hand; hold the left mouse button to spray where you are looking. Mist leaves the nozzle in a narrow cone and the
    /// droplets that reach a surface within ~1.8 m become paint (<see cref="SprayPaint"/>): a tighter, denser spot up
    /// close, a wider, fainter one from further back. Lingering on one spot builds the paint up until it runs.
    /// The can holds <see cref="Lifespan"/> seconds of spraying; it sputters near the end and then only hisses.
    /// </summary>
    public class SprayCan : WorldItem, IHandGrip, IGripRound
    {
        /// <summary>Seconds of spraying in a full can (a 400 ml can lasts about this long in one go).</summary>
        public static float Lifespan = 45f;
        public const float Radius = 0.033f, Height = 0.226f;

        [NotTuned, Range(0f, 1f)] public float paint = 1f;
        [Tooltip("Droplets that are traced per second at full flow.")] public float dropletsPerSecond = 300f;
        [Tooltip("Spread of the spray cone (standard deviation, degrees).")] public float spread = 4.5f;
        public float range = 1.8f;

        Transform _model, _nozzle;
        ParticleSystem _mist;
        ParticleSystem.EmissionModule _emission;
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
            var model = Resources.Load<GameObject>("Items/SprayCan");
            if (model)
            {
                var m = Instantiate(model, go.transform, false);
                m.name = "Model";
                m.transform.localRotation = model.transform.localRotation;     // keep the import's axis fix
                foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
                foreach (var r in m.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = Mat(mats[i] ? mats[i].name.Replace(" (Instance)", "") : "CanRed");
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
            // The nozzle: at the front of the actuator on top of the can.
            float top = Height;
            foreach (var r in GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, transform.InverseTransformPoint(r.bounds.max).y);
            _nozzle = new GameObject("Nozzle").transform;
            _nozzle.SetParent(transform, false);
            _nozzle.localPosition = new Vector3(0f, top - 0.008f, 0.0115f);

            _hiss = gameObject.AddComponent<AudioSource>();
            _hiss.clip = SoundBank.SprayHiss; _hiss.loop = true; _hiss.playOnAwake = false; _hiss.volume = 0f;
            _hiss.spatialBlend = 1f; _hiss.minDistance = 0.4f; _hiss.maxDistance = 12f; _hiss.dopplerLevel = 0f;
            _clank = gameObject.AddComponent<AudioSource>();
            _clank.playOnAwake = false; _clank.spatialBlend = 1f; _clank.minDistance = 0.4f; _clank.maxDistance = 10f;
            BuildMist();
        }

        void BuildMist()
        {
            var go = new GameObject("Mist");
            go.transform.SetParent(_nozzle, false);
            _mist = go.AddComponent<ParticleSystem>();
            _mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _mist.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.045f);
            main.startColor = new Color(0.8f, 0.07f, 0.05f, 0.22f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.04f;
            main.maxParticles = 400;
            main.playOnAwake = false;
            _emission = _mist.emission;
            _emission.rateOverTime = 0f;
            var shape = _mist.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 7f; shape.radius = 0.0008f;
            var size = _mist.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 2.8f));
            var col = _mist.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var limit = _mist.limitVelocityOverLifetime;
            limit.enabled = true; limit.dampen = 0.25f; limit.limit = 0.6f;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            if (!s_mist)
            {
                var sh = Shader.Find("Backrooms/SprayMist");
                if (sh) s_mist = new Material(sh) { name = "Spray mist", hideFlags = HideFlags.DontSave };
            }
            pr.sharedMaterial = s_mist;
            pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;
            _mist.Play();
        }

        protected override void OnPickedUp()
        {
            // Shake it: the ball rattles.
            _clank.PlayOneShot(SoundBank.CanRattle(Random.Range(0, 4)), 0.5f * SoundCatalog.Volume(SoundIds.SprayCan));
        }

        protected override void OnDropped()
        {
            Spraying = false; _use = 0f;
            _emission.rateOverTime = 0f;
        }

        public override void Hold(bool use, Vector3 aimAt, float dt)
        {
            bool spray = use && paint > 0f;
            _use = Mathf.MoveTowards(_use, use ? 1f : 0f, dt * 6f);
            Spraying = spray && _use > 0.6f;
            float flow = 0f;
            if (Spraying)
            {
                paint = Mathf.Max(0f, paint - dt / Mathf.Max(1f, Lifespan));
                // The last of it: spits and sputters.
                if (paint < 0.1f)
                {
                    _sputter += dt * 9f;
                    flow = Mathf.Clamp01(Mathf.PerlinNoise(_sputter, 0.3f) * 2.2f - 0.7f) * Mathf.Lerp(0.25f, 1f, paint / 0.1f);
                }
                else flow = 1f;
                Emit(aimAt, flow, dt);
            }
            _emission.rateOverTime = 160f * flow;
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

        void Emit(Vector3 aimAt, float flow, float dt)
        {
            _acc += dropletsPerSecond * flow * dt;
            Vector3 from = _nozzle.position;
            Vector3 dir0 = (aimAt - from).normalized;
            var paintSys = SprayPaint.Instance;
            Vector3 a = Vector3.Cross(dir0, Mathf.Abs(dir0.y) > 0.9f ? Vector3.right : Vector3.up).normalized, b = Vector3.Cross(dir0, a);
            float sigma = spread * Mathf.Deg2Rad;
            while (_acc >= 1f)
            {
                _acc -= 1f;
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
                if (bi < 0) continue;
                var hit = _hits[bi];
                // Paint goes on the building (props move; their paint would be left floating).
                if (hit.collider.GetComponentInParent<MovableProp>() || hit.collider.GetComponentInParent<WorldItem>()) continue;
                float d = hit.distance;
                float radius = 0.006f + d * 0.022f * Random.Range(0.7f, 1.3f);
                float strength = Mathf.Lerp(0.3f, 0.07f, d / range) * Random.Range(0.6f, 1.1f);
                paintSys.Deposit(hit.point, hit.normal, radius, strength);
            }
        }

        void OnDestroy()
        {
            if (_hiss) _hiss.Stop();
        }
    }
}
