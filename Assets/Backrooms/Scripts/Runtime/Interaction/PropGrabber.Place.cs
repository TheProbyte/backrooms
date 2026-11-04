using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Putting a held item down by hand (round 11, handling plan step 3b; Docs/HandlingPlan.md):
    ///  - Tap E: the hand sets it down where you look, as it naturally rests, lined up with a nearby edge, and lets go.
    ///  - Hold E: the hand brings it down there and keeps hold of it a few millimetres above the surface. While E is held,
    ///    look about to slide it (Shift: the view slowed to a quarter), the scroll wheel turns it (5 degrees a notch, 1 with
    ///    Shift), R tips it onto its next way of resting (upright, on its side, upside down...). It lines up with a desk's
    ///    edge or a wall within 3 degrees and sits flush on a wall within 1 cm (Alt: no help). Release E and it is let go,
    ///    exactly so. Right click (E still held) puts it back in the carry.
    /// Q still drops (tap) and throws (hold). Every hand item can be placed; furniture keeps its grab-and-drag.
    /// </summary>
    public partial class PropGrabber
    {
        /// <summary>E held this long (s) is a careful place; shorter, a quick one.</summary>
        public const float PlaceHoldTime = 0.2f;
        /// <summary>How long the hand takes to bring an item down onto its spot (s): over and down in a small arc.</summary>
        public const float PlaceDownTime = 0.3f;
        /// <summary>How high the item's arc down to its spot rises over the straight way (m, in the middle).</summary>
        public const float PlaceArc = 0.06f;
        /// <summary>Let go (E released): the last hover is lowered over this long before the hand opens (s).</summary>
        public const float PlaceLowerTime = 0.08f;
        /// <summary>How far above the surface the hand holds it while you adjust it (m).</summary>
        public const float PlaceHover = 0.004f;
        /// <summary>Lined up with an edge or a wall when within this (degrees); a quick place lines up within QuickSnap.</summary>
        public const float PlaceSnap = 3f, QuickSnap = 15f;
        /// <summary>Flush against a wall when within this of it (m).</summary>
        public const float PlaceFlush = 0.01f;
        /// <summary>How far from the eye a spot may be (m).</summary>
        public const float PlaceRange = 2.0f;

        enum PlacePhase { None, Pending, Careful, Setting }
        PlacePhase _place;
        float _placeDownAt, _placeW, _placeYaw;
        int _placeRest;
        bool _placeValid, _placeSnapped, _placeFine, _eWas, _rWas, _placeHas;
        Vector3 _placePos, _placeWant, _placeAt, _placeNormal;
        Quaternion _placeRot = Quaternion.identity, _placeRotWant = Quaternion.identity;
        Collider _placeOn;
        readonly Dictionary<WorldItem, Vector3[]> _corners = new Dictionary<WorldItem, Vector3[]>();
        static readonly Collider[] s_placeOver = new Collider[16];

        /// <summary>The hand is putting the held item down (E held, or setting it down after a tap).</summary>
        public bool Placing => _place != PlacePhase.None;
        /// <summary>Careful placing: E held, the item held just above its spot.</summary>
        public bool PlacingCarefully => _place == PlacePhase.Careful;
        /// <summary>Where the item is being put (its pose), for tests.</summary>
        public Vector3 PlacePosition => _placePos;
        public Quaternion PlaceRotation => _placeRot;

        // Tests: E held, scroll notches this frame, R pressed this frame, Alt held.
        [System.NonSerialized] public bool? botE;
        [System.NonSerialized] public float botScroll;
        [System.NonSerialized] public bool botR, botAlt;

        /// <summary>
        /// Every frame an item is held, before its own use: E pressed, held or released. True while placing (the item's own
        /// use and Q wait until it is done).
        /// </summary>
        bool UpdatePlacing(bool focus, Mouse mouse)
        {
            var kb = Keyboard.current;
            bool e = botE ?? (focus && kb != null && kb.eKey.isPressed);
            bool pressed = e && !_eWas, released = !e && _eWas;
            _eWas = e;
            var item = HeldItem;
            float dt = Time.deltaTime;
            if (_place == PlacePhase.None)
            {
                if (!pressed || !item || _itemT < 1f || ThrowCharge > 0f || _qDown >= 0f) return false;
                // Not while it is busy with something of its own (a plug going into a socket, a swing). (Round 11 review: asked
                // through its use pose, the sledgehammer, which always has one, could never be put down.)
                if (item.Busy) return false;
                _place = PlacePhase.Pending;
                _placeDownAt = Time.time;
                _placeRest = 0; _placeHas = false; _placeW = 0f;
                Vector3 f = Vector3.ProjectOnPlane(item.transform.forward, Vector3.up);
                _placeYaw = f.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(f).eulerAngles.y : transform.eulerAngles.y + 180f;
            }
            if (!item) { EndPlacing(); return false; }
            bool alt = botAlt || (kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed));
            bool shift = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
            switch (_place)
            {
                case PlacePhase.Pending:
                    FindPlace(item, alt ? 0f : QuickSnap);
                    if (released)
                    {
                        // A tap: down where you look, and let go.
                        if (_placeHas) _place = PlacePhase.Setting; else EndPlacing();
                        break;
                    }
                    if (Time.time - _placeDownAt >= PlaceHoldTime) _place = PlacePhase.Careful;
                    break;
                case PlacePhase.Careful:
                {
                    // Right click: back into the carry.
                    bool rmb = botRmb || (focus && mouse != null && mouse.rightButton.wasPressedThisFrame);
                    if (rmb) { EndPlacing(); return true; }
                    float scroll = botScroll;
                    if (focus && mouse != null && scroll == 0f) scroll = Notches(mouse.scroll.ReadValue().y);
                    botScroll = 0f;
                    if (scroll != 0f) _placeYaw += scroll * (shift ? 1f : 5f);
                    bool r = botR || (focus && kb != null && kb.rKey.isPressed);
                    if (r && !_rWas) _placeRest = (_placeRest + 1) % Mathf.Max(1, item.RestTurns.Length);
                    _rWas = r; botR = false;
                    _placeFine = shift;
                    FindPlace(item, alt ? 0f : PlaceSnap);
                    if (released)
                    {
                        if (!_placeHas) { EndPlacing(); break; }
                        if (_placeW >= 0.98f) PlaceNow(); else _place = PlacePhase.Setting;
                    }
                    break;
                }
                case PlacePhase.Setting:
                {
                    // Down the last of the hover and settled on the spot, then the hand lets go. (Round 11 review: let go
                    // at once, it snapped down from wherever it had got to.)
                    Vector3 final = FinalPlace;
                    // (And the palm really there: the body still bending, the item was held 20-36 cm up off the floor by the
                    // palm and dropped onto its spot when let go.)
                    bool palmOn = PalmError < 0.025f || Time.time - _setFrom > 0.6f;
                    if (_placeW >= 0.999f && (_placePos - final).sqrMagnitude < 1e-6f && _setFrom >= 0f && Time.time - _setFrom >= PlaceLowerTime && palmOn) { PlaceNow(); return true; }
                    break;
                }
            }
            if (_place == PlacePhase.None) return true;
            // Down to it: the body bends (a squat, a bend at the hips) as the hand goes there, never steps. (Round 11 review:
            // told to step in toward where the view met the floor, the body walked on and the spot went on ahead of it.)
            // (No further down than the body has bent for it: the item got to its spot in 0.3 s and the body 0.3 s later,
            // the arm 36 cm short in between.)
            _placeW = Mathf.MoveTowards(_placeW, _placeHas ? Mathf.Clamp01(BodyReady() * 1.15f + 0.1f) : 0f, dt / PlaceDownTime);
            if (_place != PlacePhase.Setting)
            {
                float k = 1f - Mathf.Exp(-dt / 0.05f);
                _placePos = _placeHas && _placeW > 0.01f ? Vector3.Lerp(_placePos, _placeWant, k) : _placeWant;
                _placeRot = _placeHas && _placeW > 0.01f ? Quaternion.Slerp(_placeRot, _placeRotWant, k) : _placeRotWant;
            }
            else
            {
                if (_setFrom < 0f) _setFrom = Time.time;
                _placePos = Vector3.MoveTowards(_placePos, FinalPlace, dt * 0.25f + (_placePos - FinalPlace).magnitude * dt / PlaceLowerTime);
                _placeRot = Quaternion.RotateTowards(_placeRot, _placeRotWant, dt * 360f);
            }
            return true;
        }

        /// <summary>How far the body has got with the squat and the bend its spot asks for, 0..1.</summary>
        float BodyReady()
        {
            float s = _placeStoop > 0.02f ? Mathf.Clamp01(_player.stoop / _placeStoop) : 1f;
            float h = _placeHinge > 1f ? Mathf.Clamp01(ReachHinge / _placeHinge) : 1f;
            return Mathf.Min(s, h);
        }

        /// <summary>The pose's position set down for good: on the surface (half a millimetre above), not hovering.</summary>
        Vector3 FinalPlace => _placeWant - Vector3.up * (PlaceHover - 0.0005f);
        float _setFrom = -1f;
        /// <summary>How far the body squats (the eye down, m) and bends at the hips (degrees) to put the item on its spot.</summary>
        float _placeStoop, _placeHinge;
        /// <summary>The body bends for the placing (UpdateStoop, UpdateReachHinge).</summary>
        bool PlaceBends => _place != PlacePhase.None && _placeHas;

        /// <summary>Scroll wheel steps: one notch is 120 (Windows) or 1, whichever the input system reports.</summary>
        static float Notches(float y)
        {
            if (Mathf.Abs(y) < 0.01f) return 0f;
            float n = Mathf.Abs(y) >= 30f ? Mathf.Round(Mathf.Abs(y) / 120f) : Mathf.Round(Mathf.Abs(y));
            return Mathf.Sign(y) * Mathf.Max(1f, n);
        }

        void EndPlacing()
        {
            _place = PlacePhase.None; _placeW = 0f; _placeHas = false; _placeFine = false; _placeSnapped = false; _setFrom = -1f;
            _placeStoop = 0f; _placeHinge = 0f;
        }

        /// <summary>
        /// Where the held item would go: on the floor, a desk, a shelf or another item the view is on (within PlaceRange and
        /// the body's reach), turned as asked (the yaw and the way it rests), lined up with the edge of what it stands on or a
        /// wall near it, flush on that wall, its lowest point a hover above the surface. Kept where it last could go if the
        /// view leaves anywhere it can.
        /// </summary>
        void FindPlace(WorldItem item, float snap)
        {
            var cam = _cam ? _cam.transform : transform;
            _placeValid = false;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, PlaceRange, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; RaycastHit hit = default;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c.transform.IsChildOf(transform) || c.transform.IsChildOf(item.transform) || _hits[i].distance >= best) continue;
                best = _hits[i].distance; hit = _hits[i];
            }
            if (best == float.MaxValue || hit.normal.y < 0.7f)
            {
                // A wall (or nothing): the floor in front of it, as Q sets a lamp down.
                if (!PlaceSpot(out var floorAt, out _)) return;
                if (!SurfaceUnder(item, floorAt, out hit)) return;
            }
            // Where the arm can put it with the body bending (no step). Further, it comes in along the way toward the feet,
            // onto whatever is under it there (the near part of a desk, or the floor in front of it).
            if (PoseAt(item, hit, snap, out var pos, out var rot, out bool snapped) && Reaches(item, pos, rot, out var plan))
            { Keep(hit, pos, rot, snapped, plan); return; }
            Vector3 feet = transform.position, far = hit.point;
            float lo = 0f, hiT = 1f; bool any = false;
            RaycastHit keepHit = default; Vector3 keepPos = default; Quaternion keepRot = default; bool keepSnap = false; ReachPlan keepPlan = default;
            for (int k = 0; k < 7; k++)
            {
                float t = (lo + hiT) * 0.5f;
                Vector3 q = Vector3.Lerp(new Vector3(feet.x, far.y, feet.z), far, t);
                if (SurfaceUnder(item, q, out var h2) && PoseAt(item, h2, snap, out var p2, out var r2, out bool s2) && Reaches(item, p2, r2, out var pl2))
                { lo = t; any = true; keepHit = h2; keepPos = p2; keepRot = r2; keepSnap = s2; keepPlan = pl2; }
                else hiT = t;
            }
            if (any && lo > 0.15f) Keep(keepHit, keepPos, keepRot, keepSnap, keepPlan);
        }

        /// <summary>The top of what is under a point (from up to 1.3 m above it): a floor, a desk, an item.</summary>
        bool SurfaceUnder(WorldItem item, Vector3 p, out RaycastHit hit)
        {
            hit = default;
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 1.3f, p.z), Vector3.down, _hits, 2.6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c.transform.IsChildOf(transform) || c.transform.IsChildOf(item.transform) || _hits[i].distance >= best) continue;
                best = _hits[i].distance; hit = _hits[i];
            }
            return best < float.MaxValue && hit.normal.y >= 0.7f;
        }

        /// <summary>The item's pose on a surface point: turned as asked, lined up and flush where it may be, a hover above it.
        /// False if it would be in something.</summary>
        bool PoseAt(WorldItem item, RaycastHit hit, float snap, out Vector3 pos, out Quaternion rot, out bool snapped)
        {
            float yaw = _placeYaw;
            snapped = false;
            if (snap > 0f && SnapYaw(hit.collider, ref yaw, snap)) snapped = true;
            var rests = item.RestTurns;
            Quaternion rest = rests.Length > 0 ? rests[Mathf.Clamp(_placeRest, 0, rests.Length - 1)] : Quaternion.identity;
            rot = Quaternion.Euler(0f, yaw, 0f) * rest;
            var corners = CornersOf(item);
            // Its footprint's middle on the point, its lowest point a hover above the surface.
            Vector3 lo = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue), hi = -lo;
            foreach (var c in corners) { Vector3 w = rot * c; lo = Vector3.Min(lo, w); hi = Vector3.Max(hi, w); }
            Vector3 mid = (lo + hi) * 0.5f;
            pos = hit.point - new Vector3(mid.x, 0f, mid.z);
            pos.y = hit.point.y - lo.y + PlaceHover;
            // A wall beside it: flush, and squared to it.
            _placeSnapped = false;
            if (snap > 0f) Flush(item, ref pos, ref rot, ref yaw, rest, lo, hi, snap);
            snapped |= _placeSnapped;
            // Not into anything (but what it stands on).
            Vector3 boxMid = pos + (lo + hi) * 0.5f, half = (hi - lo) * 0.5f - Vector3.one * 0.004f;
            half = Vector3.Max(half, Vector3.one * 0.003f);
            int m = Physics.OverlapBoxNonAlloc(boxMid + Vector3.up * 0.003f, half, s_placeOver, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < m; i++)
            {
                var c = s_placeOver[i];
                if (!c || c == hit.collider || c.transform.IsChildOf(transform) || c.transform.IsChildOf(item.transform)) continue;
                // (The chunk's floor mesh under it counts as what it stands on.)
                if (c.bounds.max.y <= pos.y + lo.y + 0.006f) continue;
                return false;
            }
            return true;
        }

        /// <summary>Can the hand put the item there with the body squatting and bending at the hips where it stands (no
        /// step), the arm a little bent? The plan says how far.</summary>
        bool Reaches(WorldItem item, Vector3 pos, Quaternion rot, out ReachPlan plan)
        {
            plan = default;
            Vector3 grip = pos + rot * Mirror(item.GripLocal);
            if (!Armed) return (grip - Eye).magnitude <= PlaceRange;
            var b = BodyNow(grip, null);
            b.room = 0f;
            return PlanReach(grip, b, 0.96f, null, out plan);
        }

        void Keep(RaycastHit hit, Vector3 pos, Quaternion rot, bool snapped, ReachPlan plan)
        {
            _placeAt = hit.point; _placeNormal = hit.normal; _placeOn = hit.collider;
            _placeSnapped = snapped;
            _placeValid = true;
            _placeHas = true;
            _placeWant = pos; _placeRotWant = rot;
            _placeStoop = plan.stoop; _placeHinge = plan.hinge;
            if (_placeW <= 0.01f) { _placePos = pos; _placeRot = rot; }
        }

        /// <summary>The yaw lined up with the sides of what it stands on (a desk's box, a shelf), within snap degrees.</summary>
        static bool SnapYaw(Collider on, ref float yaw, float snap)
        {
            if (!on || !(on is BoxCollider) && !on.GetComponentInParent<MovableProp>()) return false;
            float baseYaw = on.transform.eulerAngles.y;
            for (int k = 0; k < 4; k++)
            {
                float cand = baseYaw + k * 90f;
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, cand)) <= snap) { yaw = cand; return true; }
            }
            return false;
        }

        /// <summary>
        /// A wall within PlaceFlush of the item's footprint, on any side: the item squared to it (within snap degrees) and
        /// moved flush against it (a millimetre off).
        /// </summary>
        void Flush(WorldItem item, ref Vector3 pos, ref Quaternion rot, ref float yaw, Quaternion rest, Vector3 lo, Vector3 hi, float snap)
        {
            Vector3 mid = pos + (lo + hi) * 0.5f;
            float h = Mathf.Max(0.01f, (hi.y - lo.y) * 0.5f);
            Vector3 ext = (hi - lo) * 0.5f;
            Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            foreach (var d in dirs)
            {
                float reachOut = Mathf.Abs(d.x) * ext.x + Mathf.Abs(d.z) * ext.z;
                if (!Physics.Raycast(mid, d, out var wh, reachOut + PlaceFlush + 0.03f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (wh.collider.transform.IsChildOf(transform) || wh.collider.transform.IsChildOf(item.transform) || Mathf.Abs(wh.normal.y) > 0.3f) continue;
                // Squared to the wall.
                float wallYaw = Quaternion.LookRotation(-new Vector3(wh.normal.x, 0f, wh.normal.z)).eulerAngles.y;
                bool squared = false;
                for (int k = 0; k < 4; k++)
                {
                    float cand = wallYaw + k * 90f;
                    if (Mathf.Abs(Mathf.DeltaAngle(yaw, cand)) <= snap) { yaw = cand; squared = true; break; }
                }
                if (squared)
                {
                    rot = Quaternion.Euler(0f, yaw, 0f) * rest;
                    _placeSnapped = true;
                }
                // Flush: within PlaceFlush of its face, moved onto it.
                float gap = wh.distance - reachOut;
                if (gap < PlaceFlush + 0.002f) { pos += d * (gap - 0.001f); _placeSnapped = true; }
                break;
            }
        }

        /// <summary>The item's corners in its own space (its colliders' boxes; as built), kept per item.</summary>
        Vector3[] CornersOf(WorldItem item)
        {
            if (_corners.TryGetValue(item, out var cs)) return cs;
            var list = new List<Vector3>();
            var root = item.transform;
            foreach (var c in item.GetComponentsInChildren<Collider>(true))
            {
                if (!c || c.isTrigger) continue;
                Vector3 centre, size;
                switch (c)
                {
                    case BoxCollider b: centre = b.center; size = b.size; break;
                    case SphereCollider s: centre = s.center; size = Vector3.one * s.radius * 2f; break;
                    case CapsuleCollider cap:
                        centre = cap.center;
                        size = Vector3.one * cap.radius * 2f;
                        size[cap.direction] = Mathf.Max(cap.height, cap.radius * 2f);
                        break;
                    case MeshCollider mc when mc.sharedMesh: centre = mc.sharedMesh.bounds.center; size = mc.sharedMesh.bounds.size; break;
                    default: continue;
                }
                Matrix4x4 toRoot = root.worldToLocalMatrix * c.transform.localToWorldMatrix;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 o = new Vector3((k & 1) == 0 ? -0.5f : 0.5f, (k & 2) == 0 ? -0.5f : 0.5f, (k & 4) == 0 ? -0.5f : 0.5f);
                    list.Add(toRoot.MultiplyPoint3x4(centre + Vector3.Scale(o, size)));
                }
            }
            if (list.Count == 0) list.Add(Vector3.zero);
            cs = list.ToArray();
            if (_corners.Count > 32) _corners.Clear();
            _corners[item] = cs;
            return cs;
        }

        /// <summary>Lets go of the held item on its spot: it stays exactly so (asleep until something knocks it).</summary>
        void PlaceNow()
        {
            var item = HeldItem;
            EndPlacing();
            if (!item) return;
            Vector3 at = _placeWant - Vector3.up * (PlaceHover - 0.0005f);
            Quaternion rot = _placeRotWant;
            Vector3 fromP = item.transform.position; Quaternion fromR = item.transform.rotation;
            HeldItem = null;
            AimedSocket = null;
            item.SetHeld(false);
            item.Body.linearVelocity = Vector3.zero; item.Body.angularVelocity = Vector3.zero;
            // The last of the way glides (the arm's pose and the spot can differ by a few centimetres: the sledgehammer, which
            // turns itself in the hand, jumped 25 cm onto its spot as it was let go).
            float d = (fromP - at).magnitude, a = Quaternion.Angle(fromR, rot);
            if (d > 0.003f || a > 2f)
            {
                item.settling = true;
                item.Body.isKinematic = true;
                _settles.Add(new Settle { item = item, fromP = fromP, fromR = fromR, toP = at, toR = rot, t = 0f, time = Mathf.Clamp(Mathf.Max(d / 1.2f, a / 400f), 0.06f, 0.15f) });
            }
            else
            {
                item.Body.position = at; item.Body.rotation = rot;
                item.transform.SetPositionAndRotation(at, rot);
                item.Body.Sleep();
            }
            item.OnPlaced();
            // A soft knock, as what it is meets what it is on.
            var sounder = item.GetComponent<Audio.ImpactSounder>();
            if (sounder) Audio.ImpactAudio.SetDown(sounder, _placeOn, _placeAt, _placeNormal);
            // The body passes through it until it has stepped away (set down at the feet, it would shove the body).
            var cc = GetComponent<CharacterController>();
            if (cc)
            {
                foreach (var c in item.GetComponentsInChildren<Collider>(true)) if (c && c.enabled) Physics.IgnoreCollision(cc, c, true);
                if (!_passing.Contains(item)) _passing.Add(item);
            }
            LastPlaced = item;
        }

        struct Settle { public WorldItem item; public Vector3 fromP, toP; public Quaternion fromR, toR; public float t, time; }
        readonly List<Settle> _settles = new List<Settle>();

        /// <summary>Items let go a little off their spot glide onto it, then sleep (every frame, from LateUpdate).</summary>
        void UpdateSettles()
        {
            for (int i = _settles.Count - 1; i >= 0; i--)
            {
                var s = _settles[i];
                if (!s.item || s.item.Held) { if (s.item) s.item.settling = false; _settles.RemoveAt(i); continue; }
                s.t = Mathf.Min(1f, s.t + Time.deltaTime / s.time);
                float e = Mathf.SmoothStep(0f, 1f, s.t);
                Vector3 p = Vector3.Lerp(s.fromP, s.toP, e); Quaternion r = Quaternion.Slerp(s.fromR, s.toR, e);
                s.item.Body.position = p; s.item.Body.rotation = r;
                s.item.transform.SetPositionAndRotation(p, r);
                if (s.t >= 1f)
                {
                    s.item.settling = false;
                    s.item.Body.isKinematic = false;
                    s.item.Body.linearVelocity = Vector3.zero; s.item.Body.angularVelocity = Vector3.zero;
                    s.item.Body.Sleep();
                    _settles.RemoveAt(i);
                }
                else _settles[i] = s;
            }
        }

        /// <summary>The last item set down by hand (for tests).</summary>
        public WorldItem LastPlaced { get; private set; }

        /// <summary>Careful placing: small camcorder focus marks round the spot, brighter when it has lined up.</summary>
        void PlaceMarks()
        {
            if (_place != PlacePhase.Careful || !_placeHas) return;
            FocusMarksAlpha(_placeAt, 0.06f, 16f, _placeSnapped ? 0.9f : 0.45f);
        }
    }
}
