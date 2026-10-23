using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The free hand also presses buttons (elevator call buttons): click one within reach and the left arm
    /// reaches out with the index finger extended, taps it, and goes back. If it is a little far you step in.
    /// </summary>
    public partial class PropGrabber
    {
        [Header("Buttons")]
        [Tooltip("How far you can click a button from (m); the hand steps you in to reach it.")]
        public float pokeRange = 2.0f;

        enum PokePhase { None, Approach, Reach, Tap, Hold, Pull }

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
        ElevatorButton _pokeButton;
        float _pokeT;
        Vector3 _pokePoint, _pokeNormal;
        readonly RaycastHit[] _pokeHits = new RaycastHit[12];

        bool TryStartPoke()
        {
            var cam = _cam ? _cam : Camera.main;
            if (!cam) return false;
            int n = Physics.RaycastNonAlloc(new Ray(cam.transform.position, cam.transform.forward), _pokeHits, pokeRange, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue; ElevatorButton btn = null;
            for (int i = 0; i < n; i++)
            {
                var h = _pokeHits[i];
                if (h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                best = h.distance;
                btn = h.collider.GetComponentInParent<ElevatorButton>();          // the nearest thing hit decides: a wall in front blocks it
            }
            if (!btn) return false;
            _pokeButton = btn;
            _pokeNormal = btn.Normal;
            _pokePoint = btn.transform.TransformPoint(new Vector3(0f, 0.02f, 0.028f));      // the face of the lamp
            _poke = PokePhase.Approach; _pokeT = 0f;
            return true;
        }

        void CancelPoke()
        {
            _poke = PokePhase.None; _pokeButton = null;
            if (_player.botInput.HasValue && !botHold.HasValue) _player.botInput = null;
        }

        void UpdatePoke()
        {
            if (!_pokeButton) { CancelPoke(); return; }
            _pokeT += Time.deltaTime;
            Vector3 hover = _pokePoint + _pokeNormal * 0.11f;
            Vector3 toBtn = _pokePoint - Shoulder;
            if (toBtn.magnitude > reach + 1.1f) { CancelPoke(); return; }
            switch (_poke)
            {
                case PokePhase.Approach:
                {
                    if (toBtn.magnitude <= reach * 0.92f + 0.05f) { _player.botInput = null; _poke = PokePhase.Reach; _pokeT = 0f; break; }
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
                    if (t >= 1f) { _pokeButton.Press(); _poke = PokePhase.Hold; _pokeT = 0f; }
                    break;
                }
                case PokePhase.Hold:
                    PokeTip = _pokePoint;
                    if (_pokeT > 0.12f) { _poke = PokePhase.Pull; _pokeT = 0f; }
                    break;
                case PokePhase.Pull:
                {
                    float t = Mathf.Clamp01(_pokeT / 0.16f);
                    PokeTip = Vector3.Lerp(_pokePoint, hover, t);
                    if (t >= 1f) { _poke = PokePhase.None; _pokeButton = null; }
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
