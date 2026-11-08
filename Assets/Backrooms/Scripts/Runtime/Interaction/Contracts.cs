using System.Collections.Generic;
using System.Threading.Tasks;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Contracts (2026-11-07, Docs/ContractFloorsPlan.md): the working day. Every world has one floor (LeasePlan), kept for
    /// good; every day the hub's board posts three jobs on it:
    /// <list type="bullet">
    /// <item>STORE: a client's boxes wait on the pickup pallet in their place (through their seam): bring them in and put them
    ///   in the floor's storage. They stay there, and earn rent every day, until the client wants them back.</item>
    /// <item>SHIP: carry a client's boxes from their place to another place's DELIVERIES bay.</item>
    /// <item>RETURN: a client wants stored boxes back: take that many of theirs (their tape's colour) to their place's
    ///   DELIVERIES bay.</item>
    /// </list>
    /// Take as many as you like with the buttons under the rows, ride the lift down, do them before closing, ride back up
    /// and press PAY at the window. When the day's jobs are done (or closing has come), PAY ends the day: the rent on what
    /// is stored is paid, and tomorrow's board goes up. The money buys improvements (Improvements), one a night.
    /// <para>Office hours: the shift starts when you first step out on the floor, 9 AM on the camcorder's clock, and closes
    /// at 5 PM, 15 minutes later (later with overtime bought). At closing the floor's lights die and the seams shut; a minute
    /// later whoever is still down there blacks out and wakes on their cot, and what was not done is lost.</para>
    /// <para>Co-op: what is posted depends only on the world's seed, the day and what is stored (every game posts the same
    /// board), the buttons are shared presses (NetHooks.Poke), and the boxes have fixed ids (shared like any moved prop).</para>
    /// </summary>
    public class Contracts : MonoBehaviour
    {
        public static Contracts Instance { get; private set; }
        public WorldStreamer streamer;

        /// <summary>Nine to five, in seconds (an hour of overtime is an eighth of it again).</summary>
        public const float DaySeconds = 15f * 60f;
        const int HourStart = 9, HourEnd = 17;
        /// <summary>The clients (the four places: LeaseSeams.Kinds): their names on the board, and their tape's colour.</summary>
        public static readonly string[] Clients = { "TALLMORE", "QUILLBY", "PAPERLARK", "BELLWEATHER" };
        public static readonly Color[] ClientColours =
        {
            new Color(0.23f, 0.43f, 0.77f), new Color(0.78f, 0.2f, 0.16f), new Color(0.26f, 0.58f, 0.3f), new Color(0.58f, 0.25f, 0.5f),
        };

        WorldSaveData D => GameSession.Data;
        Task<List<ContractData>> _posting;
        int _postingDay = -1;
        float _nextCheck, _closedAt = -1f;
        bool _dirty = true;

        // ---------------- The state ----------------

        public float ShiftSeconds => DaySeconds * (1f + (D != null ? D.overtime : 0) / 8f);
        public float ClosingGrace => 60f + 60f * (D != null ? D.backupLights : 0);
        public int RentRate => 2 + (D != null ? D.rentReviews : 0);

        /// <summary>A job taken today and not over (being done, or done and not paid).</summary>
        public bool HasFloor { get { if (D == null) return false; foreach (var c in D.board) if (c.state == 1 || c.state == 2) return true; return false; } }
        public bool ShiftOn => D != null && D.shift >= 0f;
        public bool Closed => ShiftOn && D.shift >= ShiftSeconds;
        int Open { get { int n = 0; if (D != null) foreach (var c in D.board) if (c.state == 1) n++; return n; } }

        float _offSince;

        /// <summary>The time of day for the camcorder's clock: the working day's date (from March 11 1996); off shift the
        /// morning before it (from 8:30, stopping at 8:59), on shift the shift's time.</summary>
        public System.DateTime? Clock
        {
            get
            {
                if (D == null) return null;
                if (!ShiftOn) return new System.DateTime(1996, 3, 11, 8, 30, 0).AddDays(D.day - 1).AddSeconds(Mathf.Min(Time.time - _offSince, 1740f));
                float hours = Mathf.Min(D.shift, ShiftSeconds + 600f) / DaySeconds * (HourEnd - HourStart);
                return new System.DateTime(1996, 3, 11, HourStart, 0, 0).AddDays(D.day - 1).AddHours(hours);
            }
        }

        string ClosingTime => (HourEnd - 12 + (D != null ? D.overtime : 0)) + " PM";

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; LightZones.ClosingAt = -1f; }

        void Start()
        {
            if (D == null) { enabled = false; return; }
            if (D.day < 1) D.day = 1;
            _offSince = Time.time;
            BuildHub();
            D.lease = LeasePlan.Current;
            if (Closed) { LightZones.ClosingAt = Time.time - 30f; _closedAt = Time.time - ClosingGrace * 0.5f; }
            NoclipPassage.SeamsShut = Closed;
            EnsurePosted();
        }

        // ---------------- Posting ----------------

        void EnsurePosted()
        {
            if (D.board.Count > 0 || _posting != null) return;
            var world = streamer ? streamer.World : null;
            if (world == null || LeasePlan.Current < 0) return;
            int seed = streamer.Seed, slot = LeasePlan.Current, day = D.day;
            var stored = StoredByClient();
            _postingDay = day;
            _posting = Task.Run(() => Post(world, seed, slot, day, stored));
            _dirty = true;
        }

        int[] StoredByClient()
        {
            var n = new int[4];
            foreach (var id in D.stored) n[BoxClient(id)]++;
            return n;
        }

        /// <summary>The day's three jobs on the floor's leased parts (its seams and storage rooms: LeaseSeams.Survey).</summary>
        static List<ContractData> Post(WorldGenerator world, int seed, int slot, int day, int[] stored)
        {
            var list = new List<ContractData>();
            var seams = new List<LeaseSeams.SeamInfo>();
            var storage = new List<Rect>();
            LeaseSeams.Survey(world, slot, seams, storage);
            if (seams.Count == 0) return list;
            var rng = new System.Random(unchecked(seed * 31 + day * 7919 + 0x0C0A));
            var lift = LeasePlan.DistrictOrigin(LeasePlan.Centre(slot));
            var used = new HashSet<ulong>();
            var perKind = new int[4];
            var left = (int[])stored.Clone();
            for (int k = 0; k < 3; k++)
                for (int tries = 0; tries < 8; tries++)
                {
                    var c = Make(rng, day, seams, storage.Count > 0, left, used, perKind, lift);
                    if (c == null) continue;
                    c.slot = slot;
                    list.Add(c);
                    break;
                }
            return list;
        }

        static ContractData Make(System.Random rng, int day, List<LeaseSeams.SeamInfo> seams, bool hasStorage, int[] stored,
                                 HashSet<ulong> used, int[] perKind, Vector3 lift)
        {
            // Which kind: storing most, shipping when there are two places, returns once there is something stored.
            bool canShip = false;
            for (int i = 0; i < seams.Count && !canShip; i++) for (int j = 0; j < seams.Count; j++) if (seams[i].kind != seams[j].kind) { canShip = true; break; }
            bool canReturn = false;
            for (int k = 0; k < 4; k++) if (stored[k] >= 2 && seams.Exists(s => s.kind == k)) canReturn = true;
            double r = rng.NextDouble();
            int kind = canReturn && r < 0.28 ? 2 : canShip && r < 0.6 ? 1 : hasStorage ? 0 : canShip ? 1 : canReturn ? 2 : -1;
            if (kind < 0) return null;
            int growth = Mathf.Min(day - 1, 12);
            var c = new ContractData { kind = kind };
            LeaseSeams.SeamInfo Pick(System.Predicate<LeaseSeams.SeamInfo> ok)
            {
                var pool = seams.FindAll(s => ok(s) && !used.Contains(s.id));
                if (pool.Count == 0) pool = seams.FindAll(ok);
                return pool.Count == 0 ? default : pool[rng.Next(pool.Count)];
            }
            float dist;
            switch (kind)
            {
                case 0:
                {
                    var s = Pick(x => perKind[x.kind] < 24);
                    if (s.id == 0) return null;
                    c.from = s.id; c.fromKind = s.kind;
                    c.count = Mathf.Min(4 + rng.Next(0, 3) + growth / 3, 36 - perKind[s.kind]);
                    perKind[s.kind] += c.count;
                    dist = Vector3.Distance(lift, s.position);
                    break;
                }
                case 1:
                {
                    var a = Pick(x => perKind[x.kind] < 24);
                    if (a.id == 0) return null;
                    var b = Pick(x => x.kind != a.kind);
                    if (b.id == 0) return null;
                    c.from = a.id; c.fromKind = a.kind; c.to = b.id; c.toKind = b.kind;
                    c.count = Mathf.Min(3 + rng.Next(0, 3) + growth / 4, 36 - perKind[a.kind]);
                    perKind[a.kind] += c.count;
                    dist = Vector3.Distance(lift, a.position) + Vector3.Distance(a.position, b.position) * 0.6f;
                    break;
                }
                default:
                {
                    var b = Pick(x => stored[x.kind] >= 2);
                    if (b.id == 0) return null;
                    c.to = b.id; c.toKind = c.fromKind = b.kind;
                    c.count = Mathf.Min(stored[b.kind], 2 + rng.Next(0, 3) + growth / 5);
                    stored[b.kind] -= c.count;
                    dist = Vector3.Distance(lift, b.position);
                    break;
                }
            }
            if (c.count <= 0) return null;
            used.Add(c.from); if (c.to != 0) used.Add(c.to);
            float perBox = kind == 0 ? 10f : kind == 1 ? 14f : 12f;
            c.pay = Mathf.RoundToInt((30f + perBox * c.count) * (1f + dist / 300f) / 5f) * 5;
            return c;
        }

        // ---------------- Taking one, being paid ----------------

        /// <summary>The button under a row of the board.</summary>
        public void Take(int i)
        {
            if (D == null || i < 0 || i >= D.board.Count) return;
            var c = D.board[i];
            if (c.state != 0) { Beep(false); return; }
            if (ShiftOn) { CamcorderOSD.Notice("NO NEW JOBS DURING THE SHIFT"); Beep(false); return; }
            c.state = 1;
            NoclipPassage.SeamsShut = false;
            if (c.kind != 2) StackBoxes(c);
            int n = 0; foreach (var x in D.board) if (x.state == 1) n++;
            CamcorderOSD.Notice(n == 1 ? "JOB TAKEN - TAKE THE LIFT DOWN" : $"{n} JOBS TAKEN - TAKE THE LIFT DOWN");
            Beep(true);
            _dirty = true;
        }

        /// <summary>The pay window's button: pay for what is done; at the end of the day, the rent too, and the day ends.</summary>
        public void Collect()
        {
            if (D == null) return;
            int paid = 0, jobs = 0;
            foreach (var c in D.board)
            {
                if (c.state != 2) continue;
                paid += c.pay; jobs++;
                c.state = 4;
                D.jobsDone++;
                var mgr = MovablePropManager.Instance;
                if (c.kind == 0) foreach (var id in c.boxes) { if (!D.stored.Contains(id)) D.stored.Add(id); }      // kept, for rent
                else if (c.kind == 1) { if (mgr) foreach (var id in c.boxes) mgr.Forget(id); }                       // delivered
                else foreach (var id in c.boxes) { D.stored.Remove(id); if (mgr) mgr.Forget(id); }                  // handed back
            }
            D.pay += paid; D.earned += paid;
            if (ShiftOn && (Closed || Open == 0)) { EndDay(paid); return; }
            if (paid > 0) { CamcorderOSD.Notice(jobs == 1 ? $"PAID ${paid}" : $"PAID ${paid} FOR {jobs} JOBS"); Beep(true); }
            else if (Open > 0) { CamcorderOSD.Notice(ShiftOn ? $"STILL OPEN - {Open} JOB{(Open > 1 ? "S" : "")} - CLOSING {ClosingTime}" : "TAKE THE LIFT DOWN"); Beep(false); }
            else { CamcorderOSD.Notice($"NOTHING TO COLLECT - ${D.pay} IN HAND"); Beep(false); }
            _dirty = true;
        }

        void EndDay(int paid)
        {
            var mgr = MovablePropManager.Instance;
            foreach (var c in D.board)
                if (c.state == 3 && c.kind != 2 && mgr) foreach (var id in c.boxes) mgr.Forget(id);   // (the client takes them back)
            int kept = CountKept();
            int rent = kept * RentRate;
            D.pay += rent; D.earned += rent;
            CamcorderOSD.Notice($"DAY {D.day} DONE - PAY ${paid} - RENT ${rent} ({kept} BOXES)");
            Beep(true);
            D.day++;
            _offSince = Time.time;
            D.board.Clear();
            D.shift = -1f;
            LightZones.ClosingAt = -1f;
            NoclipPassage.SeamsShut = false;
            _closedAt = -1f;
            _dirty = true;
            EnsurePosted();
            Improvements.Instance?.Changed();
        }

        // ---------------- Boxes ----------------

        /// <summary>A box's fixed id (co-op games agree on it): its client (whose tape it has) and its number.</summary>
        public static ulong BoxId(int client, int n) => 0xC0A0000000000000ul | ((ulong)(client & 0xF) << 40) | ((ulong)(uint)n & 0xFFFFFFFFFFul);
        public static int BoxClient(ulong id) => (int)((id >> 40) & 0x3);
        public static bool IsBoxId(ulong id) => (id >> 48) == 0xC0A0ul;

        /// <summary>Is this one of today's taken jobs' boxes, or a stored one?</summary>
        public bool IsBox(ulong id)
        {
            if (D == null || !IsBoxId(id)) return false;
            if (D.stored.Contains(id)) return true;
            foreach (var c in D.board) if (c.state == 1 && c.boxes.Contains(id)) return true;
            return false;
        }

        static readonly Material[] s_tape = new Material[5];

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
            if (!s_tape[t] && a.boxTape)
            {
                s_tape[t] = new Material(a.boxTape) { name = "Box tape " + t, hideFlags = HideFlags.DontSave };
                var col = kit ? new Color(0.94f, 0.64f, 0.12f) : ClientColours[t];
                if (s_tape[t].HasProperty("_BaseColor")) s_tape[t].SetColor("_BaseColor", col * 1.6f);
            }
            return new MovableSpec
            {
                id = id, kind = MovableKind.Box, renderMask = uint.MaxValue, proxyMaterial = a.baseboard, position = at, yaw = yaw,
                size = kit ? new Vector3(0.7f, 0.5f, 0.5f) : new Vector3(0.5f, 0.36f, 0.4f), variant = (int)(id & 0x3FF) * 8 + 1, cardboard = true,
                materials = new[] { board, s_tape[t] ? s_tape[t] : board, a.boxPrint ? a.boxPrint : board },
            };
        }

        /// <summary>The job's boxes, stacked on the pickup pallet in the source seam's place (SeamPlan), after the boxes of any
        /// other job taken from there today: they appear there when its room loads (moved props: MovablePropManager), and
        /// are carried out through the seam.</summary>
        void StackBoxes(ContractData c)
        {
            var mgr = MovablePropManager.Instance;
            if (!mgr) return;
            int first = 0;
            foreach (var o in D.board) if (o != c && o.state == 1 && o.kind != 2 && o.fromKind == c.fromKind) first += o.count;
            c.boxes.Clear();
            c.sent = first;                          // (where its boxes start in the place's stacks)
            for (int i = 0; i < c.count; i++)
            {
                ulong id = BoxId(c.fromKind, D.nextBox++);
                var at = SeamPlan.Stack(c.fromKind, first + i);
                var spec = BoxSpec(id, at, 180f);
                if (spec == null) return;
                mgr.NetPlace(spec, at, Quaternion.Euler(0f, 180f, 0f));
                c.boxes.Add(id);
            }
        }

        /// <summary>0 nothing, 1 a taken job's source (boxes come out), 2 a destination (boxes go in).</summary>
        public int RoleOf(ulong seam)
        {
            if (D == null) return 0;
            int role = 0;
            foreach (var c in D.board)
            {
                if (c.state != 1) continue;
                if (c.kind != 2 && seam == c.from) return 1;
                if (c.kind != 0 && seam == c.to) role = 2;
            }
            return role;
        }

        /// <summary>A fitted chute's next box from its seam's place: one still on the pickup pallet (not being carried).</summary>
        public bool TakeFromStack(ulong seam, out ulong id, out int index)
        {
            id = 0; index = 0;
            var mgr = MovablePropManager.Instance;
            if (D == null || !mgr) return false;
            foreach (var c in D.board)
            {
                if (c.state != 1 || c.kind == 2 || c.from != seam) continue;
                for (int i = 0; i < c.boxes.Count; i++)
                {
                    if (!mgr.Where(c.boxes[i], out var p, out bool held) || held) continue;
                    if ((p - SeamPlan.Stack(c.fromKind, c.sent + i)).sqrMagnitude > 0.3f * 0.3f) continue;
                    id = c.boxes[i]; index = c.sent + i;
                    return true;
                }
            }
            return false;
        }

        void Finish(ContractData c)
        {
            c.state = 2;
            CamcorderOSD.Notice(Open == 0 ? "ALL JOBS DONE - COLLECT YOUR PAY" : "JOB DONE");
            Beep(true);
            _dirty = true;
        }

        // ---------------- Every frame ----------------

        readonly List<Rect> _storage = new List<Rect>();
        int _storageFor = -1;

        void Update()
        {
            if (D == null) return;
            if (_posting != null && _posting.IsCompleted)
            {
                if (_posting.Status == TaskStatus.RanToCompletion && D.board.Count == 0 && D.day == _postingDay) D.board.AddRange(_posting.Result);
                else if (_posting.Exception != null) Debug.LogException(_posting.Exception);
                _posting = null;
                _dirty = true;
            }
            var player = streamer ? streamer.player : null;
            // The shift: from the first step out on the floor with a job taken.
            if (player)
            {
                if (D.shift < 0f && Open > 0 && LeasePlan.OnCurrent(player.position))
                { D.shift = 0f; CamcorderOSD.Notice($"OFFICE HOURS 9 AM - {ClosingTime}"); _dirty = true; }
                if (D.shift >= 0f && !Closed)
                {
                    D.shift += Time.deltaTime;
                    if (Closed) Closing();
                }
            }
            // A minute after closing (more with backup lights), whoever is still out there blacks out.
            if (_closedAt >= 0f && player && Time.time - _closedAt > ClosingGrace && !HubPlan.InZone(player.position)) BlackOut();
            if (Time.time >= _nextCheck)
            {
                _nextCheck = Time.time + 0.5f;
                foreach (var c in D.board)
                {
                    if (c.state != 1) continue;
                    if (c.kind == 0) CountStored(c);
                    else if (c.kind == 1) CountDelivered(c);
                    else CountReturned(c);
                }
                if (_dirty) { _dirty = false; DrawBoard(); }
                else DrawBoardClock();
            }
        }

        void EnsureStorage()
        {
            int sig = LeasePlan.Current * 64 + LeasePlan.Wings;
            if (_storageFor == sig) return;
            _storage.Clear();
            if (streamer && streamer.World != null && LeasePlan.Current >= 0) LeaseSeams.Survey(streamer.World, LeasePlan.Current, null, _storage);
            _storageFor = sig;
        }

        bool InStorage(Vector3 p)
        {
            foreach (var r in _storage) if (r.Contains(new Vector2(p.x, p.z))) return true;
            return false;
        }

        void CountStored(ContractData c)
        {
            EnsureStorage();
            var mgr = MovablePropManager.Instance;
            if (!mgr) return;
            int n = 0;
            foreach (var id in c.boxes)
                if (mgr.Where(id, out var p, out bool held) && !held && InStorage(p)) n++;
            if (n != c.done) { c.done = n; _dirty = true; }
            if (c.done >= c.count) Finish(c);
        }

        /// <summary>Stored boxes in storage (what rent is paid on).</summary>
        int CountKept()
        {
            EnsureStorage();
            var mgr = MovablePropManager.Instance;
            if (!mgr) return D.stored.Count;
            int n = 0;
            foreach (var id in D.stored) if (mgr.Where(id, out var p, out bool held) && !held && InStorage(p)) n++;
            return n;
        }

        /// <summary>Ship: the job's boxes set down in the DELIVERIES bay of the destination's place.</summary>
        void CountDelivered(ContractData c)
        {
            var mgr = MovablePropManager.Instance;
            if (!mgr) return;
            var bay = SeamPlan.Bay(c.toKind);
            int n = 0;
            foreach (var id in c.boxes)
                if (mgr.Where(id, out var p, out bool held) && !held && bay.Contains(new Vector2(p.x, p.z))) n++;
            if (n != c.done) { c.done = n; _dirty = true; }
            if (c.done >= c.count) Finish(c);
        }

        /// <summary>Return: the client's stored boxes (any of theirs) set down in their place's DELIVERIES bay.</summary>
        void CountReturned(ContractData c)
        {
            var mgr = MovablePropManager.Instance;
            if (!mgr) return;
            var bay = SeamPlan.Bay(c.toKind);
            c.boxes.Clear();
            foreach (var id in D.stored)
            {
                if (BoxClient(id) != c.toKind || c.boxes.Count >= c.count) continue;
                bool taken = false;
                foreach (var o in D.board) if (o != c && o.state == 1 && o.kind == 2 && o.boxes.Contains(id)) taken = true;
                if (!taken && mgr.Where(id, out var p, out bool held) && !held && bay.Contains(new Vector2(p.x, p.z))) c.boxes.Add(id);
            }
            if (c.boxes.Count != c.done) { c.done = c.boxes.Count; _dirty = true; }
            if (c.done >= c.count) Finish(c);
        }

        void Closing()
        {
            NoclipPassage.SeamsShut = true;                     // the seams close: whoever is in a place stays there
            LightZones.ClosingAt = Time.time;
            ClosingBell();
            int lost = 0;
            foreach (var c in D.board) if (c.state == 1) { c.state = 3; lost++; }
            CamcorderOSD.Notice(lost > 0 ? $"CLOSING TIME - {lost} JOB{(lost > 1 ? "S" : "")} LOST" : "CLOSING TIME");
            _closedAt = Time.time;
            _dirty = true;
        }

        void BlackOut()
        {
            _closedAt = -1f;
            foreach (var c in D.board) if (c.state == 1) c.state = 3;
            // Dropped where they stood: what was held stays down there.
            var grabber = streamer && streamer.player ? streamer.player.GetComponentInChildren<PropGrabber>() : null;
            if (grabber) grabber.DropAll();
            var wake = HubWake.Instance;
            if (wake) wake.Begin(Net.NetSession.Active ? Net.NetSession.LocalId : 0);
            else if (streamer) streamer.PlaceInHub(Net.NetSession.Active ? Net.NetSession.LocalId : 0);
            CamcorderOSD.Notice("YOU BLACKED OUT");
            _dirty = true;
        }

        // ---------------- The hub's board, its buttons and the pay window ----------------

        const int BW = 480, BH = 252;
        Texture2D _boardTex;
        Color32[] _px;
        readonly ElevatorButton[] _take = new ElevatorButton[3];
        ElevatorButton _payButton;
        AudioSource _beep, _bell;
        static Material s_plate, s_on, s_off;

        void BuildHub()
        {
            MakeButtonMaterials();
            // The board's face (HubBoard: 2.4 x 1.3 on the lobby's north wall, its foot 0.7 m up, facing south).
            float face = OfficeBiome.WallThickness * 0.5f;
            var boardFoot = HubPlan.World(11.0f, 14.4f - face, 0.7f);
            var rot = Quaternion.Euler(0f, 180f, 0f);
            _boardTex = Screen(transform, "ContractsBoardScreen", boardFoot, rot, BW, BH, out _px);
            // A button under each row's number, on the wall below the board.
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var at = boardFoot + new Vector3((i - 1) * 0.7f, -0.2f, 0f);       // (row 1 on the left as you face it)
                _take[i] = Button(transform, "TakeContract" + (i + 1), at, rot, () => Take(k));
            }
            // PAY: beside the pay window (HubPayWindow at x 13.2, 1.2 wide), at the ledge's height.
            _payButton = Button(transform, "CollectPay", HubPlan.World(14.05f, 14.4f - face, 1.25f), rot, Collect);
            _beep = Sound(transform, "ContractsBeep", HubPlan.World(12.0f, 14.2f, 1.2f), 8f);
            _bell = Sound(transform, "ClosingBell", Vector3.zero, 40f);
            _bell.spatialBlend = 0f;
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

        void ClosingBell()
        {
            if (!_bell) return;
            _bell.PlayOneShot(Bell(), 0.55f);
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

        static AudioClip s_bellClip;
        static AudioClip Bell()
        {
            if (s_bellClip) return s_bellClip;
            // An electric school bell: a hammer on a gong at ~25 Hz, for two seconds, ringing out.
            int sr = 44100, n = sr * 3;
            var d = new float[n];
            var rng = new System.Random(4);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float strike = t < 2f ? Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * 25f * t)), 8f) : 0f;
                float ring = Mathf.Sin(2f * Mathf.PI * 1180f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 2730f * t) * 0.3f + Mathf.Sin(2f * Mathf.PI * 4100f * t) * 0.12f;
                float env = t < 2f ? 0.6f + 0.4f * strike : Mathf.Exp(-(t - 2f) * 5f) * 0.8f;
                d[i] = ring * env * 0.3f + (float)(rng.NextDouble() - 0.5) * strike * 0.05f;
            }
            s_bellClip = AudioClip.Create("ClosingBell", n, 1, sr, false);
            s_bellClip.SetData(d, 0);
            return s_bellClip;
        }

        // ---- Painting the board ----

        internal static readonly Color32 Ground = new Color32(20, 18, 15, 255), Flap = new Color32(36, 33, 30, 255), Amber = new Color32(245, 190, 80, 255),
            Dim = new Color32(150, 118, 60, 255), Choc = new Color32(59, 42, 30, 255), Green = new Color32(140, 210, 110, 255), Red = new Color32(225, 90, 60, 255);

        static Color32 Client(int k) => (Color32)ClientColours[Mathf.Clamp(k, 0, 3)];

        void DrawBoard()
        {
            if (_px == null) return;
            for (int i = 0; i < _px.Length; i++) _px[i] = Ground;
            PixelFont.Fill(_px, BW, BH, 0, 0, BW, 34, new Color32(240, 162, 30, 255));
            PixelFont.DrawCentred(_px, BW, BH, "JOBS - DAY " + D.day, BW / 2, 6, 3, Choc);
            if (D.board.Count == 0)
                PixelFont.DrawCentred(_px, BW, BH, _posting != null ? "POSTING ..." : "NO JOBS POSTED", BW / 2, 110, 3, Amber);
            for (int i = 0; i < D.board.Count && i < 3; i++)
            {
                var c = D.board[i];
                int y = 40 + i * 58;
                PixelFont.Fill(_px, BW, BH, 6, y - 3, BW - 6, y + 54, Flap);
                // The client's tape colour down the row's edge.
                PixelFont.Fill(_px, BW, BH, 6, y - 3, 12, y + 54, Client(c.kind == 2 ? c.toKind : c.fromKind));
                bool taken = c.state != 0;
                var ink = c.state == 4 ? Dim : Amber;
                string client = Clients[Mathf.Clamp(c.kind == 2 ? c.toKind : c.fromKind, 0, 3)];
                string what = c.kind == 0 ? $"STORE {c.count} BOXES" : c.kind == 1 ? $"SHIP {c.count} BOXES" : $"RETURN {c.count} BOXES";
                PixelFont.Draw(_px, BW, BH, (i + 1).ToString(), 18, y + 4, 4, ink);
                PixelFont.Draw(_px, BW, BH, what, 52, y + 2, 2, ink);
                string pay = "$" + c.pay;
                PixelFont.Draw(_px, BW, BH, pay, BW - 14 - PixelFont.Width(pay, 2), y + 2, 2, ink);
                // The route (from whose place to where), then how it is going.
                string from = LeaseSeams.Kinds[Mathf.Clamp(c.fromKind, 0, 3)], to = LeaseSeams.Kinds[Mathf.Clamp(c.toKind, 0, 3)];
                string route = c.kind == 0 ? from + " > STORAGE" : c.kind == 1 ? from + " > " + to : "STORAGE > " + to;
                PixelFont.Draw(_px, BW, BH, route, 52, y + 20, 2, ink);
                string st = !taken ? "FOR " + client : c.state == 1 ? $"TAKEN - {c.done} OF {c.count}" : c.state == 2 ? "DONE - COLLECT PAY" : c.state == 3 ? "LOST" : "PAID";
                PixelFont.Draw(_px, BW, BH, st, 52, y + 37, 2, !taken ? Dim : c.state == 2 ? Green : c.state == 3 ? Red : ink);
            }
            for (int i = 0; i < 3; i++)
            {
                bool can = !ShiftOn && i < D.board.Count && D.board[i].state == 0;
                if (_take[i]) _take[i].SetLit(can);
                // Over each button below the board (0.7 m apart on a 2.3 m face).
                int bx = Mathf.RoundToInt((0.5f + (i - 1) * 0.7f / 2.3f) * BW);
                if (i < D.board.Count) PixelFont.DrawCentred(_px, BW, BH, (i + 1).ToString(), bx, 212, 2, can ? Amber : Dim);
            }
            bool payDue = false;
            foreach (var c in D.board) if (c.state == 2) payDue = true;
            if (_payButton) _payButton.SetLit(payDue || (ShiftOn && (Closed || Open == 0)));
            DrawBoardClock();
        }

        void DrawBoardClock()
        {
            if (_px == null) return;
            PixelFont.Fill(_px, BW, BH, 0, 227, BW, BH, Ground);
            PixelFont.Fill(_px, BW, BH, 0, 246, BW, 248, new Color32(217, 102, 31, 255));
            PixelFont.Fill(_px, BW, BH, 0, 248, BW, 250, new Color32(232, 178, 58, 255));
            PixelFont.Fill(_px, BW, BH, 0, 250, BW, 252, new Color32(124, 138, 46, 255));
            var clock = Clock;
            string left = ShiftOn && clock.HasValue ? (Closed ? "CLOSED" : "NOW " + clock.Value.ToString("h:mm") + (clock.Value.Hour >= 12 ? " PM" : " AM")) : "OFFICE HOURS 9 - " + (HourEnd - 12 + D.overtime);
            PixelFont.Draw(_px, BW, BH, left, 12, 230, 2, Amber);
            string right = "IN HAND $" + D.pay;
            PixelFont.Draw(_px, BW, BH, right, BW - 12 - PixelFont.Width(right, 2), 230, 2, Amber);
            _boardTex.SetPixels32(_px);
            _boardTex.Apply(false);
        }

        /// <summary>Redraw the board (something it shows changed elsewhere: an improvement bought).</summary>
        public void Changed() => _dirty = true;
    }
}
