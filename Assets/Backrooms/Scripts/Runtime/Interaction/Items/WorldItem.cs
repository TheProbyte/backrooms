using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Something small the free hand can pick up and use (see PropGrabber.Items.cs). Lying in the world it is a light
    /// physics object; it freezes while the floor under it is not loaded, so it never falls out of the world. Held,
    /// it follows the hand and the player's left mouse button is its "use".
    /// </summary>
    /// <summary>What the hand holding an item sees and does this frame (see <see cref="WorldItem.HoldUpdate"/>).</summary>
    public struct HoldInput
    {
        public bool use, pressed, released;      // left mouse: held, went down, came up this frame
        public bool secondary;                   // right mouse tapped (released within a moment)
        public bool reel;                        // right mouse held down (past the tap)
        public bool aim, aimPressed;             // right mouse held down (at once), and went down this frame
        public Socket socket;                    // the socket the hand aims at (focus brackets), for items that plug in
        public bool hit;                         // the view hits something within 6 m (not the player or the item)
        public RaycastHit hitInfo;
        public Vector3 eye, aimDir, aimAt;       // aimAt: the hit point, or 2 m ahead
        public float dt;
        public PropGrabber hand;
    }

    /// <summary>Something the empty hand can click (a plug in a socket): asked before picking anything up.</summary>
    public interface IHandUse
    {
        bool HandUse(PropGrabber hand);
    }

    [RequireComponent(typeof(Rigidbody))]
    public abstract class WorldItem : MonoBehaviour
    {
        public string itemId;
        public string displayName;
        public Rigidbody Body { get; private set; }
        public bool Held { get; private set; }
        Collider[] _colliders;
        WorldStreamer _streamer;

        /// <summary>Where the item sits relative to the eye (camera frame) while held: resting, and while in use.</summary>
        public virtual Vector3 HoldOffset => new Vector3(0.17f, -0.2f, 0.38f);
        public virtual Vector3 UseOffset => HoldOffset;
        /// <summary>Palm contact point and hand frame in the item's own frame (for the arm IK).</summary>
        public virtual Vector3 GripLocal => Vector3.zero;
        /// <summary>How the item is turned in the hand (camera frame, after the usual upright-in-view hold).</summary>
        public virtual Quaternion HoldTurn => Quaternion.identity;
        /// <summary>A point to put focus marks on while held (where a strip of tape will start), if any.</summary>
        public virtual bool AimMark(out Vector3 at) { at = default; return false; }
        public virtual Quaternion GripRotationLocal => Quaternion.identity;
        public virtual float GripCurl => 0.85f;

        protected virtual void Awake()
        {
            Body = GetComponent<Rigidbody>();
            Body.sleepThreshold = 0.02f;                     // settle and stop (no resting jitter)
            _colliders = GetComponentsInChildren<Collider>();
            // Small things get a steady contact shadow instead of a flickering shadow-map one.
            foreach (var r in GetComponentsInChildren<Renderer>()) if (!(r is ParticleSystemRenderer)) r.shadowCastingMode = ShadowCastingMode.Off;
            if (!GetComponent<ContactShadow>()) gameObject.AddComponent<ContactShadow>();
        }

        /// <summary>Held in place by something other than the hand (a plug in a socket): stays kinematic.</summary>
        protected virtual bool Pinned => false;

        bool _playerIgnored;

        /// <summary>Small things (a plug, chalk, tape) do not trip the player up: walking over them would bump the view.</summary>
        void IgnorePlayer()
        {
            _playerIgnored = true;
            if (Body.mass > 0.5f) return;
            var player = FindAnyObjectByType<CamcorderPlayerController>();
            var cc = player ? player.GetComponent<CharacterController>() : null;
            if (!cc) { _playerIgnored = false; return; }
            foreach (var c in GetComponentsInChildren<Collider>(true)) Physics.IgnoreCollision(cc, c, true);
        }

        protected virtual void FixedUpdate()
        {
            if (!_playerIgnored) IgnorePlayer();
            if (Held) return;
            if (Pinned) { if (!Body.isKinematic) Body.isKinematic = true; return; }
            // Frozen while the ground under it is unloaded (it would fall forever), awake otherwise.
            if (!_streamer) _streamer = FindAnyObjectByType<WorldStreamer>();
            bool ground = !_streamer || _streamer.LayoutAt(WorldStreamer.ChunkOf(transform.position)) != null;
            if (Body.isKinematic == ground) Body.isKinematic = !ground;
            if (transform.position.y < -50f) { Body.linearVelocity = Vector3.zero; transform.position += Vector3.up * 52f; }
            if (!Body.isKinematic) NoclipTravel.ItemStep(this);           // thrown through a noclip door
        }

        public void SetHeld(bool held)
        {
            Held = held;
            Body.isKinematic = held;
            Body.interpolation = held ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            // All of them, as they are now: parts added after Awake (the lamp's cord bundle) would stay solid in the hand
            // and shove the player along as they walked.
            _colliders = GetComponentsInChildren<Collider>(true);
            foreach (var c in _colliders) if (c) c.enabled = !held;

            if (held && lootId != 0) { Saves.LootRegistry.Take(lootId); lootId = 0; transform.SetParent(null, true); }   // taken from its room: a free item from now on
            if (held) OnPickedUp(); else OnDropped();
        }

        protected virtual void OnPickedUp() { }
        protected virtual void OnDropped() { }

        /// <summary>Every frame while held. use: the button is down. aimFrom/aimAt: the eye and what it looks at.</summary>
        public virtual void Hold(bool use, Vector3 aimAt, float dt) { }

        /// <summary>Every frame while held, with everything the hand knows (by default calls <see cref="Hold"/>).</summary>
        public virtual void HoldUpdate(in HoldInput input) => Hold(input.use, input.aimAt, input.dt);

        /// <summary>A pose of its own for the item while in use (chalk on the wall), with a weight 0..1; 0: the usual hold pose.</summary>
        public virtual float UsePose(out Vector3 position, out Quaternion rotation) { position = default; rotation = default; return 0f; }

        /// <summary>Q sets it down upright where you look (a lamp) instead of tossing it.</summary>
        public virtual bool PlaceOnDrop => false;
        /// <summary>Q lets it fall straight from the hand (no toss).</summary>
        public virtual bool DropsStraight => false;
        /// <summary>Could clicking this socket with the item in hand plug something in (it gets focus brackets)?</summary>
        public virtual bool TakesSocket(Socket s) => false;

        /// <summary>Called after the hand let go of it by setting it down (PlaceOnDrop).</summary>
        public virtual void OnPlaced() { }

        /// <summary>Extra state for the world save (paint left, length of chalk, a lamp's switch...).</summary>
        public virtual float Amount { get => 1f; set { } }

        /// <summary>Spawned in a special room and not yet taken: it belongs to its chunk and is not saved on its own.</summary>
        [System.NonSerialized] public ulong lootId;
        public bool IsLoot => lootId != 0;

        /// <summary>Use pose blend 0..1 (the hand raises the item while using it).</summary>
        public virtual float UseBlend => 0f;

        /// <summary>A short line for the HUD ("paint 63 %").</summary>
        public virtual string Status => "";
    }
}
