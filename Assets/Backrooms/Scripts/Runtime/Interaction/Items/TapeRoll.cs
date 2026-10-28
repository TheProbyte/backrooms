using System.Collections.Generic;
using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A roll of blue painter's tape, carried on the fingers through its core.
    ///  - Hold the right mouse button at a wall, doorframe or floor within reach: the hand raises the roll to it and the
    ///    spot the tape will start at gets focus marks (and a little tab of tape).
    ///  - Hold the left mouse button: the end is stuck down there; drag along the surface to pull a strip off the roll (the
    ///    right button can be let go now). The strip follows the drag but only bends a little (tape can be eased round a
    ///    gentle curve, not a sharp one), and it goes round corners: over the edge of a wall end or a doorframe onto its
    ///    other face, and up a wall from the floor. Move back and it peels up again.
    ///  - Let go of the left button: it is torn off and pressed down (<see cref="Marks"/>). Right mouse again while
    ///    pulling: cancelled, the tape goes back on the roll.
    /// Up to <see cref="MaxStrip"/> a strip; the roll holds <see cref="RollMetres"/>. Model: Resources/Items/TapeRoll.fbx.
    /// </summary>
    public class TapeRoll : WorldItem, IHandGrip
    {
        public static float RollMetres = 40f;
        public const float MaxStrip = 2.4f, MinStrip = 0.03f;
        [Tooltip("Furthest you can stick the end down from your eye (m).")] public float reach = 1.6f;
        [Tooltip("Tightest bend the tape takes on a flat surface (m radius).")] public float bendRadius = 0.45f;
        [NotTuned] public float metres = 40f;

        const float Step = 0.02f;                     // the strip is laid in pieces this long

        AudioSource _pull, _fx;
        bool _pulling, _aiming, _aimValid;
        Vector3 _aimPoint, _aimNormal;
        readonly List<Vector3> _pts = new List<Vector3>();
        readonly List<Vector3> _nrm = new List<Vector3>();       // the surface under the piece starting at each point
        float _len, _lastLen, _pose;
        readonly RaycastHit[] _hits = new RaycastHit[8];

        // Held on the fingers through its core: the roll's axis (local X) along the fingers, which point forward and a
        // little up and left from a hand held low on the right.
        static readonly Vector3 Fingers = new Vector3(-0.12f, 0.3f, 1f).normalized;
        public override Quaternion HoldTurn
        {
            get
            {
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, Fingers).normalized;
                return Quaternion.LookRotation(Vector3.Cross(Fingers, up), up);
            }
        }
        public override Vector3 HoldOffset => new Vector3(0.15f, -0.25f, 0.4f);
        public override Vector3 UseOffset => new Vector3(0.12f, -0.16f, 0.42f);
        // The index, middle and ring fingers through the core, the roll resting on their backs; the little finger, shorter,
        // curled beside the roll; the thumb outside it, its pad on the roll's edge (the wall pinched between it and the
        // fingers). The palm 3 cm off the roll's side, the hand rolled 6 degrees. (Found by trying 1,000 ways against the
        // ring: index, middle, ring and thumb skin 0.7-1.3 mm into it (skin gives 1 mm), the palm clear. The round-5 grip had
        // the palm 1.3 cm from the side with all four fingers in the core: the little finger 7 mm and the palm 5.5 mm in it.)
        public override Vector3 GripLocal => TestGrip ?? new Vector3(-0.056f, 0.058f, 0f);
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Euler(TestGrip.HasValue ? TestTurn : new Vector3(0f, 0f, 6f));
        public override float GripCurl => 0.55f;
        public float FingerCurl(int finger) => TestCurls != null ? TestCurls[finger] : 0.55f;
        public float ThumbCurl => TestCurls != null ? TestCurls[4] : 0.8f;
        public float ThumbOpposition => TestCurls != null ? TestCurls[5] : 0.8f;
        /// <summary>Tests: a grip to try instead (the palm's point, the hand turned about its own axes (degrees), finger curls 0-3, the thumb's curl and turn).</summary>
        public static Vector3? TestGrip;
        public static Vector3 TestTurn;
        public static float[] TestCurls;
        /// <summary>The roll: a ring of tape on its core, round its axis (x); the fingers inside the core stop on it.</summary>
        public override float ContactDistance(Vector3 p)
        {
            float outer = _wound ? FullRadius * _wound.localScale.y : FullRadius;
            return RingDistance(p, new Vector3(0f, FullRadius, 0f), Vector3.right, 0.038f, outer, 0.024f);
        }
        public override string Status => metres <= 0.01f ? "tape: empty"
            : _pulling ? $"tape {metres - _len:0.0} m  (release: tear off, right mouse: cancel)"
            : _aiming ? (_aimValid ? "tape: hold left mouse to stick it here" : "tape: get closer to a wall or the floor")
            : $"tape {metres:0.0} m  (hold right mouse at a wall)";
        public override float Amount { get => Mathf.Clamp01(metres / RollMetres); set { metres = Mathf.Clamp01(value) * RollMetres; Shape(); } }

        // The model's wound tape ("Wound", origin at the roll's centre, roll axis local X) shrinks toward the core as tape
        // is used. It can only shrink so far (its inner edge would show in the core's hole), then the roll becomes the
        // empty model: the bare cardboard core with a torn stub of tape left on it.
        const float EmptyRadius = 0.0475f, FullRadius = 0.0525f;
        Transform _wound, _full;
        GameObject _empty;

        void Shape()
        {
            if (!_wound) _wound = FindDeep(transform, "Wound");
            if (!_full) _full = transform.Find("Model");
            bool empty = metres <= 0.01f;
            if (_empty) { _empty.SetActive(empty); if (_full) _full.gameObject.SetActive(!empty); }
            else if (_wound) _wound.gameObject.SetActive(!empty);
            if (!_wound) return;
            float f = Mathf.Clamp01(metres / RollMetres);
            // Tape on a roll is wound area: the radius goes with the square root of what is left.
            float r = Mathf.Sqrt(EmptyRadius * EmptyRadius + f * (FullRadius * FullRadius - EmptyRadius * EmptyRadius));
            float k = r / FullRadius;
            _wound.localScale = new Vector3(1f, k, k);
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }
        public override float UseBlend => _pulling || _aiming ? 1f : 0f;

        public static TapeRoll Create(Vector3 position, Quaternion rotation)
        {
            var go = ItemModels.Body("TapeRoll", position, rotation, 0.25f, "TapeRoll", new Vector3(0.048f, 0.105f, 0.105f), "TapeBlue", false, new Vector3(0f, 0.0525f, 0f));
            var t = go.AddComponent<TapeRoll>();
            t.itemId = "tape"; t.displayName = "Blue tape"; t.metres = RollMetres;
            Generation.PoolLitSwitch.Attach(go);
            return t;
        }

        protected override void Awake()
        {
            // The used-up roll, ready under the full one (made before the item's setup so it gets the same treatment).
            if (!transform.Find("ModelEmpty") && Resources.Load<GameObject>("Items/TapeRollEmpty"))
            {
                _empty = ItemModels.Attach(transform, "TapeRollEmpty", Vector3.one * 0.05f, "TapeCore");
                _empty.name = "ModelEmpty";
            }
            base.Awake();
            _pull = ItemModels.Source(gameObject, 10f);
            _pull.clip = SoundBank.TapePull; _pull.loop = true; _pull.volume = 0f;
            _fx = ItemModels.Source(gameObject, 10f);
            Shape();
        }

        static bool Stickable(in RaycastHit h) =>
            h.collider && !h.collider.isTrigger && !h.collider.GetComponentInParent<MovableProp>() && !h.collider.GetComponentInParent<WorldItem>()
            && !h.collider.GetComponent<CharacterController>();

        /// <summary>The nearest solid thing along a ray (not the player, not this roll).</summary>
        bool Cast(Vector3 from, Vector3 dir, float dist, out RaycastHit hit)
        {
            hit = default;
            int n = Physics.RaycastNonAlloc(from, dir, _hits, dist, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c.transform.IsChildOf(transform) || c.GetComponent<CharacterController>() || _hits[i].distance >= best) continue;
                best = _hits[i].distance; hit = _hits[i];
            }
            return best < float.MaxValue;
        }

        /// <summary>Surface under a point of the face with normal n (within a couple of centimetres of its plane), stickable.</summary>
        bool Under(Vector3 p, Vector3 n, out RaycastHit hit)
        {
            if (!Cast(p + n * 0.02f, -n, 0.04f, out hit)) return false;
            return Vector3.Dot(hit.normal, n) > 0.95f && Stickable(hit);
        }

        public override void HoldUpdate(in HoldInput i)
        {
            // Right mouse held, not pulling yet: the hand goes to the wall and shows where the tape will start.
            _aiming = !_pulling && i.aim && metres > MinStrip;
            _aimValid = false;
            if (_aiming && i.hit && i.hitInfo.distance <= reach && Stickable(i.hitInfo))
            {
                _aimValid = true;
                _aimPoint = i.hitInfo.point; _aimNormal = i.hitInfo.normal;
                Vector3 dir = Tab(_aimNormal, i.aimDir);
                Marks.Instance.SetPreview(_aimPoint - dir * 0.012f, _aimPoint + dir * 0.012f, _aimNormal);
            }
            else if (!_pulling) Marks.Existing?.ClearPreview();

            if (_aiming && _aimValid && i.pressed)
            {
                _pulling = true; _aiming = false;
                _pts.Clear(); _nrm.Clear();
                _pts.Add(_aimPoint); _nrm.Add(_aimNormal);
                _len = _lastLen = 0f;
                _fx.PlayOneShot(SoundBank.TapeTear, 0.25f);
            }
            if (_pulling)
            {
                if (i.aimPressed) { Cancel(); return; }
                Follow(i);
                if (_pts.Count >= 2) Marks.Instance.SetPreview(_pts, _nrm); else Marks.Instance.ClearPreview();
                float speed = Mathf.Abs(_len - _lastLen) / Mathf.Max(i.dt, 1e-4f);
                _lastLen = _len;
                _pull.volume = Mathf.MoveTowards(_pull.volume, Mathf.Clamp01(speed / 0.8f) * 0.5f, i.dt * 8f);
                if (_pull.volume > 0.001f && !_pull.isPlaying) _pull.Play();

                if (!i.use)
                {
                    // Torn off and pressed down.
                    if (_len >= MinStrip)
                    {
                        Simplify();
                        Marks.Instance.AddTape(_pts, _nrm);
                        metres = Mathf.Max(0f, metres - _len);
                        Shape();
                        _fx.PlayOneShot(SoundBank.TapeTear, 0.6f);
                    }
                    Stop();
                }
            }
            _pose = Mathf.MoveTowards(_pose, _pulling || (_aiming && _aimValid) ? 1f : 0f, i.dt * 5f);
            _holder = i.hand;
            // Out of the arm's reach (low on a wall, the floor): the body bends toward it.
            if (_pose > 0.3f && i.hand)
            {
                UseTarget(out Vector3 p, out Quaternion r);
                Vector3 palm = p + r * GripLocal;
                if ((palm - i.hand.ReachShoulder).magnitude > i.hand.ArmReach * 0.92f) i.hand.ReachToward(palm, false);
            }
        }

        PropGrabber _holder;

        /// <summary>Which way the little tab shown before sticking lies: across the view on a wall, along it on the floor.</summary>
        static Vector3 Tab(Vector3 n, Vector3 view)
        {
            Vector3 d = Mathf.Abs(n.y) > 0.7f ? Vector3.ProjectOnPlane(view, n) : Vector3.Cross(n, Vector3.up);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.ProjectOnPlane(Vector3.right, n).normalized;
        }

        void Cancel()
        {
            _fx.PlayOneShot(SoundBank.TapeTear, 0.15f);
            Stop();
        }

        /// <summary>
        /// The free end goes after what the view is on: peeling back up toward it, or laying more strip toward it over the
        /// surfaces (in short pieces, turning no faster than <see cref="bendRadius"/> allows, round corners onto the next face).
        /// </summary>
        void Follow(in HoldInput i)
        {
            Vector3 end = _pts[_pts.Count - 1], n = _nrm[_nrm.Count - 1];
            // Where the view is on: the surface it hits (if stickable and near the strip), else its line across the plane
            // of the face the end is on.
            Vector3 target;
            if (i.hit && Stickable(i.hitInfo) && (i.hitInfo.point - end).sqrMagnitude < 1.2f * 1.2f) target = i.hitInfo.point;
            else
            {
                float denom = Vector3.Dot(i.aimDir, n);
                if (denom > -1e-3f) return;
                target = i.eye + i.aimDir * (Vector3.Dot(end - i.eye, n) / denom);
            }
            // Back toward the start: peel up the last pieces.
            while (_pts.Count >= 2)
            {
                Vector3 a = _pts[_pts.Count - 2], b = _pts[_pts.Count - 1];
                Vector3 seg = b - a;
                if (Vector3.Dot(target - a, seg) >= seg.sqrMagnitude * 0.5f) break;
                _pts.RemoveAt(_pts.Count - 1); _nrm.RemoveAt(_nrm.Count - 1);
            }
            _len = Length();
            // Forward: lay pieces toward it.
            float maxTurn = Step / Mathf.Max(0.05f, bendRadius) * Mathf.Rad2Deg;
            float limit = Mathf.Min(MaxStrip, metres);
            for (int k = 0; k < 12 && _len + Step <= limit; k++)
            {
                end = _pts[_pts.Count - 1]; n = _nrm[_nrm.Count - 1];
                Vector3 to = target - end;
                if (to.magnitude < Step * 0.75f) break;
                Vector3 want = Vector3.ProjectOnPlane(to, n);
                if (want.magnitude < Step * 0.5f)
                {
                    // Straight up from here (on a wall standing on this face): on to the foot of it, the way it was going.
                    if (Vector3.Dot(to, n) <= 0f || _pts.Count < 2) break;
                    want = Vector3.ProjectOnPlane(end - _pts[_pts.Count - 2], n);
                    if (want.sqrMagnitude < 1e-8f) break;
                }
                Vector3 dir = want.normalized;
                if (_pts.Count >= 2)
                {
                    // The tape only bends so far: the heading turns toward the target by a little each piece.
                    Vector3 prev = Vector3.ProjectOnPlane(end - _pts[_pts.Count - 2], n);
                    if (prev.sqrMagnitude > 1e-8f)
                    {
                        prev.Normalize();
                        float ang = Vector3.SignedAngle(prev, dir, n);
                        if (Mathf.Abs(ang) > 100f) break;                              // the target is behind: wait
                        dir = Quaternion.AngleAxis(Mathf.Clamp(ang, -maxTurn, maxTurn), n) * prev;
                    }
                }
                if (!Advance(end, n, dir)) break;
                _len = Length();
            }
        }

        /// <summary>One piece from the end along dir: on the same face, up the next face (an inside corner), or over the edge
        /// onto the face beyond it (an outside corner). False if the surface goes nowhere stickable.</summary>
        bool Advance(Vector3 p, Vector3 n, Vector3 dir)
        {
            // Inside corner: something in the way just above the surface.
            if (Cast(p + n * 0.004f, dir, Step + 0.004f, out var wall) && Vector3.Dot(wall.normal, n) < 0.5f)
            {
                if (!Stickable(wall)) return false;
                Vector3 corner = wall.point - n * 0.004f;
                Vector3 n2 = wall.normal;
                if ((corner - p).sqrMagnitude > 1e-6f) Add(corner, n);
                _nrm[_nrm.Count - 1] = n2;                                             // the piece from the corner on lies on the wall
                Vector3 up = Vector3.ProjectOnPlane(dir, n2) + n * Mathf.Max(0.05f, -Vector3.Dot(dir, n2));
                Vector3 q = corner + up.normalized * Step;
                if (!Under(q, n2, out var on)) return false;
                Add(on.point, n2);
                return true;
            }
            Vector3 next = p + dir * Step;
            if (Under(next, n, out var hit)) { Add(hit.point, n); return true; }
            // Outside corner: the face ends before the next piece. Find the edge, then the face round it.
            float lo = 0f, hi = Step;
            for (int k = 0; k < 5; k++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Under(p + dir * mid, n, out _)) lo = mid; else hi = mid;
            }
            Vector3 edge = p + dir * lo;
            if (!Cast(edge + dir * 0.03f - n * 0.008f, -dir, 0.04f, out var side) || Vector3.Dot(side.normal, n) > 0.5f || !Stickable(side)) return false;
            Vector3 n3 = side.normal;
            if (lo > 0.002f) Add(edge, n);
            _nrm[_nrm.Count - 1] = n3;
            Vector3 down = Vector3.ProjectOnPlane(dir, n3) - n * Mathf.Max(0.05f, Vector3.Dot(dir, n3));
            Vector3 r = edge + down.normalized * Step;
            if (!Under(r, n3, out var on2)) return false;
            Add(on2.point, n3);
            return true;
        }

        void Add(Vector3 p, Vector3 n) { _pts.Add(p); _nrm.Add(n); }

        float Length()
        {
            float l = 0f;
            for (int k = 1; k < _pts.Count; k++) l += Vector3.Distance(_pts[k - 1], _pts[k]);
            return l;
        }

        /// <summary>Pieces in a straight line on one face become one (fewer quads to keep).</summary>
        void Simplify()
        {
            for (int k = _pts.Count - 2; k >= 1; k--)
            {
                if (Vector3.Dot(_nrm[k - 1], _nrm[k]) < 0.999f) continue;
                Vector3 a = (_pts[k] - _pts[k - 1]).normalized, b = (_pts[k + 1] - _pts[k]).normalized;
                if (Vector3.Dot(a, b) < 0.99995f) continue;
                _pts.RemoveAt(k); _nrm.RemoveAt(k);
            }
        }

        void Stop()
        {
            _pulling = false;
            _pts.Clear(); _nrm.Clear(); _len = 0f;
            Marks.Existing?.ClearPreview();
            if (_pull) { _pull.volume = 0f; _pull.Stop(); }
        }

        public override bool AimMark(out Vector3 at)
        {
            at = _aimPoint;
            return _aiming && _aimValid;
        }

        /// <summary>
        /// Aiming or pulling, the roll is held against the surface at the start or the free end, as far as the arm reaches
        /// from the shoulder. (It was let go out to 0.62 m from the eye: past the arm's reach, the palm was up to 93 mm off
        /// the roll, the roll going with the hand.)
        /// </summary>
        public override float UsePose(out Vector3 position, out Quaternion rotation)
        {
            UseTarget(out position, out rotation);
            if (_holder)
            {
                Vector3 s = _holder.ReachShoulder, palm = position + rotation * GripLocal;
                Vector3 clamped = s + Vector3.ClampMagnitude(palm - s, _holder.ArmReach * 0.95f);
                position += clamped - palm;
            }
            return _pose * 0.8f;
        }

        void UseTarget(out Vector3 position, out Quaternion rotation)
        {
            var eye = Camera.main ? Camera.main.transform.position : transform.position;
            Vector3 at, n, fwd;
            if (_pulling && _pts.Count > 0)
            {
                at = _pts[_pts.Count - 1]; n = _nrm[_nrm.Count - 1];
                fwd = _pts.Count >= 2 ? at - _pts[_pts.Count - 2] : Vector3.zero;
            }
            else { at = _aimPoint; n = _aimNormal; fwd = Vector3.zero; }
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.ProjectOnPlane(Camera.main ? Camera.main.transform.forward : Vector3.forward, n);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.ProjectOnPlane(Vector3.up, n);
            Vector3 p = at + n * 0.06f;
            position = eye + Vector3.ClampMagnitude(p - eye, 0.62f);
            rotation = Quaternion.LookRotation(fwd.normalized, n);
        }

        protected override void OnDropped() { Stop(); _aiming = false; _pose = 0f; }

        void OnDestroy() { if (_pulling || _aiming) Marks.Existing?.ClearPreview(); }
    }
}
