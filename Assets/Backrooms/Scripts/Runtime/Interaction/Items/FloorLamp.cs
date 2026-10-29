using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A standing floor lamp (the living rooms' lamps, OfficeChunkBuilder) you can pick up and carry, with its own cord and
    /// plug like the desk lamp (<see cref="DeskLamp"/>): it lights only while switched on and plugged into a socket with
    /// power. A lamp the world places lit is placed with its plug already in the outlet beside it (DistrictGenerator puts
    /// one there); one placed dark has its cord wound up behind its base.
    ///  - Holding it, click an outlet (or an extension cord's box) within the cord's reach: its plug goes in.
    ///  - Click anywhere else: the switch. Q sets it down. Right mouse winds the cord back up.
    /// Light: a warm point light inside the shade. The shade casts its shadow from both sides at 80 %: the wall beside the
    /// shade gets the little that comes through the fabric, above and below it the light that comes out of the open ends.
    /// </summary>
    public class FloorLamp : PowerUser, IHandGrip, IGripRound
    {
        public const float CordLength = 2.5f;
        /// <summary>The light: in the shade, just above the bulb's glass (the model's shade runs 1.38-1.70 m, 0.23 m round at
        /// its foot; the bulb 1.43-1.52 m).</summary>
        static readonly Vector3 Bulb = new Vector3(0f, 1.56f, 0f);
        const float ShadeBottom = 1.38f, ShadeTop = 1.70f, ShadeRadiusBottom = 0.225f, ShadeRadiusTop = 0.185f;
        static readonly Vector3 CordExit = new Vector3(0f, 0.02f, -0.16f);
        static readonly Vector3 BundleAt = new Vector3(-0.02f, 0f, -0.27f);
        const float PoleRadius = 0.012f, PoleBottom = 0.04f, PoleTop = 1.26f;

        [NotTuned] public bool switchOn = true;
        [Tooltip("Furthest outlet you can plug into from your eye (m).")] public float reach = 2.2f;
        /// <summary>Placed by the world lit: its plug goes into the nearest outlet in reach once the outlets are loaded.</summary>
        [System.NonSerialized] public bool startPlugged;

        Plug _plug;
        Cord _cord;
        Light _light;
        Renderer _shadeRenderer;
        int _shadeIndex = -1;
        Material _shadeOff;
        AudioSource _fx;
        float _level, _plugTries;
        bool _bundled = true;
        GameObject _bundle;

        public override Plug Plug => _bundled ? null : _plug;
        public bool Lit => switchOn && PluggedLive;
        public override bool PlaceOnDrop => true;
        public override Vector3 HoldOffset => new Vector3(0.26f, -0.42f, 0.42f);
        public override Vector3 UseOffset => HoldOffset;
        // The pole, like a handshake round it a little below the shade.
        public override Vector3 GripLocal => new Vector3(PoleRadius + 0.004f, 0.95f, 0f);
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(new Vector3(0f, 0f, 1f), Vector3.right);
        public void GripAxis(out Vector3 point, out Vector3 axis) { point = new Vector3(0f, 0.95f, 0f); axis = Vector3.up; }
        public override float GripCurl => 0.95f;
        public float FingerCurl(int finger) => 1.35f;
        public float ThumbCurl => 0.85f;
        public float ThumbOpposition => 0.75f;
        public override float ContactDistance(Vector3 p) =>
            Mathf.Min(SegmentDistance(p, new Vector3(0f, PoleBottom, 0f), new Vector3(0f, PoleTop, 0f)) - PoleRadius,
                      BoxDistance(p, new Vector3(0f, 0.012f, 0f), new Vector3(0.2f, 0.012f, 0.2f)));
        public override float Amount { get => switchOn ? 1f : 0f; set => switchOn = value > 0.5f; }
        public override string Status
        {
            get
            {
                if (_bundled || !_plug || !_plug.Plugged) return "floor lamp: click an outlet to plug in (right mouse: wind the cord)";
                if (!PluggedLive) return "floor lamp: no power";
                return switchOn ? "floor lamp: on (click: switch)" : "floor lamp: off (click: switch)";
            }
        }

        public static FloorLamp Create(Vector3 position, Quaternion rotation)
        {
            var ws = Generation.WorldStreamer.Current;
            var prefab = ws && ws.assets ? ws.assets.floorLampPrefab : null;
            var go = new GameObject("FloorLamp");
            go.transform.SetPositionAndRotation(position, rotation);
            if (prefab)
            {
                var model = Object.Instantiate(prefab, go.transform, false);
                model.transform.localRotation = prefab.transform.localRotation;
                foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
                Generation.MaterialMaps.Apply(model);
            }
            var bc = go.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.44f, 1.7f, 0.44f); bc.center = new Vector3(0f, 0.85f, 0f);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 5f; rb.linearDamping = 0.1f; rb.angularDamping = 3f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.centerOfMass = new Vector3(0f, 0.06f, 0f);           // the heavy base keeps it standing
            var lamp = go.AddComponent<FloorLamp>();
            lamp.itemId = "floorlamp"; lamp.displayName = "Floor lamp";
            return lamp;
        }

        protected override void Awake()
        {
            base.Awake();
            _fx = ItemModels.Source(gameObject, 8f);
            var ws = Generation.WorldStreamer.Current;
            var shadeOff = ws && ws.assets ? ws.assets.lampShadeOff : null;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                // The model casts no shadow of its own (from the light inside it the one-sided shade was only its rim:
                // streaks either side that moved about; two-sided, the bulb's glass round the light blocked all but a ring).
                // An open tube the shade's shape casts the shade's shadow instead (MakeShadeShadow).
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] && (mats[i] == shadeOff || mats[i].name.StartsWith("M_LampShade"))) { _shadeRenderer = r; _shadeIndex = i; _shadeOff = mats[i]; }
            }
            var lg = new GameObject("Light");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = Bulb;
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 5.5f; _light.intensity = 0f;
            _light.color = new Color(1f, 0.74f, 0.47f);
            _light.shadows = LightShadows.Soft; _light.shadowNearPlane = 0.05f;
            _light.shadowStrength = 0.8f;                       // the fabric lets a fifth through
            _light.renderingLayerMask = -1;
            _light.enabled = false;

            MakeShadeShadow(shadeOff);

            _bundle = new GameObject("CordBundle");
            _bundle.transform.SetParent(transform, false);
            _bundle.transform.localPosition = BundleAt;
            ItemModels.Attach(_bundle.transform, "CordBundle", new Vector3(0.08f, 0.028f, 0.08f), "CordBlack");
            var bbc = _bundle.AddComponent<BoxCollider>();
            bbc.size = new Vector3(0.09f, 0.035f, 0.09f); bbc.center = new Vector3(0f, 0.0175f, 0f);
            _bundle.AddComponent<CordBundleHandle>().lamp = this;
        }

        void Start()
        {
            var back = transform.TransformPoint(CordExit + new Vector3(0f, 0.02f, -0.4f));
            _plug = Plug.Create("lampplug", this, back, transform.rotation * Quaternion.Euler(0f, 180f, 0f), DeskLamp.CordColor);
            _cord = Cord.Create("LampCord", _plug, Plug.CordPoint, this, CordExit, CordLength, 0.0028f, ItemModels.Mat("CordBlack", DeskLamp.CordColor));
            ShowBundle();
        }

        void ShowBundle()
        {
            if (_bundle) _bundle.SetActive(_bundled);
            if (_plug) _plug.gameObject.SetActive(!_bundled);
            if (_cord) _cord.gameObject.SetActive(!_bundled);
        }

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
        }

        public void Bundle()
        {
            if (_bundled || (_plug && _plug.Plugged)) return;
            var hand = FindAnyObjectByType<PropGrabber>();
            if (hand && _plug && hand.HeldItem == _plug) hand.LetGo(_plug);
            _bundled = true;
            StopReel(_cord);
            if (_cord) _cord.SetLength(CordLength);
            ShowBundle();
            _fx.PlayOneShot(SoundBank.CordWhip, 0.3f * SoundCatalog.Volume(SoundIds.Cords));
        }

        public override void ReelFromPlug(Plug plug, in HoldInput i)
        {
            if (_bundled || plug != _plug) return;
            if (!ReelUpdate(_cord, _plug, i.reel, i.dt, i.hand)) return;
            var hand = i.hand;
            Bundle();
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

        public override void HoldUpdate(in HoldInput i)
        {
            if (i.secondary) { Bundle(); return; }
            if (!_bundled && ReelUpdate(_cord, _plug, i.reel, i.dt, i.hand)) { if (_plug && _plug.Plugged) _plug.Unplug(false); Bundle(); return; }
            if (!i.pressed) return;
            if (_plug && !_plug.Plugged)
            {
                var s = i.socket;
                if (s == null && i.hit && i.hitInfo.distance <= reach) s = Sockets.Nearest(i.hitInfo.point, 0.1f);
                if (s != null && _plug.CanUse(s) && Vector3.Distance(s.Position, transform.TransformPoint(CordExit)) < CordLength * 0.92f)
                {
                    if (!i.hand) { Unbundle(); _plug.PlugInto(s); return; }
                    var hand = i.hand;
                    var socket = s;
                    if (!hand.SpotBeside(s.Position, new Vector3(0.22f, 0.85f, 0.22f),
                                         (at, rot) => Vector3.Distance(at + rot * CordExit, socket.Position) < CordLength * 0.8f,
                                         out var spot, out var turn)) return;
                    Unbundle();
                    var p = _plug;
                    hand.PlaceInHand(p);
                    hand.SetDownBeside(this, spot, turn);
                    if (p) { hand.PickUp(p); p.BeginPlugIn(s); }
                    return;
                }
            }
            Toggle();
        }

        public void Toggle()
        {
            switchOn = !switchOn;
            _fx.PlayOneShot(SoundBank.SwitchClick, 0.6f * SoundCatalog.Volume(SoundIds.Switches));
        }

        /// <summary>Placed lit by the world: once the outlets round it are loaded, its plug goes into the nearest in reach.</summary>
        void TryStartPlugged()
        {
            if (!startPlugged || !_plug) return;
            _plugTries += Time.deltaTime;
            if (_plugTries > 5f) { startPlugged = false; return; }       // no outlet after all: it stays dark, cord wound up
            var exit = transform.TransformPoint(CordExit);
            Socket best = null; float bd = CordLength * 0.85f;
            foreach (var s in Sockets.Around(exit))
            {
                if (!s.IsWall || s.occupant != null || !_plug.CanUse(s)) continue;
                float d = Vector3.Distance(s.Position, exit);
                if (d < bd) { bd = d; best = s; }
            }
            if (best == null) return;
            startPlugged = false;
            Unbundle();
            _plug.PlugInto(best);
            if (_cord) _cord.Lay();
        }

        void LateUpdate()
        {
            if (startPlugged) TryStartPlugged();
            _level = Mathf.MoveTowards(_level, Lit ? 1f : 0f, Time.deltaTime * (Lit ? 12f : 6f));
            bool on = _level > 0.01f;
            if (_light.enabled != on) _light.enabled = on;
            _light.intensity = 1.8f * _level;
            // A lamp far off keeps no shadow map (each is six faces).
            var cam = Camera.main;
            var want = cam && (cam.transform.position - transform.position).sqrMagnitude < 14f * 14f ? LightShadows.Soft : LightShadows.None;
            if (_light.shadows != want) _light.shadows = want;
            if (_shadeRenderer && _shadeIndex >= 0)
            {
                var m = _level > 0.5f ? ShadeLit() : _shadeOff;
                var mats = _shadeRenderer.sharedMaterials;
                if (m && mats[_shadeIndex] != m) { mats[_shadeIndex] = m; _shadeRenderer.sharedMaterials = mats; }
            }
        }

        static Mesh s_shadeShadow;

        /// <summary>The shade's shadow: an open tube of its shape, both sides, drawn only into shadow maps.</summary>
        void MakeShadeShadow(Material mat)
        {
            if (!mat) return;
            if (!s_shadeShadow)
            {
                var w = new Generation.MeshWriter();
                var foot = new Vector3(0f, ShadeBottom, 0f);
                w.Frustum(foot, ShadeRadiusBottom, ShadeRadiusTop, ShadeTop - ShadeBottom, 24, true, Generation.MeshWriter.MetreUV);
                w.Frustum(foot, ShadeRadiusBottom - 0.002f, ShadeRadiusTop - 0.002f, ShadeTop - ShadeBottom, 24, false, Generation.MeshWriter.MetreUV);
                s_shadeShadow = w.ToMesh("ShadeShadow");
                s_shadeShadow.hideFlags = HideFlags.DontSave;
            }
            var go = new GameObject("ShadeShadow");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = s_shadeShadow;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        static Material s_shadeLit;

        /// <summary>The lit shade: the world's lit shade, glowing brighter (a bulb right behind thin fabric).</summary>
        static Material ShadeLit()
        {
            if (s_shadeLit) return s_shadeLit;
            var ws = Generation.WorldStreamer.Current;
            var on = ws && ws.assets ? ws.assets.lampShadeOn : null;
            if (!on) return null;
            s_shadeLit = new Material(on) { name = on.name + " (bright)", hideFlags = HideFlags.DontSave };
            if (s_shadeLit.HasProperty("_EmissionColor")) s_shadeLit.SetColor("_EmissionColor", on.GetColor("_EmissionColor") * 2.2f);
            return s_shadeLit;
        }
    }
}
