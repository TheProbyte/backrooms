using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Light things (a box light enough, an empty pallet, an office chair) are taken up like an item, not wrestled: click
    /// one and both hands take it, the camcorder on its strap, and it swings up into a set place for its kind and is held
    /// there steady (moved to where the hands hold it; a wall or a door frame stops it). Nothing else is in the hands
    /// meanwhile. Click again to put it down (2026-11-08: no more holding the button the whole way).
    /// <list type="bullet">
    /// <item>A box: upright in front of you, low (its bottom at the hips), a hand on each side, bobbing as you walk and
    /// lagging a turn; a wall stops it but never knocks it out of your hands. Taken, put down, stacked in the arms and
    /// tossed: PropGrabber.Boxes.</item>
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
            _carrySwing = CarrySwingTime;
            _carrySway = Vector3.zero; _carrySwayVel = Vector3.zero; _turnLag = 0f; _turnLagVel = 0f; _fastTurnT = 0f;
            _stackLower = 0f; _stackLowerVel = 0f;
            _lastPlanar = _player.PlanarVelocity;
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
                    // (A long one, a flat-pack, with its length the way you go: it goes through doors, out ahead of you.)
                    Vector3 s = Held.Spec.size;
                    float yaw = Mathf.Round(_carryFromRot.eulerAngles.y / 90f) * 90f;
                    if (Mathf.Max(s.x, s.z) > 0.8f)
                    {
                        float along = s.x >= s.z ? 90f : 0f;
                        yaw = Mathf.Abs(Mathf.DeltaAngle(_carryFromRot.eulerAngles.y, along)) <= 90f ? along : along + 180f;
                    }
                    _carryRot = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 rightLocal = Quaternion.Inverse(_carryRot) * (Vector3.right * Side);
                    Vector3 fwdLocal = Quaternion.Inverse(_carryRot) * Vector3.forward;
                    float depth = Mathf.Abs(fwdLocal.x) * s.x + Mathf.Abs(fwdLocal.z) * s.z;
                    float halfAcross = (Mathf.Abs(rightLocal.x) * s.x + Mathf.Abs(rightLocal.z) * s.z) * 0.5f;
                    // (2026-11-08, owner: it "covers camera maybe a bit too much": 6 cm lower and 3 cm nearer.)
                    _carryCentre = new Vector3(0f, 0.66f + s.y * 0.5f, 0.27f + depth * 0.5f);
                    // Quick (about a third of a second), longer for a heavy one; off a high shelf straight out, not up.
                    _carrySwing = 0.24f + 0.009f * Held.Mass;
                    _carryArc = Held.WorldBounds.min.y - transform.position.y > 0.9f ? 0f : 0.08f;
                    Vector3 r = new Vector3(Mathf.Round(rightLocal.x), 0f, Mathf.Round(rightLocal.z));
                    Vector3 f = new Vector3(Mathf.Round(fwdLocal.x), 0f, Mathf.Round(fwdLocal.z));
                    // Toward the back of its sides, near you: at the middle of them the forearms came in through its back
                    // corners (round 15).
                    Vector3 back = -f * (depth * 0.5f - Mathf.Min(0.065f, depth * 0.25f));
                    float gy = s.y * 0.5f;
                    SetGrips(r * halfAcross + back + Vector3.up * gy, r, -r * halfAcross + back + Vector3.up * gy, -r);
                    if (HandTruck.Equipped)
                    {
                        // On the hand truck's nose plate (2026-11-09): low and out ahead, the hands on its handles (in the
                        // box's frame where it will ride, as the body carries both).
                        _carryCentre = new Vector3(0f, HandTruck.PlateTop + s.y * 0.5f, HandTruck.PlateZ + depth * 0.5f);
                        _carryArc = 0.06f;
                        var toBox = Quaternion.Inverse(_carryRot);
                        Vector3 origin = _carryCentre - _carryRot * b.center;
                        SetGrips(toBox * (HandTruck.Handle(Side) - origin), toBox * Vector3.up, toBox * (HandTruck.Handle(-Side) - origin), toBox * Vector3.up);
                    }
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

        /// <summary>Let go (Release): it drops; the body passes through it until it has stepped away. Boxes still on top of
        /// it fall.</summary>
        void EndCarry()
        {
            if (Handling != Hold.Carry) return;
            DropArmBoxes();
            if (Held) Held.Body.useGravity = true;
            if (_carryColliders != null) _letGo.Add((_carryColliders, Held));
            _carryColliders = null;
            _outShown = false; BoxTossCharge = 0f; _windUp = 0f; _lmbDownAt = -1f;
        }

        /// <summary>Things let go of near the body, passed through until it is clear of them.</summary>
        readonly System.Collections.Generic.List<(Collider[] cols, MovableProp owner)> _letGo = new System.Collections.Generic.List<(Collider[], MovableProp)>();
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
            for (int i = _letGo.Count - 1; i >= 0; i--)
            {
                var (cols, owner) = _letGo[i];
                if (!owner) { _letGo.RemoveAt(i); continue; }
                if (InArms(owner)) { _letGo.RemoveAt(i); continue; }           // (taken up again)
                var b = owner.WorldBounds;
                if (b.SqrDistance(transform.position + Vector3.up * 0.9f) < 0.45f * 0.45f) continue;
                if (_cc) foreach (var c in cols) if (c) Physics.IgnoreCollision(_cc, c, false);
                _letGo.RemoveAt(i);
            }
        }

        /// <summary>Where its origin goes now (world) and its rotation: swinging up from where it was taken.</summary>
        void CarryTarget(out Vector3 pos, out Quaternion rot) => CarryTarget(out pos, out rot, out _, out _);

        /// <summary>As above, and where its middle is carried and how it is turned once up (body frame: the boxes on top
        /// of it go from there).</summary>
        void CarryTarget(out Vector3 pos, out Quaternion rot, out Vector3 centreLocal, out Quaternion rotLocal)
        {
            Vector3 boundsMid = Held.Shape.bounds.center;
            bool box = Held.Spec.kind == MovableKind.Box;
            // A box sways: the bob of the walk, the lag of a turn (PropGrabber.Boxes).
            float wind = box ? Mathf.SmoothStep(0f, 1f, _windUp) : 0f;
            centreLocal = _carryCentre + CarryLiftOffset + (box ? _carrySway + Vector3.down * _stackLower + new Vector3(0.03f * Side, 0.07f, -0.13f) * wind : Vector3.zero);
            rotLocal = box ? Quaternion.Euler(-10f * wind, _turnLag, -_carrySway.x * 40f) * _carryRot : _carryRot;
            Vector3 p = centreLocal - rotLocal * boundsMid;
            float s = Mathf.SmoothStep(0f, 1f, _carryT);
            // (Up first, then over: lifted in an arc so it clears the floor as it turns.)
            Vector3 lp = Vector3.Lerp(_carryFromPos, p, s) + Vector3.up * (_carryArc * 4f * s * (1f - s));
            Quaternion lr = Quaternion.Slerp(_carryFromRot, rotLocal, s);
            pos = transform.position + transform.rotation * lp;
            rot = transform.rotation * lr;
        }

        /// <summary>
        /// A box lifted or lowered from its carry (body frame): up as far as its bottom 1.5 m above the feet (onto a stack),
        /// down to the floor; further out as it goes up or down, clear of the head and the knees. Measured from the eye, so
        /// crouched it is carried lower.
        /// </summary>
        Vector3 CarryLiftOffset => Held && Held.Spec.kind == MovableKind.Box && !HandTruck.Equipped ? new Vector3(0f, _carryLift + (_player.BaseEye - 1.58f), Mathf.Abs(_carryLift) * 0.18f) : Vector3.zero;

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
            _carryT = Mathf.Min(1f, _carryT + Time.deltaTime / _carrySwing);
            bool box = Held.Spec.kind == MovableKind.Box;
            // (2026-11-08: a box is no longer lifted with the mouse, round 14's way to stack: the outline puts it there.)
            if (box) UpdateBoxCarry();
            else
            {
                _player.pitchScale = 1f;
                _player.lookScale = 0.75f;
                _player.moveScale = Mathf.Lerp(0.95f, 0.75f, Mathf.Clamp01(Held.Mass / 20f));
                _player.noRun = true;
            }
            // The hold's own offset follows the right hand's spot (the body bends no lower than it needs to).
            CarryTarget(out var pos, out var rot);
            Vector3 grip = pos + rot * _gripLocal;
            _offset = Quaternion.Inverse(transform.rotation) * (grip - StandShoulder);
            float off = Vector3.Distance(pos, Held.Body.position);
            _carryCaughtT = off > 0.4f ? _carryCaughtT + Time.deltaTime : 0f;
            Strain = Mathf.Clamp01(0.15f + (box ? ArmWeight : Held.Mass) / 60f + off / 0.4f * 0.6f);
            if (box)
            {
                // (2026-11-08, owner: a wall stops a box, never knocks it out of your hands. Held back a while and its place
                // in the hands clear, it is back in them.)
                if (_carryCaughtT > 0.7f && PoseClear(Held, pos - rot * Vector3.zero, rot))
                {
                    Held.Body.position = pos; Held.Body.rotation = rot; Held.transform.SetPositionAndRotation(pos, rot);
                    _carryCaughtT = 0f;
                }
            }
            else if (_carryCaughtT > 0.5f) { Release("it caught on something"); Note(Backrooms.UI.Words.Get("note.caught")); }
        }

        /// <summary>FixedUpdate while carrying: it is moved to where the hands hold it (steady, not a hand's limited pull); a
        /// wall or a door frame stops it as it would any moving thing.</summary>
        void CarryForces(Rigidbody rb)
        {
            CarryTarget(out var pos, out var rot, out var centreLocal, out var rotLocal);
            if (_arm.Count > 0) ArmForces(centreLocal, rotLocal);
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
