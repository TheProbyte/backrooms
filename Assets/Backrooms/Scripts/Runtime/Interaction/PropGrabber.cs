using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The player's free (left) hand: grab, drag, tip and shove movable props. The right hand keeps the
    /// camcorder, so it is always one-handed.
    ///
    /// Hold left mouse on a prop within reach (step in automatically if it is a little too far). The hand
    /// takes the nearest grab edge to where you look (with a little variation, never the identical spot).
    /// While holding:
    ///  - mouse left/right swings your end of the prop sideways; past the arm's limit you turn with it
    ///  - mouse up/down lifts or lowers your end; lift far enough and a long thing tips up on its end
    ///  - WASD walks and drags it with you, slower the heavier it is
    ///  - right mouse shoves it away and lets go; release left mouse to let go
    /// The hand pulls with a limited, spring-like force at the grip point, so heavy things lag, scrape and
    /// pivot about whatever stays on the floor, and nothing goes through walls.
    /// Right mouse without holding gives a quick push. Walking into light props nudges them.
    /// </summary>
    [RequireComponent(typeof(CamcorderPlayerController))]
    public class PropGrabber : MonoBehaviour
    {
        [Header("Reach")]
        public float lookRange = 2.4f;
        [Tooltip("Arm reach from the shoulder (m).")] public float reach = 0.78f;
        [Tooltip("Shoulder (left) relative to the eye, in the body's frame.")] public Vector3 shoulderOffset = new Vector3(-0.2f, -0.28f, -0.08f);

        [Header("Strength")]
        [Tooltip("Most the hand can pull or push sideways (N).")] public float maxPush = 650f;
        [Tooltip("Most the hand can lift (N).")] public float maxLift = 480f;
        [Tooltip("Hold stiffness (rad/s of the hand's spring).")] public float stiffness = 11f;
        [Tooltip("Props up to this mass (kg) are carried rather than dragged (boxes).")] public float carryMass = 12f;

        [Header("Controls")]
        [Tooltip("Metres the held end moves per mouse count.")] public float mouseToMetres = 0.0011f;
        [Tooltip("Mouse look left while holding (the rest of the mouse moves the prop).")] public float lookWhileHolding = 0.1f;
        [Tooltip("Shove impulse (N s), capped at 3.5 m/s of change.")] public float shove = 65f;

        // Read by the first-person body (hand IK) and the HUD.
        public enum State { Idle, Reaching, Holding }
        public State Current { get; private set; }
        public MovableProp Held { get; private set; }
        /// <summary>How far the arm is out (0 resting, 1 at the grip), eased.</summary>
        public float HandBlend { get; private set; }
        public Vector3 HandPosition { get; private set; }
        /// <summary>Hand frame: forward = along the fingers, up = back of the hand.</summary>
        public Quaternion HandRotation { get; private set; }
        /// <summary>Finger curl 0 (flat) .. 1 (fist).</summary>
        public float HandCurl { get; private set; }
        /// <summary>0..1: how hard the arm is working.</summary>
        public float Strain { get; private set; }
        public Vector3 Shoulder => Eye + transform.rotation * shoulderOffset;
        /// <summary>Where the shoulder would be standing upright: what the held position is measured from, so bending
        /// down to follow a low hand never moves the hand itself.</summary>
        public Vector3 StandShoulder => transform.position + Vector3.up * _player.BaseEye + transform.rotation * shoulderOffset;
        Vector3 Eye => _player.cameraPitchPivot ? _player.cameraPitchPivot.position : transform.position + Vector3.up * 1.58f;

        // Automated tests (bypass the mouse).
        [System.NonSerialized] public bool? botHold;
        [System.NonSerialized] public Vector2 botMouse;
        [System.NonSerialized] public bool botShove;

        CamcorderPlayerController _player;
        HandheldCameraShake _shake;
        Camera _cam;
        readonly System.Random _rng = new System.Random();
        readonly RaycastHit[] _hits = new RaycastHit[8];

        MovableProp _target;
        Vector3 _gripLocal, _normalLocal, _edgeLocal;
        Vector3 _offset;                 // wanted grip position: shoulder + yaw * _offset
        Quaternion _carryRel;            // carried props keep this rotation relative to the body
        float _reachT, _slipT, _savedAngularDamping;
        Vector3 _force;
        float _lag;                      // metres the grip trails where the hand wants it

        void Awake()
        {
            _player = GetComponent<CamcorderPlayerController>();
        }

        void Start()
        {
            _cam = Camera.main;
            _shake = FindAnyObjectByType<HandheldCameraShake>();
        }

        void OnDisable() => Release();

        void Update()
        {
            var mouse = Mouse.current;
            bool focus = _player.HasFocus || botHold.HasValue;
            bool hold = botHold ?? (focus && mouse != null && mouse.leftButton.isPressed);
            bool pressed = botHold.HasValue ? hold && Current == State.Idle : focus && mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool shovePressed = botShove || (focus && mouse != null && mouse.rightButton.wasPressedThisFrame);
            botShove = false;
            Vector2 md = botHold.HasValue ? botMouse : focus && mouse != null ? mouse.delta.ReadValue() : Vector2.zero;

            switch (Current)
            {
                case State.Idle:
                    if (pressed) TryStart();
                    else if (shovePressed) Push();
                    break;
                case State.Reaching:
                    if (!hold || !_target) { Cancel(); break; }
                    UpdateReach();
                    break;
                case State.Holding:
                    if (!hold || !Held) { Release(!hold ? "let go" : "prop gone"); break; }
                    if (shovePressed) { ShoveAndRelease(); break; }
                    UpdateHold(md);
                    break;
            }
            UpdateStoop();
            UpdateHandPose();

            // Camcorder hand: dips while the other hand works, trembles with effort.
            if (_shake)
            {
                float m = Held ? Held.Mass : 0f;
                _shake.dip = Current == State.Holding ? Mathf.Lerp(0.2f, 1f, Mathf.Clamp01((m - 4f) / 40f)) : Current == State.Reaching ? 0.15f : 0f;
                _shake.effort = Current == State.Holding ? Strain * Mathf.Clamp01(m / 25f) : 0f;
            }
        }

        /// <summary>Bend down for low things: the shoulder goes no more than ~0.35 m above the hand.</summary>
        void UpdateStoop()
        {
            float want = 0f;
            if (Current != State.Idle && _target)
            {
                float handY = Current == State.Holding ? (StandShoulder + transform.rotation * _offset).y : _target.transform.TransformPoint(_gripLocal).y;
                want = Mathf.Clamp(StandShoulder.y - (handY + 0.35f), 0f, 0.8f);
            }
            _player.stoop = Mathf.MoveTowards(_player.stoop, want, Time.deltaTime * (want > _player.stoop ? 1.6f : 1.0f));
        }

        // ---------------- Grab ----------------

        bool Look(float range, out RaycastHit hit, out MovableProp prop)
        {
            prop = null;
            var cam = _cam ? _cam.transform : transform;
            hit = default;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].distance >= best) continue;   // not your own body
                best = _hits[i].distance; hit = _hits[i];
            }
            if (best == float.MaxValue) return false;
            prop = hit.collider.GetComponentInParent<MovableProp>();
            return prop;
        }

        void TryStart()
        {
            if (!Look(lookRange, out var hit, out var prop)) return;
            prop.FindGrip(hit.point, hit.normal, Shoulder, _rng, out var grip, out var normal, out var edge);
            _target = prop;
            _gripLocal = prop.transform.InverseTransformPoint(grip);
            _normalLocal = prop.transform.InverseTransformDirection(normal);
            _edgeLocal = prop.transform.InverseTransformDirection(edge);
            _reachT = 0f;
            Current = State.Reaching;
        }

        void UpdateReach()
        {
            Vector3 grip = _target.transform.TransformPoint(_gripLocal);
            Vector3 toGrip = grip - Shoulder;
            _reachT += Time.deltaTime;
            if (toGrip.magnitude <= reach + 0.08f)
            {
                _player.botInput = null;
                if (HandBlend > 0.85f) Attach();
                return;
            }
            // A little too far: step in (gives up after a moment if something is in the way).
            if (_reachT > 2f || toGrip.magnitude > lookRange + 0.5f) { Cancel(); return; }
            // Too far out in front: step in. (Too far down: the stoop brings the shoulder lower.)
            Vector3 local = transform.InverseTransformDirection(toGrip); local.y = 0f;
            var flat = new Vector2(local.x, local.z);
            _player.botInput = flat.magnitude > 0.3f ? flat.normalized * 0.6f : (Vector2?)null;
        }

        void Attach()
        {
            Held = _target;
            Held.Wake();
            Held.held = true;
            Current = State.Holding;
            _savedAngularDamping = Held.Body.angularDamping;
            Held.Body.angularDamping = Mathf.Max(_savedAngularDamping, Held.Mass > carryMass ? 1.2f : 3f);
            Vector3 grip = Held.transform.TransformPoint(_gripLocal);
            _offset = ClampOffset(Quaternion.Inverse(transform.rotation) * (grip - StandShoulder));
            _carryRel = Quaternion.Inverse(transform.rotation) * Held.transform.rotation;
            _slipT = 0f;
        }

        void Cancel()
        {
            _player.botInput = null;
            _target = null;
            Current = State.Idle;
        }

        public void Release() => Release("let go");

        /// <summary>Why the last hold ended (for debugging).</summary>
        public string LastRelease { get; private set; } = "";

        public void Release(string reason)
        {
            if (Current == State.Holding) LastRelease = reason;
            if (Held)
            {
                Held.held = false;
                Held.Body.angularDamping = _savedAngularDamping;
            }
            Held = null;
            _target = null;
            if (_player)
            {
                _player.botInput = null;
                _player.lookScale = 1f; _player.moveScale = 1f; _player.noRun = false;
            }
            Strain = 0f;
            Current = State.Idle;
        }

        void ShoveAndRelease()
        {
            var p = Held;
            Vector3 grip = p.transform.TransformPoint(_gripLocal);
            Release();
            Shove(p, grip, 1f);
        }

        void Push()
        {
            if (!Look(1.7f, out var hit, out var prop)) return;
            Shove(prop, hit.point, 0.6f);
        }

        void Shove(MovableProp p, Vector3 at, float strength)
        {
            p.Wake();
            var cam = _cam ? _cam.transform : transform;
            Vector3 dir = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized + Vector3.up * 0.08f;
            float j = Mathf.Min(shove * strength, p.Mass * 3.5f);
            p.Body.AddForceAtPosition(dir.normalized * j, at, ForceMode.Impulse);
        }

        // ---------------- Holding ----------------

        Vector3 ClampOffset(Vector3 o)
        {
            // Measured from the upright shoulder: anywhere from the floor (the body stoops) to a little above
            // the shoulder, and within an arm.s length around the body.
            float floor = transform.position.y + 0.03f - StandShoulder.y;
            o.x = Mathf.Clamp(o.x, -0.62f, 0.42f);
            o.y = Mathf.Clamp(o.y, floor, 0.5f);
            o.z = Mathf.Clamp(o.z, 0.15f, 0.8f);
            float flat = Mathf.Sqrt(o.x * o.x + o.z * o.z);
            if (flat > 0.82f) { o.x *= 0.82f / flat; o.z *= 0.82f / flat; }
            return o;
        }

        void UpdateHold(Vector2 md)
        {
            float k = mouseToMetres * _player.FovMultiplier;
            var want = _offset + new Vector3(md.x * k, md.y * k, 0f);
            var clamped = ClampOffset(want);
            // Past the arm's limit sideways the body turns with it (so you can keep swinging it round);
            // past the lift limit the view follows.
            float overX = (want.x - clamped.x) / k, overY = (want.y - clamped.y) / k;
            if (Mathf.Abs(overX) > 0f || Mathf.Abs(overY) > 0f)
                _player.AddLook(overX * _player.mouseSensitivity * 0.5f, -overY * _player.mouseSensitivity * 0.3f);
            _offset = clamped;

            float m = Held.Mass;
            _player.lookScale = lookWhileHolding;
            _player.noRun = true;
            float heavy = Mathf.Clamp01(1f - m / 110f);
            float scale = Mathf.Lerp(0.4f, 1f, heavy) * (1f - 0.5f * Mathf.Clamp01(_lag / 0.4f));
            // Pulling away from something stuck (the hand is well behind where it should be): you can barely move.
            Vector3 grip = Held.transform.TransformPoint(_gripLocal);
            Vector3 away = transform.position - grip; away.y = 0f;
            if (_lag > 0.3f && Vector3.Dot(_player.PlanarVelocity, away) > 0f) scale *= 0.3f;
            _player.moveScale = Mathf.Max(0.12f, scale);

            // Keep the hand in view: the camera eases toward the grip when it drifts far off centre.
            if (_cam)
            {
                Vector3 to = grip - Eye;
                float wantPitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg - 12f;
                float diff = wantPitch - _player.Pitch;
                if (Mathf.Abs(diff) > 14f) _player.AddLook(0f, Mathf.Sign(diff) * Mathf.Min(Mathf.Abs(diff) - 14f, 60f * Time.deltaTime));
            }

            // Lost grip: the prop got stuck or was pulled out of reach.
            float d = Vector3.Distance(grip, Shoulder);
            _slipT = d > reach + 0.45f ? _slipT + Time.deltaTime : 0f;
            if (_slipT > 0.25f) Release($"slipped (grip {d:F2} m from the shoulder)");
        }

        void FixedUpdate()
        {
            if (Current != State.Holding || !Held) return;
            var rb = Held.Body;
            if (rb.isKinematic) Held.Wake();
            Vector3 T = StandShoulder + transform.rotation * _offset;
            Vector3 G = rb.position + rb.rotation * _gripLocal;
            Vector3 vG = rb.GetPointVelocity(G);
            Vector3 vT = _player.PlanarVelocity;
            float m = rb.mass;
            bool carry = m <= carryMass && Held.Spec.kind == MovableKind.Box;
            float mEff = Mathf.Min(m, 30f), w = stiffness;
            Vector3 F = mEff * (w * w * (T - G) + 2f * w * (vT - vG));
            if (carry) F -= Physics.gravity * m;                 // a box is carried, not dragged
            // A hand is only so strong.
            Vector3 h = new Vector3(F.x, 0f, F.z);
            if (h.magnitude > maxPush) h = h.normalized * maxPush;
            float up = Mathf.Clamp(F.y, -200f, maxLift);
            F = h + Vector3.up * up;
            rb.AddForceAtPosition(F, G);
            _force = F;
            if (carry)
            {
                // Carried: it keeps its angle in the hand.
                Quaternion want = transform.rotation * _carryRel;
                (want * Quaternion.Inverse(rb.rotation)).ToAngleAxis(out float ang, out Vector3 axis);
                if (ang > 180f) ang -= 360f;
                if (axis.sqrMagnitude > 0.5f) rb.AddTorque(axis * (ang * Mathf.Deg2Rad * 80f) - rb.angularVelocity * 12f, ForceMode.Acceleration);
            }
            float err = Vector3.Distance(T, G);
            _lag = err;
            Strain = Mathf.Clamp01(Mathf.Max(err / 0.3f, F.magnitude / (maxPush * 1.1f)));
        }

        // ---------------- Hand pose (for the body / IK) ----------------

        void UpdateHandPose()
        {
            bool active = Current != State.Idle && _target;
            HandBlend = Mathf.MoveTowards(HandBlend, active ? 1f : 0f, Time.deltaTime * (active ? 3.2f : 2.5f));
            if (!_target) { HandCurl = Mathf.MoveTowards(HandCurl, 0.25f, Time.deltaTime * 2f); return; }
            Vector3 grip = _target.transform.TransformPoint(_gripLocal);
            Vector3 n = _target.transform.TransformDirection(_normalLocal).normalized;
            Vector3 edge = _target.transform.TransformDirection(_edgeLocal);
            Vector3 fingers;
            if (edge.sqrMagnitude > 0.01f)
            {
                // Across the edge, pointing away from the body: palm on top, fingers over the far side.
                fingers = Vector3.Cross(n, edge).normalized;
                if (Vector3.Dot(fingers, grip - Shoulder) < 0f) fingers = -fingers;
            }
            else fingers = Vector3.ProjectOnPlane(grip - Shoulder, n).normalized;
            if (fingers.sqrMagnitude < 0.01f) fingers = transform.forward;
            HandPosition = grip;
            HandRotation = Quaternion.LookRotation(fingers, n);
            // Fingers hook over an edge only where they reach past the outside of the prop; resting across
            // its top (the near edge) they lie nearly flat instead of sinking into it.
            Vector3 centre = _target.Body ? _target.Body.worldCenterOfMass : _target.transform.position;
            bool overOutside = Vector3.Dot(fingers, Vector3.ProjectOnPlane(centre - grip, n)) < 0f;
            float hook = edge.sqrMagnitude > 0.01f && overOutside ? 0.75f : 0.12f;
            float curl = Current == State.Holding ? hook + 0.15f * Strain : 0.2f;
            HandCurl = Mathf.MoveTowards(HandCurl, curl, Time.deltaTime * 3f);
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            // Walking into light props nudges them along.
            var p = hit.rigidbody ? hit.rigidbody.GetComponent<MovableProp>() : null;
            if (!p || p == Held || p == _target || p.Mass > 25f) return;     // not what the hand is reaching for
            Vector3 dir = hit.moveDirection; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f || _player.PlanarVelocity.sqrMagnitude < 0.04f) return;
            p.Wake();
            p.Body.AddForceAtPosition(dir.normalized * Mathf.Min(140f, p.Mass * 12f) * _player.PlanarVelocity.magnitude, hit.point);
        }

        void OnDrawGizmosSelected()
        {
            if (Current == State.Idle || !_player) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(Shoulder + transform.rotation * _offset, 0.04f);
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(Shoulder, HandPosition);
            Gizmos.DrawRay(HandPosition, _force * 0.002f);
        }
    }
}
