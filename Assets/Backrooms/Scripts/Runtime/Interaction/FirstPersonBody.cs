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
                    FindHandSkin(_looseArm, arm, null);
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
                    if (up && fo && ha) { _bodyArm = MeasureArmRig(_body, Find(_body, "clavicle_" + s), up, fo, ha, s, false); FindHandSkin(_bodyArm, _body, s); }
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
        /// <summary>Backrooms/Lit's settled dust is for the world, not the player (a negative detail-albedo scale turns it off).</summary>
        static void NoDust(Material m) { if (m && m.HasProperty("_DetailAlbedoMapScale")) m.SetFloat("_DetailAlbedoMapScale", -1f); }

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
                        NoDust(m);
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
        /// <summary>Where the solved palm (or fingertip) is this frame, however far the hand is blended out.</summary>
        public Vector3 PalmNow { get; private set; }
        /// <summary>How far the wrist is bent to the side as the arm was solved this frame (degrees), and the forearm's back (the
        /// axis that bend turns the hand about), for holds that keep it small (a door's lever).</summary>
        public float WristSide { get; private set; }
        public Vector3 WristSideAxis { get; private set; }
        /// <summary>The body has an arm to measure (otherwise PropGrabber falls back on its own numbers).</summary>
        public bool HasArm => _curArm != null;

        System.Func<Vector3, float> _grabContact, _touchContact;
        // An item just taken where it lay, and where it was in the hand (from the palm, in the hand's frame) as the hand took it.
        WorldItem _takenItem;
        Vector3 _takenPos;
        Quaternion _takenRot;

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
            // (Hands in view Off: never drawn, its shadow only.)
            if (!loose) SetWorkingArmShown((blend > 0.001f || poke || _touching || _touchW > 0.004f) && _grab.HandsShown);
            Quaternion gripHand = poke ? _grab.PokeRotation : _grab.HandRotation;
            Vector3 gripWrist = poke ? _grab.PokeTip - gripHand * _wristToTip
                                     : _grab.HandPosition - gripHand * new Vector3(_wristToGrip.x, _wristToGrip.y, _wristToGrip.z);
            float s = Mathf.SmoothStep(0f, 1f, blend);
            float sTurn = s;
            if (_grab.TouchingWall && !poke)
            {
                // Reaching out to steady on a wall: the hand turns flat to it first and comes in from off it (turned with
                // the reach, it swept the fingers through a jamb: up to 37 mm in on the way).
                sTurn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blend * 1.7f));
                gripWrist += gripHand * Vector3.up * (0.06f * (1f - s));
            }
            Vector3 wrist = Vector3.Lerp(restWrist, gripWrist, s);
            Quaternion handRot = Quaternion.Slerp(restHand, gripHand, sTurn);
            Vector3 touchOff = poke ? _wristToTip : _wristToGrip;

            // What the fingers (and palm) close on: what PropGrabber holds or touches, or the wall or rail the body's own
            // touch layer rests the hand on (it had no contact at all: fingers went into the wall).
            if (_grabContact == null) { _grabContact = _grab.ContactDistance; _touchContact = TouchDistance; }
            System.Func<Vector3, float> contact = null;
            if (_grab.HasContact && blend > 0.05f) contact = _grabContact;
            else if (_touchW > 0.05f) contact = _touchContact;
            // A held item's fingers straighten back no more than 5 degrees, a rail's too (10, and 15 at the rail's index
            // knuckle, looked bent back); a hand flat on a wall as far as fingers lie flat.
            _extScale = _grab.HeldItem || (_touchRail && blend < 0.05f) ? 0.17f : 1f;

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
                bool railTouch = _touchRail && blend < 0.05f && !_grab.TouchingWall;
                // Twice: a move out can leave another part in (a rail's palm stayed 3.9 mm in after one).
                for (int pass = 0; pass < 2; pass++)
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
                        for (int ji = 0; ji < nj; ji++) f.a[ji] = ee == 0 ? 0f : -_extScale * Extend[Mathf.Min(ji, Extend.Length - 1)];
                        Pose(f);
                        bestF = Mathf.Max(bestF, ClearanceFrom(f, 0, contact));
                    }
                    for (int ji = 0; ji < nj; ji++) f.a[ji] = _closeSaved[ji];
                    Pose(f);
                    if (bestF < worst) { worst = bestF; at = k; }
                }
                // And the palm's own skin (the heel of the hand on the little finger's side was 8 mm in a jamb).
                float skin = DeepestPalmSkin(contact, pose, pose.wrist + pose.hand * touchOff, true, out Vector3 skinAt);
                if (skin < worst) { worst = skin; at = skinAt; }
                Vector3 n = worst < -0.002f ? ContactNormal(contact, at) : Vector3.zero;
                // On a rail the palm comes up off its top (out of the box the shortest way, the heel of the hand over the
                // near edge went sideways and stayed 7 mm in the rail).
                if (railTouch && worst < -0.002f) n = Vector3.up;
                if (n.sqrMagnitude <= 0.25f) break;
                pose = SolveArm(shoulder, pose.wrist + n.normalized * Mathf.Min(-worst + 0.0005f, 0.05f), handRot, pole0, _swivel, side, yaw);
                PoseArm(_upper, _upperAxis, _upperBack, _fore, _foreAxis, _foreBack, _hand, _handFingers, _handBack, shoulder, pose, forearmTwistShare);
                }
            }
            Vector3 wanted = poke ? _grab.PokeTip : _grab.HandPosition;
            Vector3 palmNow = pose.wrist + pose.hand * touchOff;
            PalmNow = palmNow;
            WristSide = pose.dev; WristSideAxis = pose.foreBack;
            PalmError = blend > 0.97f ? Vector3.Distance(palmNow, wanted) : 1f;
            // A held item goes with the hand: where the arm cannot quite put the palm on its grip (at full reach, a wrist
            // at its limit), the item is turned and moved onto the palm instead of floating off it (a sledgehammer's
            // swing left the hand by up to 186 mm, then 46).
            if (!poke && !loose && _grab.HeldItem && blend > 0.97f && !_grab.ItemWaitingForHand)
            {
                var held = _grab.HeldItem;
                var it = held.transform;
                // Just taken where it lay: held as the hand took it (in the solved hand's frame) until the lift is half done,
                // then eased to the carry (PropGrabber.HoldAsTaken). Put onto the palm and out of its skin from the first
                // frame, a can slid 2-3.5 cm in the palm as it started up.
                float asTaken = _grab.HoldAsTaken;
                if (asTaken <= 0f) _takenItem = null;
                else if (_takenItem != held)
                {
                    _takenItem = held;
                    Quaternion inv = Quaternion.Inverse(pose.hand);
                    _takenPos = inv * (it.position - palmNow); _takenRot = inv * it.rotation;
                }
                // (All the way onto the hand: items plan poses the arm can reach (FirstPersonBody.ArmStrain), so this is a
                // few degrees. Capped at 20 instead, a sledgehammer's handle was 74 degrees off the palm after a blow.)
                Quaternion turn = pose.hand * Quaternion.Inverse(gripHand);
                // (Eased in as it leaves where it lay: PropGrabber.ItemOnPalm.)
                float onPalm = _grab.ItemOnPalm * (1f - asTaken);
                // (Round 10: part way, blended about its grip, not its middle: the grip goes from where it is toward the palm
                // and the item turns about it. Its middle lerped while it turned, the grip of a chicken taken by its neck
                // swung up to 56 mm off the palm over the lift.)
                Vector3 gl = _grab.HeldGripLocal;
                {
                    Vector3 g0 = it.position + it.rotation * gl, g1 = palmNow + turn * (g0 - wanted);
                    Quaternion r = Quaternion.Slerp(it.rotation, turn * it.rotation, onPalm);
                    it.SetPositionAndRotation(Vector3.Lerp(g0, g1, onPalm) - r * gl, r);
                }
                // No part of the palm in it: the item is pushed out from the deepest of the palm's skin inside it (twice: a
                // push can bring another part in). The fingers and thumb close on it themselves. Only for what the palm
                // holds (a stick of chalk held in the fingers was pushed off the web of the thumb, the thumb 3 mm further).
                for (int pass = 0; pass < 2 && held.ContactDistanceWorld(palmNow) < 0.02f; pass++)
                {
                    float worstIn = DeepestPalmSkin(held.ContactDistanceWorld, pose, palmNow, pass == 0, out Vector3 deepAt);
                    if (worstIn >= -0.002f) break;
                    // Out the shortest way, unless that is into the palm (the skin past the middle of a handle) or not
                    // defined: then straight off the palm. (Always off the palm, a tape roll on the fingers was pushed
                    // 2 cm along them.)
                    Vector3 nOut = ContactNormal(held.ContactDistanceWorld, deepAt);
                    Vector3 off = -(pose.hand * Vector3.up);
                    Vector3 move = nOut.sqrMagnitude > 0.25f ? -nOut.normalized : off;
                    if (Vector3.Dot(move, off) < -0.3f) move = off;
                    it.position += move * Mathf.Min(-worstIn - 0.001f, 0.02f) * onPalm;
                }
                if (_takenItem == held && asTaken > 0f)
                {
                    Vector3 tp = palmNow + pose.hand * _takenPos;
                    Quaternion tr = pose.hand * _takenRot;
                    // (Round 10: taken from another side round its axis (IGripRound), it turns about that axis in the hand
                    // toward the carry's grip as the lift goes on, the palm staying on its surface.)
                    if (_grab.TakenRegrip(out Vector3 rp, out Quaternion rq)) { tp += tr * rp - tr * (rq * rp); tr *= rq; }
                    Vector3 ga = it.position + it.rotation * gl, gb = tp + tr * gl;
                    Quaternion r = Quaternion.Slerp(it.rotation, tr, asTaken);
                    it.SetPositionAndRotation(Vector3.Lerp(ga, gb, asTaken) - r * gl, r);
                }
            }

            // Fingers: one curl for all, or a held item's own shape (IHandGrip: a pinch, a pencil grip), the thumb flexing
            // and turning across the palm toward the fingers (opposition).
            float curl = Mathf.Lerp(restCurl, _grab.HandCurl, s);
            var shape = blend > 0.01f ? _grab.HeldItem as IHandGrip : null;
            // (Set by the item once the palm is on it: closing on it where it lies too, not only once it is up.)
            var posed = blend > 0.01f && _grab.ItemInGrasp ? _grab.HeldItem as IHandPose : null;
            float pointing = _grab.PokeIndex;
            if (_fingers == null) GroupFingers();
            bool closeOn = contact != null && pointing < 0.5f && (blend > 0.6f || (_touchW > 0.5f && blend < 0.05f));
            float ease = 1f - Mathf.Exp(-Time.deltaTime / 0.05f);
            // In a fist round a handle the thumb closes over the fingers (it stood 20-29 mm off a lamp's rod and a hammer's
            // handle, nothing under its tip): it stops on the handle or on the backs of the index and middle fingers.
            bool fist = closeOn && _grab.HeldItem && s > 0.5f
                        && (shape != null ? shape.FingerCurl(0) : _grab.HeldItem.GripCurl) >= 0.8f && _grab.GripOpen < 0.2f;
            if (_thumbOver == null) _thumbOver = p => Mathf.Min(_thumbOverBase(p), FingersBack(p));
            _thumbOverBase = contact;
            _thumbHug = fist ? contact : null;
            // The fingers first, then the thumb (over them).
            for (int pass = 0; pass < 2; pass++)
            foreach (var f in _fingers)
            {
                bool thumb = f.name == "Thumb";
                if (thumb != (pass == 1)) continue;
                float want = shape != null ? Mathf.Lerp(restCurl, thumb ? shape.ThumbCurl : shape.FingerCurl(FingerIndex(f.name)), s) : curl;
                float opp = thumb ? (shape != null ? Mathf.Lerp(0f, shape.ThumbOpposition, s) : Mathf.Clamp01((curl - 0.2f) * 1.1f)) : 0f;
                // Pointing: the index finger straight out, the other fingers and the thumb curled in.
                if (f.name == "Index") want = Mathf.Lerp(want, -0.05f, pointing);
                else if (thumb) { want = Mathf.Lerp(want, 0.9f, pointing); opp = Mathf.Lerp(opp, 0.85f, pointing); }   // tucked over the curled fingers (it stuck down: a finger gun)
                else want = Mathf.Lerp(want, 1.2f, pointing);
                // Hand over hand on a cord: the fingers open as the hand goes back out.
                if (!thumb && _grab.HeldItem) want *= 1f - 0.6f * _grab.GripOpen;
                // Drawn in toward the middle finger as it curls (most of the way by half a fist).
                // Holding something, together whatever the curl (a tape roll worn on the fingers: their fan put the little
                // finger 7 mm into the roll's core).
                // (All the way in to the middle finger holding something: at 0.85 of their fan, gaps showed between the
                // fingers round a spray can.)
                bool holding = _grab.HeldItem && s > 0.5f;
                f.adduct = thumb ? 0f : -f.spread * Mathf.Clamp01(Mathf.Max(want * 1.6f, holding ? 1f : 0f)) * (holding ? 1f : 0.85f);
                // Holding or touching something: each joint no further than it can go before what is beyond it touches.
                Close(f, want, opp, !closeOn ? null : thumb && fist ? _thumbOver : contact, _goal, out float goalOpp);
                // An item that sets this finger's joints itself (a pencil's tripod, a pad pinch, fingers hooked through a
                // roll's core): those, eased in with the reach (closing to the first touch left them straight).
                if (posed != null && pointing < 0.5f && posed.FingerPose(FingerIndex(f.name), _poseAngles))
                {
                    for (int i = 0; i < f.joints.Count && i < 3; i++) { _poseFrom[i] = _goal[i]; _goal[i] = Mathf.Lerp(_goal[i], _poseAngles[i], s); }
                    float oppFrom = goalOpp;
                    if (thumb) goalOpp = Mathf.Lerp(goalOpp, _poseAngles[3], s);
                    if (closeOn) PoseClear(f, oppFrom, ref goalOpp, contact);
                }
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
                // Eased into what it touches (the hand coming in fast): straight to where it stops instead (fingers swept
                // 12-21 mm into a jamb as the hand reached it).
                if (closeOn && ClearanceFrom(f, 0, contact) < -0.002f)
                {
                    for (int i = 0; i < f.joints.Count; i++) f.a[i] = _goal[i];
                    f.oppDeg = goalOpp;
                    Pose(f);
                }
            }
        }

        static int FingerIndex(string name) => name == "Index" ? 0 : name == "Middle" ? 1 : name == "Ring" ? 2 : name == "Pinky" ? 3 : 4;

        System.Func<Vector3, float> _thumbOver, _thumbOverBase, _thumbHug;

        /// <summary>The backs and sides of the index and middle fingers as they are posed now (8 mm about their bones, which lie
        /// near the back of the finger), for the thumb to close over in a fist.</summary>
        float FingersBack(Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var g in _fingers)
            {
                if (g.name != "Index" && g.name != "Middle") continue;
                int n = g.joints.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector3 a = g.joints[i].t.position, b = i + 1 < n ? g.joints[i + 1].t.position : g.joints[n - 1].t.TransformPoint(g.tipLocal);
                    best = Mathf.Min(best, WorldItem.SegmentDistance(p, a, b) - 0.008f);
                }
            }
            return best;
        }

        Mesh _skinBake;
        readonly List<Vector3> _skinVerts = new List<Vector3>();

        /// <summary>
        /// How deep a surface (a held item's contact shape, a wall) goes into the palm's skin (m, negative inside; 0 if not at all) and where:
        /// the arm's own mesh as it is drawn (baked when bake), its palm, heel and the pads at the base of the fingers.
        /// (A few points on the palm missed the pad at the ring finger's base: a sledgehammer's handle sat 10.6 mm in the
        /// skin there, a tape roll 7.2.) Without the mesh, points on the palm and the finger bases.
        /// </summary>
        float DeepestPalmSkin(System.Func<Vector3, float> distance, in ArmPose pose, Vector3 palmNow, bool bake, out Vector3 at)
        {
            at = palmNow;
            float worst = 0f;
            var sk = _curArm.skin;
            if (sk && _curArm.handVerts != null)
            {
                if (bake || _skinVerts.Count == 0)
                {
                    if (!_skinBake) _skinBake = new Mesh();
                    sk.BakeMesh(_skinBake, true);
                    _skinBake.GetVertices(_skinVerts);
                }
                Vector3 sp = sk.transform.position;
                Quaternion sr = sk.transform.rotation;
                foreach (int vi in _curArm.handVerts)
                {
                    if (vi >= _skinVerts.Count) continue;
                    Vector3 q = sp + sr * _skinVerts[vi];
                    float d = distance(q);
                    if (d < worst) { worst = d; at = q; }
                }
                return worst;
            }
            Vector3 hf = pose.hand * Vector3.forward, hr = pose.hand * Vector3.right, hu = pose.hand * Vector3.up;
            for (int a = -1; a <= 1; a++)
                for (int b = -1; b <= 1; b += 2)
                {
                    Vector3 q = palmNow + hf * (a * 0.035f) + hr * (b * 0.03f) - hu * 0.005f;
                    float d = distance(q);
                    if (d < worst) { worst = d; at = q; }
                }
            if (_fingers != null)
                foreach (var fg in _fingers)
                {
                    if (fg.name == "Thumb" || fg.joints.Count == 0) continue;
                    Vector3 q = fg.joints[0].t.position - hu * 0.012f;
                    float d = distance(q);
                    if (d < worst) { worst = d; at = q; }
                }
            return worst;
        }

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
            // Each bone's skin, measured on the mesh (MeasurePads): the radius of its round and how far the round's centre
            // lies toward the palm from the bone (m). 0: not measured (a finger's half thickness round the bone).
            public readonly float[] padR = new float[4], padO = new float[4];
            // And points of its skin on the palm's side and the sides (in the bone's own frame): what touches.
            public readonly Vector3[][] pad = new Vector3[4][];
        }
        /// <summary>Points of skin kept per finger bone (MeasurePads).</summary>
        const int PadPoints = 12;
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
            // Each bone's skin, from the mesh (the fingers are at rest now).
            MeasurePads();
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
            // A thumb inside opened out may be clear turned further across the palm (on a rail it stayed straight, inside
            // the rail, at every turn it started from).
            if (thumb && ClearanceFrom(f, 0, contact) < 0f)
            {
                if (CloseThumb(f, want, oppT, open, contact, goal, out goalOpp))
                {
                    f.oppDeg = savedOpp;
                    for (int i = 0; i < n; i++) f.a[i] = _closeSaved[i];
                    return;
                }
                for (int i = 0; i < n; i++) f.a[i] = 0f;
                f.oppDeg = open;
                Pose(f);
            }
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
                // What lies beyond a joint and is clear still closes, joint by joint: a little finger whose first bone
                // lies on a rail's top curls over its edge (it stayed straight and bent back, the tip 38 mm off the rail).
                if (!thumb)
                {
                    for (int i = 0; i < n; i++) f.a[i] = goal[i];
                    Pose(f);
                    for (int i = 1; i < n; i++)
                    {
                        if (ClearanceFrom(f, i, contact) < 0f) break;
                        float from = f.a[i], target = want * 62f * f.joints[i].w, done = from;
                        for (int st = 1; st <= 8 && target > from; st++)
                        {
                            f.a[i] = Mathf.Lerp(from, target, st / 8f);
                            Pose(f);
                            if (ClearanceFrom(f, i, contact) < 0f) break;
                            done = f.a[i];
                        }
                        f.a[i] = done; Pose(f);
                        goal[i] = done;
                    }
                }
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
            if (!thumb)
            {
                WrapClose(f, contact, goal);
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
                        // Narrowed down to where it touches (one half step left the pads 2-4 mm off what they held).
                        float lo = done, hi = k;
                        for (int it = 0; it < 3; it++)
                        {
                            float mid = (lo + hi) * 0.5f;
                            f.a[i] = target * mid;
                            if (i == 0 && thumb) f.oppDeg = Mathf.Lerp(open, goalOpp, mid);
                            Pose(f);
                            if (ClearanceFrom(f, i, contact) >= 0f) lo = mid; else hi = mid;
                        }
                        done = lo;
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
        readonly float[] _closeSaved = new float[4], _poseAngles = new float[4], _poseFrom = new float[4];

        /// <summary>
        /// A posed finger (IHandPose) never goes into what it holds: eased back from the pose toward where it closed (in
        /// _poseFrom, its turn in oppFrom) until it is no more than 1.5 mm in. (Fingers hooked through a tape roll's core
        /// went 7 mm into the roll.) The result in _goal and goalOpp.
        /// </summary>
        void PoseClear(Finger f, float oppFrom, ref float goalOpp, System.Func<Vector3, float> contact)
        {
            int n = f.joints.Count;
            float savedOpp = f.oppDeg;
            for (int i = 0; i < n; i++) _closeSaved[i] = f.a[i];
            for (int i = 0; i < n; i++) f.a[i] = _goal[i];
            f.oppDeg = goalOpp; Pose(f);
            if (ClearanceFrom(f, 0, contact) < -0.0015f)
            {
                float lo = 0f, hi = 1f;
                for (int it = 0; it < 5; it++)
                {
                    float m = (lo + hi) * 0.5f;
                    for (int i = 0; i < n; i++) f.a[i] = Mathf.Lerp(_poseFrom[i], _goal[i], m);
                    f.oppDeg = Mathf.Lerp(oppFrom, goalOpp, m); Pose(f);
                    if (ClearanceFrom(f, 0, contact) >= -0.0015f) lo = m; else hi = m;
                }
                for (int i = 0; i < n; i++) _goal[i] = Mathf.Lerp(_poseFrom[i], _goal[i], lo);
                goalOpp = Mathf.Lerp(oppFrom, goalOpp, lo);
            }
            for (int i = 0; i < n; i++) f.a[i] = _closeSaved[i];
            f.oppDeg = savedOpp; Pose(f);
        }
        readonly float[] _thumbTry = new float[4];

        /// <summary>
        /// A finger (or the thumb) closing round something, all its joints bending together in its own proportions (the goal:
        /// knuckle, middle and end joints as a curling finger bends them). When a bone touches, it and the joints before it
        /// stop there and the joints beyond go on wrapping, so a finger whose first bone meets a rod curls its middle and end
        /// bones round it, and a fingertip that meets something first stops the whole finger (a pinch). (Joint by joint
        /// from the knuckle out, the knuckle took all the bend before the middle joint moved: fingers on a lamp's rod, a
        /// can and a hammer's handle lay with the middle joint near straight and the end one bent, 59/45/36, 42/2/42, 32/29/48.)
        /// From the finger as it is (f.a, the thumb's turn set); the result in goal.
        /// </summary>
        void WrapClose(Finger f, System.Func<Vector3, float> contact, float[] goal)
        {
            int n = f.joints.Count;
            const int Steps = 16;
            int free = 0;
            for (int i = 0; i < n; i++) { _wrapFrom[i] = f.a[i]; _wrapTo[i] = goal[i]; }
            float k0 = 0f;
            for (int s = 1; s <= Steps && free < n; s++)
            {
                float k = (float)s / Steps;
                for (int i = free; i < n; i++) f.a[i] = Mathf.Lerp(_wrapFrom[i], _wrapTo[i], k);
                Pose(f);
                if (ClearanceFrom(f, free, contact) >= 0f) { k0 = k; continue; }
                ContactStops++;
                float lo = k0, hi = k;
                for (int it = 0; it < 4; it++)
                {
                    float mid = (lo + hi) * 0.5f;
                    for (int i = free; i < n; i++) f.a[i] = Mathf.Lerp(_wrapFrom[i], _wrapTo[i], mid);
                    Pose(f);
                    if (ClearanceFrom(f, free, contact) >= 0f) lo = mid; else hi = mid;
                }
                // The bone that touches (the nearest the knuckle), just past the stop.
                for (int i = free; i < n; i++) f.a[i] = Mathf.Lerp(_wrapFrom[i], _wrapTo[i], hi);
                Pose(f);
                int touching = n - 1;
                for (int b = free; b < n; b++) if (BoneClearance(f, b, contact) < 0f) { touching = b; break; }
                for (int i = free; i < n; i++) f.a[i] = Mathf.Lerp(_wrapFrom[i], _wrapTo[i], lo);
                Pose(f);
                free = touching + 1;
                k0 = lo;
                // (The joints beyond go on from here at the same pace: their sweep is still from _wrapFrom to _wrapTo.)
            }
            for (int i = 0; i < n; i++) goal[i] = f.a[i];
        }
        readonly float[] _wrapFrom = new float[4], _wrapTo = new float[4], _thumbGoal = new float[4];

        /// <summary>One bone's clearance (as <see cref="ClearanceFrom"/>, that bone alone).</summary>
        static float BoneClearance(Finger f, int i, System.Func<Vector3, float> contact)
        {
            if (f.pad[i] != null)
            {
                float m = float.MaxValue;
                var t = f.joints[i].t;
                foreach (var p in f.pad[i]) m = Mathf.Min(m, contact(t.TransformPoint(p)) + SkinGive);
                return m;
            }
            bool thumb = f.oppAxis.sqrMagnitude > 0.5f;
            SegmentPad(f, i, out Vector3 a, out Vector3 b, out Vector3 o, out float r);
            float best = float.MaxValue;
            for (int k = thumb && i == 0 ? 3 : 1; k <= 3; k++) best = Mathf.Min(best, contact(Vector3.Lerp(a, b, k / 3f) + o) - r);
            return best;
        }
        float _extScale = 1f, _thumbPrevOpp;
        bool _thumbPrevSet;

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
            float far = Mathf.Max(oppT, 25f) + 20f, bestOpp = oppT;
            for (int c = 0; c <= 8; c++) TryTurn(Mathf.Lerp(open, far, c / 8f));
            // Then finer about the best (12 degrees apart, the tip swept past a stick of chalk between two turns).
            if (found)
                for (float step = (far - open) / 16f; step > 1.5f; step *= 0.5f)
                {
                    float at = bestOpp;
                    TryTurn(at - step); TryTurn(at + step);
                }
            void TryTurn(float o)
            {
                f.oppDeg = o;
                for (int i = 0; i < n; i++) f.a[i] = 0f;
                Pose(f);
                if (ClearanceFrom(f, 0, contact) < 0f) return;
                // All its joints together, each stopping where its bone touches (WrapClose).
                for (int i = 0; i < n; i++) _thumbGoal[i] = Mathf.Max(want, 1f) * 62f * f.joints[i].w * 1.6f;
                WrapClose(f, contact, _thumbGoal);
                Pose(f);
                float gap = TipClearance(f, contact);
                // Nearest the surface (in mm), then nearest the grip's turn (10 degrees count as 2 mm).
                // (and, holding on, the turn it had: a spray can's thumb took a different one from run to run)
                float score = Mathf.Max(gap, 0f) + Mathf.Abs(o - oppT) * 0.0002f + (_thumbPrevSet ? Mathf.Abs(o - _thumbPrevOpp) * 0.0001f : 0f);
                // Over the fingers in a fist, the thumb also lies on the handle (its tip on the index finger, it stood
                // clear of a lamp's rod).
                if (_thumbHug != null) score += Mathf.Max(0f, ClearanceFrom(f, 1, _thumbHug)) * 0.3f;
                // And bent about as the grip bends it (10 degrees short count as a millimetre): a thumb lying straight
                // along a spray can touched it with its tip as well as a curled one.
                for (int i = 1; i < n; i++) score += Mathf.Max(0f, want * 62f * f.joints[i].w - f.a[i]) * 0.0001f;
                if (score < bestScore)
                {
                    bestScore = score; found = true; bestOpp = o;
                    for (int i = 0; i < n; i++) _thumbTry[i] = f.a[i];
                }
            }
            goalOpp = bestOpp;
            if (!found) return false;
            _thumbPrevOpp = goalOpp; _thumbPrevSet = true;
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
                // The bone's own skin where it was measured (points of it carried with the bone); otherwise rounds along it
                // (a third, two thirds and its end), pushed toward the palm as its pad is thicker than the back of the
                // finger. (Rounds of 8.5 mm on the bone let the pads sink 6-9 mm into a hammer's handle, a can, a lamp's rod;
                // a round about the thumb's wide end stood 4-7 mm proud of its pad.)
                var pts = f.pad[i];
                if (pts != null)
                {
                    var t = f.joints[i].t;
                    for (int k = 0; k < pts.Length; k++) best = Mathf.Min(best, contact(t.TransformPoint(pts[k])) + SkinGive);
                    continue;
                }
                SegmentPad(f, i, out Vector3 a, out Vector3 b, out Vector3 o, out float r);
                for (int k = thumb && i == 0 ? 3 : 1; k <= 3; k++)
                    best = Mathf.Min(best, contact(Vector3.Lerp(a, b, k / 3f) + o) - r);
            }
            return best;
        }

        /// <summary>How far skin gives under a grip (m): a point of it is clear this far into what it touches.</summary>
        const float SkinGive = 0.0005f;

        /// <summary>The fingertip's clearance (its pad at the tip; m, negative inside).</summary>
        static float TipClearance(Finger f, System.Func<Vector3, float> contact)
        {
            int last = f.joints.Count - 1;
            if (f.pad[last] != null) return ClearanceFrom(f, last, contact);
            SegmentPad(f, last, out _, out Vector3 b, out Vector3 o, out float r);
            return contact(b + o) - r;
        }

        /// <summary>A finger bone from a to b, its pad round's offset toward the palm o and radius r.</summary>
        static void SegmentPad(Finger f, int i, out Vector3 a, out Vector3 b, out Vector3 o, out float r)
        {
            int n = f.joints.Count;
            var j = f.joints[i];
            a = j.t.position;
            b = i + 1 < n ? f.joints[i + 1].t.position : f.joints[n - 1].t.TransformPoint(f.tipLocal);
            r = f.padR[i] > 0f ? f.padR[i] : FingerRadius;
            o = Vector3.zero;
            if (f.padO[i] > 0f)
            {
                // Toward the palm: the way the bone's own bend (about its axis) carries what lies beyond it.
                Vector3 pal = Vector3.Cross(j.t.TransformDirection(j.axis), b - a);
                if (pal.sqrMagnitude > 1e-10f) o = pal.normalized * f.padO[i];
            }
        }

        /// <summary>
        /// Each finger bone's skin, measured once on the arm's mesh with the fingers at rest: the smallest round about the
        /// bone (its centre moved toward the palm) that holds the skin around it, and the fingertip's end. MakeHuman's
        /// finger bones lie near the back of the finger: the pads reach 12-16 mm toward the palm, 7-9 toward the nail.
        /// </summary>
        void MeasurePads()
        {
            var sk = _curArm != null ? _curArm.skin : null;
            if (!sk || _fingers == null || !_hand) return;
            var baked = new Mesh();
            sk.BakeMesh(baked, true);
            var vs = new List<Vector3>();
            baked.GetVertices(vs);
            if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked);
            Vector3 sp = sk.transform.position;
            Quaternion sr = sk.transform.rotation;
            int nf = _fingers.Count;
            var pts = new Vector3[nf][];
            var pal = new Vector3[nf][];
            for (int fi = 0; fi < nf; fi++)
            {
                var f = _fingers[fi];
                int n = f.joints.Count;
                pts[fi] = new Vector3[n + 1];
                pal[fi] = new Vector3[n];
                for (int i = 0; i < n; i++) pts[fi][i] = f.joints[i].t.position;
                pts[fi][n] = f.joints[n - 1].t.TransformPoint(f.tipLocal);
                for (int i = 0; i < n; i++)
                    pal[fi][i] = Vector3.Cross(f.joints[i].t.TransformDirection(f.joints[i].axis), pts[fi][i + 1] - pts[fi][i]).normalized;
            }
            // Each vertex near the hand to the bone it lies round (the nearest, within 18 mm; not the palm at the knuckle).
            var segVerts = new List<Vector3>[nf * 4];
            for (int k = 0; k < segVerts.Length; k++) segVerts[k] = new List<Vector3>();
            Vector3 hp = _hand.position;
            foreach (var lv in vs)
            {
                Vector3 q = sp + sr * lv;
                if ((q - hp).sqrMagnitude > 0.2f * 0.2f) continue;
                float bestD = 0.0165f; int bf = -1, bs = -1; float bt = 0f;
                for (int fi = 0; fi < nf; fi++)
                    for (int s = 0; s + 1 < pts[fi].Length; s++)
                    {
                        Vector3 a = pts[fi][s], ab = pts[fi][s + 1] - a;
                        float t = Mathf.Clamp01(Vector3.Dot(q - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
                        float d = (q - (a + ab * t)).magnitude;
                        if (d < bestD) { bestD = d; bf = fi; bs = s; bt = t; }
                    }
                // (Not the base of the finger: the palm's skin there is the palm's, kept out of what the hand holds by itself.)
                if (bf < 0 || (bs == 0 && bt < 0.35f)) continue;
                // Only the palm's side of the bone: what the pad meets. (A round about all of it, the nail side and the
                // sides as well, stood 5-6 mm proud of the pad: the thumb's end round was 13.5 mm, its pad 6 mm off what
                // it held when the round touched.)
                Vector3 onBone = pts[bf][bs] + (pts[bf][bs + 1] - pts[bf][bs]) * bt;
                if (Vector3.Dot(q - onBone, pal[bf][bs]) < -0.001f) continue;
                segVerts[bf * 4 + bs].Add(q);
            }
            for (int fi = 0; fi < nf; fi++)
            {
                var f = _fingers[fi];
                int n = f.joints.Count;
                for (int s = 0; s < n && s < 4; s++)
                {
                    var list = segVerts[fi * 4 + s];
                    // (Not the thumb's first bone: it lies in the ball of the thumb, only its end is tested.)
                    bool thumbBase = s == 0 && f.oppAxis.sqrMagnitude > 0.5f;
                    if (list.Count < 6 || thumbBase) { f.padR[s] = 0f; f.padO[s] = 0f; f.pad[s] = null; continue; }
                    Vector3 a = pts[fi][s], b = pts[fi][s + 1], ab = b - a, axis = ab.normalized;
                    bool last = s == n - 1;
                    float bestR = float.MaxValue, bestO = 0f;
                    var need = new float[list.Count];
                    for (float o = 0f; o <= 0.008f; o += 0.0005f)
                    {
                        for (int k = 0; k < list.Count; k++)
                        {
                            Vector3 q = list[k];
                            float t = Vector3.Dot(q - a, ab) / ab.sqrMagnitude;
                            Vector3 off;
                            // Past the tip: from the tip's round (the end of the finger); along the bone: across it.
                            if (last && t > 1f) off = q - (b + pal[fi][s] * o);
                            else { off = q - a - axis * Vector3.Dot(q - a, axis); off -= pal[fi][s] * o; }
                            need[k] = off.magnitude;
                        }
                        // All but the furthest few (a stray vertex of the next finger or the web between them).
                        System.Array.Sort(need);
                        float r = need[Mathf.Min(need.Length - 1, Mathf.CeilToInt(need.Length * 0.96f))];
                        if (r < bestR - 1e-5f) { bestR = r; bestO = o; }
                    }
                    // (Less a millimetre: skin gives a little under a grip; to the outermost vertex the pads stopped 2-3 mm off.)
                    f.padR[s] = Mathf.Clamp(bestR - 0.001f, FingerRadius * 0.8f, 0.016f);
                    f.padO[s] = bestO;
                    // The skin itself: its most palmar point, then each the furthest from those kept, away from the joint
                    // creases (they fold as the joint bends). Carried with the bone, the fingers stop on these.
                    var use = new List<Vector3>();
                    foreach (var q in list)
                    {
                        float t = Vector3.Dot(q - a, ab) / ab.sqrMagnitude;
                        if (t < 0.12f || (!last && t > 0.9f)) continue;
                        use.Add(q);
                    }
                    if (use.Count < PadPoints) { f.pad[s] = null; continue; }
                    var dmin = new float[use.Count];
                    int pick = 0; float most = float.MinValue;
                    for (int k = 0; k < use.Count; k++) { float dp = Vector3.Dot(use[k] - a, pal[fi][s]); if (dp > most) { most = dp; pick = k; } }
                    var kept = new Vector3[PadPoints];
                    for (int k = 0; k < use.Count; k++) dmin[k] = float.MaxValue;
                    for (int c = 0; c < PadPoints; c++)
                    {
                        kept[c] = f.joints[s].t.InverseTransformPoint(use[pick]);
                        Vector3 pq = use[pick];
                        int far = 0;
                        for (int k = 0; k < use.Count; k++)
                        {
                            dmin[k] = Mathf.Min(dmin[k], (use[k] - pq).sqrMagnitude);
                            if (dmin[k] > dmin[far]) far = k;
                        }
                        pick = far;
                    }
                    f.pad[s] = kept;
                }
            }
        }

        /// <summary>The measured pads (for tests): finger: radius/offset mm per bone.</summary>
        public string PadsReport
        {
            get
            {
                if (_fingers == null) return "";
                var sb = new System.Text.StringBuilder();
                foreach (var f in _fingers)
                {
                    sb.Append(f.name).Append(':');
                    for (int i = 0; i < f.joints.Count; i++) sb.AppendFormat(" {0:0.0}/{1:0.0}", f.padR[i] * 1000f, f.padO[i] * 1000f);
                    sb.Append(' ');
                }
                return sb.ToString();
            }
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

        struct ArmPose { public Vector3 elbow, wrist, foreBack, back0; public Quaternion hand; public float score, twist, dev; }
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
                float s = float.MaxValue, tw = 0f;
                for (int k = -8; k <= 10; k++)
                {
                    var p = SolveArm(shoulder, wrist, hand, pole0, k * 15f, side, yaw);
                    if (p.score < s) { s = p.score; tw = p.twist; }
                }
                // A forearm turned past 70% of its limit costs more here (a wall lean and a jamb were held at 93 and 95,
                // the limit, where a turn 30 degrees round put it at 64).
                s += Mathf.Max(0f, Mathf.Abs(tw) - 0.7f * forearmTwist) * 0.03f;
                s += Mathf.Abs(a) / 180f * 0.3f;
                if (s < bestScore) { bestScore = s; best = f; }
            }
            return best;
        }

        /// <summary>
        /// How hard it is for the working arm to put the palm on <paramref name="palm"/> with the hand turned so: the solver's
        /// score for the best way round for the elbow (lower is easier; out of reach counts heavily), and in overDeg how far
        /// the hand would end up from that turn (degrees; about 0 when the wrist and forearm can do it). Items plan their
        /// poses with it (a sledgehammer's blow was planned with the hand turned 94 degrees past what a wrist does: the
        /// head swept sideways, or the handle left the palm).
        /// </summary>
        public float ArmStrain(Vector3 palm, Quaternion hand, out float overDeg) => ArmStrain(palm, hand, out overDeg, out _);

        /// <summary>As above, and how far the wrist bends to the side in the best pose (degrees).</summary>
        public float ArmStrain(Vector3 palm, Quaternion hand, out float overDeg, out float sideDeg)
        {
            overDeg = 0f; sideDeg = 0f;
            if (_curArm == null || !_grab) return 0f;
            Vector3 shoulder = ShoulderNow;
            Quaternion yaw = transform.rotation;
            float side = _grab.Side;
            Vector3 wrist = palm - hand * _wristToGrip;
            Vector3 dir0 = (wrist - shoulder).normalized;
            Vector3 pole0 = Vector3.ProjectOnPlane(yaw * new Vector3(0.8f * side, -1f, -0.25f), dir0).normalized;
            float best = float.MaxValue;
            for (int k = -4; k <= 5; k++)
            {
                var p = SolveArm(shoulder, wrist, hand, pole0, k * 30f, side, yaw);
                if (p.score < best) { best = p.score; overDeg = Quaternion.Angle(p.hand, hand); sideDeg = p.dev; }
            }
            float far = (wrist - shoulder).magnitude - (_upperLen + _foreLen) * 0.97f;
            return best + Mathf.Max(0f, far) * 40f;
        }

        /// <summary>
        /// Round 10: the arm solved as each frame solves it, from a shoulder where the body will be (a reach's plan), for a
        /// palm point and hand turn: the solver's score, how far the hand stays off that turn (overDeg: what the wrist and
        /// forearm cannot do), and how far the wrist then is from the shoulder (wristShare, of upper arm + forearm) once it is
        /// moved so the palm is on its point. (Offered by the shoulder-to-grip distance alone, a sledgehammer on the floor was
        /// reached for and never taken: its hand's turn put the wrist 6-10 cm out of reach.)
        /// </summary>
        public float ArmStrainFrom(Vector3 shoulder, Vector3 palm, Quaternion hand, out float overDeg, out float wristShare)
            => ArmStrainFrom(shoulder, palm, hand, out overDeg, out wristShare, out _);

        /// <summary>As above, and how far the palm still misses its point once the wrist is moved for it, as the arm is solved
        /// each frame (m). (With the hand 20-25 degrees off its turn, a lamp lying down was reached for and the palm stayed
        /// 28 mm off its rod.)</summary>
        public float ArmStrainFrom(Vector3 shoulder, Vector3 palm, Quaternion hand, out float overDeg, out float wristShare, out float miss)
        {
            overDeg = 0f; wristShare = 0f; miss = 0f;
            if (_curArm == null || !_grab) return 0f;
            Quaternion yaw = transform.rotation;
            float side = _grab.Side;
            Vector3 wrist = palm - hand * _wristToGrip;
            Vector3 dir0 = (wrist - shoulder).normalized;
            Vector3 pole0 = Vector3.ProjectOnPlane(yaw * new Vector3(0.8f * side, -1f, -0.25f), dir0).normalized;
            float best = float.MaxValue, swivel = 0f;
            Quaternion got = hand;
            for (int k = -4; k <= 5; k++)
            {
                var p = SolveArm(shoulder, wrist, hand, pole0, k * 30f, side, yaw);
                if (p.score < best) { best = p.score; got = p.hand; swivel = k * 30f; }
            }
            var p2 = SolveArm(shoulder, palm - got * _wristToGrip, hand, pole0, swivel, side, yaw);
            overDeg = Quaternion.Angle(p2.hand, hand);
            wristShare = (palm - got * _wristToGrip - shoulder).magnitude / Mathf.Max(0.1f, _upperLen + _foreLen);
            miss = (p2.wrist + p2.hand * _wristToGrip - palm).magnitude;
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
            p.dev = devC;
            p.hand = fore * Quaternion.Euler(0f, devC, 0f) * Quaternion.Euler(flexC, 0f, 0f);
            float over = Mathf.Abs(twist - tw) + Mathf.Abs(flex - flexC) + Mathf.Abs(dev - devC);
            float s = over * 0.08f + Mathf.Abs(tw) * 0.004f + (Mathf.Abs(flexC) + Mathf.Abs(devC)) * 0.004f;
            // Past 70% of a limit costs more: holds sit well inside what a wrist and forearm do (a hammer, a plug, a door's
            // lever were held with the wrist 27-30 degrees to the side, its limit 28; a chicken with the forearm turned 95).
            s += (Mathf.Max(0f, Mathf.Abs(dev) - 0.7f * lim.side) + Mathf.Max(0f, Mathf.Abs(twist) - 0.7f * lim.twist) * 0.5f
                  + Mathf.Max(0f, flex - 0.7f * lim.flex) + Mathf.Max(0f, -flex - 0.7f * lim.extend)) * 0.05f;   // (0.025 left a spray can's wrist bent back 43 of 60, a chicken's 23 to the side of 28)
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
            MeasureHinge();
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
