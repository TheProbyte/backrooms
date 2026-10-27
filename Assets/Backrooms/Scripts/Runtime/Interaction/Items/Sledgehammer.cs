using Backrooms.Audio;
using Backrooms.Destruction;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A short-handled sledgehammer (a 4 lb club hammer, 43 cm), swung one-handed (the other hand keeps filming). Carried
    /// choked up near the head, the head in view at the lower right. Hold the left mouse button to wind up: the hand slides
    /// down to the end of the handle and raises it over the shoulder (the longer, up to fullWindUp, the harder the blow); if
    /// the wall is a little out of reach you step in. Release to swing: the head comes over and down onto what the view is
    /// on. A wall breaks (Destruction.WallBreaker): the head stops dead against a steel stud or a post and springs back,
    /// punches on into a hole it makes, and sails through when it meets nothing. Furniture and loose things are knocked.
    /// Thrown hard at a wall, it breaks that too. Its settings (how wide and how hard it breaks, the wind-up) are tuned in
    /// game with F2 (ItemTuningMenu).
    /// </summary>
    public class Sledgehammer : WorldItem
    {
        const float HeadY = SledgehammerModel.HeadY, HeadHalf = SledgehammerModel.HeadHalfZ;
        const float GripEnd = 0.05f, GripCarry = 0.27f;
        [Tooltip("Seconds of winding up for the hardest blow (a quick click is a tap).")]
        [Range(0.1f, 2f)] public float fullWindUp = 0.6f;
        [Tooltip("How wide the hardest blow breaks: the radius of drywall it knocks out (m). Over a stud the board breaks less; the far board, knocked out from behind, a little less.")]
        [Range(0.05f, 0.6f)] public float breakRadius = 0.22f;
        [Tooltip("How wide a quick click (no wind-up) breaks (m).")]
        [Range(0.02f, 0.4f)] public float tapRadius = 0.08f;
        [Tooltip("How much each blow destroys: how hard it lands on wood studs and baseboards and how far the pieces fly (1 = an ordinary swing). Below about 0.9 a quick click only cracks.")]
        [Range(0.2f, 4f)] public float power = 1.3f;
        [Tooltip("How far the cracks reach round what a blow knocks out, times the break radius.")]
        [Range(1f, 4f)] public float crackSpread = 2.2f;
        [Tooltip("Seconds from the top of the swing to the wall at full wind-up (a quick click swings faster).")]
        [Range(0.06f, 0.5f)] public float swingTime = 0.2f;
        [Tooltip("Furthest the hand reaches from the shoulder (m); a wall farther than this and you step in toward it.")]
        [Range(0.4f, 1f)] public float armReach = 0.68f;

        enum Phase { Carry, WindUp, Swing, Recoil, Follow, Recover }
        Phase _phase = Phase.Carry;
        float _t, _charge, _energy, _give;

        struct Pose { public Vector3 h; public Quaternion r; public float g; }
        Pose _now, _from, _impact;
        bool _poseValid;
        Vector3 _axis, _aimPoint, _aimDir;
        bool _reachable, _hitDone;
        float _swingTime;
        WallBreaker.Impact _result;

        PropGrabber _hand;
        CamcorderPlayerController _player;
        HandheldCameraShake _shake;
        bool _stepping;
        Quaternion _view = Quaternion.identity; Vector3 _viewPos; bool _viewInit;
        Vector3 _strikeFrom;
        AudioSource _fx;
        float _quietUntil;

        public override Vector3 HoldOffset => new Vector3(0.22f, -0.36f, 0.42f);
        public override Vector3 GripLocal => new Vector3(SledgehammerModel.HandleA * 0.95f, _now.g > 0f ? _now.g : GripCarry, 0f);
        // Fingers round the handle toward the striking face (+z), the back of the hand outward (+x).
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(Vector3.forward, Vector3.right);
        public override float GripCurl => 0.97f;
        public override bool DropsStraight => false;
        public override string Status => _phase == Phase.WindUp ? $"sledgehammer: winding up {Mathf.RoundToInt(_charge * 100f)} %" : "sledgehammer (hold to wind up, release to swing)";
        public override float UseBlend => _phase == Phase.Carry ? 0f : 1f;
        // Holding a 2 kg hammer up over the shoulder: the camera strains with it.
        public override float Effort => _phase == Phase.WindUp ? _charge * 0.6f : 0f;

        public static Sledgehammer Create(Vector3 position, Quaternion rotation)
        {
            var go = new GameObject("Sledgehammer");
            go.transform.SetPositionAndRotation(position, rotation);
            var model = new GameObject("Model");
            model.transform.SetParent(go.transform, false);
            model.AddComponent<MeshFilter>().sharedMesh = SledgehammerModel.Mesh;
            var r = model.AddComponent<MeshRenderer>();
            r.sharedMaterials = SledgehammerModel.Materials;
            ItemModels.Prepare(r);
            // Handle (a capsule) and head (a box).
            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 1; cap.radius = 0.018f; cap.height = HeadY + 0.02f; cap.center = new Vector3(0f, (HeadY + 0.02f) * 0.5f, 0f);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(SledgehammerModel.HeadHalfX * 2f, SledgehammerModel.HeadHalfY * 2f, HeadHalf * 2f);
            box.center = new Vector3(0f, HeadY, 0f);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 2.1f; rb.linearDamping = 0.05f; rb.angularDamping = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.centerOfMass = new Vector3(0f, HeadY * 0.82f, 0f);     // most of its weight is the head
            var s = go.AddComponent<Sledgehammer>();
            s.itemId = "sledgehammer"; s.displayName = "Sledgehammer";
            Generation.PoolLitSwitch.Attach(go);
            return s;
        }

        protected override void Awake()
        {
            base.Awake();
            _fx = ItemModels.Source(gameObject, 20f);
            _now.g = GripCarry;
        }

        protected override void OnPickedUp()
        {
            _phase = Phase.Carry; _charge = 0f; _viewInit = false; _poseValid = false;
            _fx.PlayOneShot(SoundBank.WoodStudHit(1), 0.25f);
        }

        protected override void OnDropped()
        {
            StopStepping();
            _phase = Phase.Carry; _charge = 0f; _poseValid = false;
        }

        // ---------------- Controls (Update) ----------------

        public override void HoldUpdate(in HoldInput i)
        {
            _hand = i.hand;
            if (!_player && _hand) _player = _hand.GetComponent<CamcorderPlayerController>();
            if (!_shake && _player) _shake = _player.GetComponentInChildren<HandheldCameraShake>();
            if (_phase == Phase.Swing || _phase == Phase.Recoil || _phase == Phase.Follow) { } // the blow goes where it was sent
            else
            {
                _aimDir = i.aimDir;
                _aimPoint = i.hit && i.hitInfo.distance < 2.4f ? i.hitInfo.point : i.eye + i.aimDir * 1.1f;
            }
            float dt = i.dt;
            _t += dt;
            switch (_phase)
            {
                case Phase.Carry:
                case Phase.Recover:
                    if (i.pressed && (_phase == Phase.Carry || _t > 0.18f)) { Begin(Phase.WindUp); _charge = 0f; }
                    else if (_phase == Phase.Recover && _t >= 0.5f) Begin(Phase.Carry);
                    break;
                case Phase.WindUp:
                    _charge = Mathf.Clamp01(_charge + dt / fullWindUp);
                    StepIn(i.hit && i.hitInfo.distance < 2.4f);
                    if (!i.use) Release();
                    break;
                case Phase.Swing:
                    if (_t >= _swingTime && !_hitDone) Impact();
                    break;
                case Phase.Recoil:
                    if (_t >= 0.16f) Begin(Phase.Recover);
                    break;
                case Phase.Follow:
                    if (_t >= 0.18f) Begin(Phase.Recover);
                    break;
            }
        }

        void Begin(Phase p)
        {
            _from = _now;
            _phase = p; _t = 0f;
        }

        /// <summary>The wall is a little out of reach: walk in toward it while winding up (as the hand does for furniture).</summary>
        void StepIn(bool aimingAtSomething)
        {
            if (!_player || !_hand) return;
            bool want = false;
            if (aimingAtSomething)
            {
                Solve(_aimPoint, _aimDir, out _, out bool ok);
                float flat = Vector3.ProjectOnPlane(_aimPoint - _hand.Shoulder, Vector3.up).magnitude;
                want = !ok && flat < armReach + 1.1f;
            }
            if (want) { _player.botInput = new Vector2(0f, 0.6f); _stepping = true; }
            else StopStepping();
        }

        void StopStepping()
        {
            if (_stepping && _player) _player.botInput = null;
            _stepping = false;
        }

        void Release()
        {
            StopStepping();
            _energy = Mathf.Lerp(0.32f, 1f, _charge);
            _impact = Solve(_aimPoint, _aimDir, out _axis, out _reachable);
            var cam = Camera.main;
            _strikeFrom = cam ? cam.transform.position : Shoulder;
            _swingTime = Mathf.Lerp(swingTime * 0.6f, swingTime, _charge);
            _hitDone = false;
            Begin(Phase.Swing);
            _fx.pitch = Mathf.Lerp(1.15f, 0.9f, _charge);
            _fx.PlayOneShot(SoundBank.HammerSwing(Random.Range(0, 3)), 0.25f + 0.55f * _energy);
            if (_shake) _shake.Jolt(new Vector3(-1.5f - 2f * _energy, 1.2f * _energy, 0.8f));
        }

        void Impact()
        {
            _hitDone = true;
            var breaker = WallBreaker.Instance;
            // Along the line the view was on when the swing began (where the head was sent).
            float dist = Vector3.Distance(_strikeFrom, _aimPoint);
            _result = default;
            if (_reachable && breaker) _result = breaker.Strike(_strikeFrom, _aimDir, dist + 0.12f, BlowFor(_energy, _charge), _hand ? _hand.transform : null);
            if (!_result.hit) { Begin(Phase.Follow); _give = 1f; return; }
            _give = _result.give;
            if (_shake)
            {
                float hard = (1f - _give) * _energy;
                _shake.Jolt(new Vector3(5f + 18f * hard, (Random.value - 0.5f) * 10f * hard, -3f - 6f * hard));
            }
            var chain = FindAnyObjectByType<CamcorderAudioChain>();
            if (chain) chain.HandlingImpulse();
            Begin(_give > 0.6f ? Phase.Follow : Phase.Recoil);
        }

        /// <summary>A blow of this hammer: its strength (0..1, times its power), as wide as the wind-up and its settings give.</summary>
        Blow BlowFor(float strength, float windUp) => new Blow
        {
            energy = strength * power,
            radius = Mathf.Lerp(tapRadius, breakRadius, Mathf.Clamp01(windUp)),
            crackSpread = crackSpread,
        };

        // ---------------- Where it is (LateUpdate, from the hand) ----------------

        Vector3 Shoulder => _hand ? _hand.Shoulder : transform.position;

        /// <summary>
        /// The pose that puts the face of the head on a point, moving along a direction: the handle across the blow (hand
        /// below and to the right of the head), tipped toward the wall as far as it needs (up to 60 degrees) for the hand to
        /// be within reach. axis: what the swing turns about.
        /// </summary>
        Pose Solve(Vector3 x, Vector3 f, out Vector3 axis, out bool ok)
        {
            f.Normalize();
            Vector3 up = _view * Vector3.up, right = _view * Vector3.right;
            Vector3 u0 = Vector3.ProjectOnPlane(up * 0.85f - right * 0.35f, f).normalized;
            if (u0.sqrMagnitude < 0.5f) u0 = Vector3.ProjectOnPlane(-right, f).normalized;
            Vector3 s = Shoulder;
            Pose best = default; ok = false; axis = Vector3.Cross(u0, f).normalized;
            for (int k = 0; k <= 12; k++)
            {
                float a = k * 5f * Mathf.Deg2Rad;
                Vector3 d = u0 * Mathf.Cos(a) + f * Mathf.Sin(a);
                Vector3 face = f * Mathf.Cos(a) - u0 * Mathf.Sin(a);
                Vector3 centre = x - face * HeadHalf;
                Vector3 h = centre - d * (HeadY - GripEnd);
                best = new Pose { h = h, r = Quaternion.LookRotation(face, d), g = GripEnd };
                axis = Vector3.Cross(d, face).normalized;
                if ((h - s).magnitude <= armReach) { ok = true; break; }
            }
            if (!ok) best.h = s + Vector3.ClampMagnitude(best.h - s, armReach);
            return best;
        }

        Pose Carry()
        {
            Vector3 h = _viewPos + _view * new Vector3(0.24f, -0.37f, 0.42f);
            Vector3 d = _view * new Vector3(-0.15f, 0.55f, 0.82f).normalized;
            Vector3 face = Vector3.ProjectOnPlane(_view * new Vector3(-0.55f, -0.1f, 1f), d).normalized;
            return new Pose { h = h, r = Quaternion.LookRotation(face, d), g = GripCarry };
        }

        /// <summary>Up over the shoulder, turned back about the swing's axis from where it will strike.</summary>
        Pose Raised(Pose strike, Vector3 axis, float charge)
        {
            float back = Mathf.Lerp(80f, 145f, charge);
            var turn = Quaternion.AngleAxis(-back, axis);
            Vector3 h = Shoulder + _view * new Vector3(0.05f, Mathf.Lerp(0.12f, 0.3f, charge), Mathf.Lerp(0.12f, -0.03f, charge));
            return new Pose { h = h, r = turn * strike.r, g = GripEnd };
        }

        static Pose Blend(Pose a, Pose b, float t) => new Pose { h = Vector3.Lerp(a.h, b.h, t), r = Quaternion.Slerp(a.r, b.r, t), g = Mathf.Lerp(a.g, b.g, t) };

        public override float UsePose(out Vector3 position, out Quaternion rotation)
        {
            position = transform.position; rotation = transform.rotation;
            if (!Held || !_hand) return 0f;
            var pivot = _player && _player.cameraPitchPivot ? _player.cameraPitchPivot : (Camera.main ? Camera.main.transform : transform);
            float dt = Time.deltaTime;
            // The view the hammer is held in lags quick turns a little (it is heavy).
            if (!_viewInit) { _view = pivot.rotation; _viewPos = pivot.position; _viewInit = true; }
            _view = Quaternion.Slerp(_view, pivot.rotation, 1f - Mathf.Exp(-dt / 0.09f));
            if (Quaternion.Angle(_view, pivot.rotation) > 20f) _view = Quaternion.RotateTowards(pivot.rotation, _view, 20f);
            _viewPos = Vector3.Lerp(_viewPos, pivot.position, 1f - Mathf.Exp(-dt / 0.04f));
            _viewPos = pivot.position + Vector3.ClampMagnitude(_viewPos - pivot.position, 0.06f);

            var carry = Carry();
            if (!_poseValid) { _now = carry; _from = carry; _poseValid = true; }
            switch (_phase)
            {
                case Phase.Carry:
                {
                    // Walking: the head bobs a little with the steps.
                    float sp = _player ? _player.PlanarVelocity.magnitude : 0f;
                    carry.h += _view * new Vector3(0f, Mathf.Sin(Time.time * 7f) * 0.006f, 0f) * Mathf.Clamp01(sp / 1.5f);
                    _now = Blend(_now, carry, 1f - Mathf.Exp(-dt / 0.06f));
                    break;
                }
                case Phase.WindUp:
                {
                    var strike = Solve(_aimPoint, _aimDir, out var axis, out _);
                    var raised = Raised(strike, axis, _charge);
                    // The hand slides down the handle first, then lifts.
                    float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / 0.35f));
                    var to = Blend(carry, raised, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_charge * 1.6f)));
                    to.g = Mathf.Lerp(GripCarry, GripEnd, Mathf.Clamp01(_t / 0.22f));
                    // At the top it shakes a little with the strain.
                    to.h += Random.insideUnitSphere * 0.0015f * _charge;
                    _now = Blend(_from, to, w);
                    _now.g = to.g;
                    break;
                }
                case Phase.Swing:
                {
                    float s = Mathf.Clamp01(_t / Mathf.Max(0.01f, _swingTime));
                    s *= s;                                                          // speeding up into the blow
                    var p = Blend(_from, _impact, s);
                    // The hand's path bows up and out over the arc.
                    p.h += (_view * Vector3.up * 0.1f + _aimDir * 0.04f) * (4f * s * (1f - s));
                    _now = p;
                    break;
                }
                case Phase.Recoil:
                {
                    // Springs back off what it hit, more the less it gave.
                    float k = Mathf.Clamp01(_t / 0.16f);
                    float back = (1f - _give) * 14f * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.5f);
                    _now = _impact;
                    _now.r = Quaternion.AngleAxis(-back, _axis) * _impact.r;
                    _now.h = _impact.h - _aimDir * 0.04f * (1f - _give) * Mathf.Sin(k * Mathf.PI);
                    break;
                }
                case Phase.Follow:
                {
                    // On through (a hole, or nothing at all), slowing.
                    float k = Mathf.Clamp01(_t / 0.18f);
                    float on = (_result.hit ? 22f * _give : 55f) * (1f - (1f - k) * (1f - k));
                    _now = _impact;
                    _now.r = Quaternion.AngleAxis(on, _axis) * _impact.r;
                    _now.h = _impact.h + (_aimDir * 0.12f + Vector3.down * 0.1f) * (1f - (1f - k) * (1f - k)) * (_result.hit ? _give : 1f);
                    break;
                }
                case Phase.Recover:
                {
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / 0.5f));
                    _now = Blend(_from, carry, k);
                    _now.g = Mathf.Lerp(_from.g, GripCarry, Mathf.Clamp01((_t - 0.2f) / 0.3f));
                    break;
                }
            }
            rotation = _now.r;
            position = _now.h - rotation * new Vector3(SledgehammerModel.HandleA * 0.95f, _now.g, 0f);
            return 1f;
        }

        // ---------------- Thrown ----------------

        void OnCollisionEnter(Collision c)
        {
            if (Held || Time.time < _quietUntil) return;
            float v = c.relativeVelocity.magnitude;
            if (v < 1.5f) return;
            _quietUntil = Time.time + 0.2f;
            var contact = c.GetContact(0);
            // Thrown hard into a wall: it breaks it as a swing would.
            if (v > 6f && WallBreaker.Instance && Body)
            {
                Vector3 dir = -c.relativeVelocity.normalized;
                float e = Mathf.Clamp01(v / 14f);
                var r = WallBreaker.Instance.Strike(contact.point - dir * 0.25f, dir, 0.45f, BlowFor(e, e), transform);
                if (r.hit) return;
            }
            _fx.pitch = Random.Range(0.85f, 1.05f);
            _fx.PlayOneShot(SoundBank.HardThud(Random.Range(0, 3)), Mathf.Clamp01((v - 1f) / 6f) * 0.6f);
        }
    }
}
