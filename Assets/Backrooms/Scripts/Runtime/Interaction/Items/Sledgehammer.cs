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
    public class Sledgehammer : WorldItem, IHandPose
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
        public override Vector3 GripLocal => GripAt(_now.g > 0f ? _now.g : GripCarry);
        /// <summary>The palm's point on the handle, g along it (on the handle's face).</summary>
        static Vector3 GripAt(float g) => new Vector3(SledgehammerModel.HandleA * SledgehammerModel.HandleScale(g) * 0.95f, g, 0f);
        static readonly Quaternion GripTurn = Quaternion.LookRotation(Vector3.forward, Vector3.right)
                                              * Quaternion.AngleAxis(18f, Vector3.down);
        // Fingers round the handle toward the striking face (+z), the back of the hand outward (+x); the handle across the
        // palm on the slant a hand holds a handle at (from the heel of the hand to the base of the index finger, the head
        // toward the thumb), 18 degrees round from square across it, turned about the palm's normal only. (At 26.5 about a
        // tilted axis, the handle rose 6 mm into the heel of the hand and lay under the index finger's knuckle: the finger
        // lay straight across it.)
        public override Quaternion GripRotationLocal => GripTurn;
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
            _phase = Phase.Carry; _charge = 0f; _viewInit = false; _poseValid = false; _carryRollSet = false; _solveT = -1f;
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
            _impact = Solve(_aimPoint, _aimDir, out _axis, out _reachable, true);
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

        // The body's real shoulder and the arm's reach (the hammer was swung up to 0.77 m from the shoulder by an arm that
        // reaches 0.61: it left the hand for 0.4 s each swing).
        Vector3 Shoulder => _hand ? _hand.ReachShoulder : transform.position;
        float Reach => _hand ? Mathf.Min(armReach, _hand.ArmReach * 0.97f) : armReach;

        /// <summary>
        /// The handle as the model is (oval, 13.5 mm across and 17.5 in the swing, swelling to a knob at the butt; a round
        /// 13.5 mm from 2 cm up, the fingers closed 4 mm into its sides and the little finger missed the knob) and the head:
        /// what the fingers close on.
        /// </summary>
        public override float ContactDistance(Vector3 p)
        {
            float y = Mathf.Clamp(p.y, 0f, 0.38f), k = SledgehammerModel.HandleScale(y);
            float A = SledgehammerModel.HandleA * k, B = SledgehammerModel.HandleB * k;
            float handle;
            {
                // An oval's distance (Quilez's approximation), then along the handle past its ends.
                float k0 = Mathf.Sqrt(p.x * p.x / (A * A) + p.z * p.z / (B * B));
                float k1 = Mathf.Sqrt(p.x * p.x / (A * A * A * A) + p.z * p.z / (B * B * B * B));
                float across = k1 > 1e-6f ? k0 * (k0 - 1f) / k1 : -A;
                float along = Mathf.Abs(p.y - y);
                handle = along > 0f && across > 0f ? Mathf.Sqrt(across * across + along * along) : Mathf.Max(across, along > 0f ? along : across);
            }
            return Mathf.Min(handle, BoxDistance(p, new Vector3(0f, 0.4f, 0f), new Vector3(0.0245f, 0.0275f, 0.06f)));
        }

        // ---------------- Planning what the arm can do ----------------
        //
        // Every pose the hammer is put in is one the arm can hold it in: the hand's turn on the handle is fixed by the grip,
        // so the hammer's own turn is chosen (FirstPersonBody.ArmStrain) so that the wrist and forearm stay inside what they
        // do. (The blow was planned from the hammer alone: the hand had to turn 94 degrees past the wrist's limits, so the
        // head swept sideways, or with the item's turn capped the handle ended 74 degrees off the palm; carried, the forearm
        // was turned palm-up 104 degrees, past the 95 a forearm turns.)

        FirstPersonBody _arm;
        FirstPersonBody Arm => _arm ? _arm : (_hand ? _arm = _hand.GetComponent<FirstPersonBody>() : null);

        /// <summary>The hand's turn holding the hammer turned so (the grip's, mirrored for a left hand).</summary>
        Quaternion HandOf(Quaternion r)
        {
            Quaternion g = GripRotationLocal;
            Vector3 f = g * Vector3.forward, u = g * Vector3.up;
            if (_hand && _hand.Side < 0f) { f.x = -f.x; u.x = -u.x; }
            return r * Quaternion.LookRotation(f, u);
        }

        /// <summary>The arm's strain holding the hammer so (0 without a body to ask); over: degrees the hand would miss by.</summary>
        float Strain(in Pose p, out float over)
        {
            over = 0f;
            var arm = Arm;
            return arm && arm.HasArm ? arm.ArmStrain(p.h, HandOf(p.r), out over) : 0f;
        }

        Vector3 _solveX, _solveF, _solveAxis; float _solveT = -1f; bool _solveOk; Pose _solved;

        /// <summary>
        /// The pose that puts the face of the head on a point, moving along a direction: the handle across the blow (hand
        /// below the head), tipped toward the wall (up to 60 degrees) and leaned about the blow's line (up to 45 either way)
        /// as the arm can best hold it, the hand within reach and the arm well out (the elbow straightens into a blow; it
        /// stayed bent 62 degrees), the wind-up over the shoulder it comes from also within the wrist's limits.
        /// axis: what the swing turns about. ok: within reach. (Planned for the aim a few times a second, and afresh for the
        /// blow.)
        /// </summary>
        Pose Solve(Vector3 x, Vector3 f, out Vector3 axis, out bool ok, bool fresh = false)
        {
            f.Normalize();
            // (A plan costs about half a millisecond: kept while the aim stays put, and made at most ten times a second.)
            if (!fresh && _solveT >= 0f && ((Time.time - _solveT < 0.25f && (x - _solveX).sqrMagnitude < 0.02f * 0.02f && Vector3.Angle(f, _solveF) < 2f)
                                            || Time.time - _solveT < 0.1f))
            {
                axis = _solveAxis; ok = _solveOk; return _solved;
            }
            Vector3 up = _view * Vector3.up, right = _view * Vector3.right;
            Vector3 u0 = Vector3.ProjectOnPlane(up * 0.85f - right * 0.35f, f).normalized;
            if (u0.sqrMagnitude < 0.5f) u0 = Vector3.ProjectOnPlane(-right, f).normalized;
            Vector3 s = Shoulder;
            float reach = Reach;
            Pose best = default; ok = false; axis = Vector3.Cross(u0, f).normalized;
            float bestScore = float.MaxValue;
            for (int lean = -2; lean <= 2; lean++)
            {
                Vector3 ul = Quaternion.AngleAxis(lean * 20f, f) * u0;
                for (int k = 0; k <= 6; k++)
                {
                    float a = k * 10f * Mathf.Deg2Rad;
                    Vector3 d = ul * Mathf.Cos(a) + f * Mathf.Sin(a);
                    Vector3 face = f * Mathf.Cos(a) - ul * Mathf.Sin(a);
                    Vector3 centre = x - face * HeadHalf;
                    Vector3 h = centre - d * (HeadY - GripEnd);
                    var p = new Pose { h = h, r = Quaternion.LookRotation(face, d), g = GripEnd };
                    Vector3 ax = Vector3.Cross(d, face).normalized;
                    float dist = (h - s).magnitude;
                    // Out of reach (swinging at the air, or a wall a step away): the hand as far as it goes, any pose in
                    // reach first.
                    bool inReach = dist <= reach;
                    if (!inReach) p.h = s + (h - s) * (reach / dist);
                    float score = Strain(p, out float over) + over * 0.05f;
                    score += inReach ? Mathf.Max(0f, reach * 0.92f - dist) * 3f               // the arm out through the blow
                                     : 20f + (dist - reach) * 20f;
                    score += Mathf.Abs(lean) * 0.04f + k * 0.008f;                       // nearest the plain blow among equals
                    if (score >= bestScore) continue;
                    // The wind-up it is swung from (fully wound) within the wrist's limits too.
                    score += Strain(Raised(p, ax, 1f), out float overUp) * 0.5f + overUp * 0.03f;
                    if (score < bestScore) { bestScore = score; best = p; axis = ax; ok = inReach; }
                }
            }
            _solveX = x; _solveF = f; _solveT = Time.time; _solveAxis = axis; _solveOk = ok; _solved = best;
            return best;
        }

        float _carryRoll, _carryTilt; bool _carryRollSet;

        /// <summary>
        /// Carried choked up, the head forward and up at the lower right of the view; turned about its handle, and its head
        /// tipped back or forward a little, as the arm best holds it (searched when picked up, then kept up to date a step at a
        /// time as the view tips). (Turned about the handle only, the wrist was bent 29 degrees toward the little finger, past
        /// its 28.)
        /// </summary>
        Pose Carry()
        {
            Vector3 h = _viewPos + _view * new Vector3(0.24f, -0.37f, 0.42f);
            Vector3 dBase = new Vector3(-0.15f, 0.55f, 0.82f).normalized;
            if (Arm && Arm.HasArm)
            {
                float bestRoll = _carryRoll, bestTilt = _carryTilt, bestS = float.MaxValue;
                if (!_carryRollSet)
                {
                    for (int t = -2; t <= 1; t++)
                        for (int k = -12; k <= 12; k++) Try(k * 10f, t * 10f);
                }
                else
                {
                    Try(_carryRoll, _carryTilt);
                    bestS -= 0.005f;                                                         // (no dithering between near-equals)
                    Try(_carryRoll - 4f, _carryTilt); Try(_carryRoll + 4f, _carryTilt);
                    if (_carryTilt > -20f) Try(_carryRoll, _carryTilt - 4f);
                    if (_carryTilt < 10f) Try(_carryRoll, _carryTilt + 4f);
                }
                _carryRoll = bestRoll; _carryTilt = bestTilt; _carryRollSet = true;

                void Try(float roll, float tilt)
                {
                    var p = CarryPose(h, dBase, roll, tilt);
                    float sc = Strain(p, out float over) + over * 0.05f + Mathf.Abs(roll) * 0.0015f + Mathf.Abs(tilt) * 0.002f;
                    if (sc < bestS) { bestS = sc; bestRoll = roll; bestTilt = tilt; }
                }
            }
            return CarryPose(h, dBase, _carryRoll, _carryTilt);
        }

        /// <summary>The carry with the handle turned roll about itself and its head tipped tilt (about the view's right; back: less).</summary>
        Pose CarryPose(Vector3 h, Vector3 dBase, float roll, float tilt)
        {
            Vector3 d = _view * (Quaternion.AngleAxis(tilt, Vector3.right) * dBase);
            Vector3 face0 = Vector3.ProjectOnPlane(_view * new Vector3(-0.55f, -0.1f, 1f), d).normalized;
            Vector3 face = Quaternion.AngleAxis(roll, d) * face0;
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

        float _wristEase, _wristWant, _wristAt;
        /// <summary>The pose turned about the back of the hand as the arm holds it best (picked 12 times a second), times k.</summary>
        Pose EaseWrist(Pose p, float dt, float k)
        {
            if (Time.time >= _wristAt && k > 0.99f)
            {
                _wristAt = Time.time + 0.08f;
                float bestS = float.MaxValue;
                for (int c = -16; c <= 16; c += 8)
                {
                    var t = new Pose { h = p.h, r = Quaternion.AngleAxis(c, HandOf(p.r) * Vector3.up) * p.r, g = p.g };
                    float s = Strain(t, out float over) + over * 0.02f + Mathf.Abs(c) * 0.003f;
                    if (s < bestS) { bestS = s; _wristWant = c; }
                }
            }
            _wristEase = Mathf.MoveTowards(_wristEase, _wristWant, dt * 120f);
            p.r = Quaternion.AngleAxis(_wristEase * k, HandOf(p.r) * Vector3.up) * p.r;
            return p;
        }

        /// <summary>
        /// The thumb keeps the carry's wrap over the index and middle fingers whatever the hammer does (round 7: in the
        /// wind-up and the swing it stood out straight, 8 mm off). The fingers close on the handle as usual.
        /// </summary>
        public bool FingerPose(int finger, float[] angles)
        {
            if (finger != 4) return false;
            angles[0] = 9f; angles[1] = 27f; angles[2] = 31f; angles[3] = 57f;
            return true;
        }

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
                    // On through (a hole, or nothing at all), slowing: the whole arm goes on round the shoulder with the
                    // hammer (it went on turning about the hand, 55 degrees past what a wrist bends: the handle left the palm).
                    float k = Mathf.Clamp01(_t / 0.18f);
                    float on = (_result.hit ? 22f * _give : 50f) * (1f - (1f - k) * (1f - k));
                    var q = Quaternion.AngleAxis(on, _axis);
                    Vector3 sh = Shoulder;
                    _now = _impact;
                    _now.r = q * _impact.r;
                    _now.h = sh + q * (_impact.h - sh);
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
            // Wound up and following through, the hammer is turned a little about the back of the hand (up to 16 degrees)
            // as the wrist holds it best: the wind-up had the wrist 27 degrees to the thumb's side, the follow-through
            // 22-27 to the little finger's. Back to none by the blow (it would move the head off the aim).
            if (_phase == Phase.WindUp || _phase == Phase.Follow) _now = EaseWrist(_now, dt, 1f);
            else if (_phase == Phase.Swing) _now = EaseWrist(_now, dt, 1f - Mathf.Clamp01(_t / Mathf.Max(0.01f, _swingTime)));
            else _wristEase = Mathf.MoveTowards(_wristEase, 0f, dt * 120f);
            // Whatever the phase, the hand is where an arm can be: the hammer goes with the hand.
            {
                Vector3 s = Shoulder;
                _now.h = s + Vector3.ClampMagnitude(_now.h - s, Reach);
            }
            rotation = _now.r;
            position = _now.h - rotation * GripAt(_now.g);
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
