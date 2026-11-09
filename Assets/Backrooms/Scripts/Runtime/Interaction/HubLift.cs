using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The service lift between the world and the hub (2026-11-07, Docs/HubPlan.md). Two identical freight cabs, one in the
    /// hub's shaft and one in the shaft at the north of the world's spawn hall (HubPlan.HubCab, WorldCab). Each end always has
    /// a cab waiting, open: nobody waits for the lift to come back, and anyone can go on their own or with the others.
    /// Press the button in a cab and its gate and doors shut; everyone inside rides. The cab at the far end shuts too while
    /// they arrive (so nobody pops in before anyone's eyes), then opens with them in it; the cab they left opens again, empty.
    ///
    /// The ride (10 s): the motor, a tremble, and halfway the odd moment, after the Severance lift: the lens widens while the
    /// view dollies the other way (what is a metre off stays its size, the cab stretches round it), the picture bends and
    /// squeezes in slow waves, the hum warbles down to one low tone. The two cabs are the same, so the move from one to the
    /// other at the same place in it, during the moment, is not seen.
    ///
    /// Co-op: the buttons are shared (Net: Poke), so every player's game runs the same lift; each moves its own player when
    /// they are in the cab that leaves. Cab frame: origin at the middle of the gate on the floor, +z out of the cab.
    /// </summary>
    public class HubLift : MonoBehaviour
    {
        public WorldStreamer streamer;
        public static HubLift Instance { get; private set; }

        public float rideTime = 10f, gateTime = 1.0f, doorTime = 1.2f;

        enum Phase { Open, Closing, Closed, Opening }
        const int World = 0, Hub = 1;
        const float CabHalfW = 1.5f, CabDepth = 2.1f, CabH = 2.6f, GateW = 2.8f, DoorH = 2.48f;
        const float MomentAt = 3.6f, MomentLen = 2.8f;
        /// <summary>How far round the world's lift is loaded before the swap (m): about what the streamer keeps round a player.</summary>
        const float FarRadius = 36f;

        class Cab
        {
            public Transform root;
            public Pose frame;
            public Transform[] bars;
            public Transform doorL, doorR;
            public BoxCollider gateBlock;
            public ElevatorButton inside, call;
            public Light light;
            public Renderer panel;
            public AudioSource motor, fan, doors, ding, tone;
            // Its own state: the doors, a ride leaving from it, riders on their way into it.
            public Phase phase = Phase.Open;
            public float gate = 1f, doors01 = 1f, rideT;
            public bool leaving, swapped, localRider;
            public int arriving;
            // The floor dial beside the doors (HubLiftDial): its needle, and where it points when the lift is here.
            public Transform needle;
            public float own;
        }

        readonly Cab[] _cabs = new Cab[2];
        float _moment, _lastBob;
        CamcorderPlayerController _player;
        static Material _steel, _steelDark, _floorMat, _panelOn, _panelOff, _btnOn, _btnOff, _plate, _paint, _wood;
        static AudioClip _toneClip;
        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; Release(); }
        void OnDisable() => Release();

        void Release()
        {
            CamcorderRig.FovScale = 1f; CamcorderRig.Dolly = 0f; CamcorderRig.LensWarp = 0f; CamcorderRig.LensStretch = 0f;
            if (streamer) { streamer.PreloadHub(false); streamer.ClearSite(this); }
            Generation.RayTracedLighting.ClearPrewarm();
        }

        void Start()
        {
            EnsureMaterials();
            ElevatorSounds.Make();
            if (!_toneClip) _toneClip = MakeTone();
            _cabs[World] = Build("ServiceLift_World", HubPlan.WorldCab);
            _cabs[Hub] = Build("ServiceLift_Hub", HubPlan.HubCab);
            Pose();
        }

        // ---------------- Building ----------------

        static Material Mat(string name, Color c, float metallic, float smooth, Color? emission = null)
        {
            var m = new Material(Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            return m;
        }

        static void EnsureMaterials()
        {
            if (_steel) return;
            _steel = Mat("ServiceLift_Steel", new Color(0.56f, 0.56f, 0.54f), 0.35f, 0.4f);
            _steelDark = Mat("ServiceLift_SteelDark", new Color(0.24f, 0.23f, 0.22f), 0.4f, 0.35f);
            _floorMat = Mat("ServiceLift_Floor", new Color(0.2f, 0.19f, 0.17f), 0.3f, 0.3f);
            _panelOn = Mat("ServiceLift_LightOn", new Color(0.92f, 0.95f, 0.86f), 0f, 0.2f, new Color(2.4f, 2.5f, 2.1f));
            _panelOff = Mat("ServiceLift_LightOff", new Color(0.55f, 0.55f, 0.5f), 0f, 0.2f);
            _btnOn = Mat("ServiceLift_ButtonOn", new Color(1f, 0.7f, 0.25f), 0f, 0.5f, new Color(3.2f, 1.7f, 0.35f));
            _btnOff = Mat("ServiceLift_ButtonOff", new Color(0.55f, 0.5f, 0.42f), 0f, 0.5f, new Color(0.08f, 0.06f, 0.03f));
            _plate = Mat("ServiceLift_Plate", new Color(0.13f, 0.12f, 0.11f), 0.5f, 0.4f);
            _paint = Mat("ServiceLift_Doors", new Color(0.55f, 0.36f, 0.1f), 0.2f, 0.35f);         // company amber, gone brown
            _wood = Mat("ServiceLift_Bumper", new Color(0.36f, 0.24f, 0.14f), 0f, 0.25f);
        }

        static Transform Part(Transform parent, string name, Vector3 pos, Vector3 size, Material m, bool collide, bool cast = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.renderingLayerMask = uint.MaxValue;              // lit by every room's lights (the shaft's, the hall's through the gate)
            if (!collide) Destroy(go.GetComponent<Collider>());
            return go.transform;
        }

        static ElevatorButton Button(Transform parent, string name, Vector3 pos, Quaternion rot, System.Action onPress)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos; root.transform.localRotation = rot;
            Part(root.transform, "Plate", new Vector3(0f, 0f, 0.008f), new Vector3(0.12f, 0.24f, 0.016f), _plate, false, false);
            var lamp = Part(root.transform, "Lamp", new Vector3(0f, 0.03f, 0.02f), new Vector3(0.06f, 0.06f, 0.014f), _btnOff, false, false);
            var trig = root.AddComponent<BoxCollider>();
            trig.isTrigger = true; trig.center = new Vector3(0f, 0f, 0.04f); trig.size = new Vector3(0.22f, 0.3f, 0.12f);
            var b = root.AddComponent<ElevatorButton>();
            b.Init(lamp.GetComponent<Renderer>(), _btnOn, _btnOff);
            b.onPress = onPress;
            return b;
        }

        AudioSource Sound(Transform parent, string name, Vector3 at, float range, AudioClip clip, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            var a = ItemModels.Source(go, range);
            a.loop = loop; a.clip = clip; a.volume = 0f; a.minDistance = 1f;
            Audio.SoundRoute.Add(a);
            return a;
        }

        /// <summary>
        /// The world's cab goes wherever the current contract's floor is (LeasePlan.Current, its lift in the floor's middle):
        /// moved while nobody rides it (it is out of sight at the far end then; the hub's cab is where the players are).
        /// </summary>
        void FollowFloor()
        {
            var c = _cabs[World];
            var want = HubPlan.WorldCab;
            if ((c.frame.position - want.position).sqrMagnitude < 0.01f) return;
            if (c.leaving || c.arriving > 0) return;                   // (riding, or on its way)
            if (_player && Inside(World, _player.transform.position)) return;
            c.frame = want;
            c.root.SetPositionAndRotation(want.position, want.rotation);
        }

        Cab Build(string name, Pose frame)
        {
            var c = new Cab { frame = frame };
            c.root = new GameObject(name).transform;
            c.root.SetParent(transform, false);
            c.root.SetPositionAndRotation(frame.position, frame.rotation);
            var t = c.root;
            float zc = -0.12f - CabDepth * 0.5f;
            Part(t, "Floor", new Vector3(0f, 0.015f, zc), new Vector3(CabHalfW * 2f, 0.03f, CabDepth), _floorMat, true, false);
            Part(t, "Back", new Vector3(0f, CabH * 0.5f, -0.12f - CabDepth - 0.03f), new Vector3(CabHalfW * 2f + 0.12f, CabH, 0.06f), _steel, true);
            Part(t, "WallL", new Vector3(-(CabHalfW + 0.03f), CabH * 0.5f, zc), new Vector3(0.06f, CabH, CabDepth), _steel, true);
            Part(t, "WallR", new Vector3(CabHalfW + 0.03f, CabH * 0.5f, zc), new Vector3(0.06f, CabH, CabDepth), _steel, true);
            Part(t, "Ceiling", new Vector3(0f, CabH + 0.025f, zc), new Vector3(CabHalfW * 2f + 0.12f, 0.05f, CabDepth + 0.1f), _steelDark, true);
            Part(t, "Header", new Vector3(0f, (DoorH + CabH) * 0.5f, -0.1f), new Vector3(CabHalfW * 2f + 0.12f, CabH - DoorH, 0.06f), _steelDark, false);
            // Wooden bumpers along the sides and back (a freight car's), dented and dark.
            for (int s = -1; s <= 1; s += 2)
                foreach (float y in new[] { 0.45f, 1.0f })
                    Part(t, "Bumper", new Vector3(s * (CabHalfW - 0.02f), y, zc), new Vector3(0.04f, 0.16f, CabDepth - 0.2f), _wood, false);
            foreach (float y in new[] { 0.45f, 1.0f })
                Part(t, "BumperBack", new Vector3(0f, y, -0.12f - CabDepth + 0.02f), new Vector3(CabHalfW * 2f - 0.2f, 0.16f, 0.04f), _wood, false);
            c.panel = Part(t, "LightPanel", new Vector3(0f, CabH - 0.01f, zc), new Vector3(1.2f, 0.02f, 0.6f), _panelOn, false, false).GetComponent<Renderer>();
            var lg = new GameObject("CabLight");
            lg.transform.SetParent(t, false);
            lg.transform.localPosition = new Vector3(0f, CabH - 0.3f, zc);
            c.light = lg.AddComponent<Light>();
            c.light.type = LightType.Point; c.light.range = 5f; c.light.intensity = 2.6f; c.light.color = new Color(0.93f, 1f, 0.86f); c.light.shadows = LightShadows.None;
            lg.AddComponent<UniversalAdditionalLightData>().renderingLayers = 0xFFFFFFFFu;

            // The folding gate: upright bars across the cab's mouth, gathered to the right when open.
            c.bars = new Transform[15];
            for (int i = 0; i < c.bars.Length; i++)
                c.bars[i] = Part(t, "GateBar", new Vector3(0f, 1.18f, -0.2f), new Vector3(0.03f, 2.3f, 0.03f), _steel, false);
            Part(t, "GateRail", new Vector3(0f, 2.36f, -0.2f), new Vector3(GateW + 0.1f, 0.05f, 0.05f), _steelDark, false);
            c.gateBlock = c.root.gameObject.AddComponent<BoxCollider>();
            c.gateBlock.center = new Vector3(0f, 1.2f, -0.2f); c.gateBlock.size = new Vector3(GateW, 2.4f, 0.06f);
            // The landing doors, in the wall's opening, sliding apart into it.
            c.doorL = Part(t, "LandingDoorL", new Vector3(-GateW * 0.25f, DoorH * 0.5f, 0f), new Vector3(GateW * 0.5f + 0.01f, DoorH - 0.01f, 0.05f), _paint, true);
            c.doorR = Part(t, "LandingDoorR", new Vector3(GateW * 0.25f, DoorH * 0.5f, 0f), new Vector3(GateW * 0.5f + 0.01f, DoorH - 0.01f, 0.05f), _paint, true);
            // An orange, gold and avocado stripe across the doors, the company's, faded.
            foreach (var d in new[] { c.doorL, c.doorR })
            {
                var stripe = new GameObject("Stripe").transform;
                stripe.SetParent(d, false);
                Part(stripe, "Or", new Vector3(0f, -0.07f, 0.52f), new Vector3(1f, 0.02f, 0.1f), Mat("Stripe_Or", new Color(0.72f, 0.33f, 0.12f), 0f, 0.3f), false, false);
                Part(stripe, "Go", new Vector3(0f, -0.05f, 0.52f), new Vector3(1f, 0.02f, 0.1f), Mat("Stripe_Go", new Color(0.8f, 0.6f, 0.2f), 0f, 0.3f), false, false);
                Part(stripe, "Av", new Vector3(0f, -0.03f, 0.52f), new Vector3(1f, 0.02f, 0.1f), Mat("Stripe_Av", new Color(0.42f, 0.46f, 0.18f), 0f, 0.3f), false, false);
            }
            int side = HubPlan.InHub(frame.position) ? Hub : World;
            c.call = Button(t, "CallButton", new Vector3(GateW * 0.5f + 0.35f, 1.15f, OfficeBiome.WallThickness * 0.5f + 0.005f), Quaternion.identity, () => Call(side));
            c.inside = Button(t, "CabButton", new Vector3(CabHalfW - 0.01f, 1.2f, -0.7f), Quaternion.LookRotation(Vector3.left, Vector3.up), () => CabPressed(side));

            // The floor dial beside the doors (opposite the call button): HUB on its left, L0 on its right; the needle swings
            // across while a cab travels to or from here.
            var dial = FittingModels.Spawn("HubLiftDial", t, 0xFFFFFFFFu, new Vector3(-(GateW * 0.5f + 0.42f), 1.72f, OfficeBiome.WallThickness * 0.5f + 0.002f), Quaternion.identity);
            if (dial)
            {
                foreach (var r in dial.GetComponentsInChildren<Renderer>()) r.renderingLayerMask = uint.MaxValue;
                var pivot = new GameObject("Needle").transform;
                pivot.SetParent(dial, false);
                pivot.localPosition = new Vector3(0f, 0.05f, 0.032f);
                Part(pivot, "Hand", new Vector3(0f, 0.09f, 0f), new Vector3(0.012f, 0.18f, 0.006f), _steelDark, false, false);
                c.needle = pivot;
            }
            c.own = side == Hub ? -60f : 60f;                    // (the needle's turn as the dial is drawn: HUB left)

            c.motor = Sound(t, "Motor", new Vector3(0f, CabH + 0.2f, zc), 20f, ElevatorSounds.Motor, true);
            c.fan = Sound(t, "Fan", new Vector3(0f, CabH - 0.1f, zc), 5f, ElevatorSounds.Fan, true);
            c.doors = Sound(t, "Doors", new Vector3(0f, 1.5f, 0.05f), 14f, ElevatorSounds.DoorRun, true);
            c.ding = Sound(t, "Chime", new Vector3(0f, DoorH + 0.3f, 0.1f), 24f, null, false);
            c.tone = Sound(t, "Tone", new Vector3(0f, 1.6f, zc), 6f, _toneClip, true);
            return c;
        }

        /// <summary>The odd moment's sound: one low, steady tone with a slow beat in it.</summary>
        static AudioClip MakeTone()
        {
            const int rate = 22050; int n = rate * 2;
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                d[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * 55f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 55.5f * t) + 0.12f * Mathf.Sin(2f * Mathf.PI * 110f * t);
            }
            var clip = AudioClip.Create("LiftTone", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        // ---------------- Control ----------------

        /// <summary>The call button: a cab left shut (nothing on its way) opens.</summary>
        void Call(int side)
        {
            var c = _cabs[side];
            if (c.phase == Phase.Closed && !c.leaving && c.arriving == 0) c.phase = Phase.Opening;
        }

        /// <summary>The button in a cab: it shuts and everyone in it goes to the other end.</summary>
        void CabPressed(int side)
        {
            var c = _cabs[side];
            // Down from the hub only with a contract taken (Docs/ContractFloorsPlan.md): nowhere to go without one. With
            // work orders on the board (2026-11-08) the floor is always yours to go down to (to clock in, put up shelves,
            // light them), until closing.
            var cs = Contracts.Instance;
            bool orders = WorkOrders.Active && cs && !cs.Closed;
            if (side == Hub && cs && !cs.HasFloor && !orders && !c.leaving)
            {
                Camcorder.CamcorderOSD.Notice("NO JOB TAKEN - SEE THE BOARD");
                return;
            }
            if (side == Hub) FollowFloor();                              // (a contract taken a moment ago: its floor's cab)
            if (c.leaving || c.arriving > 0 || !(c.phase == Phase.Open || c.phase == Phase.Opening)) return;
            c.leaving = true; c.swapped = false; c.rideT = -1f; c.phase = Phase.Closing;
            var far = _cabs[1 - side];
            far.arriving++;
            if (far.phase == Phase.Open || far.phase == Phase.Opening) far.phase = Phase.Closing;     // a cab on its way there
        }

        /// <summary>A point in a cab's frame.</summary>
        Vector3 Local(int side, Vector3 world) => Quaternion.Inverse(_cabs[side].frame.rotation) * (world - _cabs[side].frame.position);

        bool Inside(int side, Vector3 world)
        {
            var p = Local(side, world);
            return Mathf.Abs(p.x) < CabHalfW - 0.05f && p.z < -0.25f && p.z > -0.12f - CabDepth && p.y > -0.6f && p.y < CabH;
        }

        void Update()
        {
            if (_cabs[0] == null) return;
            if (!_player) _player = FindAnyObjectByType<CamcorderPlayerController>();
            FollowFloor();
            float dt = Time.deltaTime;
            _moment = 0f;
            for (int s = 0; s < 2; s++) Step(s, dt);
            Warp();
            Sounds(dt);
            Pose();
        }

        void Step(int side, float dt)
        {
            var c = _cabs[side];
            switch (c.phase)
            {
                case Phase.Closing:
                    if (c.gate > 0f) c.gate = Mathf.MoveTowards(c.gate, 0f, dt / gateTime);
                    else c.doors01 = Mathf.MoveTowards(c.doors01, 0f, dt / doorTime);
                    if (c.doors01 <= 0f) { c.phase = Phase.Closed; Thunk(side); }
                    break;
                case Phase.Opening:
                    if (c.doors01 < 1f) c.doors01 = Mathf.MoveTowards(c.doors01, 1f, dt / doorTime);
                    else c.gate = Mathf.MoveTowards(c.gate, 1f, dt / gateTime);
                    if (c.gate >= 1f) c.phase = Phase.Open;
                    break;
            }
            if (c.leaving && c.phase == Phase.Closed) Ride(side, dt);
        }

        void Ride(int side, float dt)
        {
            var c = _cabs[side];
            int to = 1 - side;
            var far = _cabs[to];
            if (c.rideT < 0f)
            {
                c.rideT = 0f;
                c.localRider = _player && Inside(side, _player.transform.position);
                if (streamer && c.localRider)
                {
                    // The far end loads during the first half of the ride: the hub's chunks, or the world round its lift as far
                    // as the player will see from it (not just the lift lobby: the rest streaming in after the doors opened,
                    // and the rays building it all at once, were the ride's stutter, 2026-11-07, Docs/ContractFloorsPlan.md).
                    if (to == Hub) streamer.PreloadHub(true);
                    else streamer.SetSite(this, HubPlan.WorldCab.position, FarRadius);
                }
            }
            float swapAt = MomentAt + MomentLen * 0.5f;
            // Once the far end is all in, the rays' scene there is built up, a little a frame (not as each chunk arrives: a
            // chunk's upload and its structures in the same frame were 50-120 ms frames).
            bool farLoaded = !streamer || (to == Hub ? streamer.HubReady : streamer.SiteReady(HubPlan.WorldCab.position, FarRadius - 6f));
            if (c.localRider && !c.swapped && farLoaded) Generation.RayTracedLighting.Prewarm(_cabs[to].frame.position);
            // At the swap the far cab must be shut, its own riders (if it is leaving too) gone, and (for whoever rides) its end
            // loaded and the rays' scene there built.
            bool farReady = far.phase == Phase.Closed && !(far.leaving && !far.swapped)
                            && (!c.localRider || (farLoaded && Generation.RayTracedLighting.PrewarmReady));
            if (!c.swapped && c.rideT + dt >= swapAt && !farReady) c.rideT = swapAt - 0.0001f;   // held in the moment
            else c.rideT += dt;
            if (!c.swapped && c.rideT >= swapAt) Swap(side, to);
            if (c.localRider)
            {
                float u = Mathf.InverseLerp(MomentAt, MomentAt + MomentLen, c.rideT);
                float m = u > 0f && u < 1f ? Mathf.SmoothStep(0f, 1f, Mathf.Sin(u * Mathf.PI)) : 0f;
                _moment = Mathf.Max(_moment, m);
                if (_player)
                {
                    // A cab on its rails: a slow sway and a tremble, felt in the camera.
                    float bob = (Mathf.PerlinNoise(Time.time * 7f, 0.3f) - 0.5f) * 0.006f * RideSpeed(c.rideT);
                    _player.platformMotion += Vector3.up * (bob - _lastBob);
                    _lastBob = bob;
                }
            }
            if (c.rideT >= rideTime)
            {
                c.leaving = false; c.localRider = false; _lastBob = 0f;
                c.phase = Phase.Opening;                               // the next cab, empty
                far.arriving = Mathf.Max(0, far.arriving - 1);
                if (far.arriving == 0 && !far.leaving) { far.phase = Phase.Opening; Chime(to); }
                if (streamer) { streamer.PreloadHub(false); streamer.ClearSite(this); }
                Generation.RayTracedLighting.ClearPrewarm();
            }
        }

        float RideSpeed(float rideT)
        {
            float t = Mathf.Clamp01(rideT / rideTime);
            return 30f * t * t * (1f - t) * (1f - t) / 1.875f;          // a smootherstep's speed: none at either end
        }

        /// <summary>Riders and their things move to the other cab, at the same place in it.</summary>
        void Swap(int from, int to)
        {
            var c = _cabs[from];
            c.swapped = true;
            var a = c.frame; var b = _cabs[to].frame;
            var turn = b.rotation * Quaternion.Inverse(a.rotation);
            Vector3 Map(Vector3 p) => b.position + turn * (p - a.position);
            // Loose things in the cab (an item, a box, a cart), then the player with what they hold.
            var seen = new HashSet<Rigidbody>();
            var cols = Physics.OverlapBox(a.position + a.rotation * new Vector3(0f, 1.2f, -0.12f - CabDepth * 0.5f), new Vector3(CabHalfW, 1.2f, CabDepth * 0.5f), a.rotation, ~0, QueryTriggerInteraction.Ignore);
            foreach (var col in cols)
            {
                var rb = col.attachedRigidbody;
                if (!rb || !seen.Add(rb)) continue;
                if (_player && rb.transform.IsChildOf(_player.transform)) continue;
                var item = rb.GetComponent<WorldItem>();
                if (item && item.Held) continue;
                if (rb.GetComponentInParent<Net.RemotePlayer>()) continue;          // another player moves themselves
                rb.position = Map(rb.position); rb.rotation = turn * rb.rotation;
                rb.transform.SetPositionAndRotation(rb.position, rb.rotation);
                if (!rb.isKinematic) { rb.linearVelocity = turn * rb.linearVelocity; rb.angularVelocity = turn * rb.angularVelocity; }
            }
            if (c.localRider && _player)
            {
                _player.platformMotion -= Vector3.up * _lastBob; _lastBob = 0f;
                _player.Warp(Map(_player.transform.position), turn.eulerAngles.y);
                Physics.SyncTransforms();
            }
            if (streamer && to == World && c.localRider) { streamer.PreloadHub(false); streamer.ClearSite(this); }
        }

        /// <summary>
        /// The odd moment, on the picture: the lens widens while the camera dollies forward to keep what is a metre off the same
        /// size (a dolly zoom: the room stretches round it), with a slow warble in the zoom, the barrel bending in and out and
        /// the picture squeezed one way then the other. Nothing breaks up: it is the lens, not the tape.
        /// </summary>
        void Warp()
        {
            float m = _moment;
            if (m <= 0.0001f) { CamcorderRig.FovScale = 1f; CamcorderRig.Dolly = 0f; CamcorderRig.LensWarp = 0f; CamcorderRig.LensStretch = 0f; return; }
            float t = Time.time;
            float fovScale = 1f + 0.55f * m + 0.06f * m * Mathf.Sin(t * 2f * Mathf.PI * 0.7f);
            var cam = Camera.main;
            if (cam)
            {
                // Keep a subject a metre off its size: d * (1 - tan(f0/2) / tan(f1/2)) forward (no further than the cab allows).
                float baseFov = cam.fieldOfView / Mathf.Max(0.01f, CamcorderRig.FovScale);
                float f0 = baseFov * Mathf.Deg2Rad * 0.5f, f1 = Mathf.Min(150f, baseFov * fovScale) * Mathf.Deg2Rad * 0.5f;
                float want = 1.0f * (1f - Mathf.Tan(f0) / Mathf.Max(1e-3f, Mathf.Tan(f1)));
                // How far there is to go before the gate or a wall (not counting the player's own body and what they hold).
                float room = 2f;
                int n = Physics.RaycastNonAlloc(cam.transform.position, cam.transform.forward, s_hits, 2f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var h = s_hits[i];
                    if (_player && h.collider.transform.IsChildOf(_player.transform)) continue;
                    var it = h.collider.GetComponentInParent<WorldItem>();
                    if (it && it.Held) continue;
                    room = Mathf.Min(room, Mathf.Max(0f, h.distance - 0.35f));
                }
                CamcorderRig.Dolly = Mathf.Min(want, room);
            }
            CamcorderRig.FovScale = fovScale;
            CamcorderRig.LensWarp = m * (0.18f + 0.22f * Mathf.Sin(t * 2f * Mathf.PI * 0.45f));
            CamcorderRig.LensStretch = m * 0.35f * Mathf.Sin(t * 2f * Mathf.PI * 0.33f + 0.8f);
        }

        void Thunk(int side) { var d = _cabs[side].ding; if (d) { d.volume = 1f; d.PlayOneShot(ElevatorSounds.DoorShut, 0.55f); } }

        void Chime(int side)
        {
            var s = _cabs[side].ding;
            if (!s) return;
            s.volume = 1f;
            s.PlayOneShot(ElevatorSounds.Ding, 0.6f);
        }

        void Sounds(float dt)
        {
            float warble = 1f + 0.07f * _moment * Mathf.Sin(Time.time * 2f * Mathf.PI * 0.8f);
            for (int s = 0; s < 2; s++)
            {
                var c = _cabs[s];
                bool moving = c.leaving && c.phase == Phase.Closed && c.rideT >= 0f;
                bool arriving = c.arriving > 0 && c.phase == Phase.Closed;
                float speed = moving ? RideSpeed(c.rideT) : 0f;
                bool doorsMoving = (c.phase == Phase.Opening || c.phase == Phase.Closing) && c.doors01 > 0f && c.doors01 < 1f;
                float m = c.localRider ? _moment : 0f;
                Fade(c.motor, moving ? (0.08f + 0.4f * speed) * (1f - 0.7f * m) : arriving ? 0.05f : 0f, dt * 0.8f);
                if (c.motor) c.motor.pitch = (0.7f + 0.4f * speed) * (c.localRider ? warble : 1f);
                Fade(c.fan, 0.08f * GameSettings.Ambience, dt);          // (the building's sound: AMBIENCE)
                Fade(c.doors, doorsMoving ? 0.3f : 0f, dt * 2.5f);
                Fade(c.tone, moving ? 0.5f * m : 0f, dt * 2f);
                if (c.tone) c.tone.pitch = 1f / Mathf.Max(0.5f, warble);
            }
        }

        static void Fade(AudioSource a, float want, float step)
        {
            if (!a) return;
            a.volume = Mathf.MoveTowards(a.volume, want, step);
            if (a.volume > 0.001f && !a.isPlaying) a.Play();
            else if (a.volume <= 0.001f && a.isPlaying) a.Stop();
        }

        void Pose()
        {
            for (int s = 0; s < 2; s++)
            {
                var c = _cabs[s];
                float k = c.doors01 * c.doors01 * (3f - 2f * c.doors01);
                float slide = k * (GateW * 0.5f + 0.05f);
                c.doorL.localPosition = new Vector3(-GateW * 0.25f - slide, DoorH * 0.5f, 0f);
                c.doorR.localPosition = new Vector3(GateW * 0.25f + slide, DoorH * 0.5f, 0f);
                // Gate bars: spread across the mouth when shut, gathered against the right-hand wall when open.
                for (int i = 0; i < c.bars.Length; i++)
                {
                    float shut = -GateW * 0.5f + GateW * i / (c.bars.Length - 1);
                    float open = GateW * 0.5f - 0.32f + 0.32f * i / (c.bars.Length - 1);
                    c.bars[i].localPosition = new Vector3(Mathf.Lerp(shut, open, c.gate), 1.18f, -0.2f);
                }
                c.gateBlock.enabled = c.gate < 0.7f;
                float dim = c.localRider ? 1f - 0.5f * _moment : 1f;
                c.light.intensity = 2.6f * dim;
                c.panel.sharedMaterial = dim > 0.6f ? _panelOn : _panelOff;
                c.call.SetLit(c.phase != Phase.Open && (c.arriving > 0 || c.phase == Phase.Opening));
                if (c.needle)
                {
                    // Here: at this end's floor. Leaving: over to the other's. A cab on its way in: from there back to here.
                    var far = _cabs[1 - s];
                    float a = c.own;
                    if (c.leaving && c.rideT >= 0f) a = Mathf.Lerp(c.own, -c.own, Mathf.SmoothStep(0f, 1f, c.rideT / rideTime));
                    else if (c.arriving > 0 && far.leaving && far.rideT >= 0f) a = Mathf.Lerp(-c.own, c.own, Mathf.SmoothStep(0f, 1f, far.rideT / rideTime));
                    bool moving = (c.leaving && c.rideT >= 0f) || (c.arriving > 0 && far.leaving && far.rideT >= 0f);
                    if (moving) a += Mathf.Sin(Time.time * 23f) * 1.2f;                       // a tremble as it goes
                    c.needle.localRotation = Quaternion.Euler(0f, 0f, a);
                }
                c.inside.SetLit(c.leaving);
            }
            Physics.SyncTransforms();
        }

        // ---------------- For tests ----------------

        public bool Moving => _cabs[0] != null && (_cabs[0].leaving || _cabs[1].leaving);
        public void PressCab(bool hub) => CabPressed(hub ? Hub : World);
        public float Moment => _moment;
    }
}
