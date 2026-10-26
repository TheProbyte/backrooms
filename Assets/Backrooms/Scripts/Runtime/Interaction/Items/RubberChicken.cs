using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A rubber chicken. Hold the left mouse button to squeeze it: it squawks and its body squashes (the model's
    /// "Squeeze" shape); let go and it wheezes as the air goes back in. Throw it (Q) and it squeaks when it lands hard.
    /// Model: Resources/Items/RubberChicken.fbx (origin at the body, head forward).
    /// </summary>
    public class RubberChicken : WorldItem
    {
        SkinnedMeshRenderer _skin;
        int _shape = -1;
        float _squeeze;
        bool _squeezing;
        AudioSource _voice, _fx;
        int _variant;
        float _quietUntil;

        public override Vector3 HoldOffset => new Vector3(0.17f, -0.22f, 0.4f);
        public override Vector3 UseOffset => new Vector3(0.14f, -0.18f, 0.4f);
        public override Vector3 GripLocal => new Vector3(0.03f, 0.02f, -0.06f);
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(new Vector3(-0.7f, 0f, 0.7f), Vector3.up);
        public override float GripCurl => 0.6f + 0.35f * _squeeze;      // the fist closes as it squeezes
        public override float UseBlend => _squeeze;
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

        public override void HoldUpdate(in HoldInput i)
        {
            if (i.pressed) Squeeze(1f);
            if (i.released || (!i.use && _squeezing)) Release();
            _squeeze = Mathf.MoveTowards(_squeeze, _squeezing ? 1f : 0f, i.dt * (_squeezing ? 14f : 6f));
            Shape();
        }

        void Squeeze(float loudness)
        {
            _squeezing = true;
            _variant = Random.Range(0, 3);
            _voice.pitch = Random.Range(0.94f, 1.08f);
            _voice.clip = SoundBank.ChickenSqueeze(_variant);
            _voice.volume = 0.9f * loudness;
            _voice.Play();
        }

        void Release()
        {
            if (!_squeezing) return;
            _squeezing = false;
            _voice.Stop();
            _fx.pitch = Random.Range(0.95f, 1.06f);
            _fx.PlayOneShot(SoundBank.ChickenRelease(_variant), 0.6f);
        }

        void Shape()
        {
            if (_skin && _shape >= 0) _skin.SetBlendShapeWeight(_shape, _squeeze * 100f);
        }

        void Update()
        {
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
            _fx.PlayOneShot(SoundBank.ChickenSqueeze(Random.Range(0, 3)), Mathf.Clamp01((v - 2f) / 5f) * 0.7f);
            _squeeze = 0.8f; Shape();
        }

        protected override void OnDropped() { Release(); }
    }
}
