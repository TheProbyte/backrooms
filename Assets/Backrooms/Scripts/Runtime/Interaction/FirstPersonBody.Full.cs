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

        /// <summary>After the body: the head and neck to the view, the left arm and the camcorder to the eye.</summary>
        void UpdateFull()
        {
            var pivot = _player.cameraPitchPivot ? _player.cameraPitchPivot : transform;
            bool show = !_player.noclip && _player.Eye > HideBelowEye * 0.6f;
            if (_camcorder)
            {
                _camcorder.gameObject.SetActive(show);
                _camcorder.SetPositionAndRotation(pivot.position, pivot.rotation);
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
            if (_lRig != null && show && (!onBody || (_body && _body.gameObject.activeSelf))) UpdateLeftArm(pivot);
        }

        /// <summary>The left hand under the camcorder, palm up, holding it to the eye; the elbow down in front of the chest.</summary>
        void UpdateLeftArm(Transform cam)
        {
            var r = _lRig;
            float side = _grab ? -_grab.Side : -1f;
            Quaternion yaw = transform.rotation;
            Vector3 shoulder = cam.position + yaw * new Vector3(0.2f * side, -0.28f, -0.08f);
            // The body's arm: its own shoulder, the collarbone bringing it up and forward toward the camcorder a little.
            if (!r.root && !_leftArm) shoulder = CarryShoulder(r.clavicle, r.clavicleRest, r.upper, shoulder, 20f);
            Quaternion handRot = Quaternion.LookRotation(cam.forward * 0.85f - cam.right * side * 0.35f, -cam.up * 0.9f + cam.right * side * 0.3f);
            Vector3 grip = cam.position + cam.rotation * new Vector3(0.0f, -0.075f, 0.08f);
            Vector3 wrist = grip - handRot * new Vector3(0f, -0.02f, 0.065f);
            Vector3 d = wrist - shoulder;
            float a = r.upperLen, b = r.foreLen, len = Mathf.Clamp(d.magnitude, Mathf.Abs(a - b) + 0.02f, (a + b) * 0.999f);
            Vector3 dir = d.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(yaw * new Vector3(0.3f * side, -1f, 0.35f), dir).normalized;
            float cosA = Mathf.Clamp((a * a + len * len - b * b) / (2f * a * len), -1f, 1f);
            Vector3 elbow = shoulder + dir * (cosA * a) + pole * (Mathf.Sqrt(1f - cosA * cosA) * a);
            Vector3 wristAt = shoulder + dir * len;
            if (_leftArm) _leftArm.SetPositionAndRotation(shoulder, Quaternion.identity);
            Vector3 handBack = handRot * Vector3.up;
            Orient(r.upper, r.upperAxis, r.upperBack, elbow - shoulder, -pole);
            Orient(r.fore, r.foreAxis, r.foreBack, wristAt - elbow, handBack);
            Orient(r.hand, r.handFingers, r.handBack, handRot * Vector3.forward, handBack);
            r.hand.position = wristAt;
            foreach (var (t, rest, axis, w, _) in r.joints) t.localRotation = rest * Quaternion.AngleAxis(0.85f * 62f * w, axis);
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
                lead = Vector3.Dot(FootAt(other) - FootAt(same), fwd) * 0.45f;
            }
            Vector3 want = fwd * Mathf.Clamp(lead, -0.2f, 0.2f) * _walkAmount - _vel * 0.015f;
            _armSwing = Springs.Oscillator(_armSwing, ref _armSwingVel, want, 1.7f, 0.45f, dt);
            Vector3 hang = shoulder + yaw * new Vector3(0.065f * side, -0.55f, 0.03f) + _armSwing;
            Quaternion hangRot = Quaternion.LookRotation(Vector3.down * 0.95f + fwd * 0.12f + _armSwing * 0.6f, right * side);

            UpdateTouch(shoulder, yaw, side);
            _touchW = Springs.Damper(_touchW, ref _touchWVel, _touching ? 1f : 0f, 0.09f, dt);
            if (_touchW < 0.002f && !_touching) { wrist = hang; hand = hangRot; curl = 0.25f; return; }
            Vector3 tw; Quaternion tr; float tc;
            if (_touchRail)
            {
                // Round the rail: palm on top, fingers over and under it.
                Vector3 fingers = Vector3.ProjectOnPlane(Vector3.Cross(Vector3.up, _touchAlong), Vector3.up).normalized;
                if (Vector3.Dot(fingers, _touchPoint - shoulder) < 0f) fingers = -fingers;
                tr = Quaternion.LookRotation(fingers, Vector3.up);
                tw = _touchPoint + Vector3.up * 0.035f - fingers * 0.05f;
                tc = 0.95f;
            }
            else
            {
                // Flat on the wall: palm to it, fingers up and a little forward.
                Vector3 fingers = Vector3.ProjectOnPlane(Vector3.up * 0.75f + fwd * 0.6f, _touchNormal).normalized;
                tr = Quaternion.LookRotation(fingers, _touchNormal);
                tw = _touchPoint + _touchNormal * 0.02f - fingers * 0.055f;
                tc = 0.12f;
            }
            float s = Mathf.SmoothStep(0f, 1f, _touchW);
            wrist = Vector3.Lerp(hang, tw, s);
            hand = Quaternion.Slerp(hangRot, tr, s);
            curl = Mathf.Lerp(0.25f, tc, s);
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
                bool keep = allowed && to.magnitude < (_upperLen + _foreLen) * 1.02f && Vector3.Dot(to, fwd) > -0.12f
                            && (Time.time < _touchUntil || Mathf.Abs(_player.Lean) > 0.2f || _vel.magnitude < 0.3f);
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
                    if (h.distance >= best || h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<WorldItem>()) continue;
                    best = h.distance; hit = h;
                }
                if (best == float.MaxValue) continue;
                var bounds = hit.collider.bounds;
                bool rail = Mathf.Min(bounds.size.x, bounds.size.z) < 0.09f && bounds.size.y < 0.14f && Mathf.Max(bounds.size.x, bounds.size.z) > 0.4f;
                if (!rail && Mathf.Abs(hit.normal.y) > 0.35f) continue;
                // Beside a wall: now and then (a few seconds on, a few off); leaning round a corner: yes.
                if (!leaning && Mathf.PerlinNoise(Time.time * 0.13f, side) < (trailing ? 0.6f : 0.65f)) return;
                _touching = true; _touchRail = rail;
                _touchPoint = rail ? new Vector3(hit.point.x, bounds.max.y, hit.point.z) : hit.point;
                _touchNormal = hit.normal;
                _touchAlong = bounds.size.x > bounds.size.z ? Vector3.right : Vector3.forward;
                _touchUntil = Time.time + 0.8f + Random.value * 1.5f;
                return;
            }
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
        Vector3 OnTread(Vector3 t, Vector3 dir, float fallback)
        {
            float y0 = GroundY(t, fallback);
            float yA = GroundY(t + dir * 0.11f, fallback), yB = GroundY(t - dir * 0.11f, fallback);
            t.y = y0;
            if (Mathf.Abs(yA - y0) < 0.025f && Mathf.Abs(yB - y0) < 0.025f) return t;
            float back = 0f, front = 0f;
            for (int k = 1; k <= 14; k++) { if (Mathf.Abs(GroundY(t - dir * (k * 0.025f), fallback) - y0) > 0.025f) break; back = k * 0.025f; }
            for (int k = 1; k <= 14; k++) { if (Mathf.Abs(GroundY(t + dir * (k * 0.025f), fallback) - y0) > 0.025f) break; front = k * 0.025f; }
            float depth = back + front;
            float shift = depth < 0.24f ? (front - back) * 0.5f : Mathf.Clamp(0f, 0.1f - back, front - 0.12f);
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
            bool visible = !_player.noclip && eye > HideBelowEye;
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
            float torsoDrop = TorsoLength * (1f - Mathf.Cos(Mathf.Min(bend, 80f) * Mathf.Deg2Rad));
            float drop = Mathf.Clamp(_player.standEye - eye - torsoDrop, 0f, 0.5f);
            float squat = drop / 0.5f;
            // The body stands behind the eye along the torso's heading (between the view's and the legs'), at a height
            // eased over steps: going down stairs the hips sink smoothly instead of dropping a whole step at a time.
            {
                float backBy = bodyBack + TorsoLength * 0.9f * Mathf.Sin(Mathf.Min(bend, 80f) * Mathf.Deg2Rad);
                Vector3 local = Quaternion.Euler(0f, 0.25f * lag, 0f) * new Vector3(0f, 0f, -backBy);
                Vector3 world = _body.parent ? _body.parent.TransformPoint(local) : local;
                if (!_rootYSet || Mathf.Abs(world.y - _rootY) > 0.6f) { _rootY = world.y; _rootYVel = 0f; _rootYSet = true; }
                _rootY = Springs.Damper(_rootY, ref _rootYVel, world.y, airborne ? 0.015f : 0.06f, dt);
                _rootY = Mathf.Clamp(_rootY, world.y - 0.18f, world.y + 0.18f);
                world.y = _rootY;
                _body.position = world;
            }

            // A gait for the direction: steps shorter sideways and backward, the stance a little wider sideways.
            float sideways = speed > 0.05f ? Mathf.Abs(_moveLocal.x) / speed : 0f;
            float backward = speed > 0.05f ? Mathf.Clamp01(-_moveLocal.y / speed) : 0f;
            float step = Mathf.Clamp(0.3f + 0.22f * speed, 0.3f, 1.1f) * Mathf.Lerp(1f, 0.6f, sideways) * Mathf.Lerp(1f, 0.75f, backward);
            float run = Mathf.InverseLerp(1.6f, 3.6f, speed);
            float stanceShare = Mathf.Lerp(0.62f, 0.38f, run);
            float cycle = speed > 0.05f ? 2f * step / speed : 1f;
            float swingTime = Mathf.Clamp((1f - stanceShare) * cycle, 0.2f, 0.42f) * Mathf.Lerp(1f, 0.8f, sideways);
            float reachOut = Mathf.Min(stanceShare * step, 0.42f) * Mathf.SmoothStep(0f, 1f, speed / 0.7f) * Mathf.Lerp(1f, 0.55f, sideways);
            Vector3 vdir = speed > 0.05f ? _vel / speed : fwd;
            float wide = 0.025f * sideways;

            Vector3 HomeOf(Leg g) => Home(g, squat) + right * (Mathf.Sign(g.side) * wide);

            foreach (var g in new[] { _legA, _legB })
            {
                if (g == null) continue;
                var home = HomeOf(g);
                if (!g.placed || (g.plant - home).sqrMagnitude > 2.5f * 2.5f)
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
                float maxFoot = Mathf.Lerp(4.5f, 9f, run);
                next.swinging = true; next.t = 0f; next.duration = Mathf.Clamp(Mathf.Max(swingTime, dist * 1.5f / maxFoot), 0.2f, 0.55f);
                next.from = next.plant; next.fromFwd = next.plantFwd; next.landY = land.y; next.liftPitch = Mathf.Max(0f, next.lastPitch);
                next.landAt = land;
                if (_turnInPlace && speed < 0.2f) next.duration = Mathf.Min(next.duration, 0.28f);
                if (next.reachCap < float.MaxValue) next.duration = Mathf.Clamp(dist * 1.6f / maxFoot, 0.2f, Mathf.Min(next.duration, 0.27f));
                if (_stumble > 0f) { next.duration = Mathf.Min(next.duration, 0.22f); _stumble = 0f; }
            }

            // Hips: they lag the view's turns a little (the torso and head follow it more), go down with the squat, bob
            // over the stride and sway over the standing foot, all through springs.
            float bobWant = (_swingUp - 0.45f) * 0.03f * _walkAmount;
            _bob = Springs.Damper(_bob, ref _bobVel, bobWant, 0.04f, dt);
            float swayWant = 0f;
            if (_legA != null && _legB != null)
            {
                if (_legA.swinging && !_legB.swinging) swayWant = Mathf.Sign(_legB.side);
                else if (_legB.swinging && !_legA.swinging) swayWant = Mathf.Sign(_legA.side);
            }
            _sway = Springs.Damper(_sway, ref _swayVel, swayWant * 0.014f * _walkAmount, 0.08f, dt);
            float lead = 0f;
            if (_legA != null && _legB != null) lead = Vector3.Dot(FootAt(_legA) - FootAt(_legB), fwd) * Mathf.Sign(_legA.side);
            _twist = Springs.Damper(_twist, ref _twistVel, Mathf.Clamp(lead * 9f, -5f, 5f), 0.07f, dt);
            _hipsYOff = Springs.Damper(_hipsYOff, ref _hipsYVel, -drop + _bob, 0.04f, dt);

            Vector3 hipsBase = _body.position + up * (_hipsHeight + _hipsYOff) + right * _sway - fwd * (squat * 0.12f) + _bumpHips;

            // Feet: where each sole is this frame.
            var soles = new Vector3[2]; var fwds = new Vector3[2]; var pitches = new float[2];
            var legs = new[] { _legA, _legB };
            float liftH = Mathf.Lerp(0.06f, 0.13f, run) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(speed / 0.8f));
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
                        float cap = Mathf.Min(g.reachCap, Mathf.Lerp(1.45f, 2.2f, run));
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
                    float arc = Mathf.Sin(g.t * Mathf.PI);
                    sole += up * (arc * (Mathf.Abs(dy) > 0.03f ? Mathf.Max(liftH, 0.05f) : liftH));
                    footFwd = Vector3.Slerp(g.fromFwd, fwd, s);
                    // Forward: toes leave last and the heel lands first; backward or down a step: the toes land first.
                    float down = Mathf.InverseLerp(-0.04f, -0.12f, dy);
                    float landPitch = Mathf.Lerp(-10f, 12f, Mathf.Max(backward, down));
                    // From the tilt the foot had as it left the floor (its heel up), to the landing's.
                    float k = Mathf.Max(Mathf.Clamp01(speed / 0.6f), down);
                    pitch = Mathf.Lerp(Mathf.Max(18f * k, g.liftPitch), landPitch * k, Mathf.SmoothStep(0f, 1f, g.t));
                    swingUp = Mathf.Max(swingUp, arc);
                    if (g.t >= 1f) { g.swinging = false; g.plant = target; g.plantFwd = fwd; StepsLanded++; FootLanded?.Invoke(target, speed); }
                }
                else
                {
                    sole = g.plant;
                    footFwd = g.plantFwd;
                    Vector3 off = g.plant - HomeOf(g); off.y = 0f;
                    float behind = -Vector3.Dot(off, vdir);
                    pitch = 22f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reachOut * 0.3f, reachOut * 0.95f + 0.02f, behind)) * Mathf.Clamp01(speed / 0.3f)
                            * (1f - sideways);
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
                float reach = (g.upper + g.lower) * 0.985f;
                ankles[i] = AnkleAt(g, sole, flatFwd, footRight, up, pitches[i]);
                // A planted foot the leg can no longer reach (left behind at the end of a stride) lifts its heel, as a real
                // one does, rather than being dragged along the floor by a straight leg.
                // (Solved exactly, not stepped: stepping fed back through the hips and made the heel flicker.)
                Vector3 hipNow = hip - up * _pelvisDrop;      // where the hip is with the drop it has now
                float heelNeed = 0f;
                if (!g.swinging && _air < 0.5f && (ankles[i] - hipNow).magnitude > reach)
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
                // Up at once as far as the leg needs; down again eased (the heel settles, it does not drop).
                g.heel = heelNeed >= g.heel ? heelNeed : Springs.Damper(g.heel, ref g.heelVel, heelNeed, 0.05f, dt);
                if (heelNeed >= g.heel) g.heelVel = 0f;
                if (g.swinging) { g.heel = 0f; g.heelVel = 0f; }
                if (g.heel > pitches[i]) { pitches[i] = g.heel; ankles[i] = AnkleAt(g, sole, flatFwd, footRight, up, pitches[i]); }
                g.lastPitch = pitches[i];
                pitched[i] = Quaternion.AngleAxis(pitches[i], footRight) * flatFwd;
                Vector3 kneeTo = fwd + flatFwd;
                fronts[i] = kneeTo.sqrMagnitude > 0.05f ? kneeTo.normalized : fwd;
                Vector3 dd = ankles[i] - hip;
                if (dd.magnitude > reach)
                {
                    // How far down the hip must go for this ankle to be in reach.
                    float h2 = reach * reach - (dd.x * dd.x + dd.z * dd.z);
                    float wantY = h2 > 0f ? ankles[i].y + Mathf.Sqrt(h2) : ankles[i].y + reach * 0.5f;
                    needDrop = Mathf.Max(needDrop, hip.y - wantY);
                }
            }
            _pelvisDrop = Springs.Damper(_pelvisDrop, ref _pelvisDropVel, Mathf.Min(needDrop, 0.25f), 0.05f, dt);
            _hips.position = hipsBase - up * _pelvisDrop;
            _hips.rotation = Quaternion.AngleAxis(lag, up) * _body.rotation * _hipsRestLocal;

            // Torso: the stoop, a lean into a crouch, a lean into the walk and into turns, knocks; it takes back most of
            // the hips' lag (it turns with the view).
            float leanWant = bend + Mathf.Clamp(_moveLocal.y, -1f, 3f) * 2.5f;
            _leanS = Springs.Damper(_leanS, ref _leanSVel, leanWant, 0.06f, dt);
            PoseSpine(_leanS + _bumpTorso.x, _twist - lag * 0.75f, -_player.Lean * 11f - _player.TurnLean * 4f + _bumpTorso.y);

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
        /// peels off; toes up (negative) it turns about the heel.
        /// </summary>
        static Vector3 AnkleAt(Leg g, Vector3 sole, Vector3 flatFwd, Vector3 footRight, Vector3 up, float pitch)
        {
            if (pitch <= 0f) return sole + up * g.ankle;
            Vector3 ball = sole + flatFwd * g.toe;
            return ball + Quaternion.AngleAxis(pitch, footRight) * (up * g.ankle - flatFwd * g.toe);
        }

        float _pelvisDrop, _pelvisDropVel, _bobVel, _swayVel, _twistVel;
        /// <summary>The least the feet are apart across the body when one lands (centre to centre), metres.</summary>
        const float SideGap = 0.15f;
        Vector3 _velVel;
        Quaternion _hipsRestLocal = Quaternion.identity;
    }
}
