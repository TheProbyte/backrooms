using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A roll of blue painter's tape. Press the left mouse button on a wall, doorframe or floor within reach to stick the
    /// end down, drag along the surface to pull a strip off the roll, release to tear it: the strip stays there
    /// (<see cref="Marks"/>, the same marks as chalk). A strip lies on one flat surface and stops where that surface ends
    /// (a door opening, the edge of a frame), up to <see cref="MaxStrip"/> long. The roll holds <see cref="RollMetres"/>.
    /// Model: Resources/Items/TapeRoll.fbx.
    /// </summary>
    public class TapeRoll : WorldItem
    {
        public static float RollMetres = 40f;
        public const float MaxStrip = 1.6f, MinStrip = 0.03f;
        [Tooltip("Furthest you can stick the end down from your eye (m).")] public float reach = 1.6f;
        public float metres = 40f;

        AudioSource _pull, _fx;
        bool _pulling;
        Vector3 _start, _end, _normal;
        float _len, _lastLen, _pose;

        public override Vector3 HoldOffset => new Vector3(0.16f, -0.24f, 0.36f);
        public override Vector3 UseOffset => new Vector3(0.12f, -0.16f, 0.42f);
        public override Vector3 GripLocal => new Vector3(0.03f, 0.055f, 0f);
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(new Vector3(-0.5f, -0.2f, 0.85f), new Vector3(0.6f, 0.8f, 0f));
        public override float GripCurl => 0.8f;
        public override string Status => metres <= 0.01f ? "tape: empty" : $"tape {metres:0.0} m";
        public override float Amount { get => Mathf.Clamp01(metres / RollMetres); set { metres = Mathf.Clamp01(value) * RollMetres; Shape(); } }

        // The model's wound tape ("Wound", origin at the roll's centre, roll axis local X) shrinks toward the core as tape
        // is used. It can only shrink so far (its inner edge would show in the core's hole), then the roll becomes the
        // empty model: the bare cardboard core with a torn stub of tape left on it.
        const float EmptyRadius = 0.0475f, FullRadius = 0.0525f;
        Transform _wound, _full;
        GameObject _empty;

        void Shape()
        {
            if (!_wound) _wound = FindDeep(transform, "Wound");
            if (!_full) _full = transform.Find("Model");
            bool empty = metres <= 0.01f;
            if (_empty) { _empty.SetActive(empty); if (_full) _full.gameObject.SetActive(!empty); }
            else if (_wound) _wound.gameObject.SetActive(!empty);
            if (!_wound) return;
            float f = Mathf.Clamp01(metres / RollMetres);
            // Tape on a roll is wound area: the radius goes with the square root of what is left.
            float r = Mathf.Sqrt(EmptyRadius * EmptyRadius + f * (FullRadius * FullRadius - EmptyRadius * EmptyRadius));
            float k = r / FullRadius;
            _wound.localScale = new Vector3(1f, k, k);
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }
        public override float UseBlend => _pulling ? 1f : 0f;

        public static TapeRoll Create(Vector3 position, Quaternion rotation)
        {
            var go = ItemModels.Body("TapeRoll", position, rotation, 0.25f, "TapeRoll", new Vector3(0.048f, 0.105f, 0.105f), "TapeBlue", false, new Vector3(0f, 0.0525f, 0f));
            var t = go.AddComponent<TapeRoll>();
            t.itemId = "tape"; t.displayName = "Blue tape"; t.metres = RollMetres;
            Generation.PoolLitSwitch.Attach(go);
            return t;
        }

        protected override void Awake()
        {
            // The used-up roll, ready under the full one (made before the item's setup so it gets the same treatment).
            if (!transform.Find("ModelEmpty") && Resources.Load<GameObject>("Items/TapeRollEmpty"))
            {
                _empty = ItemModels.Attach(transform, "TapeRollEmpty", Vector3.one * 0.05f, "TapeCore");
                _empty.name = "ModelEmpty";
            }
            base.Awake();
            _pull = ItemModels.Source(gameObject, 10f);
            _pull.clip = SoundBank.TapePull; _pull.loop = true; _pull.volume = 0f;
            _fx = ItemModels.Source(gameObject, 10f);
            Shape();
        }

        static bool Stickable(in RaycastHit h) =>
            h.collider && !h.collider.GetComponentInParent<MovableProp>() && !h.collider.GetComponentInParent<WorldItem>() && !h.collider.GetComponent<CharacterController>();

        /// <summary>Is there surface under this point of the strip, flat with its start?</summary>
        bool OnSurface(Vector3 p)
        {
            if (!Physics.Raycast(p + _normal * 0.03f, -_normal, out var h, 0.06f, ~0, QueryTriggerInteraction.Ignore)) return false;
            return Vector3.Dot(h.normal, _normal) > 0.97f && Mathf.Abs(h.distance - 0.03f) < 0.01f && Stickable(h);
        }

        public override void HoldUpdate(in HoldInput i)
        {
            if (!_pulling && i.pressed && metres > MinStrip && i.hit && i.hitInfo.distance <= reach && Stickable(i.hitInfo))
            {
                _pulling = true;
                _start = i.hitInfo.point; _normal = i.hitInfo.normal;
                _end = _start; _len = _lastLen = 0f;
                _fx.PlayOneShot(SoundBank.TapeTear, 0.25f);
            }
            if (_pulling)
            {
                // The free end follows the view across the plane of the surface it started on.
                float denom = Vector3.Dot(i.aimDir, _normal);
                if (denom < -1e-3f)
                {
                    float t = Vector3.Dot(_start - i.eye, _normal) / denom;
                    Vector3 want = i.eye + i.aimDir * t;
                    Vector3 d = Vector3.ProjectOnPlane(want - _start, _normal);
                    float len = Mathf.Min(d.magnitude, MaxStrip, metres);
                    Vector3 dir = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.zero;
                    // Stop where the surface ends.
                    float ok = 0f;
                    for (float s = 0.02f; s <= len + 1e-4f; s += 0.02f) { if (!OnSurface(_start + dir * s)) break; ok = s; }
                    if (len > 0f && ok + 0.02f > len && OnSurface(_start + dir * len)) ok = len;
                    _len = ok;
                    _end = _start + dir * _len;
                }
                if (_len > 0.005f) Marks.Instance.SetPreview(_start, _end, _normal); else Marks.Instance.ClearPreview();
                float speed = Mathf.Abs(_len - _lastLen) / Mathf.Max(i.dt, 1e-4f);
                _lastLen = _len;
                _pull.volume = Mathf.MoveTowards(_pull.volume, Mathf.Clamp01(speed / 0.8f) * 0.5f, i.dt * 8f);
                if (_pull.volume > 0.001f && !_pull.isPlaying) _pull.Play();

                if (!i.use)
                {
                    // Torn off and pressed down.
                    if (_len >= MinStrip)
                    {
                        Marks.Instance.AddTape(_start, _end, _normal);
                        metres = Mathf.Max(0f, metres - _len);
                        Shape();
                        _fx.PlayOneShot(SoundBank.TapeTear, 0.6f);
                    }
                    Stop();
                }
            }
            _pose = Mathf.MoveTowards(_pose, _pulling ? 1f : 0f, i.dt * 5f);
        }

        void Stop()
        {
            _pulling = false;
            Marks.Existing?.ClearPreview();
            if (_pull) { _pull.volume = 0f; _pull.Stop(); }
        }

        /// <summary>While pulling, the roll follows the free end (as far as the arm reaches).</summary>
        public override float UsePose(out Vector3 position, out Quaternion rotation)
        {
            var eye = Camera.main ? Camera.main.transform.position : transform.position;
            Vector3 along = _end - _start;
            Vector3 at = _end + _normal * 0.06f;
            position = eye + Vector3.ClampMagnitude(at - eye, 0.62f);
            Vector3 fwd = along.sqrMagnitude > 1e-6f ? along.normalized : (Camera.main ? Camera.main.transform.forward : Vector3.forward);
            rotation = Quaternion.LookRotation(fwd, _normal);
            return _pose * 0.8f;
        }

        protected override void OnDropped() { Stop(); _pose = 0f; }

        void OnDestroy() { if (_pulling) Marks.Existing?.ClearPreview(); }
    }
}
