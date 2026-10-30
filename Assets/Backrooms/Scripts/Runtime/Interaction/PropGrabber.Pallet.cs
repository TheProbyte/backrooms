using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Pallets. A loaded one (anything resting on it) is too heavy to lift: it is pushed like furniture (PropGrabber.Furniture),
    /// the boxes riding on it. An empty one is carried: taken, the camcorder goes onto its strap and both hands swing it up
    /// onto its long edge at your side (the hand's side), its deck facing out, top edge at about hip height and a little
    /// ahead of you, so you see past it and it goes through doors. It is held steady, not wrestled: it follows the body
    /// (the mouse turns you as when walking), it only stops where it meets a wall or a door frame, and if it is held back
    /// too long it is dropped. Let go and it falls from your hands (onto its side, a clatter).
    /// </summary>
    public partial class PropGrabber
    {
        /// <summary>Pushing something as low as a pallet: the hand is not kept within the arm's reach (it pushes where the body
        /// is bent over it), nor does the body bend all the way down to it.</summary>
        bool LowPush => Pushing && Held && Held.Spec.kind == MovableKind.Pallet;

        /// <summary>Reaching for a pallet's edge (14.5 cm up): taken when the palm is near enough, as an item is.</summary>
        bool LowGrab => _target && _target.Spec.kind == MovableKind.Pallet && !HeldItem;

        /// <summary>Carrying an empty pallet at your side (see the class notes).</summary>
        public bool CarryingPallet { get; private set; }

        /// <summary>Where the carried pallet's middle is, in the body's frame (x toward the hand's side, metres from the feet).</summary>
        static readonly Vector3 PalletCarryCentre = new Vector3(0.4f, 0.56f, 0.42f);
        /// <summary>The hands on its top edge, along its length from its middle (m): the right nearer the body, the left ahead.</summary>
        const float PalletRightAlong = -0.34f, PalletLeftAlong = -0.1f;
        const float PalletSwingTime = 0.6f;

        Vector3 _palFromPos;            // the pallet's pose when taken, in the body's frame (it swings up from there)
        Quaternion _palFromRot;
        float _palT, _palCaughtT;
        float _palTopSide;              // +1 or -1: which long edge (its local z) is on top
        Collider[] _palColliders;
        CharacterController _cc;

        /// <summary>Anything resting on the pallet (a box, another pallet, an item), or not lying flat enough to tell.</summary>
        public static bool PalletLoaded(MovableProp p)
        {
            if (!p || p.Spec.kind != MovableKind.Pallet) return false;
            if (Vector3.Angle(p.transform.up, Vector3.up) > 30f) return false;
            Vector3 c = p.transform.TransformPoint(new Vector3(0f, PalletGeometry.Height + 0.14f, 0f));
            var half = new Vector3(PalletGeometry.Length * 0.5f - 0.02f, 0.12f, PalletGeometry.Width * 0.5f - 0.02f);
            int n = Physics.OverlapBoxNonAlloc(c, half, s_palOver, p.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = s_palOver[i];
                if (col.transform.IsChildOf(p.transform)) continue;
                var rb = col.attachedRigidbody;
                if (rb && (rb.GetComponent<MovableProp>() || rb.GetComponent<WorldItem>())) return true;
            }
            return false;
        }
        static readonly Collider[] s_palOver = new Collider[24];

        /// <summary>On taking hold (AttachFurniture): an empty pallet is carried. Returns true if so.</summary>
        bool StartPalletCarry()
        {
            if (!Held || Held.Spec.kind != MovableKind.Pallet || PalletLoaded(Held)) return false;
            CarryingPallet = true;
            Pushing = false;
            _palT = 0f; _palCaughtT = 0f;
            var inv = Quaternion.Inverse(transform.rotation);
            _palFromPos = inv * (Held.Body.position - transform.position);
            _palFromRot = inv * Held.Body.rotation;
            // The long edge nearer you goes on top (the hands are on it already, more or less).
            Vector3 toMe = transform.position - Held.transform.position;
            _palTopSide = Vector3.Dot(Held.transform.forward, toMe) >= 0f ? 1f : -1f;
            float topZ = _palTopSide * (PalletGeometry.Width * 0.5f - 0.01f);
            _gripLocal = new Vector3(PalletRightAlong, PalletGeometry.Height * 0.5f, topZ);
            _normalLocal = new Vector3(0f, 0f, _palTopSide);
            _edgeLocal = Vector3.right;
            _fingersLocal = Vector3.zero;
            _gripLocalB = new Vector3(PalletLeftAlong, PalletGeometry.Height * 0.5f, topZ);
            _normalLocalB = _normalLocal;
            TwoHanded = true;
            _secondFarT = 0f;
            Held.Body.useGravity = false;
            // It swings past the body: not into it.
            if (!_cc) _cc = GetComponent<CharacterController>();
            _palColliders = Held.GetComponentsInChildren<Collider>();
            if (_cc) foreach (var c in _palColliders) if (c) Physics.IgnoreCollision(_cc, c, true);
            UpdateStrap();
            return true;
        }

        /// <summary>Let go (Release): it falls; the body passes through it until it has stepped away.</summary>
        void EndPalletCarry()
        {
            if (!CarryingPallet) return;
            CarryingPallet = false;
            if (Held) Held.Body.useGravity = true;
            if (_palColliders != null) { _palPassing = _palColliders; _palPassingProp = Held; }
            _palColliders = null;
        }

        Collider[] _palPassing;
        MovableProp _palPassingProp;

        float _savedStep = -1f;

        /// <summary>
        /// While the hands go for a pallet or hold it, the feet do not step up onto it: it is only 14.5 cm high, and walking
        /// into one to push it (or stepping in to reach it) the body climbed onto what it was pushing.
        /// </summary>
        void UpdatePalletStep()
        {
            if (!_cc) _cc = GetComponent<CharacterController>();
            if (!_cc) return;
            bool want = _target && _target.Spec.kind == MovableKind.Pallet && Current != State.Idle;
            if (want && _savedStep < 0f) { _savedStep = _cc.stepOffset; _cc.stepOffset = 0.04f; }
            else if (!want && _savedStep >= 0f) { _cc.stepOffset = _savedStep; _savedStep = -1f; }
        }

        /// <summary>A dropped pallet is solid to the body again once the body is clear of it.</summary>
        void UpdatePalletPassing()
        {
            UpdatePalletStep();
            if (_palPassing == null) return;
            if (!_palPassingProp || CarryingPallet) { if (!_palPassingProp) _palPassing = null; return; }
            var b = _palPassingProp.WorldBounds;
            if (b.SqrDistance(transform.position + Vector3.up * 0.9f) < 0.45f * 0.45f) return;
            if (_cc) foreach (var c in _palPassing) if (c) Physics.IgnoreCollision(_cc, c, false);
            _palPassing = null; _palPassingProp = null;
        }

        /// <summary>The carry pose's rotation, in the body's frame: on its long edge, the deck facing out, the top edge
        /// <see cref="_palTopSide"/> up, its length along your way.</summary>
        Quaternion PalletCarryRot => Quaternion.LookRotation(Vector3.up * _palTopSide, Vector3.right * Side * _palTopSide);

        /// <summary>Where the pallet's foot goes now (world), and its rotation: swinging up from where it was taken.</summary>
        void PalletTarget(out Vector3 pos, out Quaternion rot)
        {
            Quaternion r = PalletCarryRot;
            Vector3 centre = new Vector3(PalletCarryCentre.x * Side, PalletCarryCentre.y, PalletCarryCentre.z);
            Vector3 p = centre - r * new Vector3(0f, PalletGeometry.Height * 0.5f, 0f);
            float s = Mathf.SmoothStep(0f, 1f, _palT);
            // (Up first, then over: lifted in an arc so it clears the floor as it turns.)
            Vector3 lp = Vector3.Lerp(_palFromPos, p, s) + Vector3.up * (0.25f * 4f * s * (1f - s));
            Quaternion lr = Quaternion.Slerp(_palFromRot, r, s);
            pos = transform.position + transform.rotation * lp;
            rot = transform.rotation * lr;
        }

        /// <summary>Each frame while carrying (instead of the hold's mouse work): the mouse turns you; held back too long, it drops.</summary>
        void UpdatePalletCarry()
        {
            _palT = Mathf.Min(1f, _palT + Time.deltaTime / PalletSwingTime);
            _player.lookScale = 0.75f;
            _player.moveScale = 0.8f;
            _player.noRun = true;
            // The hold's own offset follows the right hand's spot (the body bends no lower than it needs to).
            PalletTarget(out var pos, out var rot);
            Vector3 grip = pos + rot * _gripLocal;
            _offset = Quaternion.Inverse(transform.rotation) * (grip - StandShoulder);
            float off = Vector3.Distance(pos, Held.Body.position);
            _palCaughtT = off > 0.4f ? _palCaughtT + Time.deltaTime : 0f;
            Strain = Mathf.Clamp01(0.25f + off / 0.4f * 0.75f);
            if (_palCaughtT > 0.5f) Release("the pallet caught on something");
        }

        /// <summary>FixedUpdate while carrying: it is moved to where the hands hold it (not by a hand's limited pull), so it
        /// stays steady; a wall or a door frame stops it as it would any moving thing.</summary>
        void PalletCarryForces(Rigidbody rb)
        {
            PalletTarget(out var pos, out var rot);
            Vector3 v = (pos - rb.position) / 0.07f + _player.PlanarVelocity;
            rb.linearVelocity = Vector3.ClampMagnitude(v, 6f);
            (rot * Quaternion.Inverse(rb.rotation)).ToAngleAxis(out float ang, out Vector3 axis);
            if (ang > 180f) ang -= 360f;
            rb.angularVelocity = axis.sqrMagnitude > 0.5f && !float.IsNaN(axis.x) ? Vector3.ClampMagnitude(axis.normalized * (ang * Mathf.Deg2Rad / 0.09f), 9f) : Vector3.zero;
            _force = Vector3.zero;
            _lag = Vector3.Distance(pos, rb.position);
        }
    }
}
