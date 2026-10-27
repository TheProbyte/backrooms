using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms
{
    /// <summary>
    /// First-person movement and mouse look for the camcorder viewpoint.
    /// Mouse look is applied directly (no smoothing) so aim stays responsive;
    /// the handheld shake is layered on a child transform by <see cref="HandheldCameraShake"/>.
    ///
    /// Controls: WASD walk, Shift run, C crouch (toggle), Left Ctrl crawl (toggle), Space jump (tap for a
    /// hop, hold to bend the knees for a higher jump), hold F to zoom in (release to zoom back out).
    /// Stance changes move the eye on a spring, so getting down and up is eased like a person moving;
    /// dropping into a crouch or crawl from a run is quicker and carries the body forward.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class CamcorderPlayerController : MonoBehaviour
    {
        [Header("Look")]
        [Tooltip("Mouse sensitivity in degrees per mouse count.")]
        [Range(0.01f, 0.5f)] public float mouseSensitivity = 0.08f;
        [Tooltip("Invert vertical mouse look.")]
        public bool invertY = false;
        [Tooltip("Ignore input while the game window is in the background.")]
        public bool requireWindowFocus = true;
        [Tooltip("How far up/down the player can look, in degrees.")]
        [Range(45f, 89f)] public float pitchLimit = 82f;
        [Tooltip("How far down the player can look, in degrees (less than looking up, so you see your feet and not your chest).")]
        [Range(30f, 89f)] public float pitchLimitDown = 62f;

        [Header("Movement (metres per second)")]
        [Tooltip("Slow, careful walking pace.")]
        [Range(0.5f, 3f)] public float walkSpeed = 1.35f;
        [Tooltip("Speed while holding Shift.")]
        [Range(1.5f, 6f)] public float runSpeed = 3.6f;
        [Range(0.2f, 2f)] public float crouchSpeed = 0.8f;
        [Range(0.1f, 1.5f)] public float crawlSpeed = 0.45f;
        [Tooltip("How quickly the player reaches the target speed (higher = snappier).")]
        [Range(1f, 20f)] public float acceleration = 6f;
        [Tooltip("How quickly the player stops when no keys are held.")]
        [Range(1f, 20f)] public float deceleration = 8f;
        public float gravity = -19.6f;

        [Header("Stances (metres)")]
        public float standEye = 1.58f, crouchEye = 1.02f, crawlEye = 0.42f;
        public float standHeight = 1.75f, crouchHeight = 1.2f, crawlHeight = 0.62f;

        [Header("Jump")]
        [Tooltip("Height of a quick tap jump (a small hop).")]
        public float hopHeight = 0.22f;
        [Tooltip("Height of a fully charged jump (hold Space to bend the legs first).")]
        public float chargedJumpHeight = 0.5f;
        [Tooltip("Seconds of holding Space for a full charge.")]
        public float chargeTime = 0.45f;

        [Header("Zoom (hold F)")]
        [Tooltip("Field of view multiplier at full zoom (0.25 = 4x).")]
        [Range(0.1f, 0.8f)] public float maxZoomFov = 0.25f;
        [Tooltip("Zoom travel per second at full motor speed (0..1 range).")]
        public float zoomRate = 0.3f;

        [Header("References")]
        [Tooltip("Pivot that receives pitch rotation (parent of the shake rig).")]
        public Transform cameraPitchPivot;

        public enum Stance { Stand, Crouch, Crawl }
        public Stance CurrentStance { get; private set; }
        public bool IsCrawling => CurrentStance == Stance.Crawl;
        /// <summary>0 = wide, 1 = full zoom.</summary>
        public float Zoom { get; private set; }
        /// <summary>Zoom motor speed, -1..1 (for the motor sound).</summary>
        public float ZoomMotor { get; private set; }
        /// <summary>Field of view multiplier from the zoom (1 = wide).</summary>
        public float FovMultiplier => Mathf.Lerp(1f, maxZoomFov, Zoom);
        /// <summary>Landing speed in m/s on the frame the player lands, otherwise 0.</summary>
        public float LandedThisFrame { get; private set; }

        public Vector3 PlanarVelocity { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsGrounded => _controller.isGrounded;
        /// <summary>0 when still, 1 at walk speed, ~2.7 at run speed.</summary>
        public float NormalizedSpeed => walkSpeed > 0f ? PlanarVelocity.magnitude / walkSpeed : 0f;

        [Header("Noclip")]
        [Tooltip("Fly through everything (F5 > Gameplay).")]
        public bool noclip;
        [Range(1f, 12f)] public float noclipSpeed = 4f;

        /// <summary>Turns noclip on/off. Turning it off drops the player from wherever they are.</summary>
        public void SetNoclip(bool on)
        {
            noclip = on;
            _controller.enabled = !on;
            PlanarVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }

        /// <summary>Test hook: when set, used as WASD input (x strafe, y forward) regardless of focus.</summary>
        [System.NonSerialized] public Vector2? botInput;

        public void SetYaw(float yaw) { _yaw = yaw; transform.rotation = Quaternion.Euler(0f, yaw, 0f); }

        /// <summary>Set by in-game menus to suspend look, movement and cursor capture.</summary>
        public static bool InputBlocked;

        // Set by other systems each frame (e.g. PropGrabber while holding something).
        /// <summary>Multiplier on mouse look (the grabber takes most of the mouse while holding).</summary>
        [System.NonSerialized] public float lookScale = 1f;
        /// <summary>Multiplier on walking speed (heavy things slow you down).</summary>
        [System.NonSerialized] public float moveScale = 1f;
        /// <summary>No running (e.g. while dragging something).</summary>
        [System.NonSerialized] public bool noRun;
        /// <summary>How far the body bends down (m), e.g. to reach something on the floor.</summary>
        [System.NonSerialized] public float stoop;
        /// <summary>True when the game window has the mouse and keys this frame.</summary>
        public bool HasFocus { get; private set; }
        /// <summary>Movement carried over from the thing the player stands on this frame (an elevator cab); applied once.</summary>
        [System.NonSerialized] public Vector3 platformMotion;

        /// <summary>Turns the view by the given degrees (yaw right, pitch down), as mouse look would.</summary>
        public void AddLook(float yaw, float pitch)
        {
            _yaw += yaw;
            _pitch = Mathf.Clamp(_pitch + pitch, -pitchLimit, pitchLimitDown);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            ApplyPivotRotation();
        }
        public float Pitch => _pitch;

        CharacterController _controller;
        float _yaw, _pitch, _verticalVelocity;
        int _unblockFrame = -10;
        float _eye, _eyeVel, _eyeK = 40f, _eyeC = 11f;      // eye height spring
        /// <summary>The eye's height above the feet right now (stances ease between their heights, stooping lowers it).</summary>
        public float Eye => _eye;
        float _chargeT = -1f, _launchAt = -1f, _launchHeight;
        bool _wasGrounded = true;
        float _airMinVy, _hopCarry;
        float _zoomVel;
        readonly Collider[] _overlap = new Collider[8];

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _yaw = transform.eulerAngles.y;
            InputBlocked = false;
            _eye = standEye;
        }

        void OnEnable() => LockCursor(true);
        void OnDisable() => LockCursor(false);

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null) return;

            // A menu (e.g. F5 visual settings) owns the cursor and keys while open, and on the frame it closes.
            bool blocked = InputBlocked || Time.frameCount <= _unblockFrame + 1;
            if (InputBlocked) _unblockFrame = Time.frameCount;
            if (!blocked)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) LockCursor(false);
                else if (mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);
            }

            // Ignore input while the window is in the background (raw mouse deltas still arrive).
            bool hasFocus = !blocked && Cursor.lockState == CursorLockMode.Locked && (Application.isFocused || !requireWindowFocus);
            HasFocus = hasFocus;
            if (hasFocus) UpdateLook(mouse.delta.ReadValue() * FovMultiplier * lookScale);   // aim slows down when zoomed in
            if (hasFocus && keyboard.vKey.wasPressedThisFrame) SetNoclip(!noclip);             // V: fly through everything (quick testing)
            UpdateZoom(keyboard, hasFocus);
            UpdateStance(keyboard, hasFocus);
            UpdateMove(keyboard, hasFocus);
            UpdateLean(keyboard, hasFocus);
            UpdateEye();
        }

        // ---------------- Lean ----------------
        [Header("Lean")]
        [Tooltip("How far the head goes to the side when leaning (Z left, X right), metres.")] public float leanDistance = 0.32f;
        [Tooltip("How far the view rolls when fully leaned, degrees.")] public float leanRoll = 9f;
        float _leanInput, _leanAmount;
        /// <summary>Automated tests: lean input (-1..1) instead of the keys.</summary>
        [System.NonSerialized] public float? botLean;
        /// <summary>-1 leaned fully left .. 1 fully right (after walls have had their say).</summary>
        public float Lean => _leanAmount;
        readonly RaycastHit[] _leanHits = new RaycastHit[6];

        void UpdateLean(Keyboard kb, bool hasFocus)
        {
            float want = 0f;
            if (hasFocus && !noclip && CurrentStance != Stance.Crawl && !IsRunning)
            {
                if (kb.zKey.isPressed) want -= 1f;
                if (kb.xKey.isPressed) want += 1f;
            }
            if (botLean.HasValue) want = botLean.Value;
            _leanInput = Mathf.MoveTowards(_leanInput, want, Time.deltaTime * 3.5f);
            float eased = Mathf.Sign(_leanInput) * Mathf.SmoothStep(0f, 1f, Mathf.Abs(_leanInput));
            // Not into a wall: the head stops short of whatever is beside it.
            float dist = Mathf.Abs(eased) * leanDistance;
            if (dist > 0.001f)
            {
                Vector3 from = transform.TransformPoint(new Vector3(0f, _eye, 0f));
                Vector3 dir = transform.right * Mathf.Sign(eased);
                int n = Physics.SphereCastNonAlloc(from, 0.13f, dir, _leanHits, dist + 0.03f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (_leanHits[i].collider.transform.IsChildOf(transform) || _leanHits[i].distance <= 0f) continue;
                    dist = Mathf.Min(dist, Mathf.Max(0f, _leanHits[i].distance - 0.03f));
                }
            }
            float target = Mathf.Sign(eased) * dist / Mathf.Max(0.01f, leanDistance);
            _leanAmount = Mathf.MoveTowards(_leanAmount, target, Time.deltaTime * 4f);
            ApplyPivotRotation();
        }

        void ApplyPivotRotation()
        {
            if (cameraPitchPivot) cameraPitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, -_leanAmount * leanRoll);
        }

        // ---------------- Leash ----------------
        Vector3 _leashAnchor;
        float _leashRadius;
        int _leashFrame = -10;
        /// <summary>How hard (m/s) the player walked against the leash last frame (0 when not held back).</summary>
        public float LeashStrain { get; private set; }

        /// <summary>
        /// For this frame and the next: the feet may not go further than radius (horizontally) from anchor. A taut cord in
        /// hand calls it every frame; walking on against it shows in <see cref="LeashStrain"/>.
        /// </summary>
        public void Leash(Vector3 anchor, float radius)
        {
            _leashAnchor = anchor; _leashRadius = Mathf.Max(0f, radius); _leashFrame = Time.frameCount;
        }

        /// <summary>Moves the player instantly (spawning, room transitions) and resets look and velocity.</summary>
        public void Teleport(Vector3 position, float yaw, float pitch = 0f)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _controller.enabled = !noclip;
            _yaw = yaw; _pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimitDown); _verticalVelocity = 0f;
            PlanarVelocity = Vector3.zero;
            ApplyPivotRotation();
        }

        /// <summary>
        /// Moves the player without stopping them (through a crawl hole): new position, turned by yawDelta degrees, with the
        /// current speed, stance and pitch kept and turned along.
        /// </summary>
        public void Warp(Vector3 position, float yawDelta)
        {
            _controller.enabled = false;
            _yaw += yawDelta;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, _yaw, 0f));
            _controller.enabled = !noclip;
            PlanarVelocity = Quaternion.Euler(0f, yawDelta, 0f) * PlanarVelocity;
        }

        /// <summary>Like <see cref="Teleport"/>, but holds the player still (no falling) until floor collision exists below, for places whose chunks are still loading.</summary>
        public void TeleportSafe(Vector3 position, float yaw, float pitch = 0f)
        {
            Teleport(position, yaw, pitch);
            _holdForGround = 8f;
        }
        float _holdForGround;

        /// <summary>Degrees turned this frame (x = yaw, y = pitch), for the handheld look motion.</summary>
        public Vector2 LookDelta => _lookFrame == Time.frameCount ? _lookDelta : Vector2.zero;
        Vector2 _lookDelta;
        int _lookFrame = -1;

        void UpdateLook(Vector2 delta)
        {
            float py = _pitch;
            _yaw += delta.x * mouseSensitivity;
            _pitch += (invertY ? delta.y : -delta.y) * mouseSensitivity;
            float dp = Mathf.Clamp(_pitch, -pitchLimit, pitchLimitDown) - py;
            _lookDelta = new Vector2(delta.x * mouseSensitivity, dp);
            _lookFrame = Time.frameCount;
            _pitch = Mathf.Clamp(_pitch, -pitchLimit, pitchLimitDown);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            ApplyPivotRotation();
        }

        void UpdateMove(Keyboard keyboard, bool hasFocus)
        {
            Vector2 input = Vector2.zero;
            if (botInput.HasValue)
            {
                // Automated test driver (NavProbe): bypasses keyboard and focus.
                input = botInput.Value;
                hasFocus = true;
            }
            else if (hasFocus)
            {
                if (keyboard.wKey.isPressed) input.y += 1f;
                if (keyboard.sKey.isPressed) input.y -= 1f;
                if (keyboard.dKey.isPressed) input.x += 1f;
                if (keyboard.aKey.isPressed) input.x -= 1f;
            }
            input = Vector2.ClampMagnitude(input, 1f);

            if (noclip)
            {
                // Free flight along the view direction; no collision, no gravity.
                float up = 0f;
                if (hasFocus && keyboard.spaceKey.isPressed) up += 1f;
                if (hasFocus && (keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed)) up -= 1f;
                Transform view = cameraPitchPivot ? cameraPitchPivot : transform;
                float speed = noclipSpeed * (hasFocus && keyboard.leftShiftKey.isPressed ? 2f : 1f);
                Vector3 fly = (view.forward * input.y + view.right * input.x + Vector3.up * up) * speed;
                PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, fly, acceleration * speed * Time.deltaTime);
                transform.position += PlanarVelocity * Time.deltaTime;
                IsRunning = false;
                return;
            }

            if (_holdForGround > 0f)
            {
                _holdForGround -= Time.deltaTime;
                if (GroundBelow()) _holdForGround = 0f;
                else { _verticalVelocity = 0f; PlanarVelocity = Vector3.zero; return; }
            }

            // Running only makes sense standing and moving mostly forward.
            IsRunning = !noRun && hasFocus && CurrentStance == Stance.Stand && keyboard.leftShiftKey.isPressed && input.y > 0.1f;
            float targetSpeed = CurrentStance == Stance.Crawl ? crawlSpeed : CurrentStance == Stance.Crouch ? crouchSpeed : IsRunning ? runSpeed : walkSpeed;
            targetSpeed *= moveScale;
            Vector3 target = (transform.forward * input.y + transform.right * input.x) * targetSpeed;

            float rate = input.sqrMagnitude > 0.001f ? acceleration : deceleration;
            // After dropping into a crouch or crawl from a run the body carries forward for a moment.
            if (_hopCarry > 0f) { _hopCarry -= Time.deltaTime; rate *= 0.25f; }
            if (!_controller.isGrounded) rate *= 0.3f;                    // little control in the air
            PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, target, rate * Mathf.Max(targetSpeed, 1f) * Time.deltaTime);

            // Held back by a taut cord: no further out than it lets the hand go (pulled back in a little if past).
            LeashStrain = 0f;
            if (Time.frameCount - _leashFrame <= 2)
            {
                Vector3 off = transform.position - _leashAnchor; off.y = 0f;
                float dist = off.magnitude;
                if (dist > 1e-3f)
                {
                    Vector3 outDir = off / dist;
                    float room = Mathf.Max((_leashRadius - dist) / Mathf.Max(Time.deltaTime, 1e-4f), -0.35f);
                    float outward = Vector3.Dot(PlanarVelocity, outDir);
                    if (outward > room) PlanarVelocity -= outDir * (outward - room);
                    if (dist > _leashRadius - 0.04f) LeashStrain = Mathf.Max(0f, Vector3.Dot(target, outDir));
                }
            }

            if (platformMotion != Vector3.zero) { _controller.Move(platformMotion); platformMotion = Vector3.zero; }
            UpdateJump(keyboard, hasFocus);
            if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 motion = PlanarVelocity + Vector3.up * _verticalVelocity;
            bool wasGrounded = _controller.isGrounded;
            CollisionFlags flags = _controller.Move(motion * Time.deltaTime);
            // Walking down stairs: the controller does not step down by itself, so it would drop onto each tread and
            // dip and thud like a landing. If the ground was under us and a short way below is more, stay on it.
            if (wasGrounded && !_controller.isGrounded && _verticalVelocity <= 0f && SnapToGround(0.32f)) _verticalVelocity = -2f;

            // Bleed off velocity into walls so the player doesn't "stick" to them.
            if ((flags & CollisionFlags.Sides) != 0)
            {
                Vector3 actual = _controller.velocity; actual.y = 0f;
                PlanarVelocity = Vector3.ClampMagnitude(PlanarVelocity, actual.magnitude + 0.05f);
            }
            if ((flags & CollisionFlags.Above) != 0 && _verticalVelocity > 0f) _verticalVelocity = 0f;

            // Landing: the legs absorb it (the eye dips with the impact) and the step is heard.
            bool grounded = _controller.isGrounded;
            LandedThisFrame = 0f;
            if (!grounded) _airMinVy = Mathf.Min(_airMinVy, _controller.velocity.y);
            if (grounded && !_wasGrounded && _airMinVy < -1.5f)
            {
                LandedThisFrame = -_airMinVy;
                _eyeVel -= Mathf.Min(2.5f, -_airMinVy * 0.45f);
            }
            if (grounded) _airMinVy = 0f;
            _wasGrounded = grounded;
        }

        readonly RaycastHit[] _groundHits = new RaycastHit[8];

        /// <summary>Is there floor under the player (not the player's own capsule)?</summary>
        bool GroundBelow()
        {
            int n = Physics.RaycastNonAlloc(transform.position + Vector3.up * 0.6f, Vector3.down, _groundHits, 1.2f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (!_groundHits[i].collider.transform.IsChildOf(transform)) return true;
            return false;
        }

        bool SnapToGround(float maxDrop)
        {
            float r = _controller.radius * 0.9f;
            var origin = transform.position + Vector3.up * (r + _controller.skinWidth + 0.05f);
            if (!Physics.SphereCast(origin, r, Vector3.down, out var hit, maxDrop + 0.05f + _controller.skinWidth, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < 0.5f) return false;
            float drop = hit.distance - 0.05f;
            if (drop <= 0.001f) return false;
            _controller.Move(Vector3.down * drop);
            return _controller.isGrounded;
        }

        // ---------------- Stances ----------------

        /// <summary>Eye height for the current stance (before stooping, jumps and landing dips).</summary>
        public float BaseEye => EyeTarget;
        float EyeTarget => CurrentStance == Stance.Crawl ? crawlEye : CurrentStance == Stance.Crouch ? crouchEye : standEye;
        float HeightFor(Stance st) => st == Stance.Crawl ? crawlHeight : st == Stance.Crouch ? crouchHeight : standHeight;

        void UpdateStance(Keyboard kb, bool hasFocus)
        {
            if (!hasFocus || noclip) return;
            Stance want = CurrentStance;
            if (kb.cKey.wasPressedThisFrame) want = CurrentStance == Stance.Crouch ? Stance.Stand : Stance.Crouch;
            if (kb.leftCtrlKey.wasPressedThisFrame) want = CurrentStance == Stance.Crawl ? Stance.Crouch : Stance.Crawl;
            SetStanceEased(want);
        }

        /// <summary>Changes stance the way the keys do (eased, checking for room to stand). False if there is no room.</summary>
        public bool SetStanceEased(Stance want)
        {
            if (want == CurrentStance) return true;
            if (HeightFor(want) > HeightFor(CurrentStance) && !RoomToStand(HeightFor(want))) return false;   // ceiling too low

            bool fromRun = IsRunning || PlanarVelocity.magnitude > walkSpeed * 1.3f;
            if (fromRun && want != Stance.Stand)
            {
                // Sprinting into a crouch or crawl: a quick hop down, body carrying forward.
                _eyeK = 110f; _eyeC = 13f; _hopCarry = 0.45f;
                _eyeVel -= 1.2f;
            }
            else if (want == Stance.Crawl && CurrentStance == Stance.Stand)
            {
                _eyeK = 16f; _eyeC = 8f;          // slower: down through a crouch, then forward onto the hands
            }
            else { _eyeK = 28f; _eyeC = 10f; }    // crouching down or getting up, eased like a person
            CurrentStance = want;
            _controller.height = HeightFor(want);
            _controller.center = new Vector3(0f, _controller.height * 0.5f, 0f);
            return true;
        }

        /// <summary>Sets the stance directly, with the matching capsule (scripts and the benchmark; no room check).</summary>
        public void SetStance(Stance stance)
        {
            CurrentStance = stance;
            _controller.height = HeightFor(stance);
            _controller.center = new Vector3(0f, _controller.height * 0.5f, 0f);
        }

        bool RoomToStand(float height)
        {
            float r = _controller.radius * 0.95f;
            Vector3 top = transform.position + Vector3.up * (height - r);
            Vector3 low = transform.position + Vector3.up * (_controller.height + 0.02f);
            if (low.y >= top.y) return true;
            int n = Physics.OverlapCapsuleNonAlloc(low, top, r, _overlap, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (_overlap[i] != _controller) return false;   // ignore our own capsule
            return true;
        }

        void UpdateEye()
        {
            if (!cameraPitchPivot) return;
            float dt = Time.deltaTime;
            float charge = _chargeT >= 0f ? Mathf.Clamp01((Time.time - _chargeT) / Mathf.Max(0.05f, chargeTime)) : 0f;
            // Knees bend while a jump charges (and briefly before a tap hop).
            float target = EyeTarget - charge * 0.2f - (_launchAt >= 0f ? 0.06f : 0f);
            target -= Mathf.Clamp(stoop, 0f, Mathf.Max(0f, EyeTarget - 0.6f));     // bending down to reach something low
            // Small fixed substeps keep the spring stable even on a long frame.
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / (1f / 240f)), 1, 60);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float acc = (target - _eye) * _eyeK - _eyeVel * _eyeC;
                _eyeVel += acc * h;
                _eye += _eyeVel * h;
            }
            _eye = Mathf.Clamp(_eye, 0.25f, standEye + 0.3f);
            if (Mathf.Abs(target - _eye) < 0.01f && Mathf.Abs(_eyeVel) < 0.05f) { _eyeK = 40f; _eyeC = 11f; }
            var lp = cameraPitchPivot.localPosition;
            // Leaning: the head goes to the side and a little down.
            cameraPitchPivot.localPosition = new Vector3(_leanAmount * leanDistance, _eye - Mathf.Abs(_leanAmount) * 0.045f, lp.z);
        }

        // ---------------- Jump ----------------

        void UpdateJump(Keyboard kb, bool hasFocus)
        {
            bool grounded = _controller.isGrounded;
            if (hasFocus && grounded && CurrentStance != Stance.Crawl && kb.spaceKey.wasPressedThisFrame) _chargeT = Time.time;
            if (_chargeT >= 0f && (!hasFocus || kb.spaceKey.wasReleasedThisFrame))
            {
                float held = Time.time - _chargeT;
                float charge = Mathf.Clamp01(held / Mathf.Max(0.05f, chargeTime));
                _launchHeight = Mathf.Lerp(hopHeight, chargedJumpHeight, charge * charge);
                if (CurrentStance == Stance.Crouch) _launchHeight *= 0.8f;
                // A tap still needs a moment to bend the knees before leaving the ground.
                _launchAt = Time.time + Mathf.Max(0f, 0.1f - held);
                _chargeT = -1f;
            }
            if (_launchAt >= 0f && Time.time >= _launchAt)
            {
                _launchAt = -1f;
                if (grounded)
                {
                    _verticalVelocity = Mathf.Sqrt(2f * -gravity * _launchHeight);
                    _eyeVel += 0.6f;                   // legs extend
                }
            }
        }

        // ---------------- Zoom ----------------

        void UpdateZoom(Keyboard kb, bool hasFocus)
        {
            // A camcorder zoom motor: ramps up to a steady speed and eases to a stop at the ends.
            bool zoomIn = hasFocus && kb.fKey.isPressed;
            float targetVel = zoomIn ? (Zoom < 1f ? 1f : 0f) : (Zoom > 0f ? -1f : 0f);
            _zoomVel = Mathf.MoveTowards(_zoomVel, targetVel, Time.deltaTime * 4f);
            // Slow down approaching either end, like a lens reaching its stop.
            float edge = _zoomVel > 0f ? 1f - Zoom : Zoom;
            float ease = Mathf.Clamp01(edge / 0.08f) * 0.8f + 0.2f;
            Zoom = Mathf.Clamp01(Zoom + _zoomVel * ease * zoomRate * Time.deltaTime);
            if ((Zoom <= 0f && _zoomVel < 0f) || (Zoom >= 1f && _zoomVel > 0f)) _zoomVel = 0f;
            ZoomMotor = _zoomVel * ease;
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
