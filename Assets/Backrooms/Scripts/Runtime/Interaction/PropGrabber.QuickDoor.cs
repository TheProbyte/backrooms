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
            // Too far for an arm (even leaning in): no.
            if ((_quickPoint - Shoulder).magnitude > reach + 0.4f) { _quickDoor = null; return false; }
            _quick = QuickPhase.Reach; _quickT = 0f;
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
                    if (HandBlend > 0.8f || _quickT > 0.3f)
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
                    if (_quickLever) _quickPoint = _quickDoor.GripPoint(_quickSide, 1f);
                    else _quickPoint += transform.forward * Time.deltaTime * 0.6f;
                    if (_quickT > (_quickLever ? 0.22f : 0.12f)) { _quick = QuickPhase.Back; _quickT = 0f; }
                    break;
                case QuickPhase.Back:
                    _quick = QuickPhase.None; _quickDoor = null;
                    break;
            }
        }

        /// <summary>The hand on the door: flat on its face for a shove, round the lever for a tug.</summary>
        void QuickDoorPose()
        {
            HandPosition = _quickPoint;
            if (_quickLever)
            {
                float turn = _quick == QuickPhase.Reach ? 0f : 1f;
                HandRotation = _quickDoor.GripRotation(_quickSide, turn);
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
