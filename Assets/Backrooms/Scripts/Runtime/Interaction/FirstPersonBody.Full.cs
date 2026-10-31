using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The rest of the body (2026-10-27): a head and neck that follow the view and a left arm holding the camcorder to the
    /// eye (drawn into the shadows only: the camera is inside them), the right arm always there (hanging and swinging as
    /// you walk, a hand laid on a wall or a rail close by, the held thing's grip over both), and the legs' motion.
    ///
    /// Layers, lowest first, each limb taken by the highest that wants it (a pose stack): Body (walking: feet planted and
    /// stepping, the arm hanging and swinging), IK (the free hand on the world), Override (the hand on what it holds,
    /// PropGrabber). Changes of pose go through springs (Springs.Damper), which carry their velocity through: a crouch, a
    /// landing or a turn blends like a body, not a cross-fade (inertialization). Knocks (walking into a wall, a hard
    /// landing, <see cref="Bump"/>) shove the struck part through under-damped springs that pull it back: never a fall.
    /// </summary>
    public partial class FirstPersonBody
    {
        // ---------------- Head, neck, left arm, camcorder ----------------

        Transform _neck, _headBone;
        Quaternion _neckRest;
        Vector3 _headFwd, _headUp;
        Transform _leftArm, _camcorder;
        ArmRig _lRig;          // the arm holding the camcorder: a separate model (_leftArm), or the body's other arm

        void SetupFull()
        {
            if (_body)
            {
                _neck = Find(_body, Bone("Neck")); _headBone = Find(_body, Bone("Head"));
                if (_neck) _neckRest = _neck.localRotation;
                if (_headBone)
                {
                    _headFwd = _headBone.InverseTransformDirection(transform.forward);
                    _headUp = _headBone.InverseTransformDirection(transform.up);
                }
                // The head is where the camera is: in the shadows only.
                var head = Find(_body, "HeadMesh");
                if (head && head.TryGetComponent<Renderer>(out var hr)) hr.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
            float side = _grab ? _grab.Side : 1f;
            if (_isMpfb && _body)
            {
                // The body's other arm holds the camcorder (its parts only cast shadows: Setup).
                string s = side > 0f ? "l" : "r";
                var up = Find(_body, "upperarm_" + s); var fo = Find(_body, "lowerarm_" + s); var ha = Find(_body, "hand_" + s);
                if (up && fo && ha) _lRig = MeasureArmRig(_body, Find(_body, "clavicle_" + s), up, fo, ha, s, false);
            }
            else
            {
                var leftPrefab = Resources.Load<GameObject>(side > 0f ? "Player/PlayerArm" : "Player/PlayerArmRight");
                if (leftPrefab)
                {
                    _leftArm = Instantiate(leftPrefab).transform;
                    _leftArm.name = "CamcorderArm";
                    Setup(_leftArm.gameObject, false);
                    foreach (var r in _leftArm.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    var up = Find(_leftArm, "UpperArm"); var fo = Find(_leftArm, "Forearm"); var ha = Find(_leftArm, "Hand");
                    if (up && fo && ha) _lRig = MeasureArmRig(_leftArm, null, up, fo, ha, null, false);
                    else { Destroy(_leftArm.gameObject); _leftArm = null; }
                    if (_leftArm) Generation.PoolLitSwitch.Attach(_leftArm.gameObject);
                }
            }
            _camcorder = MakeCamcorder();
            if (_player) _player.Bumped += OnBumped;
        }

        void OnDestroyFull()
        {
            if (_leftArm) Destroy(_leftArm.gameObject);
            if (_camcorder) Destroy(_camcorder.gameObject);
            if (_player) _player.Bumped -= OnBumped;
        }

        /// <summary>The camcorder in the left hand, at the eye (simple shapes: it is only seen as a shadow).</summary>
        Transform MakeCamcorder()
        {
            var root = new GameObject("CamcorderShadow").transform;
            var mat = new Material(Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit")) { name = "FP_Camcorder" };
            mat.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.055f));
            NoDust(mat);
            _mats["Camcorder"] = mat;
            void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler)
            {
                var go = GameObject.CreatePrimitive(type);
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(root, false);
                go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localEulerAngles = euler;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            Part(PrimitiveType.Cube, new Vector3(-0.004f, -0.022f, 0.075f), new Vector3(0.072f, 0.082f, 0.19f), Vector3.zero);       // body
            Part(PrimitiveType.Cylinder, new Vector3(0f, -0.006f, 0.185f), new Vector3(0.058f, 0.03f, 0.058f), new Vector3(90f, 0f, 0f));   // lens
            Part(PrimitiveType.Cube, new Vector3(0.002f, 0.028f, -0.01f), new Vector3(0.034f, 0.03f, 0.055f), Vector3.zero);     // viewfinder
            Part(PrimitiveType.Cube, new Vector3(-0.044f, -0.012f, 0.07f), new Vector3(0.008f, 0.052f, 0.072f), Vector3.zero);   // flip screen
            Generation.PoolLitSwitch.Attach(root.gameObject);
            return root;
        }

        Transform _camT;

        /// <summary>
        /// After the body: the head and neck to the view, the camcorder where the camera is (held a little out in front of
        /// the eye: CamcorderPlayerController.CamcorderHold), the left hand under it.
        /// </summary>
        void UpdateFull()
        {
            var pivot = _player.cameraPitchPivot ? _player.cameraPitchPivot : transform;
            if (!_camT) { var c = pivot.GetComponentInChildren<Camera>(); _camT = c ? c.transform : pivot; }
            bool show = !_player.noclip && _player.Eye > HideBelowEye * 0.6f;
            if (_camcorder)
            {
                _camcorder.gameObject.SetActive(show);
                _camcorder.SetPositionAndRotation(_camT.position, _camT.rotation);
            }
            if (_body && _body.gameObject.activeSelf && _neck && _headBone)
            {
                // The neck takes some of the turn, the head the rest: it looks where the camera does.
                _neck.localRotation = _neckRest;
                Quaternion want = Quaternion.LookRotation(pivot.forward, pivot.up) * Quaternion.Inverse(Quaternion.LookRotation(_headFwd, _headUp));
                Quaternion delta = want * Quaternion.Inverse(_headBone.rotation);
                _neck.rotation = Quaternion.Slerp(Quaternion.identity, delta, 0.4f) * _neck.rotation;
                _headBone.rotation = want;
            }
            if (_leftArm) _leftArm.gameObject.SetActive(show);
            bool onBody = _lRig != null && !_leftArm;
            // The camcorder on its strap (PropGrabber H): the left arm is free, and seen.
            bool strap = _grab && _grab.CamcorderOnStrap;
            // (Drawn only once it is well out: at the camcorder the hand is right in front of the lens, palm up.)
            SetCamArmShown(strap || leftReachW > 0.45f);
            if (_lRig != null && show && (!onBody || (_body && _body.gameObject.activeSelf)))
            {
                if (strap) UpdateFreeLeftArm(pivot);
                else UpdateLeftArm(pivot, _camT);
            }
        }

        /// <summary>
        /// For probes: how far the deepest finger joint (and the wrist) of each hand is inside what the hands hold (m, 0 if
        /// none is), by its colliders (PropGrabber.ContactDistance). The left hand only while it holds too.
        /// </summary>
        public void HandDepths(out float right, out float left)
        {
            right = left = 0f;
            if (!_grab || !_grab.HasContact) return;
            if (_hand) right = Mathf.Max(right, -_grab.ContactDistance(_hand.position));
            foreach (var j in _fingerJoints) if (j.t) right = Mathf.Max(right, -_grab.ContactDistance(j.t.position));
            right = Mathf.Max(right, ForearmDepth(_fore, _hand));
            if (_lRig == null || !_grab.TwoHanded) return;
            if (_lRig.hand) left = Mathf.Max(left, -_grab.ContactDistance(_lRig.hand.position));
            foreach (var j in _lRig.joints) if (j.t) left = Mathf.Max(left, -_grab.ContactDistance(j.t.position));
            left = Mathf.Max(left, ForearmDepth(_lRig.fore, _lRig.hand));
        }

        /// <summary>For probes: as HandDepths, the hand (fingers and wrist) and the forearm apart, each side, and where the
        /// deepest forearm point is (world).</summary>
        public void HandDepthsDetail(out float rHand, out float rFore, out float lHand, out float lFore, out Vector3 rAt, out Vector3 lAt)
        {
            rHand = rFore = lHand = lFore = 0f; rAt = lAt = Vector3.zero;
            if (!_grab || !_grab.HasContact) return;
            if (_hand) rHand = Mathf.Max(rHand, -_grab.ContactDistance(_hand.position));
            foreach (var j in _fingerJoints) if (j.t) rHand = Mathf.Max(rHand, -_grab.ContactDistance(j.t.position));
            rFore = ForearmDepth(_fore, _hand, out rAt);
            if (_lRig == null || !_grab.TwoHanded) return;
            if (_lRig.hand) lHand = Mathf.Max(lHand, -_grab.ContactDistance(_lRig.hand.position));
            foreach (var j in _lRig.joints) if (j.t) lHand = Mathf.Max(lHand, -_grab.ContactDistance(j.t.position));
            lFore = ForearmDepth(_lRig.fore, _lRig.hand, out lAt);
        }

        float ForearmDepth(Transform fore, Transform hand, out Vector3 at)
        {
            at = Vector3.zero;
            if (!fore || !hand) return 0f;
            float d = 0f;
            for (int i = 1; i <= 5; i++)
            {
                Vector3 p = Vector3.Lerp(fore.position, hand.position, i / 5f);
                float k = 0.035f - _grab.ContactDistance(p);
                if (k > d) { d = k; at = p; }
            }
            return d;
        }

        /// <summary>How far a forearm (elbow to wrist, 3.5 cm thick) is inside what the hands hold (m).</summary>
        float ForearmDepth(Transform fore, Transform hand)
        {
            if (!fore || !hand) return 0f;
            float d = 0f;
            for (int i = 1; i <= 5; i++) d = Mathf.Max(d, 0.035f - _grab.ContactDistance(Vector3.Lerp(fore.position, hand.position, i / 5f)));
            return d;
        }

        readonly System.Collections.Generic.List<Renderer> _camArm = new System.Collections.Generic.List<Renderer>();
        bool _camArmShown;

        /// <summary>The camcorder arm drawn (the camcorder on its strap, the hand free) or only in the shadows (holding it).</summary>
        void SetCamArmShown(bool shown)
        {
            if (shown == _camArmShown) return;
            _camArmShown = shown;
            var mode = shown ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            foreach (var r in _camArm) if (r) r.shadowCastingMode = mode;
            if (_leftArm) foreach (var r in _leftArm.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = mode;
        }

        bool CamArmPart(string rendererName)
        {
            float side = _grab ? _grab.Side : 1f;
            return rendererName.EndsWith(side > 0f ? ".ArmL" : ".ArmR");
        }

        Vector3 _freeGrip;
        float _leftOut;                  // how far the left hand is kept out from its grip, off what it holds (m)
        float _freeW, _holdCurl = 0.3f;  // how far out to its grip (0 hanging, 1 on it), and its fingers' close there
        Vector3 _holdGrip;
        Quaternion _holdRot = Quaternion.identity;
        Quaternion _freeRot = Quaternion.identity;

        /// <summary>
        /// The left hand free of the camcorder: on the furniture's second grip (palm on its surface, fingers round it) while
        /// both hands hold it, else hanging at the side. Eased between the two so it reaches rather than jumps.
        /// </summary>
        void UpdateFreeLeftArm(Transform eye)
        {
            var r = _lRig;
            float side = _grab ? -_grab.Side : -1f;
            Quaternion yaw = transform.rotation;
            Vector3 shoulder = eye.position + yaw * new Vector3(0.2f * side, -0.28f, -0.08f);
            if (!r.root && !_leftArm) shoulder = CarryShoulder(r.clavicle, r.clavicleRest, r.upper, shoulder, 10f);
            // Hanging at the side, worked out every frame.
            Vector3 hang = shoulder + yaw * new Vector3(0.06f * side, -0.62f, 0.04f);
            Quaternion hangRot = Quaternion.LookRotation(Vector3.down, yaw * new Vector3(side, 0f, 0f));
            if (_grab.TwoHanded)
            {
                Vector3 n = _grab.SecondNormal.normalized;
                _holdGrip = _grab.SecondGrip + n * (0.012f + _leftOut);
                // Fingers the way the grip found fits the surface (PropGrabber.PlaceSecond), else away from the body along it;
                // the back of the hand out of it. Closed round an edge they hook over, nearly flat on a face.
                Vector3 fingers = Vector3.ProjectOnPlane(_grab.SecondFingers, n);
                if (fingers.sqrMagnitude < 1e-4f) fingers = Vector3.ProjectOnPlane(_holdGrip - shoulder, n);
                if (fingers.sqrMagnitude < 1e-4f) fingers = Vector3.ProjectOnPlane(yaw * Vector3.forward, n);
                _holdRot = Quaternion.LookRotation(fingers.normalized, n);
                _holdCurl = _grab.SecondCurl;
            }
            // Out to the grip and back as a blend between the two, both worked out this frame: on it, the hand is exactly
            // where it holds (round 15: eased after it every frame, it trailed behind into a carried box as you turned).
            _freeW = Mathf.MoveTowards(_freeW, _grab.TwoHanded ? 1f : 0f, Time.deltaTime / 0.25f);
            float sw = Mathf.SmoothStep(0f, 1f, _freeW);
            _freeGrip = Vector3.Lerp(hang, _holdGrip, sw);
            _freeRot = Quaternion.Slerp(hangRot, _holdRot, sw);
            float curl = Mathf.Lerp(0.3f, _holdCurl, sw);
            Vector3 wrist = _freeGrip - _freeRot * new Vector3(0f, -0.02f, 0.065f);
            Vector3 d = wrist - shoulder;
            float a = r.upperLen, b = r.foreLen, len = Mathf.Clamp(Gait.SoftReach(d.magnitude, a + b, (a + b) * 0.03f), Mathf.Abs(a - b) + 0.02f, (a + b) * 0.999f);
            Vector3 dir = d.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(yaw * new Vector3(0.6f * side, -1f, -0.2f), dir).normalized;
            float cosA = Mathf.Clamp((a * a + len * len - b * b) / (2f * a * len), -1f, 1f);
            Vector3 elbow = shoulder + dir * (cosA * a) + pole * (Mathf.Sqrt(1f - cosA * cosA) * a);
            Vector3 wristAt = shoulder + dir * len;
            if (_leftArm) _leftArm.SetPositionAndRotation(shoulder, Quaternion.identity);
            Vector3 handBack = _freeRot * Vector3.up;
            Vector3 hinge = -side * Vector3.Cross(elbow - shoulder, wristAt - elbow);
            Orient(r.upper, r.upperAxis, r.upperBack, elbow - shoulder, hinge.sqrMagnitude > 1e-8f ? hinge : -pole);
            Vector3 f = (wristAt - elbow).normalized;
            Vector3 thumb = Vector3.ProjectOnPlane(side * Vector3.Cross(_freeRot * Vector3.forward, handBack), f);
            Vector3 foreBack = thumb.sqrMagnitude > 1e-6f ? side * Vector3.Cross(thumb.normalized, f) : handBack;
            Orient(r.fore, r.foreAxis, r.foreBack, wristAt - elbow, foreBack);
            Orient(r.hand, r.handFingers, r.handBack, _freeRot * Vector3.forward, handBack);
            r.hand.position = wristAt;
            foreach (var j in r.joints) j.t.localRotation = j.rest * Quaternion.AngleAxis(curl * 62f * j.weight, j.axis);
            // Out of what it holds: kept out by as far as any finger joint or the wrist went in, then let back slowly, so it
            // rests on the surface (round 14: 3-7 cm into a box's side and an armchair's back; this arm has no finger contact).
            if (_grab.TwoHanded)
            {
                float deep = r.hand ? -_grab.ContactDistance(r.hand.position) : 0f;
                foreach (var j in r.joints) if (j.t) deep = Mathf.Max(deep, -_grab.ContactDistance(j.t.position));
                _leftOut = deep > 0.002f ? Mathf.Min(_leftOut + deep * 0.6f, 0.08f) : Mathf.MoveTowards(_leftOut, 0f, Time.deltaTime * 0.05f);
            }
            else _leftOut = 0f;
        }

        /// <summary>
        /// The left hand under the camcorder, palm up, holding it a little out in front of the face; the elbow down in front
        /// of the chest. The shoulder is the body's (from the eye), the grip the camcorder's (the camera, with its shake).
        /// </summary>
        void UpdateLeftArm(Transform eye, Transform cam)
        {
            var r = _lRig;
            float side = _grab ? -_grab.Side : -1f;
            Quaternion yaw = transform.rotation;
            Vector3 shoulder = eye.position + yaw * new Vector3(0.2f * side, -0.28f, -0.08f);
            // The body's arm: its own shoulder, the collarbone bringing it up and forward toward the camcorder a little.
            if (!r.root && !_leftArm) shoulder = CarryShoulder(r.clavicle, r.clavicleRest, r.upper, shoulder, 20f);
            Quaternion handRot = Quaternion.LookRotation(cam.forward * 0.85f - cam.right * side * 0.35f, -cam.up * 0.9f + cam.right * side * 0.3f);
            Vector3 grip = cam.position + cam.rotation * new Vector3(0.0f, -0.075f, 0.08f);
            // Sent off the camcorder for a moment by the held item (LeftReach): there and back as a blend of the two.
            if (Time.time - _leftReachSet > 0.05f) leftReachW = Mathf.MoveTowards(leftReachW, 0f, Time.deltaTime / 0.2f);
            float reachW = Mathf.SmoothStep(0f, 1f, leftReachW);
            if (reachW > 0f)
            {
                grip = Vector3.Lerp(grip, leftReachAt, reachW);
                handRot = Quaternion.Slerp(handRot, leftReachRot, reachW);
            }
            Vector3 wrist = grip - handRot * new Vector3(0f, -0.02f, 0.065f);
            Vector3 d = wrist - shoulder;
            // (Soft IK: going up stairs the body's height trails the eye's and the arm comes near straight; eased, it
            // does not snap.)
            float a = r.upperLen, b = r.foreLen, len = Mathf.Clamp(Gait.SoftReach(d.magnitude, a + b, (a + b) * 0.03f), Mathf.Abs(a - b) + 0.02f, (a + b) * 0.999f);
            Vector3 dir = d.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(yaw * new Vector3(0.3f * side, -1f, 0.35f), dir).normalized;
            float cosA = Mathf.Clamp((a * a + len * len - b * b) / (2f * a * len), -1f, 1f);
            Vector3 elbow = shoulder + dir * (cosA * a) + pole * (Mathf.Sqrt(1f - cosA * cosA) * a);
            Vector3 wristAt = shoulder + dir * len;
            if (_leftArm) _leftArm.SetPositionAndRotation(shoulder, Quaternion.identity);
            Vector3 handBack = handRot * Vector3.up;
            // The upper arm's back on the elbow's hinge (as the working arm's).
            Vector3 hinge = -side * Vector3.Cross(elbow - shoulder, wristAt - elbow);
            Orient(r.upper, r.upperAxis, r.upperBack, elbow - shoulder, hinge.sqrMagnitude > 1e-8f ? hinge : -pole);
            // The forearm's roll from where the thumb points (across the forearm, always well defined), not from the back
            // of the hand: palm up under the camcorder the back of the hand lies nearly along the forearm, and its roll
            // flipped (the arm's bones turned up to 2400 degrees a second).
            Vector3 f = (wristAt - elbow).normalized;
            Vector3 thumb = Vector3.ProjectOnPlane(side * Vector3.Cross(handRot * Vector3.forward, handBack), f);
            Vector3 foreBack = thumb.sqrMagnitude > 1e-6f ? side * Vector3.Cross(thumb.normalized, f) : handBack;
            Orient(r.fore, r.foreAxis, r.foreBack, wristAt - elbow, foreBack);
            Orient(r.hand, r.handFingers, r.handBack, handRot * Vector3.forward, handBack);
            r.hand.position = wristAt;
            if (reachW > 0.5f) foreach (var j in r.joints) j.t.localRotation = j.rest * Quaternion.AngleAxis(leftReachCurl * 62f * j.weight, j.axis);
            else CloseOnCamcorder(r, cam);
        }

        /// <summary>
        /// A held item sends the camcorder hand somewhere for a moment (the hammer: a nail held to the wall between finger and
        /// thumb): where the palm goes, turned how (fingers forward, back of the hand up), how far the fingers close, and how
        /// far it is there (0: on the camcorder, 1: there). Set every frame while it is wanted; it eases back by itself.
        /// </summary>
        [System.NonSerialized] public Vector3 leftReachAt;
        [System.NonSerialized] public Quaternion leftReachRot = Quaternion.identity;
        [System.NonSerialized] public float leftReachCurl = 0.5f, leftReachW;
        float _leftReachSet;

        /// <summary>Sends the camcorder hand to a palm point and turn this frame, w of the way there (see <see cref="leftReachAt"/>).</summary>
        public void LeftReach(Vector3 at, Quaternion rot, float curl, float w)
        {
            leftReachAt = at; leftReachRot = rot; leftReachCurl = curl; leftReachW = Mathf.Clamp01(w); _leftReachSet = Time.time;
        }

        static readonly Vector3 CamBodyCentre = new Vector3(-0.004f, -0.022f, 0.075f), CamBodyHalf = new Vector3(0.036f, 0.041f, 0.095f);

        /// <summary>
        /// The camcorder hand's fingers round the camcorder's body (MakeCamcorder's box, at the camera) from under it: each
        /// finger's joints bend together until a joint or its tip comes within half a finger (8 mm) of the body, at most a
        /// loose fist. (All curled to 0.85 regardless, the shadow showed a fist closed on nothing under the camcorder.)
        /// </summary>
        void CloseOnCamcorder(ArmRig r, Transform cam)
        {
            var js = r.joints;
            int i0 = 0;
            while (i0 < js.Count)
            {
                int i1 = i0;
                while (i1 + 1 < js.Count && js[i1 + 1].finger == js[i0].finger) i1++;
                float done = 0f;
                bool met = false;
                for (int s = 1; s <= 12; s++)
                {
                    float k = s / 12f * 0.85f;
                    for (int j = i0; j <= i1; j++) js[j].t.localRotation = js[j].rest * Quaternion.AngleAxis(k * 62f * js[j].weight, js[j].axis);
                    float near = float.MaxValue;
                    for (int j = i0; j <= i1; j++)
                    {
                        Vector3 p = js[j].t.position;
                        near = Mathf.Min(near, WorldItem.BoxDistance(cam.InverseTransformPoint(p), CamBodyCentre, CamBodyHalf));
                        if (j == i1)
                        {
                            Vector3 prev = j > i0 ? js[j - 1].t.position : js[j].t.parent.position;
                            Vector3 tip = p + (p - prev) * 0.9f;
                            near = Mathf.Min(near, WorldItem.BoxDistance(cam.InverseTransformPoint(tip), CamBodyCentre, CamBodyHalf));
                        }
                    }
                    if (near < FingerRadius) { met = true; break; }
                    done = k;
                }
                // A finger past the camcorder's side that meets nothing stays loosely curled (curled to 0.85 in the air, two
                // fingers made a fist beside it).
                if (!met) done = Mathf.Min(done, 0.45f);
                for (int j = i0; j <= i1; j++) js[j].t.localRotation = js[j].rest * Quaternion.AngleAxis(done * 62f * js[j].weight, js[j].axis);
                i0 = i1 + 1;
            }
        }

        // ---------------- The right hand at rest: hanging, swinging, or on the world ----------------

        Vector3 _armSwing, _armSwingVel;
        float _touchW, _touchWVel, _touchUntil, _nextTouchScan;
        bool _touching, _touchRail;
        Vector3 _touchPoint, _touchNormal, _touchAlong;
        readonly RaycastHit[] _touchHits = new RaycastHit[6];

        /// <summary>
        /// Where the free hand is (the Body and IK layers): hanging at the side, swinging opposite its leg as you walk (a
        /// damped pendulum, so it trails starts and stops), or laid on a wall or round a rail within reach when you lean
        /// round a corner or stand close by.
        /// </summary>
        void RestPose(Vector3 shoulder, Quaternion yaw, float side, out Vector3 wrist, out Quaternion hand, out float curl)
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 fwd = yaw * Vector3.forward, right = yaw * Vector3.right;
            // Swing: opposite the leg on the same side (its foot's lead), and a lag to the body's acceleration.
            float lead = 0f;
            if (_legA != null && _legB != null)
            {
                var same = Mathf.Sign(_legA.side) == Mathf.Sign(side * _sideSign) ? _legA : _legB;
                var other = same == _legA ? _legB : _legA;
                lead = Vector3.Dot(FootAt(other) - FootAt(same), fwd) * 0.32f;
            }
            // Running the arms are held bent (elbows near 90 degrees) and swing further.
            float runArms = Mathf.InverseLerp(1.9f, 3.2f, _vel.magnitude);
            // (About 30 cm front to back walking, as measured on people; the arms bend running rather than swing wider.)
            Vector3 want = fwd * Mathf.Clamp(lead, -0.17f, 0.17f) * _walkAmount - _vel * 0.015f;
            _armSwing = Springs.Oscillator(_armSwing, ref _armSwingVel, want, Mathf.Lerp(1.7f, 2.4f, runArms), 0.45f, dt);
            // The elbow bends as the arm swings forward (about 30 degrees across a walking swing, from gait data) and
            // straightens behind.
            float ahead = Vector3.Dot(_armSwing, fwd);
            Vector3 hangLocal = Vector3.Lerp(new Vector3(0.065f * side, -0.55f, 0.03f), new Vector3(0.1f * side, -0.38f, 0.17f), runArms);
            hangLocal.y += Mathf.Max(0f, ahead) * 0.32f;
            Vector3 hang = shoulder + yaw * hangLocal + _armSwing;
            Quaternion hangRot = Quaternion.LookRotation(Vector3.Lerp(Vector3.down * 0.95f + fwd * 0.12f, fwd * 0.7f + Vector3.down * 0.55f - right * side * 0.2f, runArms)
                                                         + _armSwing * 0.6f, right * side);
            float hangCurl = Mathf.Lerp(0.25f, 0.75f, runArms);

            UpdateTouch(shoulder, yaw, side);
            _touchW = Springs.Damper(_touchW, ref _touchWVel, _touching ? 1f : 0f, 0.09f, dt);
            if (_touchW < 0.002f && !_touching) { wrist = hang; hand = hangRot; curl = hangCurl; return; }
            Vector3 tw; Quaternion tr; float tc;
            if (_touchRail)
            {
                // Round the rail: palm on top, fingers over and under it (they stop on it: TouchDistance).
                Vector3 across = Vector3.ProjectOnPlane(Vector3.Cross(Vector3.up, _touchAlong), Vector3.up).normalized;
                if (Vector3.Dot(across, _touchPoint - shoulder) < 0f) across = -across;
                // The palm on the top of the rail, over its middle (it was past the far edge, 3 cm off it).
                Vector3 top = _touchPoint + across * Vector3.Dot(_touchRailCentre - _touchPoint, across);
                // The rail across the palm on a slant, as a hand takes a handrail, turned as the arm holds it best (picked
                // once per touch). (Square across it, the forearm was turned 93 degrees, its limit 95, and the wrist bent 27
                // to the side, its limit 28.)
                if (!_touchFingersSet) { _touchFingers = RailFingers(top, across); _touchFingersSet = true; }
                Vector3 fingers = _touchFingers;
                tr = Quaternion.LookRotation(fingers, Vector3.up);
                tw = top + Vector3.up * 0.003f - tr * _wristToGrip;
                // Enough to wrap the far edge; a finger past the rail that meets nothing stays relaxed (curled to a fist,
                // the little finger hung in the air beside the rail).
                tc = 0.75f;
            }
            else
            {
                // Flat on the wall: palm to it, the fingers carrying on the forearm's line along the wall and a little up,
                // so the wrist only bends back (it was asked for fingers straight up: past what a wrist turns, the hand
                // ended 74 degrees off the wall and the fingers in it).
                Vector3 along = Vector3.ProjectOnPlane(_touchPoint - shoulder, _touchNormal);
                if (along.sqrMagnitude < 1e-4f) along = Vector3.ProjectOnPlane(fwd, _touchNormal);
                Vector3 fingers = Vector3.ProjectOnPlane(along.normalized + Vector3.up * 0.35f, _touchNormal).normalized;
                // Turned round the wall's normal as the arm lays a hand flat best (picked once per touch: ReachableFingers).
                // (Along the reach and a little up, the forearm was turned 96 degrees, past its 95: the hand could not lie
                // flat, one edge touched and the middle of the palm stood 17 mm off the wall.)
                if (_touchFingersSet) fingers = _touchFingers;
                else { _touchFingers = fingers = ReachableFingers(_touchPoint, _touchNormal, fingers); _touchFingersSet = true; }
                tr = Quaternion.LookRotation(fingers, _touchNormal);
                tw = _touchPoint + _touchNormal * 0.02f - fingers * 0.055f;
                tc = 0.12f;
            }
            float s = Mathf.SmoothStep(0f, 1f, _touchW);
            wrist = Vector3.Lerp(hang, tw, s);
            hand = Quaternion.Slerp(hangRot, tr, s);
            curl = Mathf.Lerp(hangCurl, tc, s);
        }

        Vector3 _touchRailCentre, _touchFingers;
        bool _touchFingersSet;
        /// <summary>Tests: the rail hand's turn from square across the rail (degrees about the vertical) instead of the arm's choice.</summary>
        public static float? TestRailTurn;

        /// <summary>
        /// The fingers' way across a rail (palm down on its top at palm): square across it or turned up to 40 degrees about
        /// the vertical toward the way the body faces, where the arm holds it with the least strain (the solver's score, which
        /// charges extra past 70% of a limit) and the forearm clear of the rail, a little in favour of square.
        /// </summary>
        Vector3 RailFingers(Vector3 palm, Vector3 across)
        {
            if (TestRailTurn.HasValue) return Quaternion.AngleAxis(TestRailTurn.Value, Vector3.up) * across;
            Vector3 best = across; float bestS = float.MaxValue;
            Vector3 shoulder = ShoulderNow;
            Quaternion yaw = transform.rotation;
            float side = _grab ? _grab.Side : 1f;
            Vector3 fwd = transform.forward;
            for (int a = -60; a <= 60; a += 5)
            {
                Vector3 f = Quaternion.AngleAxis(a, Vector3.up) * across;
                // Only toward the way the body faces (turned back, the forearm was turned past its limit the other way and
                // the index finger went into the rail). (Round 7: up to 60 degrees: held to 40, the forearm was turned 96
                // degrees, past its 95, and the wrist 26 to the side; at 75 the hand lay along the rail's top.)
                if (Vector3.Dot(f - across, fwd) < -1e-3f) continue;
                Quaternion hand = Quaternion.LookRotation(f, Vector3.up);
                Vector3 wrist = palm - hand * _wristToGrip;
                Vector3 dir0 = (wrist - shoulder).normalized;
                Vector3 pole0 = Vector3.ProjectOnPlane(yaw * new Vector3(0.8f * side, -1f, -0.25f), dir0).normalized;
                float s = float.MaxValue; ArmPose bp = default;
                for (int k = -8; k <= 10; k++)
                {
                    var p = SolveArm(shoulder, wrist, hand, pole0, k * 15f, side, yaw);
                    if (p.score < s) { s = p.score; bp = p; }
                }
                // The forearm turned no more than about 75 degrees (a hand on a rail beside you lies along it at a slant,
                // not straight across it), a slant of about 50 degrees liked best.
                s += Mathf.Max(0f, Mathf.Abs(bp.twist) - 0.7f * forearmTwist) * 0.03f + Mathf.Max(0f, Mathf.Abs(bp.twist) - 75f) * 0.15f
                     + Mathf.Abs(Mathf.Abs(a) - 52f) * 0.004f;
                // Not with the forearm lying on the rail (turned 60 degrees, it lay along the top, 6 mm into it): the
                // forearm's underside (3 cm from its middle near the wrist, and a little more) clear of it.
                for (int t = 1; t <= 4; t++)
                {
                    Vector3 q = Vector3.Lerp(bp.wrist, bp.elbow, t * 0.1f);
                    s += Mathf.Max(0f, 0.035f - TouchDistance(q)) * 30f;
                }
                if (s < bestS) { bestS = s; best = f; }
            }
            return best;
        }
        readonly Collider[] _touchColArr = new Collider[1];

        /// <summary>
        /// The touch layer's one reach (m, shoulder to palm): a touch is found within it and kept within it and a little
        /// more. (Found up to 0.543 m away and dropped past 0.532, a wall lean was dropped and found again every 0.12 s and
        /// the hand never got there.)
        /// </summary>
        public static float TouchReachOf(float upperLen, float foreLen) => (upperLen + foreLen) * 0.95f;
        public const float TouchKeepMargin = 0.04f;
        public static bool TouchKept(float dist, float reach) => dist <= reach + TouchKeepMargin;
        float _touchRailR = 0.02f;

        /// <summary>
        /// Where on a wall the free hand rests: ahead of the shoulder along the wall about as far as the shoulder is from it
        /// (so the forearm meets the wall at about 45 degrees and the wrist bends back only as far as a wrist does), between
        /// waist and chest height, within the arm's reach, and only where the wall really is. (The first point the rays hit
        /// was often right beside the shoulder: a palm flat there is out of a wrist's reach.)
        /// </summary>
        Vector3 ComfortableTouch(RaycastHit hit, Vector3 shoulder, Vector3 fwd, Vector3 outward)
        {
            Vector3 n = hit.normal, P = hit.point;
            float dWall = Vector3.Dot(shoulder - P, n);
            Vector3 foot = shoulder - n * dWall;
            Vector3 t = Vector3.ProjectOnPlane(fwd, n); t.y = 0f;
            if (t.sqrMagnitude < 0.04f) { t = Vector3.ProjectOnPlane(outward, n); t.y = 0f; }      // a wall ahead: out to the side
            if (t.sqrMagnitude < 1e-4f) return P;
            t.Normalize();
            float along = Mathf.Max(Vector3.Dot(P - foot, t), dWall * 0.95f + 0.06f);
            float y = Mathf.Clamp(P.y, shoulder.y - 0.45f, shoulder.y - 0.12f);
            float reachTo = TouchReachOf(_upperLen, _foreLen);
            Vector3 Q = foot + t * along; Q.y = y;
            for (int k = 0; k < 6 && (Q - shoulder).magnitude > reachTo; k++) { along *= 0.85f; Q = foot + t * along; Q.y = y; }
            // The wall must be there (not past its end or round a corner).
            if (Physics.Raycast(Q + n * 0.15f, -n, out var h, 0.3f, ~0, QueryTriggerInteraction.Ignore) && !h.collider.transform.IsChildOf(transform)
                && Vector3.Dot(h.normal, n) > 0.9f && Mathf.Abs(Vector3.Dot(h.point - P, n)) < 0.03f)
                return h.point;
            return P;
        }

        /// <summary>The surface the body's touch layer rests the hand on (the wall's plane, or round the rail), for the fingers.</summary>
        float TouchDistance(Vector3 p)
        {
            // A rail that is a box: its own shape (a round one stood in for it: the fingers went down inside its far face).
            if (_touchRail && _touchColArr[0] is BoxCollider) return PropGrabber.CollidersDistance(_touchColArr, p);
            if (_touchRail)
            {
                Vector3 a = _touchAlong.normalized, d = p - _touchRailCentre;
                return (d - a * Vector3.Dot(d, a)).magnitude - _touchRailR;
            }
            return Vector3.Dot(p - _touchPoint, _touchNormal);
        }

        float _sideSign = 1f;
        float _still;     // which way a leg's "side" points relative to the arm's (measured: the model's x may be mirrored)

        void UpdateTouch(Vector3 shoulder, Quaternion yaw, float side)
        {
            bool allowed = _grab && _grab.HandBlend < 0.05f && _player.IsGrounded && !_player.noclip
                           && _player.CurrentStance != CamcorderPlayerController.Stance.Crawl && _vel.magnitude < 1.7f;
            Vector3 fwd = yaw * Vector3.forward, right = yaw * Vector3.right;
            if (_touching)
            {
                Vector3 to = _touchPoint - shoulder;
                bool keep = allowed && TouchKept(to.magnitude, TouchReachOf(_upperLen, _foreLen)) && Vector3.Dot(to, fwd) > -0.12f
                            && (Time.time < _touchUntil || Mathf.Abs(_player.Lean) > 0.2f || _vel.magnitude < 0.3f)
                            && !TouchMoved();
                if (!keep) _touching = false;
                return;
            }
            _still = _vel.magnitude < 0.1f ? _still + Time.deltaTime : 0f;
            if (!allowed || Time.time < _nextTouchScan) return;
            _nextTouchScan = Time.time + 0.12f;
            // When a hand goes out: leaning round a corner; walking slowly right beside a wall (trailing along it, now and
            // then); or having stood close to one for a moment. Not just for being near a wall.
            bool leaning = Mathf.Abs(_player.Lean) > 0.3f;
            float speed = _vel.magnitude;
            bool trailing = speed > 0.15f && speed < 0.9f, resting = _still > 1.5f;
            if (!leaning && !trailing && !resting) return;
            Vector3 from = shoulder - Vector3.up * 0.18f;
            Vector3[] dirs = leaning ? new[] { right * side, (right * side + fwd * 0.8f).normalized, (right * side * 0.35f + fwd).normalized, (fwd - Vector3.up * 0.35f).normalized }
                                     : new[] { right * side, (right * side + fwd * 0.5f).normalized };
            float reach = leaning ? 0.6f : 0.42f;
            foreach (var d in dirs)
            {
                int n = Physics.RaycastNonAlloc(from, d, _touchHits, reach, ~0, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue; RaycastHit hit = default;
                for (int i = 0; i < n; i++)
                {
                    var h = _touchHits[i];
                    if (h.distance >= best || h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<WorldItem>() || Swings(h.collider)) continue;
                    best = h.distance; hit = h;
                }
                if (best == float.MaxValue) continue;
                var bounds = hit.collider.bounds;
                bool rail = Mathf.Min(bounds.size.x, bounds.size.z) < 0.09f && bounds.size.y < 0.14f && Mathf.Max(bounds.size.x, bounds.size.z) > 0.4f;
                if (!rail && Mathf.Abs(hit.normal.y) > 0.35f) continue;
                // Beside a wall: now and then (a few seconds on, a few off); leaning round a corner: yes.
                if (!leaning && Mathf.PerlinNoise(Time.time * 0.13f, side) < (trailing ? 0.6f : 0.65f)) return;
                if (rail && (new Vector3(hit.point.x, bounds.max.y, hit.point.z) - shoulder).magnitude > TouchReachOf(_upperLen, _foreLen)) continue;
                _touchColArr[0] = hit.collider;
                _touching = true; _touchRail = rail;
                _touchPoint = rail ? new Vector3(hit.point.x, bounds.max.y, hit.point.z) : hit.point;
                _touchNormal = hit.normal;
                _touchAlong = bounds.size.x > bounds.size.z ? Vector3.right : Vector3.forward;
                _touchRailCentre = bounds.center;
                _touchRailR = Mathf.Max(0.008f, Mathf.Min(Mathf.Min(bounds.size.x, bounds.size.z), bounds.size.y) * 0.5f);
                if (!rail) _touchPoint = ComfortableTouch(hit, shoulder, fwd, right * side);
                _touchUntil = Time.time + 0.8f + Random.value * 1.5f;
                _touchFingersSet = false;
                _touchOnCol = hit.collider.transform.InverseTransformPoint(_touchPoint);
                return;
            }
        }

        Vector3 _touchOnCol;

        /// <summary>
        /// Something that swings or moves: a door's leaf (its kinematic body) or a loose prop. Not leant on. (A lean picked
        /// a door's leaf and the hand stayed planted while it swung from 2 to 87 degrees, the fingertips through it.)
        /// </summary>
        public static bool Swings(Collider c)
        {
            var rb = c.attachedRigidbody;
            return rb && (!rb.isKinematic || c.GetComponentInParent<Door>());
        }

        /// <summary>What the hand rests on has moved more than a centimetre under it since it was touched: let go.</summary>
        bool TouchMoved()
        {
            var c = _touchColArr[0];
            if (!c) return true;
            return (c.transform.TransformPoint(_touchOnCol) - _touchPoint).sqrMagnitude > 0.01f * 0.01f;
        }

        // ---------------- Knocks ----------------

        Vector2 _bumpTorso, _bumpTorsoVel;           // degrees: forward / sideways
        Vector3 _bumpHips, _bumpHipsVel;             // metres
        float _stumble;

        void OnBumped(Vector3 shove, Vector3 at) => Bump(shove * 0.6f, at);

        /// <summary>
        /// A knock (world velocity change, m/s) at a point: the struck part is shoved (the torso up high, the hips and legs
        /// low, where a low knock also makes a foot step to catch it), the view jolts, the feet slide a little, and springs
        /// pull the body back to its pose. Never a fall.
        /// </summary>
        public void Bump(Vector3 shove, Vector3 at)
        {
            Vector3 local = Quaternion.Inverse(transform.rotation) * shove;
            float high = Mathf.InverseLerp(0.5f, 1.4f, at.y - transform.position.y);
            _bumpTorsoVel += new Vector2(local.z, -local.x) * (160f * Mathf.Lerp(0.4f, 1f, high));
            _bumpHipsVel += new Vector3(shove.x, 0f, shove.z) * Mathf.Lerp(0.45f, 0.15f, high);
            if (high < 0.5f) _stumble = 0.4f;
            _player.Push(shove * 0.2f);
            var shake = FindAnyObjectByType<HandheldCameraShake>();
            if (shake) shake.Jolt(new Vector3(-local.z * 22f, local.x * 14f, -local.x * 18f));
        }

        void UpdateKnocks(float dt)
        {
            _bumpTorso.x = Springs.Oscillator(_bumpTorso.x, ref _bumpTorsoVel.x, 0f, 2.2f, 0.45f, dt);
            _bumpTorso.y = Springs.Oscillator(_bumpTorso.y, ref _bumpTorsoVel.y, 0f, 2.2f, 0.45f, dt);
            _bumpHips = Springs.Oscillator(_bumpHips, ref _bumpHipsVel, Vector3.zero, 2.6f, 0.5f, dt);
            _bumpTorso = Vector2.ClampMagnitude(_bumpTorso, 18f);
            _bumpHips = Vector3.ClampMagnitude(_bumpHips, 0.08f);
            _stumble = Mathf.Max(0f, _stumble - dt);
            // A hard landing: the knees give and spring back.
            if (_player.LandedThisFrame > 2f) _hipsYVel -= Mathf.Min(1.6f, (_player.LandedThisFrame - 2f) * 0.35f + 0.4f);
        }

        // ---------------- Legs ----------------

        float _legYaw, _legYawVel, _hipsYOff, _hipsYVel, _leanS, _leanSVel, _airVel;
        bool _yawSet, _turnInPlace;
        // The lower body's own heading (the hips and feet): it turns with the view while walking, and standing still only
        // once the view has gone well round (then the feet step round); in between the torso and neck twist to the view.
        Vector3 _legFwd = Vector3.forward, _legRight = Vector3.right;
        // The body's height, eased over a stair's steps (the controller drops or climbs a whole step in one frame).
        float _rootY, _rootYVel, _offGround, _lastRootY;
        bool _rootYSet;

        /// <summary>
        /// The ground where a foot should land near t: a stair tread is found (where the floor steps up or down within a
        /// tread's depth) and the foot put on it whole, heel and toe clear of its edges, instead of half over the nosing.
        /// </summary>
        // How far the shoe reaches behind and ahead of the ankle (measured on the shoes, MeasureHeels), for stair treads.
        float _treadBack = 0.1f, _treadFront = 0.12f;

        Vector3 OnTread(Vector3 t, Vector3 dir, float fallback)
        {
            float y0 = GroundY(t, fallback);
            float yA = GroundY(t + dir * _treadFront, fallback), yB = GroundY(t - dir * _treadBack, fallback);
            t.y = y0;
            if (Mathf.Abs(yA - y0) < 0.025f && Mathf.Abs(yB - y0) < 0.025f) return t;
            float back = 0f, front = 0f;
            for (int k = 1; k <= 14; k++) { if (Mathf.Abs(GroundY(t - dir * (k * 0.025f), fallback) - y0) > 0.025f) break; back = k * 0.025f; }
            for (int k = 1; k <= 14; k++) { if (Mathf.Abs(GroundY(t + dir * (k * 0.025f), fallback) - y0) > 0.025f) break; front = k * 0.025f; }
            float depth = back + front;
            // The whole shoe on the tread, heel clear of the nosing behind and the toe clear of the riser ahead. A tread
            // shorter than the shoe: going up, the toe clear of the riser and the heel over the nosing behind; going down,
            // the heel on the tread and the toes over the nosing (as people do: never the toe into the riser).
            float shift;
            if (depth >= _treadBack + _treadFront) shift = Mathf.Clamp(0f, _treadBack - back, front - _treadFront);
            else
            {
                bool up = GroundY(t + dir * (front + 0.05f), fallback) > y0 + 0.025f;
                shift = up ? front - _treadFront : _treadBack - back;
            }
            return t + dir * shift;
        }

        /// <summary>
        /// On stairs a foot goes one tread at a time: if the way from 'from' to 'to' crosses more than one change in floor
        /// height, the landing is pulled back onto the tread after the first (a walking stride would otherwise skip every
        /// other tread going down, loping like a puppet). Returns the landing and how far along it is.
        /// </summary>
        Vector3 OneTread(Vector3 from, Vector3 to, float fallback, out float cap)
        {
            cap = float.MaxValue;
            Vector3 d = to - from; d.y = 0f;
            float len = d.magnitude;
            if (len < 0.15f) return to;
            d /= len;
            float lastY = GroundY(from, fallback), firstEdge = -1f;
            for (float s = 0.04f; s < len + 0.02f; s += 0.04f)
            {
                float y = GroundY(from + d * s, fallback);
                if (Mathf.Abs(y - lastY) < 0.06f) continue;
                lastY = y;
                if (firstEdge < 0f) { firstEdge = s; continue; }
                // A second edge before the landing: stop on the tread between them.
                cap = Mathf.Max(firstEdge + 0.1f, s - 0.13f);
                return OnTread(from + d * cap, d, fallback);
            }
            return to;
        }

        void UpdateBody()
        {
            if (!_body) return;
            float eye = _player.Eye;
            // (Bending down for something low keeps the body, and its own arm: the separate arm used to be swapped in
            // mid-grab, a grey sleeve and another hand.)
            bool visible = !_player.noclip && (eye > HideBelowEye
                           || (_player.stoop > 0.05f && _player.CurrentStance != CamcorderPlayerController.Stance.Crawl));
            if (_body.gameObject.activeSelf != visible)
            {
                _body.gameObject.SetActive(visible);
                if (_legA != null) _legA.placed = false;
                if (_legB != null) _legB.placed = false;
            }
            if (!visible) { _yawSet = false; _rootYSet = false; return; }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 up = transform.up;
            UpdateKnocks(dt);

            // The body's motion, smoothed a little (a step is planned from it).
            Vector3 v = _player.PlanarVelocity; v.y = 0f;
            _vel = Springs.Damper(_vel, ref _velVel, v, 0.05f, dt);
            float speed = _vel.magnitude;
            bool grounded = _player.IsGrounded;
            // Off the ground for real (a jump, a fall), not the moment the controller leaves a stair's edge or a curb
            // before it settles on the next: only then do the legs let go of the floor and hang.
            float rootNow = transform.position.y;
            float climb = _rootYSet ? (rootNow - _lastRootY) / dt : 0f;
            _lastRootY = rootNow;
            _offGround = grounded ? 0f : _offGround + dt;
            bool airborne = !grounded && (_offGround > 0.14f || climb > 0.8f);
            _air = Mathf.Clamp01(Springs.Damper(_air, ref _airVel, airborne ? 1f : 0f, 0.05f, dt));
            _walkAmount = Mathf.MoveTowards(_walkAmount, !airborne ? Mathf.Clamp01(speed / 0.5f) : 0f, dt * 3f);

            // The lower body's heading. Walking: it follows the view (a moment behind). Standing: it stays put while you
            // look around (the torso and neck take the turn) until the view is 40 degrees round, then it turns to it and
            // the feet step round with it.
            float yawNow = transform.eulerAngles.y;
            if (!_yawSet) { _legYaw = yawNow; _legYawVel = 0f; _turnInPlace = false; _yawSet = true; }
            float behindView = Mathf.DeltaAngle(_legYaw, yawNow);
            bool walkingLegs = speed > 0.2f || airborne;
            if (!walkingLegs && Mathf.Abs(behindView) > 40f) _turnInPlace = true;
            if (_turnInPlace && (Mathf.Abs(behindView) < 3f || walkingLegs)) _turnInPlace = false;
            float legTarget = walkingLegs || _turnInPlace ? yawNow : _legYaw;
            _legYaw = Springs.DamperAngle(_legYaw, ref _legYawVel, legTarget, walkingLegs ? 0.1f : 0.12f, dt);
            float lag = Mathf.DeltaAngle(yawNow, _legYaw);
            if (Mathf.Abs(lag) > 65f) { lag = Mathf.Sign(lag) * 65f; _legYaw = yawNow + lag; }
            Quaternion legRot = Quaternion.Euler(0f, _legYaw, 0f);
            Vector3 fwd = legRot * Vector3.forward, right = legRot * Vector3.right;
            _legFwd = fwd; _legRight = right;
            _moveLocal = new Vector2(Vector3.Dot(_vel, right), Vector3.Dot(_vel, fwd));

            // How far the eye is below standing: leaning the torso forward gives some, the knees the rest.
            float low = Mathf.Clamp01((_player.standEye - eye) / Mathf.Max(0.1f, _player.standEye - _player.crouchEye));
            float bend = Mathf.Clamp(_player.stoop, 0f, 0.8f) * 55f + low * 30f;
            // (What the bend itself brings the shoulder down and forward: measured on the rig, MeasureBend.)
            float torsoDrop = _bendMeasured ? BendTable(_bendDrop, Mathf.Min(bend, 80f))
                                            : TorsoLength * (1f - Mathf.Cos(Mathf.Min(bend, 80f) * Mathf.Deg2Rad));
            float drop = Mathf.Clamp(_player.standEye - eye - torsoDrop, 0f, 0.5f);
            float squat = drop / 0.5f;
            // Bending forward from the hips over a desk or a board (PropGrabber.ReachHinge): the hips stay and the body comes
            // a little forward under the view, so the shoulder and the head go out over it.
            float hinge = _grab ? Mathf.Clamp(_grab.ReachHinge, 0f, 80f) : 0f;
            // The body stands behind the eye along the torso's heading (between the view's and the legs'), at a height
            // eased over steps: going down stairs the hips sink smoothly instead of dropping a whole step at a time.
            {
                // (Measured on the rig, the shoulder keeps its place under the eye however far the torso bends; the
                // squat's hips go back over the heels, below, and the body comes forward as much.)
                float backBy = bodyBack - HingeShift(hinge) + (_bendMeasured ? BendTable(_bendAhead, Mathf.Min(bend, 80f)) - squat * 0.12f
                                                         : TorsoLength * 0.9f * Mathf.Sin(Mathf.Min(bend, 80f) * Mathf.Deg2Rad));
                Vector3 local = Quaternion.Euler(0f, 0.25f * lag, 0f) * new Vector3(0f, 0f, -backBy);
                Vector3 world = _body.parent ? _body.parent.TransformPoint(local) : local;
                if (!_rootYSet || Mathf.Abs(world.y - _rootY) > 0.6f) { _rootY = world.y; _rootYVel = 0f; _rootYSet = true; }
                _rootY = Springs.Damper(_rootY, ref _rootYVel, world.y, airborne ? 0.015f : 0.06f, dt);
                _rootY = Mathf.Clamp(_rootY, world.y - 0.18f, world.y + 0.18f);
                world.y = _rootY;
                _body.position = world;
            }

            // A gait for the speed from human gait data (Motion/Gait.cs): steps a second rise with speed (about 113 a minute
            // walking at 1.35 m/s, steps of 0.72 m; 165 running at 3.6 m/s, steps of 1.3 m), a foot is on the ground 60 %
            // of the cycle walking and under 40 % running. For the direction: side-steps short and quick (a leg reaches
            // little sideways: about 4 a second, 0.35 m, at walking speed), backward steps shorter, the stance a little
            // wider sideways; shorter, quicker steps crouched. The step's length is always the speed over the cadence.
            float sideways = speed > 0.05f ? Mathf.Abs(_moveLocal.x) / speed : 0f;
            float backward = speed > 0.05f ? Mathf.Clamp01(-_moveLocal.y / speed) : 0f;
            float run = Gait.RunAmount(speed);
            float cadence = Gait.Cadence(speed, run) * Mathf.Lerp(1f, 2.1f, sideways) * Mathf.Lerp(1f, 1.25f, backward) * (1f + 0.15f * squat);
            float step = speed > 0.05f ? Mathf.Clamp(speed / cadence, 0.15f, 1.45f) : 0.3f;
            float stanceShare = Gait.StanceShare(speed, run);
            float cycle = 2f / cadence;
            float swingTime = Mathf.Clamp((1f - stanceShare) * cycle, 0.2f, 0.48f) * Mathf.Lerp(1f, 0.85f, sideways);
            // Where a foot lands ahead of the hip: a little short of half the stance's travel (a heel far out in front made
            // the hips drop 9 cm at every step; people's hips rise and fall about 4-5 cm).
            float reachOut = Mathf.Min(stanceShare * step * 0.88f, 0.5f) * Mathf.SmoothStep(0f, 1f, speed / 0.7f) * Mathf.Lerp(1f, 0.55f, sideways);
            Vector3 vdir = speed > 0.05f ? _vel / speed : fwd;
            float wide = 0.025f * sideways;
            // The foot's tilt at toe-off, heel up, rolling about the ball of the foot (steeper running), and at a heel
            // strike, toes up, about the heel (Perry's rockers); how long the landing's tilt takes to roll out.
            float toeOffPitch = Mathf.Lerp(38f, 50f, run) * (1f - sideways) * (1f - backward);
            float heelStrike = Mathf.Lerp(-14f, -8f, run);
            float rollOut = Mathf.Lerp(0.11f, 0.06f, run);

            Vector3 HomeOf(Leg g) => Home(g, squat) + right * (Mathf.Sign(g.side) * wide);

            foreach (var g in new[] { _legA, _legB })
            {
                if (g == null) continue;
                var home = HomeOf(g);
                // Far from where it should be (a teleport, a respawn): put it there. A swinging foot is judged by where it
                // left from (its old plant falls far behind during a running stride, and resetting it then dropped the
                // step: a foot jumping forward, and steps missed).
                bool far = g.swinging ? (g.from - home).sqrMagnitude > 3.5f * 3.5f : (g.plant - home).sqrMagnitude > 2.5f * 2.5f;
                if (!g.placed || far)
                {
                    g.plant = home; g.plant.y = GroundY(home, _body.position.y);
                    g.plantFwd = fwd; g.swinging = false; g.placed = true;
                }
            }

            // Two planted feet crowding each other (after a turn): the one further from home steps out.
            Leg crowded = null;
            if (_legA != null && _legB != null && !_legA.swinging && !_legB.swinging)
            {
                Leg L = _legA.side < _legB.side ? _legA : _legB, R = L == _legA ? _legB : _legA;
                Vector3 dLR = R.plant - L.plant;
                if (Vector3.Dot(dLR, right) < 0.11f && Mathf.Abs(Vector3.Dot(dLR, fwd)) < 0.28f)
                    crowded = (L.plant - HomeOf(L)).sqrMagnitude > (R.plant - HomeOf(R)).sqrMagnitude ? L : R;
            }

            // Which foot steps next: one left behind, out of place once stopped, crowded, or caught by a knock.
            Leg next = null; float worst = 0f;
            foreach (var g in new[] { _legA, _legB })
            {
                if (g == null || g.swinging) continue;
                var other = g == _legA ? _legB : _legA;
                bool stairs = other != null && other.swinging && other.reachCap < float.MaxValue;
                if (other != null && other.swinging && other.t < Mathf.Lerp(1f, 0.55f, Mathf.Max(run, _turnInPlace ? 0.8f : stairs ? 0.6f : 0f))) continue;
                Vector3 off = g.plant - HomeOf(g); off.y = 0f;
                float behind = -Vector3.Dot(off, vdir);
                float need;
                if (speed > 0.08f) need = Mathf.Max(behind - reachOut * 0.9f, off.magnitude - (reachOut + 0.16f));
                else need = off.magnitude - 0.07f;
                need = Mathf.Max(need, (Vector3.Angle(g.plantFwd, fwd) - 22f) * 0.006f);
                if (g == crowded) need = Mathf.Max(need, 0.05f);
                if (_stumble > 0f) need = Mathf.Max(need, off.magnitude);
                if (airborne) need = -1f;
                // Steps evenly spaced at the cadence: not before most of a step's time since the other foot left (people's
                // steps alternate evenly; stepping only when left behind let one foot leave the moment the other landed
                // while the other waited: a limp, 0.43 s and 0.60 s steps). A foot far behind goes anyway.
                // (Side-steps lead and close up, unevenly: not for them.)
                if (speed > 0.4f && sideways < 0.5f && other != null && !_turnInPlace && !stairs && _stumble <= 0f && need < Mathf.Lerp(0.12f, 0.3f, run)
                    && Time.time - other.liftedAt < Mathf.Lerp(0.95f, 0.9f, run) / cadence) continue;
                if (need > worst) { worst = need; next = g; }
            }
            if (next != null)
            {
                Vector3 land = OnTread(HomeOf(next) + _vel * swingTime + vdir * reachOut, fwd, _body.position.y);
                // Alternating on stairs, as people climb them: a foot goes at most one tread past the other (planted)
                // foot, not one past itself (that made both feet stop on every tread: half the walking speed, the feet
                // falling behind and the legs trailing).
                var otherG = next == _legA ? _legB : _legA;
                // (Where the other foot is landing, if it is still in the air.)
                Vector3 otherAt = otherG == null ? next.plant : otherG.swinging ? otherG.landAt : otherG.plant;
                Vector3 fromRef = Vector3.Dot(otherAt - next.plant, vdir) > 0f ? otherAt : next.plant;
                land = OneTread(fromRef, land, _body.position.y, out float capRef);
                Vector3 flatSpan = land - next.plant; flatSpan.y = 0f;
                next.reachCap = capRef < float.MaxValue ? flatSpan.magnitude + 0.02f : float.MaxValue;
                float dist = Vector3.Distance(new Vector3(next.plant.x, 0f, next.plant.z), new Vector3(land.x, 0f, land.z));
                // A swinging foot's fastest (m/s): about 5-6 walking (it covers the stride in the swing), 10 running.
                float maxFoot = Mathf.Lerp(5.5f, 10f, run);
                next.liftedAt = Time.time;
                next.swinging = true; next.t = 0f; next.duration = Mathf.Clamp(Mathf.Max(swingTime, dist * 1.5f / maxFoot), 0.2f, 0.55f);
                next.from = next.plant; next.fromFwd = next.plantFwd; next.landY = land.y; next.liftPitch = Mathf.Max(0f, next.lastPitch);
                next.landAt = land;
                if (_turnInPlace && speed < 0.2f) next.duration = Mathf.Min(next.duration, 0.28f);
                if (next.reachCap < float.MaxValue) next.duration = Mathf.Clamp(dist * 1.6f / 4.5f, 0.2f, Mathf.Min(next.duration, 0.27f));
                if (_stumble > 0f) { next.duration = Mathf.Min(next.duration, 0.22f); _stumble = 0f; }
            }

            // Hips: they lag the view's turns a little (the torso and head follow it more), go down with the squat, bob
            // over the stride and sway over the standing foot, all through springs.
            // (Walking the hips sit a little lower, the knees softer, and rise a centimetre as a foot passes the other: most
            // of the rise and fall comes from the legs' reach, below.)
            float bobWant = (_swingUp * 0.01f - 0.015f) * _walkAmount;
            _bob = Springs.Damper(_bob, ref _bobVel, bobWant, 0.04f, dt);
            float swayWant = 0f;
            if (_legA != null && _legB != null)
            {
                if (_legA.swinging && !_legB.swinging) swayWant = Mathf.Sign(_legB.side);
                else if (_legB.swinging && !_legA.swinging) swayWant = Mathf.Sign(_legA.side);
            }
            _sway = Springs.Damper(_sway, ref _swayVel, swayWant * 0.014f * _walkAmount, 0.08f, dt);
            // The pelvis turns with the stride, the hip of the leg in front going forward with it (gait data: about 4
            // degrees each way walking, more running); the thorax turns the other way with the opposite arm (about 3); the
            // spine takes the difference. Little of it sideways.
            float leadR = 0f;           // the right foot ahead (+1) or behind (-1), in steps
            if (_legA != null && _legB != null)
                leadR = Mathf.Clamp(Vector3.Dot(FootAt(_legA) - FootAt(_legB), fwd) * Mathf.Sign(_legA.side) / Mathf.Max(0.3f, step), -1f, 1f);
            float gaitW = _walkAmount * (1f - 0.7f * sideways);
            _pelvisYaw = Springs.Damper(_pelvisYaw, ref _pelvisYawVel, -Mathf.Lerp(4f, 6f, run) * leadR * gaitW, 0.05f, dt);
            _twist = Springs.Damper(_twist, ref _twistVel, Mathf.Lerp(3f, 6f, run) * leadR * gaitW, 0.06f, dt);
            // Obliquity: while one foot carries the body the other side of the pelvis drops a few degrees.
            float obl = 0f;
            if (_legA != null && _legB != null)
            {
                if (_legA.swinging && !_legB.swinging) obl = -Mathf.Sign(_legA.side);
                else if (_legB.swinging && !_legA.swinging) obl = -Mathf.Sign(_legB.side);
            }
            _pelvisRoll = Springs.Damper(_pelvisRoll, ref _pelvisRollVel, obl * Mathf.Lerp(4f, 5f, run) * _walkAmount * Mathf.Clamp01(speed / 0.6f), 0.06f, dt);
            _hipsYOff = Springs.Damper(_hipsYOff, ref _hipsYVel, -drop + _bob, 0.04f, dt);

            Vector3 hipsBase = _body.position + up * (_hipsHeight + _hipsYOff) + right * _sway - fwd * (squat * 0.12f) + _bumpHips;

            // Feet: where each sole is this frame.
            var soles = new Vector3[2]; var fwds = new Vector3[2]; var pitches = new float[2];
            var legs = new[] { _legA, _legB };
            float liftH = Mathf.Lerp(0.065f, 0.15f, run) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(speed / 0.8f));
            float swingUp = 0f;
            for (int i = 0; i < 2; i++)
            {
                var g = legs[i];
                if (g == null) continue;
                Vector3 sole, footFwd; float pitch;
                if (g.swinging)
                {
                    g.t = Mathf.Min(1f, g.t + dt / Mathf.Max(0.05f, g.duration));
                    float left = (1f - g.t) * g.duration;
                    Vector3 target = HomeOf(g) + _vel * left + vdir * reachOut;
                    Vector3 rel = target - _body.position;
                    float lat = Vector3.Dot(rel, right);
                    if (lat * Mathf.Sign(g.side) < 0.06f) target += right * (Mathf.Sign(g.side) * 0.06f - lat);
                    // Closing up behind a side-step, the trailing foot stops a foot's width from the planted one, never
                    // beside or past it (the push below fades out at landing, so it must be in the target itself).
                    var otherLeg = g == _legA ? _legB : _legA;
                    if (otherLeg != null && !otherLeg.swinging)
                    {
                        float apart = Vector3.Dot(target - otherLeg.plant, right) * Mathf.Sign(g.side);
                        if (apart < SideGap) target += right * (Mathf.Sign(g.side) * (SideGap - apart));
                    }
                    {
                        float cap = Mathf.Min(g.reachCap, Mathf.Max(1.45f, 2.1f * step));
                        Vector3 span = target - g.from; span.y = 0f;
                        if (span.magnitude > cap) target = g.from + span.normalized * cap + Vector3.up * target.y;
                    }
                    target = OnTread(target, fwd, _body.position.y);
                    // Eased, but always there by the end of the step (a short step down could land in the air).
                    float remain = Mathf.Max((1f - g.t) * g.duration, dt);
                    g.landY = Mathf.MoveTowards(g.landY, target.y, Mathf.Max(dt * 3f, Mathf.Abs(target.y - g.landY) * dt / remain));
                    target.y = g.landY;
                    float s = Mathf.SmoothStep(0f, 1f, g.t);
                    sole = Vector3.Lerp(g.from, target, s);
                    // Up or down a step the foot goes the way a real one does: stepping down it carries forward over the
                    // edge first and then lowers onto the tread below; stepping up it rises first, then goes over the
                    // nosing. A small lift either way (not the whole step's height on top, which threw the knee up).
                    float dy = target.y - g.from.y;
                    float vy = dy < -0.03f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, g.t))
                             : dy > 0.03f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.65f, g.t)) : s;
                    sole.y = g.from.y + dy * vy;
                    // The lift is highest early in the swing (the knee folds, the heel comes up behind), the toes skimming
                    // lower as the foot passes the other one.
                    float arc = Gait.SwingLift(g.t);
                    sole += up * (arc * (Mathf.Abs(dy) > 0.03f ? Mathf.Max(liftH, 0.05f) : liftH));
                    footFwd = Vector3.Slerp(g.fromFwd, fwd, s);
                    // Forward: toes leave last and the heel lands first, toes up; backward or down a step: the toes land first.
                    float down = Mathf.InverseLerp(-0.04f, -0.12f, dy);
                    float landPitch = Mathf.Lerp(heelStrike * (1f - sideways), 12f, Mathf.Max(backward, down));
                    // From exactly the tilt the foot had as it left the floor (heel up: no jump at lift-off), levelling,
                    // to the landing's.
                    float k = Mathf.Max(Mathf.Clamp01(speed / 0.6f), down);
                    pitch = Gait.SwingPitch(g.t, g.liftPitch, landPitch * k);
                    g.pitchS = pitch; g.pitchVel = 0f;
                    swingUp = Mathf.Max(swingUp, Mathf.Sin(g.t * Mathf.PI));
                    if (g.t >= 1f)
                    {
                        g.swinging = false; g.plant = target; g.plantFwd = fwd; g.since = 0f; g.landPitch = pitch;
                        StepsLanded++; FootLanded?.Invoke(target, speed);
                    }
                }
                else
                {
                    sole = g.plant;
                    footFwd = g.plantFwd;
                    Vector3 off = g.plant - HomeOf(g); off.y = 0f;
                    float behind = -Vector3.Dot(off, vdir);
                    // The heel rises about the ball of the foot from a little behind the hip to toe-off (the forefoot rocker).
                    // (Over at least 20 cm: starting or stopping, reachOut passes through nothing and the share jumped.)
                    float rise = Gait.HeelRise(behind / Mathf.Max(reachOut, 0.2f), toeOffPitch) * Mathf.Clamp01(speed / 0.3f);
                    // Just landed: the heel strike's tilt rolls out about the heel (toes come down), or a toes-first landing's
                    // about the ball (the heel comes down), eased: it used to switch to flat in one frame.
                    g.since += dt;
                    float roll = g.since < rollOut ? 1f - Mathf.SmoothStep(0f, 1f, g.since / rollOut) : 0f;
                    // Through a fast spring (inertialized): the tilt a planted foot wants can change in a frame (a start, a
                    // stop, a turn), the foot's roll about its heel or ball does not.
                    g.pitchS = Springs.Damper(g.pitchS, ref g.pitchVel, Mathf.Lerp(rise, g.landPitch, roll), 0.03f, dt);
                    pitch = g.pitchS;
                }
                soles[i] = sole; fwds[i] = footFwd; pitches[i] = pitch;
            }
            // The feet never in each other: a swinging foot passing the standing one goes round it (side to side).
            if (_legA != null && _legB != null)
            {
                int li = _legA.side < _legB.side ? 0 : 1, ri = 1 - li;
                Vector3 d = soles[ri] - soles[li];
                float gap = Vector3.Dot(d, right), along = Mathf.Abs(Vector3.Dot(d, fwd));
                const float MinGap = 0.12f;
                if (gap < MinGap && along < 0.3f)
                {
                    float push = (MinGap - gap) * (1f - Mathf.InverseLerp(0.2f, 0.3f, along));
                    // Eased over the step (full mid-swing, none at lift-off or landing), so it never jumps.
                    float ease = 0f;
                    if (legs[ri].swinging) ease = Mathf.Max(ease, Mathf.Sin(legs[ri].t * Mathf.PI));
                    if (legs[li].swinging) ease = Mathf.Max(ease, Mathf.Sin(legs[li].t * Mathf.PI));
                    push *= ease;
                    // Only a foot in the air goes round: a planted one never slides (two crowded planted feet make a
                    // step instead, above).
                    if (legs[ri].swinging && !legs[li].swinging) soles[ri] += right * push;
                    else if (legs[li].swinging && !legs[ri].swinging) soles[li] -= right * push;
                    else if (legs[li].swinging && legs[ri].swinging) { soles[ri] += right * (push * 0.5f); soles[li] -= right * (push * 0.5f); }
                }
            }
            _swingUp = swingUp;

            // Ankles, and how far the hips must come down so both legs reach (stairs, a foot on a lower tread).
            var ankles = new Vector3[2]; var pitched = new Vector3[2]; var fronts = new Vector3[2];
            float needDrop = 0f;
            for (int i = 0; i < 2; i++)
            {
                var g = legs[i];
                if (g == null) continue;
                Vector3 hipRel = g.thigh.position - _hips.position;
                Vector3 hip = hipsBase + hipRel;
                Vector3 hang = hip - up * ((g.upper + g.lower) * 0.9f + g.ankle) + fwd * 0.06f;
                Vector3 sole = Vector3.Lerp(soles[i], hang, _air);
                if (_air > 0.5f) { g.plant = sole; g.swinging = false; g.placed = _air < 0.99f; }
                Vector3 flatFwd = Vector3.ProjectOnPlane(fwds[i], up).normalized;
                if (flatFwd.sqrMagnitude < 0.5f) flatFwd = fwd;
                Vector3 footRight = Vector3.Cross(up, flatFwd);
                float legLen = g.upper + g.lower;
                // How straight a leg on the floor may be before the hips come down for it: nearly straight standing (knee
                // about 9 degrees; it was 20, a slight squat), a little more bent walking (about 16: the knee's give after
                // the heel strike), bent running (about 30: a runner's knee never straightens on the ground).
                float reach = legLen * Mathf.Lerp(Mathf.Lerp(0.997f, 0.99f, _walkAmount), 0.965f, run);
                ankles[i] = AnkleAt(g, sole, flatFwd, footRight, up, pitches[i]);
                // The gait's ankle, before any heel lift for reach: the hips come down for that first. (Using the lifted
                // ankle fed back: a lifted heel let the hips stay up, which kept the heel lifted: the body stood on tiptoe
                // after a spawn or a teleport until its next step.)
                Vector3 gaitAnkle = ankles[i];
                // A planted foot the leg can no longer reach (left behind at the end of a stride) lifts its heel, as a real
                // one does, rather than being dragged along the floor by a straight leg.
                // (Solved exactly, not stepped: stepping fed back through the hips and made the heel flicker.)
                Vector3 hipNow = hip - up * _pelvisDrop;      // where the hip is with the drop it has now
                float heelNeed = 0f;
                // (Only a foot behind: a foot ahead out of reach, as at a heel strike, is reached by the hips coming down;
                // lifting its heel snapped it 35 degrees in a frame.)
                Vector3 travel = speed > 0.1f ? vdir : fwd;
                if (!g.swinging && _air < 0.5f && Vector3.Dot(sole - hipNow, travel) < 0f && (ankles[i] - hipNow).magnitude > reach)
                {
                    float lo = Mathf.Max(0f, pitches[i]), hi = Mathf.Lerp(35f, 22f, sideways);
                    if ((AnkleAt(g, sole, flatFwd, footRight, up, hi) - hipNow).magnitude > reach) lo = hi;
                    else for (int k = 0; k < 10; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if ((AnkleAt(g, sole, flatFwd, footRight, up, mid) - hipNow).magnitude > reach) lo = mid; else hi = mid;
                    }
                    heelNeed = lo;
                }
                // Up as fast as a heel rises at toe-off (500 degrees a second: at once, it jumped 35 degrees in a frame when
                // the body rose a stair); down again eased (the heel settles, it does not drop).
                if (heelNeed >= g.heel) { g.heel = Mathf.MoveTowards(g.heel, heelNeed, Mathf.Lerp(500f, 1200f, run) * dt); g.heelVel = 0f; }
                else g.heel = Springs.Damper(g.heel, ref g.heelVel, heelNeed, 0.05f, dt);
                if (g.swinging) { g.heel = 0f; g.heelVel = 0f; }
                if (g.heel > 0f && g.heel > pitches[i]) { pitches[i] = g.heel; ankles[i] = AnkleAt(g, sole, flatFwd, footRight, up, pitches[i]); }
                g.lastPitch = pitches[i];
                // Heel up, the toes bend at the ball of the foot and stay on the floor (they used to turn with the foot,
                // into the floor); relaxed while the legs hang.
                g.toeBend = Mathf.Clamp(pitches[i], 0f, 60f) * (1f - _air);
                pitched[i] = Quaternion.AngleAxis(pitches[i], footRight) * flatFwd;
                Vector3 kneeTo = fwd + flatFwd;
                fronts[i] = kneeTo.sqrMagnitude > 0.05f ? kneeTo.normalized : fwd;
                // Soft IK: a swinging leg eases into straight (no snap before the heel strike), a planted one only over the
                // last few millimetres.
                g.soft = Springs.Damper(g.soft, ref g.softVel, (g.swinging ? 0.03f : Mathf.Lerp(0.004f, 0.02f, run)) * legLen, 0.06f, dt);
                // A swinging leg late in its swing counts too, so the hips are down for the heel strike when it lands.
                // (Early in its swing a foot is free: one just lifted far behind pulled the hips 20 cm down running.)
                float r = g.swinging ? Mathf.Lerp(legLen * 0.999f, reach, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, g.t))) : reach;
                Vector3 dd = gaitAnkle - hip;
                if ((!g.swinging || g.t > 0.5f) && dd.magnitude > r)
                {
                    // How far down the hip must go for this ankle to be in reach.
                    float h2 = r * r - (dd.x * dd.x + dd.z * dd.z);
                    float wantY = h2 > 0f ? gaitAnkle.y + Mathf.Sqrt(h2) : gaitAnkle.y + r * 0.5f;
                    needDrop = Mathf.Max(needDrop, hip.y - wantY);
                }
            }
            _pelvisDrop = Springs.Damper(_pelvisDrop, ref _pelvisDropVel, Mathf.Min(needDrop, 0.25f), 0.05f, dt);
            _hips.position = hipsBase - up * _pelvisDrop;
            _hips.rotation = Quaternion.AngleAxis(hinge * HingePelvis, right) * Quaternion.AngleAxis(_pelvisRoll, fwd) *Quaternion.AngleAxis(lag + _pelvisYaw, up) * _body.rotation * _hipsRestLocal;

            // Torso: the stoop, a lean into a crouch, a lean into the walk and into turns, knocks; it takes back most of
            // the hips' lag (it turns with the view), and the stride's pelvis turn and drop (the shoulders stay level).
            // (Hinging at the hips for something over a desk or a board: the spine takes its share of the hinge, not
            // through the spring: the view moves with it this frame, PropGrabber.ApplyHingeView.)
            float leanWant = bend + Mathf.Clamp(_moveLocal.y, -1f, 3f) * 2.5f;
            _leanS = Springs.Damper(_leanS, ref _leanSVel, leanWant, 0.06f, dt);
            PoseSpine(_leanS + hinge * (1f - HingePelvis) + _bumpTorso.x, _twist - _pelvisYaw - lag * 0.75f + HingeTwistAt(hinge) * _hingeTwistSign * Mathf.Sign(_grab ? _grab.Side : 1f),
                      -_player.Lean * 11f - _player.TurnLean * 4f + _bumpTorso.y - _pelvisRoll * 0.85f);

            for (int i = 0; i < 2; i++)
            {
                var g = legs[i];
                if (g == null) continue;
                SolveLeg(g, g.thigh.position, ankles[i], fronts[i], pitched[i], up);
            }
        }

        /// <summary>
        /// The ankle for a foot whose sole is at 'sole' (under the ankle when flat), pitched by 'pitch' degrees: heel up
        /// (positive) it turns about the ball of the foot, so the toes stay where they are on the floor while the heel
        /// peels off; toes up (negative) it turns about the back of the heel, which stays on the floor (a heel strike).
        /// </summary>
        static Vector3 AnkleAt(Leg g, Vector3 sole, Vector3 flatFwd, Vector3 footRight, Vector3 up, float pitch)
        {
            if (pitch >= 0f)
            {
                Vector3 ball = sole + flatFwd * g.toe;
                return ball + Quaternion.AngleAxis(pitch, footRight) * (up * g.ankle - flatFwd * g.toe);
            }
            Vector3 heel = sole - flatFwd * g.heelBack;
            return heel + Quaternion.AngleAxis(pitch, footRight) * (up * g.ankle + flatFwd * g.heelBack);
        }

        float _pelvisDrop, _pelvisDropVel, _bobVel, _swayVel, _twistVel, _pelvisYaw, _pelvisYawVel, _pelvisRoll, _pelvisRollVel;
        /// <summary>The least the feet are apart across the body when one lands (centre to centre), metres.</summary>
        const float SideGap = 0.15f;
        Vector3 _velVel;
        Quaternion _hipsRestLocal = Quaternion.identity;
    }
}
