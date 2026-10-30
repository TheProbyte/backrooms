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

        /// <summary>The walk keys this frame (x strafe, y forward, at most 1 long), whether or not the body could move.</summary>
        public Vector2 MoveInput { get; private set; }

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

        // ---------------- Momentum ----------------
        [Header("Momentum")]
        [Tooltip("Getting up to speed walking: half-life of the gap (s). A critically damped spring, so speed builds and settles without a corner.")]
        public float accelHalfLife = 0.05f;
        [Tooltip("Getting up to a run (s): a little heavier.")] public float runAccelHalfLife = 0.08f;
        [Tooltip("Coming to a stop, or reversing (s): the short slide of a body carrying on.")] public float stopHalfLife = 0.07f;
        [Tooltip("In the air, what the keys can still change (s): little.")] public float airHalfLife = 0.45f;
        [Tooltip("A jump still works this long after walking off an edge (s).")] public float coyoteTime = 0.12f;
        [Tooltip("A jump pressed this long before landing happens on landing (s).")] public float jumpBuffer = 0.15f;
        [Tooltip("How far the view rolls into a turn at full sideways acceleration (degrees).")] public float turnRoll = 1.1f;
        Vector3 _moveAccel;
        float _lastGrounded = -10f, _jumpPressedAt = -10f;
        bool _jumpHeldInAir;
        Vector3 _lastPlanar;
        float _turnLean, _turnLeanVel;
        float _bumpCooldown;
        /// <summary>Leaning into a turn, -1..1 (+: the body tips to its right, as when turning right).</summary>
        public float TurnLean => _turnLean;
        /// <summary>Walked into something hard enough to feel it: the shove (world, m/s) and where (the contact point).</summary>
        public event System.Action<Vector3, Vector3> Bumped;

        [Tooltip("Walking speed on stairs, as a share of the flat's.")] public float stairSpeed = 0.8f;
        float _onStairs, _stairY, _stairTimer;
        /// <summary>0..1: walking a staircase (the floor stepping up or down under the feet in the last moment).</summary>
        public float OnStairs => _onStairs;

        void UpdateStairs(bool grounded)
        {
            // Stairs: the grounded height changing in steps (a ramp changes it smoothly too, but stairs are what this is
            // for; a ramp's slow walk is no harm).
            float y = transform.position.y;
            if (!grounded) { _stairY = y; _stairTimer = 0f; }
            else if (Mathf.Abs(y - _stairY) > 0.08f) { _stairY = y; _stairTimer = 0.6f; }
            _stairTimer -= Time.deltaTime;
            _onStairs = Mathf.MoveTowards(_onStairs, _stairTimer > 0f ? 1f : 0f, Time.deltaTime * 3f);
        }

        /// <summary>A shove that moves the feet a little (never knocks you over): added to the walking speed, at most 1 m/s.</summary>
        public void Push(Vector3 v)
        {
            v.y = 0f;
            PlanarVelocity += Vector3.ClampMagnitude(v, 1f);
        }
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
            if (cameraPitchPivot) cameraPitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, -_leanAmount * leanRoll - _turnLean * turnRoll);
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
            MoveInput = input;

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

            // Pulling up onto a ledge (Space at a ledge 0.45-1.35 m up): the body climbs, nothing else moves it.
            if (_mantleT >= 0f) { UpdateMantle(); return; }

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
            // Stairs are walked a little slower than the flat (a step a tread at a time each foot, as people do).
            targetSpeed *= Mathf.Lerp(1f, stairSpeed, _onStairs);
            Vector3 target = (transform.forward * input.y + transform.right * input.x) * targetSpeed;

            // Momentum: the speed follows the keys through a critically damped spring (it builds up and settles, keeps
            // its pace through a change of direction, and a stop slides a little); slower to change running, and in the
            // air hardly at all.
            bool pushing = input.sqrMagnitude > 0.001f;
            bool reversing = pushing && Vector3.Dot(PlanarVelocity, target) < 0f;
            float half = !pushing || reversing ? stopHalfLife : IsRunning ? runAccelHalfLife : accelHalfLife;
            // After dropping into a crouch or crawl from a run the body carries forward for a moment.
            if (_hopCarry > 0f) { _hopCarry -= Time.deltaTime; half *= 4f; }
            if (!_controller.isGrounded) half = airHalfLife;
            PlanarVelocity = Springs.Damper(PlanarVelocity, ref _moveAccel, target, half, Time.deltaTime);
            _moveAccel.y = 0f;
            // Leaning into a turn: the sideways part of the acceleration (as a body tips into a curve).
            {
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                Vector3 acc = (PlanarVelocity - _lastPlanar) / dt;
                _lastPlanar = PlanarVelocity;
                float side = Vector3.Dot(acc, transform.right);
                float want = _controller.isGrounded ? Mathf.Clamp(side / 9f, -1f, 1f) * Mathf.Clamp01(PlanarVelocity.magnitude / 1.2f) : 0f;
                _turnLean = Springs.Damper(_turnLean, ref _turnLeanVel, want, 0.12f, Time.deltaTime);
            }

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
                _moveAccel = Vector3.ClampMagnitude(_moveAccel, 2f);
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
                // A real drop (about a metre and more, into a pit room's shaft): the camcorder in the hand jolts down.
                if (-_airMinVy > 4.5f)
                {
                    if (!_shakeRef) _shakeRef = FindAnyObjectByType<HandheldCameraShake>();
                    float hard = Mathf.Clamp01((-_airMinVy - 4.5f) / 3.5f);
                    if (_shakeRef) _shakeRef.Jolt(new Vector3(9f + 10f * hard, (Random.value - 0.5f) * 8f * hard, (Random.value - 0.5f) * 10f * (0.4f + hard)));
                }
            }
            if (grounded) { _airMinVy = 0f; _lastGrounded = Time.time; }
            UpdateStairs(grounded);
            _wasGrounded = grounded;
            _bumpCooldown -= Time.deltaTime;
        }

        /// <summary>Walking into a wall, a door, furniture at speed: felt (the body reacts, FirstPersonBody.Bump).</summary>
        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (_bumpCooldown > 0f || Mathf.Abs(hit.normal.y) > 0.5f) return;
            Vector3 n = hit.normal; n.y = 0f; n.Normalize();
            float into = -Vector3.Dot(_lastPlanar, n);
            if (into < 1.1f) return;
            _bumpCooldown = 0.45f;
            Bumped?.Invoke(n * into, hit.point);
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
            UpdateHold(dt);
        }

        // ---------------- The camcorder held out ----------------
        [Header("Camcorder held out")]
        [Tooltip("How far in front of the eye the camcorder (the camera) is held, looking ahead and looking fully down (m).")]
        public float holdOut = 0.09f, holdOutDown = 0.11f;
        [Tooltip("How far below the eye it is held, looking ahead and looking fully down (m).")]
        public float holdDrop = 0.02f, holdDropDown = 0.04f;
        float _holdIn = 1f, _holdInVel;
        readonly RaycastHit[] _holdHits = new RaycastHit[6];

        /// <summary>
        /// The camcorder (the camera) relative to the eye, in the pitch pivot's frame. It is held a little out in front of
        /// the face, in the body's frame (looking down tilts it there rather than swinging it round the eye), so looking
        /// down shows the shoe tips, not the chest; it comes in toward the face when something is right in front. The eye
        /// (the pivot) stays where the head is: the hands, the body and the reach work from it.
        /// </summary>
        public Vector3 CamcorderHold { get; private set; }

        void UpdateHold(float dt)
        {
            float down = Mathf.Clamp01(_pitch / Mathf.Max(1f, pitchLimitDown));
            Vector3 want = new Vector3(0f, -Mathf.Lerp(holdDrop, holdDropDown, down), Mathf.Lerp(holdOut, holdOutDown, down));
            if (noclip) want = Vector3.zero;
            // Not into a wall: shorter when something is in front of the face (a sphere the size of the camcorder's front,
            // plus the near clip).
            float len = want.magnitude, room = len;
            if (len > 1e-3f)
            {
                Vector3 dir = transform.rotation * want / len;
                int n = Physics.SphereCastNonAlloc(cameraPitchPivot.position, 0.07f, dir, _holdHits, len + 0.05f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var h = _holdHits[i];
                    if (h.distance <= 0f || h.collider.transform.IsChildOf(transform) || h.rigidbody) continue;
                    room = Mathf.Min(room, Mathf.Max(0f, h.distance - 0.05f));
                }
            }
            float k = len > 1e-3f ? room / len : 1f;
            // In at once (no clipping), back out gently.
            _holdIn = k < _holdIn ? Mathf.Min(_holdIn, Mathf.Max(k, _holdIn - dt * 6f)) : Springs.Damper(_holdIn, ref _holdInVel, k, 0.15f, dt);
            if (k < _holdIn) _holdInVel = 0f;
            _holdIn = Mathf.Clamp01(_holdIn);
            CamcorderHold = Quaternion.Inverse(cameraPitchPivot.localRotation) * (want * _holdIn);
        }

        // ---------------- Mantle ----------------
        // Space at a ledge 0.45-1.35 m above the feet, close ahead, with room on top for the body: it pulls up onto it (up
        // the face, then over), about half a second; out of a pit room's shaft in two pulls (the block in its corner first).

        [Header("Mantle")]
        [Tooltip("Lowest and highest ledge (m above the feet) Space pulls you up onto; lower is an ordinary jump.")]
        public float mantleMin = 0.45f, mantleMax = 1.35f;
        [Tooltip("How long a pull up takes (s), a little more for a higher ledge.")] public float mantleTime = 0.5f;
        float _mantleT = -1f, _mantleDur;
        Vector3 _mantleFrom, _mantleTo;
        HandheldCameraShake _shakeRef;
        /// <summary>Pulling up onto a ledge now (for the body and tests).</summary>
        public bool Mantling => _mantleT >= 0f;
        /// <summary>Tests: a pull up asked for this frame (as Space).</summary>
        [System.NonSerialized] public bool botMantle;

        /// <summary>A ledge ahead to pull up onto: where the feet will stand on top of it.</summary>
        bool FindLedge(out Vector3 stand)
        {
            stand = default;
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 feet = transform.position;
            float r = _controller.radius;
            // Its top, a little way past the front of the body (from above, down to the feet).
            for (float reach = r + 0.2f; reach <= r + 0.5f; reach += 0.15f)
            {
                Vector3 from = feet + fwd * reach + Vector3.up * (mantleMax + 0.15f);
                if (!Physics.Raycast(from, Vector3.down, out var hit, mantleMax + 0.15f - mantleMin + 0.05f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < 0.75f) continue;
                float h = hit.point.y - feet.y;
                if (h < mantleMin || h > mantleMax) continue;
                // A face in front of the body (a ledge, not a slope or a lip of the floor).
                if (!Physics.Raycast(feet + Vector3.up * (h - 0.12f), fwd, reach + 0.1f, ~0, QueryTriggerInteraction.Ignore)) continue;
                // Room on top for the standing body a little past the edge: a slimmer body than the controller's, since a
                // walkway can be narrower than it (a pit room's are 0.48 m; the controller stands overhanging them).
                // (The top was found reach ahead, at least 0.2 m past the edge: a step more onto it.)
                Vector3 top = hit.point + fwd * 0.05f + Vector3.up * (_controller.skinWidth + 0.02f);
                const float slim = 0.2f;
                Vector3 a = top + Vector3.up * (slim + 0.02f), b = top + Vector3.up * (_controller.height - slim);
                if (Physics.CheckCapsule(a, b, slim, ~0, QueryTriggerInteraction.Ignore)) continue;
                stand = top;
                return true;
            }
            return false;
        }

        bool TryMantle()
        {
            if (CurrentStance == Stance.Crawl || !FindLedge(out var stand)) return false;
            _mantleFrom = transform.position; _mantleTo = stand;
            _mantleDur = mantleTime * Mathf.Lerp(0.85f, 1.2f, Mathf.InverseLerp(mantleMin, mantleMax, stand.y - transform.position.y));
            _mantleT = 0f;
            _verticalVelocity = 0f; PlanarVelocity = Vector3.zero; _moveAccel = Vector3.zero;
            _chargeT = -1f; _launchAt = -1f; _jumpPressedAt = -10f;
            _eyeVel -= 0.8f;                                     // the arms take the weight
            if (!_shakeRef) _shakeRef = FindAnyObjectByType<HandheldCameraShake>();
            if (_shakeRef) _shakeRef.Jolt(new Vector3(5f, 0f, (Random.value - 0.5f) * 4f));
            return true;
        }

        void UpdateMantle()
        {
            _mantleT += Time.deltaTime / Mathf.Max(0.1f, _mantleDur);
            float t = Mathf.Clamp01(_mantleT);
            // Up the face first (the hips over the edge), then over onto the top.
            float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.65f));
            float over = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.35f) / 0.65f));
            Vector3 want = new Vector3(Mathf.Lerp(_mantleFrom.x, _mantleTo.x, over), Mathf.Lerp(_mantleFrom.y, _mantleTo.y + 0.03f, up), Mathf.Lerp(_mantleFrom.z, _mantleTo.z, over));
            _controller.enabled = false;
            transform.position = want;
            _controller.enabled = true;
            PlanarVelocity = Vector3.zero; _verticalVelocity = 0f;
            if (t >= 1f)
            {
                _mantleT = -1f;
                _verticalVelocity = -2f;
                _airMinVy = 0f; _lastGrounded = Time.time;
                _eyeVel += 0.5f;
            }
        }

        // ---------------- Jump ----------------

        void UpdateJump(Keyboard kb, bool hasFocus)
        {
            // Forgiving: a jump still works a moment after walking off an edge (coyote time), and one pressed just before
            // landing happens on landing (buffered: held, it charges from the landing; let go, it is a hop).
            bool grounded = _controller.isGrounded || (Time.time - _lastGrounded < coyoteTime && _verticalVelocity <= 0f);
            bool pressed = (hasFocus && kb.spaceKey.wasPressedThisFrame) || botMantle;
            botMantle = false;
            // At a ledge (and in the air just after a jump at it): pull up onto it instead.
            if (pressed && TryMantle()) return;
            if (pressed && !grounded) { _jumpPressedAt = Time.time; _jumpHeldInAir = true; }
            if (_jumpHeldInAir && (!hasFocus || !kb.spaceKey.isPressed)) _jumpHeldInAir = false;
            if (grounded && _chargeT < 0f && _launchAt < 0f && Time.time - _jumpPressedAt < jumpBuffer && CurrentStance != Stance.Crawl)
            {
                _jumpPressedAt = -10f;
                if (_jumpHeldInAir) _chargeT = Time.time;
                else { _launchHeight = hopHeight; _launchAt = Time.time + 0.05f; }
            }
            if (hasFocus && grounded && CurrentStance != Stance.Crawl && pressed) _chargeT = Time.time;
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
                    _lastGrounded = -10f;                  // no second jump off the same coyote moment
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
