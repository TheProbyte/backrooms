using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A small desk lamp with its own cord (<see cref="CordLength"/>) and plug: the first light you can carry.
    ///  - Holding it, click a wall outlet (or an extension cord's box) within the cord's reach: its plug goes in.
    ///  - Click anywhere else: the switch on its base.
    ///  - Q sets it down upright where you look. Walk off with it further than its cord and the plug tears out (a snap
    ///    and a jerk of the hand).
    ///  - Its cord stays wound up behind its base until you need it: clicking an outlet with the lamp lets it out, and so
    ///    does picking the bundle up (you then hold the plug). Right mouse with the lamp in hand winds it back up.
    /// It lights (a warm, shadowed spot out of its shade) when switched on and its plug is in a socket with power.
    /// Model: Resources/Items/DeskLamp.fbx.
    /// </summary>
    public class DeskLamp : PowerUser, IHandGrip
    {
        public const float CordLength = 1.8f;
        static readonly Vector3 Bulb = new Vector3(0f, 0.3231f, 0.1032f);
        static readonly Vector3 ShadeDir = new Vector3(0f, -0.6428f, 0.766f);
        static readonly Vector3 CordExit = new Vector3(0.007f, 0.0029f, -0.12f);
        public static readonly Color CordColor = new Color(0.04f, 0.04f, 0.04f);

        [NotTuned] public bool switchOn = true;
        [Tooltip("Furthest outlet you can plug into from your eye (m).")] public float reach = 2.2f;

        Plug _plug;
        Cord _cord;
        Light _light;
        Renderer _bulbRenderer;
        int _bulbIndex = -1;
        Material _bulbOff;
        AudioSource _fx;
        float _level;

        public override Plug Plug => _bundled ? null : _plug;
        bool _bundled = true;
        GameObject _bundle;
        // The bundle model's lead ends at (0.0032, 0.0027, 0.0439) from its origin (as imported: Blender's X is mirrored),
        // heading off toward +Z and a little -X: turned and placed so it runs on from the end of the cord stub modelled on
        // the base (end about (-0.0063, 0.003, -0.119), heading back and a little -X), overlapping it by 3 mm.
        static readonly Vector3 BundleAt = new Vector3(-0.0242f, 0f, -0.1563f);
        static readonly Quaternion BundleTurn = Quaternion.Euler(0f, 20.8f, 0f);
        public bool Lit => switchOn && PluggedLive;
        public override bool PlaceOnDrop => true;
        public override Vector3 HoldOffset => new Vector3(0.2f, -0.35f, 0.4f);
        public override Vector3 UseOffset => HoldOffset;
        // Round the lower rod like a handshake: the palm on its right, fingers forward round its front, thumb round the back;
        // the model's lower rod stands at (0, y, -0.035), 9 mm round, from the base to its knuckle at y 0.165; the upper
        // rod leans from there to the shade's pivot at (0, 0.38, 0.05). (The grip and the contact used to be a stem 3-5 cm
        // in front of the real rod: ring and pinky closed on nothing.)
        static readonly Vector3 RodLow = new Vector3(0f, 0.035f, -0.035f), RodKnuckle = new Vector3(0f, 0.165f, -0.035f), RodTop = new Vector3(0f, 0.37f, 0.05f);
        // (1.8 cm up the rod from where it was: the little finger came down onto the base and was bent straight back.)
        public override Vector3 GripLocal => new Vector3(0.0115f, 0.118f, -0.035f);
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(new Vector3(0f, 0f, 1f), Vector3.right);
        public override float GripCurl => 0.95f;
        // A thin rod: past a fist's curl, so the middle and end joints go on wrapping it until they touch (at a fist's curl
        // they stopped short: a loose C round the rod, knuckles 34-52 and middle joints 42-65 degrees).
        public float FingerCurl(int finger) => 1.35f;
        public float ThumbCurl => 0.85f;
        public float ThumbOpposition => 0.75f;
        /// <summary>
        /// The hinge's knob where the rods meet: a drum 13.5 mm round across the lamp (x -23.5 to 16 mm), measured on the
        /// model. (The index fingertip went 7-8 mm into it: drawn, but not in the contact.)
        /// </summary>
        static float Knob(Vector3 p)
        {
            Vector3 q = p - new Vector3(-0.00375f, 0.165f, -0.035f);
            float radial = new Vector2(q.y, q.z).magnitude - 0.0135f, axial = Mathf.Abs(q.x) - 0.01975f;
            return Mathf.Min(Mathf.Max(radial, axial), 0f) + new Vector2(Mathf.Max(radial, 0f), Mathf.Max(axial, 0f)).magnitude;
        }
        /// <summary>The fingers close round the lower rod (and stop on the knob, the upper rod and the base).</summary>
        public override float ContactDistance(Vector3 p) =>
            Mathf.Min(Mathf.Min(Mathf.Min(SegmentDistance(p, RodLow, RodKnuckle) - 0.0095f, SegmentDistance(p, RodKnuckle, RodTop) - 0.008f),
                                Knob(p)),
                      BoxDistance(p, new Vector3(0f, 0.015f, 0f), new Vector3(0.075f, 0.015f, 0.075f)));
        public override float Amount { get => switchOn ? 1f : 0f; set => switchOn = value > 0.5f; }
        public override string Status
        {
            get
            {
                if (_bundled || !_plug || !_plug.Plugged) return "lamp: click an outlet to plug in (right mouse: wind the cord)";
                if (!PluggedLive) return "lamp: no power";
                return switchOn ? "lamp: on (click: switch)" : "lamp: off (click: switch)";
            }
        }

        public static DeskLamp Create(Vector3 position, Quaternion rotation)
        {
            var go = ItemModels.Body("DeskLamp", position, rotation, 1.6f, "DeskLamp", new Vector3(0.15f, 0.38f, 0.17f), "LampBody", false, new Vector3(0f, 0.19f, 0.02f));
            var rb = go.GetComponent<Rigidbody>();
            rb.centerOfMass = new Vector3(0f, 0.035f, 0f);          // the weighted base keeps it upright
            rb.angularDamping = 2f;
            var lamp = go.AddComponent<DeskLamp>();
            lamp.itemId = "desklamp"; lamp.displayName = "Desk lamp";
            Generation.PoolLitSwitch.Attach(go);
            return lamp;
        }

        protected override void Awake()
        {
            base.Awake();
            _fx = ItemModels.Source(gameObject, 8f);
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // its own light comes from inside the shade
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].name == "Item_LampBulb") { _bulbRenderer = r; _bulbIndex = i; _bulbOff = mats[i]; }
            }
            var lg = new GameObject("Light");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = Bulb + ShadeDir * 0.02f;
            lg.transform.localRotation = Quaternion.LookRotation(ShadeDir, Vector3.up);
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Spot;
            _light.spotAngle = 125f; _light.innerSpotAngle = 60f;
            _light.range = 6.5f; _light.intensity = 0f;
            _light.color = new Color(1f, 0.83f, 0.62f);
            _light.shadows = LightShadows.Soft; _light.shadowNearPlane = 0.06f; _light.shadowStrength = 0.9f;
            _light.renderingLayerMask = -1;
            _light.enabled = false;
            MakeDust();

            // The wound-up cord behind the base; clicking it lets the cord out into your hand.
            _bundle = new GameObject("CordBundle");
            _bundle.transform.SetParent(transform, false);
            _bundle.transform.localPosition = BundleAt;
            _bundle.transform.localRotation = BundleTurn;
            ItemModels.Attach(_bundle.transform, "CordBundle", new Vector3(0.08f, 0.028f, 0.08f), "CordBlack");
            var bc = _bundle.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.09f, 0.035f, 0.09f); bc.center = new Vector3(0f, 0.0175f, 0f);
            _bundle.AddComponent<CordBundleHandle>().lamp = this;
        }

        void Start()
        {
            // Its plug and cord, lying behind it.
            var back = transform.TransformPoint(CordExit + new Vector3(0f, 0.02f, -0.4f));
            _plug = Plug.Create("lampplug", this, back, transform.rotation * Quaternion.Euler(0f, 180f, 0f), CordColor);
            _cord = Cord.Create("LampCord", _plug, Plug.CordPoint, this, CordExit, CordLength, 0.0028f, ItemModels.Mat("CordBlack", CordColor));
            ShowBundle();
        }

        void ShowBundle()
        {
            if (_bundle) _bundle.SetActive(_bundled);
            if (_plug) _plug.gameObject.SetActive(!_bundled);
            if (_cord) _cord.gameObject.SetActive(!_bundled);
        }

        /// <summary>Lets the cord out: the plug appears where the bundle was.</summary>
        public override void Unbundle()
        {
            if (!_bundled) return;
            _bundled = false;
            if (_plug)
            {
                var at = transform.TransformPoint(BundleAt + Vector3.up * 0.03f);
                _plug.Body.position = at; _plug.transform.position = at;
                _plug.Body.linearVelocity = Vector3.zero;
            }
            ShowBundle();
            if (_cord) _cord.Lay();
            _fx.PlayOneShot(SoundBank.CordWhip, 0.25f);
        }

        /// <summary>Winds the cord back up behind the base (not while it is plugged in).</summary>
        public void Bundle()
        {
            if (_bundled || (_plug && _plug.Plugged)) return;
            var hand = FindAnyObjectByType<PropGrabber>();
            if (hand && _plug && hand.HeldItem == _plug) hand.LetGo(_plug);
            _bundled = true;
            StopReel(_cord);
            if (_cord) _cord.SetLength(CordLength);          // reeled in: full length again for next time
            ShowBundle();
            _fx.PlayOneShot(SoundBank.CordWhip, 0.3f);
        }

        public override void ReelFromPlug(Plug plug, in HoldInput i)
        {
            if (_bundled || plug != _plug) return;
            if (!ReelUpdate(_cord, _plug, i.reel, i.dt, i.hand)) return;
            var hand = i.hand;
            Bundle();                     // lets go of the plug: the cord is wound up behind the base
            if (hand) hand.PickUp(this);
        }

        public override void WakeCord() { if (_cord) _cord.Wake(); }
        public override float CordReach => CordLength;
        protected override Cord GetCord() => _cord;

        public override bool TakesSocket(Socket s) =>
            _plug && !_plug.Plugged && _plug.CanUse(s) && Vector3.Distance(s.Position, transform.TransformPoint(CordExit)) < CordLength * 0.92f;

        void OnDestroy()
        {
            if (_plug) Destroy(_plug.gameObject);
            if (_cord) Destroy(_cord.gameObject);
        }

        // Clicking an outlet with the lamp in hand: it is set down beside the feet, off the way to the outlet and near enough
        // for its cord (PropGrabber.SetDownBeside), and the hand takes its plug there. (It was set down where the view met the
        // floor: under the outlet, in the way. The body stepping in climbed onto it, the plug stopped 15 cm short, and the lamp
        // was launched 46 m when the body came off it.)

        public override void HoldUpdate(in HoldInput i)
        {
            if (i.secondary) { Bundle(); return; }
            if (!_bundled && ReelUpdate(_cord, _plug, i.reel, i.dt, i.hand)) { if (_plug && _plug.Plugged) _plug.Unplug(false); Bundle(); return; }
            if (!i.pressed) return;
            if (_plug && !_plug.Plugged)
            {
                var s = i.socket;
                if (s == null && i.hit && i.hitInfo.distance <= reach) s = Sockets.Nearest(i.hitInfo.point, 0.1f);
                if (s != null && _plug.CanUse(s))
                {
                    if (Vector3.Distance(s.Position, transform.TransformPoint(CordExit)) < CordLength * 0.92f)
                    {
                        if (!i.hand) { Unbundle(); _plug.PlugInto(s); return; }
                        // Set down beside the feet, then the hand takes the plug to the outlet. Nowhere to set it down: nothing.
                        var hand = i.hand;
                        var socket = s;
                        if (!hand.SpotBeside(s.Position, new Vector3(0.09f, 0.2f, 0.09f),
                                             (at, rot) => Vector3.Distance(at + rot * CordExit, socket.Position) < CordLength * 0.8f,
                                             out var spot, out var turn)) return;
                        Unbundle();
                        var p = _plug;
                        hand.PlaceInHand(p);               // the hand keeps the plug as it lets the cord out
                        hand.SetDownBeside(this, spot, turn);
                        if (p) { hand.PickUp(p); p.BeginPlugIn(s); }
                        return;
                    }
                }
            }
            Toggle();
        }

        public void Toggle()
        {
            switchOn = !switchOn;
            _fx.PlayOneShot(SoundBank.SwitchClick, 0.6f);
        }

        // ---------------- Dust in the beam ----------------

        ParticleSystem _dust;
        ParticleSystemRenderer _dustRenderer;
        MaterialPropertyBlock _dustMpb;
        static Material s_dustMat;

        /// <summary>
        /// Dust floats in the air round the lamp all the time; the shader shows a mote only while it is inside the beam,
        /// so they drift into the light, glint, and drift out again.
        /// </summary>
        void MakeDust()
        {
            if (!s_dustMat)
            {
                var sh = Shader.Find("Backrooms/DustMote");
                if (!sh) return;
                s_dustMat = new Material(sh) { name = "DustMote", hideFlags = HideFlags.DontSave };
            }
            var go = new GameObject("Dust");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Bulb + ShadeDir * 0.45f;
            _dust = go.AddComponent<ParticleSystem>();
            _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _dust.main;
            main.loop = true; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.015f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.0065f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.45f), new Color(1f, 1f, 1f, 1f));
            main.gravityModifier = 0.0005f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 140;
            main.prewarm = true;
            var emission = _dust.emission;
            emission.rateOverTime = 14f;
            var shape = _dust.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.9f, 0.9f, 0.9f);
            var noise = _dust.noise;
            noise.enabled = true; noise.strength = 0.012f; noise.frequency = 0.35f; noise.scrollSpeed = 0.05f;
            _dustRenderer = go.GetComponent<ParticleSystemRenderer>();
            _dustRenderer.sharedMaterial = s_dustMat;
            _dustRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            _dustRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _dustRenderer.receiveShadows = false;
            _dustMpb = new MaterialPropertyBlock();
        }

        void UpdateDust()
        {
            if (!_dust) return;
            bool on = _level > 0.05f && !Held;
            if (on && !_dust.isPlaying) _dust.Play();
            else if (!on && _dust.isPlaying) _dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (!on) return;
            var t = _light.transform;
            _dustMpb.SetVector("_ConePos", t.position);
            _dustMpb.SetVector("_ConeDir", t.forward);
            _dustMpb.SetFloat("_ConeCos", Mathf.Cos(_light.spotAngle * 0.5f * 0.8f * Mathf.Deg2Rad));
            _dustMpb.SetFloat("_Range", 1.6f);
            _dustMpb.SetFloat("_Strength", 1.5f * _level);
            _dustRenderer.SetPropertyBlock(_dustMpb);
        }

        void LateUpdate()
        {
            UpdateDust();
            // The filament warms up and cools down in a moment; a lamp far from the camera keeps no shadow map.
            _level = Mathf.MoveTowards(_level, Lit ? 1f : 0f, Time.deltaTime * (Lit ? 12f : 6f));
            bool on = _level > 0.01f;
            if (_light.enabled != on) _light.enabled = on;
            _light.intensity = 2.6f * _level;
            var cam = Camera.main;
            _light.shadows = cam && (cam.transform.position - transform.position).sqrMagnitude < 20f * 20f ? LightShadows.Soft : LightShadows.None;
            if (_bulbRenderer)
            {
                var glow = BulbGlow();
                var want = _level > 0.5f && glow ? glow : _bulbOff;
                var mats = _bulbRenderer.sharedMaterials;
                if (mats[_bulbIndex] != want) { mats[_bulbIndex] = want; _bulbRenderer.sharedMaterials = mats; }
            }
        }

        /// <summary>The bulb glows with the ceiling fixtures' lit lens material (already compiled; nothing new to build).</summary>
        static Material BulbGlow()
        {
            var ws = Generation.WorldStreamer.Current;
            return ws && ws.assets ? ws.assets.lensOn : null;
        }
    }
}
