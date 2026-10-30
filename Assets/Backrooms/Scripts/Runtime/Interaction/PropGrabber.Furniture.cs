using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Handling furniture by physics (Docs/FurnitureHandlingPlan.md): nothing snaps, the hand only applies what a hand could.
    /// <list type="bullet">
    /// <item><b>A wrist.</b> Besides the pull at the grip, the hand turns what it holds toward the angle it took it at, with a
    /// limited torque (<see cref="wristTorque"/>): a light chair held by its frame keeps its angle; held by a leg it swings;
    /// a sofa barely notices. Past what the wrist can hold it gives (the angle it wants follows the prop).</item>
    /// <item><b>Turning on purpose.</b> Hold <b>R</b> while holding: the mouse turns it about your grip instead of moving your
    /// end (up and down tip it toward or away from you, left and right roll it onto a side). Gravity and the floor do the
    /// rest: past its balance point it goes over by itself.</item>
    /// <item><b>Two hands.</b> Taking furniture lets the camcorder hang on its strap at your chest (the picture drops and
    /// swings: HandheldCameraShake.strap) and frees the left hand: it is taken with both, a second grip across from the
    /// first. Twice the strength, and two grips make a lever, so it can be lifted and turned. Let go and it comes back up.</item>
    /// </list>
    /// </summary>
    public partial class PropGrabber
    {
        [Header("Furniture")]
        [Tooltip("Most a wrist can turn what it holds (N m). A 12 kg chair held 0.3 m from its balance point weighs 35 N m.")]
        public float wristTorque = 14f;
        [Tooltip("With both hands, most the two grips together can turn it (N m), on top of the lever they make.")]
        public float twoHandTorque = 60f;
        [Tooltip("Degrees the held thing turns per mouse count with R held.")]
        public float twistPerCount = 0.18f;
        [Tooltip("Second hand: how far from the first grip it looks for its own (m).")]
        public float secondGripSpread = 0.42f;

        /// <summary>Tests: R held this frame (twist).</summary>
        [System.NonSerialized] public bool? botTwist;

        /// <summary>The camcorder hangs on its strap at the chest (while furniture is taken or held): the left hand is free.</summary>
        public bool CamcorderOnStrap { get; private set; }
        /// <summary>Holding furniture with both hands right now (the left on <see cref="SecondGrip"/>).</summary>
        public bool TwoHanded { get; private set; }
        /// <summary>Twisting what it holds (R held).</summary>
        public bool Twisting { get; private set; }
        /// <summary>The second hand's grip (world) and the surface's normal there, while two-handed.</summary>
        public Vector3 SecondGrip => TwoHanded && Held ? Held.transform.TransformPoint(_gripLocalB) : Vector3.zero;
        public Vector3 SecondNormal => TwoHanded && Held ? Held.transform.TransformDirection(_normalLocalB) : Vector3.up;

        Quaternion _wristRel;            // the held thing's rotation relative to the body that the wrist keeps
        Vector3 _gripLocalB, _normalLocalB;
        float _secondFarT;

        float _strapOffAt;
        Collider[] _heldColliders;

        /// <summary>
        /// The camcorder goes onto its strap by itself while the hands take or hold furniture (both hands are needed), and
        /// comes back up a moment after letting go. While it hangs, the camera keeps out of what you hold.
        /// </summary>
        void UpdateStrap()
        {
            bool want = !HeldItem && _target && (Current == State.Reaching || Current == State.Holding);
            if (want) _strapOffAt = Time.time + 0.35f;
            bool on = want || (CamcorderOnStrap && Time.time < _strapOffAt);
            if (on != CamcorderOnStrap)
            {
                CamcorderOnStrap = on;
                MicThump();
                if (!on) TwoHanded = false;
            }
            if (Held && (_heldColliders == null || _heldColliders.Length == 0 || !_heldColliders[0] || !_heldColliders[0].transform.IsChildOf(Held.transform)))
                _heldColliders = Held.GetComponentsInChildren<Collider>();
            if (_shake)
            {
                _shake.strap = on ? 1f : 0f;
                _shake.avoid = on && Held ? _heldColliders : on && _target ? _target.GetComponentsInChildren<Collider>() : null;
            }
        }

        bool TwistHeld(bool focus)
        {
            var kb = Keyboard.current;
            Twisting = botTwist ?? (focus && kb != null && kb.rKey.isPressed);
            return Twisting;
        }

        /// <summary>
        /// The mouse turns the held thing: up/down tip it away or toward you, left/right roll it onto a side. It turns about
        /// its own balance point, so the hand (and the other hand) go round with it: pushing a chair's back away tips it
        /// over its far legs, the floor pushing back where it is in the way.
        /// </summary>
        void UpdateTwist(Vector2 md)
        {
            float k = twistPerCount * _player.FovMultiplier;
            var dq = Quaternion.AngleAxis(-md.x * k, Vector3.forward) * Quaternion.AngleAxis(md.y * k, Vector3.right);
            _player.lookScale = 0f;
            if (!Held) return;
            Quaternion yaw = transform.rotation;
            Vector3 T = StandShoulder + yaw * _offset;
            Quaternion dqW = yaw * dq * Quaternion.Inverse(yaw);
            // Standing on the floor: about the edge of its foot on the side it is tipped toward (rocked over, as you would:
            // its feet are not asked to slide). Lifted: one hand about its balance point (the arm pushes it round), two
            // about the hands (the wrists turn it, it swings round them).
            var wb = Held.WorldBounds;
            Vector3 C;
            if (wb.min.y < transform.position.y + 0.08f)
            {
                Vector3 mid = wb.center;
                Vector3 move = dqW * (T - mid) - (T - mid); move.y = 0f;
                C = new Vector3(mid.x, wb.min.y, mid.z);
                if (move.sqrMagnitude > 1e-8f)
                {
                    float best = float.MinValue;
                    for (int c = 0; c < 4; c++)
                    {
                        var corner = new Vector3((c & 1) == 0 ? wb.min.x : wb.max.x, wb.min.y, (c & 2) == 0 ? wb.min.z : wb.max.z);
                        // Toward the tip, along it: the middle of the bottom edge on that side.
                        float d = Vector3.Dot(corner - mid, move);
                        if (d > best + 1e-4f) { best = d; C = corner; }
                        else if (Mathf.Abs(d - best) <= 1e-4f) C = (C + corner) * 0.5f;
                    }
                    Vector3 m = move.normalized;
                    C = new Vector3(mid.x, wb.min.y, mid.z) + m * Vector3.Dot(C - new Vector3(mid.x, wb.min.y, mid.z), m);
                }
            }
            else C = TwoHanded ? T + yaw * _wristRel * ((_gripLocalB - _gripLocal) * 0.5f)
                               : T + yaw * _wristRel * (Held.Body.centerOfMass - _gripLocal);
            Vector3 Tn = C + dqW * (T - C);
            var newRel = dq * _wristRel;
            var newOffset = ClampOffset(Quaternion.Inverse(yaw) * (Tn - StandShoulder));
            _twistAt = Time.time;
            if (TwoHanded)
            {
                // Both hands go round with it, so it only turns as far as both can still reach (and the right hand's
                // spot is where it was asked to be: a clamped one would twist the two grips against each other). Out of
                // reach, you step toward where the hands have to go (as the hand steps you in to take something).
                Vector3 TA = StandShoulder + yaw * newOffset;
                Vector3 TB = TA + yaw * newRel * (_gripLocalB - _gripLocal);
                if ((TA - Tn).sqrMagnitude > 0.03f * 0.03f || Vector3.Distance(TB, LeftShoulder) > ArmReach + 0.2f)
                {
                    Vector3 need = Quaternion.Inverse(yaw) * ((Tn + TB) * 0.5f - (ReachShoulder + LeftShoulder) * 0.5f);
                    var flat = new Vector2(need.x, need.z);
                    if (flat.sqrMagnitude > 1e-4f) { _player.botInput = flat.normalized * 0.5f; _twistStepping = true; }
                    return;
                }
            }
            if (_twistStepping) { _player.botInput = null; _twistStepping = false; }
            _wristRel = newRel;
            _offset = newOffset;
        }

        /// <summary>When a hold starts: the wrist keeps the angle it was taken at; with the camcorder on its strap the
        /// left hand takes a second grip.</summary>
        void AttachFurniture()
        {
            _wristRel = Quaternion.Inverse(transform.rotation) * Held.transform.rotation;
            TwoHanded = false;
            _secondFarT = 0f;
            if (CamcorderOnStrap) TakeSecondGrip();
        }

        void ReleaseFurniture() { TwoHanded = false; Twisting = false; if (_twistStepping) { _twistStepping = false; } }

        bool _twistStepping;
        float _twistAt = -10f;
        /// <summary>Turning it just now (the grip does not count as slipping while the hands work it round).</summary>
        public bool TurningNow => Time.time - _twistAt < 0.6f;

        /// <summary>No longer turning: stop the steps the turn asked for.</summary>
        void EndTwistSteps() { if (_twistStepping && !Twisting) { _player.botInput = null; _twistStepping = false; } }

        /// <summary>The left (free) shoulder, about where the body's is.</summary>
        Vector3 LeftShoulder => ReachShoulder - transform.right * Side * 0.4f;

        /// <summary>
        /// The left hand's grip: the best spot on any grab edge for a left hand working with the right: to the right hand's
        /// left (seen from the body), 0.25 to 0.75 m from it, about as high, near the left shoulder, on an edge facing up or
        /// toward you rather than down. None if nothing fits (too small to take with two hands).
        /// </summary>
        void TakeSecondGrip()
        {
            if (!Held || Held.Shape == null) return;
            Vector3 a = Held.transform.TransformPoint(_gripLocal);
            Vector3 left = -(transform.right * Side);
            Vector3 ls = StandShoulder + left * 0.4f;
            float best = float.MinValue; Vector3 bp = default, bn = default;
            foreach (var e in Held.Shape.edges)
            {
                Vector3 ea = Held.transform.TransformPoint(e.a), eb = Held.transform.TransformPoint(e.b);
                Vector3 n = Held.transform.TransformDirection(e.normal);
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(ea, eb) / 0.04f));
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 p = Vector3.Lerp(ea, eb, i / (float)steps);
                    float d = Vector3.Distance(p, a);
                    if (d < 0.25f || d > 0.75f) continue;
                    float side = Vector3.Dot(p - a, left);
                    if (side < 0.12f) continue;
                    float score = Mathf.Min(side, 0.5f) - 0.6f * Mathf.Abs(p.y - a.y) - 0.35f * Vector3.Distance(p, ls)
                                - 0.3f * Mathf.Max(0f, -n.y) - 0.2f * Mathf.Max(0f, Vector3.Dot(n, (p - ls).normalized));
                    if (score > best) { best = score; bp = p; bn = n; }
                }
            }
            if (best == float.MinValue) return;
            _gripLocalB = Held.transform.InverseTransformPoint(bp);
            _normalLocalB = Held.transform.InverseTransformDirection(bn);
            _wristRel = Quaternion.Inverse(transform.rotation) * Held.transform.rotation;
            TwoHanded = true;
            _secondFarT = 0f;
        }

        /// <summary>How heavy the body feels pushed at a point along a direction (kg): its mass, less where a push there
        /// mostly turns it (a chair taken by the top of its back).</summary>
        static float PointMass(Rigidbody rb, Vector3 p, Vector3 dir)
        {
            Vector3 c = Vector3.Cross(p - rb.worldCenterOfMass, dir);
            Vector3 l = Quaternion.Inverse(rb.rotation * rb.inertiaTensorRotation) * c, I = rb.inertiaTensor;
            float inv = 1f / rb.mass + l.x * l.x / Mathf.Max(I.x, 1e-4f) + l.y * l.y / Mathf.Max(I.y, 1e-4f) + l.z * l.z / Mathf.Max(I.z, 1e-4f);
            return 1f / inv;
        }

        /// <summary>The body's moment of inertia about a world axis through its centre of mass (kg m^2).</summary>
        static float InertiaAbout(Rigidbody rb, Vector3 axis)
        {
            Vector3 l = Quaternion.Inverse(rb.rotation * rb.inertiaTensorRotation) * axis, I = rb.inertiaTensor;
            return l.x * l.x * I.x + l.y * l.y * I.y + l.z * l.z * I.z;
        }

        /// <summary>A hand's spring force worked out for mass <paramref name="mEff"/>, scaled down to what the body really
        /// weighs at that point, so a step never overshoots (springs tuned for the whole mass, pulling far from its middle,
        /// flipped it back and forth every physics step).</summary>
        static Vector3 AtPointMass(Rigidbody rb, Vector3 p, Vector3 F, float mEff)
        {
            float f = F.magnitude;
            if (f < 1e-4f) return F;
            float mp = PointMass(rb, p, F / f);
            return mp < mEff ? F * (mp / mEff) : F;
        }

        /// <summary>The second hand's pull and the wrist's (or two hands') turn, after the first hand's pull (FixedUpdate).</summary>
        void FurnitureForces(Rigidbody rb, Vector3 T, bool carry)
        {
            float m = rb.mass, mEff = Mathf.Min(m, 30f), w = stiffness;
            if (TwoHanded)
            {
                // The left hand's spot follows from the right's and the angle it is held at: the two never pull apart.
                Vector3 TB = T + transform.rotation * _wristRel * (_gripLocalB - _gripLocal);
                Vector3 B = rb.position + rb.rotation * _gripLocalB;
                Vector3 vB = rb.GetPointVelocity(B);
                Vector3 F = mEff * (w * w * (TB - B) + 2f * w * (_player.PlanarVelocity - vB));
                F = AtPointMass(rb, B, F, mEff);
                Vector3 h = new Vector3(F.x, 0f, F.z);
                if (h.magnitude > maxPush) h = h.normalized * maxPush;
                F = h + Vector3.up * Mathf.Clamp(F.y, -200f, maxLift);
                rb.AddForceAtPosition(F, B);
                // Too far for the left arm (it was turned or dragged away): it lets go, the right keeps hold.
                // (Not while the hands work it round: the grip lags the turn.)
                bool far = Vector3.Distance(B, LeftShoulder) > ArmReach + 0.35f;
                _secondFarT = far && !TurningNow ? _secondFarT + Time.fixedDeltaTime : far ? _secondFarT : 0f;
                if (_secondFarT > 0.6f) TwoHanded = false;
            }
            if (carry) return;                              // a carried box keeps its angle its own way
            // The wrist: toward the angle it holds, never more than a wrist (or two hands) can.
            Quaternion want = transform.rotation * _wristRel;
            (want * Quaternion.Inverse(rb.rotation)).ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            if (axis.sqrMagnitude < 0.5f || float.IsNaN(axis.x)) return;
            // Past what it can hold, the wrist gives: the angle it wants comes round to where the thing is.
            // Only which way it faces gives freely: how far it is tipped gives only once it is well over (knocked down), so
            // swinging round on the spot with it does not let it roll over and stay over.
            if (Mathf.Abs(ang) > 35f && !Twisting)
            {
                Quaternion actual = Quaternion.Inverse(transform.rotation) * rb.rotation;
                Vector3 upWant = _wristRel * Vector3.up, upHas = actual * Vector3.up;
                Quaternion faced = Quaternion.FromToRotation(upHas, upWant) * actual;     // its angle, tipped back as held
                Quaternion to = Vector3.Angle(upWant, upHas) > 60f ? actual : faced;
                _wristRel = Quaternion.Slerp(_wristRel, to, Time.fixedDeltaTime * 3f);
            }
            // Its real resistance to turning about this axis (a rough m * 0.12 was several times too much for a chair
            // and made the wrist overshoot every step: the chair shook).
            float I = Mathf.Min(m * 0.12f, InertiaAbout(rb, axis.normalized));
            float wr = 9f;
            Vector3 tau = I * (wr * wr * (ang * Mathf.Deg2Rad) * axis.normalized - 2f * wr * rb.angularVelocity);
            float cap = TwoHanded ? twoHandTorque : wristTorque;
            if (tau.magnitude > cap) tau = tau.normalized * cap;
            rb.AddTorque(tau, ForceMode.Force);
        }
    }
}
