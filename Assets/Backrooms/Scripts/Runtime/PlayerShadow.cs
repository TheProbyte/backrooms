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
    ///  - The Poolrooms have no shadow maps: the same capsules go to the shaders each frame (_PoolCapsA / _PoolCapsB) and the
    ///    traced sun is tested against them (PoolCommon.hlsl, PoolCapsuleSun: soft, as Unreal's capsule shadows), so tiles,
    ///    water and things there get it too.
    /// Only while no body is shown (PropGrabber.bodyMode 2; the bodies cast their own), and Graphics > Player shadow on.
    ///
    /// 2026-10-31, the owner: "my shadow looks odd when passing by because its not animated". The real body now casts it
    /// whenever it is up (FirstPersonBody, None: drawn into the shadows only, walking, crouching and reaching as it does),
    /// and the Poolrooms' capsules follow its bones (FirstPersonBody.ShadowCapsules) with the camcorder added. This
    /// stand-in is left for when the body is hidden (crawling) or is not the MakeHuman one.
    ///
    /// 2026-11-01, the owner: the shadow "looks disjointed and not connected". In the Poolrooms the body's capsules are
    /// replaced by the body itself: each frame its renderers are drawn, as the sun sees them, into a small map of their own
    /// (PlayerShadowMap.shader, 256 texels over 2.4 m, as Unreal's per-object shadows give a character its own map), which
    /// PoolCommon.hlsl tests (PoolBodySun, soft as PCSS). The capsules remain for the stand-in.
    /// </summary>
    [DefaultExecutionOrder(110)]                // after FirstPersonBody (100) has posed the bones this frame
    public class PlayerShadow : MonoBehaviour
    {
        const int Count = 8;          // head, torso, two legs, two upper arms ... (see Pose)
        const int Max = 12;           // the shaders' arrays (PoolCommon.hlsl)
        static Mesh s_capsule;
        // (Named apart from the first version's 8-long arrays: Unity keeps a global array's first size for the session.)
        static readonly int IdA = Shader.PropertyToID("_PoolCapsA"), IdB = Shader.PropertyToID("_PoolCapsB"), IdN = Shader.PropertyToID("_PoolCapsN");
        static readonly int IdMap = Shader.PropertyToID("_PoolBodyMap"), IdMtx = Shader.PropertyToID("_PoolBodyMtx"), IdParams = Shader.PropertyToID("_PoolBodyParams");
        const int MapSize = 256;
        const float MapWidth = 2.4f;
        RenderTexture _map;
        Material _mapMat;
        CommandBuffer _cmd;
        bool _mapDue;

        CamcorderPlayerController _player;
        Interaction.PropGrabber _grab;
        Transform _root;
        readonly Transform[] _parts = new Transform[Count];
        readonly MeshRenderer[] _renderers = new MeshRenderer[Count];
        readonly Vector3[] _a = new Vector3[Max], _b = new Vector3[Max];
        readonly float[] _r = new float[Max];
        readonly Vector4[] _ga = new Vector4[Max], _gb = new Vector4[Max];
        Interaction.FirstPersonBody _body;
        float _nextFind;
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

        void OnEnable() => RenderPipelineManager.beginContextRendering += DrawMap;

        void OnDisable()
        {
            RenderPipelineManager.beginContextRendering -= DrawMap;
            Shader.SetGlobalVector(IdParams, Vector4.zero);
        }

        void OnDestroy()
        {
            if (_mat) Destroy(_mat);
            if (_mapMat) Destroy(_mapMat);
            if (_map) { _map.Release(); Destroy(_map); }
            _cmd?.Release();
            Shader.SetGlobalFloat(IdN, 0f);
            Shader.SetGlobalVector(IdParams, Vector4.zero);
        }

        /// <summary>
        /// Readies the body's map for this frame, centred on its middle and seen along the sun (u, v across the sun's rays,
        /// w toward the sun). False if it cannot be made (then the capsules are used).
        /// </summary>
        bool PrepareMap(Vector3 centre)
        {
            if (!_mapMat)
            {
                var sh = Shader.Find("Hidden/Backrooms/PlayerShadowMap");
                if (!sh || !sh.isSupported) return false;
                _mapMat = new Material(sh) { name = "Player shadow map", hideFlags = HideFlags.DontSave };
            }
            if (!_map)
            {
                _map = new RenderTexture(MapSize, MapSize, 0, RenderTextureFormat.RHalf) { name = "Player shadow map", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                if (!_map.Create()) { Destroy(_map); _map = null; return false; }
            }
            var L = Generation.PoolBiome.SunDirection;
            var u = Vector3.Cross(Vector3.up, L).normalized;
            var v = Vector3.Cross(L, u);
            float k = 1f / MapWidth;
            var m = Matrix4x4.identity;
            m.SetRow(0, new Vector4(u.x * k, u.y * k, u.z * k, -Vector3.Dot(centre, u) * k + 0.5f));
            m.SetRow(1, new Vector4(v.x * k, v.y * k, v.z * k, -Vector3.Dot(centre, v) * k + 0.5f));
            m.SetRow(2, new Vector4(L.x, L.y, L.z, -Vector3.Dot(centre, L)));
            m.SetRow(3, new Vector4(0f, 0f, 0f, 1f));
            Shader.SetGlobalMatrix(IdMtx, m);
            Shader.SetGlobalTexture(IdMap, _map);
            Shader.SetGlobalVector(IdParams, new Vector4(1f, MapWidth, 0f, 0f));
            _mapDue = true;
            return true;
        }

        /// <summary>Before the frame's cameras: the body's renderers into its map (after this frame's skinning).</summary>
        void DrawMap(ScriptableRenderContext ctx, System.Collections.Generic.List<Camera> cams)
        {
            if (!_mapDue || !_map || !_mapMat || !_body) return;
            _mapDue = false;
            _cmd ??= new CommandBuffer { name = "Player shadow map" };
            _cmd.Clear();
            _cmd.SetRenderTarget(_map);
            _cmd.ClearRenderTarget(false, true, new Color(-100f, 0f, 0f, 0f));
            foreach (var r in _body.ShadowRenderers)
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy || r.shadowCastingMode == ShadowCastingMode.Off) continue;
                if (r is not SkinnedMeshRenderer && r is not MeshRenderer) continue;
                int subs = r.sharedMaterials.Length;
                for (int s = 0; s < subs; s++) _cmd.DrawRenderer(r, _mapMat, s, 0);
            }
            ctx.ExecuteCommandBuffer(_cmd);
        }

        void LateUpdate()
        {
            if (!_grab) _grab = GetComponent<Interaction.PropGrabber>() ?? FindAnyObjectByType<Interaction.PropGrabber>();
            var gc = GraphicsController.Instance;
            bool on = (gc == null || gc.Options == null || gc.Options.playerShadow) && (!_grab || _grab.Bodiless) && _player && !_player.noclip;
            if (on && !_body && Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 1f;
                _body = GetComponentInChildren<Interaction.FirstPersonBody>();
                if (!_body) _body = FindAnyObjectByType<Interaction.FirstPersonBody>();
            }
            // The body itself casts the shadow when it can; its bones give the Poolrooms' capsules.
            int n = on && _body ? _body.ShadowCapsules(_a, _b, _r) : 0;
            if (n > 0)
            {
                var cam = Camera.main;
                if (cam && n < Max)
                {
                    var c = cam.transform;
                    _a[n] = c.position - c.forward * 0.12f; _b[n] = c.position + c.forward * 0.08f; _r[n] = 0.055f; n++;
                }
            }
            bool stand = on && n == 0;
            if (_root.gameObject.activeSelf != stand) _root.gameObject.SetActive(stand);
            if (!on) { Shader.SetGlobalFloat(IdN, 0f); Shader.SetGlobalVector(IdParams, Vector4.zero); return; }
            // The body itself into its own map for the Poolrooms' sun: then no capsules.
            if (n > 0 && PrepareMap((_a[1] + _b[1]) * 0.5f)) { Shader.SetGlobalFloat(IdN, 0f); return; }
            Shader.SetGlobalVector(IdParams, Vector4.zero);
            if (stand)
            {
                Pose();
                for (int i = 0; i < Count; i++) Place(_parts[i], _a[i], _b[i], _r[i]);
                n = Count;
            }
            // The Poolrooms' shaders: the capsules, world space (a.xyz, radius), (b.xyz).
            for (int i = 0; i < Max; i++) { _ga[i] = i < n ? new Vector4(_a[i].x, _a[i].y, _a[i].z, _r[i]) : Vector4.zero; _gb[i] = i < n ? (Vector4)_b[i] : Vector4.zero; }
            Shader.SetGlobalVectorArray(IdA, _ga);
            Shader.SetGlobalVectorArray(IdB, _gb);
            Shader.SetGlobalFloat(IdN, n);
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
