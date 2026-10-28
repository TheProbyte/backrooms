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
                if (mpfb && WorkingArmPart(r.name)) _workArm.Add(r);
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

        /// <summary>The working arm's shoulder joint as the body stands this frame (its collarbone at rest).</summary>
        public Vector3 ShoulderNow { get; private set; }
        /// <summary>
        /// How far the working hand's palm reaches from <see cref="ShoulderNow"/> (m): the rig's own arm nearly straight, the
        /// palm, and what the collarbone adds. PropGrabber reaches, steps in and lets go by this (it used a fixed 0.7 m, and
        /// the arm is 0.61 m: things were taken from a distance).
        /// </summary>
        public float ArmReach { get; private set; } = 0.6f;
        /// <summary>
        /// How far the solved palm (or fingertip) is from where PropGrabber wants it (m), with the arm all the way out; 1
        /// otherwise. PropGrabber takes hold only once it is there.
        /// </summary>
        public float PalmError { get; private set; } = 1f;
        /// <summary>The body has an arm to measure (otherwise PropGrabber falls back on its own numbers).</summary>
        public bool HasArm => _curArm != null;

        System.Func<Vector3, float> _grabContact, _touchContact;

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
                if (loose && !showLoose) { PalmError = 1f; return; }
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
                shoulder = natural;
            }
            ShoulderNow = natural;
            ArmReach = (_upperLen + _foreLen) * 0.96f + _wristToGrip.magnitude + (loose ? 0f : 0.04f);
            // Resting (the Body and IK layers: hanging and swinging, or a hand on the world); reaching (the Override
            // layer, PropGrabber): palm on the grip.
            RestPose(natural, yaw, side, out Vector3 restWrist, out Quaternion restHand, out float restCurl);
            bool poke = _grab.PokePose;
            // First person: the working arm is drawn while it does something; hanging at the side, only its shadow.
            if (!loose) SetWorkingArmShown(blend > 0.001f || poke || _touching || _touchW > 0.004f);
            Quaternion gripHand = poke ? _grab.PokeRotation : _grab.HandRotation;
            Vector3 gripWrist = poke ? _grab.PokeTip - gripHand * _wristToTip
                                     : _grab.HandPosition - gripHand * new Vector3(_wristToGrip.x, _wristToGrip.y, _wristToGrip.z);
            float s = Mathf.SmoothStep(0f, 1f, blend);
            Vector3 wrist = Vector3.Lerp(restWrist, gripWrist, s);
            Quaternion handRot = Quaternion.Slerp(restHand, gripHand, s);
            Vector3 touchOff = poke ? _wristToTip : _wristToGrip;

            // What the fingers (and palm) close on: what PropGrabber holds or touches, or the wall or rail the body's own
            // touch layer rests the hand on (it had no contact at all: fingers went into the wall).
            if (_grabContact == null) { _grabContact = _grab.ContactDistance; _touchContact = TouchDistance; }
            System.Func<Vector3, float> contact = null;
            if (_grab.HasContact && blend > 0.05f) contact = _grabContact;
            else if (_touchW > 0.05f) contact = _touchContact;

            // A long reach brings the shoulder forward a little (and the arm with it), as a real one does.
            {
                Vector3 toWrist = wrist - shoulder;
                float over = toWrist.magnitude - (_upperLen + _foreLen) * 0.97f;
                if (over > 0f) shoulder += toWrist.normalized * Mathf.Min(over, 0.09f);
            }
            // The body's arm: its collarbone carries the shoulder toward the reach's (as far as a collarbone turns).
            if (!loose) shoulder = CarryShoulder(_curArm.clavicle, _curArm.clavicleRest, _upper, Vector3.Lerp(natural, shoulder, s), 28f);
            // Where the hand touches (palm or fingertip): kept there whatever the wrist ends up doing.
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
                // Staying near the elbow's swing now is worth a little (no flipping between near-equal ways), at most 0.2:
                // a clearly better way round is taken (it stuck on one 20 degrees off the hand's angle, the elbow up).
                float score = p.score + (_swivelSet ? Mathf.Min(Mathf.Abs(Mathf.DeltaAngle(sw, _swivel)) * 0.008f, 0.2f) : 0f);
                if (score < bestScore) { bestScore = score; bestSwivel = sw; }
            }
            _swivel = _swivelSet ? Mathf.LerpAngle(_swivel, bestSwivel, 1f - Mathf.Exp(-Time.deltaTime / 0.08f)) : bestSwivel;
            _swivelSet = true;
            var pose = SolveArm(shoulder, wrist, handRot, pole0, _swivel, side, yaw);
            // The wrist as it is now puts the palm somewhere else: move the wrist so the palm is back on its point.
            pose = SolveArm(shoulder, touch - pose.hand * touchOff, handRot, pole0, _swivel, side, yaw);

            if (loose) { _arm.position = shoulder; _arm.rotation = Quaternion.identity; }
            PoseArm(_upper, _upperAxis, _upperBack, _fore, _foreAxis, _foreBack, _hand, _handFingers, _handBack, shoulder, pose, forearmTwistShare);
            // A hand on a wall the wrist cannot lay quite flat touches with the edge of the hand (the knuckles at the
            // surface) instead of pushing that edge into the wall (a jamb's touch, 20 degrees off flat: the little finger's
            // knuckle 2 cm in).
            if (contact != null && !poke && !_grab.HeldItem && _fingers != null && (_grab.TouchingWall || (_touchW > 0.5f && blend < 0.05f)))
            {
                float worst = 0f; Vector3 at = Vector3.zero;
                foreach (var f in _fingers)
                {
                    if (f.name == "Thumb" || f.joints.Count == 0) continue;
                    Vector3 k = f.joints[0].t.position;
                    float d = contact(k) - FingerRadius;
                    if (d < worst) { worst = d; at = k; }
                    // The finger as straight as it goes (straight, or bent back as far as it bends): if even that is in
                    // the wall, the hand comes out that far (a jamb's fingertips were 11.7 mm in).
                    int nj = f.joints.Count;
                    for (int ji = 0; ji < nj; ji++) _closeSaved[ji] = f.a[ji];
                    float bestF = float.MinValue;
                    for (int ee = 0; ee <= 1; ee++)
                    {
                        for (int ji = 0; ji < nj; ji++) f.a[ji] = ee == 0 ? 0f : -Extend[Mathf.Min(ji, Extend.Length - 1)];
                        Pose(f);
                        bestF = Mathf.Max(bestF, ClearanceFrom(f, 0, contact));
                    }
                    for (int ji = 0; ji < nj; ji++) f.a[ji] = _closeSaved[ji];
                    Pose(f);
                    if (bestF < worst) { worst = bestF; at = k; }
                }
                Vector3 n = worst < -0.002f ? ContactNormal(contact, at) : Vector3.zero;
                if (n.sqrMagnitude > 0.25f)
                {
                    pose = SolveArm(shoulder, pose.wrist + n.normalized * Mathf.Min(-worst, 0.05f), handRot, pole0, _swivel, side, yaw);
                    PoseArm(_upper, _upperAxis, _upperBack, _fore, _foreAxis, _foreBack, _hand, _handFingers, _handBack, shoulder, pose, forearmTwistShare);
                }
            }
            Vector3 wanted = poke ? _grab.PokeTip : _grab.HandPosition;
            Vector3 palmNow = pose.wrist + pose.hand * touchOff;
            PalmError = blend > 0.97f ? Vector3.Distance(palmNow, wanted) : 1f;
            // A held item goes with the hand: where the arm cannot quite put the palm on its grip (at full reach, a wrist
            // at its limit), the item is turned and moved onto the palm instead of floating off it (a sledgehammer's
            // swing left the hand by up to 186 mm, then 46).
            if (!poke && !loose && _grab.HeldItem && blend > 0.97f && !_grab.ItemWaitingForHand)
            {
                var held = _grab.HeldItem;
                var it = held.transform;
                Quaternion turn = pose.hand * Quaternion.Inverse(gripHand);
                it.SetPositionAndRotation(palmNow + turn * (it.position - wanted), turn * it.rotation);
                // No part of the palm in it: the item is pushed out along its surface where the palm (its middle, heel and
                // the base of the fingers, either side) is inside (a sledgehammer's handle sat 10.6 mm in the palm, a tape
                // roll 7.2).
                Vector3 hf = pose.hand * Vector3.forward, hr = pose.hand * Vector3.right, hu = pose.hand * Vector3.up;
                float worstIn = 0f; Vector3 deepAt = palmNow;
                for (int a = -1; a <= 1; a++)
                    for (int b = -1; b <= 1; b += 2)
                    {
                        // (Either side the palm bulges: the ball of the thumb and the little finger's side, 5 mm proud.)
                        Vector3 q = palmNow + hf * (a * 0.035f) + hr * (b * 0.03f) - hu * 0.005f;
                        float d = held.ContactDistanceWorld(q);
                        if (d < worstIn) { worstIn = d; deepAt = q; }
                    }
                if (worstIn < -0.002f)
                {
                    Vector3 nOut = ContactNormal(held.ContactDistanceWorld, deepAt);
                    if (nOut.sqrMagnitude > 0.25f) it.position -= nOut.normalized * Mathf.Min(-worstIn - 0.001f, 0.02f);
                }
            }

            // Fingers: one curl for all, or a held item's own shape (IHandGrip: a pinch, a pencil grip), the thumb flexing
            // and turning across the palm toward the fingers (opposition).
            float curl = Mathf.Lerp(restCurl, _grab.HandCurl, s);
            var shape = blend > 0.01f ? _grab.HeldItem as IHandGrip : null;
            float pointing = _grab.PokeIndex;
            if (_fingers == null) GroupFingers();
            bool closeOn = contact != null && pointing < 0.5f && (blend > 0.6f || (_touchW > 0.5f && blend < 0.05f));
            float ease = 1f - Mathf.Exp(-Time.deltaTime / 0.05f);
            foreach (var f in _fingers)
            {
                bool thumb = f.name == "Thumb";
                float want = shape != null ? Mathf.Lerp(restCurl, thumb ? shape.ThumbCurl : shape.FingerCurl(FingerIndex(f.name)), s) : curl;
                float opp = thumb ? (shape != null ? Mathf.Lerp(0f, shape.ThumbOpposition, s) : Mathf.Clamp01((curl - 0.2f) * 1.1f)) : 0f;
                // Pointing: the index finger straight out, the other fingers and the thumb curled in.
                if (f.name == "Index") want = Mathf.Lerp(want, -0.05f, pointing);
                else if (thumb) { want = Mathf.Lerp(want, 0.9f, pointing); opp = Mathf.Lerp(opp, 0.85f, pointing); }   // tucked over the curled fingers (it stuck down: a finger gun)
                else want = Mathf.Lerp(want, 1.2f, pointing);
                // Hand over hand on a cord: the fingers open as the hand goes back out.
                if (!thumb && _grab.HeldItem) want *= 1f - 0.6f * _grab.GripOpen;
                // Drawn in toward the middle finger as it curls (most of the way by half a fist).
                f.adduct = thumb ? 0f : -f.spread * Mathf.Clamp01(want * 1.6f) * 0.85f;
                // A held item's fingers straighten back no more than 10 degrees (30 at the knuckle bent the little finger
                // straight back on a lamp's base and a tape roll).
                _extScale = _grab.HeldItem ? 0.34f : 1f;
                // Holding or touching something: each joint no further than it can go before what is beyond it touches.
                Close(f, want, opp, closeOn ? contact : null, _goal, out float goalOpp);
                float sum = 0f, of = 0f;
                for (int i = 0; i < f.joints.Count; i++)
                {
                    f.a[i] = f.placed ? Mathf.Lerp(f.a[i], _goal[i], ease) : _goal[i];
                    sum += f.a[i]; of += want * 62f * f.joints[i].w;
                }
                f.oppDeg = f.placed ? Mathf.Lerp(f.oppDeg, goalOpp, ease) : goalOpp;
                f.curl = of > 1e-3f ? want * sum / of : want;
                f.opp = opp;
                f.placed = true;
                Pose(f);
            }
        }

        static int FingerIndex(string name) => name == "Index" ? 0 : name == "Middle" ? 1 : name == "Ring" ? 2 : name == "Pinky" ? 3 : 4;

        /// <summary>The way out of a surface at p (the gradient of the distance), or zero where it is not defined.</summary>
        static Vector3 ContactNormal(System.Func<Vector3, float> distance, Vector3 p)
        {
            const float H = 0.003f;
            return new Vector3(distance(p + Vector3.right * H) - distance(p - Vector3.right * H),
                               distance(p + Vector3.up * H) - distance(p - Vector3.up * H),
                               distance(p + Vector3.forward * H) - distance(p - Vector3.forward * H)) / (2f * H);
        }

        /// <summary>
        /// The upper arm, forearm and hand to a solved pose. The upper arm's back is the elbow's hinge (its rest back, the
        /// back of the hand in MakeHuman's rest pose, lies along the hinge); it used to be put in the bend plane, rolling
        /// the upper arm 60-70 degrees off the hinge and kinking the sleeve at the elbow. The forearm bone takes
        /// twistShare of the forearm's turn, the hand all of it.
        /// </summary>
        static void PoseArm(Transform upper, Vector3 upperAxis, Vector3 upperBack, Transform fore, Vector3 foreAxis, Vector3 foreBackL,
                            Transform hand, Vector3 handFingers, Vector3 handBack, Vector3 shoulder, in ArmPose pose, float twistShare)
        {
            Vector3 f = (pose.wrist - pose.elbow).normalized;
            Vector3 foreBack = Quaternion.AngleAxis(pose.twist * twistShare, f) * pose.back0;
            Orient(upper, upperAxis, upperBack, pose.elbow - shoulder, pose.back0);
            Orient(fore, foreAxis, foreBackL, pose.wrist - pose.elbow, foreBack);
            Orient(hand, handFingers, handBack, pose.hand * Vector3.forward, pose.hand * Vector3.up);
            // The model's own segment offsets put the hand at the wrist; nudge it onto the solved point.
            hand.position = pose.wrist;
        }

        /// <summary>
        /// For tests: a body's arm in its rest pose, put through the solver and the posing (to its own rest wrist and hand
        /// angle, the elbow swung where it is at rest): the largest angle (degrees) any of its bones ends up from its rest
        /// rotation. Should be about zero: the solver and the rig agree on what "unturned" is.
        /// </summary>
        public static float RestPoseError(Transform body, string side = "r")
        {
            var up = Find(body, "upperarm_" + side); var fo = Find(body, "lowerarm_" + side); var ha = Find(body, "hand_" + side);
            if (!up || !fo || !ha) return float.NaN;
            var r = MeasureArmRig(body, Find(body, "clavicle_" + side), up, fo, ha, side, false);
            Quaternion ru = up.rotation, rf = fo.rotation, rh = ha.rotation;
            Vector3 S = up.position, E = fo.position, W = ha.position;
            Quaternion handRot = Quaternion.LookRotation(ha.TransformDirection(r.handFingers), ha.TransformDirection(r.handBack));
            Vector3 dir = (W - S).normalized;
            Vector3 pole = Vector3.ProjectOnPlane(E - S, dir).normalized;
            float sideSign = side == "l" ? -1f : 1f;
            var lim = new ArmLimits { flex = 70f, extend = 60f, side = 28f, twist = 95f };
            var pose = SolveArmCore(r.upperLen, r.foreLen, S, W, handRot, pole, 0f, sideSign, Quaternion.identity, lim);
            PoseArm(up, r.upperAxis, r.upperBack, fo, r.foreAxis, r.foreBack, ha, r.handFingers, r.handBack, S, pose, 0.9f);
            float err = Mathf.Max(Quaternion.Angle(ru, up.rotation), Mathf.Max(Quaternion.Angle(rf, fo.rotation), Quaternion.Angle(rh, ha.rotation)));
            up.rotation = ru; fo.rotation = rf; ha.rotation = rh;
            return err;
        }

        // ---------------- Fingers ----------------
        //
        // Each finger (and the thumb) closes only as far as it can before it meets the surface of what the hand holds
        // (PropGrabber.ContactDistance: a held item's shape, the furniture's colliders, a door's lever, a wall; or the wall
        // or rail the body's touch layer rests the hand on): the curl is swept from straight to the pose's amount and stops
        // at the first touch of a phalanx (each of its joints and between them, a finger's thickness round). So fingers
        // wrap a thin stem tightly, lie on a flat top, and never sink into what they hold. The thumb also turns across the
        // palm toward the fingers (opposition), swept with its curl.

        class Finger
        {
            public string name;
            public readonly List<(Transform t, Quaternion rest, Vector3 axis, float w)> joints = new List<(Transform, Quaternion, Vector3, float)>();
            public Vector3 tipLocal;          // the fingertip in the last joint's frame
            public Vector3 oppAxis;           // the thumb: its base turns about this toward the palm (local to the first joint)
            public float curl, opp;           // how far it is closed (for tests), and the thumb's wanted opposition
            public readonly float[] a = new float[4];      // each joint's bend now (degrees)
            public float oppDeg;              // the thumb's base turned across the palm now (degrees; negative: opened out)
            public Vector3 abdAxis;           // the knuckle turns about this (local) to fan the finger in or out
            public float spread, adduct;      // its fan out from the middle finger at rest, and how far it is drawn in now (degrees)
            public bool placed;
        }
        readonly float[] _goal = new float[4];
        /// <summary>The thumb opened out (degrees) before it closes round something big (MakeHuman's thumb at rest already points
        /// across the palm: round a spray can it started inside it).</summary>
        const float ThumbOpen = -50f;
        /// <summary>How far a finger's joints straighten back past straight (degrees, knuckle out), to lie flat on a surface.</summary>
        static readonly float[] Extend = { 30f, 8f, 6f };
        List<Finger> _fingers;
        /// <summary>Half a finger's thickness (MakeHuman's fingers are about 17 mm across).</summary>
        const float FingerRadius = 0.0085f;
        /// <summary>How far the thumb's base turns across the palm at full opposition (degrees).</summary>
        const float ThumbOppDeg = 50f;
        public int ContactStops { get; private set; }  // fingers stopped by a surface last frame (for tests)
        Vector3 _thumbOppAxis;

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
                if (f.name == "Thumb") f.oppAxis = _thumbOppAxis;
            }
            // How far each finger fans out from the middle one at rest: it is drawn in toward it as it curls (fingers close
            // together round a handle; the rig's fan left gaps between them on a can, a lamp's rod, a lever).
            var mid = _fingers.Find(x => x.name == "Middle");
            Vector3 N = _hand ? _hand.TransformDirection(_handBack).normalized : Vector3.up;
            if (mid != null && mid.joints.Count > 1 && N.sqrMagnitude > 0.5f)
            {
                Vector3 dm = Vector3.ProjectOnPlane(mid.joints[1].t.position - mid.joints[0].t.position, N);
                foreach (var f in _fingers)
                {
                    if (f == mid || f.name == "Thumb" || f.joints.Count < 2) continue;
                    Vector3 df = Vector3.ProjectOnPlane(f.joints[1].t.position - f.joints[0].t.position, N);
                    f.spread = Vector3.SignedAngle(dm, df, N);
                    f.abdAxis = f.joints[0].t.InverseTransformDirection(N);
                }
            }
        }

        static void Pose(Finger f)
        {
            for (int i = 0; i < f.joints.Count; i++)
            {
                var j = f.joints[i];
                Quaternion turn = i == 0 && f.oppDeg != 0f && f.oppAxis.sqrMagnitude > 0.5f ? Quaternion.AngleAxis(f.oppDeg, f.oppAxis) : Quaternion.identity;
                if (i == 0 && f.adduct != 0f && f.abdAxis.sqrMagnitude > 0.5f) turn *= Quaternion.AngleAxis(f.adduct, f.abdAxis);
                j.t.localRotation = j.rest * turn * Quaternion.AngleAxis(f.a[i], j.axis);
            }
        }

        /// <summary>
        /// Where a finger's joints go (goal, degrees; the thumb's opposition in goalOpp): curled by want (and opposed by opp),
        /// or with something to close on, joint by joint from the knuckle out, each bending until what lies beyond it touches
        /// (so a finger whose first bone meets a can goes on wrapping it with the next two, as real fingers do; one curl for
        /// the whole finger stopped them all at the first touch, the tips a centimetre and a half off). The thumb starts
        /// opened out and closes from there. A finger already inside at the start takes whatever leaves it least inside.
        /// </summary>
        void Close(Finger f, float want, float opp, System.Func<Vector3, float> contact, float[] goal, out float goalOpp)
        {
            int n = f.joints.Count;
            bool thumb = f.oppAxis.sqrMagnitude > 0.5f;
            float oppT = opp * ThumbOppDeg;
            for (int i = 0; i < n; i++) goal[i] = want * 62f * f.joints[i].w;
            goalOpp = thumb ? oppT : 0f;
            if (contact == null) return;
            // (What the finger is now is put back at the end: the caller eases from it to the goal. Left at the last way
            // tried, a finger already inside eased from a full curl every frame and stayed 2-3 cm in a wall.)
            float savedOpp = f.oppDeg;
            for (int i = 0; i < n; i++) { _closeSaved[i] = f.a[i]; f.a[i] = 0f; }
            float open = thumb ? Mathf.Min(ThumbOpen, oppT) : 0f;
            f.oppDeg = open;
            Pose(f);
            if (ClearanceFrom(f, 0, contact) < 0f)
            {
                // Inside already. A finger may straighten back (past straight at the knuckle, as fingers laid flat on a
                // wall do: the rig's rest fingers are a little bent, and with the palm flat on a wall they went 3 cm into
                // it), or curl part way: of the ways that are clear, the one nearest the wanted curl; if none is, the
                // least inside.
                ContactStops++;
                float bestK = 0f, bestC = float.MinValue;
                bool clear = false;
                for (int s = thumb ? 0 : -4; s <= 4; s++)
                {
                    float k = s / 4f;
                    for (int i = 0; i < n; i++) f.a[i] = k >= 0f ? goal[i] * k : k * _extScale * Extend[Mathf.Min(i, Extend.Length - 1)];
                    f.oppDeg = Mathf.Lerp(open, goalOpp, Mathf.Max(k, 0f));
                    Pose(f);
                    float c = ClearanceFrom(f, 0, contact);
                    if (c >= 0f) { clear = true; bestK = k; bestC = c; }
                    else if (!clear && c > bestC + 0.0005f) { bestC = c; bestK = k; }
                }
                for (int i = 0; i < n; i++) goal[i] = bestK >= 0f ? goal[i] * bestK : bestK * _extScale * Extend[Mathf.Min(i, Extend.Length - 1)];
                goalOpp = Mathf.Lerp(open, goalOpp, Mathf.Max(bestK, 0f));
                f.oppDeg = savedOpp;
                for (int i = 0; i < n; i++) f.a[i] = _closeSaved[i];
                return;
            }
            if (thumb && CloseThumb(f, want, oppT, open, contact, goal, out goalOpp))
            {
                f.oppDeg = savedOpp;
                for (int i = 0; i < n; i++) f.a[i] = _closeSaved[i];
                return;
            }
            const int Steps = 8;
            float oppAt = open;
            for (int i = 0; i < n; i++)
            {
                float target = goal[i], done = 0f;
                for (int s = 1; s <= Steps; s++)
                {
                    float k = (float)s / Steps;
                    f.a[i] = target * k;
                    if (i == 0 && thumb) f.oppDeg = Mathf.Lerp(open, goalOpp, k);
                    Pose(f);
                    if (ClearanceFrom(f, i, contact) < 0f)
                    {
                        ContactStops++;
                        float half = (done + k) * 0.5f;
                        f.a[i] = target * half;
                        if (i == 0 && thumb) f.oppDeg = Mathf.Lerp(open, goalOpp, half);
                        Pose(f);
                        if (ClearanceFrom(f, i, contact) >= 0f) done = half;
                        break;
                    }
                    done = k;
                }
                f.a[i] = target * done;
                if (i == 0 && thumb) { oppAt = Mathf.Lerp(open, goalOpp, done); f.oppDeg = oppAt; }
                Pose(f);
                goal[i] = f.a[i];
            }
            goalOpp = thumb ? oppAt : 0f;
            f.oppDeg = savedOpp;
            for (int i = 0; i < n; i++) f.a[i] = _closeSaved[i];
        }
        readonly float[] _closeSaved = new float[4];
        readonly float[] _thumbTry = new float[4];
        float _extScale = 1f;

        /// <summary>
        /// The thumb round what the hand holds: for turns across the palm from opened out to well across, each joint bends in
        /// turn until what lies beyond it touches (further than the wanted curl if need be), and the turn whose tip ends
        /// nearest the surface is taken, the nearest the grip's own turn (<see cref="IHandGrip.ThumbOpposition"/>) among
        /// equals. (Opened out and swept back with the curl, it stopped at its first touch: tips 12-23 mm off a can, a
        /// chicken's neck, a stick of chalk; straight along a hammer's handle and a lamp's rod.) False if it starts inside
        /// whatever the turn.
        /// </summary>
        bool CloseThumb(Finger f, float want, float oppT, float open, System.Func<Vector3, float> contact, float[] goal, out float goalOpp)
        {
            int n = f.joints.Count;
            goalOpp = oppT;
            float bestScore = float.MaxValue;
            bool found = false;
            float far = Mathf.Max(oppT, 25f) + 20f;
            const int Steps = 8;
            for (int c = 0; c <= 6; c++)
            {
                float o = Mathf.Lerp(open, far, c / 6f);
                f.oppDeg = o;
                for (int i = 0; i < n; i++) f.a[i] = 0f;
                Pose(f);
                if (ClearanceFrom(f, 0, contact) < 0f) continue;
                for (int i = 0; i < n; i++)
                {
                    float target = Mathf.Max(want, 1f) * 62f * f.joints[i].w * 1.3f, done = 0f;
                    for (int s = 1; s <= Steps; s++)
                    {
                        float k = (float)s / Steps;
                        f.a[i] = target * k;
                        Pose(f);
                        if (ClearanceFrom(f, i, contact) < 0f)
                        {
                            float half = (done + k) * 0.5f;
                            f.a[i] = target * half;
                            Pose(f);
                            if (ClearanceFrom(f, i, contact) >= 0f) done = half;
                            break;
                        }
                        done = k;
                    }
                    f.a[i] = target * done;
                    Pose(f);
                }
                var last = f.joints[n - 1].t;
                float gap = contact(last.TransformPoint(f.tipLocal)) - FingerRadius;
                // Nearest the surface (in mm), then nearest the grip's turn (10 degrees count as 2 mm).
                float score = Mathf.Max(gap, 0f) + Mathf.Abs(o - oppT) * 0.0002f;
                if (score < bestScore)
                {
                    bestScore = score; found = true; goalOpp = o;
                    for (int i = 0; i < n; i++) _thumbTry[i] = f.a[i];
                }
            }
            if (!found) return false;
            for (int i = 0; i < n; i++) goal[i] = _thumbTry[i];
            return true;
        }

        /// <summary>
        /// The clearance of the part of a finger beyond joint 'from' (m; negative inside): over its phalanges (each joint
        /// and the middle between them) and the tip, the surface distance less a finger's half thickness. Not the thumb's
        /// first bone (the ball of the thumb lies on what the palm holds).
        /// </summary>
        static float ClearanceFrom(Finger f, int from, System.Func<Vector3, float> contact)
        {
            int n = f.joints.Count;
            bool thumb = f.oppAxis.sqrMagnitude > 0.5f;
            float best = float.MaxValue;
            for (int i = from; i < n; i++)
            {
                Vector3 a = f.joints[i].t.position;
                Vector3 b = i + 1 < n ? f.joints[i + 1].t.position : f.joints[n - 1].t.TransformPoint(f.tipLocal);
                if (!(thumb && i == 0)) best = Mathf.Min(best, contact((a + b) * 0.5f));
                best = Mathf.Min(best, contact(b));
            }
            return best - FingerRadius;
        }

        [Header("Arm limits")]
        [Tooltip("Wrist: furthest bend toward the palm, and back (degrees).")] public float wristFlex = 70f, wristExtend = 60f;
        [Tooltip("Wrist: furthest bend sideways (degrees).")] public float wristSide = 28f;
        [Tooltip("Forearm: furthest turn either way from thumb-up (palm down / palm up), degrees.")] public float forearmTwist = 95f;
        float _swivel;
        bool _swivelSet;

        [Tooltip("How much of the forearm's turn (palm up or down) its bone takes; the hand takes all of it. The rig has no " +
                 "twist bones: the rest shows at the wrist, where it twists the skin (most of it goes to the forearm, whose " +
                 "elbow end is under the sleeve).")]
        [Range(0f, 1f)] public float forearmTwistShare = 0.9f;

        struct ArmPose { public Vector3 elbow, wrist, foreBack, back0; public Quaternion hand; public float score, twist; }
        struct ArmLimits { public float flex, extend, side, twist; }

        /// <summary>
        /// Of the ways a palm can lie flat on a face at <paramref name="point"/> (turned round the face's normal), the
        /// fingers' direction the arm reaches with the least strain (the solver's score: wrist and forearm inside their
        /// limits, the elbow down), leaning a little toward <paramref name="prefer"/>. (Fingers along the forearm's line,
        /// tipped up, on a door jamb's face needed the forearm turned further than it turns: the hand stayed 34 degrees
        /// off flat and the little-finger side went 3 cm into the wall.)
        /// </summary>
        public Vector3 ReachableFingers(Vector3 point, Vector3 normal, Vector3 prefer)
        {
            if (_curArm == null || !_grab) return prefer;
            Vector3 basis = Vector3.ProjectOnPlane(prefer, normal);
            if (basis.sqrMagnitude < 1e-4f) basis = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (basis.sqrMagnitude < 1e-4f) return prefer;
            basis.Normalize();
            Vector3 shoulder = ShoulderNow;
            Quaternion yaw = transform.rotation;
            float side = _grab.Side;
            Vector3 best = basis; float bestScore = float.MaxValue;
            for (int a = -180; a < 180; a += 15)
            {
                Vector3 f = Quaternion.AngleAxis(a, normal) * basis;
                Quaternion hand = Quaternion.LookRotation(f, normal);
                Vector3 wrist = point - hand * _wristToGrip;
                Vector3 dir0 = (wrist - shoulder).normalized;
                Vector3 pole0 = Vector3.ProjectOnPlane(yaw * new Vector3(0.8f * side, -1f, -0.25f), dir0).normalized;
                float s = float.MaxValue;
                for (int k = -8; k <= 10; k++) s = Mathf.Min(s, SolveArm(shoulder, wrist, hand, pole0, k * 15f, side, yaw).score);
                s += Mathf.Abs(a) / 180f * 0.3f;
                if (s < bestScore) { bestScore = s; best = f; }
            }
            return best;
        }

        ArmPose SolveArm(Vector3 shoulder, Vector3 wrist, Quaternion hand, Vector3 pole0, float swivel, float side, Quaternion yaw)
            => SolveArmCore(_upperLen, _foreLen, shoulder, wrist, hand, pole0, swivel, side, yaw,
                            new ArmLimits { flex = wristFlex, extend = wristExtend, side = wristSide, twist = forearmTwist });

        /// <summary>
        /// One arm pose for an elbow swing (degrees about the shoulder-wrist line from the default down-and-out): the elbow,
        /// the wrist, the forearm's turn (its back), and the hand within the wrist's limits, with a score (lower is more
        /// natural: little left undone at the limits, a forearm not turned far, the elbow not up or across the chest).
        /// </summary>
        static ArmPose SolveArmCore(float a, float b, Vector3 shoulder, Vector3 wrist, Quaternion hand, Vector3 pole0, float swivel, float side,
                                    Quaternion yaw, ArmLimits lim)
        {
            var p = new ArmPose();
            Vector3 toW = wrist - shoulder;
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
            float tw = Mathf.Clamp(twist, -lim.twist, lim.twist);
            p.foreBack = Quaternion.AngleAxis(tw, f) * b0;
            p.back0 = b0; p.twist = tw;
            // The wrist: what is left of the hand's angle, as a bend toward the palm or back and a bend to the side.
            var fore = Quaternion.LookRotation(f, p.foreBack);
            Vector3 Fl = Quaternion.Inverse(fore) * F;
            float flex = Mathf.Asin(Mathf.Clamp(-Fl.y, -1f, 1f)) * Mathf.Rad2Deg;     // + toward the palm
            float dev = Mathf.Atan2(Fl.x, Fl.z) * Mathf.Rad2Deg;
            float flexC = Mathf.Clamp(flex, -lim.extend, lim.flex), devC = Mathf.Clamp(dev, -lim.side, lim.side);
            p.hand = fore * Quaternion.Euler(0f, devC, 0f) * Quaternion.Euler(flexC, 0f, 0f);
            float over = Mathf.Abs(twist - tw) + Mathf.Abs(flex - flexC) + Mathf.Abs(dev - devC);
            float s = over * 0.08f + Mathf.Abs(tw) * 0.004f + (Mathf.Abs(flexC) + Mathf.Abs(devC)) * 0.004f;
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
            public float heelBack = 0.06f;               // ankle to the back of the heel, along the foot (the heel strike's pivot)
            public Transform ball;                       // the toes (MakeHuman's ball bone): bent so they stay on the floor as the heel rises
            public Quaternion ballRest;
            public Vector3 ballAxis;                     // its bend axis, local; positive lifts the toes (extension at the ball of the foot)
            public float toeBend;                        // degrees this frame
            public float since = 1f, landPitch;          // time since this foot landed, and its tilt then (rolled out after: the heel rocker)
            public float soft, softVel;                  // soft IK: how far short of straight the knee starts easing (m)
            public float liftedAt = -10f;                // when this foot last left the floor (Time.time)
            public float pitchS, pitchVel;               // a planted foot's tilt, through a spring
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
            // The ball of the foot (MakeHuman's toe bone), where the heel turns as it lifts; the toes bend there.
            if (foot != shin && foot.childCount > 0)
            {
                var ball = foot.GetChild(0);
                float toe = Vector3.Dot(ball.position - foot.position, fwd);
                if (toe > 0.05f && toe < 0.2f)
                {
                    g.toe = toe;
                    g.ball = ball; g.ballRest = ball.localRotation;
                    // The bend axis across the foot, signed so a positive bend lifts the toe tip.
                    g.ballAxis = ball.InverseTransformDirection(transform.right);
                    Vector3 tipL = ball.InverseTransformPoint(ball.position + fwd * 0.05f);
                    float y0 = ball.TransformPoint(tipL).y;
                    ball.localRotation = g.ballRest * Quaternion.AngleAxis(20f, g.ballAxis);
                    if (ball.TransformPoint(tipL).y < y0) g.ballAxis = -g.ballAxis;
                    ball.localRotation = g.ballRest;
                }
            }
            return g;
        }

        /// <summary>
        /// The shoes as they stand (MakeHuman's skinned shoes, in the rest pose): the back of each heel, the pivot of the heel
        /// strike (a foot landing toes-up rolls down about its heel, not about the ankle), and the ankle's height over the
        /// sole. MakeHuman's shoe soles reach below the model's ground, so the ankle measured from the model's origin put the
        /// shoes 2-3 cm into the floor.
        /// </summary>
        void MeasureHeels()
        {
            SkinnedMeshRenderer shoes = null;
            foreach (var r in _body.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name.Contains("shoes")) { shoes = r; break; }
            if (!shoes) return;
            var mesh = new Mesh();
            shoes.BakeMesh(mesh, true);
            var verts = mesh.vertices;
            Vector3 fwd = transform.forward, right = transform.right, up = transform.up;
            foreach (var g in new[] { _legA, _legB })
            {
                if (g == null || g.foot == g.shin) continue;
                float back = 0f, below = 0f, front = 0f;
                foreach (var lv in verts)
                {
                    Vector3 w = shoes.transform.position + shoes.transform.rotation * lv;
                    Vector3 d = w - g.foot.position;
                    if (Mathf.Abs(Vector3.Dot(d, right)) > 0.08f || Vector3.Dot(w - _body.position, right) * g.side < 0f) continue;
                    back = Mathf.Max(back, -Vector3.Dot(d, fwd));
                    below = Mathf.Max(below, -Vector3.Dot(d, up));
                    front = Mathf.Max(front, Vector3.Dot(d, fwd));
                }
                if (back > 0.03f && back < 0.14f) { g.heelBack = back; _treadBack = back + 0.015f; }
                if (below > 0.03f && below < 0.18f) g.ankle = below;
                if (front > 0.1f && front < 0.25f) _treadFront = front + 0.005f;
            }
            Destroy(mesh);
        }

        void MeasureBody()
        {
            _hipsHeight = Vector3.Dot(_hips.position - _body.position, transform.up);
            _hipsRestLocal = Quaternion.Inverse(_body.rotation) * _hips.rotation;
            _torsoRest = _torso.localRotation;
            MeasureSpine();
            MeasureBend();
            _torsoBendAxis = _torso.InverseTransformDirection(transform.right);
            _torsoUpAxis = _torso.InverseTransformDirection(transform.up);
            _torsoFwdAxis = _torso.InverseTransformDirection(transform.forward);
            _legA = MeasureLeg(_thighL, _shinL, Find(_body, Bone("FootL")));
            _legB = MeasureLeg(_thighR, _shinR, Find(_body, Bone("FootR")));
            MeasureHeels();
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
            float len = Mathf.Clamp(Gait.SoftReach(d.magnitude, a + b, g.soft), Mathf.Abs(a - b) + 0.05f, (a + b) * 0.999f);
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
            // The toes bend at the ball of the foot (as the heel rises they stay on the floor).
            if (g.ball) g.ball.localRotation = g.ballRest * Quaternion.AngleAxis(g.toeBend, g.ballAxis);
        }
    }
}
