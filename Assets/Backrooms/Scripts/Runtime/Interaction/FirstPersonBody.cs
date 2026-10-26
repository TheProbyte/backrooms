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
        Vector3 _elbowSmoothed;
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

            // Two-bone IK. The elbow points down and out to the arm's own side.
            Vector3 toW = wrist - shoulder;
            float d = Mathf.Clamp(toW.magnitude, 0.05f, (_upperLen + _foreLen) * 0.999f);
            Vector3 dir = toW.normalized;
            Vector3 pole = yaw * new Vector3(0.8f * side, -1f, -0.25f);
            Vector3 poleDir = Vector3.ProjectOnPlane(pole, dir).normalized;
            float a = _upperLen, b = _foreLen;
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            Vector3 elbow = shoulder + dir * (cosA * a) + poleDir * (Mathf.Sqrt(1f - cosA * cosA) * a);
            _elbowSmoothed = _elbowSmoothed == Vector3.zero ? elbow : Vector3.Lerp(_elbowSmoothed, elbow, 1f - Mathf.Exp(-Time.deltaTime / 0.04f));
            elbow = _elbowSmoothed;
            Vector3 wristAt = elbow + (wrist - elbow).normalized * b;          // if out of reach, the hand trails the grip

            Vector3 handBackW = handRot * Vector3.up;
            _arm.position = shoulder;
            _arm.rotation = Quaternion.identity;
            Orient(_upper, _upperAxis, _upperBack, elbow - shoulder, Vector3.Lerp(-poleDir, handBackW, 0.3f));
            Orient(_fore, _foreAxis, _foreBack, wristAt - elbow, handBackW);
            Orient(_hand, _handFingers, _handBack, handRot * Vector3.forward, handBackW);
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
        // The legs are placed, not swung: each foot has a target on the ground from a walk cycle that follows the actual
        // motion in the body's frame (forwards, backwards, sideways, any mix), and a two-bone IK puts the hip, knee and
        // ankle on it with the knee forward. The hips go as low as the eye is (after what bending the torso accounts for),
        // so crouching, stooping and getting up or down bend the knees, and the body is hidden while the eye is too low for
        // a person to be standing under it (crawling, and the start of getting up from it).

        class Leg
        {
            public Transform thigh, shin, foot;
            public float upper, lower, ankle, side;      // lengths, ankle height over the sole, hip offset to the side
            public Vector3 thighAxis, thighFront, shinAxis, shinFront, footFwd, footUp;   // rest axes, local to each bone
            public float phase;                          // 0 or 0.5: half a cycle apart
        }
        Leg _legA, _legB;
        Vector3 _torsoUpAxis;
        float _gait, _air;
        Vector2 _moveLocal;
        const float HideBelowEye = 0.82f, TorsoLength = 0.65f, StanceShare = 0.6f;

        Leg MeasureLeg(Transform thigh, Transform shin, Transform foot, float phase)
        {
            if (!thigh || !shin) return null;
            if (!foot) foot = shin;
            Vector3 fwd = transform.forward, up = transform.up;
            var g = new Leg { thigh = thigh, shin = shin, foot = foot, phase = phase };
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
            _legA = MeasureLeg(_thighL, _shinL, Find(_body, "FootL"), 0f);
            _legB = MeasureLeg(_thighR, _shinR, Find(_body, "FootR"), 0.5f);
        }

        void UpdateBody()
        {
            if (!_body) return;
            float eye = _player.Eye;
            bool visible = !_player.noclip && eye > HideBelowEye;
            if (_body.gameObject.activeSelf != visible) _body.gameObject.SetActive(visible);
            if (!visible) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 right = transform.right, fwd = transform.forward, up = transform.up;

            // Motion in the body's frame (x to the side, y forward), smoothed a little so the cycle does not twitch.
            Vector3 v = _player.PlanarVelocity;
            var local = new Vector2(Vector3.Dot(v, right), Vector3.Dot(v, fwd));
            _moveLocal = Vector2.Lerp(_moveLocal, local, 1f - Mathf.Exp(-dt / 0.1f));
            float speed = _moveLocal.magnitude;
            bool grounded = _player.IsGrounded;
            _air = Mathf.MoveTowards(_air, grounded ? 0f : 1f, dt * 6f);
            // A step gets longer with speed; the cycle (two steps) advances with the distance covered.
            float step = Mathf.Clamp(0.3f + 0.22f * speed, 0.3f, 1.1f);
            _gait = Mathf.Repeat(_gait + speed * dt / (2f * step), 1f);
            _walkAmount = Mathf.MoveTowards(_walkAmount, grounded ? Mathf.Clamp01(speed / 0.5f) : 0f, dt * 4f);
            Vector2 dir = speed > 0.03f ? _moveLocal / speed : Vector2.up;

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
            // Hips: down with the squat (and back, for balance), a bob through each step, a sway over the standing foot.
            float bob = -0.022f * Mathf.Cos(_gait * 4f * Mathf.PI) * _walkAmount;
            float sway = (_legA != null ? Mathf.Sign(_legA.side) : 1f) * 0.012f * Mathf.Cos(2f * Mathf.PI * (_gait - StanceShare * 0.5f)) * _walkAmount;
            _hips.position = _body.position + up * (_hipsHeight - drop + bob) + right * sway - fwd * (squat * 0.12f);
            // Torso: the stoop, a lean into a crouch, a little twist against the stride.
            float lean = bend + Mathf.Clamp(_moveLocal.y, -1f, 3f) * 2.5f;           // forward (a little more walking faster)
            float twist = Mathf.Sin(_gait * 2f * Mathf.PI) * 4f * _walkAmount * Mathf.Clamp(dir.y, -1f, 1f);
            _torso.localRotation = _torsoRest * Quaternion.AngleAxis(lean, _torsoBendAxis) * Quaternion.AngleAxis(twist, _torsoUpAxis);

            foreach (var g in new[] { _legA, _legB })
            {
                if (g == null) continue;
                float p = Mathf.Repeat(_gait + g.phase, 1f);
                float x, lift = 0f, pitch = 0f;
                if (p < StanceShare)
                {
                    // On the ground: the foot stays put while the body goes over it (it moves back in the body's frame);
                    // the heel comes up at the end, ready to push off.
                    x = Mathf.Lerp(0.5f, -0.5f, p / StanceShare);
                    pitch = 22f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StanceShare * 0.65f, StanceShare, p));
                }
                else
                {
                    // In the air: forward to the next step, lifted in the middle, toes up as it reaches out.
                    float sw = (p - StanceShare) / (1f - StanceShare);
                    x = Mathf.Lerp(-0.5f, 0.5f, Mathf.SmoothStep(0f, 1f, sw));
                    lift = Mathf.Sin(sw * Mathf.PI);
                    pitch = Mathf.Lerp(22f, -12f, Mathf.SmoothStep(0f, 1f, sw));
                }
                float k = _walkAmount;
                Vector2 off = dir * (x * step * k);
                off.x *= 0.6f;                                                       // sideways steps are shorter
                float lateral = g.side + off.x;
                if (lateral * Mathf.Sign(g.side) < 0.05f) lateral = Mathf.Sign(g.side) * 0.05f;   // feet never cross
                lateral += Mathf.Sign(g.side) * squat * 0.06f;                                    // knees apart in a squat
                float liftH = Mathf.Lerp(0.07f, 0.13f, Mathf.InverseLerp(1.5f, 3.6f, speed));
                Vector3 hip = g.thigh.position;
                Vector3 sole = _body.position + right * lateral + fwd * (off.y + squat * 0.04f) + up * (lift * liftH * k);
                pitch *= k;
                // In the air (jumping, falling): the feet hang under the hips, knees a little bent.
                Vector3 hang = hip - up * ((g.upper + g.lower) * 0.9f + g.ankle) + fwd * 0.06f;
                sole = Vector3.Lerp(sole, hang, _air);
                Vector3 footFwd = Quaternion.AngleAxis(pitch, right) * fwd;
                // A raised heel lifts the ankle: it turns about the ball of the foot.
                Vector3 ankle = sole + up * g.ankle + up * (Mathf.Sin(Mathf.Max(0f, pitch) * Mathf.Deg2Rad) * 0.11f);
                SolveLeg(g, hip, ankle, fwd, footFwd, up);
            }
        }

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
