using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>Something the index finger presses or flicks (an elevator button, a light switch).</summary>
    public interface IPokeTarget
    {
        /// <summary>Out of the wall, toward whoever presses it.</summary>
        Vector3 PokeNormal { get; }
        /// <summary>The fingertip's stroke: it comes to <paramref name="from"/> and pushes through to <paramref name="to"/>.</summary>
        void PokeStroke(out Vector3 from, out Vector3 to);
        /// <summary>Called half way through the stroke.</summary>
        void Press();
        /// <summary>Where the focus marks go.</summary>
        Vector3 FocusPoint { get; }
    }

    /// <summary>
    /// The free hand also presses buttons and flicks light switches: click one within reach and the free arm reaches
    /// out with the index finger extended, pushes it (a button in, a switch's toggle down or up), and goes back. If it is
    /// a little far you step in.
    /// </summary>
    public partial class PropGrabber
    {
        [Header("Buttons")]
        [Tooltip("How far you can click a button from (m); the hand steps you in to reach it.")]
        public float pokeRange = 2.0f;

        enum PokePhase { None, Approach, Reach, Tap, Stroke, Hold, Pull }

        /// <summary>The arm is out pressing a button.</summary>
        public bool Poking => _poke >= PokePhase.Reach;
        /// <summary>Where the index fingertip is, and the hand frame (forward = along the fingers, up = back of the hand).</summary>
        public Vector3 PokeTip { get; private set; }
        public Quaternion PokeRotation { get; private set; } = Quaternion.identity;
        /// <summary>0..1: index finger out, the others curled.</summary>
        public float PokeIndex { get; private set; }
        /// <summary>The arm is in (or returning from) the pointing pose, so the body keeps using it while the arm lowers.</summary>
        public bool PokePose { get; private set; }

        PokePhase _poke;
        IPokeTarget _pokeButton;
        Object _pokeObject;              // the target as a Unity object (to notice it being destroyed)
        float _pokeT;
        bool _pressed;
        Vector3 _pokePoint, _pokeTo, _pokeNormal;

        /// <summary>The button or switch a click would press right now (for the focus marks), or null.</summary>
        public IPokeTarget AimedPoke { get; private set; }

        /// <summary>The nearest thing the view hits within reach, if it is a button or a switch (a wall in front blocks it).</summary>
        IPokeTarget LookPoke()
        {
            var cam = _cam ? _cam : Camera.main;
            if (!cam) return null;
            int n = Physics.RaycastNonAlloc(new Ray(cam.transform.position, cam.transform.forward), _pokeHits, pokeRange, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue; IPokeTarget btn = null;
            for (int i = 0; i < n; i++)
            {
                var h = _pokeHits[i];
                if (h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                var t = h.collider.GetComponentInParent<IPokeTarget>();
                if (t == null && h.collider.isTrigger) continue;           // other triggers do not block
                best = h.distance; btn = t;
            }
            return btn;
        }
        readonly RaycastHit[] _pokeHits = new RaycastHit[12];

        bool TryStartPoke()
        {
            var btn = LookPoke();
            if (btn == null) return false;
            _pokeButton = btn;
            _pokeObject = btn as Object;
            _pokeNormal = btn.PokeNormal;
            btn.PokeStroke(out _pokePoint, out _pokeTo);
            _pressed = false;
            _poke = PokePhase.Approach; _pokeT = 0f;
            return true;
        }

        void CancelPoke()
        {
            _poke = PokePhase.None; _pokeButton = null; _pokeObject = null;
            if (_player.botInput.HasValue && !botHold.HasValue) _player.botInput = null;
        }

        void UpdatePoke()
        {
            if (_pokeButton == null || !_pokeObject) { CancelPoke(); return; }
            _pokeT += Time.deltaTime;
            Vector3 hover = _pokePoint + _pokeNormal * 0.11f;
            Vector3 toBtn = _pokePoint - Shoulder;
            if (toBtn.magnitude > reach + 1.1f) { CancelPoke(); return; }
            switch (_poke)
            {
                case PokePhase.Approach:
                {
                    // The fingertip is further out than the palm: a little closer than for a grab.
                    if (toBtn.magnitude <= reach * 0.95f) { _player.botInput = null; _poke = PokePhase.Reach; _pokeT = 0f; break; }
                    if (_pokeT > 2.5f) { CancelPoke(); break; }
                    Vector3 local = transform.InverseTransformDirection(toBtn); local.y = 0f;
                    var flat = new Vector2(local.x, local.z);
                    _player.botInput = flat.magnitude > 0.1f ? flat.normalized * 0.55f : (Vector2?)null;
                    break;
                }
                case PokePhase.Reach:
                    PokeTip = hover;
                    if (HandBlend > 0.9f && _pokeT > 0.25f) { _poke = PokePhase.Tap; _pokeT = 0f; }
                    break;
                case PokePhase.Tap:
                {
                    float t = Mathf.Clamp01(_pokeT / 0.11f);
                    PokeTip = Vector3.Lerp(hover, _pokePoint, t * t * (3f - 2f * t));
                    if (t >= 1f) { _poke = PokePhase.Stroke; _pokeT = 0f; }
                    break;
                }
                case PokePhase.Stroke:
                {
                    // Pushed in (a button) or flicked over (a switch): quick, and it gives half way.
                    float t = Mathf.Clamp01(_pokeT / 0.08f);
                    PokeTip = Vector3.Lerp(_pokePoint, _pokeTo, t * t);
                    if (t >= 0.5f && !_pressed) { _pressed = true; _pokeButton.Press(); }
                    if (t >= 1f) { _poke = PokePhase.Hold; _pokeT = 0f; }
                    break;
                }
                case PokePhase.Hold:
                    PokeTip = _pokeTo;
                    if (_pokeT > 0.1f) { _poke = PokePhase.Pull; _pokeT = 0f; }
                    break;
                case PokePhase.Pull:
                {
                    float t = Mathf.Clamp01(_pokeT / 0.16f);
                    PokeTip = Vector3.Lerp(_pokeTo, hover, t * t * (3f - 2f * t));
                    if (t >= 1f) { _poke = PokePhase.None; _pokeButton = null; _pokeObject = null; }
                    break;
                }
            }
            // Finger points into the wall, a little downward; the back of the hand faces up.
            Vector3 fingers = (-_pokeNormal + Vector3.down * 0.18f).normalized;
            PokeRotation = Quaternion.LookRotation(fingers, Vector3.up);
        }

        void UpdatePokeFingers()
        {
            PokeIndex = Mathf.MoveTowards(PokeIndex, Poking ? 1f : 0f, Time.deltaTime * 6f);
        }
    }
}
