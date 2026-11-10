using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The work (2026-11-07, revised 2026-11-09: Docs/GameDesign.md sections 6-7). Every world has one floor (LeasePlan),
    /// kept for good. Its clients' seams are always open for business (Contracts.Batches): take a lot of boxes from behind
    /// a seam, store it on lit shelves for its time, bring it back when it is due; paid a third when it is stored and the
    /// rest when it is back. The company's work orders (WorkOrders) set broad goals on top and unlock what the counter sells.
    /// <para>No working day since 2026-11-09: no shift, no closing, no blackout, no night. Business time runs while anyone is
    /// out of the hub; the camcorder's date follows it (client time). A slip prints each time you ride back up
    /// (Contracts.Slip). The old day's code is kept in git history (the punch clock pass, 2026-11-08).</para>
    /// <para>Co-op: lots depend on the save, which the host keeps; boxes have fixed ids (shared like any moved prop). The
    /// batches themselves are not synced to friends yet.</para>
    /// </summary>
    public partial class Contracts : MonoBehaviour
    {
        public static Contracts Instance { get; private set; }
        public WorldStreamer streamer;

        /// <summary>The old working day's length (s): the power bill is still priced by it (a generator's charge a day).</summary>
        public const float DaySeconds = 15f * 60f;
        /// <summary>The clients (the four places: LeaseSeams.Kinds): their names, and their tape's colour.</summary>
        public static readonly string[] Clients = { "TALLMORE", "QUILLBY", "PAPERLARK", "BELLWEATHER" };
        public static readonly Color[] ClientColours =
        {
            new Color(0.23f, 0.43f, 0.77f), new Color(0.78f, 0.2f, 0.16f), new Color(0.26f, 0.58f, 0.3f), new Color(0.58f, 0.25f, 0.5f),
        };

        WorldSaveData D => GameSession.Data;
        float _nextCheck;
        bool _dirty = true;

        /// <summary>The camcorder's date and time: the date follows client time (about a day a minute of business time,
        /// decision 10), the time of day runs as a normal clock from 9 AM.</summary>
        public System.DateTime? Clock
        {
            get
            {
                if (D == null) return null;
                double days = System.Math.Floor(D.business * ClientDaysPerSecond);
                return new System.DateTime(1996, 3, 11, 9, 0, 0).AddDays(days).AddSeconds(D.business % 86400f);
            }
        }

        public string DateText => Clock.HasValue ? Clock.Value.ToString("MMM dd yyyy").ToUpperInvariant() : "";

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; LightZones.ClosingAt = -1f; PowerGrid.AllLive = false; }

        void Start()
        {
            if (D == null) { enabled = false; return; }
            if (D.day < 1) D.day = 1;
            BuildHub();
            D.lease = LeasePlan.Current;
            // Storage (2026-11-08, Docs/GameDesign.md section 8): shelves measured for light, every outlet live.
            if (!GetComponent<ShelfWatch>()) gameObject.AddComponent<ShelfWatch>();
            if (!GetComponent<OrderTracker>()) gameObject.AddComponent<OrderTracker>();
            if (!GetComponent<StaffDoors>()) gameObject.AddComponent<StaffDoors>();
            if (!GetComponent<ReceiptPrinters>()) gameObject.AddComponent<ReceiptPrinters>();
            PowerGrid.AllLive = true;
            // (No closing any more: the lights stay as they are and the seams stay open.)
            LightZones.ClosingAt = -1f;
            NoclipPassage.SeamsShut = false;
            HandTruck.LoadAll();
        }

        /// <summary>A small printed label on a wall (cream on the company's brown), width metres wide.</summary>
        internal static GameObject Label(Transform parent, string name, string text, Vector3 at, Quaternion rot, float width)
        {
            int w = PixelFont.Width(text, 1) + 6, h = 11;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = Choc;
            PixelFont.Draw(px, w, h, text, 3, 2, 1, new Color32(243, 230, 200, 255));
            tex.SetPixels32(px); tex.Apply(false);
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.SetPositionAndRotation(at + rot * new Vector3(0f, 0f, 0.012f), rot * Quaternion.Euler(0f, 180f, 0f));
            q.transform.localScale = new Vector3(width, width * h / w, 1f);
            var sh = Shader.Find("Backrooms/TvScreen") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(sh) { name = name };
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", new Color(0.9f, 0.88f, 0.85f));
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.renderingLayerMask = uint.MaxValue;
            return q;
        }

        // ---------------- Boxes ----------------

        /// <summary>A box's fixed id (co-op games agree on it): its client (whose tape it has) and its number.</summary>
        public static ulong BoxId(int client, int n) => 0xC0A0000000000000ul | ((ulong)(client & 0xF) << 40) | ((ulong)(uint)n & 0xFFFFFFFFFFul);
        /// <summary>A client's box of a given shape (BoxShapes: bits 44-46 of the id; 0, the old box, on boxes saved before).</summary>
        public static ulong BoxId(int client, int n, int shape) => BoxId(client, n) | ((ulong)(shape & 7) << 44);
        public static int BoxShape(ulong id) => Mathf.Min((int)((id >> 44) & 7), BoxShapes.Length - 1);
        public static Vector3 BoxSize(ulong id) => BoxShapes[BoxShape(id)].size;

        /// <summary>
        /// The clients' boxes (2026-11-08, Docs/GameDesign.md "Carrying boxes"): size (m) and weight (kg). Light, normal and
        /// heavy, against what you can hold in your arms (about 20 kg: three light, two normal or one heavy).
        /// 0 the WAREHOUSE's plain medium box (and every box saved before), 1 OFFICE SUPPLY's small one, 2 the PRINT SHOP's
        /// box of paper (small and heavy), 3 the FURNITURE STORE's long flat flat-pack, 4 OFFICE SUPPLY's medium one.
        /// </summary>
        public static readonly (Vector3 size, float kg)[] BoxShapes =
        {
            (new Vector3(0.5f, 0.36f, 0.4f), 8f),
            (new Vector3(0.34f, 0.24f, 0.28f), 5f),
            (new Vector3(0.43f, 0.27f, 0.3f), 12f),
            (new Vector3(1.0f, 0.12f, 0.45f), 13f),
            (new Vector3(0.46f, 0.32f, 0.36f), 7.5f),
        };

        /// <summary>The shape of a new box of a client's (OFFICE SUPPLY's lots small or medium, by turns).</summary>
        public static int ShapeFor(int client, int n) => client == 1 ? 2 : client == 2 ? (n % 2 == 0 ? 1 : 4) : client == 3 ? 3 : 0;
        public static int BoxClient(ulong id) => (int)((id >> 40) & 0x3);
        public static bool IsBoxId(ulong id) => (id >> 48) == 0xC0A0ul;

        /// <summary>Is this a box of a batch (waiting, coming in, stored or due back)?</summary>
        public bool IsBox(ulong id)
        {
            if (D == null || !IsBoxId(id)) return false;
            foreach (var b in D.batches) if (b.boxes.Contains(id)) return true;
            return false;
        }

        static readonly Material[] s_tape = new Material[5];

        /// <summary>
        /// The tape for a client's box or a kit (their colour; Overflow's amber on a kit), or null for any other box. Made in
        /// code, so a save or a co-op message cannot name it: every box is given it again from its id when it is built
        /// (MovableProp.BuildCardboard). Before 2026-11-09 a box that had been saved, or sent to a friend, came back with no
        /// tape material and drew magenta.
        /// </summary>
        public static Material Tape(ulong id)
        {
            bool kit = Improvements.IsKit(id);
            if (!kit && !IsBoxId(id)) return null;
            int t = kit ? 4 : BoxClient(id);
            if (s_tape[t]) return s_tape[t];
            var streamer = Instance ? Instance.streamer : FindAnyObjectByType<WorldStreamer>();
            var a = streamer ? streamer.assets : null;
            if (!a || !a.boxTape) return null;
            s_tape[t] = new Material(a.boxTape) { name = "Box tape " + t, hideFlags = HideFlags.DontSave };
            var col = kit ? new Color(0.94f, 0.64f, 0.12f) : ClientColours[t];
            if (s_tape[t].HasProperty("_BaseColor")) s_tape[t].SetColor("_BaseColor", col * 1.6f);
            return s_tape[t];
        }

        /// <summary>A contract's box (or an improvement's kit), as a movable cardboard box, or null. Its tape is its client's
        /// colour (Overflow's amber on a kit).</summary>
        public static MovableSpec BoxSpec(ulong id, Vector3 at, float yaw)
        {
            var streamer = Instance ? Instance.streamer : FindAnyObjectByType<WorldStreamer>();
            var a = streamer ? streamer.assets : null;
            var board = a && a.cardboard ? a.cardboard : null;
            if (!board) return null;
            bool kit = Improvements.IsKit(id);
            int t = kit ? 4 : BoxClient(id);
            Tape(id);
            return new MovableSpec
            {
                id = id, kind = MovableKind.Box, renderMask = uint.MaxValue, proxyMaterial = a.baseboard, position = at, yaw = yaw,
                size = kit ? Kits.Size(Kits.KindOf(id)) : BoxSize(id), mass = kit ? 0f : BoxShapes[BoxShape(id)].kg,
                variant = (int)(id & 0x3FF) * 8 + 1, cardboard = true,
                materials = new[] { board, s_tape[t] ? s_tape[t] : board, a.boxPrint ? a.boxPrint : board },
            };
        }

        /// <summary>A seam's part in the work for its chute: 1 a lot is waiting behind it (boxes come out), else 0.</summary>
        public int RoleOf(ulong seam)
        {
            if (D == null) return 0;
            int kind = -1;
            foreach (var s in _seams) if (s.id == seam) { kind = s.kind; break; }
            if (kind < 0) return 0;
            foreach (var b in D.batches) if (b.client == kind && b.state <= 1) return 1;
            return 0;
        }

        /// <summary>A fitted chute's next box from its seam's place: one of a lot still on the pickup pallet.</summary>
        public bool TakeFromStack(ulong seam, out ulong id, out int index)
        {
            id = 0; index = 0;
            var mgr = MovablePropManager.Instance;
            if (D == null || !mgr) return false;
            int kind = -1;
            foreach (var s in _seams) if (s.id == seam) { kind = s.kind; break; }
            if (kind < 0) return false;
            foreach (var b in D.batches)
            {
                if (b.client != kind || b.state > 1) continue;
                for (int i = 0; i < b.boxes.Count; i++)
                {
                    if (!mgr.Where(b.boxes[i], out var p, out bool held) || held) continue;
                    if ((p - SeamPlan.Stack(kind, i, BoxSize(b.boxes[i]))).sqrMagnitude > 0.3f * 0.3f) continue;
                    id = b.boxes[i]; index = i;
                    return true;
                }
            }
            return false;
        }

        /// <summary>A fragile box hit too hard (FragileBox): broken, out of its batch, $15 off what it would have paid.</summary>
        public void Broke(ulong id)
        {
            if (D == null || !D.fragile.Remove(id)) return;
            foreach (var b in D.batches)
                if (b.boxes.Remove(id)) { b.count = Mathf.Max(0, b.count - 1); b.completion = Mathf.Max(0, b.completion - 15); }
            D.batches.RemoveAll(b => b.boxes.Count == 0 && b.state > 0);
            D.stored.Remove(id);
            MovablePropManager.Instance?.Forget(id);
            CamcorderOSD.Notice("FRAGILE BOX BROKEN - $15 OFF THE PAY");
            Beep(false);
            _dirty = true;
        }

        // ---------------- Every frame ----------------

        readonly List<Rect> _storage = new List<Rect>();
        int _storageFor = -1;

        Vector3 LiftPoint => LeasePlan.Current >= 0 ? LeasePlan.DistrictOrigin(LeasePlan.Centre(LeasePlan.Current)) : Vector3.zero;

        void Update()
        {
            if (D == null) return;
            SlipFrame();
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.5f;
            EnsureFloorStart();
            EnsureGuideTape();
            BatchTick();
            if (WorkOrders.Active) WorkOrders.Tick(_seams);
            if (_dirty) { _dirty = false; DrawBoard(); }
            else DrawBoardClock();
        }

        void EnsureStorage()
        {
            int sig = LeasePlan.Current * 64 + LeasePlan.Wings;
            if (_storageFor == sig) return;
            _storage.Clear();
            _seams.Clear();
            if (!streamer || streamer.World == null || LeasePlan.Current < 0) return;
            LeaseSeams.Survey(streamer.World, LeasePlan.Current, _seams, _storage);
            _storageFor = sig;
        }

        readonly List<LeaseSeams.SeamInfo> _seams = new List<LeaseSeams.SeamInfo>();

        // ---------------- The starter shelf, the knocked-over ones (2026-11-08) ----------------

        /// <summary>
        /// Puts out what the floor starts with (once a world): the starter shelf by the lift with a desk lamp in front of it,
        /// plugged into the outlet beside it (LeaseSeams.StarterSpot); and in each leased district, the knocked-over
        /// shelves lying in its old storerooms (LeaseSeams.FoundSpots), once each district is leased.
        /// </summary>
        GameObject _tape;
        int _tapeFor = -1;

        /// <summary>The company's tape from the lift to the first seam (GuideTape), laid once the floor is known.</summary>
        void EnsureGuideTape()
        {
            int slot = LeasePlan.Current;
            if (slot == _tapeFor || !streamer || streamer.World == null || slot < 0) return;
            if (_tape) Destroy(_tape);
            _tapeFor = slot;
            EnsureStorage();
            foreach (var s in _seams)
            {
                if (!LeaseSeams.IsFirst(s, slot)) continue;
                var front = s.position + Quaternion.Euler(0f, s.yaw, 0f) * Vector3.forward * 0.9f;
                _tape = GuideTape.Build(streamer.World, slot, LeaseSeams.LiftDoor(slot), front, ClientColours[LeaseSeams.FirstKind]);
                if (!_tape) Debug.LogWarning("[Contracts] No way along the floor from the lift to the first seam for the tape.");
                return;
            }
            Debug.LogWarning("[Contracts] This floor has no first seam (LeaseSeams.FirstSeamDistrict).");
        }

        void EnsureFloorStart()
        {
            if (!streamer || streamer.World == null || LeasePlan.Current < 0 || !MovablePropManager.Instance) return;
            if (!FittingModels.Ready) return;
            int slot = LeasePlan.Current;
            if (!D.starterSet)
            {
                D.starterSet = true;
                var c = LeasePlan.Centre(slot);
                var L = streamer.World.District(new Vector3Int(c.x, 0, c.y));
                if (LeaseSeams.StarterSpot(L, out var shelf, out float yaw, out var outlet))
                {
                    var o = LeasePlan.DistrictOrigin(c);
                    var at = o + new Vector3(shelf.x, 0f, shelf.y);
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    Shelves.Add(ShelfTypes.Standard, at, rot);
                    // The lamp a metre out in front, a little toward the outlet, its shade toward the shelf.
                    var toOutlet = o + new Vector3(outlet.x, 0f, outlet.y) - at;
                    var side = Vector3.ProjectOnPlane(toOutlet, rot * Vector3.forward).normalized;
                    var lampAt = at + rot * Vector3.forward * 1.0f + side * 0.55f + Vector3.up * 0.01f;
                    var lamp = DeskLamp.Create(lampAt, rot * Quaternion.Euler(0f, 180f, 0f));
                    lamp.startPlugged = true;
                }
            }
            foreach (var d in LeasePlan.Districts(slot))
            {
                bool done = false;
                for (int i = 0; i + 1 < D.foundIn.Count; i += 2) if (D.foundIn[i] == d.x && D.foundIn[i + 1] == d.y) { done = true; break; }
                if (done) continue;
                D.foundIn.Add(d.x); D.foundIn.Add(d.y);
                var spots = new List<(Vector2 at, float yaw)>();
                LeaseSeams.FoundSpots(streamer.World.District(new Vector3Int(d.x, 0, d.y)), d, spots);
                var o = LeasePlan.DistrictOrigin(d);
                foreach (var (p, yaw) in spots)
                {
                    // Lying on its back across the room, its middle at the spot (as it stood: facing yaw).
                    var up = Quaternion.Euler(0f, yaw, 0f);
                    var t = ShelfTypes.All[ShelfTypes.Standard];
                    var at = o + new Vector3(p.x, t.size.z * 0.5f, p.y) + up * Vector3.forward * (t.size.y * 0.5f);
                    Shelves.Add(ShelfTypes.Standard | ShelfTypes.Broken, at, up * Quaternion.Euler(-90f, 0f, 0f));
                }
            }
        }

        // ---------------- The hub's board and the pay window ----------------

        const int BW = 480, BH = 252;
        Texture2D _boardTex;
        Color32[] _px;
        ElevatorButton _payButton;
        AudioSource _beep;
        static Material s_plate, s_on, s_off;

        void BuildHub()
        {
            MakeButtonMaterials();
            // The board's face (HubBoard: 2.4 x 1.3 on the lobby's north wall, its foot 0.7 m up, facing south).
            float face = OfficeBiome.WallThickness * 0.5f;
            var boardFoot = HubPlan.World(11.0f, 14.4f - face, 0.7f);
            var rot = Quaternion.Euler(0f, 180f, 0f);
            _boardTex = Screen(transform, "ContractsBoardScreen", boardFoot, rot, BW, BH, out _px);
            // The pay window (HubPayWindow at x 13.2, 1.2 wide), at the ledge's height: the last trip's slip again.
            var payAt = HubPlan.World(14.05f, 14.4f - face, 1.25f);
            _payButton = Button(transform, "PaySlip", payAt, rot, PayWindow);
            Label(transform, "PaySlipLabel", "PAY SLIPS", payAt + new Vector3(0f, 0.17f, -0.004f), rot, 0.32f);
            // (The punch clock on the lift hall's wall is the company's old days now: decoration, no button. 2026-11-09.)
            _beep = Sound(transform, "ContractsBeep", HubPlan.World(12.0f, 14.2f, 1.2f), 8f);
            _slipSound = Sound(transform, "SlipSound", HubPlan.World(14.05f, 14.2f, 1.2f), 9f);
            _slipSound.spatialBlend = 0f;                       // (printed wherever you come up: heard as the camcorder hears it)
        }

        // ---- Shared with the improvements board ----

        internal static void MakeButtonMaterials()
        {
            if (s_plate != null) return;
            var lit = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            Material M(string n, Color c, Color? e = null)
            {
                var m = new Material(lit) { name = n };
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.4f);
                if (e.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", e.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
                return m;
            }
            s_plate = M("Contracts_Plate", new Color(0.13f, 0.12f, 0.11f));
            s_on = M("Contracts_ButtonOn", new Color(1f, 0.7f, 0.25f), new Color(3.2f, 1.7f, 0.35f));
            s_off = M("Contracts_ButtonOff", new Color(0.55f, 0.5f, 0.42f), new Color(0.08f, 0.06f, 0.03f));
        }

        /// <summary>A flap board's screen (2.3 x 1.2 m) on a HubBoard, its foot at boardFoot.</summary>
        internal static Texture2D Screen(Transform parent, string name, Vector3 boardFoot, Quaternion rot, int w, int h, out Color32[] px)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.SetPositionAndRotation(boardFoot + rot * new Vector3(0f, 0.65f, 0.106f), rot * Quaternion.Euler(0f, 180f, 0f));
            q.transform.localScale = new Vector3(2.3f, 1.2f, 1f);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = name };
            px = new Color32[w * h];
            var sh = Shader.Find("Backrooms/TvScreen") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(sh) { name = name };
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", new Color(1.15f, 1.1f, 1.05f));
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.renderingLayerMask = uint.MaxValue;
            return tex;
        }

        internal static ElevatorButton Button(Transform parent, string name, Vector3 at, Quaternion rot, System.Action onPress)
        {
            MakeButtonMaterials();
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(at, rot);
            Part(root.transform, "Plate", new Vector3(0f, 0f, 0.008f), new Vector3(0.12f, 0.16f, 0.016f), s_plate);
            var lamp = Part(root.transform, "Lamp", new Vector3(0f, 0.0f, 0.02f), new Vector3(0.06f, 0.06f, 0.014f), s_off);
            var trig = root.AddComponent<BoxCollider>();
            trig.isTrigger = true; trig.center = new Vector3(0f, 0f, 0.04f); trig.size = new Vector3(0.2f, 0.24f, 0.12f);
            var b = root.AddComponent<ElevatorButton>();
            b.Init(lamp.GetComponent<Renderer>(), s_on, s_off);
            b.onPress = onPress;
            return b;
        }

        static Transform Part(Transform parent, string name, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off; r.renderingLayerMask = uint.MaxValue;
            return go.transform;
        }

        internal static AudioSource Sound(Transform parent, string name, Vector3 at, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = at;
            var a = ItemModels.Source(go, range);
            Audio.SoundRoute.Add(a);
            return a;
        }

        internal void Beep(bool ok)
        {
            if (!_beep) return;
            _beep.PlayOneShot(ok ? Tone(880f, 660f, 0.22f) : Tone(220f, 180f, 0.3f), 0.5f);
        }

        static readonly Dictionary<(float, float, float), AudioClip> s_tones = new Dictionary<(float, float, float), AudioClip>();
        internal static AudioClip Tone(float f0, float f1, float len)
        {
            if (s_tones.TryGetValue((f0, f1, len), out var clip)) return clip;
            int sr = 44100, n = (int)(sr * len);
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr, f = t < len * 0.5f ? f0 : f1;
                float env = Mathf.Clamp01(t * 200f) * Mathf.Clamp01((len - t) * 30f);
                d[i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * t)) * 0.18f * env;
            }
            clip = AudioClip.Create("ContractTone", n, 1, sr, false);
            clip.SetData(d, 0);
            return s_tones[(f0, f1, len)] = clip;
        }

        // ---- Painting the board ----

        internal static readonly Color32 Ground = new Color32(20, 18, 15, 255), Flap = new Color32(36, 33, 30, 255), Amber = new Color32(245, 190, 80, 255),
            Dim = new Color32(150, 118, 60, 255), Choc = new Color32(59, 42, 30, 255), Green = new Color32(140, 210, 110, 255), Red = new Color32(225, 90, 60, 255),
            Locked = new Color32(96, 82, 62, 255), LockedSel = new Color32(190, 150, 80, 255);

        static Color32 Client(int k) => (Color32)ClientColours[Mathf.Clamp(k, 0, 3)];
        static string Name(int k) => Clients[Mathf.Clamp(k, 0, 3)];

        // ---- The board (2026-11-09): your batches, a line each, with how long is left; along the foot what is safe, what
        // is at risk, what is due back and what is in hand; and what to do next. The work orders: the camcorder's tracker.

        struct BoardRow { public string text, right; public Color32 chip, rightInk; public bool dim; }
        readonly List<BoardRow> _rows = new List<BoardRow>();

        /// <summary>A batch as a line (2026-11-09, the owner: "BEHIND THE SEAM ... WAITING" was not clear): whose and how many
        /// on the left; on the right, in plain words, what to do with it now and how long is left.</summary>
        BoardRow BatchRow(BatchData b)
        {
            var r = new BoardRow { chip = Client(b.client), rightInk = Amber, text = $"{Name(b.client)} X{b.count}" };
            switch (b.state)
            {
                case 0: r.right = "READY TO PICK UP"; r.dim = true; break;
                case 1:
                {
                    var mgr = MovablePropManager.Instance;
                    int left = b.boxes.Count - (mgr ? SafeCount(b, mgr) : 0);
                    r.right = left > 0 ? $"SHELVE {left} MORE" : "ALL SHELVED";
                    break;
                }
                case 2: r.right = "STORED " + MinSec(b.left); r.rightInk = Green; break;
                default:
                    bool late = b.since > b.window;
                    r.right = late ? "LATE - TAKE BACK" : "TAKE BACK " + MinSec(b.window - b.since);
                    r.rightInk = late ? Red : Amber;
                    break;
            }
            return r;
        }

        void Rows()
        {
            _rows.Clear();
            if (D == null) return;
            // Due back first, then stored (soonest first), coming in, waiting.
            var list = new List<BatchData>(D.batches);
            list.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            foreach (var b in list)
            {
                if (b.state == 0 && !D.seamsFound.Contains(b.client)) continue;      // (a client not found yet: not on the board)
                _rows.Add(BatchRow(b));
                if (_rows.Count == 4) break;
            }
            float Rank(BatchData b) => b.state == 3 ? -1000f + (b.window - b.since) : b.state == 2 ? b.left : b.state == 1 ? 100000f : 200000f;
        }

        /// <summary>What to do next (the board's green line and the tracker's first): one plain step.</summary>
        public string NextStep
        {
            get
            {
                if (D == null) return "";
                if (!D.tapeWatched && WorkOrders.Active && D.woStage == 0) return "WATCH THE TAPE - SIT DOWN IN THE TRAINING ROOM";
                foreach (var b in D.batches) if (b.state == 3) return b.since > b.window ? $"{Name(b.client)} IS LATE - TAKE THEIR BOXES BACK TO THEIR SEAM" : $"TAKE {Name(b.client)}'S BOXES BACK TO THEIR SEAM";
                var mgr = MovablePropManager.Instance;
                foreach (var b in D.batches)
                    if (b.state == 1)
                    {
                        int left = b.boxes.Count - (mgr ? SafeCount(b, mgr) : 0);
                        return $"PUT {left} MORE {Name(b.client)} BOX{(left == 1 ? "" : "ES")} ON A LIT SHELF";
                    }
                if (D.batchesIn == 0) return "FOLLOW THE BLUE TAPE FROM THE LIFT TO TALLMORE'S SEAM";
                int risk = AtRisk;
                if (risk > 0) return $"{risk} BOX{(risk > 1 ? "ES" : "")} LEFT OUT - PUT THEM ON A LIT SHELF";
                foreach (int k in D.seamsFound)
                {
                    if (NeedOf(k) != null || OnHold(k)) continue;
                    var w = Waiting(k);
                    if (w != null) return $"MORE WORK: {Name(k)}'S {w.count} BOXES ARE READY AT THEIR SEAM";
                }
                return "FIND ANOTHER SEAM OUT ON THE FLOOR";
            }
        }

        void DrawBoard()
        {
            if (_px == null || D == null) return;
            for (int i = 0; i < _px.Length; i++) _px[i] = Ground;
            DrawRowsOnly();
            _rowsAt = Time.time;
            if (_payButton) _payButton.SetLit(!string.IsNullOrEmpty(D.lastSlip));
            DrawBoardClock();
        }

        float _rowsAt;

        /// <summary>What changes by the second (every half second): the header's date, the countdowns, the four numbers
        /// along the foot (safe, at risk, due back, in hand), and what to do next.</summary>
        void DrawBoardClock()
        {
            if (_px == null || D == null) return;
            // The countdowns move: the rows again every second.
            if (Time.time - _rowsAt > 1f) { _rowsAt = Time.time; DrawRowsOnly(); }
            PixelFont.Fill(_px, BW, BH, 0, 0, BW, 34, new Color32(240, 162, 30, 255));
            PixelFont.Draw(_px, BW, BH, "WORK BOARD", 10, 7, 3, Choc);
            string date = DateText;
            PixelFont.Draw(_px, BW, BH, date, BW - 10 - PixelFont.Width(date, 2), 4, 2, Choc);
            string sub = Running ? "BUSINESS TIME RUNNING" : "TIME STOPPED - YOU'RE OFF THE FLOOR";
            PixelFont.Draw(_px, BW, BH, sub, BW - 10 - PixelFont.Width(sub, 1), 23, 1, Choc);
            PixelFont.Fill(_px, BW, BH, 0, 184, BW, BH, Ground);
            int safe = SafeNow, risk = AtRisk, due = 0, target = 0;
            foreach (var b in D.batches) if (b.state == 3) due++;
            if (WorkOrders.Active) foreach (int i in WorkOrders.Open()) if (WorkOrders.All[i].goal == WorkOrders.Goal.StoredAtOnce) target = Mathf.Max(target, WorkOrders.All[i].target);
            Cell(0, "STORED SAFE", target > 0 ? safe + "/" + target : safe.ToString(), Amber);
            Cell(1, "AT RISK", risk.ToString(), risk > 0 ? Red : Amber);
            Cell(2, "DUE BACK", due.ToString(), due > 0 ? Green : Amber);
            Cell(3, "IN HAND", "$" + D.pay, Amber);
            string next = NextStep;
            while (next.Length > 4 && PixelFont.Width(next, 1) > BW - 16) next = next.Substring(0, next.Length - 1);
            if (next.Length > 0) PixelFont.DrawCentred(_px, BW, BH, next, BW / 2, 232, 1, Green);
            PixelFont.Fill(_px, BW, BH, 0, 246, BW, 248, new Color32(217, 102, 31, 255));
            PixelFont.Fill(_px, BW, BH, 0, 248, BW, 250, new Color32(232, 178, 58, 255));
            PixelFont.Fill(_px, BW, BH, 0, 250, BW, 252, new Color32(124, 138, 46, 255));
            _boardTex.SetPixels32(_px);
            _boardTex.Apply(false);

            void Cell(int k, string label, string value, Color32 ink)
            {
                int x0 = 8 + k * 117, x1 = x0 + 112;
                PixelFont.Fill(_px, BW, BH, x0, 188, x1, 226, Flap);
                PixelFont.DrawCentred(_px, BW, BH, label, (x0 + x1) / 2, 192, 1, Dim);
                PixelFont.DrawCentred(_px, BW, BH, value, (x0 + x1) / 2, 205, 2, ink);
            }
        }

        /// <summary>The rows' area painted again (their countdowns), without the rest.</summary>
        void DrawRowsOnly()
        {
            PixelFont.Fill(_px, BW, BH, 0, 34, BW, 184, Ground);
            Rows();
            PixelFont.Draw(_px, BW, BH, "YOUR BATCHES", 12, 41, 1, Dim);
            if (_rows.Count == 0) PixelFont.DrawCentred(_px, BW, BH, "NOTHING ON THE GO", BW / 2, 96, 2, Dim);
            for (int i = 0; i < _rows.Count && i < 4; i++)
            {
                var r = _rows[i];
                int y = 54 + i * 32;
                PixelFont.Fill(_px, BW, BH, 8, y - 4, BW - 8, y + 22, Flap);
                PixelFont.Fill(_px, BW, BH, 8, y - 4, 16, y + 22, r.chip);
                string text = r.text;
                int room = BW - 26 - 26 - PixelFont.Width(r.right ?? "", 2);
                while (text.Length > 4 && PixelFont.Width(text, 2) > room) text = text.Substring(0, text.Length - 1);
                PixelFont.Draw(_px, BW, BH, text, 26, y + 2, 2, r.dim ? Dim : Amber);
                if (!string.IsNullOrEmpty(r.right)) PixelFont.Draw(_px, BW, BH, r.right, BW - 18 - PixelFont.Width(r.right, 2), y + 2, 2, r.rightInk);
            }
        }

        /// <summary>Redraw the board (something it shows changed elsewhere: something bought).</summary>
        public void Changed() => _dirty = true;
    }
}
