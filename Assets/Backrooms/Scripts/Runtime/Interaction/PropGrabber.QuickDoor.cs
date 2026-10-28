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
        Vector3 _quickPoint, _quickNormal;

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

        bool TryQuickDoor()
        {
            var door = LookDoor(lookRange, out var hit);
            if (!door || door.Held) return false;
            int side = door.SideOf(Eye);
            bool shut = door.Angle < 3f;
            _quickDoor = door;
            _quickSide = side;
            _quickPull = shut && side > 0;                 // it opens toward you: a tug on the lever
            _quickLever = _quickPull || shut && door.Latched;   // a latched door: the lever first, pushed or pulled
            _quickPoint = _quickLever ? door.GripPoint(side, 0f) : hit;
            _quickNormal = door.FaceNormal(side);
            // Too far for an arm and a step in: no. (Within a step the hand goes when the step has brought it there: from
            // 0.8 m off a door's face, the usual distance, a push was refused outright.)
            if ((_quickPoint - ReachShoulder).magnitude > ArmReach + 0.6f) { _quickDoor = null; return false; }
            _quick = QuickPhase.Reach; _quickT = 0f;
            ResetReach();
            _quickTurnQ = _quickLever ? LeverTurn(door, side, 0f) : Quaternion.identity;
            return true;
        }

        void UpdateQuickDoor()
        {
            if (!_quickDoor) { _quick = QuickPhase.None; return; }
            _quickT += Time.deltaTime;
            switch (_quick)
            {
                case QuickPhase.Reach:
                    if (_quickLever) _quickPoint = _quickDoor.GripPoint(_quickSide, 0f);
                    // The door goes when the hand is on it (it used to go after 0.3 s wherever the hand was); out of reach,
                    // a step in first; never there: nothing.
                    // (The hand goes out with the elbow bent while the body steps in, and onto the door once it is that near:
                    // BentReach. If it never gets there, the door goes anyway after a moment: it used to do nothing.)
                    if (!(HandBlend > 0.8f && PalmError < 0.015f && WithinBent(_quickPoint)) && _quickT < 1.1f)
                    {
                        if (!WithinBent(_quickPoint)) StepInToward(_quickPoint, 0.5f);
                        else _player.botInput = null;
                        break;
                    }
                    _player.botInput = null;
                    {
                        // Contact: the door goes.
                        _quickDoor.Shove(_quickSide, _quickPoint, transform.forward, _quickPull);
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
                            _touchFx.PlayOneShot(SoundBank.WallPat(Random.Range(0, 3)), 0.6f);
                        }
                        _quick = QuickPhase.Push; _quickT = 0f;
                    }
                    break;
                case QuickPhase.Push:
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
                    if (_quickT > (_quickLever ? 0.22f : 0.12f)) { _quick = QuickPhase.Back; _quickT = 0f; }
                    break;
                case QuickPhase.Back:
                    // (The tug's step goes on a moment after the hand lets go.)
                    if (_quickPull && _quickT < 0.45f) { _player.botInput = PullStep(); break; }
                    if (_quickPull) _player.botInput = null;
                    _quick = QuickPhase.None; _quickDoor = null;
                    break;
            }
        }

        /// <summary>Back from a tugged door and toward its latch side (the bot input for the step).</summary>
        Vector2 PullStep()
        {
            Vector3 w = _quickNormal + _quickDoor.TowardLatch * 0.8f; w.y = 0f;
            Vector3 local = transform.InverseTransformDirection(w);
            return new Vector2(local.x, local.z).normalized * 0.8f;
        }

        /// <summary>The hand on the door: flat on its face for a shove, round the lever for a tug.</summary>
        void QuickDoorPose()
        {
            HandPosition = _quick == QuickPhase.Reach ? BentReach(_quickPoint) : _quickPoint;
            if (_quickLever)
            {
                float turn = _quick == QuickPhase.Reach ? 0f : 1f;
                // Turned on the lever as the arm holds it best (LeverTurn, picked when the click came).
                HandRotation = _quickTurnQ * _quickDoor.GripRotation(_quickSide, turn);
                Vector3 p = _quickPoint + HandRotation * Vector3.up * 0.012f;       // the palm on top of the bar
                HandPosition = _quick == QuickPhase.Reach ? BentReach(p) : p;
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
