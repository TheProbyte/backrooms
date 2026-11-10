using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Heavy things (a couch, an armchair, a table, a heavy box, a loaded pallet) by physics (Docs/FurnitureHandlingPlan.md):
    /// nothing snaps, the hands only apply what hands could. Two ways to take one, by where you take it:
    /// <list type="bullet">
    /// <item><b>By its top</b> (anywhere on top: the back, an arm, a side): both hands on the edge, the camcorder on its strap
    /// at your chest. <b>W</b> pushes it ahead, <b>S</b> pulls it back, <b>A/D</b> slide it; <b>the mouse turns you</b> and it
    /// turns with you (steering); <b>mouse up</b> lifts your end a little (a few centimetres, off its feet), down sets it back.
    /// That is all: it is pushed and steered, not carried.</item>
    /// <item><b>By its underside</b> (look at its bottom edge, or the low part of it): both hands under it. <b>Mouse up</b> tips
    /// it up about its far bottom edge (onto its back, or up on end if you took it by an end), <b>down</b> lowers it again; it
    /// goes as far as hands under it can take it, a quarter turn, and stops where something is in the way. Let go part way
    /// and it falls back (or on over, past its balance point).</item>
    /// </list>
    /// Light things (a light box, an empty pallet, an office chair) are carried instead (PropGrabber.Carry).
    /// </summary>
    public partial class PropGrabber
    {
        [Header("Furniture")]
        [Tooltip("Most a wrist can turn what it holds (N m).")]
        public float wristTorque = 14f;
        [Tooltip("With both hands, most the two grips together can turn it (N m), on top of the lever they make.")]
        public float twoHandTorque = 60f;
        [Tooltip("Second hand: how far from the first grip it looks for its own (m).")]
        public float secondGripSpread = 0.42f;
        [Tooltip("Pushing: most the hands on it, your weight behind it, can keep it upright (N m).")]
        public float pushTorque = 40f;
        [Tooltip("Pushing: how far ahead of its spot the hands go the way you walk (m).")]
        public float pushLead = 0.25f;
        [Tooltip("Pushing: most it is turned round with you about the vertical, steering (N m).")]
        public float pushTurnTorque = 160f;
        [Tooltip("Pushing with both hands on it (a lever between them): most it is turned round with you (N m). A 54 kg couch's " +
                 "feet hold it against about 220 N m.")]
        public float twoHandTurnTorque = 380f;
        [Tooltip("Pushing: how high the mouse lifts your end (m).")]
        public float pushLiftMax = 0.12f;
        [Tooltip("Tipping from the underside: degrees per mouse count.")]
        public float tipPerCount = 0.15f;
        Vector3 _pushLead;

        public enum Hold { None, Push, Tip, Carry }
        /// <summary>How the held prop is being handled (see the class notes, and PropGrabber.Carry).</summary>
        public Hold Handling { get; private set; }
        /// <summary>Pushing heavy furniture by its top.</summary>
        public bool Pushing => Handling == Hold.Push && Held;
        /// <summary>Tipping heavy furniture up from its underside.</summary>
        public bool Tipping => Handling == Hold.Tip && Held;

        /// <summary>Heavy: pushed or tipped, never carried (everything that is not light, PropGrabber.Carry.IsLight).</summary>
        bool IsFurniture(MovableProp p) => p && !IsLight(p);

        /// <summary>The camcorder hangs on its strap at the chest (while a prop is held with both hands): the left hand is free.</summary>
        public bool CamcorderOnStrap { get; private set; }
        /// <summary>Holding with both hands right now (the left on <see cref="SecondGrip"/>).</summary>
        public bool TwoHanded { get; private set; }
        /// <summary>The second hand's grip (world) and the surface's normal there, while two-handed.</summary>
        public Vector3 SecondGrip => TwoHanded && Held ? Held.transform.TransformPoint(_gripLocalB) : Vector3.zero;
        public Vector3 SecondNormal => TwoHanded && Held ? Held.transform.TransformDirection(_normalLocalB) : Vector3.up;
        /// <summary>Which way the second hand's fingers lie (world), or zero to leave it to the body.</summary>
        public Vector3 SecondFingers => TwoHanded && Held && _fingersLocalB.sqrMagnitude > 0.5f ? Held.transform.TransformDirection(_fingersLocalB) : Vector3.zero;
        /// <summary>How far the second hand's fingers close: round an edge they hook over, nearly flat on a face (round 14: always
        /// 0.8, the fingers went into a box's side and a couch's top).</summary>
        public float SecondCurl => !TwoHanded ? 0.3f : (_hookB ? 0.75f : 0.08f) + 0.08f * Strain;

        Quaternion _wristRel;            // the held thing's rotation relative to the body that the hands keep
        Vector3 _gripLocalB, _normalLocalB, _fingersLocalB;
        bool _hookB;
        float _secondFarT;

        float _strapOffAt;
        Collider[] _heldColliders;

        // Pushing: where the hands were taken (the lift is added on), how far up the mouse has lifted them.
        Vector3 _pushBase;
        float _pushLift, _pushFrom;
        // Tipping: the pose it was taken in, the edge it turns about, the angle wanted and the angle it is driven to.
        Vector3 _tipFromPos, _tipPivot, _tipAxis;
        Quaternion _tipFromRot;
        float _tipWant, _tipAngle, _tipBlockT;

        /// <summary>
        /// The camcorder goes onto its strap by itself while both hands hold a prop (and while reaching for a light one),
        /// and comes back up a moment after letting go. While it hangs, the camera keeps out of what you hold.
        /// </summary>
        void UpdateStrap()
        {
            // (Not for an empty pallet: one hand takes it under the arm, the camcorder stays at the eye, round 14.)
            bool want = !HeldItem && _target && (Current == State.Holding || (Current == State.Reaching && IsLight(_target))) && !UnderArm;
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
                // A box lifted above the hips: the strap camera tips up to keep it in the picture (to stack it).
                _shake.strapLook = on && Carrying && Held.Spec.kind == MovableKind.Box && _carryLift > 0.12f ? Held.WorldBounds.center : (Vector3?)null;
                // Carrying a box: the view goes where you look (to aim where it goes down), from nearer the eye.
                _shake.strapFollow = on && (CarryingBox || (Current == State.Reaching && _target && _target.Spec.kind == MovableKind.Box)) ? 0.9f : 0f;
            }
        }

        /// <summary>
        /// A grip that takes it from underneath (tipping) rather than by its top (pushing): an edge facing down, or low on
        /// it (the bottom fifth of its height).
        /// </summary>
        public static bool UnderGrip(MovableProp p, Vector3 grip, Vector3 normal)
        {
            if (!p) return false;
            var wb = p.WorldBounds;
            float f = wb.size.y > 1e-3f ? (grip.y - wb.min.y) / wb.size.y : 1f;
            return normal.y < -0.3f || f < 0.2f;
        }

        /// <summary>When a hold starts: carried if light, else pushed by its top or tipped from its underside.</summary>
        void AttachFurniture()
        {
            _wristRel = Quaternion.Inverse(transform.rotation) * Held.transform.rotation;
            TwoHanded = false;
            _secondFarT = 0f;
            Handling = Hold.None;
            if (StartCarry()) { ClearPlan(); return; }
            Vector3 grip = Held.transform.TransformPoint(_gripLocal), n = Held.transform.TransformDirection(_normalLocal);
            // Carried or tipped up, the hands go up with it: the squat and bend the reach planned are done with, and the body
            // stands as the hands rise (round 13: the plan's squat stayed on for the whole hold, the camera at the floor, and
            // did not rise as a couch was tipped up). Pushing, you stay leant over it as you took it (cleared, the body
            // straightened off a grip at arm's length and it slipped).
            if (UnderGrip(Held, grip, n)) { StartTip(); ClearPlan(); } else StartPush();
        }

        void ReleaseFurniture()
        {
            EndCarry();
            Handling = Hold.None;
            TwoHanded = false;
        }

        // ---------------- Pushing (by the top) ----------------

        void StartPush()
        {
            Handling = Hold.Push;
            _pushFrom = Time.time;
            _pushLift = 0f;
            // Exactly where the hands took it (not clamped toward the body: that pulled the grip and swung the couch round).
            _pushBase = Quaternion.Inverse(transform.rotation) * (Held.transform.TransformPoint(_gripLocal) - StandShoulder);
            _offset = _pushBase;
            TakeSecondGrip();
        }

        /// <summary>Each frame while pushing: mouse up lifts your end a little; the mouse turns you (and steers it).</summary>
        void UpdatePush(Vector2 md)
        {
            _pushLift = Mathf.Clamp(_pushLift + md.y * mouseToMetres * _player.FovMultiplier, 0f, pushLiftMax);
            _offset = _pushBase + Vector3.up * _pushLift;
            UpdateHold(Vector2.zero);
            PushLook();
        }

        /// <summary>Pushing: the mouse turns you (slower the heavier it is), and steers what you push.</summary>
        void PushLook()
        {
            if (!Held) return;
            _player.lookScale = Mathf.Lerp(0.85f, 0.35f, Mathf.Clamp01((Held.Mass - 10f) / 60f));
            // Taken looking down at it: the view comes up to look ahead over it (once, as the push starts).
            const float ahead = 22f;
            if (Time.time - _pushFrom < 0.9f && _player.Pitch > ahead)
                _player.AddLook(0f, -Mathf.Min(_player.Pitch - ahead, 70f * Time.deltaTime));
        }

        // ---------------- Tipping (from the underside) ----------------

        /// <summary>
        /// Taken from underneath: it will turn about its far bottom edge (the far side of its footprint, square to one of its
        /// own sides, from you), the near side coming up. The second hand goes under the same edge, to the left.
        /// </summary>
        void StartTip()
        {
            Handling = Hold.Tip;
            var rb = Held.Body;
            _tipFromPos = rb.position; _tipFromRot = rb.rotation;
            _tipWant = _tipAngle = 0f; _tipBlockT = 0f;
            // Away from you, along whichever of its own sides lies most that way (it tips square over an edge).
            Vector3 away = Flat(Held.WorldBounds.center - transform.position);
            away = away.sqrMagnitude > 1e-6f ? away.normalized : Flat(transform.forward).normalized;
            Vector3 best = away; float bestDot = -2f;
            var t = Held.transform;
            foreach (var ax in new[] { t.right, -t.right, t.up, -t.up, t.forward, -t.forward })
            {
                Vector3 f = Flat(ax);
                if (f.magnitude < 0.7f) continue;
                f.Normalize();
                float d = Vector3.Dot(f, away);
                if (d > bestDot) { bestDot = d; best = f; }
            }
            away = best;
            // The far bottom edge: the corners of its shape lowest down and furthest that way.
            var b = Held.Shape.bounds;
            var corners = new Vector3[8];
            float minY = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                corners[i] = t.TransformPoint(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z));
                minY = Mathf.Min(minY, corners[i].y);
            }
            float far = float.MinValue;
            foreach (var c in corners) if (c.y < minY + 0.05f) far = Mathf.Max(far, Vector3.Dot(c, away));
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var c in corners) if (c.y < minY + 0.05f && Vector3.Dot(c, away) > far - 0.05f) { sum += c; n++; }
            _tipPivot = n > 0 ? sum / n : Held.WorldBounds.center;
            _tipAxis = Vector3.Cross(Vector3.up, away).normalized;
            // The left hand under the same edge, 0.4 m to the left of the right (kept on it).
            Vector3 a = t.TransformPoint(_gripLocal);
            Vector3 edge = t.TransformDirection(_edgeLocal);
            if (edge.sqrMagnitude < 1e-4f) edge = -transform.right * Side;
            edge.Normalize();
            if (Vector3.Dot(edge, -transform.right * Side) < 0f) edge = -edge;
            Vector3 bp = ClosestOnHeld(a + edge * 0.4f);
            PlaceSecond(bp, t.TransformDirection(_normalLocal));
            TwoHanded = (t.TransformPoint(_gripLocalB) - a).magnitude > 0.15f;
        }

        Vector3 ClosestOnHeld(Vector3 p)
        {
            Vector3 best = p; float bd = float.MaxValue;
            foreach (var c in Held.GetComponentsInChildren<Collider>())
            {
                if (!c.enabled || c.isTrigger) continue;
                Vector3 q = c.ClosestPoint(p);
                float d = (q - p).sqrMagnitude;
                if (d < bd) { bd = d; best = q; }
            }
            return best;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>How far it has turned about the tipping edge since it was taken (degrees, up positive).</summary>
        float TipAngleNow()
        {
            (Held.Body.rotation * Quaternion.Inverse(_tipFromRot)).ToAngleAxis(out float a, out Vector3 ax);
            if (a > 180f) a -= 360f;
            if (ax.sqrMagnitude < 0.5f || float.IsNaN(ax.x)) return 0f;
            return a * Vector3.Dot(ax.normalized, _tipAxis);
        }

        /// <summary>Each frame while tipping: mouse up tips it further up, down lowers it; it is held there, turned at a
        /// pace its weight allows, and stops where it meets something.</summary>
        void UpdateTip(Vector2 md)
        {
            float dt = Time.deltaTime;
            _tipWant = Mathf.Clamp(_tipWant + md.y * tipPerCount * _player.FovMultiplier, 0f, 92f);
            float rate = Mathf.Lerp(80f, 30f, Mathf.Clamp01((Held.Mass - 10f) / 50f));
            _tipAngle = Mathf.MoveTowards(_tipAngle, _tipWant, rate * dt);
            // Held back (a wall, the ceiling, something on the floor): it stays where it got to.
            float now = TipAngleNow();
            _tipBlockT = Mathf.Abs(now - _tipAngle) > 12f ? _tipBlockT + dt : 0f;
            if (_tipBlockT > 0.3f) { _tipAngle = _tipWant = Mathf.Clamp(now, 0f, 92f); _tipBlockT = 0f; }
            _player.lookScale = 0.15f;
            _player.moveScale = 0.05f;
            _player.noRun = true;
            Vector3 grip = Held.transform.TransformPoint(_gripLocal);
            _offset = Quaternion.Inverse(transform.rotation) * (grip - StandShoulder);   // the body bends only as far as the hands are low
            Strain = Mathf.Clamp01(0.35f + Held.Mass / 90f);
            if (Vector3.Distance(grip, ReachShoulder) > ArmReach + 1.1f) Release("it went over, out of the hands");
        }

        /// <summary>FixedUpdate while tipping: it is turned about its edge to the angle the hands hold it at.</summary>
        void TipForces(Rigidbody rb)
        {
            var q = Quaternion.AngleAxis(_tipAngle, _tipAxis);
            DriveTo(rb, _tipPivot + q * (_tipFromPos - _tipPivot), q * _tipFromRot, 0.08f, 3f);
            _force = Vector3.zero;
        }

        /// <summary>Moves a body to a pose by setting its velocities (steady, it does not sag or swing; whatever is in the
        /// way still stops it).</summary>
        static void DriveTo(Rigidbody rb, Vector3 pos, Quaternion rot, float tau, float maxSpeed)
        {
            rb.linearVelocity = Vector3.ClampMagnitude((pos - rb.position) / tau, maxSpeed);
            (rot * Quaternion.Inverse(rb.rotation)).ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            rb.angularVelocity = axis.sqrMagnitude > 0.5f && !float.IsNaN(axis.x) ? Vector3.ClampMagnitude(axis.normalized * (ang * Mathf.Deg2Rad / (tau * 1.2f)), 6f) : Vector3.zero;
        }

        // ---------------- The second hand ----------------

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
            for (int side2 = 0; side2 < (e.normal2.sqrMagnitude > 0.5f ? 2 : 1); side2++)
            {
                Held.EdgeSide(e, side2 == 1, out Vector3 ea, out Vector3 eb, out Vector3 n);
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
            PlaceSecond(bp, bn);
            _wristRel = Quaternion.Inverse(transform.rotation) * Held.transform.rotation;
            TwoHanded = true;
            _secondFarT = 0f;
        }

        /// <summary>
        /// The left hand put on the model as the right one is (MovableProp.PlaceHand): on its real surface, the fingers the
        /// way that fits there, hooked only over an edge (round 14: it was left on the collider box, in the cushions, its
        /// fingers closed into whatever it held).
        /// </summary>
        void PlaceSecond(Vector3 p, Vector3 n)
        {
            n = n.normalized;
            if (Held.PlaceHand(p, n, LeftShoulder, out var palm, out var fingers, out bool hook))
            {
                p = palm; _fingersLocalB = Held.transform.InverseTransformDirection(fingers); _hookB = hook;
            }
            else { p = Held.OnSurface(p, n); _fingersLocalB = Vector3.zero; _hookB = false; }
            _gripLocalB = Held.transform.InverseTransformPoint(p);
            _normalLocalB = Held.transform.InverseTransformDirection(n);
        }

        // ---------------- Forces ----------------

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

        /// <summary>The second hand's pull and the hands' turn while pushing, after the first hand's pull (FixedUpdate).</summary>
        void FurnitureForces(Rigidbody rb, Vector3 T)
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
                F = h + Vector3.up * Mathf.Clamp(F.y, -200f, maxLift * PushLiftShare);
                rb.AddForceAtPosition(F, B);
                // Too far for the left arm (turned or dragged away): it lets go, the right keeps hold.
                bool far = Vector3.Distance(B, LeftShoulder) > ArmReach + 0.35f;
                _secondFarT = far ? _secondFarT + Time.fixedDeltaTime : 0f;
                if (_secondFarT > 0.6f) TwoHanded = false;
            }
            // The hands: toward the angle they took it at, never more than they can.
            Quaternion want = transform.rotation * _wristRel;
            (want * Quaternion.Inverse(rb.rotation)).ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            if (axis.sqrMagnitude < 0.5f || float.IsNaN(axis.x)) return;
            // Past what they can hold they give: which way it faces gives once it is well round (steering lags round a
            // corner); how far it is tipped only once it is well over (knocked down).
            if (Mathf.Abs(ang) > 70f)
            {
                Quaternion actual = Quaternion.Inverse(transform.rotation) * rb.rotation;
                Vector3 upWant = _wristRel * Vector3.up, upHas = actual * Vector3.up;
                Quaternion faced = Quaternion.FromToRotation(upHas, upWant) * actual;     // its angle, tipped back as held
                Quaternion to = Vector3.Angle(upWant, upHas) > 60f ? actual : faced;
                _wristRel = Quaternion.Slerp(_wristRel, to, Time.fixedDeltaTime * 3f);
            }
            // Its real resistance to turning about this axis (a rough m * 0.12 overshot every step: the chair shook).
            float I = Mathf.Min(m * 0.12f, InertiaAbout(rb, axis.normalized));
            // (About the vertical, what it takes to turn it against its feet on the floor: the spring was sized to its turning
            // inertia alone and asked for less than the friction holding it.)
            // (No more than three times its real inertia about the vertical: more and the damping overshoots every step.)
            float Iy = Mathf.Max(I, Mathf.Min(m * 0.5f, InertiaAbout(rb, Vector3.up) * 3f));
            float wr = 9f;
            Vector3 tau = I * (wr * wr * (ang * Mathf.Deg2Rad) * axis.normalized - 2f * wr * rb.angularVelocity);
            // Your weight swings it round with you: much stronger about the vertical (steering) than at tipping it.
            Vector3 turn = Vector3.Project(tau, Vector3.up) * (Iy / Mathf.Max(I, 1e-4f)), tip = tau - Vector3.Project(tau, Vector3.up);
            tau = Vector3.ClampMagnitude(turn, TwoHanded ? twoHandTurnTorque : pushTurnTorque) + Vector3.ClampMagnitude(tip, TwoHanded ? twoHandTorque : pushTorque);
            rb.AddTorque(tau, ForceMode.Force);
            // A couch pushed round never spins faster than you could swing it (about 90 degrees a second): whatever else
            // goes wrong, it does not whirl.
            var av = rb.angularVelocity;
            if (Mathf.Abs(av.y) > MaxPushSpin) { av.y = Mathf.Sign(av.y) * MaxPushSpin; rb.angularVelocity = av; }
        }
        const float MaxPushSpin = 1.6f;

        /// <summary>How much of a hand's lifting strength pushing uses: a steadying hold, up to all of it as the mouse
        /// lifts your end.</summary>
        float PushLiftShare => Mathf.Lerp(0.25f, 1f, pushLiftMax > 0f ? _pushLift / pushLiftMax : 0f);
    }
}
