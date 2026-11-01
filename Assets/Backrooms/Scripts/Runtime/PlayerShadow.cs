using Backrooms.Camcorder;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms
{
    /// <summary>
    /// The player's shadow with no body shown (2026-10-31, the owner, through a queued task: "a player shadow with no visible
    /// body"). A stand-in of rough capsules (head, torso, legs, the left arm holding the camcorder up, the right arm down, the
    /// camcorder) that never draws itself, only its shadow (ShadowCastingMode.ShadowsOnly). It bobs and sways a little in
    /// step with the walk (or the wading clock), changes shape crouching and crawling, and leans the way the camera points.
    ///  - Level 0: the shadow-casting lights take it as they are.
    ///  - The Poolrooms have no shadow maps: the same capsules go to the shaders each frame (_PoolCapA / _PoolCapB) and the
    ///    traced sun is tested against them (PoolCommon.hlsl, PoolCapsuleSun: soft, as Unreal's capsule shadows), so tiles,
    ///    water and things there get it too.
    /// Only while no body is shown (PropGrabber.bodyMode 2; the bodies cast their own), and Graphics > Player shadow on.
    /// </summary>
    public class PlayerShadow : MonoBehaviour
    {
        const int Count = 8;          // head, torso, two legs, two upper arms ... (see Pose)
        static Mesh s_capsule;
        static readonly int IdA = Shader.PropertyToID("_PoolCapA"), IdB = Shader.PropertyToID("_PoolCapB"), IdN = Shader.PropertyToID("_PoolCapCount");

        CamcorderPlayerController _player;
        Interaction.PropGrabber _grab;
        Transform _root;
        readonly Transform[] _parts = new Transform[Count];
        readonly MeshRenderer[] _renderers = new MeshRenderer[Count];
        readonly Vector3[] _a = new Vector3[Count], _b = new Vector3[Count];
        readonly float[] _r = new float[Count];
        readonly Vector4[] _ga = new Vector4[Count], _gb = new Vector4[Count];
        float _phase, _bob, _lean;
        Material _mat;

        void Awake()
        {
            _player = GetComponent<CamcorderPlayerController>();
            if (!s_capsule)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                s_capsule = go.GetComponent<MeshFilter>().sharedMesh;
                Destroy(go);
            }
            var sh = Shader.Find("Backrooms/Lit");             // (it has a shadow caster pass; the stand-in never draws itself)
            _mat = new Material(sh) { name = "Player shadow", hideFlags = HideFlags.DontSave };
            _root = new GameObject("PlayerShadow").transform;
            _root.SetParent(transform, false);
            for (int i = 0; i < Count; i++)
            {
                var p = new GameObject("Part" + i);
                p.transform.SetParent(_root, false);
                p.AddComponent<MeshFilter>().sharedMesh = s_capsule;
                var r = p.AddComponent<MeshRenderer>();
                r.sharedMaterial = _mat;
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.renderingLayerMask = uint.MaxValue;
                _parts[i] = p.transform; _renderers[i] = r;
            }
        }

        void OnDestroy()
        {
            if (_mat) Destroy(_mat);
            Shader.SetGlobalFloat(IdN, 0f);
        }

        void LateUpdate()
        {
            if (!_grab) _grab = GetComponent<Interaction.PropGrabber>() ?? FindAnyObjectByType<Interaction.PropGrabber>();
            var gc = GraphicsController.Instance;
            bool on = (gc == null || gc.Options == null || gc.Options.playerShadow) && (!_grab || _grab.Bodiless) && _player && !_player.noclip;
            if (_root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
            if (!on) { Shader.SetGlobalFloat(IdN, 0f); return; }
            Pose();
            for (int i = 0; i < Count; i++) Place(_parts[i], _a[i], _b[i], _r[i]);
            // The Poolrooms' shaders: the same capsules, world space (a.xyz, radius), (b.xyz).
            for (int i = 0; i < Count; i++) { _ga[i] = new Vector4(_a[i].x, _a[i].y, _a[i].z, _r[i]); _gb[i] = _b[i]; }
            Shader.SetGlobalVectorArray(IdA, _ga);
            Shader.SetGlobalVectorArray(IdB, _gb);
            Shader.SetGlobalFloat(IdN, Count);
        }

        /// <summary>Where each capsule is now (world).</summary>
        void Pose()
        {
            var t = transform;
            var cam = Camera.main;
            Vector3 up = Vector3.up, fwd = t.forward, right = t.right, foot = t.position;
            float speed = _player.PlanarVelocity.magnitude, dt = Time.deltaTime;
            // The step clock: the wading one in water, else one of our own at a walk's pace.
            float stride;
            if (_player.Wade > 0.5f) stride = _player.WadePhase * 0.5f;
            else { _phase += speed * dt / (_player.IsRunning ? 1.05f : 0.72f) * 0.5f; stride = _phase; }
            float s = Mathf.Sin(stride * Mathf.PI * 2f), c2 = Mathf.Abs(Mathf.Cos(stride * Mathf.PI * 2f));
            float moving = Mathf.Clamp01(speed / Mathf.Max(0.1f, _player.walkSpeed));
            _bob = Mathf.Lerp(_bob, (c2 - 0.5f) * 0.03f * moving, 1f - Mathf.Exp(-dt / 0.05f));
            // Leaning the way the camera points (forward looking down, back a little looking up).
            _lean = Mathf.Lerp(_lean, Mathf.Clamp(_player.Pitch * 0.25f, -6f, 14f), 1f - Mathf.Exp(-dt / 0.15f));
            Vector3 eye = cam ? cam.transform.position : foot + up * 1.6f;
            float eyeH = Mathf.Max(0.3f, eye.y - foot.y);

            if (_player.CurrentStance == CamcorderPlayerController.Stance.Crawl)
            {
                // On hands and knees: the body along the floor, legs trailing, the head up front.
                float y = Mathf.Clamp(eyeH - 0.1f, 0.3f, 0.6f);
                var chest = foot + fwd * 0.2f + up * y; var hip = foot - fwd * 0.35f + up * (y - 0.05f);
                Set(0, eye - fwd * 0.05f + up * 0.02f, eye - fwd * 0.05f + up * 0.02f, 0.11f);       // head
                Set(1, hip, chest, 0.15f);                                                              // torso
                Set(2, hip + right * 0.1f, foot - fwd * (0.75f - 0.08f * s) + right * 0.12f + up * 0.08f, 0.07f);
                Set(3, hip - right * 0.1f, foot - fwd * (0.75f + 0.08f * s) - right * 0.12f + up * 0.08f, 0.07f);
                Set(4, chest - right * 0.17f, foot + fwd * (0.35f + 0.08f * s) - right * 0.2f + up * 0.04f, 0.045f);
                Set(5, chest + right * 0.17f, foot + fwd * (0.35f - 0.08f * s) + right * 0.2f + up * 0.04f, 0.045f);
                Set(6, eye + fwd * 0.12f - up * 0.06f, eye + fwd * 0.3f - up * 0.06f, 0.05f);        // the camcorder
                Set(7, chest - right * 0.17f, eye + fwd * 0.12f - up * 0.08f, 0.04f);                // its strap / arm
                return;
            }
            // Standing or crouching: proportions from the eye's height (a crouch bends the legs).
            float scale = Mathf.Clamp(eyeH / 1.6f, 0.55f, 1.1f);
            float hipY = Mathf.Lerp(0.5f, 0.92f, Mathf.InverseLerp(0.85f, 1.6f, eyeH)) + _bob;
            var lean = Quaternion.AngleAxis(_lean, right);
            Vector3 hipC = foot + up * hipY;
            Vector3 Lean(Vector3 p) => hipC + lean * (p - hipC);
            Vector3 neck = Lean(foot + up * (eyeH - 0.2f + _bob) - fwd * 0.05f);
            Vector3 head = Lean(foot + up * (eyeH - 0.02f + _bob) - fwd * 0.07f);
            Set(0, head, head, 0.11f);
            Set(1, hipC + up * 0.05f, neck - up * 0.08f, 0.16f);
            // Legs: from the hips to the feet, a small swing with the step (a crouch puts the knees out front).
            float swing = 0.13f * moving * s, knee = 1f - Mathf.InverseLerp(0.95f, 1.5f, eyeH);
            Set(2, hipC + right * 0.1f, foot + right * 0.12f + fwd * (swing + 0.25f * knee) + up * 0.06f, 0.075f);
            Set(3, hipC - right * 0.1f, foot - right * 0.12f + fwd * (-swing + 0.25f * knee) + up * 0.06f, 0.075f);
            // The left hand holds the camcorder up at the eye; the right arm hangs and swings a little.
            Vector3 shoulderL = neck - right * 0.19f - up * 0.05f, shoulderR = neck + right * 0.19f - up * 0.05f;
            Vector3 cam0 = cam ? cam.transform.position : eye, camF = cam ? cam.transform.forward : fwd;
            Vector3 hand = cam0 - camF * 0.06f - up * 0.06f - (cam ? cam.transform.right : right) * 0.04f;
            Set(4, shoulderL, hand, 0.045f);
            Set(5, shoulderR, shoulderR - up * 0.58f * scale - fwd * (0.1f * moving * s) + right * 0.03f, 0.045f);
            Set(6, cam0 - camF * 0.12f, cam0 + camF * 0.08f, 0.055f);
            Set(7, hand, hand, 0.05f);
        }

        void Set(int i, Vector3 a, Vector3 b, float r) { _a[i] = a; _b[i] = b; _r[i] = r; }

        /// <summary>The unit capsule (2 tall, 0.5 radius) stretched between two points.</summary>
        static void Place(Transform part, Vector3 a, Vector3 b, float r)
        {
            var d = b - a;
            float len = d.magnitude;
            part.position = (a + b) * 0.5f;
            part.rotation = len > 1e-4f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity;
            part.localScale = new Vector3(r * 2f, (len + 2f * r) * 0.5f, r * 2f);
        }
    }
}
