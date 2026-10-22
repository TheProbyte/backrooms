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

        /// <summary>Turns the view by the given degrees (yaw right, pitch down), as mouse look would.</summary>
        public void AddLook(float yaw, float pitch)
        {
            _yaw += yaw;
            _pitch = Mathf.Clamp(_pitch + pitch, -pitchLimit, pitchLimit);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (cameraPitchPivot) cameraPitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
        public float Pitch => _pitch;

        CharacterController _controller;
        float _yaw, _pitch, _verticalVelocity;
        int _unblockFrame = -10;
        float _eye, _eyeVel, _eyeK = 40f, _eyeC = 11f;      // eye height spring
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
            UpdateZoom(keyboard, hasFocus);
            UpdateStance(keyboard, hasFocus);
            UpdateMove(keyboard, hasFocus);
            UpdateEye();
        }

        /// <summary>Moves the player instantly (spawning, room transitions) and resets look and velocity.</summary>
        public void Teleport(Vector3 position, float yaw, float pitch = 0f)
        {
            _controller.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            _controller.enabled = !noclip;
            _yaw = yaw; _pitch = pitch; _verticalVelocity = 0f;
            PlanarVelocity = Vector3.zero;
            if (cameraPitchPivot) cameraPitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        /// <summary>Degrees turned this frame (x = yaw, y = pitch), for the handheld look motion.</summary>
        public Vector2 LookDelta => _lookFrame == Time.frameCount ? _lookDelta : Vector2.zero;
        Vector2 _lookDelta;
        int _lookFrame = -1;

        void UpdateLook(Vector2 delta)
        {
            float py = _pitch;
            _yaw += delta.x * mouseSensitivity;
            _pitch += (invertY ? delta.y : -delta.y) * mouseSensitivity;
            float dp = Mathf.Clamp(_pitch, -pitchLimit, pitchLimit) - py;
            _lookDelta = new Vector2(delta.x * mouseSensitivity, dp);
            _lookFrame = Time.frameCount;
            _pitch = Mathf.Clamp(_pitch, -pitchLimit, pitchLimit);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (cameraPitchPivot) cameraPitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
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

            UpdateJump(keyboard, hasFocus);
            if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 motion = PlanarVelocity + Vector3.up * _verticalVelocity;
            CollisionFlags flags = _controller.Move(motion * Time.deltaTime);

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
            if (want == CurrentStance) return;
            if (HeightFor(want) > HeightFor(CurrentStance) && !RoomToStand(HeightFor(want))) return;   // ceiling too low

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
            cameraPitchPivot.localPosition = new Vector3(lp.x, _eye, lp.z);
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
