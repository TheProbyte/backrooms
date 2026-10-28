using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A stick of white chalk. Hold the left mouse button on a wall or floor within reach and move your view to draw: the
    /// line follows where you look, freehand (an arrow, a number, anything), and stays there (<see cref="Marks"/>). Lifting
    /// the button or moving to another surface starts a new stroke. The stick wears down as it draws
    /// (<see cref="LineMetres"/> of line in a stick) and is gone when used up.
    /// Model: Resources/Items/Chalk.fbx (origin at the butt, tip forward).
    /// </summary>
    public class Chalk : WorldItem, IHandGrip
    {
        /// <summary>Metres of line a whole stick draws.</summary>
        public static float LineMetres = 30f;
        public const float Length = 0.09f, Radius = 0.008f;
        [Tooltip("Furthest you can draw from your eye (m).")] public float reach = 1.15f;
        [NotTuned, Range(0f, 1f)] public float left = 1f;

        Transform _model;
        AudioSource _scratch;
        bool _drawing;
        Vector3 _last, _lastNormal;
        float _speed, _pose;
        Vector3 _tip, _tipNormal, _aimDir;

        public override Vector3 HoldOffset => new Vector3(0.15f, -0.2f, 0.34f);
        public override Vector3 UseOffset => new Vector3(0.12f, -0.14f, 0.42f);
        // A writing grip (a tripod, as a pencil is held): the stick pinched between the pads of the thumb and the index
        // finger, resting on the middle finger's end, ring and little finger curled in. It runs from the web of the thumb out
        // past the index fingertip (its tip 4 cm beyond), 35 degrees down from the fingers toward the palm and a little
        // across. (Found by trying 1,300 ways against the stick: thumb, index and middle skin 0.9-1.5 mm into it (skin
        // gives 1 mm), nothing else in it. The round-5 grip had the stick under the index and middle pads, nearly along
        // the fingers: two fingertips pressing on a stick.)
        public override Vector3 GripLocal => TestGrip ?? new Vector3(0.02544f, 0.01526f - Radius, 0f);
        /// <summary>
        /// The stick as it is drawn (its model's axis is the item's z, through the origin), worn down from the tip. (The
        /// collider, for lying on the floor, sits a radius higher: fingers closed on it went 7.5 mm into the stick seen.)
        /// </summary>
        public override float ContactDistance(Vector3 p)
        {
            float h = Mathf.Max(0.02f, Length * left);
            return SegmentDistance(p, new Vector3(0f, 0f, Mathf.Min(0.0085f, h * 0.5f)), new Vector3(0f, 0f, Mathf.Max(h - 0.0085f, h * 0.5f))) - 0.0085f;
        }
        public override Quaternion GripRotationLocal => TestGrip.HasValue ? Quaternion.Euler(TestTurn)
            : Quaternion.LookRotation(new Vector3(-0.2756f, 0.5094f, 0.8152f), new Vector3(0f, 0.848f, -0.5299f));
        public override Quaternion HoldTurn => TestHold.HasValue ? Quaternion.Euler(TestHold.Value) : Quaternion.identity;
        /// <summary>Tests: how the stick is turned in the view (degrees) instead.</summary>
        public static Vector3? TestHold;
        public override float GripCurl => 0.85f;
        public float FingerCurl(int finger) => TestCurls != null ? TestCurls[finger] : finger == 0 ? 0.85f : finger == 1 ? 0.9f : finger == 2 ? 0.95f : 1f;
        public float ThumbCurl => TestCurls != null ? TestCurls[4] : 0.7f;
        public float ThumbOpposition => TestCurls != null ? TestCurls[5] : 0.8f;
        /// <summary>Tests: a grip to try instead (the palm's point; the hand's axes in the stick's frame as Euler angles; finger curls 0-3, the thumb's curl and turn).</summary>
        public static Vector3? TestGrip;
        public static Vector3 TestTurn;
        public static float[] TestCurls;
        public override string Status => $"chalk {Mathf.CeilToInt(left * 100f)} %";
        public override float Amount { get => left; set { left = Mathf.Clamp01(value); Shape(); } }

        public static Chalk Create(Vector3 position, Quaternion rotation)
        {
            var go = ItemModels.Body("Chalk", position, rotation, 0.012f, "Chalk", new Vector3(0.017f, 0.017f, Length), "ChalkWhite", true, new Vector3(0f, Radius, Length * 0.5f));
            var c = go.AddComponent<Chalk>();
            c.itemId = "chalk"; c.displayName = "Chalk";
            Generation.PoolLitSwitch.Attach(go);
            return c;
        }

        protected override void Awake()
        {
            base.Awake();
            _model = transform.Find("Model");
            _scratch = ItemModels.Source(gameObject, 10f);
            _scratch.clip = SoundBank.ChalkScratch; _scratch.loop = true; _scratch.volume = 0f;
        }

        /// <summary>The stick gets shorter from the tip (the butt stays in the hand).</summary>
        void Shape()
        {
            if (!_model) _model = transform.Find("Model");
            if (_model) _model.localScale = new Vector3(1f, 1f, Mathf.Max(0.12f, left));
            var col = GetComponent<CapsuleCollider>();
            if (col) { col.height = Mathf.Max(0.02f, Length * left); col.center = new Vector3(0f, Radius, col.height * 0.5f); }
        }

        static bool Drawable(in RaycastHit h) =>
            h.collider && !h.collider.GetComponentInParent<MovableProp>() && !h.collider.GetComponentInParent<WorldItem>() && !h.collider.GetComponent<CharacterController>();

        public override void HoldUpdate(in HoldInput i)
        {
            bool can = i.use && left > 0f && i.hit && i.hitInfo.distance <= reach && Drawable(i.hitInfo);
            float drawn = 0f;
            if (can)
            {
                Vector3 p = i.hitInfo.point, n = i.hitInfo.normal;
                bool sameSurface = _drawing && Vector3.Dot(n, _lastNormal) > 0.97f && Mathf.Abs(Vector3.Dot(p - _last, _lastNormal)) < 0.02f
                                   && (p - _last).sqrMagnitude < 0.25f * 0.25f;
                if (!sameSurface)
                {
                    // A new stroke: a dab where it touches.
                    _drawing = true; _last = p; _lastNormal = n;
                    Vector3 dab = Vector3.Cross(n, Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up).normalized * 0.002f;
                    Marks.Instance.AddChalk(p - dab, p + dab, n);
                }
                else if ((p - _last).sqrMagnitude >= 0.006f * 0.006f)
                {
                    drawn = Vector3.Distance(p, _last);
                    Marks.Instance.AddChalk(_last, p, n);
                    _last = p;
                }
                _tip = p; _tipNormal = n; _aimDir = i.aimDir;
            }
            else _drawing = false;

            if (drawn > 0f)
            {
                left = Mathf.Max(0f, left - drawn / Mathf.Max(1f, LineMetres));
                Shape();
            }
            _pose = Mathf.MoveTowards(_pose, can ? 1f : 0f, i.dt * 7f);
            _speed = Mathf.Lerp(_speed, drawn / Mathf.Max(i.dt, 1e-4f), 1f - Mathf.Exp(-i.dt / 0.06f));
            float vol = can ? Mathf.Clamp01(_speed / 0.6f) * 0.55f + 0.05f : 0f;
            _scratch.volume = Mathf.MoveTowards(_scratch.volume, vol, i.dt * 6f);
            _scratch.pitch = 0.85f + Mathf.Clamp01(_speed / 1.2f) * 0.35f;
            if (_scratch.volume > 0.001f && !_scratch.isPlaying) _scratch.Play();
            else if (_scratch.volume <= 0.001f && _scratch.isPlaying) _scratch.Stop();

            if (left <= 0f)
            {
                // Used up: the last crumb is gone.
                AudioSource.PlayClipAtPoint(SoundBank.Tick(1), transform.position, 0.4f);
                i.hand.ItemGone(this);
                Destroy(gameObject);
            }
        }

        /// <summary>While drawing, the tip is on the surface, the stick leaning back toward you.</summary>
        public override float UsePose(out Vector3 position, out Quaternion rotation)
        {
            Vector3 dir = Vector3.Slerp(-_tipNormal, _aimDir, 0.45f).normalized;
            if (dir.sqrMagnitude < 0.5f) dir = _aimDir;
            rotation = Quaternion.LookRotation(dir, Vector3.Cross(dir, Vector3.Cross(Vector3.up, dir)).sqrMagnitude > 1e-4f ? Vector3.up : Vector3.forward);
            position = _tip + _tipNormal * 0.001f - dir * (Length * Mathf.Max(0.12f, left));
            return _pose;
        }

        protected override void OnDropped()
        {
            _drawing = false; _pose = 0f;
            if (_scratch) _scratch.Stop();
        }
    }
}
