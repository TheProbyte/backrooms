using System.Collections.Generic;
using Backrooms.Audio;
using Backrooms.Interaction;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Net
{
    /// <summary>
    /// Another player, as this machine sees them (co-op, 2026-11-04): the same MakeHuman body as your own, its shirt in
    /// their colour, posed from what their game sends twenty times a second. It is drawn a tenth of a second in the past,
    /// between the two updates either side (snapshot interpolation, as Source and most shooters do: never a jump, even when
    /// an update is late), guessing on from their speed for a moment if the updates stop.
    ///
    /// The pose is worked out here, procedurally (as your own body's is): the feet step along where they walk (two-bone IK
    /// to footholds that swing and lift with a gait cycle) so crouching bends the knees and the soles stay on the floor;
    /// the spine leans into a crouch or a run; the head turns with their view; the left hand holds the camcorder to the eye;
    /// the right hand goes where theirs is (on a door's lever, a box, what they hold) or hangs and swings. Sitting: the hips
    /// on the seat, the feet planted. Their footsteps are heard from their feet.
    /// <para>2026-11-09 (the owner: "improve animations ... crouching, crawling, visuals for multiplayer other players,
    /// laying down when getting out of the medbay"). What a walk cycle needs to stop looking stiff, as animators teach it
    /// and procedural systems do it: the hips sway over the standing foot and turn with the stride while the chest turns
    /// the other way; the body leans into speeding up, slowing and turning; the feet roll from heel to toe. Standing still:
    /// breathing and a slow shift of weight, and the feet stay planted, stepping round when the body turns on the spot
    /// instead of sliding. A real crouch (hips back and down, knees out). Crawling on hands and knees in a diagonal gait
    /// (a knee with the opposite hand), the shins along the floor. In the air: knees tucked. Sliding: one leg out in
    /// front. Lying down (on the return chute's belt and the cot): flat on the back, and sitting up from it.</para>
    /// A capsule (kinematic) stands where they are, so you bump into them and what you throw hits them.
    /// </summary>
    public sealed class RemotePlayer : MonoBehaviour
    {
        public struct State
        {
            public double t;
            public Vector3 pos;
            public float yaw, pitch, eye;
            public byte flags;
            public Vector3 vel;
            public Vector3 hand;          // the right hand's palm, in their frame (yaw only)
            public float handBlend;
            public Vector3 itemPos;       // what they hold, in their frame
            public Quaternion itemRot;
            public Vector3 seatHip;       // seated: the hips, in their frame
        }
        public const int FStance = 3, FSeated = 4, FNoclip = 8, FRun = 16, FGround = 32, FUse = 64, FSlide = 128;

        public int Peer { get; private set; }
        public State Current { get; private set; }
        public bool HasState => _buf.Count > 0;
        public Vector3 Eye => transform.position + Vector3.up * Mathf.Max(0.35f, _eye);
        public Vector3 LookDir => Quaternion.Euler(Current.pitch, Current.yaw, 0f) * Vector3.forward;
        public Transform HandBone => _handR ? _handR : transform;
        public Transform BackBone => _spine3 ? _spine3 : transform;
        /// <summary>What they hold, shown in their hand (an item made on this machine for them: NetWorld).</summary>
        [System.NonSerialized] public WorldItem held;
        /// <summary>A backpack on their back.</summary>
        [System.NonSerialized] public WorldItem worn;

        const double Delay = 0.1;
        const float BodyScale = 0.952f;
        /// <summary>The hazmat suit's eyes are at 1.513 m: made this much bigger, its eyes (and the camcorder) are at 1.58 m.</summary>
        const float HazmatScale = 1.044f;
        readonly List<State> _buf = new List<State>();
        double _offset; bool _hasOffset;

        Transform _body, _pelvis, _spine1, _spine2, _spine3, _neck, _head;
        Transform _thighL, _thighR, _calfL, _calfR, _footL, _footR;
        Transform _upperL, _upperR, _lowerL, _lowerR, _handL, _handR;
        readonly List<(Transform t, Quaternion rot, Vector3 pos)> _rest = new List<(Transform, Quaternion, Vector3)>();
        Quaternion _footLRest, _footRRest, _handLRest;
        float _pelvisRestY = 0.948f;
        Transform _camcorder;
        bool _camcorderAtEye;          // the model's origin is its eyecup (the Overflow look's model), not the old box's back
        float _palm;                   // wrist to the middle of the palm (the glove's grip)
        CapsuleCollider _cap;
        AudioSource _feet;
        readonly List<Material> _mats = new List<Material>();
        float _phase, _eye = 1.58f, _crouch, _run, _seat, _crawl, _speed, _handW;
        int _lastStepSide = -1, _stepPick;
        // The feet planted while standing (world), and a step in progress (0..1) from where it left.
        readonly Vector3[] _plant = new Vector3[2], _stepFrom = new Vector3[2];
        readonly float[] _stepT = { 1f, 1f };
        bool _planted;
        float _lie, _air, _slide, _idleT, _turn, _lastYaw, _accFwd, _accSide;
        Vector3 _lastVel;

        public static RemotePlayer Create(int peer, Transform parent)
        {
            var go = new GameObject("Player_" + NetSession.NameOf(peer));
            go.transform.SetParent(parent, false);
            var rp = go.AddComponent<RemotePlayer>();
            rp.Peer = peer;
            rp.Build();
            return rp;
        }

        // ---------------- Building ----------------

        void Build()
        {
            // The hazmat suit (rigged on the MakeHuman skeleton, so posed the same way), or the MakeHuman body.
            var hazmat = Resources.Load<GameObject>("Player/Hazmat/PlayerHazmatSuit");
            var prefab = hazmat ? hazmat : Resources.Load<GameObject>("Player/MPFB/PlayerMPFB");
            var shader = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (prefab)
            {
                _body = Instantiate(prefab, transform, false).transform;
                _body.name = "Body";
                // The model's eyes are at 1.66 m; the camera (and the camcorder) at the player's eye, 1.58 m: the body is made
                // that much smaller, or the camcorder is held in front of the mouth.
                _body.localScale = Vector3.one * (hazmat ? HazmatScale : BodyScale);
                var tint = NetSession.ColourOf(Peer);
                foreach (var r in _body.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool top = r.name.EndsWith(".Torso") || r.name.EndsWith(".ArmL") || r.name.EndsWith(".ArmR");
                    for (int i = 0; i < mats.Length; i++) mats[i] = hazmat ? HazmatMaterial(mats[i], shader) : Material(mats[i], shader, top ? tint : Color.white);
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    if (r is SkinnedMeshRenderer sk) { sk.updateWhenOffscreen = true; sk.quality = SkinQuality.Bone4; }
                }
                if (hazmat) SetupHazmat();
                _pelvis = Find("pelvis"); _spine1 = Find("spine_01"); _spine2 = Find("spine_02"); _spine3 = Find("spine_03");
                _neck = Find("neck_01"); _head = Find("head");
                _thighL = Find("thigh_l"); _thighR = Find("thigh_r"); _calfL = Find("calf_l"); _calfR = Find("calf_r"); _footL = Find("foot_l"); _footR = Find("foot_r");
                _upperL = Find("upperarm_l"); _upperR = Find("upperarm_r"); _lowerL = Find("lowerarm_l"); _lowerR = Find("lowerarm_r"); _handL = Find("hand_l"); _handR = Find("hand_r");
                foreach (var t in _body.GetComponentsInChildren<Transform>(true)) _rest.Add((t, t.localRotation, t.localPosition));
                if (_pelvis) _pelvisRestY = transform.InverseTransformPoint(_pelvis.position).y;
                if (_footL) _footLRest = Quaternion.Inverse(transform.rotation) * _footL.rotation;
                if (_footR) _footRRest = Quaternion.Inverse(transform.rotation) * _footR.rotation;
                if (_handL) _handLRest = Quaternion.Inverse(transform.rotation) * _handL.rotation;
                // The glove's grip (2026-11-06): from the wrist to the middle of the palm, six tenths of the way to the
                // knuckles (the suit's glove is big: about 7.5 cm), so what they hold sits in the palm, not at the wrist.
                var knuckle = Find("middle_01_r");
                if (_handR && knuckle) _palm = (knuckle.position - _handR.position).magnitude * 0.6f;
                Generation.PoolLitSwitch.Attach(_body.gameObject);
            }
            else
            {
                // No body model: a plain capsule in their colour.
                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Destroy(cap.GetComponent<Collider>());
                cap.transform.SetParent(transform, false);
                cap.transform.localPosition = Vector3.up * 0.9f; cap.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
                var m = new Material(shader); m.SetColor("_BaseColor", NetSession.ColourOf(Peer)); _mats.Add(m);
                cap.GetComponent<Renderer>().sharedMaterial = m;
            }
            _camcorder = MakeCamcorder(shader);
            // Something to bump into (and for thrown things to hit).
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false;
            _cap = gameObject.AddComponent<CapsuleCollider>();
            _cap.radius = 0.28f; _cap.height = 1.75f; _cap.center = Vector3.up * 0.875f;
            var fx = new GameObject("Footsteps");
            fx.transform.SetParent(transform, false);
            _feet = ItemModels.Source(fx, 18f);
            SoundBank.ApplySpatial(_feet, SoundIds.Footsteps);
        }

        Transform Find(string name)
        {
            foreach (var t in _body.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        Material Material(Material imported, Shader shader, Color tint)
        {
            string key = imported ? imported.name.Replace(" (Instance)", "") : "PlayerMPFB.body";
            string asset = key.StartsWith("PlayerMPFB.") ? key.Substring("PlayerMPFB.".Length) : key;
            var m = new Material(shader) { name = "Remote_" + key };
            if (m.HasProperty("_DetailAlbedoMapScale")) m.SetFloat("_DetailAlbedoMapScale", -1f);
            Texture tex = imported ? imported.mainTexture : null;
            if (!tex) tex = Resources.Load<Texture2D>("Player/MPFB/Textures/" + asset + "_diffuse");
            if (!tex) tex = Resources.Load<Texture2D>("Player/MPFB/Textures/" + asset);
            if (!tex && asset == "low-poly") tex = Resources.Load<Texture2D>("Player/MPFB/Textures/brown_eye");     // the eyes
            if (tex) m.SetTexture("_BaseMap", tex);
            bool shirt = asset.Contains("casualsuit");
            // Each player's shirt in their colour (the host's as it comes), so you can tell who is who: the dark stripes
            // stay, the cloth takes the colour.
            m.SetColor("_BaseColor", shirt && Peer != 0 && tint != Color.white ? tint * 1.7f : Color.white);
            var nrm = Resources.Load<Texture2D>("Player/MPFB/Textures/" + asset + "_normal");
            if (nrm && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Smoothness", asset == "body" ? 0.38f : asset.StartsWith("shoes") ? 0.25f : 0.12f);
            _mats.Add(m);
            return m;
        }

        readonly System.Collections.Generic.Dictionary<Material, Material> _hazmatMats = new System.Collections.Generic.Dictionary<Material, Material>();

        /// <summary>The suit's own material moved onto Backrooms/Lit (same properties), so the light grid lights it.</summary>
        Material HazmatMaterial(Material src, Shader shader)
        {
            if (!src) return src;
            if (_hazmatMats.TryGetValue(src, out var m)) return m;
            m = new Material(src) { name = "Remote_" + src.name };
            if (shader && src.shader && src.shader.name == "Universal Render Pipeline/Lit")
            {
                int queue = src.renderQueue;
                m.shader = shader;
                m.renderQueue = queue;
            }
            if (m.HasProperty("_DetailAlbedoMapScale")) m.SetFloat("_DetailAlbedoMapScale", -1f);
            _mats.Add(m);
            _hazmatMats[src] = m;
            return m;
        }

        /// <summary>
        /// Their suit's look: their name on the chest tape and their number on the back, and a face of their own (picked from
        /// their peer number until the look is chosen and sent).
        /// </summary>
        void SetupHazmat()
        {
            var ap = _body.GetComponent<StyleLab.HazmatAppearance>();
            if (!ap) return;
            var shader = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (ap.skinTones != null) for (int i = 0; i < ap.skinTones.Length; i++) ap.skinTones[i] = HazmatMaterial(ap.skinTones[i], shader);
            if (ap.hairTones != null) for (int i = 0; i < ap.hairTones.Length; i++) ap.hairTones[i] = HazmatMaterial(ap.hairTones[i], shader);
            ap.labelMaterial = HazmatMaterial(ap.labelMaterial, shader);
            _appearance = ap;
            ap.firstPerson = false;
            ApplyLook();
        }

        StyleLab.HazmatAppearance _appearance;

        /// <summary>Where their voice comes from (NetWorld.Voice): their head, or their middle until the body is up.</summary>
        public Transform VoiceAnchor => _head ? _head : transform;

        /// <summary>How loud they are speaking (0..1): the suit's mouth opens and closes with it (Overflow's suit only).</summary>
        public void SetTalk(float level) { if (_appearance) _appearance.talk = Mathf.Clamp01(level); }
        int _lookShown = -1;

        /// <summary>The look they chose (YOUR LOOK, sent with their name and when it changes), or one from their player
        /// number if they sent none.</summary>
        void ApplyLook()
        {
            if (!_appearance) return;
            int sent = NetSession.LookOf(Peer);
            var look = StyleLab.PlayerLook.Valid(sent) ? StyleLab.PlayerLook.Unpack(sent) : StyleLab.PlayerLook.FromPeer(Peer);
            look.ApplyTo(_appearance);
            _appearance.playerName = NetSession.NameOf(Peer);
            _appearance.Apply();
            _lookShown = sent;
        }

        Transform MakeCamcorder(Shader shader)
        {
            // The Overflow look: the lab's camcorder model, its eyecup at the eye (Camcorder.CamcorderModel).
            var model = Backrooms.Camcorder.CamcorderModel.Make(transform, "Camcorder", _mats, false);
            if (model) { _camcorderAtEye = true; return model; }
            var root = new GameObject("Camcorder").transform;
            root.SetParent(transform, false);
            var dark = new Material(shader) { name = "Remote_Camcorder" };
            dark.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.055f)); dark.SetFloat("_Smoothness", 0.45f);
            if (dark.HasProperty("_DetailAlbedoMapScale")) dark.SetFloat("_DetailAlbedoMapScale", -1f);
            _mats.Add(dark);
            var lens = new Material(shader) { name = "Remote_Lens" };
            lens.SetColor("_BaseColor", new Color(0.01f, 0.012f, 0.02f)); lens.SetFloat("_Smoothness", 0.9f);
            _mats.Add(lens);
            var red = new Material(shader) { name = "Remote_Tally" };
            red.SetColor("_BaseColor", new Color(0.9f, 0.05f, 0.03f));
            if (red.HasProperty("_EmissionColor")) { red.EnableKeyword("_EMISSION"); red.SetColor("_EmissionColor", new Color(3f, 0.1f, 0.05f)); }
            _mats.Add(red);
            void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler, Material m)
            {
                var go = GameObject.CreatePrimitive(type);
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(root, false);
                go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localEulerAngles = euler;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = m;
                r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            Part(PrimitiveType.Cube, new Vector3(-0.004f, -0.022f, 0.075f), new Vector3(0.072f, 0.082f, 0.19f), Vector3.zero, dark);        // body
            Part(PrimitiveType.Cylinder, new Vector3(0f, -0.006f, 0.185f), new Vector3(0.058f, 0.03f, 0.058f), new Vector3(90f, 0f, 0f), dark);
            Part(PrimitiveType.Cylinder, new Vector3(0f, -0.006f, 0.214f), new Vector3(0.046f, 0.002f, 0.046f), new Vector3(90f, 0f, 0f), lens);
            Part(PrimitiveType.Cube, new Vector3(0.002f, 0.028f, -0.01f), new Vector3(0.034f, 0.03f, 0.055f), Vector3.zero, dark);          // viewfinder
            Part(PrimitiveType.Cube, new Vector3(-0.044f, -0.012f, 0.07f), new Vector3(0.008f, 0.052f, 0.072f), Vector3.zero, dark);        // flip screen
            Part(PrimitiveType.Sphere, new Vector3(0.02f, 0.022f, 0.16f), new Vector3(0.007f, 0.007f, 0.007f), Vector3.zero, red);         // the red recording light
            Generation.PoolLitSwitch.Attach(root.gameObject);
            return root;
        }

        void OnDestroy() { foreach (var m in _mats) if (m) Destroy(m); }

        // ---------------- Updates from their game ----------------

        public void Push(State s)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double est = now - s.t;
            // Their clock against ours: the quickest arrival so far, drifting up slowly (clocks drift, routes change).
            if (!_hasOffset || est < _offset) { _offset = est; _hasOffset = true; }
            else _offset += (est - _offset) * 0.002;
            if (_buf.Count > 0 && s.t <= _buf[_buf.Count - 1].t) return;        // late (UDP): a newer one is here
            _buf.Add(s);
            if (_buf.Count == 1) { Current = s; transform.SetPositionAndRotation(s.pos, Quaternion.Euler(0f, s.yaw, 0f)); _eye = s.eye; }
            while (_buf.Count > 30) _buf.RemoveAt(0);
        }

        /// <summary>Put straight there (a teleport: no gliding across the map).</summary>
        public void Snap() { if (_buf.Count > 0) { var s = _buf[_buf.Count - 1]; _buf.Clear(); _buf.Add(s); Current = s; transform.position = s.pos; } }

        State Sample()
        {
            double rt = Time.realtimeSinceStartupAsDouble - _offset - Delay;
            var last = _buf[_buf.Count - 1];
            if (rt >= last.t)
            {
                // Late: guess on from their speed for a quarter of a second, then wait.
                var s = last;
                float ahead = Mathf.Min(0.25f, (float)(rt - last.t));
                s.pos += s.vel * ahead;
                return s;
            }
            for (int i = _buf.Count - 1; i > 0; i--)
            {
                var a = _buf[i - 1]; var b = _buf[i];
                if (rt < a.t) continue;
                float k = (float)((rt - a.t) / System.Math.Max(1e-6, b.t - a.t));
                // A jump of more than 4 m between updates is a teleport: no gliding through walls.
                if ((b.pos - a.pos).sqrMagnitude > 16f) return k < 0.5f ? a : b;
                var s = b;
                s.pos = Vector3.Lerp(a.pos, b.pos, k);
                s.yaw = Mathf.LerpAngle(a.yaw, b.yaw, k);
                s.pitch = Mathf.Lerp(a.pitch, b.pitch, k);
                s.eye = Mathf.Lerp(a.eye, b.eye, k);
                s.vel = Vector3.Lerp(a.vel, b.vel, k);
                s.hand = Vector3.Lerp(a.hand, b.hand, k);
                s.handBlend = Mathf.Lerp(a.handBlend, b.handBlend, k);
                s.itemPos = Vector3.Lerp(a.itemPos, b.itemPos, k);
                s.itemRot = Quaternion.Slerp(a.itemRot, b.itemRot, k);
                s.seatHip = Vector3.Lerp(a.seatHip, b.seatHip, k);
                // Old ones go.
                if (i > 2) _buf.RemoveRange(0, i - 2);
                return s;
            }
            return _buf[0];
        }

        // ---------------- Each frame ----------------

        void LateUpdate()
        {
            if (_appearance && NetSession.LookOf(Peer) != _lookShown) ApplyLook();      // they changed their look
            if (_buf.Count == 0) return;
            float dt = Time.deltaTime;
            var s = Sample();
            Current = s;
            transform.SetPositionAndRotation(s.pos, Quaternion.Euler(0f, s.yaw, 0f));
            bool noclip = (s.flags & FNoclip) != 0;
            if (_body) _body.gameObject.SetActive(!noclip);
            if (_camcorder) _camcorder.gameObject.SetActive(!noclip);
            if (_cap) _cap.enabled = !noclip;
            if (noclip) return;

            int stance = s.flags & FStance;
            bool seated = (s.flags & FSeated) != 0;
            _eye = Mathf.Lerp(_eye, s.eye, 1f - Mathf.Exp(-dt * 14f));
            float want = Mathf.Clamp01((1.58f - _eye) / (1.58f - 1.02f));
            _crouch = Mathf.MoveTowards(_crouch, stance == 2 || seated ? 0f : want, dt * 4f);
            _crawl = Mathf.MoveTowards(_crawl, stance == 2 ? 1f : 0f, dt * 2.5f);
            // Lying (the return chute's belt, the cot): seated with the eye almost on what they lie on.
            bool lying = seated && s.eye < 0.4f;
            _lie = Mathf.MoveTowards(_lie, lying ? 1f : 0f, dt * 2.2f);
            _seat = Mathf.MoveTowards(_seat, seated && !lying ? 1f : 0f, dt * 3f);
            _air = Mathf.MoveTowards(_air, seated || stance == 2 || (s.flags & FGround) != 0 ? 0f : 1f, dt * 5f);
            _slide = Mathf.MoveTowards(_slide, (s.flags & FSlide) != 0 ? 1f : 0f, dt * 5f);
            Vector3 flatVel = new Vector3(s.vel.x, 0f, s.vel.z);
            if (dt > 1e-5f)
            {
                // How fast they turn, and how they speed up or slow (in their own frame): the body leans with both.
                _turn = Mathf.Lerp(_turn, Mathf.DeltaAngle(_lastYaw, s.yaw) / dt, 1f - Mathf.Exp(-dt * 6f));
                Vector3 acc = transform.InverseTransformDirection((flatVel - _lastVel) / dt);
                _accFwd = Mathf.Lerp(_accFwd, Mathf.Clamp(acc.z, -8f, 8f), 1f - Mathf.Exp(-dt * 5f));
                _accSide = Mathf.Lerp(_accSide, Mathf.Clamp(acc.x, -8f, 8f), 1f - Mathf.Exp(-dt * 5f));
            }
            _lastYaw = s.yaw; _lastVel = flatVel;
            _speed = Mathf.Lerp(_speed, seated ? 0f : flatVel.magnitude, 1f - Mathf.Exp(-dt * 8f));
            _run = Mathf.MoveTowards(_run, (s.flags & FRun) != 0 ? 1f : 0f, dt * 3f);
            _handW = Mathf.MoveTowards(_handW, s.handBlend, dt * 6f);
            if (_cap) { float h = Mathf.Lerp(1.75f, 1.2f, _crouch); h = Mathf.Lerp(h, 0.62f, _crawl); h = Mathf.Lerp(h, 1.3f, _seat); h = Mathf.Lerp(h, 0.56f, _lie); _cap.height = h; _cap.center = Vector3.up * h * 0.5f; }

            if (_body) Pose(s, dt);
            PlaceCamcorder(s);
            PlaceHeld(s);
            // Their spray: the jet and the hiss from their can (the paint itself comes as paint).
            if (held is SprayCan can) can.RemoteSpray((s.flags & FUse) != 0, Eye, LookDir, dt);
        }

        void Pose(State s, float dt)
        {
            foreach (var (t, rot, pos) in _rest) { if (!t) continue; t.localRotation = rot; t.localPosition = pos; }
            var root = transform;
            Vector3 up = root.up, fwd = root.forward, right = root.right;
            Quaternion pelvisRest = _pelvis ? _pelvis.rotation : root.rotation;

            // The gait: a cycle a stride (two steps) long, faster with speed; crawling, two hand plants long.
            float stride = Mathf.Lerp(1.15f, 1.9f, _run) * Mathf.Lerp(1f, 0.6f, _crouch);
            float moving = Mathf.Clamp01(_speed / 0.4f);
            _phase = Mathf.Repeat(_phase + _speed * dt / Mathf.Max(0.3f, Mathf.Lerp(stride, 0.84f, _crawl)), 1f);
            float th = _phase * Mathf.PI * 2f;
            Vector3 localVel = root.InverseTransformDirection(new Vector3(s.vel.x, 0f, s.vel.z));
            Vector3 along = localVel.sqrMagnitude > 0.01f ? localVel.normalized : Vector3.forward;
            // Standing still: breathing, and the weight shifting slowly from one leg to the other.
            _idleT += dt;
            float upright = (1f - _crawl) * (1f - _lie);
            float idle = (1f - moving) * upright * (1f - _seat);
            float breath = Mathf.Sin(_idleT * 1.5f + Peer);
            float shift = Mathf.Sin(_idleT * 0.37f + Peer * 2.1f);

            // Hips: lower as they crouch (and back: a crouch sits back), swaying over the standing foot as they walk; on the
            // seat when sitting, low and level when crawling, on the mattress when lying.
            float bob = moving * 0.03f * Mathf.Abs(Mathf.Sin(th));
            float hipY = _pelvisRestY - 0.42f * _crouch - bob;
            float sway = Mathf.Cos(th) * 0.022f * moving * Mathf.Lerp(1f, 0.5f, _run) + shift * 0.014f * idle;
            Vector3 hip = root.position + up * hipY - fwd * (0.1f * _crouch) + right * sway;
            hip = Vector3.Lerp(hip, root.position + up * 0.36f - fwd * 0.14f, _slide);
            if (_seat > 0.001f)
            {
                Vector3 seatHip = root.TransformPoint(s.seatHip.sqrMagnitude > 0.001f ? s.seatHip : new Vector3(0f, Mathf.Max(0.42f, _eye - 0.78f), -0.35f));
                hip = Vector3.Lerp(hip, seatHip, _seat);
            }
            if (_crawl > 0.001f) hip = Vector3.Lerp(hip, root.position + up * 0.5f - fwd * 0.32f + right * (Mathf.Cos(th) * 0.02f * moving), _crawl);
            // Lying: the head is where they are (the pillow), the hips down the body from it.
            Vector3 lieHip = root.TransformPoint(s.seatHip.sqrMagnitude > 0.001f ? s.seatHip : new Vector3(0f, -0.05f, 0.55f));
            Vector3 spineDir = root.position - lieHip; spineDir.y *= 0.25f;
            spineDir = spineDir.sqrMagnitude > 1e-4f ? spineDir.normalized : -fwd;
            Vector3 lieRight = Vector3.Cross(spineDir, Vector3.up).normalized;
            if (_lie > 0.001f) hip = Vector3.Lerp(hip, lieHip, _lie);
            if (_pelvis) _pelvis.position = hip;

            // The hips turn with the stride (the hip of the leg swinging forward goes forward with it) and tip into a
            // crouch; the lean: into a crouch or a run, into speeding up (back when stopping), into a turn.
            float twist = Mathf.Sin(th) * Mathf.Lerp(5f, 9f, _run) * moving * upright * (1f - _seat);
            float lean = (26f * _crouch + (3f + 7f * _run) * moving + Mathf.Clamp(_accFwd * 1.5f, -6f, 8f) * (1f - _seat)) * upright - 4f * _seat - 24f * _slide;
            float roll = (Mathf.Clamp(-_turn * 0.02f * Mathf.Clamp01(_speed / 2f), -6f, 6f) + Mathf.Clamp(-_accSide * 1.2f, -5f, 5f)) * upright * (1f - _seat);
            if (_pelvis)
            {
                _pelvis.rotation = Quaternion.AngleAxis(twist, up) * Quaternion.AngleAxis(roll * 0.4f, fwd) * Quaternion.AngleAxis(8f * _crouch * upright - 10f * _slide, right) * _pelvis.rotation;
                // Crawling: the whole body tips forward onto its hands.
                if (_crawl > 0.001f) _pelvis.rotation = Quaternion.AngleAxis(78f * _crawl, right) * _pelvis.rotation;
                // Lying on the back: the spine along the mattress to the pillow, the face to the ceiling.
                if (_lie > 0.001f)
                {
                    var lieRot = Quaternion.LookRotation(Vector3.up, spineDir) * Quaternion.Inverse(root.rotation);
                    _pelvis.rotation = Quaternion.Slerp(_pelvis.rotation, lieRot * pelvisRest, _lie);
                }
            }

            // The spine: the lean, and the chest turning against the hips; breathing in the chest when still.
            float keep = 1f - _lie;
            if (_spine1) _spine1.rotation = Quaternion.AngleAxis(lean * 0.5f * keep, right) * Quaternion.AngleAxis(-twist * 0.6f, up) * Quaternion.AngleAxis(roll * 0.3f, fwd) * _spine1.rotation;
            if (_spine2) _spine2.rotation = Quaternion.AngleAxis((lean * 0.3f - breath * 0.9f * idle) * keep, right) * Quaternion.AngleAxis(-twist * 0.7f, up) * Quaternion.AngleAxis(roll * 0.3f, fwd) * _spine2.rotation;
            if (_spine3) _spine3.rotation = Quaternion.AngleAxis(-breath * 0.6f * idle * keep, right) * Quaternion.AngleAxis(-twist * 0.5f, up) * _spine3.rotation;
            // The head takes the rest of the view (and stays level through the hips' turning: the chest has undone it).
            float pitch = (Mathf.Clamp(s.pitch, -70f, 70f) - lean - 75f * _crawl) * keep;
            if (_neck) _neck.rotation = Quaternion.AngleAxis(pitch * 0.4f, right) * _neck.rotation;
            if (_head) _head.rotation = Quaternion.AngleAxis(pitch * 0.6f, right) * _head.rotation;

            // The legs.
            for (int side = 0; side < 2; side++)
            {
                var thigh = side == 0 ? _thighL : _thighR; var calf = side == 0 ? _calfL : _calfR; var foot = side == 0 ? _footL : _footR;
                if (!thigh || !calf || !foot) continue;
                float sx = side == 0 ? -1f : 1f;
                float thS = th + side * Mathf.PI;
                var footRest = root.rotation * (side == 0 ? _footLRest : _footRRest);
                if (_lie > 0.5f)
                {
                    // Lying: straight out along the mattress, the toes up.
                    Vector3 target = lieHip - spineDir * 0.84f + lieRight * (sx * 0.11f) + Vector3.up * 0.03f;
                    TwoBone(thigh, calf, foot, target, Vector3.up);
                    foot.rotation = Quaternion.LookRotation(Vector3.up, spineDir) * Quaternion.Inverse(root.rotation) * footRest;
                    _plant[side] = root.TransformPoint(new Vector3(sx * 0.11f, 0.073f, 0f)); _stepT[side] = 1f;
                    continue;
                }
                if (_crawl >= 0.5f)
                {
                    // Hands and knees: the knee on the floor under the hips, the shin lying back along the floor; each knee
                    // comes forward with the opposite hand.
                    var local = new Vector3(sx * 0.13f, 0.09f + Mathf.Max(0f, Mathf.Cos(thS)) * 0.03f * moving, -0.84f + Mathf.Sin(thS) * 0.13f * moving);
                    TwoBone(thigh, calf, foot, root.TransformPoint(local), fwd - up * 0.9f + right * (sx * 0.1f));
                    foot.rotation = Quaternion.AngleAxis(70f, right) * footRest;
                    _plant[side] = root.TransformPoint(new Vector3(sx * 0.11f, 0.073f, 0f)); _stepT[side] = 1f;
                    continue;
                }
                // Walking: footholds swinging along where they go, lifting on the way forward. Narrower at a run.
                float reach = 0.24f * stride * moving;
                float x = sx * 0.11f * Mathf.Lerp(1f, 1.45f, _crouch) * Mathf.Lerp(1f, 0.75f, _run * moving);
                float lift = Mathf.Max(0f, Mathf.Cos(thS));
                Vector3 cycleLocal = new Vector3(x, 0.073f, 0f) + along * (Mathf.Sin(thS) * reach);
                cycleLocal.y += lift * 0.11f * moving * Mathf.Lerp(1f, 1.5f, _run);
                Vector3 cycle = root.TransformPoint(cycleLocal);
                Vector3 home = root.TransformPoint(new Vector3(x, 0.073f, 0f));
                if (!_planted) _plant[side] = home;
                // Standing (or nearly): the foot stays where it is in the world. Left behind by a turn on the spot or a
                // shuffle, it steps to where it belongs, one foot at a time.
                if (moving > 0.5f || _seat > 0.3f || _air > 0.3f || _slide > 0.3f) { _plant[side] = cycle; _stepT[side] = 1f; }
                else
                {
                    if (_stepT[side] >= 1f)
                    {
                        var p = _plant[side]; p.y = home.y; _plant[side] = p;
                        Vector3 off = _plant[side] - home; off.y = 0f;
                        if (off.magnitude > 0.2f && _stepT[1 - side] >= 1f) { _stepFrom[side] = _plant[side]; _stepT[side] = 0f; }
                    }
                    if (_stepT[side] < 1f)
                    {
                        _stepT[side] = Mathf.Min(1f, _stepT[side] + dt / 0.26f);
                        float k = Mathf.SmoothStep(0f, 1f, _stepT[side]);
                        _plant[side] = Vector3.Lerp(_stepFrom[side], home, k) + up * (Mathf.Sin(k * Mathf.PI) * 0.07f);
                        if (_stepT[side] >= 1f && (s.flags & FGround) != 0) Step(home, s);
                    }
                }
                Vector3 goal = Vector3.Lerp(_plant[side], cycle, Mathf.Clamp01(moving * 2f));
                // In the air: the knees come up. Sliding: one leg out in front, the other tucked. Sitting: planted.
                goal += (up * 0.14f + fwd * 0.06f) * _air;
                if (_slide > 0.001f) goal = Vector3.Lerp(goal, root.TransformPoint(side == 1 ? new Vector3(0.1f, 0.09f, 0.8f) : new Vector3(-0.16f, 0.073f, 0.26f)), _slide);
                if (_seat > 0.001f) goal = Vector3.Lerp(goal, root.TransformPoint(new Vector3(x * 1.2f, 0.073f, 0.12f)), _seat);
                Vector3 knee = fwd + right * (sx * (0.15f + 0.45f * _crouch)) + up * 0.2f * _seat;
                TwoBone(thigh, calf, foot, goal, knee);
                // The foot rolls: toes down as it pushes off behind, toes up as it comes down in front.
                float rollFoot = lift > 0f ? Mathf.Lerp(22f, -14f, (Mathf.Sin(thS) + 1f) * 0.5f) * Mathf.Min(1f, lift * 3f) * moving * (1f - _seat) : 0f;
                foot.rotation = Quaternion.AngleAxis(rollFoot + 30f * _air, right) * footRest;
                // A step lands as the foot comes down.
                if (moving > 0.3f && (s.flags & FGround) != 0 && _seat < 0.5f)
                {
                    float c = Mathf.Cos(thS);
                    if (c < -0.85f && _lastStepSide != side) { _lastStepSide = side; Step(foot.position, s); }
                }
            }
            _planted = true;

            // The left hand holds the camcorder to the eye.
            Vector3 eye = root.position + up * _eye;
            var look = Quaternion.Euler(s.pitch, s.yaw, 0f);
            if (_upperL && _lowerL && _handL)
            {
                Vector3 grip = eye + look * (_camcorderAtEye ? new Vector3(-0.035f, -0.165f, 0.17f) : new Vector3(-0.045f, -0.07f, 0.16f));   // (under the Overflow camcorder's body)
                TwoBone(_upperL, _lowerL, _handL, grip, -right * 0.6f - up + fwd * 0.2f);
                _handL.rotation = look * _handLRest;
            }
            // The right hand: where theirs is, else hanging and swinging against the right leg (higher and wider at a run,
            // out a little in the air); crawling, planted on the floor ahead, in step with the left knee; lying, by the side.
            if (_upperR && _lowerR && _handR)
            {
                float swing = Mathf.Sin(th);
                Vector3 hang = root.TransformPoint(new Vector3(0.24f + 0.04f * _run * moving, Mathf.Lerp(0.86f, 0.62f, _crouch) + _seat * 0.1f + (Mathf.Max(0f, swing) * 0.05f + 0.1f * _run) * moving,
                                                               0.05f + swing * Mathf.Lerp(0.16f, 0.3f, _run) * moving));
                hang += (up * 0.18f + right * 0.1f) * _air;
                if (_slide > 0.001f) hang = Vector3.Lerp(hang, root.TransformPoint(new Vector3(0.3f, 0.12f, -0.25f)), _slide);      // a hand down behind, steadying
                if (_crawl > 0.5f) hang = root.TransformPoint(new Vector3(0.2f, 0.05f + Mathf.Max(0f, Mathf.Cos(th)) * 0.06f * moving, 0.45f + swing * 0.14f * moving));
                if (_lie > 0.5f) hang = lieHip + lieRight * 0.3f + spineDir * 0.08f + Vector3.up * 0.02f;
                Vector3 grip = root.TransformPoint(s.hand);
                // The palm (not the wrist) to the grip: the wrist short of it along the arm's reach.
                Vector3 reach = grip - _upperR.position;
                if (reach.sqrMagnitude > 1e-4f) grip -= reach.normalized * _palm;
                Vector3 target = Vector3.Lerp(hang, grip, Mathf.Clamp01(_handW));
                TwoBone(_upperR, _lowerR, _handR, target, right * 0.6f - up + -fwd * 0.2f);
            }
        }

        /// <summary>Two-bone IK: a (shoulder or hip), b (elbow or knee), c (hand or foot) put to reach the target, the middle
        /// joint bending toward the hint (law of cosines in the plane of the target and the hint).</summary>
        static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
        {
            Vector3 A = a.position;
            float la = (b.position - A).magnitude, lb = (c.position - b.position).magnitude;
            if (la < 1e-4f || lb < 1e-4f) return;
            Vector3 to = target - A;
            float d = Mathf.Clamp(to.magnitude, Mathf.Abs(la - lb) + 0.01f, (la + lb) * 0.999f);
            Vector3 dir = to.sqrMagnitude > 1e-8f ? to.normalized : Vector3.down;
            Vector3 n = Vector3.Cross(dir, hint);
            if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(dir, Vector3.forward);
            n.Normalize();
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            Vector3 upper = Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, n) * dir;
            a.rotation = Quaternion.FromToRotation(b.position - A, upper) * a.rotation;
            Vector3 B = b.position;
            Vector3 end = A + dir * d;
            b.rotation = Quaternion.FromToRotation(c.position - B, end - B) * b.rotation;
        }

        void PlaceCamcorder(State s)
        {
            if (!_camcorder) return;
            var look = Quaternion.Euler(s.pitch, s.yaw, 0f);
            Vector3 eye = transform.position + Vector3.up * _eye;
            _camcorder.SetPositionAndRotation(eye + look * (_camcorderAtEye ? new Vector3(0f, 0f, 0.035f) : new Vector3(-0.02f, -0.02f, 0.11f)), look);
        }

        void PlaceHeld(State s)
        {
            if (held)
            {
                if (held.transform.parent != transform) held.transform.SetParent(transform, true);
                held.transform.SetPositionAndRotation(transform.TransformPoint(s.itemPos), transform.rotation * s.itemRot);
            }
            if (worn && _spine3)
            {
                if (worn.transform.parent != _spine3) worn.transform.SetParent(_spine3, true);
                // On the back: behind the shoulder blades, upright with the body.
                worn.transform.SetPositionAndRotation(_spine3.position - transform.forward * 0.2f - transform.up * 0.08f, transform.rotation * Quaternion.Euler(0f, 180f, 0f));
            }
        }

        // ---------------- Their footsteps ----------------

        void Step(Vector3 at, State s)
        {
            if (!_feet) return;
            const string id = SoundIds.Footsteps;
            var floor = Surfaces.At(at, transform);
            int pick = _stepPick++;
            AudioClip clip;
            float k = 1f;
            switch (floor)
            {
                case ProceduralSounds.Floor.Water: clip = SoundBank.WaterStep(pick % Mathf.Max(1, SoundBank.WaterStepCount)); k = 1.1f; break;
                case ProceduralSounds.Floor.Carpet: clip = SoundBank.Step(pick % Mathf.Max(1, SoundBank.StepCount)); break;
                default: clip = SoundBank.HardStep(floor, pick % 6); k = 1.2f; break;
            }
            if (!clip) return;
            float heavy = (s.flags & FRun) != 0 ? 1.25f : (s.flags & FStance) == 1 ? 0.55f : 0.8f;
            _feet.transform.position = at + Vector3.up * 0.03f;
            _feet.pitch = Random.Range(0.94f, 1.06f);
            _feet.PlayOneShot(clip, SoundBank.BaseLevel(id) * SoundCatalog.Volume(id) * heavy * k);
        }
    }
}
