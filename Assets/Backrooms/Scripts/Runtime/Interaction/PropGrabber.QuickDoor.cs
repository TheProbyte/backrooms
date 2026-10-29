using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Right click on a door within reach: the quick way through (the slow way is holding the lever and walking).
    ///  - Shut, from the side it opens away from: the hand presses the lever (if it is latched) and shoves; the door swings
    ///    wide. Unlatched, the palm shoves the door itself.
    ///  - Shut, from the side it opens toward you: the hand grabs the lever and tugs; the door swings part way open (it
    ///    stops at you if you stand in its way).
    ///  - Already open: the palm shoves it the way you are facing, wider or toward shut (shut hard, it bangs and latches).
    /// </summary>
    public partial class PropGrabber
    {
        enum QuickPhase { None, Reach, Push, Back }
        QuickPhase _quick;
        Quaternion _quickTurnQ = Quaternion.identity;
        Door _quickDoor;
        bool _quickPull, _quickLever;
        int _quickSide;
        float _quickT;
        Vector3 _quickPoint, _quickNormal, _quickBackOff;

        bool QuickDooring => _quick != QuickPhase.None;

        Door LookDoor(float range, out Vector3 point)
        {
            point = default;
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Door door = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                best = _hits[i].distance;
                door = _hits[i].collider.GetComponentInParent<Door>();
                point = _hits[i].point;
            }
            return door;
        }

        /// <summary>
        /// Round 11 (handling plan, step 2): a left click on a door is the quick way (it was right click only). Held past
        /// this (s) before the hand has it, it becomes the careful way instead: the hand keeps the lever and the door goes
        /// with you as you walk (PropGrabber.Door), to peek through a crack.
        /// </summary>
        public const float HoldToDrag = 0.22f;
        /// <summary>A door open at least this far (degrees) is swung shut by a click; less (ajar), it is opened on.</summary>
        public const float ClickShutsFrom = 30f;
        bool _quickClick, _quickClose;

        bool TryQuickDoor() => TryQuickDoor(false);

        bool TryQuickDoor(bool click)
        {
            var door = LookDoor(lookRange, out var hit);
            if (!door || door.Held) return false;
            int side = door.SideOf(Eye);
            bool shut = door.Angle < 3f;
            _quickDoor = door;
            _quickSide = side;
            _quickClick = click;
            // A click on a door well open swings it shut (the palm on its face); ajar, it opens on, toward you by the lever.
            _quickClose = click && door.Angle >= ClickShutsFrom;
            _quickPull = !_quickClose && side > 0 && (shut || click);   // it opens toward you: a tug on the lever
            _quickLever = _quickPull || shut && door.Latched;   // a latched door: the lever first, pushed or pulled
            _quickPoint = _quickLever ? door.GripPoint(side, 0f) : hit;
            _quickNormal = door.FaceNormal(side);
            // Too far for an arm and a step in: no. (Within a step the hand goes when the step has brought it there: from
            // 0.8 m off a door's face, the usual distance, a push was refused outright.)
            if ((_quickPoint - ReachShoulder).magnitude > ArmReach + 0.6f) { _quickDoor = null; return false; }
            _quick = QuickPhase.Reach; _quickT = 0f; _sideFix = 0f;
            ResetReach();
            _quickTurnQ = _quickLever ? LeverTurn(door, side, 0f) : Quaternion.identity;
            return true;
        }

        void UpdateQuickDoor(bool hold = false)
        {
            if (!_quickDoor) { _quick = QuickPhase.None; return; }
            _quickT += Time.deltaTime;
            switch (_quick)
            {
                case QuickPhase.Reach:
                    if (_quickLever) _quickPoint = _quickDoor.GripPoint(_quickSide, 0f);
                    // A click still held: the careful way (the hand keeps the lever and the door goes with you).
                    if (_quickClick && hold && _quickT >= HoldToDrag) { QuickToHold(); break; }
                    // The door goes when the hand is on it (it used to go after 0.3 s wherever the hand was); out of reach,
                    // a step in first; never there: nothing.
                    // (The hand goes out with the elbow bent while the body steps in, and onto the door once it is that near:
                    // BentReach. If it never gets there, the door goes anyway after a moment: it used to do nothing.)
                    // (Round 11: or once it has had a third of a second and the palm is within 4 cm: it waited for 15 mm.)
                    bool on = HandBlend > 0.8f && PalmError < 0.015f && WithinBent(_quickPoint);
                    bool nearOn = _quickT > 0.33f && HandBlend > 0.8f && PalmError < 0.04f;
                    if ((!(on || nearOn) && _quickT < 0.9f) || (_quickClick && hold))
                    {
                        if (!WithinBent(_quickPoint)) StepInToward(_quickPoint, 0.6f);
                        else _player.botInput = null;
                        break;
                    }
                    _player.botInput = null;
                    {
                        // Contact: the door goes.
                        if (_quickClose) _quickDoor.ShoveShut();
                        else _quickDoor.Shove(_quickSide, _quickPoint, transform.forward, _quickPull);
                        MicThump();
                        if (!_quickLever)
                        {
                            if (!_touchFx)
                            {
                                var go = new GameObject("HandTouch");
                                go.transform.SetParent(transform, false);
                                _touchFx = go.AddComponent<AudioSource>();
                                _touchFx.playOnAwake = false; _touchFx.spatialBlend = 1f; _touchFx.minDistance = 0.3f; _touchFx.maxDistance = 8f;
                            }
                            _touchFx.transform.position = _quickPoint;
                            _touchFx.PlayOneShot(SoundBank.WallPat(Random.Range(0, 3)), 0.6f * SoundCatalog.Volume(SoundIds.Touch));
                        }
                        _quick = QuickPhase.Push; _quickT = 0f;
                    }
                    break;
                case QuickPhase.Push:
                    if (_quickLever && _quickPull)
                    {
                        // A tug (round 9): the hand keeps the lever and pulls the door on round while the body steps back and to
                        // its latch side, the elbow bent, until the lever leaves the bent arm, the door is well open, or 0.7 s.
                        // Then the door swings on by itself. (It let go after 0.22 s with the arm out straight, the door
                        // resting on the body at 24 degrees: not enough to walk through.)
                        _quickPoint = _quickDoor.GripPoint(_quickSide, 1f);
                        _quickDoor.Tug(Mathf.Lerp(80f, 110f, Mathf.Clamp01(_quickT / 0.3f)));
                        // (To the latch side first, the lever coming round with the hand; stepping straight back took the
                        // shoulder away from the lever and the hand let go at once.)
                        _player.botInput = _quickDoor.Angle < 20f ? SideStep() : PullStep();
                        // (The hand goes no further than 0.97 of the reach: QuickDoorPose. More than 2 cm past that, the palm
                        // would be off the lever: it lets go.)
                        // (Not on the first frames: the lever pressed down turns the hand at once, and the arm takes a frame or
                        // two to follow; checking the palm then, it let go at once and the door stopped at 25 degrees.)
                        bool leaves = (_quickPoint - ReachShoulder).magnitude > ArmReach * 0.97f + 0.02f || (_quickT > 0.15f && HandBlend > 0.97f && PalmError > 0.03f);
                        if (leaves || _quickT > 0.9f || _quickDoor.Angle > 58f)
                        {
                            _quick = QuickPhase.Back; _quickT = 0f;
                            _quickBackOff = HandPosition - ReachShoulder;
                            _letGoUntil = Time.time + 0.45f; _letGoOff = _quickBackOff;
                        }
                        break;
                    }
                    // The hand goes with the door a moment (on the pressed lever when it took the lever), then lets go.
                    // (A tug keeps the lever; a shove lets the door swing off the hand, which follows through a little: the
                    // hand chasing a lever swinging away at 200 degrees a second was left up to 0.4 m behind it.)
                    if (_quickLever && (_quickPull || _quickT < 0.12f)) _quickPoint = _quickDoor.GripPoint(_quickSide, 1f);
                    else _quickPoint += transform.forward * Time.deltaTime * 0.6f;
                    // With the door to the end of the arm's reach, no further (it was left up to 0.3 m behind the door).
                    _quickPoint = ReachShoulder + Vector3.ClampMagnitude(_quickPoint - ReachShoulder, ArmReach);
                    // A tug: a step back and out of the door's way, toward its latch side, as it comes (standing at the
                    // lever, the door swung 6 degrees into you, bounced and latched again).
                    if (_quickPull) _player.botInput = PullStep();
                    if (_quickT > (_quickLever ? 0.22f : 0.12f)) { _quick = QuickPhase.Back; _quickT = 0f; _quickBackOff = HandPosition - ReachShoulder; }
                    break;
                case QuickPhase.Back:
                    // (The tug's step goes on after the hand lets go until the door is open enough to walk through, or 1 s;
                    // the door, resting on the body, follows it out. The step stopped after 0.45 s and the door with it, at 25
                    // degrees.)
                    if (_quickPull && _quickT < 1f && _quickDoor.Angle < 50f) { _player.botInput = PullStep(); _quickDoor.Tug(30f); break; }
                    if (_quickPull) _player.botInput = null;
                    _quick = QuickPhase.None; _quickDoor = null;
                    break;
            }
        }

        /// <summary>A click held on: the quick way becomes the careful one on the same door (the hand goes on to its lever).</summary>
        void QuickToHold()
        {
            var door = _quickDoor;
            _quick = QuickPhase.None; _quickDoor = null; _quickClick = false;
            _player.botInput = null;
            _door = door;
            _doorSide = door.SideOf(Eye);
            _doorPhase = DoorPhase.Reach; _doorT = 0f; _doorTurn = 0f;
            ResetReach();
            _doorTurnQ = LeverTurn(door, _doorSide, 0f); _doorRollAt = Time.time;
        }

        /// <summary>Back from a tugged door and toward its latch side (the bot input for the step).</summary>
        Vector2 PullStep()
        {
            // (Mostly to the latch side: out of the leaf's sweep the soonest, and still a way out with a wall behind.)
            // (Round 11: straight back if a wall is right there on the latch side: stepping into it, the body stayed put and
            // the door, resting on it, crept from 19 to 39 degrees over a second.)
            Vector3 latch = LatchSideOpen() ? _quickDoor.TowardLatch : Vector3.zero;
            Vector3 w = _quickNormal + latch * 1.2f; w.y = 0f;
            Vector3 local = transform.InverseTransformDirection(w);
            return new Vector2(local.x, local.z).normalized;
        }

        /// <summary>Toward a tugged door's latch side only, out of the leaf's sweep (the bot input for the step); straight
        /// back if that side is walled.</summary>
        Vector2 SideStep()
        {
            Vector3 w = LatchSideOpen() ? _quickDoor.TowardLatch : _quickNormal; w.y = 0f;
            Vector3 local = transform.InverseTransformDirection(w);
            return new Vector2(local.x, local.z).normalized * 0.75f;
        }

        float _latchCheckAt = -1f; bool _latchOpen = true;
        /// <summary>Room for the body to step 0.4 m toward the tugged door's latch side (checked a few times a second).</summary>
        bool LatchSideOpen()
        {
            if (Time.time < _latchCheckAt) return _latchOpen;
            _latchCheckAt = Time.time + 0.2f;
            var cc = GetComponent<CharacterController>();
            if (!cc || !_quickDoor) return _latchOpen = true;
            Vector3 dir = _quickDoor.TowardLatch; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) return _latchOpen = true;
            Vector3 c = transform.position + cc.center;
            float half = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
            Vector3 a = c + Vector3.up * half, b = c - Vector3.up * (half - 0.2f);
            int n = Physics.CapsuleCastNonAlloc(a, b, cc.radius * 0.9f, dir.normalized, s_pickHits, 0.4f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = s_pickHits[i].collider;
                if (!col || col.transform.IsChildOf(transform) || col.GetComponentInParent<Door>() == _quickDoor) continue;
                return _latchOpen = false;
            }
            return _latchOpen = true;
        }

        /// <summary>The hand on the door: flat on its face for a shove, round the lever for a tug.</summary>
        void QuickDoorPose()
        {
            // Let go: the hand stays where it was from the shoulder as it fades back (it was left at the lever while the
            // body stepped away, the arm straight and the palm up to 326 mm off it at full blend).
            if (_quick == QuickPhase.Back) { HandPosition = ReachShoulder + _quickBackOff; HandCurl = Mathf.MoveTowards(HandCurl, 0.3f, Time.deltaTime * 4f); return; }
            HandPosition = _quick == QuickPhase.Reach ? BentReach(_quickPoint) : _quickPoint;
            if (_quickLever)
            {
                float turn = _quick == QuickPhase.Reach ? 0f : 1f;
                // Turned on the lever as the arm holds it best (LeverTurn, picked when the click came).
                HandRotation = _quickTurnQ * _quickDoor.GripRotation(_quickSide, turn);
                if (_quick != QuickPhase.Reach) HandRotation = WithWristSide(HandRotation);
                Vector3 p = _quickPoint + HandRotation * Vector3.up * 0.012f;       // the palm on top of the bar
                // (Tugging, no further than a bent arm: past it the hand lets go, UpdateQuickDoor.)
                HandPosition = _quick == QuickPhase.Reach ? BentReach(p) : _quickPull ? ReachShoulder + Vector3.ClampMagnitude(p - ReachShoulder, ArmReach * 0.97f) : p;
                HandCurl = Mathf.MoveTowards(HandCurl, 0.8f, Time.deltaTime * 8f);
            }
            else
            {
                Vector3 n = _quickNormal;                                   // out of the face toward you: the back of the hand
                Vector3 fingers = Vector3.ProjectOnPlane(Vector3.up + transform.right * Side * 0.3f, n).normalized;
                HandRotation = Quaternion.LookRotation(fingers, n);
                HandCurl = Mathf.MoveTowards(HandCurl, 0.1f, Time.deltaTime * 8f);
            }
        }
    }
}
