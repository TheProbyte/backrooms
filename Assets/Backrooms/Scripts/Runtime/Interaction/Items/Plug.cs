using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The plug at the end of a cord (a lamp's, or an extension cord's). Pick it up and click a wall outlet or the box of
    /// an extension cord within reach to push it in; pick it up again (or pull the cord taut) to take it out. While it is
    /// in a socket it stays there, held by the socket, and the thing at the other end of its cord has power if the socket
    /// does. Model: Resources/Items/Plug.fbx (origin at the face the prongs leave, prongs forward).
    /// </summary>
    public class Plug : WorldItem, IHandGrip, IHandPose
    {
        /// <summary>Where the cord leaves the back of the plug (model frame).</summary>
        public static readonly Vector3 CordPoint = new Vector3(0f, 0f, -0.046f);
        [Tooltip("Furthest socket you can push it into from your eye (m).")] public float reach = 2.2f;

        /// <summary>What the cord powers: a lamp, or an extension cord's box.</summary>
        public WorldItem owner;
        public Socket Socket { get; private set; }
        bool _lost;                  // in a wall socket whose chunk is unloaded: waiting to find it again
        Vector3 _lostAt;
        float _retry;
        AudioSource _fx;

        public bool Plugged => Socket != null || _lost;
        /// <summary>The wall socket it is in (for saving), or the place it waits for one.</summary>
        public Vector3 WallPosition => _lost ? _lostAt : Socket != null && Socket.IsWall ? Socket.local : Vector3.zero;
        protected override bool Pinned => Plugged;

        public override Vector3 HoldOffset => new Vector3(0.14f, -0.18f, 0.33f);
        // A pinch: the plug's body under the pads of the index and middle fingers, the thumb opposed under it, ring and
        // pinky tucked; the palm clear behind and above it, the prongs forward past the fingertips. (It used to sit inside
        // the fist with the prongs through the fingers.)
        // (The body under the index and middle pads: the palm's centre a little to the little-finger side, above and behind
        // the plug; the fingers along the forearm's line, curling down onto its top.)
        // (Placed by trying the closing fingers against the body: 5.5 cm below the palm's centre, 3 cm ahead of it, index,
        // middle and thumb pads all touching; 1.5 cm higher and further forward the thumb missed it by 15 mm.)
        // Round 7: a pad pinch. The body between the thumb's pad and the index and middle pads, those two bent 48 and 52
        // degrees at the middle joint, the thumb's end joint 25, ring and little curled out of the way; the prongs out past
        // the fingertips. (Fitted to the posed fingers' skin: 0-1 mm into its box. It was a tip pinch with straight fingers,
        // the thumb straight: a dangling claw.) Held in the view with the hand turned as the old grip held it.
        // Going into a socket the hand turns to the push grip it had (fingers along the prongs, palm above): pinched, the
        // fingers 46 degrees off the prongs, the hand could not square the plug to the socket and it never went in.
        public override Vector3 GripLocal => TestGrip ?? Vector3.Lerp(PinchGrip, PushGrip, PushBlend);
        public override Quaternion GripRotationLocal => TestGrip.HasValue ? Quaternion.Euler(TestTurn) : Quaternion.Slerp(PinchTurn, PushTurn, PushBlend);
        // (Turned over 0.15 s at the click: turned as it went, over 0.45 s, the palm was up to 39 mm off it.)
        float PushBlend => Mathf.SmoothStep(0f, 1f, _regrip);
        float _regrip;
        static readonly Vector3 PinchGrip = new Vector3(-0.02908f, 0.06118f, -0.02733f), PushGrip = new Vector3(0.014f, 0.055f, -0.0525f);
        static readonly Quaternion PushTurn = Quaternion.LookRotation(new Vector3(0f, 0.05f, 1f), new Vector3(0f, 1f, -0.05f));
        static readonly Quaternion PinchTurn = Quaternion.LookRotation(new Vector3(0.6886f, 0.0238f, 0.7247f), new Vector3(-0.3841f, 0.8597f, 0.3366f));
        static readonly Quaternion HoldNew = Quaternion.LookRotation(new Vector3(0f, 0.05f, 1f), new Vector3(0f, 1f, -0.05f)) * Quaternion.Inverse(PinchTurn);
        public override Quaternion HoldTurn => TestHold.HasValue ? Quaternion.Euler(TestHold.Value) : HoldNew;
        /// <summary>Tests: how it is turned in the view (degrees) instead.</summary>
        public static Vector3? TestHold;
        /// <summary>Tests: a grip to try instead (the palm's point; the hand's axes in the plug's frame as Euler angles), joint bends (as IHandPose; null: as usual).</summary>
        public static Vector3? TestGrip;
        public static Vector3 TestTurn;
        public static float[][] TestPose;
        static readonly float[][] Pinch =
        {
            new[] { 15f, 48f, 15f, 0f }, new[] { 20f, 52f, 18f, 0f }, new[] { 50f, 60f, 35f, 0f }, new[] { 55f, 60f, 35f, 0f },
            new[] { 5f, 15f, 25f, 40f },
        };
        public bool FingerPose(int finger, float[] angles)
        {
            var src = TestPose ?? Pinch;
            if (src == null || src[finger] == null || PushBlend > 0.4f) return false;
            for (int i = 0; i < 4; i++) angles[i] = src[finger][i];
            return true;
        }
        public override float GripCurl => 0.45f;
        public float FingerCurl(int finger) => finger == 0 ? 0.45f : finger == 1 ? 0.55f : finger == 2 ? 0.85f : 0.9f;
        public float ThumbCurl => 0.45f;
        public float ThumbOpposition => 0.55f;
        public override string Status => Plugged ? "plug: in" : "plug: click an outlet";

        public static Plug Create(string itemId, WorldItem owner, Vector3 position, Quaternion rotation, Color body)
        {
            var go = ItemModels.Body(itemId, position, rotation, 0.06f, "Plug", new Vector3(0.035f, 0.03f, 0.045f), "PlugBody", false, new Vector3(0f, 0f, -0.0225f));
            // The body in the cord's colour.
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].name == "Item_PlugBody") mats[i] = ItemModels.Mat("PlugBody_" + ColorUtility.ToHtmlStringRGB(body), body);
                r.sharedMaterials = mats;
            }
            var p = go.AddComponent<Plug>();
            p.itemId = itemId; p.displayName = "Plug"; p.owner = owner;
            Generation.PoolLitSwitch.Attach(go);
            return p;
        }

        protected override void Awake()
        {
            base.Awake();
            _fx = ItemModels.Source(gameObject, 8f);
        }

        /// <summary>Could this plug go into that socket? (Not its own box, not a loop, not too many cords in a row.)</summary>
        public bool CanUse(Socket s)
        {
            if (s == null || s.occupant != null || s.dead) return false;
            if (owner is CordBox box)
            {
                if (s.box == box || s.Depth + 1 > CordBox.MaxChain) return false;
                for (var b = s.box; b; b = b.Plug && b.Plug.Socket != null ? b.Plug.Socket.box : null) if (b == box) return false;
            }
            return true;
        }

        public bool PlugInto(Socket s)
        {
            if (!CanUse(s)) return false;
            if (Socket != null) Socket.occupant = null;
            _lost = false;
            Socket = s; s.occupant = this;
            Body.isKinematic = true;
            Pose();
            _fx.PlayOneShot(SoundBank.PlugIn, 0.7f * SoundCatalog.Volume(SoundIds.Cords));
            Wake();
            return true;
        }

        /// <summary>Out of its socket (popped: yanked by its cord).</summary>
        public void Unplug(bool popped)
        {
            if (Socket != null) Socket.occupant = null;
            bool was = Plugged;
            Socket = null; _lost = false;
            if (!Held) Body.isKinematic = false;
            if (was) _fx.PlayOneShot(SoundBank.PlugOut, (popped ? 0.8f : 0.5f) * SoundCatalog.Volume(SoundIds.Cords));
            if (popped && !Held) Body.linearVelocity = Random.insideUnitSphere * 0.6f + Vector3.up * 0.8f;
            Wake();
        }

        /// <summary>Its wall socket's chunk went away: keep the place, find it again when it comes back.</summary>
        public void SocketLost()
        {
            if (Socket == null || !Socket.IsWall) return;
            _lostAt = Socket.local; _lost = true; Socket = null;
        }

        /// <summary>Restore from a save: in the wall socket at this position (found when its chunk loads).</summary>
        public void WaitForWall(Vector3 at) { _lostAt = at; _lost = true; Body.isKinematic = true; }

        void Wake()
        {
            var cord = owner ? owner.GetComponent<PowerUser>() : null;
            if (cord) cord.WakeCord();
        }

        void Pose()
        {
            if (Socket == null) return;
            if (_seatT < 1f)
            {
                // The last few millimetres from where the hand left it, eased in over a few frames (it jumped 8-9 mm in
                // one: the wrist cannot square a plug exactly).
                _seatT = Mathf.Min(1f, _seatT + Time.deltaTime / _seatTime);
                float e = Mathf.SmoothStep(0f, 1f, _seatT);
                transform.SetPositionAndRotation(Vector3.Lerp(_seatFrom, Socket.Position, e), Quaternion.Slerp(_seatFromRot, Socket.PlugRotation, e));
                return;
            }
            transform.SetPositionAndRotation(Socket.Position, Socket.PlugRotation);
        }
        float _seatT = 1f;
        Vector3 _seatFrom;
        Quaternion _seatFromRot;
        /// <summary>How long a plug let go short of its socket takes to slide the rest of the way in (s); up to 0.2 s from
        /// SeatFrom away.</summary>
        public const float SeatTime = 0.1f;
        /// <summary>With no body: the glide from the held position into the socket (s).</summary>
        public const float QuickSeatTime = 0.14f;
        /// <summary>Round 11: a plug let go this near its socket (m) slides in the rest of the way (it had to be within 3 cm).</summary>
        public const float SeatFrom = 0.065f;
        float _seatTime = SeatTime;

        void Update()
        {
            if (Socket != null && Socket.dead) SocketLost();
            if (_lost && Time.time >= _retry)
            {
                _retry = Time.time + 0.5f;
                var s = Sockets.WallAt(_lostAt);
                if (s != null && s.occupant == null) { Socket = s; s.occupant = this; _lost = false; Pose(); }
            }
        }

        void LateUpdate() { if (Socket != null && !Held) Pose(); }

        protected override void OnPickedUp() { if (Plugged) Unplug(false); }
        protected override void OnDropped() { _going = null; _pushT = -1f; _goPose = 0f; _regrip = 0f; if (owner is PowerUser user) user.StopReeling(); }

        /// <summary>Put down, a plug just falls from the hand.</summary>
        public override bool DropsStraight => true;

        /// <summary>Within reach of the eye and of its cord (from the thing at the other end).</summary>
        public override bool TakesSocket(Socket s)
        {
            if (!CanUse(s)) return false;
            var user = owner as PowerUser;
            return !user || Vector3.Distance(s.Position, user.transform.position) < user.CordReach * 0.95f;
        }

        public override void HoldUpdate(in HoldInput i)
        {
            // Right mouse held: reel the cord in toward this end (the thing at the other end is dragged in).
            if (owner is PowerUser user) { user.ReelFromPlug(this, i); if (!Held) return; }
            _regrip = Mathf.MoveTowards(_regrip, _going != null ? 1f : 0f, i.dt / 0.15f);
            if (_going != null) { GoingIn(i); return; }
            _goPose = Mathf.MoveTowards(_goPose, 0f, i.dt * 3f);
            if (!i.pressed) return;
            var s = i.socket;
            if (s == null && i.hit && i.hitInfo.distance <= reach) s = Sockets.Nearest(i.hitInfo.point, 0.1f, owner as CordBox);
            if (s == null || !CanUse(s)) return;
            BeginPlugIn(s);
        }

        // ---------------- Into a socket ----------------
        // The hand carries the plug to the socket, square to it 5 cm out, pushes it in along the socket's axis and lets go
        // (bending and stepping in for a low or far socket). It used to jump from the hand into the socket, up to 1.1 m, in
        // one frame.
        // The hand's target is kept within a comfortable reach of the shoulder as it is now, on the way to the socket: it
        // moves on as the body bends and steps in, so the arm stays bent and the palm keeps up with it. (Sent straight to a
        // socket out of reach, the arm locked straight for a second with the palm up to 47 cm behind its target, and pumped
        // at the end.) No headway for a second (something in the way): it gives up and the hand comes back.
        Socket _going;
        float _goT, _goPose, _pushT = -1f, _stuckT, _slowT, _stretch, _bestGap, _settleT;
        int _tries;
        Vector3 _goPos; Quaternion _goRot = Quaternion.identity;
        const float Approach = 0.05f, PushTime = 0.16f;
        /// <summary>The hand's target is kept this far from the shoulder (of the arm's reach): bent, not locked straight.</summary>
        public const float ComfortReach = 0.86f;
        /// <summary>Seconds without getting nearer before it gives up (the hand comes back, the plug stays in it).</summary>
        public const float GiveUpAfter = 1f;
        /// <summary>Is it on its way into a socket (for tests)?</summary>
        public bool GoingInto => _going != null;
        public override bool Busy => GoingInto;
        /// <summary>The last plug-in gave up (for tests).</summary>
        public bool GaveUp { get; private set; }

        /// <summary>Plug in to this socket, the hand taking it there (it must be in the hand).</summary>
        public void BeginPlugIn(Socket s) { _going = s; _goT = 0f; _pushT = -1f; _stuckT = 0f; _slowT = 0f; _stretch = 0f; _bestGap = float.MaxValue; _tries = 0; _settleT = 0f; GaveUp = false; }

        void GiveUp() { _going = null; _pushT = -1f; GaveUp = true; }

        /// <summary>
        /// Where the plug may go this frame on its way to goal: goal itself if the palm can be there with the arm bent
        /// (<see cref="ComfortReach"/> of the reach from the shoulder), or else as far along the way from the shoulder to it as
        /// that allows. Pure, for tests.
        /// </summary>
        public static Vector3 WithinComfort(Vector3 goal, Vector3 palmOffset, Vector3 shoulder, float reach, out bool clamped) =>
            WithinComfort(goal, palmOffset, shoulder, reach, ComfortReach, out clamped);

        /// <summary>As above, within this much of the reach.</summary>
        public static Vector3 WithinComfort(Vector3 goal, Vector3 palmOffset, Vector3 shoulder, float reach, float within, out bool clamped)
        {
            Vector3 palm = goal + palmOffset, to = palm - shoulder;
            float r = reach * within;
            clamped = to.sqrMagnitude > r * r;
            if (!clamped) return goal;
            return shoulder + to.normalized * r - palmOffset;
        }

        void GoingIn(in HoldInput i)
        {
            var s = _going;
            if (s == null || !CanUse(s) || s.occupant != null) { GiveUp(); return; }
            // Not until the hand has it (the fingers closed on it where it was): bending for the socket with the plug still
            // waiting for the hand folded the arm up, the palm 30-35 mm off it (round 8).
            if (i.hand && i.hand.ItemWaitingForHand) return;
            // No body (Body: None, the default): no arm to carry it there. It goes straight in: a short glide from where it is
            // held into the socket (owner, 2026-10-31: "now that there's no hands it should be immediate maybe or in between").
            if (i.hand && i.hand.Bodiless)
            {
                _going = null;
                Vector3 from = transform.position; Quaternion fromRot = transform.rotation;
                i.hand.LetGo(this);
                PlugInto(s);
                _seatFrom = from; _seatFromRot = fromRot; _seatT = 0f;
                _seatTime = QuickSeatTime;
                transform.SetPositionAndRotation(from, fromRot);
                return;
            }
            _goT += i.dt;
            // (Round 11: square to the socket in about a third of a second; 1.4 a second took 0.7 s before anything else.)
            _goPose = Mathf.MoveTowards(_goPose, 1f, i.dt * 3f);
            Vector3 outward = -(s.PlugRotation * Vector3.forward);
            float d = _pushT < 0f ? Approach : Approach * (1f - Mathf.SmoothStep(0f, 1f, _pushT / PushTime));
            Vector3 goal = s.Position + outward * d;
            _goRot = s.PlugRotation;
            Vector3 palmOff = _goRot * GripLocal;
            Vector3 shoulder = i.hand.ReachShoulder;
            float reach = i.hand.ArmReach;
            // Bent while the body can still come closer; once it cannot (a low socket, the body bent as far as it goes, or
            // against the wall), the arm reaches on out as far as it goes (a socket 0.3 m up is reached nearly straight; the
            // collarbone carries the shoulder on a little past the reach measured from where it rests).
            float within = Mathf.Lerp(ComfortReach, 1.2f, _stretch);
            _goPos = WithinComfort(goal, palmOff, shoulder, reach, within, out bool clamped);
            // The body steps in and bends until the socket itself (where the push ends) is within the comfortable reach.
            i.hand.ReachToward(s.Position + palmOff, _pushT < 0f, ComfortReach - 0.06f);
            if (_pushT < 0f)
            {
                // Square in front of it, the hand there and the socket within reach: push. No headway for a second (out of
                // reach, held back): give up, the hand comes back.
                float gap = (transform.position - goal).magnitude;
                WithinComfort(s.Position, palmOff, shoulder, reach, within, out bool socketFar);
                bool there = _goPose >= 1f && !clamped && !socketFar && gap < 0.015f && i.hand.PalmError < 0.02f;
                // (Round 11: after half a second, within 3.5 cm will do: the push and the seat take the rest.)
                bool nearly = _goT > 0.5f && _goPose >= 1f && !clamped && !socketFar && gap < 0.035f && i.hand.PalmError < 0.04f;
                // (And held off a few centimetres short, the arm as far as it goes: a socket 0.28 m up from 0.33 m off the
                // wall stopped the plug 55 mm short; it waited two seconds, then gave up. The push takes it, and it slides
                // the rest of the way in: SeatFrom.)
                bool heldShort = _goT > 0.7f && _goPose >= 1f && _slowT > 0.25f && (transform.position - s.Position).magnitude < SeatFrom + Approach;
                if (there || nearly || heldShort) { _pushT = 0f; _settleT = 0f; return; }
                if (gap < _bestGap - 0.01f) { _bestGap = gap; _stuckT = 0f; _slowT = 0f; }
                else if (_goPose >= 1f) { _stuckT += i.dt; _slowT += i.dt; }
                if (_slowT > 0.2f) _stretch = Mathf.MoveTowards(_stretch, 1f, i.dt * 1.5f);
                if (_stuckT > GiveUpAfter || _goT > 5f) GiveUp();
                return;
            }
            // Pushing in, once, smoothly; then a moment for the hand to catch up before it lets go (it snapped the last 3 cm).
            _pushT += i.dt;
            if (_pushT < PushTime) return;
            float off = (transform.position - s.Position).magnitude;
            _settleT += i.dt;
            if (off > 0.004f && _settleT < 0.08f) return;
            if (off < SeatFrom)
            {
                _going = null;
                Vector3 from = transform.position; Quaternion fromRot = transform.rotation;
                i.hand.LetGo(this);
                PlugInto(s);
                _seatFrom = from; _seatFromRot = fromRot; _seatT = 0f;
                _seatTime = Mathf.Lerp(SeatTime, 0.2f, Mathf.InverseLerp(0.01f, SeatFrom, off));
                transform.SetPositionAndRotation(from, fromRot);
            }
            else if (++_tries >= 2) GiveUp();
            else { _pushT = -1f; _bestGap = float.MaxValue; _stuckT = 0f; _slowT = 0f; }
        }

        public override float UsePose(out Vector3 position, out Quaternion rotation)
        {
            position = _goPos; rotation = _goRot;
            return _goPose > 0f ? Mathf.SmoothStep(0f, 1f, _goPose) : 0f;
        }

        void OnDestroy()
        {
            if (Socket != null && Socket.occupant == this) Socket.occupant = null;
        }
    }

    /// <summary>Something with a cord and a plug (a lamp, an extension cord): its plug and cord report through this.</summary>
    public abstract class PowerUser : WorldItem
    {
        public abstract Plug Plug { get; }
        public abstract void WakeCord();
        /// <summary>Lets the cord out if it is wound up (a lamp's): its plug and cord exist after this.</summary>
        public virtual void Unbundle() { }
        /// <summary>Its plug is in a socket that has power.</summary>
        public bool PluggedLive => Plug && Plug.Socket != null && Plug.Socket.Powered;
        /// <summary>How far its plug can be from it (its cord's length).</summary>
        public virtual float CordReach => 100f;

        [Tooltip("Reeling the cord in (right mouse held), metres a second.")] public float reelSpeed = 1.8f;
        AudioSource _reelFx;

        /// <summary>
        /// Holding this item's plug with the right mouse held: the cord comes in toward the plug and this is dragged along
        /// the floor; once it is all in, it is wound up and in the hand. (Called every frame the plug is held.)
        /// </summary>
        public virtual void ReelFromPlug(Plug plug, in HoldInput i) { }

        public void StopReeling() => StopReel(GetCord());

        /// <summary>
        /// Right mouse held: the cord comes in hand over hand. Its length shrinks; past it the plug pops out of its socket
        /// (no jolt) and is dragged in along the floor. True once it is all in (the owner then coils or winds it up).
        /// </summary>
        protected bool ReelUpdate(Cord cord, Plug plug, bool reel, float dt, PropGrabber hand = null)
        {
            if (!_reelFx)
            {
                var at = new GameObject("ReelSound");
                at.transform.SetParent(transform, false);
                _reelFx = ItemModels.Source(at, 8f);
                _reelFx.clip = SoundBank.CordReel; _reelFx.loop = true; _reelFx.volume = 0f;
            }
            bool active = reel && cord && plug && cord.isActiveAndEnabled;
            // The sound is the cord running through the hand: at whichever end is held.
            if (plug) _reelFx.transform.position = plug.Held ? plug.transform.position : transform.position;
            if (active && !_reelFx.isPlaying) _reelFx.Play();
            _reelFx.volume = Mathf.MoveTowards(_reelFx.volume, (active ? 0.55f : 0f) * SoundCatalog.Volume(SoundIds.Cords), dt * 6f);
            if (!active)
            {
                if (_reelFx.isPlaying && _reelFx.volume <= 0f) _reelFx.Stop();
                if (cord) cord.reeling = false;
                return false;
            }
            cord.reeling = true;
            // The hand pulls hand over hand toward itself along the cord (it stood still the whole reel).
            if (hand)
            {
                Vector3 near = plug.Held ? plug.transform.position : transform.position, far = plug.Held ? transform.position : plug.transform.position;
                Vector3 way = far - near; way.y *= 0.3f;
                if (way.sqrMagnitude > 1e-4f) hand.ReelStroke(way.normalized);
            }
            float d = Vector3.Distance(cord.A, cord.B);
            // Not while the far end is still catching up (stuck behind something): the cord would only stretch.
            // As much as the hand pulls in (round 7: it came in at 1.8 m/s whatever the hand did, 1.2 m a 25 cm stroke).
            float take = hand ? hand.TakeReeled() : reelSpeed * dt;
            if (d < cord.length + 0.25f) cord.SetLength(Mathf.Max(0.3f, cord.length - take));
            if (plug.Plugged && d > cord.length) plug.Unplug(true);
            if (!plug.Plugged) plug.Body.WakeUp();
            return cord.length <= 0.32f || d < 0.3f;
        }

        protected void StopReel(Cord cord)
        {
            if (_reelFx) { _reelFx.Stop(); _reelFx.volume = 0f; }
            if (cord) cord.reeling = false;
        }

        protected override void OnDropped() => StopReel(GetCord());
        protected virtual Cord GetCord() => null;
    }
}
