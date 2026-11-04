using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A rubber chicken. Hold the left mouse button to squeeze it: its body squashes (the model's "Squeeze" shape) as it
    /// draws air in with a wheeze; let go and it lets the air out in a squawk, longer and louder the longer it was
    /// squeezed. Throw it (hold Q) and it squeaks when it lands hard.
    /// Model: Resources/Items/RubberChicken.fbx (origin at the body, head forward).
    /// </summary>
    public class RubberChicken : WorldItem, IHandGrip, IGripRound
    {
        SkinnedMeshRenderer _skin;
        int _shape = -1;
        float _squeeze;
        bool _squeezing;
        AudioSource _voice, _fx;
        int _variant;
        float _quietUntil;

        // Held by the neck (the model's neck: about z 0.09-0.21, 13-15 mm round, centred y 0.008), head up out of the top of
        // the fist and the body hanging below: the fist round the neck, the back of the hand up and out, the index side
        // toward the head. (The grip used to be inside the body: the hand in it, fingertips out of its belly.)
        public override Vector3 HoldOffset => new Vector3(0.19f, -0.36f, 0.29f);
        public override Vector3 UseOffset => new Vector3(0.17f, -0.33f, 0.29f);
        // Rolled 8 degrees outward in the view: held square, the forearm was turned palm-down 97 degrees (a forearm turns 95).
        public override Quaternion HoldTurn => Quaternion.Euler(0f, 0f, -8f) * Quaternion.Euler(-70f, 0f, 0f);
        static readonly Vector3 NeckBack = new Vector3(0.6f, 0.8f, 0f);
        public override Vector3 GripLocal => new Vector3(0f, 0.008f, 0.15f) + NeckBack * (NeckR + 0.002f);
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(new Vector3(0.8f, -0.6f, 0f), NeckBack);
        /// <summary>Round 10: the neck taken from any side (IGripRound): it runs along z through the grip's middle.</summary>
        public void GripAxis(out Vector3 point, out Vector3 axis) { point = new Vector3(0f, 0.008f, 0.15f); axis = Vector3.forward; }
        public override float GripCurl => 0.85f + 0.12f * _squeeze;      // the fist closes as it squeezes
        // Squeezed, the fist closes 10-15 degrees further and the pads sink 2-4 mm into the rubber (ContactDistance). (The
        // fingers stayed as in the plain hold, stopped on the neck.)
        public float FingerCurl(int finger) => 0.9f + 0.45f * _squeeze;
        // (0.8: it stopped short of the neck, 12-18 mm off, run to run. Closing further on a squeeze, 1.5, hooked the end
        // joint to 91 degrees over the top and still stood 9 mm off.)
        public float ThumbCurl => 1f;
        public float ThumbOpposition => 0.85f;
        const float NeckR = 0.014f;

        /// <summary>The model's shape, roughly: the body, the neck and the head (the fingers close on the neck).</summary>
        public override float ContactDistance(Vector3 p)
        {
            float neck = SegmentDistance(p, new Vector3(0f, 0.01f, 0.06f), new Vector3(0f, 0.006f, 0.205f)) - NeckR;
            float head = Vector3.Distance(p, new Vector3(0f, 0.018f, 0.262f)) - 0.032f;
            float body = SegmentDistance(p, new Vector3(0f, 0f, -0.095f), new Vector3(0f, 0f, 0.02f)) - 0.041f;
            // Squeezed, the rubber gives under the fingers.
            return Mathf.Min(neck, Mathf.Min(head, body)) + SqueezeGive * _squeeze;
        }
        /// <summary>How far the pads sink into the rubber squeezed (m).</summary>
        public const float SqueezeGive = 0.011f;     // (4.5 mm closed the fist only 3-4 degrees)
        public override float UseBlend => _squeeze;
        public override bool Pocketable => true;
        public override bool UsesAim => false;
        public override string Status => "rubber chicken (hold to squeeze)";

        public static RubberChicken Create(Vector3 position, Quaternion rotation)
        {
            var go = ItemModels.Body("RubberChicken", position, rotation, 0.18f, "RubberChicken", new Vector3(0.12f, 0.12f, 0.46f), "ChickenYellow", true, Vector3.zero);
            var c = go.AddComponent<RubberChicken>();
            c.itemId = "rubberchicken"; c.displayName = "Rubber chicken";
            Generation.PoolLitSwitch.Attach(go);
            return c;
        }

        protected override void Awake()
        {
            base.Awake();
            _skin = GetComponentInChildren<SkinnedMeshRenderer>();
            if (_skin && _skin.sharedMesh) _shape = _skin.sharedMesh.GetBlendShapeIndex("Squeeze");
            _voice = ItemModels.Source(gameObject, 25f);
            _fx = ItemModels.Source(gameObject, 25f);
            Body.angularDamping = 1.2f;
        }

        float _squeezedAt, _squawkEnd = -1f;

        public override void HoldUpdate(in HoldInput i)
        {
            if (i.pressed) Squeeze();
            if (i.released || (!i.use && _squeezing)) Release();
            _squeeze = Mathf.MoveTowards(_squeeze, _squeezing ? 1f : 0f, i.dt * (_squeezing ? 5f : 14f));
            Shape();
        }

        /// <summary>Squeezing: it draws air in with a wheeze while the body squashes.</summary>
        void Squeeze()
        {
            _squeezing = true;
            _squeezedAt = Time.time; _squawkEnd = -1f;
            _variant = Random.Range(0, 3);
            _voice.pitch = Random.Range(0.95f, 1.06f);
            _voice.clip = SoundBank.ChickenRelease(_variant);
            _voice.volume = 0.55f * SoundCatalog.Volume(SoundIds.RubberChicken);
            _voice.Play();
        }

        /// <summary>Letting go: the air comes out in a squawk, as long and loud as the squeeze filled it.</summary>
        void Release()
        {
            if (!_squeezing) return;
            _squeezing = false;
            _voice.Stop();
            float air = Mathf.Clamp01((Time.time - _squeezedAt) / 0.45f) * 0.75f + 0.25f;
            _voice.clip = SoundBank.ChickenSqueeze(_variant);
            _voice.pitch = Random.Range(0.94f, 1.08f) * Mathf.Lerp(1.12f, 0.96f, air);
            _voice.volume = 0.95f * air * SoundCatalog.Volume(SoundIds.RubberChicken);
            _voice.time = 0f;
            _voice.Play();
            _squawkEnd = Time.time + 0.12f + 0.68f * air;          // a short squeeze, a short squeak (faded out in Update)
        }

        void Shape()
        {
            if (_skin && _shape >= 0) _skin.SetBlendShapeWeight(_shape, _squeeze * 100f);
        }

        void Update()
        {
            if (_squawkEnd > 0f && Time.time > _squawkEnd)
            {
                _voice.volume = Mathf.MoveTowards(_voice.volume, 0f, Time.deltaTime * 14f);
                if (_voice.volume <= 0f) { _voice.Stop(); _squawkEnd = -1f; }
            }
            if (Held) return;
            // Lying about: let go of any squeeze.
            if (_squeezing) Release();
            if (_squeeze > 0f) { _squeeze = Mathf.MoveTowards(_squeeze, 0f, Time.deltaTime * 6f); Shape(); }
        }

        void OnCollisionEnter(Collision c)
        {
            if (Held || Time.time < _quietUntil) return;
            float v = c.relativeVelocity.magnitude;
            if (v < 2.2f) return;
            _quietUntil = Time.time + 0.35f;
            // A short squawk as it lands on its belly.
            _fx.pitch = Random.Range(1.0f, 1.15f);
            _fx.PlayOneShot(SoundBank.ChickenSqueeze(Random.Range(0, 3)), Mathf.Clamp01((v - 2f) / 5f) * 0.7f * SoundCatalog.Volume(SoundIds.RubberChicken));
            _squeeze = 0.8f; Shape();
        }

        protected override void OnDropped() { Release(); }
    }
}
