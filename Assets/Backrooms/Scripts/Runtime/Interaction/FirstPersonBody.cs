using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Simple first-person body (placeholder art from Tools/Blender/make_player.py): the right arm reaches
    /// for whatever <see cref="PropGrabber"/> holds (two-bone IK, palm on the grip, fingers curled), and a
    /// torso and legs you see when looking down (legs swing while walking, torso bends with the stoop).
    /// The left hand is the camcorder, so it is never seen. (PropGrabber.rightHanded off: the old left arm.)
    ///
    /// The models are rigid segments with their origins at the joints; bone axes are measured from the
    /// model at start, so the code does not depend on how the FBX axes were converted.
    /// </summary>
    [RequireComponent(typeof(CamcorderPlayerController))]
    [DefaultExecutionOrder(100)]                // after PropGrabber has placed the hand for this frame
    public partial class FirstPersonBody : MonoBehaviour
    {
        [Tooltip("Body pushed back from the eye so looking down shows chest and feet, not the inside of the torso.")]
        public float bodyBack = 0.16f;
        public bool showBody = true;
        public bool castShadows = true;

        CamcorderPlayerController _player;
        PropGrabber _grab;
        Transform _arm, _upper, _fore, _hand, _body, _hips, _torso, _thighL, _thighR, _shinL, _shinR;
        float _upperLen, _foreLen;
        // Rest-pose axes (local to each segment): along the bone, and the "back" (watch / back of the hand) side.
        Vector3 _upperAxis, _upperBack, _foreAxis, _foreBack, _handFingers, _handBack;
        Vector3 _wristToGrip;                              // grip point in the hand's frame (fingers, back, side)
        readonly List<(Transform t, Quaternion rest, Vector3 axis, float weight, string finger)> _fingerJoints = new List<(Transform, Quaternion, Vector3, float, string)>();
        Vector3 _wristToTip;                               // index fingertip in the hand's frame (side, back, fingers)
        Quaternion _torsoRest;
        Vector3 _torsoBendAxis;
        float _walkAmount;
        float _hipsHeight = 0.93f;

        static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>
        {
            { "Skin", new Color(0.78f, 0.52f, 0.40f) }, { "WatchStrap", new Color(0.30f, 0.32f, 0.27f) },
            { "WatchCase", new Color(0.75f, 0.74f, 0.70f) }, { "WatchFace", new Color(0.02f, 0.02f, 0.025f) },
            { "Ring", new Color(0.12f, 0.12f, 0.12f) }, { "Nail", new Color(0.86f, 0.66f, 0.6f) }, { "Shirt", new Color(0.10f, 0.10f, 0.11f) },
            { "Pants", new Color(0.025f, 0.025f, 0.03f) }, { "Shoes", new Color(0.03f, 0.03f, 0.035f) }, { "Sole", new Color(0.06f, 0.06f, 0.06f) },
            { "Hair", new Color(0.07f, 0.055f, 0.045f) },
        };
        readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();

        void Awake()
        {
            _player = GetComponent<CamcorderPlayerController>();
            _grab = GetComponent<PropGrabber>();
            if (!GetComponent<BodyPresence>()) gameObject.AddComponent<BodyPresence>();
        }

        /// <summary>A foot came down (world position of the sole, the body's speed then): the footstep is heard from it.</summary>
        public event System.Action<Vector3, float> FootLanded;
        /// <summary>Steps taken (for tests).</summary>
        public int StepsLanded { get; private set; }
        /// <summary>The legs are shown and walking (not crawling, flying or hidden): footsteps come from the feet.</summary>
        public bool FeetActive => _body && _body.gameObject.activeSelf && !_player.noclip;
        Vector3 _torsoFwdAxis;

        void Start()
        {
            float side = _grab ? _grab.Side : 1f;
            var armPrefab = Resources.Load<GameObject>(side > 0f ? "Player/PlayerArmRight" : "Player/PlayerArm");
            if (!armPrefab) armPrefab = Resources.Load<GameObject>("Player/PlayerArm");
            var mpfbPrefab = useMakeHumanBody ? Resources.Load<GameObject>("Player/MPFB/PlayerMPFB") : null;
            var bodyPrefab = mpfbPrefab ? mpfbPrefab : Resources.Load<GameObject>("Player/PlayerBody");
            if (armPrefab)
            {
                // The separate arm: the only arm on the old body; on the MakeHuman body, the hand while crawling.
                var arm = Instantiate(armPrefab).transform;
                arm.name = "FirstPersonArm";
                Setup(arm.gameObject, false);
                var up = Find(arm, "UpperArm"); var fo = Find(arm, "Forearm"); var ha = Find(arm, "Hand");
                if (up && fo && ha)
                {
                    _looseArm = MeasureArmRig(arm, null, up, fo, ha, null, true);
                    _looseArm.renderers = arm.GetComponentsInChildren<Renderer>();
                    Generation.PoolLitSwitch.Attach(arm.gameObject);
                }
                else Destroy(arm.gameObject);
            }
            if (bodyPrefab && showBody)
            {
                _isMpfb = bodyPrefab == mpfbPrefab;
                _body = Instantiate(bodyPrefab, transform, false).transform;
                _body.name = "FirstPersonBody";
                Setup(_body.gameObject, _isMpfb);
                _hips = Find(_body, Bone("Hips")); _torso = Find(_body, Bone("Torso"));
                _thighL = Find(_body, Bone("ThighL")); _thighR = Find(_body, Bone("ThighR")); _shinL = Find(_body, Bone("ShinL")); _shinR = Find(_body, Bone("ShinR"));
                MeasureBody();
                if (_isMpfb)
                {
                    // The working arm is the body's own (right for a right-hander).
                    string s = side > 0f ? "r" : "l";
                    var up = Find(_body, "upperarm_" + s); var fo = Find(_body, "lowerarm_" + s); var ha = Find(_body, "hand_" + s);
                    if (up && fo && ha) _bodyArm = MeasureArmRig(_body, Find(_body, "clavicle_" + s), up, fo, ha, s, false);
                }
                Generation.PoolLitSwitch.Attach(_body.gameObject);
            }
            UseArm(_bodyArm ?? _looseArm);
            SetupFull();
        }

        void OnDestroy()
        {
            OnDestroyFull();
            if (_looseArm != null && _looseArm.root) Destroy(_looseArm.root.gameObject);
            foreach (var m in _mats.Values) Destroy(m);
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        /// <summary>
        /// Backrooms/Lit materials (so the light grid lights the body too), one per FBX material name: plain colours on the
        /// old models, the textures on the MakeHuman body (whose head and camcorder arm only cast shadows).
        /// </summary>
        void Setup(GameObject go, bool mpfb)
        {
            var shader = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mpfb) { mats[i] = MpfbMaterial(mats[i], shader); continue; }
                    string key = mats[i] ? mats[i].name.Replace(" (Instance)", "") : "Skin";
                    if (!_mats.TryGetValue(key, out var m))
                    {
                        m = new Material(shader) { name = "FP_" + key };
                        var c = Colors.TryGetValue(key, out var cc) ? cc : Color.gray;
                        m.SetColor("_BaseColor", c);
                        bool metal = key == "WatchCase" || key == "Ring";
                        m.SetFloat("_Metallic", metal ? 0.8f : 0f);
                        m.SetFloat("_Smoothness", key == "WatchFace" ? 0.8f : metal ? 0.6f : key == "Skin" ? 0.4f : key == "Nail" ? 0.55f : 0.15f);
                        ItemModels.AddDetail(m, key);
                        _mats[key] = m;
                    }
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = mpfb && ShadowOnlyPart(r.name) ? ShadowCastingMode.ShadowsOnly
                                    : castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                // Skinned (Tools/Blender/make_player_rig.py): bounds follow the pose, or a reaching arm or a squat
                // would be culled while on screen (its imported bounds fit only the rest pose).
                if (r is SkinnedMeshRenderer sk) { sk.updateWhenOffscreen = true; sk.quality = SkinQuality.Bone4; }
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        // ---------------- Arm ----------------

        void LateUpdate()
        {
            ContactStops = 0;
            UpdateBody();
            UpdateArm();
            UpdateFull();
        }

        void UpdateArm()
        {
            // The body's own arm while the body is shown; the separate arm while crawling (the body is hidden then).
            var armNow = _bodyArm != null && FeetActive ? _bodyArm : _looseArm;
            if (armNow != _curArm) UseArm(armNow);
            if (_curArm == null || !_grab) return;
            float blend = _grab.HandBlend;
            bool loose = _curArm.root != null;
            if (_looseArm != null && _looseArm.renderers != null)
            {
                // The separate arm is always there (the shadow has it) unless the body is not and nothing is held.
                bool showLoose = loose && (blend > 0.001f || FeetActive);
                foreach (var r in _looseArm.renderers) if (r) r.enabled = showLoose;
                if (loose && !showLoose) return;
            }

            Vector3 shoulder = _grab.Shoulder;
            Quaternion yaw = transform.rotation;
            float side = _grab.Side;
            // The body's arm hangs from its own shoulder (the collarbone at rest); reaching, the shoulder goes toward the
            // reach (below).
            Vector3 natural = shoulder;
            if (!loose)
            {
                if (_curArm.clavicle) _curArm.clavicle.localRotation = _curArm.clavicleRest;
                natural = _upper.position;
            }
            // Resting (the Body and IK layers: hanging and swinging, or a hand on the world); reaching (the Override
            // layer, PropGrabber): palm on the grip.
            RestPose(natural, yaw, side, out Vector3 restWrist, out Quaternion restHand, out float restCurl);
            bool poke = _grab.PokePose;
            Quaternion gripHand = poke ? _grab.PokeRotation : _grab.HandRotation;
            Vector3 gripWrist = poke ? _grab.PokeTip - gripHand * _wristToTip
                                     : _grab.HandPosition - gripHand * new Vector3(_wristToGrip.x, _wristToGrip.y, _wristToGrip.z);
            float s = Mathf.SmoothStep(0f, 1f, blend);
            Vector3 wrist = Vector3.Lerp(restWrist, gripWrist, s);
            Quaternion handRot = Quaternion.Slerp(restHand, gripHand, s);

            // A long reach brings the shoulder forward a little (and the arm with it), as a real one does.
            {
                Vector3 toWrist = wrist - shoulder;
                float over = toWrist.magnitude - (_upperLen + _foreLen) * 0.97f;
                if (over > 0f) shoulder += toWrist.normalized * Mathf.Min(over, 0.09f);
            }
            // The body's arm: its collarbone carries the shoulder toward the reach's (as far as a collarbone turns).
            if (!loose) shoulder = CarryShoulder(_curArm.clavicle, _curArm.clavicleRest, _upper, Vector3.Lerp(natural, shoulder, s), 28f);
            // Where the hand touches (palm or fingertip): kept there whatever the wrist ends up doing.
            Vector3 touchOff = poke ? _wristToTip : _wristToGrip;
            Vector3 touch = wrist + handRot * touchOff;

            // Two-bone IK with a skeleton's limits: the elbow may swing anywhere round the shoulder-wrist line, and the
            // swing is chosen so the forearm can turn and the wrist bend to the hand's angle without going past what a
            // forearm and wrist can do, keeping the elbow down and out to the side. Whatever is still past a limit is
            // not done: the hand stays at the most the wrist allows (and the palm stays on its point).
            Vector3 dir0 = (wrist - shoulder).normalized;
            Vector3 pole0 = Vector3.ProjectOnPlane(yaw * new Vector3(0.8f * side, -1f, -0.25f), dir0).normalized;
            float bestSwivel = _swivelSet ? _swivel : 0f, bestScore = float.MaxValue;
            for (int k = -8; k <= 10; k++) Try(k * 15f);
            float centre = bestSwivel;
            for (int k = -2; k <= 2; k++) Try(centre + k * 4f);
            void Try(float sw)
            {
                var p = SolveArm(shoulder, wrist, handRot, pole0, sw, side, yaw);
                float score = p.score + (_swivelSet ? Mathf.Abs(Mathf.DeltaAngle(sw, _swivel)) * 0.008f : 0f);
                if (score < bestScore) { bestScore = score; bestSwivel = sw; }
            }
            _swivel = _swivelSet ? Mathf.LerpAngle(_swivel, bestSwivel, 1f - Mathf.Exp(-Time.deltaTime / 0.08f)) : bestSwivel;
            _swivelSet = true;
            var pose = SolveArm(shoulder, wrist, handRot, pole0, _swivel, side, yaw);
            // The wrist as it is now puts the palm somewhere else: move the wrist so the palm is back on its point.
            pose = SolveArm(shoulder, touch - pose.hand * touchOff, handRot, pole0, _swivel, side, yaw);
            Vector3 elbow = pose.elbow, wristAt = pose.wrist;
            Vector3 poleDir = Vector3.ProjectOnPlane(elbow - shoulder, (wristAt - shoulder).normalized).normalized;

            if (loose) { _arm.position = shoulder; _arm.rotation = Quaternion.identity; }
            Orient(_upper, _upperAxis, _upperBack, elbow - shoulder, Vector3.Lerp(-poleDir, pose.foreBack, 0.3f));
            Orient(_fore, _foreAxis, _foreBack, wristAt - elbow, pose.foreBack);
            Orient(_hand, _handFingers, _handBack, pose.hand * Vector3.forward, pose.hand * Vector3.up);
            // The model's own segment offsets put the hand at the wrist; nudge it onto the solved point.
            _hand.position = wristAt;

            float curl = Mathf.Lerp(restCurl, _grab ? _grab.HandCurl : 0.2f, s);
            float pointing = _grab ? _grab.PokeIndex : 0f;
            if (_fingers == null) GroupFingers();
            bool contact = _grab && _grab.HasContact && pointing < 0.5f && blend > 0.6f;
            float ease = 1f - Mathf.Exp(-Time.deltaTime / 0.05f);
            foreach (var f in _fingers)
            {
                // Pointing: the index finger straight out, the other fingers and the thumb curled in.
                float want = f.name == "Index" ? Mathf.Lerp(curl, -0.05f, pointing) : Mathf.Lerp(curl, f.name == "Thumb" ? 0.6f : 1.2f, pointing);
                // Holding something: no further than the finger can go before it touches it.
                float c = contact ? ContactCurl(f, want) : want;
                f.curl = f.placed ? Mathf.Lerp(f.curl, c, ease) : c;
                f.placed = true;
                Curl(f, f.curl);
            }
        }

        // ---------------- Fingers ----------------
        //
        // Each finger (and the thumb) curls only as far as it can before it meets the surface of what the hand holds
        // (PropGrabber.ContactDistance: a held item's shape, the furniture's colliders, a door's lever, a wall): the curl
        // is swept from straight to the pose's amount and stops at the first touch of a knuckle or the fingertip. So
        // fingers wrap a thin stem tightly, lie on a flat top, and never sink into what they hold.

        class Finger
        {
            public string name;
            public readonly List<(Transform t, Quaternion rest, Vector3 axis, float w)> joints = new List<(Transform, Quaternion, Vector3, float)>();
            public Vector3 tipLocal;          // the fingertip in the last joint's frame
            public float curl;
            public bool placed;
        }
        List<Finger> _fingers;
        const float FingerRadius = 0.0065f;
        public int ContactStops { get; private set; }  // fingers stopped by a surface last frame (for tests)

        void GroupFingers()
        {
            _fingers = new List<Finger>();
            foreach (var (t, rest, axis, w, name) in _fingerJoints)
            {
                var f = _fingers.Find(x => x.name == name);
                if (f == null) { f = new Finger { name = name }; _fingers.Add(f); }
                f.joints.Add((t, rest, axis, w));
            }
            foreach (var f in _fingers)
            {
                foreach (var j in f.joints) j.t.localRotation = j.rest;
                var last = f.joints[f.joints.Count - 1].t;
                var prev = f.joints.Count > 1 ? f.joints[f.joints.Count - 2].t : last.parent;
                Vector3 dir = (last.position - prev.position);
                Vector3 tip = last.position + dir.normalized * (dir.magnitude * 0.9f);
                f.tipLocal = last.InverseTransformPoint(tip);
            }
        }

        static void Curl(Finger f, float c)
        {
            foreach (var j in f.joints) j.t.localRotation = j.rest * Quaternion.AngleAxis(c * 62f * j.w, j.axis);
        }

        /// <summary>The fingertip or the last knuckle touches (the base of the finger lies against what is held anyway).</summary>
        bool Touches(Finger f)
        {
            int n = f.joints.Count;
            var last = f.joints[n - 1].t;
            if (_grab.ContactDistance(last.TransformPoint(f.tipLocal)) < FingerRadius) return true;
            return n > 1 && _grab.ContactDistance(last.position) < FingerRadius;
        }

        /// <summary>How far this finger may curl toward want before it touches what the hand holds.</summary>
        float ContactCurl(Finger f, float want)
        {
            if (want <= 0.02f) return want;
            Curl(f, 0f);
            if (Touches(f)) { ContactStops++; return Mathf.Min(want, 0.12f); }
            const int Steps = 8;
            float ok = 0f;
            for (int s = 1; s <= Steps; s++)
            {
                float c = want * s / Steps;
                Curl(f, c);
                if (Touches(f))
                {
                    // Halfway back between the last free curl and this one.
                    float mid = (ok + c) * 0.5f;
                    Curl(f, mid);
                    if (!Touches(f)) ok = mid;
                    ContactStops++;
                    return ok;
                }
                ok = c;
            }
            return want;
        }

        [Header("Arm limits")]
        [Tooltip("Wrist: furthest bend toward the palm, and back (degrees).")] public float wristFlex = 70f, wristExtend = 60f;
        [Tooltip("Wrist: furthest bend sideways (degrees).")] public float wristSide = 28f;
        [Tooltip("Forearm: furthest turn either way from thumb-up (palm down / palm up), degrees.")] public float forearmTwist = 95f;
        float _swivel;
        bool _swivelSet;

        struct ArmPose { public Vector3 elbow, wrist, foreBack; public Quaternion hand; public float score; }

        /// <summary>
        /// One arm pose for an elbow swing (degrees about the shoulder-wrist line from the default down-and-out): the elbow,
        /// the wrist, the forearm's turn (its back), and the hand within the wrist's limits, with a score (lower is more
        /// natural: little left undone at the limits, a forearm not turned far, the elbow not up or across the chest).
        /// </summary>
        ArmPose SolveArm(Vector3 shoulder, Vector3 wrist, Quaternion hand, Vector3 pole0, float swivel, float side, Quaternion yaw)
        {
            var p = new ArmPose();
            Vector3 toW = wrist - shoulder;
            float a = _upperLen, b = _foreLen;
            float d = Mathf.Clamp(toW.magnitude, 0.05f, (a + b) * 0.999f);
            Vector3 dir = toW.sqrMagnitude > 1e-8f ? toW.normalized : Vector3.down;
            Vector3 pole = Vector3.ProjectOnPlane(Quaternion.AngleAxis(swivel, dir) * pole0, dir).normalized;
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            p.elbow = shoulder + dir * (cosA * a) + pole * (Mathf.Sqrt(1f - cosA * cosA) * a);
            p.wrist = p.elbow + (wrist - p.elbow).normalized * b;          // if out of reach, the hand trails the grip
            Vector3 u = (p.elbow - shoulder).normalized, f = (p.wrist - p.elbow).normalized;
            // The elbow's hinge, and from it the back of the wrist with the thumb up (forearm turned neither way).
            Vector3 h = Vector3.Cross(u, f);
            if (h.sqrMagnitude < 1e-6f) h = Vector3.Cross(pole, dir);
            Vector3 b0 = -side * h.normalized;
            Vector3 F = hand * Vector3.forward, B = hand * Vector3.up;
            Vector3 want = Vector3.ProjectOnPlane(B, f);
            float twist = want.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(b0, want, f) : 0f;
            float tw = Mathf.Clamp(twist, -forearmTwist, forearmTwist);
            p.foreBack = Quaternion.AngleAxis(tw, f) * b0;
            // The wrist: what is left of the hand's angle, as a bend toward the palm or back and a bend to the side.
            var fore = Quaternion.LookRotation(f, p.foreBack);
            Vector3 Fl = Quaternion.Inverse(fore) * F;
            float flex = Mathf.Asin(Mathf.Clamp(-Fl.y, -1f, 1f)) * Mathf.Rad2Deg;     // + toward the palm
            float dev = Mathf.Atan2(Fl.x, Fl.z) * Mathf.Rad2Deg;
            float flexC = Mathf.Clamp(flex, -wristExtend, wristFlex), devC = Mathf.Clamp(dev, -wristSide, wristSide);
            p.hand = fore * Quaternion.Euler(0f, devC, 0f) * Quaternion.Euler(flexC, 0f, 0f);
            float over = Mathf.Abs(twist - tw) + Mathf.Abs(flex - flexC) + Mathf.Abs(dev - devC);
            float s = over * 0.05f + Mathf.Abs(tw) * 0.004f + (Mathf.Abs(flexC) + Mathf.Abs(devC)) * 0.004f;
            Vector3 e = p.elbow - shoulder;
            s += Mathf.Max(0f, e.y + 0.05f) * 6f;                                      // elbow up level with the shoulder
            s += Mathf.Max(0f, 0.04f - Vector3.Dot(e, yaw * Vector3.right * side)) * 8f; // elbow in across the chest
            p.score = s;
            return p;
        }

        /// <summary>Rotates a segment so its bone axis points along <paramref name="along"/> with its back toward <paramref name="back"/>.</summary>
        static void Orient(Transform t, Vector3 localAxis, Vector3 localBack, Vector3 along, Vector3 back)
        {
            if (along.sqrMagnitude < 1e-8f) return;
            Vector3 bk = Vector3.ProjectOnPlane(back, along);
            if (bk.sqrMagnitude < 1e-6f) bk = Vector3.ProjectOnPlane(Vector3.up, along);
            Quaternion want = Quaternion.LookRotation(along, bk);
            Quaternion local = Quaternion.LookRotation(localAxis, Vector3.ProjectOnPlane(localBack, localAxis));
            t.rotation = want * Quaternion.Inverse(local);
        }

        // ---------------- Body ----------------
        //
        // The feet are planted, not cycled: a foot on the ground stays where it is in the world while the body moves over
        // it, and steps (lifted, swung to where it should land) once it is left behind, one foot at a time. Where it lands
        // depends on how the body is moving at that moment, so starting, stopping, turning on the spot and changing from
        // forwards to backwards or sideways all come out of the same rule as a few natural steps, never a snap: stopping
        // finishes the step under way and brings the other foot alongside. A two-bone IK puts the hip, knee and ankle on
        // each foot. The hips go as low as the eye is (after what bending the torso accounts for), so crouching, stooping
        // and getting up or down bend the knees; the body is hidden while the eye is too low for a person to be standing
        // under it (crawling, and the start of getting up from it).

        class Leg
        {
            public Transform thigh, shin, foot;
            public float upper, lower, ankle, side;      // lengths, ankle height over the sole, hip offset to the side
            public float toe = 0.11f;                    // ankle to the ball of the foot, along the foot
            public float heel, heelVel;                  // the heel lifted (degrees) because the leg could not reach the floor
            public float lastPitch, liftPitch;           // the foot's tilt last frame, and when this step left the floor
            public Vector3 landAt;                       // where this step was planned to land
            public Vector3 thighAxis, thighFront, shinAxis, shinFront, footFwd, footUp;   // rest axes, local to each bone
            public Vector3 plant;                        // the sole on the ground (world), while not stepping
            public Vector3 plantFwd;                     // its heading on the ground
            public bool swinging;
            public Vector3 from, fromFwd;                // where the step started
            public float t, duration;                    // step progress 0..1 and its length (s)
            public float landY;                          // the ground height it is stepping to (eased)
            public float reachCap = float.MaxValue;      // how far from where it left it may land (one stair tread at a time)
            public bool placed;
        }
        Leg _legA, _legB;
        Vector3 _torsoUpAxis;
        float _air, _bob, _sway, _twist, _swingUp;
        Vector2 _moveLocal;
        Vector3 _vel;
        const float HideBelowEye = 0.82f, TorsoLength = 0.65f;
        readonly RaycastHit[] _footHits = new RaycastHit[6];

        Leg MeasureLeg(Transform thigh, Transform shin, Transform foot)
        {
            if (!thigh || !shin) return null;
            if (!foot) foot = shin;
            Vector3 fwd = transform.forward, up = transform.up;
            var g = new Leg { thigh = thigh, shin = shin, foot = foot };
            g.upper = Vector3.Distance(thigh.position, shin.position);
            g.lower = foot != shin ? Vector3.Distance(shin.position, foot.position) : 0.42f;
            g.ankle = foot != shin ? Mathf.Clamp(Vector3.Dot(foot.position - _body.position, up), 0.03f, 0.15f) : 0.08f;
            g.side = Vector3.Dot(thigh.position - _body.position, transform.right);
            g.thighAxis = thigh.InverseTransformDirection((shin.position - thigh.position).normalized);
            g.thighFront = thigh.InverseTransformDirection(fwd);
            g.shinAxis = shin.InverseTransformDirection(foot != shin ? (foot.position - shin.position).normalized : -up);
            g.shinFront = shin.InverseTransformDirection(fwd);
            g.footFwd = foot.InverseTransformDirection(fwd);
            g.footUp = foot.InverseTransformDirection(up);
            // The ball of the foot (MakeHuman's toe bone), where the heel turns as it lifts.
            if (foot != shin && foot.childCount > 0)
            {
                float toe = Vector3.Dot(foot.GetChild(0).position - foot.position, fwd);
                if (toe > 0.05f && toe < 0.2f) g.toe = toe;
            }
            return g;
        }

        void MeasureBody()
        {
            _hipsHeight = Vector3.Dot(_hips.position - _body.position, transform.up);
            _hipsRestLocal = Quaternion.Inverse(_body.rotation) * _hips.rotation;
            _torsoRest = _torso.localRotation;
            MeasureSpine();
            _torsoBendAxis = _torso.InverseTransformDirection(transform.right);
            _torsoUpAxis = _torso.InverseTransformDirection(transform.up);
            _torsoFwdAxis = _torso.InverseTransformDirection(transform.forward);
            _legA = MeasureLeg(_thighL, _shinL, Find(_body, Bone("FootL")));
            _legB = MeasureLeg(_thighR, _shinR, Find(_body, Bone("FootR")));
        }

        /// <summary>Where a foot stands when the body is still: under its hip (wider and a little forward in a squat).</summary>
        Vector3 Home(Leg g, float squat)
        {
            float lateral = g.side + Mathf.Sign(g.side) * squat * 0.06f;
            return _body.position + _legRight * lateral + _legFwd * (squat * 0.04f);
        }

        /// <summary>
        /// The floor under a point: a stair tread or a slope within a step of the body's own height (the body's height if
        /// nothing is found). Not furniture, items or the low beams of racks: a foot passing under or beside them would
        /// jump up onto them.
        /// </summary>
        float GroundY(Vector3 p, float fallback)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, fallback + 0.3f, p.z), Vector3.down, _footHits, 0.75f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue, y = fallback;
            for (int k = 0; k < n; k++)
            {
                var h = _footHits[k];
                if (h.collider.transform.IsChildOf(transform) || h.normal.y < 0.6f || h.distance >= best || h.distance < 0.005f) continue;
                if (h.collider.GetComponentInParent<WorldItem>() || h.collider.GetComponentInParent<MovableProp>() || h.collider.name == "PropCollider") continue;
                best = h.distance; y = h.point.y;
            }
            return y;
        }

        Vector3 FootAt(Leg g) => g.foot ? g.foot.position : g.plant;

        /// <summary>Two-bone IK for a leg: hip to ankle with the knee pointing forward, then the foot's own angle.</summary>
        static void SolveLeg(Leg g, Vector3 hip, Vector3 ankleTarget, Vector3 front, Vector3 footFwd, Vector3 up)
        {
            Vector3 d = ankleTarget - hip;
            float a = g.upper, b = g.lower;
            float len = Mathf.Clamp(d.magnitude, Mathf.Abs(a - b) + 0.05f, (a + b) * 0.999f);
            Vector3 dirn = d.sqrMagnitude > 1e-8f ? d.normalized : -up;
            Vector3 pole = Vector3.ProjectOnPlane(front, dirn);
            pole = pole.sqrMagnitude > 1e-6f ? pole.normalized : Vector3.ProjectOnPlane(up, dirn).normalized;
            float cosA = Mathf.Clamp((a * a + len * len - b * b) / (2f * a * len), -1f, 1f);
            Vector3 knee = hip + dirn * (cosA * a) + pole * (Mathf.Sqrt(1f - cosA * cosA) * a);
            Vector3 ankle = hip + dirn * len;
            Orient(g.thigh, g.thighAxis, g.thighFront, knee - hip, pole);
            if (g.shin != g.foot || g.foot == null)
                Orient(g.shin, g.shinAxis, g.shinFront, ankle - knee, Vector3.ProjectOnPlane(front, ankle - knee));
            if (g.foot && g.foot != g.shin)
                Orient(g.foot, g.footFwd, g.footUp, footFwd, Vector3.ProjectOnPlane(up, footFwd));
        }
    }
}
