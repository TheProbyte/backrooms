using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Simple first-person body (placeholder art from Tools/Blender/make_player.py): the left arm reaches
    /// for whatever <see cref="PropGrabber"/> holds (two-bone IK, palm on the grip, fingers curled), and a
    /// torso and legs you see when looking down (legs swing while walking, torso bends with the stoop).
    /// The right hand is the camcorder, so it is never seen.
    ///
    /// The models are rigid segments with their origins at the joints; bone axes are measured from the
    /// model at start, so the code does not depend on how the FBX axes were converted.
    /// </summary>
    [RequireComponent(typeof(CamcorderPlayerController))]
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
        readonly List<(Transform t, Quaternion rest, Vector3 axis, float weight)> _fingerJoints = new List<(Transform, Quaternion, Vector3, float)>();
        Vector3 _thighAxisL, _thighAxisR;
        Quaternion _thighRestL, _thighRestR, _shinRestL, _shinRestR, _torsoRest;
        Vector3 _legSwingAxisL, _legSwingAxisR, _shinAxisL, _shinAxisR, _torsoBendAxis;
        float _walkPhase, _walkAmount;
        float _hipsHeight = 0.93f;
        Vector3 _elbowSmoothed;
        Renderer[] _armRenderers;

        static readonly Dictionary<string, Color> Colors = new Dictionary<string, Color>
        {
            { "Skin", new Color(0.78f, 0.52f, 0.40f) }, { "WatchStrap", new Color(0.30f, 0.32f, 0.27f) },
            { "WatchCase", new Color(0.75f, 0.74f, 0.70f) }, { "WatchFace", new Color(0.02f, 0.02f, 0.025f) },
            { "Ring", new Color(0.12f, 0.12f, 0.12f) }, { "Shirt", new Color(0.10f, 0.10f, 0.11f) },
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
            var armPrefab = Resources.Load<GameObject>("Player/PlayerArm");
            var bodyPrefab = Resources.Load<GameObject>("Player/PlayerBody");
            if (armPrefab)
            {
                _arm = Instantiate(armPrefab).transform;
                _arm.name = "FirstPersonArm";
                Setup(_arm.gameObject);
                _upper = Find(_arm, "UpperArm"); _fore = Find(_arm, "Forearm"); _hand = Find(_arm, "Hand");
                MeasureArm();
                _armRenderers = _arm.GetComponentsInChildren<Renderer>();
            }
            if (bodyPrefab && showBody)
            {
                _body = Instantiate(bodyPrefab, transform, false).transform;
                _body.name = "FirstPersonBody";
                Setup(_body.gameObject);
                _hips = Find(_body, "Hips"); _torso = Find(_body, "Torso");
                _thighL = Find(_body, "ThighL"); _thighR = Find(_body, "ThighR"); _shinL = Find(_body, "ShinL"); _shinR = Find(_body, "ShinR");
                MeasureBody();
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
                        m.SetFloat("_Smoothness", key == "WatchFace" ? 0.8f : metal ? 0.6f : key == "Skin" ? 0.4f : 0.15f);
                        _mats[key] = m;
                    }
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
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
            foreach (var name in new[] { "Index", "Middle", "Ring", "Pinky", "Thumb" })
                for (int i = 1; i <= 3; i++)
                {
                    var t = Find(_arm, name + i);
                    if (!t) continue;
                    float w = name == "Thumb" ? (i == 1 ? 0.2f : 0.5f) : i == 1 ? 1f : i == 2 ? 1.25f : 0.8f;
                    _fingerJoints.Add((t, t.localRotation, t.InverseTransformDirection(bendW), w));
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
            // Resting: hanging by the side, out of view. Reaching: palm on the grip.
            Vector3 restWrist = shoulder + yaw * new Vector3(-0.06f, -0.52f, 0.12f);
            Quaternion restHand = yaw * Quaternion.LookRotation(Vector3.down * 0.9f + Vector3.forward * 0.3f, Vector3.left);
            Quaternion gripHand = _grab.HandRotation;
            Vector3 gripWrist = _grab.HandPosition - gripHand * new Vector3(_wristToGrip.x, _wristToGrip.y, _wristToGrip.z);
            float s = Mathf.SmoothStep(0f, 1f, blend);
            Vector3 wrist = Vector3.Lerp(restWrist, gripWrist, s);
            Quaternion handRot = Quaternion.Slerp(restHand, gripHand, s);

            // Two-bone IK. The elbow points down and out to the left (a natural left arm).
            Vector3 toW = wrist - shoulder;
            float d = Mathf.Clamp(toW.magnitude, 0.05f, (_upperLen + _foreLen) * 0.999f);
            Vector3 dir = toW.normalized;
            Vector3 pole = yaw * new Vector3(-0.8f, -1f, -0.25f);
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
            foreach (var (t, rest, axis, w) in _fingerJoints)
                t.localRotation = rest * Quaternion.AngleAxis(curl * 62f * w, axis);
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

        void MeasureBody()
        {
            _hipsHeight = Vector3.Dot(_hips.position - _body.position, transform.up);
            _torsoRest = _torso.localRotation;
            _thighRestL = _thighL.localRotation; _thighRestR = _thighR.localRotation;
            _shinRestL = _shinL.localRotation; _shinRestR = _shinR.localRotation;
            // Legs swing about the body's side axis.
            Vector3 side = _body.right;
            _legSwingAxisL = _thighL.InverseTransformDirection(side); _legSwingAxisR = _thighR.InverseTransformDirection(side);
            _shinAxisL = _shinL.InverseTransformDirection(side); _shinAxisR = _shinR.InverseTransformDirection(side);
            _torsoBendAxis = _torso.InverseTransformDirection(side);
        }

        void UpdateBody()
        {
            if (!_body) return;
            bool visible = _player.CurrentStance != CamcorderPlayerController.Stance.Crawl && !_player.noclip;
            if (_body.gameObject.activeSelf != visible) _body.gameObject.SetActive(visible);
            if (!visible) return;
            _body.localPosition = new Vector3(0f, 0f, -bodyBack);

            // Walk cycle from the distance actually covered.
            float speed = _player.PlanarVelocity.magnitude;
            float fwd = Vector3.Dot(_player.PlanarVelocity, transform.forward) >= -0.05f ? 1f : -1f;
            _walkPhase += speed * Time.deltaTime / 0.7f * Mathf.PI * fwd;
            _walkAmount = Mathf.MoveTowards(_walkAmount, Mathf.Clamp01(speed / 1.2f), Time.deltaTime * 3f);
            float swing = Mathf.Sin(_walkPhase) * 26f * _walkAmount;
            bool crouch = _player.CurrentStance == CamcorderPlayerController.Stance.Crouch;
            float squat = crouch ? 1f : Mathf.Clamp01(_player.stoop / 0.8f) * 0.6f;      // knees bend for a deep stoop
            _thighL.localRotation = _thighRestL * Quaternion.AngleAxis(-swing - squat * 70f, _legSwingAxisL);
            _thighR.localRotation = _thighRestR * Quaternion.AngleAxis(swing - squat * 70f, _legSwingAxisR);
            float kneeL = Mathf.Max(0f, Mathf.Sin(_walkPhase + 1.2f)) * 35f * _walkAmount + squat * 100f;
            float kneeR = Mathf.Max(0f, Mathf.Sin(_walkPhase + Mathf.PI + 1.2f)) * 35f * _walkAmount + squat * 100f;
            _shinL.localRotation = _shinRestL * Quaternion.AngleAxis(kneeL, _shinAxisL);
            _shinR.localRotation = _shinRestR * Quaternion.AngleAxis(kneeR, _shinAxisR);
            // Hips drop for a squat; the torso leans into a stoop.
            float hipsY = Mathf.Lerp(_hipsHeight, _hipsHeight - 0.31f, squat);
            _hips.position = _body.position + transform.up * hipsY;
            float bend = Mathf.Clamp(_player.stoop, 0f, 0.8f) * 55f + (crouch ? 15f : 0f);
            _torso.localRotation = _torsoRest * Quaternion.AngleAxis(-bend, _torsoBendAxis);
        }
    }
}
