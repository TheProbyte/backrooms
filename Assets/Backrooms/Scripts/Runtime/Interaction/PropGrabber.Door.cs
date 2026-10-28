using UnityEngine;

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
            return true;
        }

        float _doorSlipT;

        void EndDoor()
        {
            _doorSlipT = 0f; _notThereT = 0f;
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
                    // 12 cm short. (The solved palm decides; the reach says when to step in.)
                    if (HandBlend > 0.85f && HandArrived && toGrip.magnitude <= ArmReach + 0.15f)
                    {
                        _player.botInput = null;
                        _doorPhase = DoorPhase.Turn; _doorT = 0f; _door.Grab(_doorSide); MicThump();
                        break;
                    }
                    if (toGrip.magnitude <= ArmReach)
                    {
                        if (!ComeCloser(grip, 2.5f)) EndDoor();
                        else if (_notThereT <= 0.2f) _player.botInput = null;
                        break;
                    }
                    if (_doorT > 2f || toGrip.magnitude > lookRange + 0.6f) { EndDoor(); break; }
                    Vector3 local = transform.InverseTransformDirection(toGrip); local.y = 0f;
                    var flat = new Vector2(local.x, local.z);
                    _player.botInput = flat.magnitude > 0.2f ? flat.normalized * 0.6f : (Vector2?)null;
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
                    // (Within 0.9 of the reach, the elbow a little bent: at full stretch the palm fell 4 cm behind the
                    // lever pulling and the hand let go and took it again.)
                    target = RS + Vector3.ClampMagnitude(target - RS, ArmReach * 0.9f);
                    _door.Drive(target, dt);
                    _player.lookScale = 0.75f;
                    // It could not follow (the hand would have to let go).
                    // The hand lets go when the lever leaves it (it used to stay "holding" with the palm up to 23 cm off).
                    _doorSlipT = Vector3.Distance(_door.GripPoint(_doorSide, 1f), RS) > ArmReach + 0.2f || PalmError > 0.05f ? _doorSlipT + dt : 0f;
                    if (_doorSlipT > 0.12f) EndDoor();
                    break;
                }
            }
        }

        /// <summary>The hand on the lever (for the arm IK).</summary>
        void DoorHandPose()
        {
            HandRotation = _door.GripRotation(_doorSide, _doorTurn);
            // The palm on top of the lever's bar (its radius and a little), not at its middle.
            HandPosition = _door.GripPoint(_doorSide, _doorTurn) + HandRotation * Vector3.up * 0.012f;
            float curl = _doorPhase == DoorPhase.Reach ? 0.35f : 0.8f;
            HandCurl = Mathf.MoveTowards(HandCurl, curl, Time.deltaTime * 4f);
        }
    }
}
