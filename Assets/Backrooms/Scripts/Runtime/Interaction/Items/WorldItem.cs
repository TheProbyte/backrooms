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
        public virtual Quaternion GripRotationLocal => Quaternion.identity;
        public virtual float GripCurl => 0.85f;

        protected virtual void Awake()
        {
            Body = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>();
        }

        protected virtual void FixedUpdate()
        {
            if (Held) return;
            // Frozen while the ground under it is unloaded (it would fall forever), awake otherwise.
            if (!_streamer) _streamer = FindAnyObjectByType<WorldStreamer>();
            bool ground = !_streamer || _streamer.LayoutAt(WorldStreamer.ChunkOf(transform.position)) != null;
            if (Body.isKinematic == ground) Body.isKinematic = !ground;
            if (transform.position.y < -50f) { Body.linearVelocity = Vector3.zero; transform.position += Vector3.up * 52f; }
        }

        public void SetHeld(bool held)
        {
            Held = held;
            Body.isKinematic = held;
            Body.interpolation = held ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
            foreach (var c in _colliders) if (c) c.enabled = !held;
            foreach (var r in GetComponentsInChildren<Renderer>()) r.shadowCastingMode = held ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (held) OnPickedUp(); else OnDropped();
        }

        protected virtual void OnPickedUp() { }
        protected virtual void OnDropped() { }

        /// <summary>Every frame while held. use: the button is down. aimFrom/aimAt: the eye and what it looks at.</summary>
        public virtual void Hold(bool use, Vector3 aimAt, float dt) { }

        /// <summary>Use pose blend 0..1 (the hand raises the item while using it).</summary>
        public virtual float UseBlend => 0f;

        /// <summary>A short line for the HUD ("paint 63 %").</summary>
        public virtual string Status => "";
    }
}
