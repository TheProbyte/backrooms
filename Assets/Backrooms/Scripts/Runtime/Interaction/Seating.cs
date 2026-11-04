using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Sitting down (2026-11-04; the owner chose chairs, sofas and armchairs; the brief: "comfort comes through ... sitting
    /// down", "voluntary sitting/resting provides a pause without fatigue mechanics"). With the hand empty, E on a chair, an
    /// armchair or a sofa within reach (standing on its feet) sits you on it: you turn to face the way it faces and go down
    /// onto it (its seat, or the place on a sofa nearest where you look: a sofa has two or three), your eye at a seated
    /// person's height over the seat. Seated you look about (about 110 degrees either way of its front); on an office chair
    /// the chair swivels with you past that, and W/A/S/D push it along on its casters a little. E or Space stands you up,
    /// in front of it where there is room (else beside or behind it); on anything but an office chair W does too. While you
    /// sit the seat stays where it is (frozen, saved where it is left); the hand still works (things within reach). The
    /// body (FirstPersonBody.SetSeat) sits: hips on the seat, feet on the floor in front of it.
    /// Similar to how first-person games handle seats: a constrained look with the body's pose driven to the seat, with
    /// the transitions eased rather than cut.
    /// </summary>
    [DefaultExecutionOrder(-20)]                     // before the player controller, which reads where the seat puts you
    public class Seating : MonoBehaviour
    {
        CamcorderPlayerController _pc;
        PropGrabber _grab;
        FirstPersonBody _body;

        /// <summary>What you sit on now, or null.</summary>
        public MovableProp Chair { get; private set; }
        public bool Seated => Chair;
        int _slot;
        float _t = 1f;                   // sitting down (0..1)
        float _standT = -1f;             // getting up (0..1), or -1
        Vector3 _fromFeet, _standFeet;
        float _fromYaw, _turnLeft;
        float _sitAt;
        bool _sounded;
        /// <summary>Tests: E pressed this frame; W/A/S/D on an office chair.</summary>
        [System.NonSerialized] public bool botSit;
        [System.NonSerialized] public Vector2? botScoot;

        const float SitTime = 0.6f, StandTime = 0.5f, YawRange = 110f, SwivelFree = 35f;

        void Awake()
        {
            _pc = GetComponent<CamcorderPlayerController>();
            _grab = GetComponent<PropGrabber>();
            _body = GetComponent<FirstPersonBody>();
        }

        public static bool Seat(MovableKind k) => k == MovableKind.OfficeChair || k == MovableKind.WoodenChair || k == MovableKind.LawnChair
                                                 || k == MovableKind.SchoolChair || k == MovableKind.Armchair || k == MovableKind.Sofa;

        /// <summary>It can be sat on now: a seat, on its feet, not held.</summary>
        public static bool CanSitOn(MovableProp p) => p && p.Shape != null && Seat(p.Spec.kind) && !p.held && p.transform.up.y > 0.9f;

        /// <summary>How many places to sit it has (a sofa: by its width).</summary>
        public static int Places(MovableProp p)
        {
            if (!p || p.Spec.kind != MovableKind.Sofa) return 1;
            var B = p.Shape.bounds;
            float arm = Mathf.Min(0.2f, B.size.x * 0.14f);
            return Mathf.Clamp(Mathf.RoundToInt((B.size.x - 2f * arm) / 0.62f), 1, 4);
        }

        /// <summary>Where the hips go on a place to sit (prop frame: its front +Z).</summary>
        public static Vector3 HipLocal(MovableProp p, int slot)
        {
            var B = p.Shape.bounds;
            float h = B.size.y, x = B.center.x, top, z;
            switch (p.Spec.kind)
            {
                case MovableKind.Armchair:
                case MovableKind.Sofa:
                {
                    // The cushions' top (a little over the seat block), the hips back from the front toward the back cushion.
                    top = B.min.y + h * 0.52f + 0.02f;
                    float backD = B.size.z * 0.27f;
                    z = B.min.z + backD + (B.size.z - backD) * 0.36f;
                    if (p.Spec.kind == MovableKind.Sofa)
                    {
                        float arm = Mathf.Min(0.2f, B.size.x * 0.14f), inner = B.size.x - 2f * arm;
                        int n = Places(p);
                        x = B.min.x + arm + inner * (Mathf.Clamp(slot, 0, n - 1) + 0.5f) / n;
                    }
                    break;
                }
                default:
                    top = B.min.y + h * 0.53f;
                    z = B.center.z + 0.02f;
                    break;
            }
            return new Vector3(x, top + 0.1f, z);
        }

        int NearestPlace(MovableProp p, Vector3 at)
        {
            int n = Places(p), best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                float d = (p.transform.TransformPoint(HipLocal(p, i)) - at).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        bool Office => Chair && Chair.Spec.kind == MovableKind.OfficeChair;

        void Update()
        {
            var kb = Keyboard.current;
            bool focus = _pc.HasFocus;
            bool e = botSit || (focus && kb != null && kb.eKey.wasPressedThisFrame);
            botSit = false;
            if (!Seated)
            {
                if (e && !_grab.HeldItem && _grab.Current == PropGrabber.State.Idle) TrySit();
                return;
            }
            if (!Chair || !Chair.gameObject.activeInHierarchy || !_pc.seated) { Release(); return; }
            float dt = Time.deltaTime;
            bool hold = _grab.HeldItem;
            bool up = (e && !hold) || (focus && kb != null && kb.spaceKey.wasPressedThisFrame)
                      || (!Office && focus && kb != null && kb.wKey.wasPressedThisFrame && Time.time > _sitAt + 0.35f);
            if (_standT < 0f && _t >= 1f && up) BeginStand();
            if (_standT >= 0f) { UpdateStand(dt); return; }
            if (Office) Scoot(dt, focus, kb);
            Place(dt);
        }

        void TrySit()
        {
            var cam = Camera.main ? Camera.main.transform : transform;
            if (!Physics.Raycast(cam.position, cam.forward, out var hit, 2.3f, ~0, QueryTriggerInteraction.Ignore)) return;
            var p = hit.collider.GetComponentInParent<MovableProp>();
            if (!CanSitOn(p)) return;
            Chair = p;
            _slot = NearestPlace(p, hit.point);
            p.Freeze();                                           // it stays put while you sit (saved where it is)
            _fromFeet = transform.position;
            _fromYaw = transform.eulerAngles.y;
            _turnLeft = Mathf.DeltaAngle(_fromYaw, FaceYaw());
            _t = 0f; _standT = -1f;
            _sitAt = Time.time;
            _pc.SetSeated(true);
            _sounded = false;
            Place(0f);
        }

        float FaceYaw() => Chair ? Chair.transform.eulerAngles.y : transform.eulerAngles.y;

        /// <summary>The eye (world) over the hips, a little forward, as a seated person's.</summary>
        void Pose(out Vector3 hip, out Vector3 eye, out Vector3 feetFloor, out Vector3 fwd, out Vector3 right, out float floor)
        {
            var t = Chair.transform;
            hip = t.TransformPoint(HipLocal(Chair, _slot));
            fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            right = Vector3.Cross(Vector3.up, fwd);
            floor = t.position.y;
            eye = hip + Vector3.up * 0.64f + fwd * 0.05f;
            feetFloor = new Vector3(eye.x, floor, eye.z);
        }

        void Place(float dt)
        {
            Pose(out var hip, out var eye, out var feet, out var fwd, out var right, out float floor);
            float s = Mathf.SmoothStep(0f, 1f, _t);
            if (_t < 1f)
            {
                _t = Mathf.Min(1f, _t + dt / SitTime);
                float s2 = Mathf.SmoothStep(0f, 1f, _t);
                // turn to face the way it faces as you go down
                float d = _turnLeft * (s2 - s);
                if (Mathf.Abs(d) > 1e-4f) _pc.AddLook(d, 0f);
                s = s2;
                // Down on it: a cushion's soft thump, or a chair's knock and creak.
                if (!_sounded && _t > 0.8f)
                {
                    _sounded = true;
                    BackpackSounds.Make();
                    bool soft = Chair.Spec.kind == MovableKind.Sofa || Chair.Spec.kind == MovableKind.Armchair;
                    Audio.WorldSound.PlayAt(soft ? BackpackSounds.Flop : BackpackSounds.Thud, hip, soft ? 0.55f : 0.4f);
                }
            }
            _pc.seatFeet = Vector3.Lerp(_fromFeet, feet, s);
            _pc.seatEye = Mathf.Lerp(_pc.BaseEyeStanding, eye.y - floor, s);
            _pc.seatYaw = FaceYaw();
            // (No limit while turning to face its front as you sit: clamped part way round, you ended up facing its side.)
            _pc.seatYawRange = Office || _t < 1f ? 360f : YawRange;
            if (_body) _body.SetSeat(true, hip, new Vector3(0f, floor, 0f) + Flat(hip + fwd * 0.42f - right * 0.11f), new Vector3(0f, floor, 0f) + Flat(hip + fwd * 0.42f + right * 0.11f), fwd);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>An office chair: it swivels with the view past a little, and W/A/S/D push it along on its casters.</summary>
        void Scoot(float dt, bool focus, Keyboard kb)
        {
            var t = Chair.transform;
            float delta = Mathf.DeltaAngle(t.eulerAngles.y, transform.eulerAngles.y);
            if (Mathf.Abs(delta) > SwivelFree)
            {
                float turn = (delta - Mathf.Sign(delta) * SwivelFree) * Mathf.Min(1f, dt * 7f);
                RotateChair(turn);
            }
            Vector2 input = botScoot ?? Vector2.zero;
            if (!botScoot.HasValue && focus && kb != null)
            {
                if (kb.wKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed) input.y -= 1f;
                if (kb.aKey.isPressed) input.x -= 1f;
                if (kb.dKey.isPressed) input.x += 1f;
            }
            if (input.sqrMagnitude < 0.01f) return;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, right = Vector3.Cross(Vector3.up, fwd);
            Vector3 move = (fwd * input.y + right * input.x).normalized * 0.4f * dt;
            // Not through anything: the chair's own shape swept the way it goes.
            if (Chair.Body.SweepTest(move.normalized, out var hit, move.magnitude + 0.02f, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(transform)) return;
            t.position += move;
            Chair.Body.position = t.position;
        }

        void RotateChair(float deg)
        {
            var t = Chair.transform;
            Vector3 pivot = t.TransformPoint(HipLocal(Chair, _slot));
            t.RotateAround(new Vector3(pivot.x, t.position.y, pivot.z), Vector3.up, deg);
            Chair.Body.position = t.position; Chair.Body.rotation = t.rotation;
        }

        void BeginStand()
        {
            Pose(out var hip, out var eye, out var feet, out var fwd, out var right, out float floor);
            _fromFeet = _pc.seatFeet;
            _standFeet = FindStandSpot(hip, fwd, right, floor);
            _standT = 0f;
        }

        /// <summary>Room to stand: in front of it, else beside it, else behind it, else where you were.</summary>
        Vector3 FindStandSpot(Vector3 hip, Vector3 fwd, Vector3 right, float floor)
        {
            var cc = GetComponent<CharacterController>();
            float r = cc ? cc.radius : 0.3f, h = cc ? Mathf.Max(cc.height, 1.7f) : 1.75f;
            Vector3 baseAt = new Vector3(hip.x, floor, hip.z);
            Vector3[] tries = { fwd * 0.62f, fwd * 0.8f, right * 0.65f, -right * 0.65f, fwd * 0.5f + right * 0.5f, fwd * 0.5f - right * 0.5f, -fwd * 0.75f };
            foreach (var d in tries)
            {
                Vector3 at = baseAt + d;
                Vector3 a = at + Vector3.up * (r + 0.05f), b = at + Vector3.up * (h - r);
                if (Physics.CheckCapsule(a, b, r * 0.95f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(at + Vector3.up * 0.4f, Vector3.down, 0.7f, ~0, QueryTriggerInteraction.Ignore)) continue;   // a floor there
                return at;
            }
            return _fromFeet;
        }

        void UpdateStand(float dt)
        {
            _standT = Mathf.Min(1f, _standT + dt / StandTime);
            float s = Mathf.SmoothStep(0f, 1f, _standT);
            Pose(out var hip, out var eye, out var feet, out var fwd, out var right, out float floor);
            _pc.seatFeet = Vector3.Lerp(_fromFeet, _standFeet, s);
            _pc.seatEye = Mathf.Lerp(eye.y - floor, _pc.BaseEyeStanding, s);
            if (_body) _body.SetSeat(s < 0.5f, hip, Flat(hip + fwd * 0.42f - right * 0.11f) + Vector3.up * floor, Flat(hip + fwd * 0.42f + right * 0.11f) + Vector3.up * floor, fwd);
            if (_standT >= 1f) Release();
        }

        void Release()
        {
            if (Chair) MovablePropManager.Instance?.Store(Chair);
            Chair = null;
            _standT = -1f; _t = 1f;
            _pc.SetSeated(false);
            if (_body) _body.SetSeat(false, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.forward);
        }

        /// <summary>Stands up at once (the player taken elsewhere: a teleport, a world ending).</summary>
        public void StandNow()
        {
            if (!Seated) return;
            Pose(out var hip, out var eye, out var feet, out var fwd, out var right, out float floor);
            var at = FindStandSpot(hip, fwd, right, floor);
            Release();
            _pc.Teleport(at, transform.eulerAngles.y, _pc.Pitch);
        }

        /// <summary>The hint while seated.</summary>
        public string Hint => !Seated ? null : _standT >= 0f ? null : Office ? "E STAND UP · W/A/S/D ROLL" : "E STAND UP";
    }
}
