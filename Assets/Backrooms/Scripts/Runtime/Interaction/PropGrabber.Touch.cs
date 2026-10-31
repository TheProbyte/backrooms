using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The free hand steadies you on the world now and then: walking past a door jamb or round the end of a wall on the
    /// hand's side, it reaches out and rests on the wall by the edge for a moment (palm flat, fingers on the surface),
    /// with a soft pat, then lets go as you pass. Not every time, and never while it is doing anything else.
    /// </summary>
    public partial class PropGrabber
    {
        [Header("Steadying hand")]
        [Tooltip("The free hand touches door jambs and wall ends as you pass them.")] public bool steadyHand = true;
        [Tooltip("Chance it does, each time an edge is in reach.")] [Range(0f, 1f)] public float steadyChance = 0.55f;

        bool _touching;
        float _touchT, _touchHold, _nextTouchCheck, _touchCooldown;
        Vector3 _touchPoint, _touchNormal, _touchFingers;
        Collider[] _touchColliders;
        AudioSource _touchFx;

        /// <summary>Times the hand has steadied on a wall (for tests).</summary>
        public int Touches { get; private set; }
        /// <summary>The free hand is resting on a wall.</summary>
        public bool TouchingWall => _touching;

        void UpdateTouch(bool free)
        {
            float dt = Time.deltaTime;
            if (_touching)
            {
                _touchT += dt;
                Vector3 toP = _touchPoint - ReachShoulder;
                bool behind = Vector3.Dot(toP, transform.forward) < -0.08f;
                if (!free || _touchT > _touchHold || behind || toP.magnitude > ArmReach + 0.05f || _player.PlanarVelocity.magnitude < 0.15f)
                {
                    _touching = false;
                    _touchCooldown = Time.time + 1.2f;
                }
                return;
            }
            if (!free || !steadyHand || HandBlend > 0.05f || Time.time < _touchCooldown || Time.time < _nextTouchCheck) return;
            _nextTouchCheck = Time.time + 0.1f;
            float speed = _player.PlanarVelocity.magnitude;
            if (speed < 0.6f || _player.IsCrawling || !_player.IsGrounded || _player.noclip) return;
            if (!FindEdge(out var p, out var n, out var col)) return;
            _touchCooldown = Time.time + 2.5f;                                    // one try per edge
            if (Random.value > steadyChance) return;
            _touching = true;
            Touches++;
            _touchT = 0f;
            _touchHold = Random.Range(0.45f, 0.8f);
            _touchPoint = p; _touchNormal = n;
            _touchFingers = Vector3.ProjectOnPlane(Vector3.up + transform.forward * 0.45f, n).normalized;
            var moved = ReachableTouch();
            // (The fingers close on the face the hand ended on: moved along it, it may be another collider's.)
            _touchColliders = moved && moved != col ? new[] { col, moved } : new[] { col };
            if (!_touchFx)
            {
                var go = new GameObject("HandTouch");
                go.transform.SetParent(transform, false);
                _touchFx = ItemModels.Source(go, 6f);
                _touchFx.minDistance = 0.3f;
            }
            _touchFx.transform.position = p;
            _touchFx.PlayOneShot(SoundBank.WallPat(Random.Range(0, 3)), 0.35f * SoundCatalog.Volume(SoundIds.Touch));
        }

        /// <summary>
        /// A wall end or door jamb ahead on the hand's side: a fan of rays from shoulder height, from the side round to the
        /// front; an edge is where one ray stops close and the next (further round to the front) goes on much further.
        /// The touch goes a few centimetres back from the edge, on the face looking at you.
        /// </summary>
        bool FindEdge(out Vector3 point, out Vector3 normal, out Collider col)
        {
            point = normal = Vector3.zero; col = null;
            Vector3 origin = transform.position + Vector3.up * 1.15f;
            Vector3 fwd = transform.forward;
            float prevDist = -1f; RaycastHit prev = default; Vector3 prevDir = fwd;
            for (int k = 0; k <= 7; k++)
            {
                float ang = Mathf.Lerp(85f, 8f, k / 7f) * Side;
                Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * fwd;
                bool hit = Physics.Raycast(origin, dir, out var h, 0.85f, ~0, QueryTriggerInteraction.Ignore)
                           && !h.collider.transform.IsChildOf(transform) && Mathf.Abs(h.normal.y) < 0.3f
                           && !h.collider.GetComponentInParent<WorldItem>() && !h.collider.GetComponentInParent<MovableProp>()
                           && !FirstPersonBody.Swings(h.collider);
                float d = hit ? h.distance : 2f;
                if (prevDist > 0f && prevDist < 0.7f && d - prevDist > 0.35f && Vector3.Dot(prev.normal, -prevDir) > 0.35f)
                {
                    Vector3 p = prev.point;
                    // Back from the edge along the wall, toward where the earlier rays hit.
                    Vector3 along = Vector3.ProjectOnPlane(dir - prevDir, prev.normal).normalized;
                    p -= along * 0.05f;
                    p.y = transform.position.y + Random.Range(1.0f, 1.22f);
                    if (Vector3.Dot(p - origin, fwd) < 0.2f) return false;
                    if ((p - ReachShoulder).magnitude > ArmReach) return false;
                    point = p + prev.normal * 0.004f; normal = prev.normal; col = prev.collider;
                    return true;
                }
                prevDist = hit ? d : -1f; prev = h; prevDir = dir;
            }
            return false;
        }

        /// <summary>
        /// The touch made one an arm can do: the point moved along the face away from the edge (outward, to the hand's side)
        /// until the forearm meets the face at no more than about 50 degrees from its normal, where the face goes that far,
        /// and the fingers carrying on the forearm's line across the face, tipped up. (A palm flat on a face straight ahead,
        /// fingers up, needs the wrist bent back 90 degrees: the hand ended 104 degrees off and the fingers in the jamb.)
        /// </summary>
        Collider ReachableTouch()
        {
            Collider moved = null;
            Vector3 S = ReachShoulder, n = _touchNormal;
            float dFace = Vector3.Dot(S - _touchPoint, n);
            Vector3 foot = S - n * dFace;
            Vector3 outward = Vector3.ProjectOnPlane(transform.right * Side, n); outward.y = 0f;
            if (dFace > 0.05f && outward.sqrMagnitude > 0.04f)
            {
                outward.Normalize();
                float now = Vector3.Dot(_touchPoint - foot, outward), want = dFace * 0.85f;
                if (now < want)
                {
                    Vector3 q = _touchPoint + outward * (want - now);
                    if ((q - S).magnitude < ArmReach * 0.95f && Physics.Raycast(q + n * 0.1f, -n, out var h, 0.2f, ~0, QueryTriggerInteraction.Ignore)
                        && Vector3.Dot(h.normal, n) > 0.9f && Mathf.Abs(Vector3.Dot(h.point - _touchPoint, n)) < 0.02f)
                    {
                        _touchPoint = h.point + n * 0.004f;
                        moved = h.collider;
                    }
                }
            }
            Vector3 along = Vector3.ProjectOnPlane(_touchPoint - S, n);
            if (along.sqrMagnitude > 1e-4f) _touchFingers = Vector3.ProjectOnPlane(along.normalized + Vector3.up * 0.5f, n).normalized;
            // Turned round the face's normal to the way the arm can lay the palm flat.
            if (Armed) _touchFingers = _body.ReachableFingers(_touchPoint, n, _touchFingers);
            return moved;
        }

        /// <summary>The hand flat on the wall (for the arm IK).</summary>
        void TouchHandPose()
        {
            HandPosition = _touchPoint;
            HandRotation = Quaternion.LookRotation(_touchFingers, _touchNormal);
            HandCurl = Mathf.MoveTowards(HandCurl, 0.15f, Time.deltaTime * 3f);
        }
    }
}
