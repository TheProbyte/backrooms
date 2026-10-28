using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The free hand holds items (<see cref="WorldItem"/>, e.g. the spray can): click one within reach to pick it up,
    /// hold the left mouse button to use it, Q to drop it. While it holds something the hand does nothing else (no
    /// grabbing furniture, no buttons). The item is carried low on the hand's side of the view and raised a little
    /// while in use; the arm (FirstPersonBody) grips it.
    /// </summary>
    public partial class PropGrabber
    {
        public WorldItem HeldItem { get; private set; }
        Quaternion _swayRot;
        Vector3 _swayPos, _swayVel;
        bool _swayInit;
        float _bobPhase;
        [Header("Items")]
        [Tooltip("Reach for picking up items (m).")] public float itemRange = 2.1f;

        float _itemT = 1f;               // 0..1 while the item comes up with the hand from where it lay
        float _switchT = 1f; Vector3 _switchPos; Quaternion _switchRot;   // the hand easing from one held item to the next
        /// <summary>The item just taken still lies where it was (the hand on its way to it, or closing on it).</summary>
        public bool ItemWaitingForHand => HeldItem && _itemT <= 0f;
        bool _weighted;                  // the held item's weight is slowing the player
        Vector3 _itemFromPos;
        Quaternion _itemFromRot;

        [Tooltip("Picking up forgives this much aim (degrees off the centre of the view).")] public float pickAssistAngle = 14f;
        readonly Collider[] _near = new Collider[48];
        /// <summary>What a click would pick up right now (for the focus marks), or null.</summary>
        public WorldItem AimedItem { get; private set; }
        /// <summary>The point of a cord the view is on (its plug is the AimedItem), or null.</summary>
        public Vector3? AimedCordPoint { get; private set; }
        /// <summary>The socket a click would plug into with the held item (for the focus marks), or null.</summary>
        public Socket AimedSocket { get; private set; }
        [Tooltip("Plugging in forgives this much aim (degrees off the centre of the view).")] public float socketAssistAngle = 10f;
        [Tooltip("Furthest socket the hand plugs into (m from the eye).")] public float socketReach = 2.2f;
        [Tooltip("A cord's plug further than this from the eye is not taken by looking at the cord (m).")] public float cordGrabReach = 4.5f;

        /// <summary>
        /// The item a click picks up: the one the centre of the view is on, or else the item nearest the aim within
        /// <see cref="pickAssistAngle"/> and reach that can be seen from the eye (so small things like chalk are easy).
        /// </summary>
        bool LookItem(float range, out WorldItem item)
        {
            item = null;
            AimedCordPoint = null;
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Collider first = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                best = _hits[i].distance;
                first = _hits[i].collider;
                item = first.GetComponentInParent<WorldItem>();
            }
            // A cord in front of whatever the view is on: its plug (you take the cord, not what it belongs to).
            var cord = Cord.Aimed(cam.position, cam.forward, Mathf.Min(best + 0.05f, range), out float along, out Vector3 onCord);
            // (Only a plug the body can reach: round 8, the hand takes it where it lies; it flew in from 4.5 m.)
            if (cord && (!item || along < best - 0.02f) && cord.endA is Plug plug && !plug.Held && plug.gameObject.activeInHierarchy
                && (plug.transform.position - cam.position).sqrMagnitude < cordGrabReach * cordGrabReach && BodyReaches(plug))
            {
                item = plug; AimedCordPoint = onCord;
                return true;
            }
            // The item the view is on: offered only if the body can get to it (with a lean or a short step); further, the
            // player walks closer (round 8: it flew to the hand from up to 2.1 m).
            if (item && !item.Held) { if (BodyReaches(item)) return true; item = null; return false; }
            item = null;
            // Looking straight at furniture: that is what the click is for, unless the view passes close by an item lying on
            // it that the body can reach (round 9: chalk on a desk was never offered, the desk took the click).
            bool onFurniture = first && first.GetComponentInParent<MovableProp>();

            // Near the aim: everything within reach, scored by how far off the aim it is (and a little by distance).
            Vector3 eye = cam.position, fwd = cam.forward;
            int m = Physics.OverlapSphereNonAlloc(eye + fwd * (range * 0.5f), range * 0.5f + 0.3f, _near, ~0, QueryTriggerInteraction.Ignore);
            float bestScore = float.MaxValue;
            for (int i = 0; i < m; i++)
            {
                var it = _near[i].GetComponentInParent<WorldItem>();
                if (!it || it.Held || (it == item)) continue;
                Vector3 c = _near[i].bounds.center, to = c - eye;
                float dist = to.magnitude;
                if (dist > range + 0.2f || dist < 0.05f) continue;
                float angle = Vector3.Angle(fwd, to);
                float off = Vector3.Cross(fwd, to).magnitude;                     // metres from the line of sight
                if (angle > pickAssistAngle && off > 0.12f) continue;
                if (angle > pickAssistAngle * 2f) continue;
                if (onFurniture && (off > 0.1f || Mathf.Abs(Vector3.Dot(to, fwd) - best) > 0.35f)) continue;
                float score = angle + dist * 3f;
                if (score >= bestScore || !InSight(eye, c, it) || !BodyReaches(it)) continue;
                bestScore = score; item = it;
            }
            return item;
        }

        /// <summary>Nothing solid between the eye and a point of the item.</summary>
        bool InSight(Vector3 eye, Vector3 point, WorldItem it)
        {
            Vector3 d = point - eye;
            int n = Physics.RaycastNonAlloc(eye, d.normalized, _hits, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var t = _hits[i].collider.transform;
                if (t.IsChildOf(transform) || t.IsChildOf(it.transform)) continue;
                if (_hits[i].distance < d.magnitude - 0.05f) return false;
            }
            return true;
        }

        /// <summary>Camcorder-style focus marks around what a click would pick up.</summary>
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !_cam) return;
            if (HeldItem)
            {
                // The socket a click would plug into: small marks round it (or where the held item points: tape's start).
                if (AimedSocket != null) FocusMarks(AimedSocket.Position, 0.022f, 16f);
                else if (HeldItem.AimMark(out var mark)) FocusMarks(mark, 0.03f, 16f);
                return;
            }
            if (AimedPoke != null && Current == State.Idle && _poke == PokePhase.None) { FocusMarks(AimedPoke.FocusPoint, 0.045f, 18f); return; }
            if (AimedDoorGrip.HasValue && !OnDoor) { FocusMarks(AimedDoorGrip.Value, 0.05f, 18f); return; }
            if (!AimedItem || Current != State.Idle) return;
            if (AimedCordPoint.HasValue) { FocusMarks(AimedCordPoint.Value, 0.03f, 18f); return; }
            var b = new Bounds(AimedItem.transform.position, Vector3.zero);
            foreach (var r in AimedItem.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            FocusMarks(b.center, b.extents.magnitude, 22f);
        }

        /// <summary>Camcorder-style focus marks (four corners) round a point of the world, sized by a world radius.</summary>
        void FocusMarks(Vector3 at, float radius, float minSize)
        {
            Vector3 sp = _cam.WorldToScreenPoint(at);
            if (sp.z <= 0f) return;
            float size = Mathf.Clamp(radius / Mathf.Max(0.2f, sp.z) * Screen.height * 0.9f, minSize, 160f);
            float x = sp.x, y = Screen.height - sp.y, h = size * 0.5f, len = Mathf.Max(6f, size * 0.22f), t = Mathf.Max(1.5f, Screen.height / 540f);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            void Bar(float bx, float by, float bw, float bh) => GUI.DrawTexture(new Rect(bx, by, bw, bh), Texture2D.whiteTexture);
            Bar(x - h, y - h, len, t); Bar(x - h, y - h, t, len);
            Bar(x + h - len, y - h, len, t); Bar(x + h - t, y - h, t, len);
            Bar(x - h, y + h - t, len, t); Bar(x - h, y + h - len, t, len);
            Bar(x + h - len, y + h - t, len, t); Bar(x + h - t, y + h - len, t, len);
            GUI.color = old;
        }

        /// <summary>The empty hand clicks something that has its own use (a plug in a socket).</summary>
        bool TryHandUse()
        {
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, itemRange, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue; IHandUse use = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider.transform.IsChildOf(transform) || _hits[i].distance >= best) continue;
                var u = _hits[i].collider.GetComponentInParent<IHandUse>();
                if (u == null && _hits[i].collider.isTrigger) continue;
                best = _hits[i].distance; use = u;
            }
            return use != null && use.HandUse(this);
        }

        bool TryPickUp()
        {
            if (!LookItem(itemRange, out var item) || item.Held) return false;
            PickUp(item);
            return true;
        }

        /// <summary>
        /// Takes an item by hand: the hand goes out to it where it lies (the body bending and stepping in) and it is not
        /// touched until the palm is on it (PropGrabber.Pickup). One already where the hand holds things (put there) is taken
        /// at once, the hand closing on it where it is.
        /// </summary>
        public void PickUp(WorldItem item)
        {
            if (!item || item == HeldItem) return;
            if (HeldItem) Drop();
            if (Current != State.Idle) Release("picked up an item");
            AimedItem = null; AimedCordPoint = null;
            if (_fetch) EndFetch();
            if (InHandsPlace(item, out bool palmOn)) { Take(item, palmOn); return; }
            StartFetch(item);
        }

        /// <summary>
        /// Into the hand where it lies: palmOn, the palm is on it (the fingers close, then it comes up with the hand); else
        /// it waits there, held, until the palm gets to it (ReachFor), or is left there if the hand cannot.
        /// </summary>
        void Take(WorldItem item, bool palmOn, bool fromReach = false)
        {
            // Taking something with the hand already out and off it (a reeled-in lamp, its plug just let go): the hand eases
            // over to it (its target jumped there in one frame).
            if (!palmOn && HandBlend > 0.5f) { _switchPos = HandPosition; _switchRot = HandRotation; _switchT = 0f; } else _switchT = 1f;
            HeldItem = item;
            AimedItem = null; AimedCordPoint = null;
            _itemFromPos = item.transform.position; _itemFromRot = item.transform.rotation;
            _itemT = 0f; _graspOn = palmOn; _graspT = 0f;
            // (The body's plan for the reach stays through the grasp and the lift: the squat and the bend come back off as it
            // comes up. Put straight in the hand, there is none.)
            if (!palmOn) BeginReachFor(item); else if (!fromReach) ClearPlan();
            MicThump();
            _swayInit = false; _qDown = -1f; ThrowCharge = 0f;
            _easeLocal = _easeWant = Quaternion.identity; _easeNext = 0f;
            item.SetHeld(true);
        }

        /// <summary>Lets go of the held item where it is, with no toss (a plug pushed into a socket).</summary>
        public void LetGo(WorldItem item)
        {
            if (!item || HeldItem != item) return;
            HeldItem = null;
            item.SetHeld(false);
        }

        // ---------------- Set down beside the feet ----------------
        // To plug in a lamp or a coiled cord held in the hand, the hand puts it down beside the feet, off the way to the outlet
        // and near enough for its cord, then takes the plug to the outlet. (A lamp was set down where the view met the floor,
        // under the outlet: the body stepping in climbed onto it, the plug stopped 15 cm short, and the lamp was launched 46 m
        // when the body came off it; a coil was tossed toward the wall.) The body passes through it until it has stepped away.

        /// <summary>Where it may stand, beside the feet (x: toward the hand's side, z: forward; m), most wanted first.</summary>
        static readonly Vector2[] BesideSpots =
        {
            new Vector2(0.38f, 0.1f), new Vector2(0.38f, -0.12f), new Vector2(0.42f, 0.32f), new Vector2(-0.38f, 0.1f),
            new Vector2(-0.38f, -0.12f), new Vector2(0.15f, -0.4f), new Vector2(-0.15f, -0.4f), new Vector2(-0.42f, 0.32f),
        };
        static readonly RaycastHit[] s_downHits = new RaycastHit[12];
        static readonly Collider[] s_over = new Collider[12];
        readonly List<WorldItem> _passing = new List<WorldItem>();

        /// <summary>
        /// A spot on the floor beside the feet for the held item (a box of halfSize standing on it), clear of everything, at
        /// least 0.35 m off the way from the feet to toward, where fits(at, rotation) says it will do (its cord reaches);
        /// turned to face you. False if there is none.
        /// </summary>
        public bool SpotBeside(Vector3 toward, Vector3 halfSize, System.Func<Vector3, Quaternion, bool> fits, out Vector3 at, out Quaternion rot)
        {
            at = Vector3.zero;
            rot = Quaternion.Euler(0f, transform.eulerAngles.y + 180f, 0f);
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, right = Vector3.Cross(Vector3.up, fwd);
            Vector3 feet = transform.position, to = toward; to.y = feet.y;
            Vector3 path = to - feet;
            float best = float.MaxValue;
            for (int k = 0; k < BesideSpots.Length; k++)
            {
                Vector3 p = feet + right * (BesideSpots[k].x * Side) + fwd * BesideSpots[k].y;
                if (!FloorBeside(p, out Vector3 floor)) continue;
                Vector3 q = floor; q.y = feet.y;
                float t = path.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(q - feet, path) / path.sqrMagnitude) : 0f;
                if ((q - (feet + path * t)).magnitude < 0.35f) continue;
                if (fits != null && !fits(floor, rot)) continue;
                int n = Physics.OverlapBoxNonAlloc(floor + Vector3.up * (halfSize.y + 0.02f), halfSize, s_over, rot, ~0, QueryTriggerInteraction.Ignore);
                bool clear = true;
                for (int c = 0; c < n && clear; c++)
                {
                    var tr = s_over[c].transform;
                    clear = tr.IsChildOf(transform) || (HeldItem && tr.IsChildOf(HeldItem.transform));
                }
                if (!clear) continue;
                float score = k * 0.1f + Vector3.Distance(floor, toward) * 0.3f;
                if (score < best) { best = score; at = floor; }
            }
            return best < float.MaxValue;
        }

        /// <summary>The floor under p near the feet' height (not the player's own body, not the held item).</summary>
        bool FloorBeside(Vector3 p, out Vector3 floor)
        {
            floor = p;
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.7f, Vector3.down, s_downHits, 1.4f, ~0, QueryTriggerInteraction.Ignore);
            float bestD = float.MaxValue; bool ok = false;
            for (int i = 0; i < n; i++)
            {
                var tr = s_downHits[i].collider.transform;
                if (tr.IsChildOf(transform) || (HeldItem && tr.IsChildOf(HeldItem.transform)) || s_downHits[i].distance >= bestD) continue;
                bestD = s_downHits[i].distance; floor = s_downHits[i].point; ok = s_downHits[i].normal.y > 0.8f;
            }
            return ok && Mathf.Abs(floor.y - transform.position.y) < 0.25f;
        }

        /// <summary>Puts an item where the hand is now, as the hand would grip it (a plug let out of a lamp in the hand: it
        /// appeared at the lamp's back, low by the body, and the arm folded up to fetch it).</summary>
        public void PlaceInHand(WorldItem item)
        {
            if (!item) return;
            Quaternion grip = item.GripRotationLocal;
            Quaternion rot = HandRotation * Quaternion.Inverse(Quaternion.LookRotation(Mirror(grip * Vector3.forward), Mirror(grip * Vector3.up)));
            Vector3 pos = HandPosition - rot * Mirror(item.GripLocal);
            if (item.Body) { item.Body.position = pos; item.Body.rotation = rot; item.Body.linearVelocity = Vector3.zero; }
            item.transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Sets the held item down there, upright and still; the body passes through it until it has stepped away.</summary>
        public void SetDownBeside(WorldItem item, Vector3 at, Quaternion rot)
        {
            if (!item || HeldItem != item) return;
            HeldItem = null;
            AimedSocket = null;
            item.SetHeld(false);
            item.Body.position = at; item.Body.rotation = rot;
            item.transform.SetPositionAndRotation(at, rot);
            item.Body.linearVelocity = Vector3.zero; item.Body.angularVelocity = Vector3.zero;
            item.OnPlaced();
            var cc = GetComponent<CharacterController>();
            if (!cc) return;
            foreach (var c in item.GetComponentsInChildren<Collider>(true)) if (c && c.enabled) Physics.IgnoreCollision(cc, c, true);
            if (!_passing.Contains(item)) _passing.Add(item);
        }

        /// <summary>Items set down at the feet become solid to the body again once it has stepped away from them.</summary>
        void UpdatePassing()
        {
            if (_passing.Count == 0) return;
            var cc = GetComponent<CharacterController>();
            for (int i = _passing.Count - 1; i >= 0; i--)
            {
                var it = _passing[i];
                if (!it) { _passing.RemoveAt(i); continue; }
                if (it.Held) continue;
                Vector3 d = transform.position - it.transform.position; d.y = 0f;
                if (cc && d.magnitude < cc.radius + 0.45f) continue;
                // (Light things never stop the body: WorldItem keeps them out of its way.)
                if (cc && it.Body && it.Body.mass > 0.5f)
                    foreach (var c in it.GetComponentsInChildren<Collider>(true)) if (c && c.enabled) Physics.IgnoreCollision(cc, c, false);
                _passing.RemoveAt(i);
            }
        }

        /// <summary>The held item is gone (used up): the hand is free.</summary>
        public void ItemGone(WorldItem item)
        {
            if (HeldItem == item) HeldItem = null;
        }

        /// <summary>Lets go of the held item: it keeps the hand's motion plus a little toss forward (or is set down upright).</summary>
        public void Drop()
        {
            var item = HeldItem;
            if (!item) return;
            if (item.PlaceOnDrop && PlaceSpot(out var at, out var yaw))
            {
                HeldItem = null;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                item.SetHeld(false);
                // Through the body too: an interpolated rigidbody would put it back where the hand had it.
                item.Body.position = at; item.Body.rotation = rot;
                item.transform.SetPositionAndRotation(at, rot);
                item.Body.linearVelocity = Vector3.zero; item.Body.angularVelocity = Vector3.zero;
                item.OnPlaced();
                return;
            }
            HeldItem = null;
            AimedSocket = null;
            item.SetHeld(false);
            if (item.DropsStraight)
            {
                // Just let go: it falls from where the hand had it.
                item.Body.linearVelocity = Vector3.zero;
                item.Body.angularVelocity = Random.insideUnitSphere * 1.5f;
                return;
            }
            var cam = _cam ? _cam.transform : transform;
            item.Body.linearVelocity = _player.PlanarVelocity + cam.forward * 1.1f + Vector3.up * 0.4f;
            item.Body.angularVelocity = Random.insideUnitSphere * 3f;
        }

        /// <summary>
        /// The socket a click would plug into: the one the view is nearest (within <see cref="socketAssistAngle"/>, or a
        /// few centimetres off the line), within reach, that can be seen and that the held item can use.
        /// </summary>
        Socket FindSocket(WorldItem item)
        {
            var cam = _cam ? _cam.transform : transform;
            Vector3 eye = cam.position, fwd = cam.forward;
            Socket best = null; float bestScore = float.MaxValue;
            foreach (var s in Sockets.Around(eye))
            {
                if (s.occupant != null || s.dead) continue;
                Vector3 p = s.Position, to = p - eye;
                float dist = to.magnitude;
                if (dist > socketReach || dist < 0.05f) continue;
                if (Vector3.Dot(s.Normal, -to) <= 0f) continue;                     // from behind its wall
                float angle = Vector3.Angle(fwd, to);
                float off = Vector3.Cross(fwd, to).magnitude;
                if (angle > socketAssistAngle && off > 0.06f) continue;
                float score = angle + dist * 2f;
                if (score >= bestScore || !item.TakesSocket(s)) continue;
                // Nothing in the way (the socket's own plate is right at it).
                Vector3 target = p + s.Normal * 0.01f, d = target - eye;
                if (Physics.Raycast(eye, d.normalized, out var block, d.magnitude - 0.005f, ~0, QueryTriggerInteraction.Ignore)
                    && !block.collider.transform.IsChildOf(transform) && !block.collider.transform.IsChildOf(item.transform)) continue;
                bestScore = score; best = s;
            }
            return best;
        }

        /// <summary>Where Q sets an item down: a floor or table top within reach, the item turned to face you.</summary>
        bool PlaceSpot(out Vector3 at, out float yaw)
        {
            at = default; yaw = transform.eulerAngles.y + 180f;
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, 2.4f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool found = false;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c.transform.IsChildOf(transform) || (HeldItem && c.transform.IsChildOf(HeldItem.transform)) || _hits[i].distance >= best) continue;
                best = _hits[i].distance;
                found = _hits[i].normal.y > 0.75f;
                at = _hits[i].point;
            }
            if (!found)
            {
                // Looking at a wall: put it on the floor in front of it.
                Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                var from = transform.position + Vector3.up * 0.8f + fwd * Mathf.Min(best < float.MaxValue ? best - 0.25f : 0.7f, 0.7f);
                if (!Physics.Raycast(from, Vector3.down, out var floor, 1.5f, ~0, QueryTriggerInteraction.Ignore) || floor.normal.y < 0.75f) return false;
                at = floor.point;
            }
            return true;
        }

        bool _wasUse, _rmbWas;
        /// <summary>Automated tests: the right mouse button held.</summary>
        [System.NonSerialized] public bool botRmb;
        float _qDown = -1f, _rmbDown = -1f;
        const float RightTap = 0.25f;
        /// <summary>0..1 while Q is held to throw (wind-up), 0 otherwise.</summary>
        public float ThrowCharge { get; private set; }
        [Header("Throwing")]
        [Tooltip("Hold Q longer than this to throw instead of dropping (s).")] public float throwHold = 0.2f;
        [Tooltip("Seconds of holding Q for the hardest throw.")] public float throwFullCharge = 0.9f;
        [Tooltip("Throw speed (m/s) from a short to a full wind-up.")] public Vector2 throwSpeed = new Vector2(3.5f, 13f);

        /// <summary>
        /// A held item's weight in the hand: something heavy (the lamp, a coiled cord) slows your steps and your turns a
        /// little. Furniture and doors set their own.
        /// </summary>
        void UpdateCarriedWeight()
        {
            if (HeldItem)
            {
                float m = HeldItem.Body ? HeldItem.Body.mass : 0f;
                _player.moveScale = Mathf.Lerp(1f, 0.82f, Mathf.Clamp01(m / 2f));
                _player.lookScale = Mathf.Lerp(1f, 0.86f, Mathf.Clamp01(m / 2f));
                _weighted = true;
            }
            else if (_weighted)
            {
                _weighted = false;
                if (Current == State.Idle && !OnDoor) { _player.moveScale = 1f; _player.lookScale = 1f; }
            }
        }

        /// <summary>Throws the held item along the view, harder the longer Q was held.</summary>
        void Throw(float charge)
        {
            MicThump();
            var item = HeldItem;
            if (!item) return;
            HeldItem = null;
            item.SetHeld(false);
            var cam = _cam ? _cam.transform : transform;
            float speed = Mathf.Lerp(throwSpeed.x, throwSpeed.y, charge);
            item.Body.linearVelocity = _player.PlanarVelocity + (cam.forward + Vector3.up * 0.12f).normalized * speed;
            item.Body.angularVelocity = Random.insideUnitSphere * (4f + 8f * charge);
            AudioSource.PlayClipAtPoint(Audio.SoundBank.CordWhip, item.transform.position, 0.15f + 0.25f * charge);
        }

        void UpdateHeldItem(bool focus, Mouse mouse)
        {
            var kb = Keyboard.current;
            // Q: a tap drops (or sets down), held it winds up a throw that goes on release.
            if (focus && kb != null && kb.qKey.wasPressedThisFrame) _qDown = Time.time;
            if (_qDown >= 0f)
            {
                float held = Time.time - _qDown;
                ThrowCharge = held > throwHold ? Mathf.Clamp01((held - throwHold) / Mathf.Max(0.05f, throwFullCharge)) : 0f;
                if (kb == null || !kb.qKey.isPressed)
                {
                    float charge = ThrowCharge;
                    _qDown = -1f; ThrowCharge = 0f; _wasUse = false;
                    if (held <= throwHold) Drop(); else Throw(Mathf.Max(0.05f, charge));
                    return;
                }
            }
            bool use = focus && mouse != null && mouse.leftButton.isPressed && _itemT >= 1f;
            if (botHold.HasValue) use = botHold.Value;
            var cam = _cam ? _cam.transform : transform;
            // Right mouse: a tap (released within RightTap) is the item's quick action; held longer, it reels a cord in.
            bool rmb = botRmb || (focus && mouse != null && mouse.rightButton.isPressed);
            if (rmb && _rmbDown < 0f) _rmbDown = Time.time;
            bool tap = !rmb && _rmbDown >= 0f && Time.time - _rmbDown <= RightTap;
            bool reel = rmb && _rmbDown >= 0f && Time.time - _rmbDown > RightTap;
            bool rmbPressed = rmb && !_rmbWas;
            _rmbWas = rmb;
            if (!rmb) _rmbDown = -1f;
            AimedSocket = FindSocket(HeldItem);
            var input = new HoldInput { use = use, pressed = use && !_wasUse, released = !use && _wasUse, eye = cam.position, aimDir = cam.forward, dt = Time.deltaTime, hand = this,
                                        secondary = tap, reel = reel, socket = AimedSocket, aim = rmb, aimPressed = rmbPressed };
            _wasUse = use;
            input.hit = AimHit(out input.hitInfo);
            input.aimAt = input.hit ? input.hitInfo.point : cam.position + cam.forward * 2f;
            HeldItem.HoldUpdate(input);
            if (!HeldItem) return;                // used up
            if (_shake) { _shake.dip = 0.12f + 0.1f * HeldItem.UseBlend; _shake.effort = HeldItem.Effort; }
        }

        // ---------------- Held as the arm holds it best ----------------
        Quaternion _easeLocal = Quaternion.identity, _easeWant = Quaternion.identity;
        float _easeNext;
        static readonly float[] EaseAngles = { -24f, -12f, 0f, 12f, 24f };

        /// <summary>
        /// The held item turned about the palm (at most 24 degrees about the back of the hand and about the fingers) to where
        /// the arm holds it with the wrist and forearm well inside their limits (FirstPersonBody.ArmStrain), looked for a few
        /// times a second and eased to. (A spray can and a rubber chicken were held with the wrist 25-26 degrees to the side,
        /// its limit 28, and the chicken's forearm turned 95, its limit.) Not for the sledgehammer (it plans its own poses)
        /// or while an item is in use (a plug going in, chalk on a wall).
        /// </summary>
        void EaseForArm(WorldItem item, ref Vector3 pos, ref Quaternion rot, float useW, float dt)
        {
            bool on = _body && _body.HasArm && !(item is Sledgehammer) && useW <= 0f && ThrowCharge <= 0f && _itemT >= 1f && HandBlend > 0.9f;
            Quaternion grip = item.GripRotationLocal;
            Quaternion hand = rot * Quaternion.LookRotation(Mirror(grip * Vector3.forward), Mirror(grip * Vector3.up));
            Vector3 palm = pos + rot * Mirror(item.GripLocal);
            if (on && Time.time >= _easeNext)
            {
                _easeNext = Time.time + 0.15f;
                float best = float.MaxValue;
                foreach (float a in EaseAngles)
                    foreach (float b in EaseAngles)
                    {
                        Quaternion local = Quaternion.AngleAxis(a, Vector3.up) * Quaternion.AngleAxis(b, Vector3.forward);
                        float s = _body.ArmStrain(palm, hand * local, out _) + (Mathf.Abs(a) + Mathf.Abs(b)) * 0.003f;
                        if (s < best - 1e-4f) { best = s; _easeWant = local; }
                    }
            }
            if (!on) _easeWant = Quaternion.identity;
            _easeLocal = Quaternion.Slerp(_easeLocal, _easeWant, 1f - Mathf.Exp(-dt / 0.3f));
            Quaternion turn = hand * _easeLocal * Quaternion.Inverse(hand);
            rot = turn * rot;
            pos = palm + turn * (pos - palm);
        }

        /// <summary>The nearest thing the centre of the view is on, within 6 m (not the player or the held item).</summary>
        bool AimHit(out RaycastHit hit)
        {
            hit = default;
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool any = false;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c.transform.IsChildOf(transform) || (HeldItem && c.transform.IsChildOf(HeldItem.transform)) || _hits[i].distance >= best) continue;
                best = _hits[i].distance; hit = _hits[i]; any = true;
            }
            return any;
        }

        /// <summary>What the centre of the view is on (within 6 m), or a point 2 m ahead.</summary>
        Vector3 AimPoint()
        {
            var cam = _cam ? _cam.transform : transform;
            int n = Physics.RaycastNonAlloc(cam.position, cam.forward, _hits, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 at = cam.position + cam.forward * 2f;
            for (int i = 0; i < n; i++)
            {
                var c = _hits[i].collider;
                if (c.transform.IsChildOf(transform) || (HeldItem && c.transform.IsChildOf(HeldItem.transform)) || _hits[i].distance >= best) continue;
                best = _hits[i].distance; at = _hits[i].point;
            }
            return at;
        }

        Vector3 Mirror(Vector3 v) => Side > 0f ? v : new Vector3(-v.x, v.y, v.z);

        /// <summary>After the camera has moved: the held item follows the view, and the hand grips it.</summary>
        void LateUpdate()
        {
            if (!HeldItem) return;
            var item = HeldItem;
            Transform pivot = _player.cameraPitchPivot ? _player.cameraPitchPivot : (_cam ? _cam.transform : transform);
            // The item is in the hand, not glued to the lens: it trails the view's turns and the body's moves a little and
            // bobs with the steps, and the hand (which grips it) goes with it.
            float dt = Time.deltaTime;
            // Half as far out as the camcorder is held (CamcorderPlayerController.CamcorderHold, a little in front of the
            // eye): a held thing sits in the picture nearly as it did with the camera at the eye, and the arm stays bent
            // (all the way out, it was nearly straight).
            Vector3 anchor = pivot.position + pivot.rotation * (_player.CamcorderHold * 0.5f);
            if (!_swayInit) { _swayRot = pivot.rotation; _swayPos = anchor; _swayVel = Vector3.zero; _swayInit = true; }
            // Heavier things lag the view's turns more.
            float lag = 0.075f * (1f + Mathf.Clamp(item.Body ? item.Body.mass : 0.3f, 0f, 2f) * 0.6f);
            _swayRot = Quaternion.Slerp(_swayRot, pivot.rotation, 1f - Mathf.Exp(-dt / lag));
            if (Quaternion.Angle(_swayRot, pivot.rotation) > 25f) _swayRot = Quaternion.RotateTowards(pivot.rotation, _swayRot, 25f);
            // The load swings on the arm as on a pendulum (an under-damped spring): it trails a start, swings past a stop
            // and settles; heavier things swing slower and further.
            float mass = Mathf.Clamp(item.Body ? item.Body.mass : 0.3f, 0.05f, 3f);
            _swayPos = Springs.Oscillator(_swayPos, ref _swayVel, anchor, Mathf.Lerp(4.2f, 2.6f, mass / 3f), Mathf.Lerp(0.55f, 0.38f, mass / 3f), dt);
            if ((_swayPos - anchor).sqrMagnitude > 0.07f * 0.07f)
            {
                _swayPos = anchor + Vector3.ClampMagnitude(_swayPos - anchor, 0.07f);
                _swayVel *= 0.5f;
            }
            float speed = _player.PlanarVelocity.magnitude;
            _bobPhase += dt * speed * 5.2f;
            Vector3 bob = new Vector3(Mathf.Sin(_bobPhase) * 0.006f, Mathf.Abs(Mathf.Cos(_bobPhase)) * 0.01f - 0.005f, 0f) * Mathf.Clamp01(speed / 1.5f);
            Quaternion view = _swayRot;
            Vector3 offset = Mirror(Vector3.Lerp(item.HoldOffset, item.UseOffset, item.UseBlend)) + bob;
            // Winding up a throw: back and up over the shoulder.
            offset += new Vector3(0.04f * Side, 0.1f, -0.16f) * ThrowCharge;
            Vector3 pos = _swayPos + view * offset;
            // Reeling a cord in: the hand pulls back along the cord 30 cm (the cord comes in as far as the hand pulls it:
            // ReelPulled), then goes back out for the next pull, 1.7 a second, rolling at the wrist and forearm as it winds
            // the cord on. It keeps hold of what it holds (round 7: the cord came in 1.2 m a stroke for a 25 cm pull, and
            // the fingers opened to a curl of 0.37 on every stroke with the coil in that hand alone).
            bool reeling = Time.time < _reelUntil;
            _reelW = Mathf.MoveTowards(_reelW, reeling ? 1f : 0f, dt * 5f);
            if (reeling) _reelPhase += dt * ReelRate;
            float reelRoll, reelFlex;
            {
                float ph = _reelPhase - Mathf.Floor(_reelPhase);
                float outAmt = ph < 0.6f ? 1f - ph / 0.6f : (ph - 0.6f) / 0.4f;
                pos += _reelDir * ((outAmt - 0.5f) * ReelStrokeLength * _reelW);
                if (reeling && ph < 0.6f && outAmt < _reelOut) _reelPulled += (_reelOut - outAmt) * ReelStrokeLength * _reelW;
                _reelOut = outAmt;
                if (!reeling) _reelPulled = 0f;
                GripOpen = 0f;
                reelRoll = Mathf.Sin(2f * Mathf.PI * ph) * 8f * _reelW * Side;
                // The hand tips with the stroke as the forearm swings (held level, the wrist bent back 50 near the body and
                // forward 40 out along the cord).
                reelFlex = (0.5f - outAmt) * 22f * _reelW;     // (round 9: 60, the wrist flapped -37..+31 every stroke)
            }
            // Upright in the view, turned a little in toward the middle, tipped forward while in use.
            Quaternion rot = view * Quaternion.Euler(-8f * item.UseBlend - 25f * ThrowCharge, -10f * Side, 4f * Side) * item.HoldTurn;
            // (About the fingers' line, near the forearm's: rolled about the view's line, the wrist flapped 60 degrees.)
            if (_reelW > 0.001f)
            {
                rot = Quaternion.AngleAxis(reelFlex, rot * (item.GripRotationLocal * Vector3.right)) * rot;
                rot = Quaternion.AngleAxis(reelRoll, rot * (item.GripRotationLocal * Vector3.forward)) * rot;
            }
            float w = item.UsePose(out var usePos, out var useRot);
            EaseForArm(item, ref pos, ref rot, w, dt);
            if (w > 0f) { pos = Vector3.Lerp(pos, usePos, w); rot = Quaternion.Slerp(rot, useRot, w); }
            bool reaching = false;
            float tiltLeft = 0f;
            Vector3 reachGrip = Vector3.zero;
            if (_itemT < 1f)
            {
                // It stays where it lay until the palm is on it (the body bending and stepping in for it), the fingers close
                // on it, then it comes up with the hand. Never without the hand: it flew a metre into the hand from a
                // reeled-in coil, then after waiting 0.9 s for a hand that had not got there (round 8). If the hand cannot
                // get there it is left where it lay.
                if (_itemT <= 0f)
                {
                    Vector3 gripAt = _itemFromPos + _itemFromRot * Mirror(item.GripLocal);
                    if (!_graspOn)
                    {
                        int r = ReachFor(gripAt, item, dt);
                        if (r < 0) { LeaveWhereItLies(item); return; }
                        if (r > 0) { _graspOn = true; _graspT = 0f; }
                        else { reaching = true; reachGrip = gripAt; }
                    }
                    else
                    {
                        // The palm on it: the body stays bent over it while the fingers close (UpdateStoop, ReachHinge).
                        _graspT += dt;
                        if ((HandCurl >= item.GripCurl - 0.05f || _graspT >= 0.25f) && HandBlend > 0.9f)
                        {
                            // Up with the hand at a hand's pace (it rose 1.1 m in 0.28 s, 22 cm in one frame).
                            // (0.4-0.95 s was slow next to a hand: round 9, 0.45-0.75.)
                            _liftTime = Mathf.Clamp(0.3f + 0.4f * Vector3.Distance(_itemFromPos, pos), 0.45f, 0.75f);
                            _itemT = 1e-4f;
                        }
                    }
                }
                else { _itemT = Mathf.Min(1f, _itemT + dt / _liftTime); if (_itemT >= 1f) LiftDone(); }
                float s = Mathf.SmoothStep(0f, 1f, _itemT);
                pos = Vector3.Lerp(_itemFromPos, pos, s);
                rot = Quaternion.Slerp(_itemFromRot, rot, s);
                // The hand's turn about the palm reaching for it (fingers down along a can on the floor) goes back off as it
                // comes up: about the palm's normal, so the palm stays on its grip. The grip it took is held until the lift
                // is half done, then eased to the carry's (taken back off from the start, the can slid 2-3.5 cm in the palm).
                tiltLeft = 1f - Ease(0.35f, 1f, _itemT);
            }
            else _pickTurn = Quaternion.identity;
            item.transform.SetPositionAndRotation(pos, rot);

            Quaternion grip = item.GripRotationLocal;
            Vector3 f = Mirror(grip * Vector3.forward), u = Mirror(grip * Vector3.up);
            HandPosition = reaching ? _pickHandAt : item.transform.TransformPoint(Mirror(item.GripLocal));
            HandRotation = item.transform.rotation * Quaternion.LookRotation(f, u);
            if (reaching) HandRotation = ReachTurn(HandRotation, reachGrip);
            else if (tiltLeft > 0.001f) HandRotation = WithReachTurn(HandRotation, tiltLeft);
            if (_switchT < 1f)
            {
                _switchT = Mathf.Min(1f, _switchT + Time.deltaTime / 0.3f);
                float e = Mathf.SmoothStep(0f, 1f, _switchT);
                HandPosition = Vector3.Lerp(_switchPos, HandPosition, e);
                HandRotation = Quaternion.Slerp(_switchRot, HandRotation, e);
            }
        }
    }
}
