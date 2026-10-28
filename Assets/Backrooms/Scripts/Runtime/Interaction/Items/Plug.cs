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
    public class Plug : WorldItem, IHandGrip
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
        public override Vector3 GripLocal => new Vector3(0.014f, 0.055f, -0.0525f);
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(new Vector3(0f, 0.05f, 1f), new Vector3(0f, 1f, -0.05f));
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
            _fx.PlayOneShot(SoundBank.PlugIn, 0.7f);
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
            if (was) _fx.PlayOneShot(SoundBank.PlugOut, popped ? 0.8f : 0.5f);
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
            transform.SetPositionAndRotation(Socket.Position, Socket.PlugRotation);
        }

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
        protected override void OnDropped() { _going = null; _goPose = 0f; if (owner is PowerUser user) user.StopReeling(); }

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
        Socket _going;
        float _goT, _goPose, _pushT = -1f;
        Vector3 _goPos; Quaternion _goRot = Quaternion.identity;
        const float Approach = 0.05f, PushTime = 0.18f;

        /// <summary>Plug in to this socket, the hand taking it there (it must be in the hand).</summary>
        public void BeginPlugIn(Socket s) { _going = s; _goT = 0f; _pushT = -1f; }

        void GoingIn(in HoldInput i)
        {
            var s = _going;
            if (s == null || !CanUse(s) || s.occupant != null) { _going = null; return; }
            _goT += i.dt;
            _goPose = Mathf.MoveTowards(_goPose, 1f, i.dt * 1.6f);
            Vector3 outward = -(s.PlugRotation * Vector3.forward);
            float d = _pushT < 0f ? Approach : Approach * (1f - Mathf.Clamp01(_pushT / PushTime));
            _goPos = s.Position + outward * d; _goRot = s.PlugRotation;
            i.hand.ReachToward(_goPos, _pushT < 0f);
            if (_pushT < 0f)
            {
                // Square in front of it, the hand there: push. Never getting there (out of reach): the hand comes back.
                bool there = _goPose >= 1f && (transform.position - _goPos).magnitude < 0.015f && i.hand.PalmError < 0.02f;
                if (there) _pushT = 0f;
                else if (_goT > 3f) _going = null;
                return;
            }
            _pushT += i.dt;
            if (_pushT < PushTime) return;
            // In: let go. Short of it (the body moved): back out and again.
            if ((transform.position - s.Position).magnitude < 0.03f) { _going = null; i.hand.LetGo(this); PlugInto(s); }
            else _pushT = -1f;
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
            _reelFx.volume = Mathf.MoveTowards(_reelFx.volume, active ? 0.55f : 0f, dt * 6f);
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
            if (d < cord.length + 0.25f) cord.SetLength(Mathf.Max(0.3f, cord.length - reelSpeed * dt));
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
