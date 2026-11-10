using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Batches (2026-11-09, Docs/GameDesign.md sections 6-7; the owner's brief: goods circulate instead of piling up). Each
    /// client's seam is like a Satisfactory resource node: it never runs dry. A lot of their boxes always waits on the
    /// pallet behind it (unless the work needs equipment you do not have yet, or the client is waiting on an overdue
    /// batch). Pick the boxes up and the lot is yours: a batch.
    /// <list type="number">
    /// <item><b>Coming in:</b> carried through and shelved. No deadline.</item>
    /// <item><b>Stored:</b> once every box is safe on a lit shelf, the intake fee (a third) is paid and the countdown starts.
    ///   It runs in business time (on the floor and behind the seams, never in the hub). A box left unsafe past a grace is
    ///   scuffed: a mark on it and 5% off the completion pay.</item>
    /// <item><b>Due back:</b> the collection window opens (half the storage time, at least two minutes). Every box back
    ///   in the client's place behind their seam and the completion pay (two thirds) is paid. Past the window the pay slides
    ///   down over five minutes to half, and the client offers nothing new until it is back.</item>
    /// </list>
    /// Each payment once per batch, whatever is done with the boxes in between; a returned batch leaves the game.
    /// </summary>
    public partial class Contracts
    {
        // ---------------- The numbers (examples to tune in play: Docs/GameDesign.md section 6, decision 9) ----------------

        /// <summary>A client's lot (the node's "purity"): boxes, minutes stored, dollars a box, and what it needs.</summary>
        static readonly int[] LotBoxes = { 4, 5, 7, 3 };
        static readonly float[] LotMinutes = { 4f, 5f, 5f, 7f };
        static readonly int[] PerBox = { 10, 14, 9, 22 };
        /// <summary>The work some clients' lots need (2026-11-09, decision 8): QUILLBY's heavy paper a hand truck,
        /// BELLWEATHER's long flat-packs a platform truck.</summary>
        static readonly string[] Needs = { null, "A HAND TRUCK", null, "A PLATFORM TRUCK" };
        public const float Grace = 45f, LateSlide = 300f, LateFloor = 0.5f, ScuffCost = 0.05f, NextLotAfter = 20f;
        /// <summary>A stored box left unsafe and unattended this long (s) brings the warning (the scuff comes at
        /// <see cref="Grace"/>); anyone within <see cref="Attending"/> m of it is attending to it, and its clock waits.</summary>
        public const float WarnAfter = 10f, Attending = 8f;
        /// <summary>Client days for each second of business time (about a day a minute: three weeks in twenty minutes).</summary>
        public const float ClientDaysPerSecond = 1f / 60f;

        /// <summary>The collection window for a batch stored this long (s).</summary>
        public static float WindowFor(float storeFor) => Mathf.Max(120f, storeFor * 0.5f);

        /// <summary>What is left of the completion pay this long after it came due (1 inside the window, sliding to the
        /// floor over five minutes after it).</summary>
        public static float LateFactor(float since, float window)
        {
            if (since <= window) return 1f;
            return Mathf.Lerp(1f, LateFloor, Mathf.Clamp01((since - window) / LateSlide));
        }

        /// <summary>The completion pay with lateness and scuffs taken off (never below 30% of it).</summary>
        public static int CompletionPay(int completion, float since, float window, int scuffs)
        {
            float f = LateFactor(since, window) * (1f - ScuffCost * scuffs);
            return Mathf.RoundToInt(completion * Mathf.Max(0.3f, f));
        }

        /// <summary>A batch settled with boxes missing (2026-11-09, the review: every batch needs an ending, failure included):
        /// the completion pay for the share of its boxes that came back.</summary>
        public static int SettlePay(int completion, float since, float window, int scuffs, int back, int count)
            => count <= 0 ? 0 : Mathf.RoundToInt(CompletionPay(completion, since, window, scuffs) * Mathf.Clamp01(back / (float)count));

        /// <summary>A batch's total pay (rounded to $5) and its split: a third when stored, the rest when back.</summary>
        public static void Price(int client, int count, float minutes, float distance, int rates, out int intake, out int completion)
        {
            float total = PerBox[Mathf.Clamp(client, 0, 3)] * count * Mathf.Sqrt(Mathf.Max(1f, minutes) / 4f) * (1f + distance / 300f) * (1f + 0.15f * rates);
            int t = Mathf.Max(15, Mathf.RoundToInt(total / 5f) * 5);
            intake = Mathf.Max(5, Mathf.RoundToInt(t / 3f / 5f) * 5);
            completion = t - intake;
        }

        // ---------------- The state ----------------

        /// <summary>Business time runs: while you (in co-op, anyone) are on the floor, never in the hub or behind a seam.</summary>
        public bool Running { get; private set; }

        public string NeedOf(int client)
        {
            if (D == null) return null;
            string need = Needs[Mathf.Clamp(client, 0, 3)];
            if (need == null) return null;
            if (client == 1 && D.handTrucks.Count >= 4) return null;
            if (client == 3 && D.trucks > 0) return null;
            return need;
        }

        /// <summary>The client is waiting on an overdue batch: nothing new from them until it is back.</summary>
        public bool OnHold(int client)
        {
            if (D == null) return false;
            foreach (var b in D.batches) if (b.client == client && b.state == 3 && b.since > b.window) return true;
            return false;
        }

        /// <summary>The client's batch waiting behind their seam now, or null.</summary>
        public BatchData Waiting(int client)
        {
            if (D == null) return null;
            foreach (var b in D.batches) if (b.client == client && b.state == 0) return b;
            return null;
        }

        bool HasSeam(int client) { foreach (var s in _seams) if (s.kind == client) return true; return false; }

        bool IsFirstSeam(ulong id) { foreach (var s in _seams) if (s.id == id) return LeaseSeams.IsFirst(s, LeasePlan.Current); return false; }

        /// <summary>How many clients have a seam on your leased floor (a floor can have fewer than four).</summary>
        public int ClientsOnFloor { get { int n = 0; for (int k = 0; k < 4; k++) if (HasSeam(k)) n++; return n; } }

        /// <summary>The lift to the client's seam (m): their nearest seam, or the taped first one.</summary>
        float Distance(int client) => WorkOrders.SeamFor(client, _seams, LiftPoint, out var s) && s.kind == client ? Vector3.Distance(LiftPoint, s.position) : 120f;

        // ---------------- Every half second ----------------

        float _lastTick = -1f;

        void BatchTick()
        {
            if (D.workVersion < 2) { if (!MovablePropManager.Instance) return; Migrate(); }
            float now = Time.time;
            float dt = _lastTick < 0f ? 0f : Mathf.Min(now - _lastTick, 2f);
            _lastTick = now;
            Running = AnyoneOut();
            if (!Running) dt = 0f;
            if (_devSkip > 0f) { dt += _devSkip; _devSkip = 0f; }
            D.business += dt;
            EnsureStorage();
            if (_seams.Count == 0) return;
            NoteFound();
            for (int k = 0; k < 4; k++) EnsureLot(k);
            var mgr = MovablePropManager.Instance;
            if (!mgr) return;
            for (int i = D.batches.Count - 1; i >= 0; i--)
            {
                var b = D.batches[i];
                switch (b.state)
                {
                    case 0: Waiting(b, mgr); break;
                    case 1: ComingIn(b, mgr); break;
                    case 2: Stored(b, mgr, dt); break;
                    case 3: DueBack(b, mgr, dt); break;
                }
            }
            Power(dt);
            CollectUnseen(mgr);
            KeepStoredList();
        }

        /// <summary>Is anyone (you or, in co-op, another player) in this client's room behind their seam?</summary>
        bool AnyoneIn(int client)
        {
            var player = streamer ? streamer.player : null;
            if (player && InRoom(player.position, client)) return true;
            foreach (var r in _remotes) if (r && InRoom(r.transform.position, client)) return true;
            return false;
        }

        /// <summary>A box signed for (returned, or a lot withdrawn): it goes once nobody can see it go.</summary>
        void Collect(ulong id) { if (!D.collect.Contains(id)) D.collect.Add(id); }

        /// <summary>Signed-for boxes (2026-11-09): can't be taken again (PropGrabber).</summary>
        public static bool Collecting(ulong id) { var d = GameSession.Data; return d != null && d.collect.Contains(id); }

        /// <summary>Signed-for boxes leave unseen: in a client's room, once nobody is in it; anywhere else, once nobody is
        /// within 12 m.</summary>
        void CollectUnseen(MovablePropManager mgr)
        {
            for (int i = D.collect.Count - 1; i >= 0; i--)
            {
                var id = D.collect[i];
                if (!mgr.Where(id, out var p, out bool held)) { D.collect.RemoveAt(i); continue; }
                if (held) continue;
                int room = SeamPlan.InZone(p) ? SeamPlan.KindAt(p) : -1;
                bool seen = room >= 0 ? AnyoneIn(room) : AnyoneNear(p, 12f);
                if (seen) continue;
                mgr.Forget(id);
                D.collect.RemoveAt(i);
            }
        }

        bool AnyoneNear(Vector3 p, float r)
        {
            var player = streamer ? streamer.player : null;
            if (player && (player.position - p).sqrMagnitude < r * r) return true;
            foreach (var o in _remotes) if (o && (o.transform.position - p).sqrMagnitude < r * r) return true;
            return false;
        }

        float _devSkip;

        /// <summary>The developer menu's fast forward (L, PROGRESSION): this much business time passes at the next tick, as if
        /// you had been out on the floor (stored batches come due, late ones slide).</summary>
        public void DevSkip(float seconds) => _devSkip += Mathf.Max(0f, seconds);

        /// <summary>Developer: money in hand.</summary>
        public void DevPay(int amount) { if (D == null) return; D.pay += amount; _dirty = true; Supplies.Instance?.Changed(); Improvements.Instance?.Changed(); }

        /// <summary>Is anyone out of the hub (on the floor, behind a seam, in the lift on the way)?</summary>
        bool AnyoneOut()
        {
            _remotes = Net.NetSession.Active ? FindObjectsByType<Net.RemotePlayer>(FindObjectsSortMode.None) : System.Array.Empty<Net.RemotePlayer>();
            var player = streamer ? streamer.player : null;
            if (player && OnTheFloor(player.position)) return true;
            foreach (var r in _remotes) if (r && OnTheFloor(r.transform.position)) return true;
            return false;
        }

        /// <summary>In the backrooms, where business time runs: not the hub, and not a client's real-world room behind a
        /// seam (2026-11-09, the owner: "the time should pause on storing things when you aren't in the backrooms, so that
        /// includes seams since that's real world places").</summary>
        public static bool OnTheFloor(Vector3 p) => !HubPlan.InZone(p) && !SeamPlan.InZone(p);
        Net.RemotePlayer[] _remotes = System.Array.Empty<Net.RemotePlayer>();

        /// <summary>A client's seam found: you came within a few metres of it, or went through.</summary>
        void NoteFound()
        {
            if (!D.seamsFound.Contains(LeaseSeams.FirstKind)) D.seamsFound.Add(LeaseSeams.FirstKind);   // (taped from the lift)
            var player = streamer ? streamer.player : null;
            if (!player) return;
            var p = player.position;
            int kind = -1;
            if (SeamPlan.InZone(p)) kind = SeamPlan.KindAt(p);
            else foreach (var s in Seam.All)
                if (s && s.kind <= 3 && (s.transform.position - p).sqrMagnitude < 9f)
                {
                    kind = s.kind;
                    // (Another seam, even one more of TALLMORE's: on a floor with few kinds this is what finding one means.)
                    if (!D.otherSeamFound && !IsFirstSeam(s.id)) { D.otherSeamFound = true; _dirty = true; if (D.seamsFound.Contains(kind)) CamcorderOSD.Notice($"ANOTHER {LeaseSeams.Kinds[kind]} SEAM - MORE OF {Clients[kind]}'S WORK"); }
                    break;
                }
            if (kind < 0 || kind > 3 || D.seamsFound.Contains(kind)) return;
            D.seamsFound.Add(kind);
            if (kind != LeaseSeams.FirstKind) CamcorderOSD.Notice($"NEW CLIENT FOUND - {Clients[kind]} ({LeaseSeams.Kinds[kind]}) - THEIR CARD IS BY THE SEAM");
            _dirty = true;
        }

        /// <summary>A lot waiting behind a client's seam, unless they are on hold, the work needs what you do not have, or
        /// their last lot only just went.</summary>
        void EnsureLot(int client)
        {
            while (D.lotAt.Count < 4) D.lotAt.Add(0f);
            if (!HasSeam(client) || Waiting(client) != null || NeedOf(client) != null) return;
            if (D.business < D.lotAt[client]) return;
            // (Not while their last lot is still being cleared from the pallet: its boxes are in the room.)
            var mgr = MovablePropManager.Instance;
            if (!mgr) return;
            foreach (var o in D.batches)
                if (o.client == client && o.state == 1)
                    foreach (var id in o.boxes)
                        if (mgr.Where(id, out var p, out bool held) && !held && InRoom(p, client)) return;
            if (OnHold(client)) return;
            // (2026-11-09, the owner: the next lot appeared "right in front of you" as the last box of the one before came
            // off the pallet. The client's people bring it through their staff door while nobody is in the room.)
            if (AnyoneIn(client)) return;
            var b = new BatchData { id = ++D.nextBatch, client = client };
            bool first = D.batchesIn == 0 && client == LeaseSeams.FirstKind;
            int stage = Mathf.Clamp(D.woStage, 0, 4);
            b.count = first ? 4 : LotBoxes[client] + Mathf.Min(stage, 3);
            if (!first && D.chutes.Exists(id => _seams.Exists(s => s.id == id && s.kind == client))) b.count = Mathf.RoundToInt(b.count * 1.5f);   // a chute: big lots
            float minutes = first ? 3f : LotMinutes[client] + 0.5f * stage;
            b.storeFor = minutes * 60f;
            b.window = WindowFor(b.storeFor);
            b.fragile = client == 1 && WorkOrders.WantsFragile;
            Price(client, b.count, minutes, Distance(client), D.rates, out b.intake, out b.completion);
            StackLot(b);
            if (b.boxes.Count == 0) return;
            D.batches.Add(b);
            _dirty = true;
        }

        /// <summary>The lot's boxes on the pickup pallet behind the client's seam (SeamPlan), in piles of their shape.</summary>
        void StackLot(BatchData b)
        {
            var mgr = MovablePropManager.Instance;
            if (!mgr) return;
            int shape = ShapeFor(b.client, b.id);
            var size = BoxShapes[shape].size;
            for (int i = 0; i < b.count; i++)
            {
                ulong id = BoxId(b.client, D.nextBox++, shape);
                if (b.fragile) D.fragile.Add(id);
                var at = SeamPlan.Stack(b.client, i, size);
                var spec = BoxSpec(id, at, 180f);
                if (spec == null) return;
                mgr.NetPlace(spec, at, Quaternion.Euler(0f, 180f, 0f));
                b.boxes.Add(id);
            }
        }

        /// <summary>In the client's place behind their seam.</summary>
        static bool InRoom(Vector3 p, int client) => SeamPlan.InZone(p) && SeamPlan.KindAt(p) == client;

        void Waiting(BatchData b, MovablePropManager mgr)
        {
            // An overdue batch puts the client on hold: a lot nobody has touched goes back.
            if (OnHold(b.client)) { Withdraw(b, mgr); return; }
            foreach (var id in b.boxes)
                if (mgr.Where(id, out var p, out bool held) && (held || !InRoom(p, b.client)))
                {
                    b.state = 1;
                    D.lotAt[b.client] = D.business + NextLotAfter;
                    if (!D.seamsFound.Contains(b.client)) D.seamsFound.Add(b.client);
                    CamcorderOSD.Notice($"{Clients[b.client]} BATCH TAKEN - {b.count} BOXES - SHELVE THEM ALL ON LIT SHELVES");
                    _dirty = true;
                    return;
                }
        }

        void Withdraw(BatchData b, MovablePropManager mgr)
        {
            foreach (var id in b.boxes) { Collect(id); D.fragile.Remove(id); }
            D.batches.Remove(b);
            _dirty = true;
        }

        /// <summary>How many of its boxes are safe on a lit shelf now (not in anyone's hands).</summary>
        static int SafeCount(BatchData b, MovablePropManager mgr)
        {
            int n = 0;
            foreach (var id in b.boxes) if (mgr.Where(id, out var p, out bool held) && !held && Shelves.OnSafe(p)) n++;
            return n;
        }

        void ComingIn(BatchData b, MovablePropManager mgr)
        {
            if (SafeCount(b, mgr) < b.boxes.Count || b.boxes.Count == 0) return;
            // Every box safe: the intake fee, and the countdown starts.
            b.state = 2;
            b.left = b.storeFor;
            b.unsafeFor.Clear();
            for (int i = 0; i < b.boxes.Count; i++) b.unsafeFor.Add(0f);
            D.pay += b.intake; D.earned += b.intake;
            D.batchesIn++;
            Trip($"{Clients[b.client]} STORED ({b.boxes.Count})", "$" + b.intake);
            CamcorderOSD.Notice($"{Clients[b.client]} STORED - +${b.intake} - DUE BACK IN {MinSec(b.left)}");
            Beep(true);
            _dirty = true;
        }

        void Stored(BatchData b, MovablePropManager mgr, float dt)
        {
            b.left -= dt;
            while (b.unsafeFor.Count < b.boxes.Count) b.unsafeFor.Add(0f);
            int risk = 0;
            for (int i = 0; i < b.boxes.Count; i++)
            {
                var id = b.boxes[i];
                bool known = mgr.Where(id, out var p, out bool held);
                if (known && !held && Shelves.OnSafe(p)) { b.unsafeFor[i] = 0f; continue; }
                // (2026-11-09, the review: goods being handled are ordinary work. In someone's arms, on the hand truck, on a
                // cart or truck, or with someone nearby: no clock. Only a box set down somewhere unsafe and left.)
                if (!known || held || OnCart(id) || Attended(p)) continue;
                b.unsafeFor[i] += dt;
                if (b.unsafeFor[i] < WarnAfter) continue;
                risk++;
                if (b.unsafeFor[i] >= Grace && !D.scuffed.Contains(id))
                {
                    D.scuffed.Add(id);
                    var mp = MovableProp.Find(id);
                    if (mp) ScuffMarks.Attach(mp);
                    CamcorderOSD.Notice($"A {Clients[b.client]} BOX GOT SCUFFED - {Mathf.RoundToInt(ScuffCost * 100f)}% OFF ITS PAY");
                    Beep(false);
                    _dirty = true;
                }
            }
            if (risk > 0 && !b.riskSaid && dt > 0f)
            {
                b.riskSaid = true;
                CamcorderOSD.Notice($"{risk} {Clients[b.client]} BOX{(risk > 1 ? "ES" : "")} LEFT OFF A LIT SHELF - THEY'LL GET SCUFFED");
            }
            else if (risk == 0) b.riskSaid = false;
            if (b.left > 0f) return;
            b.state = 3;
            b.since = 0f;
            CamcorderOSD.Notice($"{Clients[b.client]}'S BOXES ARE DUE BACK - TAKE THEM THROUGH THEIR SEAM ({MinSec(b.window)})");
            Beep(true);
            _dirty = true;
        }

        void DueBack(BatchData b, MovablePropManager mgr, float dt)
        {
            b.since += dt;
            if (b.since > b.window && !b.lateSaid)
            {
                b.lateSaid = true;
                CamcorderOSD.Notice($"{Clients[b.client]}'S BOXES ARE LATE - THE PAY IS DROPPING, AND THEY'RE ON HOLD");
                Beep(false);
                _dirty = true;
            }
            int back = 0;
            foreach (var id in b.boxes) if (mgr.Where(id, out var p, out bool held) && !held && InRoom(p, b.client)) back++;
            // Every batch ends (2026-11-09, the review): all back, or, once the late pay has slid to its floor, settled with
            // whatever is back; the boxes still out are charged as lost, and the client takes work again.
            bool settle = b.since >= b.window + LateSlide && back < b.boxes.Count;
            if ((back < b.boxes.Count && !settle) || b.boxes.Count == 0) return;
            int scuffs = 0;
            foreach (var id in b.boxes) if (D.scuffed.Contains(id) && mgr.Where(id, out var sp, out bool sh) && !sh && InRoom(sp, b.client)) scuffs++;
            int pay = SettlePay(b.completion, b.since, b.window, scuffs, back, b.boxes.Count);
            bool onTime = b.since <= b.window && !settle;
            int lost = b.boxes.Count - back;
            D.pay += pay; D.earned += pay;
            D.batchesDone++;
            if (onTime) D.batchesOnTime++;
            if (!D.clientsServed.Contains(b.client)) D.clientsServed.Add(b.client);
            string note = settle ? $" {back} OF {b.boxes.Count}" : !onTime ? " LATE" : "";
            if (scuffs > 0) note += $" {scuffs} SCUFFED";
            Trip($"{Clients[b.client]} {(settle ? "SETTLED" : "RETURNED")}{note}", "$" + pay);
            // (Paid now; the boxes stay where you left them until you have gone, then the client's people take them in: the
            // owner, 2026-11-09: they "immediately disappear in front of you".)
            foreach (var id in b.boxes) { Collect(id); D.scuffed.Remove(id); D.fragile.Remove(id); D.stored.Remove(id); }
            D.batches.Remove(b);
            CamcorderOSD.Notice(settle ? $"{Clients[b.client]} SETTLED - {back} OF {b.boxes.Count} BACK, {lost} CHARGED AS LOST - +${pay} - THEY'LL TAKE WORK AGAIN"
                                       : $"{Clients[b.client]} RETURNED - +${pay}" + (onTime ? "" : " (LATE)"));
            Beep(true);
            WorkOrders.BatchReturned(b.client, onTime, b.fragile);
            _dirty = true;
        }

        /// <summary>The power bill (decision 11, revised after the review): the generators' charge by the minute while business
        /// time runs, taken from what you have as it comes. With nothing in hand it is owed, and taken from the next payment;
        /// the machines never stop for it (an overload is the only thing that stops a generator).</summary>
        void Power(float dt)
        {
            D.powerOwed += Machines.Bill() / DaySeconds * dt;
            int take = Mathf.Min(Mathf.FloorToInt(D.powerOwed), D.pay);
            if (take <= 0) return;
            D.powerOwed -= take;
            D.pay -= take;
            D.tripPower += take;
            _dirty = true;
        }

        /// <summary>The box rests on a cart or truck (or on boxes on one): goods on wheels are being moved, not left.</summary>
        static bool OnCart(ulong id)
        {
            var mp = MovableProp.Find(id);
            if (!mp) return false;
            var from = mp.transform.position + Vector3.up * 0.05f;
            for (int level = 0; level < 4; level++)
            {
                int n = Physics.RaycastNonAlloc(from, Vector3.down, s_down, 0.35f, ~0, QueryTriggerInteraction.Ignore);
                MovableProp under = null; float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    var o = s_down[i].collider.GetComponentInParent<MovableProp>();
                    if (!o || o == mp || s_down[i].distance >= best) continue;
                    best = s_down[i].distance; under = o;
                }
                if (!under) return false;
                var k = under.Spec.kind;
                if (k == MovableKind.PlatformTruck || k == MovableKind.UtilityCart || k == MovableKind.JanitorCart) return true;
                if (k != MovableKind.Box) return false;
                mp = under; from = under.transform.position + Vector3.up * 0.05f;
            }
            return false;
        }
        static readonly RaycastHit[] s_down = new RaycastHit[8];

        /// <summary>Someone is near it (within <see cref="Attending"/> m): it is being seen to.</summary>
        bool Attended(Vector3 p)
        {
            var player = streamer ? streamer.player : null;
            if (player && (player.position - p).sqrMagnitude < Attending * Attending) return true;
            if (Net.NetSession.Active)
                foreach (var r in _remotes) if (r && (r.transform.position - p).sqrMagnitude < Attending * Attending) return true;
            return false;
        }

        /// <summary>D.stored: the boxes of batches stored or due back (what the rest of the game asks about).</summary>
        void KeepStoredList()
        {
            D.stored.Clear();
            foreach (var b in D.batches) if (b.state >= 2) D.stored.AddRange(b.boxes);
        }

        /// <summary>Boxes of stored batches safe on lit shelves now (the board's SAFE, the "stored at once" orders).</summary>
        public int SafeNow
        {
            get
            {
                var mgr = MovablePropManager.Instance;
                if (D == null || !mgr) return 0;
                int n = 0;
                foreach (var b in D.batches) if (b.state == 2 || b.state == 3) n += SafeCount(b, mgr);
                return n;
            }
        }

        /// <summary>Boxes of stored batches not safe now (off a shelf, or on a dim one): the board's AT RISK.</summary>
        public int AtRisk
        {
            get
            {
                var mgr = MovablePropManager.Instance;
                if (D == null || !mgr) return 0;
                int n = 0;
                foreach (var b in D.batches) if (b.state == 2) n += b.boxes.Count - SafeCount(b, mgr);
                return n;
            }
        }

        /// <summary>m:ss.</summary>
        public static string MinSec(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        // ---------------- The seam's card (SeamCard) ----------------

        static readonly Color32 CardInk = new Color32(52, 38, 28, 255), CardRed = new Color32(176, 52, 32, 255), CardGreen = new Color32(42, 112, 44, 255);

        /// <summary>What a client's seam card says: the place, the lot waiting (or why not), and your batches of theirs.</summary>
        public void Card(int client, List<(string text, Color32 ink)> lines)
        {
            if (D == null) return;
            lines.Add((LeaseSeams.Kinds[Mathf.Clamp(client, 0, 3)], CardInk));
            string need = NeedOf(client);
            bool hold = OnHold(client);
            if (need != null) { lines.Add(("NEEDS:", CardRed)); lines.Add((need, CardRed)); lines.Add(("(SUPPLY COUNTER)", CardInk)); }
            else if (hold) { lines.Add(("ON HOLD:", CardRed)); lines.Add(("YOUR LATE BOXES 1ST", CardRed)); }
            var w = Waiting(client);
            if (w != null)
            {
                int days = Mathf.Max(1, Mathf.RoundToInt(w.storeFor * ClientDaysPerSecond));
                lines.Add(($"LOT: {w.count} BOXES" + (w.fragile ? " FRAGILE" : ""), w.fragile ? CardRed : CardInk));
                lines.Add(($"STORE {MinSec(w.storeFor)} ({days} DAYS)", CardInk));
                lines.Add(($"${w.intake} IN + ${w.completion} BACK", CardInk));
                lines.Add(("TAKE THE BOXES", CardGreen));
            }
            else if (need == null && !hold) lines.Add(("NEXT LOT SOON", CardInk));
            foreach (var b in D.batches)
            {
                if (b.client != client || b.state == 0) continue;
                string st = b.state == 1 ? "COMING IN" : b.state == 2 ? "STORED " + MinSec(b.left) : b.since > b.window ? "LATE!" : "DUE " + MinSec(b.window - b.since);
                lines.Add(($"YOURS: {st}", b.state == 3 ? CardRed : CardInk));
            }
        }

        // ---------------- Old saves (before 2026-11-09) ----------------

        /// <summary>
        /// Moves a save from the working day over to batches, once: boxes kept in storage become stored batches of their
        /// clients (intake already counted: they paid rent), the day's open jobs' boxes still on the pallets go back, and
        /// the work orders start their stage again on the new list (stages already done stay done).
        /// </summary>
        void Migrate()
        {
            if (D.workVersion >= 2) return;
            D.workVersion = 2;
            var mgr = MovablePropManager.Instance;
            foreach (var c in D.board)
                if (c.state == 1 && c.kind != 2 && mgr)
                    foreach (var id in c.boxes) if (!D.stored.Contains(id)) mgr.Forget(id);
            D.board.Clear();
            D.shift = -1f;
            var byClient = new List<ulong>[4];
            for (int k = 0; k < 4; k++) byClient[k] = new List<ulong>();
            foreach (var id in D.stored) byClient[BoxClient(id)].Add(id);
            for (int k = 0; k < 4; k++)
                for (int i = 0; i < byClient[k].Count; i += 8)
                {
                    var b = new BatchData { id = ++D.nextBatch, client = k, state = 2, intake = 0 };
                    for (int j = i; j < Mathf.Min(i + 8, byClient[k].Count); j++) { b.boxes.Add(byClient[k][j]); b.unsafeFor.Add(0f); }
                    b.count = b.boxes.Count;
                    float minutes = LotMinutes[k];
                    b.storeFor = b.left = minutes * 60f;
                    b.window = WindowFor(b.storeFor);
                    Price(k, b.count, minutes, 120f, 0, out _, out b.completion);
                    D.batches.Add(b);
                }
            WorkOrders.Migrate();
            Debug.Log($"[Contracts] Moved this save over to batches: {D.batches.Count} stored batches from {D.stored.Count} kept boxes.");
        }
    }
}
