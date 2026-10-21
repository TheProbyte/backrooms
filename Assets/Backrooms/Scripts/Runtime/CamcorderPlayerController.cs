using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms
{
    /// <summary>
    /// First-person movement and mouse look for the camcorder viewpoint.
    /// Mouse look is applied directly (no smoothing) so aim stays responsive;
    /// the handheld shake is layered on a child transform by <see cref="HandheldCameraShake"/>.
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
        [Tooltip("How quickly the player reaches the target speed (higher = snappier).")]
        [Range(1f, 20f)] public float acceleration = 6f;
        [Tooltip("How quickly the player stops when no keys are held.")]
        [Range(1f, 20f)] public float deceleration = 8f;
        public float gravity = -19.6f;

        [Header("References")]
        [Tooltip("Pivot that receives pitch rotation (parent of the shake rig).")]
        public Transform cameraPitchPivot;

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

        CharacterController _controller;
        float _yaw, _pitch, _verticalVelocity;
        int _unblockFrame = -10;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _yaw = transform.eulerAngles.y;
            InputBlocked = false;
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
            if (hasFocus) UpdateLook(mouse.delta.ReadValue());
            UpdateMove(keyboard, hasFocus);
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

            // Running only makes sense moving mostly forward.
            IsRunning = hasFocus && keyboard.leftShiftKey.isPressed && input.y > 0.1f;
            float targetSpeed = IsRunning ? runSpeed : walkSpeed;
            Vector3 target = (transform.forward * input.y + transform.right * input.x) * targetSpeed;

            float rate = input.sqrMagnitude > 0.001f ? acceleration : deceleration;
            PlanarVelocity = Vector3.MoveTowards(PlanarVelocity, target, rate * targetSpeed * Time.deltaTime);

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
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
