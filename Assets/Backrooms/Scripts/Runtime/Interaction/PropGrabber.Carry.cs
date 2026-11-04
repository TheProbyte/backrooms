using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Light things (a box light enough, an empty pallet, an office chair) are taken up like an item, not wrestled: hold the
    /// left mouse button on one and both hands take it, the camcorder on its strap, and it swings up into a set place for
    /// its kind and is held there steady (moved to where the hands hold it; a wall or a door frame stops it, and held back
    /// half a second it is dropped). Nothing else is in the hands meanwhile. Let go and it drops.
    /// <list type="bullet">
    /// <item>A box: upright in front of you, low (its bottom at the hips), a hand on each side. Mouse up lifts it (up to
    /// above your head, to stack it), down lowers it (to the floor); the view follows a little.</item>
    /// <item>An empty pallet: on its long edge under your arm, the top edge at hip height, a little ahead of you, so you see
    /// past it and it goes through doors; one hand, the camcorder stays up at the eye. A loaded one (anything resting on it)
    /// is heavy: pushed (PropGrabber.Furniture).</item>
    /// <item>An office chair: upright, a little off the floor, ahead of you on the hand's side, by the top of its back.</item>
    /// </list>
    /// </summary>
    public partial class PropGrabber
    {
        /// <summary>Carrying a light thing (see the class notes).</summary>
        public bool Carrying => Handling == Hold.Carry && Held;
        public bool CarryingPallet => Carrying && Held.Spec.kind == MovableKind.Pallet;

        /// <summary>Taken up and carried rather than pushed: a light box, an office chair, an empty pallet.</summary>
        public bool IsLight(MovableProp p) => p && ((p.Spec.kind == MovableKind.Box && p.Mass <= carryMass)
                                                     || p.Spec.kind == MovableKind.OfficeChair || p.Spec.kind == MovableKind.WoodenChair
                                                     || p.Spec.kind == MovableKind.LawnChair || p.Spec.kind == MovableKind.SchoolChair
                                                     || (p.Spec.kind == MovableKind.Pallet && !PalletLoaded(p)));

        /// <summary>An empty pallet, taken or being taken under the arm: one hand, the camcorder kept at the eye (round 14:
        /// it went down onto its strap, though the pallet is under one arm).</summary>
        bool UnderArm => Current == State.Holding ? CarryingPallet
                       : Current == State.Reaching && _target && _target.Spec.kind == MovableKind.Pallet && IsLight(_target);

        /// <summary>Pushing something as low as a pallet: the hands are not kept within the arm's reach (they push where the
        /// body is bent over it), nor does the body bend all the way down to it.</summary>
        bool LowPush => Pushing && Held.Spec.kind == MovableKind.Pallet;

        /// <summary>Reaching for a pallet's edge (14.5 cm up).</summary>
        bool LowGrab => _target && _target.Spec.kind == MovableKind.Pallet && !HeldItem;

        /// <summary>A pallet's middle when carried, in the body's frame (x toward the hand's side, metres from the feet).</summary>
        static readonly Vector3 PalletCarryCentre = new Vector3(0.4f, 0.56f, 0.42f);
        /// <summary>The hands on its top edge, along its length from its middle (m): the right nearer the body, the left ahead.</summary>
        const float PalletRightAlong = -0.34f, PalletLeftAlong = -0.1f;
        const float CarrySwingTime = 0.55f;

        Vector3 _carryFromPos;          // its pose when taken, in the body's frame (it swings up from there)
        Quaternion _carryFromRot;
        Vector3 _carryCentre;           // where its middle is carried (body frame) and how it is turned
        Quaternion _carryRot;
        float _carryT, _carryCaughtT, _carryArc;
        float _carryLift;               // a box: how far the mouse has lifted (+) or lowered (-) it from the carry (m)
        float _yawRate, _lastYaw;       // how fast the body turns (rad/s, smoothed), for what is carried
        bool _yawSet;

        /// <summary>The body's turn rate, smoothed over a few frames (each frame: Update).</summary>
        void UpdateYawRate()
        {
            float y = transform.eulerAngles.y, dt = Time.deltaTime;
            if (_yawSet && dt > 1e-5f)
            {
                float r = Mathf.DeltaAngle(_lastYaw, y) * Mathf.Deg2Rad / dt;
                if (Mathf.Abs(r) > 30f) r = 0f;                                   // a teleport, not a turn
                _yawRate = Mathf.Lerp(_yawRate, r, 1f - Mathf.Exp(-dt / 0.05f));
            }
            _lastYaw = y; _yawSet = true;
        }
        float _palTopSide;              // a pallet: +1 or -1, which long edge (its local z) is on top
        Collider[] _carryColliders;
        CharacterController _cc;

        /// <summary>Anything resting on the pallet (a box, another pallet, an item); false if it is not lying flat.</summary>
        public static bool PalletLoaded(MovableProp p)
        {
            if (!p || p.Spec.kind != MovableKind.Pallet) return false;
            if (Vector3.Angle(p.transform.up, Vector3.up) > 30f) return false;
            Vector3 c = p.transform.TransformPoint(new Vector3(0f, PalletGeometry.Height + 0.14f, 0f));
            var half = new Vector3(PalletGeometry.Length * 0.5f - 0.02f, 0.12f, PalletGeometry.Width * 0.5f - 0.02f);
            int n = Physics.OverlapBoxNonAlloc(c, half, s_palOver, p.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = s_palOver[i];
                if (col.transform.IsChildOf(p.transform)) continue;
                var rb = col.attachedRigidbody;
                if (rb && (rb.GetComponent<MovableProp>() || rb.GetComponent<WorldItem>())) return true;
            }
            return false;
        }
        static readonly Collider[] s_palOver = new Collider[24];

        /// <summary>On taking hold (AttachFurniture): a light thing is carried. Returns true if so.</summary>
        bool StartCarry()
        {
            if (!IsLight(Held)) return false;
            Handling = Hold.Carry;
            _carryT = 0f; _carryCaughtT = 0f; _carryLift = 0f;
            var inv = Quaternion.Inverse(transform.rotation);
            _carryFromPos = inv * (Held.Body.position - transform.position);
            _carryFromRot = inv * Held.Body.rotation;
            var b = Held.Shape.bounds;
            switch (Held.Spec.kind)
            {
                case MovableKind.Pallet:
                {
                    // The long edge nearer you goes on top; the deck faces out.
                    _palTopSide = Vector3.Dot(Held.transform.forward, transform.position - Held.transform.position) >= 0f ? 1f : -1f;
                    _carryRot = Quaternion.LookRotation(Vector3.up * _palTopSide, Vector3.right * Side * _palTopSide);
                    _carryCentre = new Vector3(PalletCarryCentre.x * Side, PalletCarryCentre.y, PalletCarryCentre.z);
                    _carryArc = 0.25f;
                    float topZ = _palTopSide * (PalletGeometry.Width * 0.5f - 0.01f);
                    SetGrips(new Vector3(PalletRightAlong, PalletGeometry.Height * 0.5f, topZ), new Vector3(0f, 0f, _palTopSide),
                             new Vector3(PalletLeftAlong, PalletGeometry.Height * 0.5f, topZ), new Vector3(0f, 0f, _palTopSide));
                    break;
                }
                case MovableKind.OfficeChair:
                case MovableKind.LawnChair:
                case MovableKind.SchoolChair:
                {
                    // Facing your way, its back toward you; ahead on the hand's side, 8 cm off the floor.
                    _carryRot = Quaternion.identity;
                    _carryCentre = new Vector3(0.3f * Side, b.extents.y + 0.08f, 0.32f + b.extents.z);
                    _carryArc = 0.1f;
                    float hw = Mathf.Min(b.extents.x, 0.25f) * 0.7f, y = b.max.y - 0.01f, z = b.min.z + 0.045f;
                    SetGrips(new Vector3(b.center.x + hw * Side, y, z), Vector3.up, new Vector3(b.center.x - hw * Side, y, z), Vector3.up);
                    break;
                }
                default:
                {
                    // A box: upright, turned to the nearest quarter of how it was turned to you; low in front, a hand each side.
                    float yaw = Mathf.Round(_carryFromRot.eulerAngles.y / 90f) * 90f;
                    _carryRot = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 s = Held.Spec.size;
                    Vector3 rightLocal = Quaternion.Inverse(_carryRot) * (Vector3.right * Side);
                    Vector3 fwdLocal = Quaternion.Inverse(_carryRot) * Vector3.forward;
                    float depth = Mathf.Abs(fwdLocal.x) * s.x + Mathf.Abs(fwdLocal.z) * s.z;
                    float halfAcross = (Mathf.Abs(rightLocal.x) * s.x + Mathf.Abs(rightLocal.z) * s.z) * 0.5f;
                    _carryCentre = new Vector3(0f, 0.72f + s.y * 0.5f, 0.3f + depth * 0.5f);
                    _carryArc = 0.1f;
                    Vector3 r = new Vector3(Mathf.Round(rightLocal.x), 0f, Mathf.Round(rightLocal.z));
                    Vector3 f = new Vector3(Mathf.Round(fwdLocal.x), 0f, Mathf.Round(fwdLocal.z));
                    // Toward the back of its sides, near you: at the middle of them the forearms came in through its back
                    // corners (round 15).
                    Vector3 back = -f * (depth * 0.5f - Mathf.Min(0.065f, depth * 0.25f));
                    float gy = s.y * 0.5f;
                    SetGrips(r * halfAcross + back + Vector3.up * gy, r, -r * halfAcross + back + Vector3.up * gy, -r);
                    break;
                }
            }
            TwoHanded = !UnderArm;
            _secondFarT = 0f;
            Held.Body.useGravity = false;
            // It swings past the body, not into it.
            if (!_cc) _cc = GetComponent<CharacterController>();
            _carryColliders = Held.GetComponentsInChildren<Collider>();
            if (_cc) foreach (var c in _carryColliders) if (c) Physics.IgnoreCollision(_cc, c, true);
            UpdateStrap();
            return true;

            void SetGrips(Vector3 a, Vector3 na, Vector3 bb, Vector3 nb)
            {
                _gripLocal = a; _normalLocal = na; _edgeLocal = Vector3.zero; _fingersLocal = Vector3.zero;
                _gripLocalB = bb; _normalLocalB = nb; _fingersLocalB = Vector3.zero; _hookB = false;
            }
        }

        /// <summary>Let go (Release): it drops; the body passes through it until it has stepped away.</summary>
        void EndCarry()
        {
            if (Handling != Hold.Carry) return;
            if (Held) Held.Body.useGravity = true;
            if (_carryColliders != null) { _passingProp = _carryColliders; _passingPropOwner = Held; }
            _carryColliders = null;
        }

        Collider[] _passingProp;
        MovableProp _passingPropOwner;
        float _savedStep = -1f;

        /// <summary>
        /// While the hands go for a pallet or hold it, the feet do not step up onto it: it is only 14.5 cm high, and walking
        /// into one to push it (or stepping in to reach it) the body climbed onto what it was pushing.
        /// </summary>
        void UpdatePalletStep()
        {
            if (!_cc) _cc = GetComponent<CharacterController>();
            if (!_cc) return;
            bool want = _target && _target.Spec.kind == MovableKind.Pallet && Current != State.Idle;
            if (want && _savedStep < 0f) { _savedStep = _cc.stepOffset; _cc.stepOffset = 0.04f; }
            else if (!want && _savedStep >= 0f) { _cc.stepOffset = _savedStep; _savedStep = -1f; }
        }

        /// <summary>A dropped carried thing is solid to the body again once the body is clear of it.</summary>
        void UpdateCarryPassing()
        {
            UpdatePalletStep();
            if (_passingProp == null) return;
            if (!_passingPropOwner || Carrying) { if (!_passingPropOwner) _passingProp = null; return; }
            var b = _passingPropOwner.WorldBounds;
            if (b.SqrDistance(transform.position + Vector3.up * 0.9f) < 0.45f * 0.45f) return;
            if (_cc) foreach (var c in _passingProp) if (c) Physics.IgnoreCollision(_cc, c, false);
            _passingProp = null; _passingPropOwner = null;
        }

        /// <summary>Where its origin goes now (world) and its rotation: swinging up from where it was taken.</summary>
        void CarryTarget(out Vector3 pos, out Quaternion rot)
        {
            Vector3 boundsMid = Held.Shape.bounds.center;
            Vector3 p = _carryCentre + CarryLiftOffset - _carryRot * boundsMid;
            float s = Mathf.SmoothStep(0f, 1f, _carryT);
            // (Up first, then over: lifted in an arc so it clears the floor as it turns.)
            Vector3 lp = Vector3.Lerp(_carryFromPos, p, s) + Vector3.up * (_carryArc * 4f * s * (1f - s));
            Quaternion lr = Quaternion.Slerp(_carryFromRot, _carryRot, s);
            pos = transform.position + transform.rotation * lp;
            rot = transform.rotation * lr;
        }

        /// <summary>
        /// A box lifted or lowered from its carry (body frame): up as far as its bottom 1.5 m above the feet (onto a stack),
        /// down to the floor; further out as it goes up or down, clear of the head and the knees. Measured from the eye, so
        /// crouched it is carried lower.
        /// </summary>
        Vector3 CarryLiftOffset => Held && Held.Spec.kind == MovableKind.Box ? new Vector3(0f, _carryLift + (_player.BaseEye - 1.58f), Mathf.Abs(_carryLift) * 0.18f) : Vector3.zero;

        /// <summary>The range of the lift for the box held: its bottom from 3 cm to 1.5 m above the feet.</summary>
        void CarryLiftRange(out float lo, out float hi)
        {
            float bottom = _carryCentre.y - Held.Spec.size.y * 0.5f + (_player.BaseEye - 1.58f);
            lo = 0.03f - bottom; hi = Mathf.Max(lo, 1.5f - bottom);
        }

        /// <summary>Each frame while carrying: the mouse turns you as when walking (a box: mouse up and down lifts and
        /// lowers it); held back too long, it drops.</summary>
        void UpdateCarry(Vector2 md)
        {
            _carryT = Mathf.Min(1f, _carryT + Time.deltaTime / CarrySwingTime);
            bool box = Held.Spec.kind == MovableKind.Box;
            if (box && _carryT >= 1f)
            {
                CarryLiftRange(out float lo, out float hi);
                _carryLift = Mathf.Clamp(_carryLift + md.y * mouseToMetres * 1.2f * _player.FovMultiplier, lo, hi);
            }
            // (Round 14, owner: to stack boxes, mouse up raises it. The view follows a third as far.)
            _player.pitchScale = box ? 0.35f : 1f;
            _player.lookScale = 0.75f;
            _player.moveScale = Mathf.Lerp(0.95f, 0.75f, Mathf.Clamp01(Held.Mass / 20f));
            _player.noRun = true;
            // The hold's own offset follows the right hand's spot (the body bends no lower than it needs to).
            CarryTarget(out var pos, out var rot);
            Vector3 grip = pos + rot * _gripLocal;
            _offset = Quaternion.Inverse(transform.rotation) * (grip - StandShoulder);
            float off = Vector3.Distance(pos, Held.Body.position);
            _carryCaughtT = off > 0.4f ? _carryCaughtT + Time.deltaTime : 0f;
            Strain = Mathf.Clamp01(0.15f + Held.Mass / 60f + off / 0.4f * 0.6f);
            if (_carryCaughtT > 0.5f) { Release("it caught on something"); Note("IT CAUGHT ON SOMETHING"); }
        }

        /// <summary>FixedUpdate while carrying: it is moved to where the hands hold it (steady, not a hand's limited pull); a
        /// wall or a door frame stops it as it would any moving thing.</summary>
        void CarryForces(Rigidbody rb)
        {
            CarryTarget(out var pos, out var rot);
            // The body's own motion (walking and turning) carried straight across, the gap closed on top: steered by the gap
            // alone it trailed a turn by ~10 degrees, and its back corner swung into the forearms (round 15). (From the body's
            // walk and turn rate, not the target's step-to-step change: physics steps fall unevenly between frames.)
            Vector3 wT = Vector3.up * _yawRate;
            Vector3 vT = _player.PlanarVelocity + Vector3.Cross(wT, pos - transform.position);
            Vector3 v = (pos - rb.position) / 0.07f + vT;
            rb.linearVelocity = Vector3.ClampMagnitude(v, 8f);
            (rot * Quaternion.Inverse(rb.rotation)).ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            Vector3 w = axis.sqrMagnitude > 0.5f && !float.IsNaN(axis.x) ? axis.normalized * (ang * Mathf.Deg2Rad / 0.09f) : Vector3.zero;
            rb.angularVelocity = Vector3.ClampMagnitude(w + wT, 12f);
            _force = Vector3.zero;
            _lag = Vector3.Distance(pos, rb.position);
        }
    }
}
