using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The free hand opens doors (<see cref="Door"/>): hold the left mouse button on a door within reach and the hand goes
    /// to the lever on your side (stepping in if it is a little far) and pushes it down. Keep holding: the hand stays
    /// where it is in front of you and the door goes with it as you walk, so a step forward or back opens it a crack (to
    /// peek) and walking on swings it wide. Let go and it keeps swinging with what it had. The hand slips off if the door
    /// cannot follow (it is wide open, or you walked off sideways).
    /// </summary>
    public partial class PropGrabber
    {
        enum DoorPhase { None, Reach, Turn, Hold }
        DoorPhase _doorPhase;
        Door _door;
        int _doorSide;
        float _doorT, _doorTurn;
        Vector3 _doorOffset;

        /// <summary>The hand is on (or going to) a door's lever.</summary>
        public bool OnDoor => _doorPhase != DoorPhase.None;
        /// <summary>The lever a click would take (for the focus marks), or null.</summary>
        public Vector3? AimedDoorGrip { get; private set; }

        Door LookDoor(float range)
        {
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Door door = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                best = _hits[i].distance;
                door = _hits[i].collider.GetComponentInParent<Door>();
            }
            return door;
        }

        bool TryStartDoor()
        {
            var door = LookDoor(lookRange);
            if (!door) return false;
            _door = door;
            _doorSide = door.SideOf(Eye);
            _doorPhase = DoorPhase.Reach; _doorT = 0f; _doorTurn = 0f;
            ResetReach();
            _doorTurnQ = LeverTurn(door, _doorSide, 0f); _doorRollAt = Time.time;
            return true;
        }

        float _doorSlipT, _doorRollAt = -1f, _doorStallT;
        Quaternion _doorTurnQ = Quaternion.identity;
        bool _doorPushing;

        /// <summary>
        /// How far out (of the arm's reach) the hand goes while the body still has to step in: the elbow bent 25-35 degrees.
        /// (Sent straight to a lever 15-23 cm out of reach, the arm went out locked straight, the palm still 146-231 mm short
        /// at full blend, and slid on over 10-14 frames as the body stepped in.)
        /// </summary>
        const float ReachBent = 0.93f;
        // The bent reach now: a little straighter once the body is held off (pressed against the door, the lever 0.58 m from
        // the shoulder, 0.93 of the reach is 0.586), and how long it has been held off.
        float _bentNow = ReachBent, _heldOffT;
        /// <summary>A point within the bent arm's reach of the shoulder, on the way to p.</summary>
        Vector3 BentReach(Vector3 p) { Vector3 s = ReachShoulder; return s + Vector3.ClampMagnitude(p - s, ArmReach * _bentNow); }
        bool WithinBent(Vector3 p) => (p - ReachShoulder).magnitude <= ArmReach * _bentNow + 0.01f;

        /// <summary>
        /// Steps in toward p (out of the bent arm's reach). Held off (the body against the door), it leans in toward it and
        /// lets the arm straighten a little (to 0.98 of the reach) rather than keep walking into the door.
        /// </summary>
        void StepInToward(Vector3 p, float speed)
        {
            Vector3 local = transform.InverseTransformDirection(p - ReachShoulder); local.y = 0f;
            var flat = new Vector2(local.x, local.z);
            _player.botInput = flat.magnitude > 0.12f ? flat.normalized * speed : (Vector2?)null;
            _heldOffT = _player.PlanarVelocity.magnitude < 0.2f ? _heldOffT + Time.deltaTime : 0f;
            if (_heldOffT > 0.2f)
            {
                if (p.y < ReachShoulder.y - 0.2f) _stoopExtra = Mathf.MoveTowards(_stoopExtra, 0.35f, Time.deltaTime * 1.2f);
                _bentNow = Mathf.MoveTowards(_bentNow, 0.98f, Time.deltaTime * 0.15f);
            }
        }
        void ResetReach() { _bentNow = ReachBent; _heldOffT = 0f; }

        /// <summary>Where the player is trying to walk (world, level, 0..1): the keys, or a test bot's input.</summary>
        Vector3 MoveIntent()
        {
            Vector2 v = Vector2.zero;
            if (_player.botInput.HasValue) v = _player.botInput.Value;
            else if (_player.HasFocus && Keyboard.current != null)
            {
                var k = Keyboard.current;
                if (k.wKey.isPressed) v.y += 1f;
                if (k.sKey.isPressed) v.y -= 1f;
                if (k.dKey.isPressed) v.x += 1f;
                if (k.aKey.isPressed) v.x -= 1f;
            }
            v = Vector2.ClampMagnitude(v, 1f);
            return transform.rotation * new Vector3(v.x, 0f, v.y);
        }

        /// <summary>
        /// The hand on the lever turned as the arm holds it best (FirstPersonBody.ArmStrain): rolled about the bar (up to 28
        /// degrees) and the bar taken across the palm on a slant (turned about the back of the hand, up to 30), as a hand
        /// takes a lever from the side. (Square on it, the wrist was bent 28-29 degrees to the side through a whole pull.)
        /// </summary>
        Quaternion LeverTurn(Door d, int side, float turn)
        {
            if (!Armed) return Quaternion.identity;
            Quaternion r0 = d.GripRotation(side, turn);
            Vector3 bar = d.GripPoint(side, turn);
            Quaternion best = Quaternion.identity; float bestS = float.MaxValue;
            // (Round 9: a little wider, and the wrist bent to the side past 18 degrees costs more: pulling, it was 26-31, its
            // limit about 30, through the whole hold.)
            for (int y = -40; y <= 40; y += 10)
                for (int a = -42; a <= 42; a += 14)
                {
                    Quaternion t = Quaternion.AngleAxis(a, r0 * Vector3.right) * Quaternion.AngleAxis(y, r0 * Vector3.up);
                    Quaternion r = t * r0;
                    float sc = _body.ArmStrain(bar + r * Vector3.up * 0.012f, r, out float over, out float dev) + over * 0.02f + (Mathf.Abs(a) + Mathf.Abs(y)) * 0.002f
                               + Mathf.Max(0f, Mathf.Abs(dev) - 18f) * 0.03f;
                    if (sc < bestS) { bestS = sc; best = t; }
                }
            return best;
        }

        /// <summary>A door's lever let go: until then the fading hand keeps this place from the shoulder (UpdateHandPose).</summary>
        float _letGoUntil = -1f;
        Vector3 _letGoOff;
        // The hand turned about its palm's normal to keep the wrist's bend to the side within WristSideMax (WithWristSide).
        float _sideFix;
        const float WristSideMax = 20f;

        /// <summary>
        /// The hand on a lever turned a little about the forearm's back so the wrist bends no more than WristSideMax to the side
        /// (the bar a little more on the slant across the palm), from how the arm was solved last frame. Pulling, the wrist was
        /// bent 26-31 degrees to the side through the whole hold (its limit about 30).
        /// </summary>
        Quaternion WithWristSide(Quaternion r)
        {
            if (!Armed || HandBlend < 0.9f) return r;
            float d = _body.WristSide, excess = d - Mathf.Clamp(d, -WristSideMax, WristSideMax);
            if (excess != 0f) _sideFix = Mathf.Clamp(_sideFix + excess * 0.35f, -30f, 30f);
            else if (Mathf.Abs(d) < WristSideMax - 4f) _sideFix = Mathf.MoveTowards(_sideFix, 0f, Time.deltaTime * 8f);
            Vector3 axis = _body.WristSideAxis;
            return axis.sqrMagnitude > 0.5f ? Quaternion.AngleAxis(-_sideFix, axis) * r : r;
        }

        void EndDoor()
        {
            if (_door && HandBlend > 0.3f) { _letGoUntil = Time.time + 0.45f; _letGoOff = HandPosition - ReachShoulder; }
            _sideFix = 0f;
            _doorSlipT = 0f; _notThereT = 0f; _doorPushing = false; _doorStallT = 0f;
            if (_door) _door.Release();
            _door = null;
            _doorPhase = DoorPhase.None;
            _player.botInput = botHold.HasValue ? _player.botInput : null;
            _player.lookScale = 1f;
        }

        void UpdateDoor(bool hold)
        {
            if (!_door || !hold) { EndDoor(); return; }
            float dt = Time.deltaTime;
            _doorT += dt;
            Vector3 grip = _door.GripPoint(_doorSide, _doorTurn);
            Vector3 toGrip = grip - ReachShoulder;
            switch (_doorPhase)
            {
                case DoorPhase.Reach:
                {
                    // The lever is taken (and turned) only once the hand is on it: it used to turn by itself with the palm
                    // 12 cm short. (The solved palm decides; the reach says when to step in.) The hand goes out with the
                    // elbow bent while the body steps in (BentReach), and onto the lever once it is that near.
                    float d = toGrip.magnitude, bent = ArmReach * _bentNow;
                    bool there = HandBlend > 0.85f && HandArrived && WithinBent(grip);
                    if (there || (_doorT > 1.2f && d <= ArmReach + 0.1f))
                    {
                        _player.botInput = null;
                        _doorPhase = DoorPhase.Turn; _doorT = 0f; _door.Grab(_doorSide); MicThump();
                        _doorTurnQ = LeverTurn(_door, _doorSide, 0f); _doorRollAt = Time.time;
                        break;
                    }
                    // Never there (held off by something): the door still opens, shoved by the hand the way you face, rather
                    // than leave you stuck at it. (It gave up after 2 s and nothing happened.)
                    if (_doorT > 1.6f || d > lookRange + 0.6f)
                    {
                        _player.botInput = null;
                        if (d <= lookRange + 0.6f) _door.Shove(_doorSide, grip, transform.forward, _doorSide > 0);
                        EndDoor();
                        break;
                    }
                    if (d <= bent)
                    {
                        if (!ComeCloser(grip, 2.5f)) EndDoor();
                        else if (_notThereT <= 0.2f) _player.botInput = null;
                        break;
                    }
                    StepInToward(grip, 0.6f);
                    break;
                }
                case DoorPhase.Turn:
                    // The lever goes down under the hand.
                    _doorTurn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_doorT / 0.22f));
                    _door.Turn(_doorTurn);
                    if (_doorT >= 0.22f)
                    {
                        _doorPhase = DoorPhase.Hold;
                        _doorOffset = Quaternion.Inverse(transform.rotation) * (_door.GripPoint(_doorSide, 1f) - StandShoulder);
                    }
                    break;
                case DoorPhase.Hold:
                {
                    // The hand stays in front of the body where it took the lever, within the arm's reach of the shoulder
                    // as it is now; the door follows it.
                    Vector3 target = StandShoulder + transform.rotation * _doorOffset, RS = ReachShoulder;
                    Vector3 lever = _door.GripPoint(_doorSide, 1f);
                    // (Within 0.9 of the reach, the elbow a little bent: at full stretch the palm fell 4 cm behind the
                    // lever pulling and the hand let go and took it again.)
                    target = RS + Vector3.ClampMagnitude(target - RS, ArmReach * 0.9f);
                    // Walking the way the door opens (into a door pushed, back from one pulled): the hand pushes it on ahead
                    // of the body and holds it where it got to when you stop. (The hand was kept where it took the lever from
                    // the body, and the body stopped at the shut door: a door pushed while walking stayed at 0 degrees.)
                    Vector3 opening = _door.OpeningAt(lever);
                    float into = Vector3.Dot(MoveIntent(), opening);
                    // (Pushing only: pulled toward you, the lever came in to the body, the elbow bent 97 degrees; walking back
                    // with the hand where it took the lever opens it as you go.)
                    if (into > 0.25f && _doorSide < 0)
                    {
                        target = lever + opening * (0.09f * into);
                        _doorOffset = Quaternion.Inverse(transform.rotation) * (lever - StandShoulder);
                        _doorPushing = true;
                    }
                    // Round 10: pulling with no room behind (a wall), walking back gets nowhere: the arm stood out straight, the
                    // door stuck at 26 degrees and swung back to 14 when let go. The hand tugs it round as a quick pull does
                    // (TugFromHold): the body steps to the latch side, out of the leaf's way, and the door rests on the body.
                    if (_doorSide > 0 && !_doorPushing)
                    {
                        bool stuck = into > 0.25f && _door.Angle < 55f && _doorT > 0.3f
                                     && (_player.PlanarVelocity.magnitude < 0.12f || (lever - RS).magnitude > ArmReach * 0.95f);
                        _doorStallT = stuck ? _doorStallT + dt : 0f;
                        if (_doorStallT > 0.3f) { TugFromHold(); break; }
                    }
                    _door.Drive(target, dt);
                    _player.lookScale = 0.75f;
                    if (Time.time - _doorRollAt > 0.25f) { _doorTurnQ = Quaternion.RotateTowards(_doorTurnQ, LeverTurn(_door, _doorSide, 1f), 10f); _doorRollAt = Time.time; }
                    // Pushed on past the arm's reach: the hand lets go and the door swings on with the speed it had (and
                    // the body walking on pushes it further).
                    if (_doorPushing && ((lever - RS).magnitude > ArmReach || PalmError > 0.02f)) { EndDoor(); break; }
                    // It could not follow (the hand would have to let go).
                    // The hand lets go when the lever leaves it (it used to stay "holding" with the palm up to 23 cm off).
                    // (Round 9: sooner, 3 cm off for two frames: at 5 cm for 0.12 s the arm stood out straight, the palm up to
                    // 14 cm off the lever at full blend.)
                    _doorSlipT = Vector3.Distance(lever, RS) > ArmReach + 0.2f || PalmError > 0.03f ? _doorSlipT + dt : 0f;
                    if (_doorSlipT > 0.03f) EndDoor();
                    break;
                }
            }
        }

        /// <summary>
        /// A pulled door held with no room to walk back (round 10): the hand keeps the lever and the quick pull's tug takes over
        /// (UpdateQuickDoor): the body steps back and to the latch side as the door comes round, the door rests on the body,
        /// and the hand fades out when it can no longer follow.
        /// </summary>
        void TugFromHold()
        {
            var d = _door; int side = _doorSide;
            Quaternion turnQ = _doorTurnQ;
            _sideFix = 0f; _doorSlipT = 0f; _notThereT = 0f; _doorPushing = false; _doorStallT = 0f;
            d.Release();
            _door = null;
            _doorPhase = DoorPhase.None;
            _player.lookScale = 1f;
            _quickDoor = d; _quickSide = side; _quickPull = true; _quickLever = true;
            _quickPoint = d.GripPoint(side, 1f); _quickNormal = d.FaceNormal(side);
            _quickTurnQ = turnQ;
            _quick = QuickPhase.Push; _quickT = 0f;
            ResetReach();
        }

        /// <summary>The hand on the lever (for the arm IK).</summary>
        void DoorHandPose()
        {
            Quaternion r0 = _door.GripRotation(_doorSide, _doorTurn);
            // Rolled about the bar as the arm holds it best (LeverRoll), the wrist not bent far to the side.
            HandRotation = _doorPhase == DoorPhase.Reach ? _doorTurnQ * r0 : WithWristSide(_doorTurnQ * r0);
            // The palm on top of the lever's bar (its radius and a little), not at its middle.
            Vector3 p = _door.GripPoint(_doorSide, _doorTurn) + HandRotation * Vector3.up * 0.012f;
            // Reaching, no further than the bent arm goes (the body steps in for the rest).
            HandPosition = _doorPhase == DoorPhase.Reach ? BentReach(p) : p;
            float curl = _doorPhase == DoorPhase.Reach ? 0.35f : 0.8f;
            HandCurl = Mathf.MoveTowards(HandCurl, curl, Time.deltaTime * 4f);
        }
    }
}
