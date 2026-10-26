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
    public class Plug : WorldItem
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
        public override Vector3 GripLocal => new Vector3(0.012f, 0f, -0.02f);
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(new Vector3(-0.3f, 0f, 0.95f), Vector3.up);
        public override float GripCurl => 0.85f;
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

        public override void HoldUpdate(in HoldInput i)
        {
            if (!i.pressed || !i.hit || i.hitInfo.distance > reach) return;
            var s = Sockets.Nearest(i.hitInfo.point, 0.1f, owner as CordBox);
            if (s == null || !CanUse(s)) return;
            i.hand.LetGo(this);
            PlugInto(s);
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
    }
}
