using System.Collections;
using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A portable CD radio cassette player (2026-11-04, after the owner's photo; models: Tools/Blender/build_boombox.py).
    /// Found now and then on an atrium's upper storey. Carried by its handle (it swings up as you take it and folds down
    /// when you set it down); left click while carrying it starts or stops the tape. Set down, its keys are pressed with
    /// the finger like any button: PLAY, PAUSE, F.FWD and REWIND latch down; STOP/EJECT stops the tape, and when it is
    /// stopped opens the cassette door (and shuts it again); REC will not go down (the cassette's tab is broken out). The
    /// VOLUME knob steps the volume. A cassette in the hand clicked on it goes in (its door opens, takes it and shuts).
    /// The tape plays through <see cref="TapeDeckFilter"/> (wow on the pitch here, flutter, hiss, the tape's and the
    /// speaker's sound there), from a source in the world, so the camcorder hears it as it hears everything. Runs on
    /// batteries that never run down (the brief: no flat batteries). Saved: its volume (Amount); a cassette in it is
    /// saved as itself and taken back in when they load (Adopt).
    /// </summary>
    public class Boombox : WorldItem, IGripRound
    {
        // ---------------- Its parts (Resources/Items/Boombox_Layout.json: pivots in Blender's frame) ----------------

        [System.Serializable]
        class LayoutJson { public float[] door, cassette, handle, handle_grip, key0, key1, key2, key3, key4, key5; public float tilt_deg = 6f; }

        static bool s_layoutRead;
        static Vector3 s_door = new Vector3(0f, 0.0185f, 0.1528f), s_cassette = new Vector3(0f, 0.0331f, -0.0155f),
                       s_handle = new Vector3(0f, 0.217f, -0.105f), s_grip = new Vector3(0f, 0f, 0.185f);
        static readonly Vector3[] s_keys = new Vector3[6];
        static float s_tilt = 6f;
        static Generation.ArtStyle.Look s_layoutLook;

        static Vector3 U(float[] b, Vector3 fallback) => b != null && b.Length == 3 ? new Vector3(-b[0], b[2], -b[1]) : fallback;

        static void ReadLayout()
        {
            if (s_layoutRead && s_layoutLook == Generation.ArtStyle.Current) return;     // (read again for the other look's model)
            s_layoutRead = true;
            s_layoutLook = Generation.ArtStyle.Current;
            for (int i = 0; i < 6; i++) s_keys[i] = new Vector3(0.0562f - i * 0.02245f, 0.1401f, 0.1425f);
            var t = Generation.ArtStyle.Load<TextAsset>("Items/Boombox_Layout");
            if (!t) return;
            var j = JsonUtility.FromJson<LayoutJson>(t.text);
            if (j == null) return;
            s_door = U(j.door, s_door); s_cassette = U(j.cassette, s_cassette); s_handle = U(j.handle, s_handle); s_grip = U(j.handle_grip, s_grip);
            var keys = new[] { j.key0, j.key1, j.key2, j.key3, j.key4, j.key5 };
            for (int i = 0; i < 6; i++) s_keys[i] = U(keys[i], s_keys[i]);
            s_tilt = j.tilt_deg;
        }

        /// <summary>Out of its front panel (it leans back a little), toward whoever presses its keys.</summary>
        Vector3 PanelNormal => new Vector3(0f, Mathf.Sin(s_tilt * Mathf.Deg2Rad), Mathf.Cos(s_tilt * Mathf.Deg2Rad));
        Vector3 PanelUp => new Vector3(0f, Mathf.Cos(s_tilt * Mathf.Deg2Rad), -Mathf.Sin(s_tilt * Mathf.Deg2Rad));

        Transform _door, _handle;
        readonly Transform[] _keys = new Transform[6];
        Renderer _face;
        Material[] _faceOff;
        Material _faceOn;
        AudioSource _music, _fx, _motor;
        TapeDeckFilter _tape;

        public enum Key { Pause, StopEject, FastForward, Rewind, Play, Rec }
        enum Deck { Stopped, Playing, Winding }
        Deck _deck;
        bool _paused, _doorOpen;
        int _windDir;
        float _doorT, _handleT, _speed, _wow;
        readonly float[] _keyT = new float[6];
        readonly float[] _keyTap = new float[6];
        int _volume = 1;
        static readonly float[] Volumes = { 0.22f, 0.42f, 0.68f, 1f };
        float _litUntil;
        bool _lit;
        float _autoStopAt = -1f;

        public Cassette Inserted { get; private set; }

        // ---------------- The item ----------------

        // Carried at your side by its handle (swung up), its front turned outward.
        public override Vector3 HoldOffset => new Vector3(0.3f, -0.66f, 0.38f);
        public override Quaternion HoldTurn => Quaternion.Euler(0f, 90f, 0f);
        Vector3 GripPoint => s_handle + new Vector3(0f, s_grip.z, 0f);
        public override Vector3 GripLocal => GripPoint + Vector3.up * 0.012f;
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(Vector3.forward, Vector3.up);
        public void GripAxis(out Vector3 point, out Vector3 axis) { point = GripPoint; axis = Vector3.right; }
        public override float GripCurl => 0.95f;
        public override bool PlaceOnDrop => true;
        public override bool UsesAim => false;
        public override bool Pocketable => false;
        public override float Effort => 0.35f;
        public override Quaternion[] RestTurns => s_rests;
        static readonly Quaternion[] s_rests = { Quaternion.identity, Quaternion.Euler(-90f, 0f, 0f) };
        public override float Amount { get => _volume / 3f; set => _volume = Mathf.Clamp(Mathf.RoundToInt(value * 3f), 0, 3); }
        public override string Status
        {
            get
            {
                string tape = Inserted ? Inserted.Title : "no tape";
                string state = _deck == Deck.Playing ? (_paused ? "paused" : "playing") : _deck == Deck.Winding ? (_windDir > 0 ? "fast forward" : "rewinding") : "stopped";
                return $"boombox: {state}, {tape} (click: play / stop)";
            }
        }

        public override float ContactDistance(Vector3 p)
        {
            // the handle's grip when it is up (what the fingers close on), else the body
            float body = BoxDistance(p, new Vector3(0f, 0.108f, 0f), new Vector3(0.25f, 0.108f, 0.15f));
            if (_handleT < 0.5f) return body;
            var g = GripPoint;
            float grip = SegmentDistance(p, g - Vector3.right * 0.1f, g + Vector3.right * 0.1f) - 0.008f;
            return Mathf.Min(body, grip);
        }

        public static Boombox Create(Vector3 position, Quaternion rotation)
        {
            var go = ItemModels.Body("Boombox", position, rotation, 4.4f, "Boombox", new Vector3(0.5f, 0.21f, 0.24f), "Paint_BoomSilver", false, new Vector3(0f, 0.105f, -0.03f));
            var b = go.AddComponent<Boombox>();
            b.itemId = "boombox"; b.displayName = "Boombox";
            Generation.PoolLitSwitch.Attach(go);
            return b;
        }

        protected override void Awake()
        {
            ReadLayout();
            base.Awake();
            // The front, round the door's opening (the main box stops short of it so the cassette can be taken out).
            void Box(Vector3 c, Vector3 s) { var bc = gameObject.AddComponent<BoxCollider>(); bc.center = c; bc.size = s; }
            Box(new Vector3(-0.165f, 0.105f, 0.12f), new Vector3(0.17f, 0.21f, 0.05f));
            Box(new Vector3(0.165f, 0.105f, 0.12f), new Vector3(0.17f, 0.21f, 0.05f));
            Box(new Vector3(0f, 0.15f, 0.125f), new Vector3(0.16f, 0.12f, 0.06f));
            Box(new Vector3(0f, 0.0095f, 0.125f), new Vector3(0.16f, 0.019f, 0.06f));
            // The parts that move, each on its pivot; the face (its prints, the LCD and the LED).
            var faceGo = ItemModels.Attach(transform, "BoomboxFace", Vector3.one * 0.001f, "Print_BoomFace");
            faceGo.name = "Face";
            _face = faceGo.GetComponentInChildren<Renderer>();
            _door = Part("Door", "BoomboxDoor", s_door);
            _handle = Part("Handle", "BoomboxHandle", s_handle);
            for (int i = 0; i < 6; i++) _keys[i] = Part("Key" + i, "BoomboxKey" + i, s_keys[i]);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // What the finger presses: each key, the volume knob, the function buttons.
            Vector3 n = PanelNormal, up = PanelUp;
            for (int i = 0; i < 6; i++)
            {
                Vector3 c = s_keys[i] - up * 0.0115f + n * 0.011f;
                Button("Key" + i, c, new Vector3(0.021f, 0.022f, 0.02f), (Key)i);
            }
            Vector3 face0 = s_door - n * -0.0045f;                 // the face at the door's foot (s = 0)
            Vector3 F(float x, float s, float o) => face0 + new Vector3(-x, 0f, 0f) + up * s + n * o;
            Button("Volume", F(-0.079f, 0.153f, 0.008f), new Vector3(0.045f, 0.045f, 0.02f), (Key)(-1));
            Button("Function", F(0.0f, 0.136f, 0.004f), new Vector3(0.085f, 0.03f, 0.02f), (Key)(-2));
            Button("CdKeys", F(0.082f, 0.157f, 0.004f), new Vector3(0.04f, 0.04f, 0.02f), (Key)(-2));
            // Its sounds: the tape through the deck and the speakers (from the speakers' middle), the mechanism's own.
            _music = Source("Speaker", new Vector3(0f, 0.09f, 0.12f), 28f);
            _music.spatializePostEffects = true;                  // the deck's sound first, then placed in the world
            _music.loop = false;
            _tape = _music.gameObject.AddComponent<TapeDeckFilter>();
            _fx = Source("Mechanism", new Vector3(0f, 0.06f, 0.12f), 10f);
            _motor = Source("Motor", new Vector3(0f, 0.05f, 0.08f), 6f);
            _motor.loop = true;
            // Heard by the way round through doorways (Audio.SoundRoute), as the building's own sounds are.
            SoundRoute.Add(_music); SoundRoute.Add(_fx); SoundRoute.Add(_motor);
            BoomboxSounds.Make();
            _faceOff = _face ? _face.sharedMaterials : null;
        }

        void Start()
        {
            MakeLitFace();
            SetLit(false);
            if (!Inserted) Cassette.AdoptNear(this);
        }

        Transform Part(string name, string resource, Vector3 pivot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pivot;
            ItemModels.Attach(go.transform, resource, Vector3.one * 0.005f, "Paint_BoomSilver");
            return go.transform;
        }

        void Button(string name, Vector3 centre, Vector3 size, Key key)
        {
            var go = new GameObject("Button_" + name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = Quaternion.Euler(-s_tilt, 0f, 0f);
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true; bc.size = size;
            var b = go.AddComponent<BoomboxButton>();
            b.box = this; b.key = key;
        }

        AudioSource Source(string name, Vector3 at, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            return ItemModels.Source(go, range);
        }

        void MakeLitFace()
        {
            if (!_face || _faceOn) return;
            var src = Generation.MaterialMaps.TrofferOn;
            var baked = _face.sharedMaterial;
            var tex = baked && baked.HasProperty("_BaseMap") ? baked.GetTexture("_BaseMap") : null;
            var glow = Resources.Load<Texture2D>(Generation.ArtStyle.Path("Items/BoomboxFace") + "_Emission");
            if (!src || !tex || !glow) return;
            _faceOn = new Material(src) { name = "Boombox face (lit)", hideFlags = HideFlags.DontSave };
            _faceOn.SetTexture("_BaseMap", tex);
            _faceOn.SetTextureScale("_BaseMap", Vector2.one); _faceOn.SetTextureOffset("_BaseMap", Vector2.zero);
            _faceOn.SetColor("_BaseColor", Color.white);
            _faceOn.SetTexture("_EmissionMap", glow);
            _faceOn.SetColor("_EmissionColor", new Color(1.1f, 1.1f, 1.05f));
            _faceOn.EnableKeyword("_EMISSION");
        }

        void SetLit(bool on)
        {
            if (on == _lit && _face && (!on || _face.sharedMaterial == _faceOn)) return;
            _lit = on;
            if (!_face || _faceOff == null) return;
            if (on && _faceOn)
            {
                var mats = new Material[_faceOff.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = _faceOn;
                _face.sharedMaterials = mats;
            }
            else _face.sharedMaterials = _faceOff;
        }

        protected override void OnPickedUp() { if (_doorOpen) SetDoor(false); }
        protected override void OnDropped() { }

        public override void HoldUpdate(in HoldInput i)
        {
            // Carried: a click starts the tape, or stops it.
            if (i.pressed)
            {
                if (_deck == Deck.Stopped) Press(Key.Play); else Press(Key.StopEject);
            }
        }

        // ---------------- The deck ----------------

        /// <summary>Where a cassette stands in its door (world), for taking one back in after a load.</summary>
        public Vector3 SlotWorld => _door ? _door.TransformPoint(s_cassette) : transform.position;
        public bool CanTake => !Inserted && !Held;

        /// <summary>A cassette held in the hand is clicked on it: the door opens, takes it and shuts.</summary>
        public bool TryInsert(Cassette c, PropGrabber hand)
        {
            if (!c || Inserted || Held) { if (!Held) Thunk(); return false; }
            if (_deck != Deck.Stopped) Stop(false);
            if (hand) hand.LetGo(c);
            StartCoroutine(InsertRoutine(c));
            return true;
        }

        IEnumerator InsertRoutine(Cassette c)
        {
            bool wasOpen = _doorOpen;
            Adopt(c);                                               // into the holder at once (the door opens round it)
            if (!wasOpen) { SetDoor(true); yield return new WaitForSeconds(0.3f); }
            Fx(BoomboxSounds.Insert, 0.7f);
            yield return new WaitForSeconds(0.35f);
            SetDoor(false);
        }

        /// <summary>Puts a cassette in its door (no sound): loading, or the end of inserting one.</summary>
        public void Adopt(Cassette c)
        {
            if (!c || Inserted) return;
            Inserted = c;
            c.EnterDeck(this, _door, s_cassette, Quaternion.Euler(90f - s_tilt, 0f, 0f), _doorOpen);
        }

        /// <summary>Its cassette was taken out by hand (the door was open).</summary>
        public void Removed(Cassette c)
        {
            if (Inserted != c) return;
            Inserted = null;
            if (_deck != Deck.Stopped) Stop(false);
        }

        void SetDoor(bool open)
        {
            if (open == _doorOpen) return;
            _doorOpen = open;
            Fx(open ? BoomboxSounds.Eject : BoomboxSounds.DoorShut, open ? 0.8f : 0.6f);
            if (Inserted) Inserted.SetTakeable(open);
        }

        /// <summary>A key, the knob or a button is pressed (by the finger, or a click while carried).</summary>
        public void Press(Key key)
        {
            _litUntil = Time.time + 45f;
            if ((int)key == -1) { _volume = (_volume + 1) % Volumes.Length; Fx(BoomboxSounds.Click, 0.5f); return; }
            if ((int)key == -2) { Fx(BoomboxSounds.Click, 0.45f); return; }
            int k = (int)key;
            switch (key)
            {
                case Key.Rec:
                    // the cassette's tab is broken out (and with none in, the mechanism still will not let it go down)
                    _keyTap[k] = 0.35f; Thunk(); return;
                case Key.StopEject:
                    _keyTap[k] = 1f;
                    if (_deck != Deck.Stopped || _paused) { Stop(true); return; }
                    SetDoor(!_doorOpen);
                    return;
                case Key.Play:
                    if (_doorOpen || _deck == Deck.Playing) { _keyTap[k] = 0.4f; Thunk(); return; }
                    if (_deck == Deck.Winding) StopWinding();
                    Fx(BoomboxSounds.Latch, 0.8f);
                    StartPlay();
                    return;
                case Key.Pause:
                    _paused = !_paused;
                    Fx(BoomboxSounds.Latch, 0.55f);
                    if (_deck == Deck.Playing) { if (_paused) _music.Pause(); else _music.UnPause(); }
                    return;
                case Key.FastForward:
                case Key.Rewind:
                    if (_doorOpen) { _keyTap[k] = 0.4f; Thunk(); return; }
                    if (_deck == Deck.Playing) { _keyTap[k] = 0.4f; Thunk(); return; }        // (play must be stopped first)
                    Fx(BoomboxSounds.Latch, 0.8f);
                    _deck = Deck.Winding; _windDir = key == Key.FastForward ? 1 : -1;
                    _motor.clip = BoomboxSounds.Wind; _motor.volume = 0.32f; _motor.pitch = 0.9f; _motor.Play();
                    if (!Inserted) _autoStopAt = Time.time + 1.1f;
                    return;
            }
        }

        void StartPlay()
        {
            _deck = Deck.Playing;
            _speed = 0.82f;                                         // the capstan coming up to speed: a little wow
            _motor.clip = BoomboxSounds.Motor; _motor.volume = 0.06f; _motor.pitch = 1f; _motor.Play();
            if (!Inserted) { _autoStopAt = Time.time + 1.2f; return; }
            var clip = Inserted.Clip;
            if (!clip) { _autoStopAt = Time.time + 1.2f; return; }
            if (Inserted.Position >= clip.length - 0.1f) { _autoStopAt = Time.time + 0.4f; return; }   // at the end of the side
            _music.clip = clip;
            _music.time = Mathf.Clamp(Inserted.Position, 0f, clip.length - 0.05f);
            _music.pitch = _speed;
            _tape.running = true;
            if (_paused) { _music.Play(); _music.Pause(); } else _music.Play();
        }

        void Stop(bool sound)
        {
            if (_deck == Deck.Playing && _music.clip && Inserted) Inserted.Position = _music.time;
            if (sound) Fx(BoomboxSounds.Stop, 0.8f);
            _music.Stop(); _tape.running = false;
            _motor.Stop();
            _deck = Deck.Stopped; _paused = false; _autoStopAt = -1f;
        }

        void StopWinding() { _motor.Stop(); _deck = Deck.Stopped; }

        void AutoStop()
        {
            Fx(BoomboxSounds.AutoStop, 0.75f);
            Stop(false);
        }

        void Thunk() => Fx(BoomboxSounds.Thunk, 0.5f);

        void Fx(AudioClip c, float v)
        {
            if (!c || !_fx) return;
            _fx.pitch = Random.Range(0.96f, 1.04f);
            _fx.PlayOneShot(c, v);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            // The handle up while carried, down when set down; the door; the keys.
            _handleT = Mathf.MoveTowards(_handleT, Held ? 1f : 0f, dt * 5f);
            if (_handle) _handle.localRotation = Quaternion.Euler(-90f * Smooth(_handleT), 0f, 0f);
            _doorT = Mathf.MoveTowards(_doorT, _doorOpen ? 1f : 0f, dt * (_doorOpen ? 7f : 4.5f));
            if (_door) _door.localRotation = Quaternion.Euler(22f * Smooth(_doorT), 0f, 0f);
            for (int k = 0; k < 6; k++)
            {
                bool latched = (k == (int)Key.Play && _deck == Deck.Playing) || (k == (int)Key.Pause && _paused)
                               || (k == (int)Key.FastForward && _deck == Deck.Winding && _windDir > 0) || (k == (int)Key.Rewind && _deck == Deck.Winding && _windDir < 0);
                float want = latched ? 0.8f : _keyTap[k];
                _keyTap[k] = Mathf.MoveTowards(_keyTap[k], 0f, dt * 6f);
                _keyT[k] = Mathf.MoveTowards(_keyT[k], want, dt * (want > _keyT[k] ? 30f : 9f));
                if (_keys[k]) _keys[k].localRotation = Quaternion.Euler(15f * _keyT[k], 0f, 0f);
            }
            SetLit(_deck != Deck.Stopped || Time.time < _litUntil);
            _tape.level = Volumes[_volume];
            if (_autoStopAt > 0f && Time.time > _autoStopAt) { AutoStop(); return; }
            switch (_deck)
            {
                case Deck.Playing:
                {
                    if (!Inserted || !_music.clip) break;
                    if (_paused) { _tape.running = false; break; }
                    _tape.running = true;
                    // up to speed, then the slow wow of a worn belt (the flutter is the filter's)
                    _speed = Mathf.MoveTowards(_speed, 1f, dt * 0.6f);
                    _wow += dt;
                    _music.pitch = _speed * (1f + 0.0011f * Mathf.Sin(_wow * 2f * Mathf.PI * 0.55f) + 0.0005f * Mathf.Sin(_wow * 2f * Mathf.PI * 1.3f + 1f));
                    Inserted.Position = _music.time;
                    if (!_music.isPlaying || _music.time >= _music.clip.length - 0.05f) { Inserted.Position = _music.clip.length; AutoStop(); }
                    break;
                }
                case Deck.Winding:
                {
                    if (!Inserted || !Inserted.Clip) break;
                    float len = Inserted.Clip.length;
                    // the take-up pack grows: winding speeds up toward the end
                    float p = Inserted.Position / len;
                    float rate = 18f * (0.8f + 0.5f * (_windDir > 0 ? p : 1f - p));
                    Inserted.Position = Mathf.Clamp(Inserted.Position + _windDir * rate * dt, 0f, len);
                    _motor.pitch = 0.85f + 0.3f * (_windDir > 0 ? p : 1f - p);
                    if ((_windDir > 0 && Inserted.Position >= len) || (_windDir < 0 && Inserted.Position <= 0f)) AutoStop();
                    break;
                }
            }
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        protected override void OnDisable()
        {
            base.OnDisable();
            if (_deck == Deck.Playing && Inserted && _music && _music.clip) Inserted.Position = _music.time;
        }
    }

    /// <summary>One of a boombox's keys (or its knob, or its buttons): something the free hand presses.</summary>
    public class BoomboxButton : MonoBehaviour, IPokeTarget
    {
        public Boombox box;
        public Boombox.Key key;
        public Vector3 PokeNormal => transform.forward;
        public Vector3 FocusPoint => transform.position;
        public void PokeStroke(out Vector3 from, out Vector3 to)
        {
            from = transform.position + transform.forward * 0.03f;
            to = transform.position - transform.forward * 0.006f;
        }
        public void Press() { if (box && !box.Held) box.Press(key); }
    }

    /// <summary>The boombox's mechanism, made once in code: the keys latching and letting go, the door's spring, a
    /// cassette clacking into its holder, the motor's soft whir while playing, the whine of winding, the auto-stop.</summary>
    public static class BoomboxSounds
    {
        public static AudioClip Latch, Stop, AutoStop, Thunk, Click, Eject, DoorShut, Insert, Motor, Wind;
        const int Rate = 44100;

        static AudioClip Clip(string name, float[] d) { var c = AudioClip.Create(name, d.Length, 1, Rate, false); c.SetData(d, 0); return c; }

        public static void Make()
        {
            if (Latch) return;
            var r = new System.Random(4471);
            float N() => (float)(r.NextDouble() * 2.0 - 1.0);
            // A mechanical clunk: a thump (the key's travel ending), a hard tick (the latch), a short metal ring.
            float[] Clunk(float len, float thump, float tick, float ring, float ringHz, float lpk)
            {
                int n = (int)(len * Rate); var d = new float[n]; float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    lp += (N() - lp) * lpk;
                    float body = Mathf.Sin(t * 2f * Mathf.PI * (180f - 900f * t)) * Mathf.Exp(-t * 60f) * thump;
                    float click = lp * Mathf.Exp(-t * 420f) * tick + N() * Mathf.Exp(-t * 900f) * tick * 0.4f;
                    float rg = Mathf.Sin(t * 2f * Mathf.PI * ringHz) * Mathf.Exp(-t * 55f) * ring;
                    d[i] = (body + click + rg) * 0.9f;
                }
                return d;
            }
            Latch = Clip("BoomLatch", Clunk(0.18f, 0.5f, 0.9f, 0.12f, 2300f, 0.45f));
            Stop = Clip("BoomStop", Clunk(0.2f, 0.6f, 0.8f, 0.15f, 1900f, 0.4f));
            AutoStop = Clip("BoomAutoStop", Clunk(0.22f, 0.7f, 1f, 0.2f, 1700f, 0.4f));
            Thunk = Clip("BoomThunk", Clunk(0.12f, 0.45f, 0.25f, 0f, 1000f, 0.2f));
            Click = Clip("BoomClick", Clunk(0.06f, 0.08f, 0.6f, 0.05f, 3400f, 0.7f));
            DoorShut = Clip("BoomDoorShut", Clunk(0.16f, 0.4f, 0.7f, 0.06f, 1400f, 0.35f));
            Insert = Clip("BoomInsert", Clunk(0.14f, 0.3f, 0.8f, 0.04f, 2600f, 0.6f));
            // The door's spring letting it out: a release click, then the spring's twang and the door's rattle.
            {
                int n = (int)(0.4f * Rate); var d = new float[n]; float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    lp += (N() - lp) * 0.3f;
                    float rel = lp * Mathf.Exp(-t * 300f);
                    float twang = Mathf.Sin(t * 2f * Mathf.PI * (620f + 80f * Mathf.Exp(-t * 20f))) * Mathf.Exp(-t * 14f) * 0.22f;
                    float rattle = t > 0.08f ? N() * Mathf.Exp(-(t - 0.08f) * 60f) * 0.35f * (0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI * 45f)) : 0f;
                    d[i] = rel * 0.8f + twang + rattle;
                }
                Eject = Clip("BoomEject", d);
            }
            // The motor and capstan while playing: a soft whir (a loop).
            {
                int n = Rate * 2; var d = new float[n]; float lp = 0f, lp2 = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    lp += (N() - lp) * 0.08f; lp2 += (lp - lp2) * 0.3f;
                    d[i] = lp2 * 0.5f * (1f + 0.3f * Mathf.Sin(t * 2f * Mathf.PI * 8f)) + Mathf.Sin(t * 2f * Mathf.PI * 94f) * 0.08f + Mathf.Sin(t * 2f * Mathf.PI * 188f) * 0.04f;
                }
                Motor = Clip("BoomMotor", d);
            }
            // Winding: the motor's whine up high and the tape's rustle past the heads (a loop).
            {
                int n = Rate * 2; var d = new float[n]; float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    lp += (N() - lp) * 0.25f;
                    float whine = Mathf.Sin(t * 2f * Mathf.PI * 300f) * 0.25f + Mathf.Sin(t * 2f * Mathf.PI * 600f) * 0.12f + Mathf.Sin(t * 2f * Mathf.PI * 1500f) * 0.04f;
                    d[i] = whine * (0.85f + 0.15f * Mathf.Sin(t * 2f * Mathf.PI * 12f)) + lp * 0.35f;
                }
                Wind = Clip("BoomWind", d);
            }
        }
    }
}
