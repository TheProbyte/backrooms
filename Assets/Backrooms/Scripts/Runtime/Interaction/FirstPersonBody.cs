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
    public class FirstPersonBody : MonoBehaviour
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
        Renderer[] _armRenderers;

        static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>
        {
            { "Skin", new Color(0.78f, 0.52f, 0.40f) }, { "WatchStrap", new Color(0.30f, 0.32f, 0.27f) },
            { "WatchCase", new Color(0.75f, 0.74f, 0.70f) }, { "WatchFace", new Color(0.02f, 0.02f, 0.025f) },
            { "Ring", new Color(0.12f, 0.12f, 0.12f) }, { "Nail", new Color(0.86f, 0.66f, 0.6f) }, { "Shirt", new Color(0.10f, 0.10f, 0.11f) },
            { "Pants", new Color(0.025f, 0.025f, 0.03f) }, { "Shoes", new Color(0.03f, 0.03f, 0.035f) }, { "Sole", new Color(0.06f, 0.06f, 0.06f) },
        };
        readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();

        void Awake()
        {
            _player = GetComponent<CamcorderPlayerController>();
            _grab = GetComponent<PropGrabber>();
        }

        void Start()
        {
            float side = _grab ? _grab.Side : 1f;
            var armPrefab = Resources.Load<GameObject>(side > 0f ? "Player/PlayerArmRight" : "Player/PlayerArm");
            if (!armPrefab) armPrefab = Resources.Load<GameObject>("Player/PlayerArm");
            var bodyPrefab = Resources.Load<GameObject>("Player/PlayerBody");
            if (armPrefab)
            {
                _arm = Instantiate(armPrefab).transform;
                _arm.name = "FirstPersonArm";
                Setup(_arm.gameObject);
                _upper = Find(_arm, "UpperArm"); _fore = Find(_arm, "Forearm"); _hand = Find(_arm, "Hand");
                MeasureArm();
                _armRenderers = _arm.GetComponentsInChildren<Renderer>();
                Generation.PoolLitSwitch.Attach(_arm.gameObject);
            }
            if (bodyPrefab && showBody)
            {
                _body = Instantiate(bodyPrefab, transform, false).transform;
                _body.name = "FirstPersonBody";
                Setup(_body.gameObject);
                _hips = Find(_body, "Hips"); _torso = Find(_body, "Torso");
                _thighL = Find(_body, "ThighL"); _thighR = Find(_body, "ThighR"); _shinL = Find(_body, "ShinL"); _shinR = Find(_body, "ShinR");
                MeasureBody();
                Generation.PoolLitSwitch.Attach(_body.gameObject);
            }
        }

        void OnDestroy()
        {
            if (_arm) Destroy(_arm.gameObject);
            foreach (var m in _mats.Values) Destroy(m);
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        /// <summary>Backrooms/Lit materials (so the light grid lights the body too), one per FBX material name.</summary>
        void Setup(GameObject go)
        {
            var shader = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
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
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                // Skinned (Tools/Blender/make_player_rig.py): bounds follow the pose, or a reaching arm or a squat
                // would be culled while on screen (its imported bounds fit only the rest pose).
                if (r is SkinnedMeshRenderer sk) { sk.updateWhenOffscreen = true; sk.quality = SkinQuality.Bone4; }
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        // ---------------- Arm ----------------

        void MeasureArm()
        {
            // Rest pose straight out of the model: axes from child positions.
            _upperLen = Vector3.Distance(_upper.position, _fore.position);
            _foreLen = Vector3.Distance(_fore.position, _hand.position);
            Vector3 boneW = (_fore.position - _upper.position).normalized;
            var watch = Find(_arm, "WatchCase");
            Vector3 backW = watch ? Vector3.ProjectOnPlane(watch.position - _fore.position, boneW).normalized : Vector3.up;
            _upperAxis = _upper.InverseTransformDirection(boneW);
            _upperBack = _upper.InverseTransformDirection(backW);
            _foreAxis = _fore.InverseTransformDirection((_hand.position - _fore.position).normalized);
            _foreBack = _fore.InverseTransformDirection(backW);
            var middle = Find(_arm, "Middle1");
            Vector3 fingersW = middle ? (middle.position - _hand.position).normalized : boneW;
            fingersW = Vector3.ProjectOnPlane(fingersW, backW).normalized;
            _handFingers = _hand.InverseTransformDirection(fingersW);
            _handBack = _hand.InverseTransformDirection(backW);
            // Where the hand touches what it holds: under the knuckles, on the palm side.
            float palmLen = middle ? Vector3.Dot(middle.position - _hand.position, fingersW) : 0.1f;
            _wristToGrip = new Vector3(0f, -0.018f, palmLen * 0.78f);     // (side, back, fingers)

            var ix2 = Find(_arm, "Index2"); var ix3 = Find(_arm, "Index3");
            if (ix2 && ix3)
            {
                Vector3 tipW = ix3.position + (ix3.position - ix2.position).normalized * (Vector3.Distance(ix2.position, ix3.position) * 0.9f);
                Vector3 d = tipW - _hand.position, sideRest = Vector3.Cross(backW, fingersW).normalized;
                _wristToTip = new Vector3(Vector3.Dot(d, sideRest), Vector3.Dot(d, backW), Vector3.Dot(d, fingersW));
            }

            // Thumb rest pose: out to the side, forward, and down toward the palm (the exported model has it
            // pointing up and out). Set here so it does not depend on the FBX axis conversion.
            Vector3 sideW = Vector3.Cross(backW, fingersW).normalized;
            var thumb1 = Find(_arm, "Thumb1"); var thumb2 = Find(_arm, "Thumb2");
            if (thumb1 && thumb2)
            {
                if (Vector3.Dot(sideW, thumb1.position - _hand.position) < 0f) sideW = -sideW;      // toward the thumb
                Vector3 now = (thumb2.position - thumb1.position).normalized;
                Vector3 want = (sideW * 0.45f + fingersW * 0.75f - backW * 0.5f).normalized;
                thumb1.rotation = Quaternion.FromToRotation(now, want) * thumb1.rotation;
            }
            // Finger joints bend toward the palm. (Checked numerically: rotating about back x fingers moves
            // the fingertips to the palm side.)
            Vector3 bendW = Vector3.Cross(backW, fingersW).normalized;
            // The thumb folds across the palm (toward the index finger's base and the palm side), not about the
            // fingers' axis: about that it would swing back toward the wrist in a fist.
            Vector3 thumbBendW = bendW;
            if (thumb1 && thumb2)
            {
                Vector3 thumbDir = (thumb2.position - thumb1.position).normalized;
                Vector3 across = (-sideW * 0.75f - backW * 0.55f + fingersW * 0.25f).normalized;
                Vector3 ax = Vector3.Cross(thumbDir, across);
                if (ax.sqrMagnitude > 1e-6f) thumbBendW = ax.normalized;
            }
            foreach (var name in new[] { "Index", "Middle", "Ring", "Pinky", "Thumb" })
                for (int i = 1; i <= 3; i++)
                {
                    var t = Find(_arm, name + i);
                    if (!t) continue;
                    float w = name == "Thumb" ? (i == 1 ? 0.2f : 0.5f) : i == 1 ? 1f : i == 2 ? 1.25f : 0.8f;
                    _fingerJoints.Add((t, t.localRotation, t.InverseTransformDirection(name == "Thumb" ? thumbBendW : bendW), w, name));
                }
        }

        void LateUpdate()
        {
            UpdateArm();
            UpdateBody();
        }

        void UpdateArm()
        {
            if (!_arm) return;
            float blend = _grab ? _grab.HandBlend : 0f;
            bool show = blend > 0.001f;
            foreach (var r in _armRenderers) r.enabled = show;
            if (!show) return;

            Vector3 shoulder = _grab.Shoulder;
            Quaternion yaw = transform.rotation;
            float side = _grab.Side;
            // Resting: hanging by the side, out of view. Reaching: palm on the grip.
            Vector3 restWrist = shoulder + yaw * new Vector3(0.06f * side, -0.52f, 0.12f);
            Quaternion restHand = yaw * Quaternion.LookRotation(Vector3.down * 0.9f + Vector3.forward * 0.3f, Vector3.right * side);
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

            _arm.position = shoulder;
            _arm.rotation = Quaternion.identity;
            Orient(_upper, _upperAxis, _upperBack, elbow - shoulder, Vector3.Lerp(-poleDir, pose.foreBack, 0.3f));
            Orient(_fore, _foreAxis, _foreBack, wristAt - elbow, pose.foreBack);
            Orient(_hand, _handFingers, _handBack, pose.hand * Vector3.forward, pose.hand * Vector3.up);
            // The model's own segment offsets put the hand at the wrist; nudge it onto the solved point.
            _hand.position = wristAt;

            float curl = _grab ? _grab.HandCurl : 0.2f;
            float pointing = _grab ? _grab.PokeIndex : 0f;
            foreach (var (t, rest, axis, w, finger) in _fingerJoints)
            {
                // Pointing: the index finger straight out, the other fingers and the thumb curled in.
                float c = finger == "Index" ? Mathf.Lerp(curl, -0.05f, pointing) : Mathf.Lerp(curl, finger == "Thumb" ? 0.6f : 1.2f, pointing);
                t.localRotation = rest * Quaternion.AngleAxis(c * 62f * w, axis);
            }
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
            public Vector3 thighAxis, thighFront, shinAxis, shinFront, footFwd, footUp;   // rest axes, local to each bone
            public Vector3 plant;                        // the sole on the ground (world), while not stepping
            public Vector3 plantFwd;                     // its heading on the ground
            public bool swinging;
            public Vector3 from, fromFwd;                // where the step started
            public float t, duration;                    // step progress 0..1 and its length (s)
            public float landY;                          // the ground height it is stepping to (eased)
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
            return g;
        }

        void MeasureBody()
        {
            _hipsHeight = Vector3.Dot(_hips.position - _body.position, transform.up);
            _torsoRest = _torso.localRotation;
            _torsoBendAxis = _torso.InverseTransformDirection(transform.right);
            _torsoUpAxis = _torso.InverseTransformDirection(transform.up);
            _legA = MeasureLeg(_thighL, _shinL, Find(_body, "FootL"));
            _legB = MeasureLeg(_thighR, _shinR, Find(_body, "FootR"));
        }

        /// <summary>Where a foot stands when the body is still: under its hip (wider and a little forward in a squat).</summary>
        Vector3 Home(Leg g, float squat)
        {
            float lateral = g.side + Mathf.Sign(g.side) * squat * 0.06f;
            return _body.position + transform.right * lateral + transform.forward * (squat * 0.04f);
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

        void UpdateBody()
        {
            if (!_body) return;
            float eye = _player.Eye;
            bool visible = !_player.noclip && eye > HideBelowEye;
            if (_body.gameObject.activeSelf != visible)
            {
                _body.gameObject.SetActive(visible);
                if (_legA != null) _legA.placed = false;
                if (_legB != null) _legB.placed = false;
            }
            if (!visible) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 right = transform.right, fwd = transform.forward, up = transform.up;

            // The body's motion, smoothed a little (a step is planned from it).
            Vector3 v = _player.PlanarVelocity; v.y = 0f;
            _vel = Vector3.Lerp(_vel, v, 1f - Mathf.Exp(-dt / 0.08f));
            _moveLocal = new Vector2(Vector3.Dot(_vel, right), Vector3.Dot(_vel, fwd));
            float speed = _vel.magnitude;
            bool grounded = _player.IsGrounded;
            _air = Mathf.MoveTowards(_air, grounded ? 0f : 1f, dt * 6f);
            _walkAmount = Mathf.MoveTowards(_walkAmount, grounded ? Mathf.Clamp01(speed / 0.5f) : 0f, dt * 3f);

            // How far the eye is below standing: leaning the torso forward gives some (stooping to reach, and a lean that
            // comes with crouching), the knees the rest.
            float low = Mathf.Clamp01((_player.standEye - eye) / Mathf.Max(0.1f, _player.standEye - _player.crouchEye));
            float bend = Mathf.Clamp(_player.stoop, 0f, 0.8f) * 55f + low * 30f;
            float torsoDrop = TorsoLength * (1f - Mathf.Cos(Mathf.Min(bend, 80f) * Mathf.Deg2Rad));
            float drop = Mathf.Clamp(_player.standEye - eye - torsoDrop, 0f, 0.5f);
            float squat = drop / 0.5f;
            // The eye stays where it is: as the torso leans forward, the body goes back under it (or the shoulders and
            // neck would come up in front of the lens).
            _body.localPosition = new Vector3(0f, 0f, -bodyBack - TorsoLength * 0.9f * Mathf.Sin(Mathf.Min(bend, 80f) * Mathf.Deg2Rad));

            // Steps: longer with speed, sideways ones shorter; the time a foot spends on the ground shrinks toward a run.
            float sideways = speed > 0.05f ? Mathf.Abs(_moveLocal.x) / speed : 0f;
            float step = Mathf.Clamp(0.3f + 0.22f * speed, 0.3f, 1.1f) * Mathf.Lerp(1f, 0.6f, sideways);
            float run = Mathf.InverseLerp(1.6f, 3.6f, speed);
            float stanceShare = Mathf.Lerp(0.62f, 0.38f, run);
            float cycle = speed > 0.05f ? 2f * step / speed : 1f;                       // two steps
            float swingTime = Mathf.Clamp((1f - stanceShare) * cycle, 0.2f, 0.42f);
            float stanceTime = speed > 0.05f ? stanceShare * cycle : 0f;
            // A foot lands this far ahead of the hip: half of what the body covers while it stands, fading out (never
            // snapping) as the body slows to a stop.
            float reachOut = Mathf.Min(stanceShare * step, 0.42f) * Mathf.SmoothStep(0f, 1f, speed / 0.7f);
            Vector3 vdir = speed > 0.05f ? _vel / speed : fwd;

            foreach (var g in new[] { _legA, _legB })
            {
                if (g == null) continue;
                var home = Home(g, squat);
                if (!g.placed || (g.plant - home).sqrMagnitude > 2.5f * 2.5f)
                {
                    // Just shown (or far off after a teleport): standing under the hip.
                    g.plant = home; g.plant.y = GroundY(home, _body.position.y);
                    g.plantFwd = fwd; g.swinging = false; g.placed = true;
                }
            }

            // Which foot steps next: one that is left behind (or out of place once stopped), and only while the other is
            // on the ground (at a run, once the other is most of the way through its own step).
            Leg next = null; float worst = 0f;
            foreach (var g in new[] { _legA, _legB })
            {
                if (g == null || g.swinging) continue;
                var other = g == _legA ? _legB : _legA;
                if (other != null && other.swinging && other.t < Mathf.Lerp(1f, 0.55f, run)) continue;
                Vector3 off = g.plant - Home(g, squat); off.y = 0f;
                float behind = -Vector3.Dot(off, vdir);
                float need;
                if (speed > 0.08f) need = Mathf.Max(behind - reachOut * 0.9f, off.magnitude - (reachOut + 0.16f));
                else need = off.magnitude - 0.07f;
                // Turned on the spot: the foot is twisted under the body.
                need = Mathf.Max(need, (Vector3.Angle(g.plantFwd, fwd) - 35f) * 0.004f);
                if (!grounded) need = -1f;
                if (need > worst) { worst = need; next = g; }
            }
            if (next != null)
            {
                // A long step (catching up after a start or a turn) takes a little longer: a foot is not flung.
                Vector3 land = Home(next, squat) + _vel * swingTime + vdir * reachOut;
                float dist = Vector3.Distance(new Vector3(next.plant.x, 0f, next.plant.z), new Vector3(land.x, 0f, land.z));
                float maxFoot = Mathf.Lerp(4.5f, 9f, run);                            // peak foot speed, m/s
                next.swinging = true; next.t = 0f; next.duration = Mathf.Clamp(Mathf.Max(swingTime, dist * 1.5f / maxFoot), 0.2f, 0.55f);
                next.from = next.plant; next.fromFwd = next.plantFwd;
            }

            // Hips: down with the squat (and back, for balance); up as a leg passes under the body mid-step, down with
            // both feet on the ground; over the standing foot.
            float bobWant = (_swingUp - 0.45f) * 0.03f * _walkAmount;
            _bob = Mathf.Lerp(_bob, bobWant, 1f - Mathf.Exp(-dt / 0.06f));
            float swayWant = 0f;
            if (_legA != null && _legB != null)
            {
                if (_legA.swinging && !_legB.swinging) swayWant = Mathf.Sign(_legB.side);
                else if (_legB.swinging && !_legA.swinging) swayWant = Mathf.Sign(_legA.side);
            }
            _sway = Mathf.Lerp(_sway, swayWant * 0.014f * _walkAmount, 1f - Mathf.Exp(-dt / 0.12f));
            // A little twist of the torso against the stride: the shoulders turn away from the leading foot.
            float lead = 0f;
            if (_legA != null && _legB != null)
                lead = Vector3.Dot(FootAt(_legA) - FootAt(_legB), fwd) * Mathf.Sign(_legA.side);
            _twist = Mathf.Lerp(_twist, Mathf.Clamp(lead * 9f, -5f, 5f), 1f - Mathf.Exp(-dt / 0.1f));
            _hips.position = _body.position + up * (_hipsHeight - drop + _bob) + right * _sway - fwd * (squat * 0.12f);
            // Torso: the stoop, a lean into a crouch, a lean into the walk.
            float lean = bend + Mathf.Clamp(_moveLocal.y, -1f, 3f) * 2.5f;
            _torso.localRotation = _torsoRest * Quaternion.AngleAxis(lean, _torsoBendAxis) * Quaternion.AngleAxis(_twist, _torsoUpAxis);

            float liftH = Mathf.Lerp(0.06f, 0.13f, run) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(speed / 0.8f));
            float swingUp = 0f;
            foreach (var g in new[] { _legA, _legB })
            {
                if (g == null) continue;
                Vector3 hip = g.thigh.position;
                Vector3 sole, footFwd;
                float pitch = 0f;
                if (g.swinging)
                {
                    g.t = Mathf.Min(1f, g.t + dt / Mathf.Max(0.05f, g.duration));
                    // Where it lands, from how the body moves now: ahead of the hip by half a stance, and on its own side.
                    float left = (1f - g.t) * g.duration;
                    Vector3 target = Home(g, squat) + _vel * left + vdir * reachOut;
                    Vector3 rel = target - _body.position;
                    float lat = Vector3.Dot(rel, right);
                    if (lat * Mathf.Sign(g.side) < 0.06f) target += right * (Mathf.Sign(g.side) * 0.06f - lat);   // feet never cross
                    // The ground where it lands, eased (a target sliding over a tread edge must not jerk the foot).
                    float groundY = GroundY(target, _body.position.y);
                    g.landY = g.t <= dt / Mathf.Max(0.05f, g.duration) + 1e-4f ? groundY : Mathf.MoveTowards(g.landY, groundY, dt * 1.5f);
                    target.y = g.landY;
                    float s = Mathf.SmoothStep(0f, 1f, g.t);
                    sole = Vector3.Lerp(g.from, target, s);
                    float arc = Mathf.Sin(g.t * Mathf.PI);
                    sole += up * (arc * (liftH + Mathf.Max(0f, Mathf.Max(g.from.y, target.y) - Mathf.Min(g.from.y, target.y))));
                    footFwd = Vector3.Slerp(g.fromFwd, fwd, s);
                    // Toes leave last, heel lands first.
                    pitch = Mathf.Lerp(18f, -10f, Mathf.SmoothStep(0f, 1f, g.t)) * Mathf.Clamp01(speed / 0.6f);
                    swingUp = Mathf.Max(swingUp, arc);
                    if (g.t >= 1f) { g.swinging = false; g.plant = target; g.plantFwd = fwd; }
                }
                else
                {
                    sole = g.plant;
                    footFwd = g.plantFwd;
                    // The heel comes up as the body goes on past the foot, ready to push off.
                    Vector3 off = g.plant - Home(g, squat); off.y = 0f;
                    float behind = -Vector3.Dot(off, vdir);
                    pitch = 22f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reachOut * 0.3f, reachOut * 0.95f + 0.02f, behind)) * Mathf.Clamp01(speed / 0.3f);
                }
                // In the air (jumping, falling): the feet hang under the hips, knees a little bent.
                Vector3 hang = hip - up * ((g.upper + g.lower) * 0.9f + g.ankle) + fwd * 0.06f;
                sole = Vector3.Lerp(sole, hang, _air);
                if (_air > 0.5f) { g.plant = sole; g.swinging = false; g.placed = _air < 0.99f; }
                Vector3 flatFwd = Vector3.ProjectOnPlane(footFwd, up).normalized;
                if (flatFwd.sqrMagnitude < 0.5f) flatFwd = fwd;
                Vector3 footRight = Vector3.Cross(up, flatFwd);
                Vector3 pitched = Quaternion.AngleAxis(pitch, footRight) * flatFwd;
                // A raised heel lifts the ankle: it turns about the ball of the foot.
                Vector3 ankle = sole + up * g.ankle + up * (Mathf.Sin(Mathf.Max(0f, pitch) * Mathf.Deg2Rad) * 0.11f);
                // The knee points between where the body faces and where the foot does.
                SolveLeg(g, hip, ankle, (fwd + flatFwd).normalized, pitched, up);
            }
            _swingUp = swingUp;

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
