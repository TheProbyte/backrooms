using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Improvements (2026-11-07, the owner: "each day you can improve one of your outputs ... like satisfactory";
    /// "improvements will be payed and you don't HAVE to have one each night"): the hub's second board, on the lobby's
    /// south wall, lists what the day's pay can buy for the floor. NEXT steps through the list, ORDER buys the one picked.
    /// As many as you can pay for (2026-11-09: no shift, no one-a-night limit).
    /// <list type="bullet">
    /// <item>INTAKE CHUTE KIT: a crate waits by the lift on the floor; set it down in front of a seam and the chute is fitted
    ///   (Seam.Fit): from then on it pulls that place's boxes through by itself.</item>
    /// <item>PLATFORM TRUCK: a trolley by the lift; boxes ride on it (CartWheels).</item>
    /// <item>BETTER RATES: new batches pay 15% more (overtime and backup lights went with the working day, 2026-11-09).</item>
    /// <item>RENT REVIEW: a dollar more a stored box a day.</item>
    /// <item>LEASE A WING (north, east, south, west): its hoarding comes down (LeaseGate), its seams and storage rooms are
    ///   the board's from the next day (LeasePlan).</item>
    /// <item>POOLROOMS ACCESS: a noclip door past the first wing leased, into the Poolrooms.</item>
    /// </list>
    /// Co-op: the board's buttons are shared presses, so every game buys the same thing; kits and trucks have fixed ids.
    /// </summary>
    public class Improvements : MonoBehaviour
    {
        public static Improvements Instance { get; private set; }
        public WorldStreamer streamer;

        WorldSaveData D => GameSession.Data;

        enum Kind { ChuteKit, Truck, Overtime, BackupLights, RentReview, Wing, Poolrooms }

        struct Item { public Kind kind; public int wing; public string name, note, unlock; public int cost; public bool locked; }

        readonly List<Item> _items = new List<Item>();
        int _pick;
        float _nextCheck;
        bool _dirty = true;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (D == null) { enabled = false; return; }
            BuildBoard();
            // (Kits and trucks bought on earlier nights are moved props: they are kept where they were left.)
        }

        public void Changed() => _dirty = true;

        // ---------------- The list ----------------

        void List()
        {
            _items.Clear();
            var locked = new List<Item>();
            void Add(Kind k, string name, string note, int cost, string unlock, int wing = -1)
            {
                var it = new Item { kind = k, name = name, note = note, cost = cost, wing = wing, unlock = unlock, locked = !Unlocks.Has(unlock) };
                (it.locked ? locked : _items).Add(it);
            }
            // (2026-11-08: kits and trucks are sold at the supply counter now; what is here lasts for good, each level
            // unlocked by finishing work orders: Interaction.WorkOrders. 2026-11-09, the owner: "how to get improvements?
            // unlocked by work orders isn't clear": all of it is listed, what is locked greyed with the orders that open it.)
            if (D.rates < 2) Add(Kind.RentReview, "BETTER RATES", "NEW BATCHES PAY 15% MORE", 250 + 150 * D.rates, "rates" + (D.rates + 1));
            int owned = LeasePlan.WingCount;
            for (int w = 0; w < 4; w++)
                if ((D.wings & (1 << w)) == 0) Add(Kind.Wing, $"LEASE {LeasePlan.WingNames[w]} WING", "MORE SEAMS AND FLOOR", 400 + 200 * owned, "wings", w);
            if (!D.poolrooms) Add(Kind.Poolrooms, "POOLROOMS ACCESS", owned > 0 ? "A DOOR PAST YOUR FIRST WING" : "NEEDS A WING FIRST", 900, "poolrooms");
            _items.AddRange(locked);
            _pick = Mathf.Clamp(_pick, 0, Mathf.Max(0, _items.Count - 1));
        }

        // ---------------- The buttons ----------------

        void Next()
        {
            if (D == null) return;
            List();
            if (_items.Count == 0) return;
            _pick = (_pick + 1) % _items.Count;
            Contracts.Instance?.Beep(true);
            _dirty = true;
        }

        /// <summary>The host: buys the named improvement for a joined player. True if it went through.</summary>
        public bool OrderByName(string name)
        {
            if (D == null) return false;
            List();
            int i = _items.FindIndex(x => x.name == name);
            if (i < 0 || _items[i].locked || D.pay < _items[i].cost) return false;
            int was = _pick, pay = D.pay;
            _pick = i;
            Order();
            _pick = Mathf.Clamp(was, 0, Mathf.Max(0, _items.Count - 1));
            return D.pay < pay;
        }

        void Order()
        {
            if (D == null) return;
            List();
            if (_items.Count == 0) return;
            var it = _items[_pick];
            var cs = Contracts.Instance;
            if (it.locked) { CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.locked_item", it.name, WorkOrders.FinishText(it.unlock))); cs?.Beep(false); return; }
            string no = it.kind == Kind.Poolrooms && LeasePlan.WingCount == 0 ? "LEASE A WING FIRST"
                      : D.pay < it.cost ? $"NOT ENOUGH - ${D.pay} IN HAND" : null;
            if (no != null) { CamcorderOSD.Notice(no); cs?.Beep(false); return; }
            // Co-op: the crew's one wallet is the host's; a joined game asks it to buy.
            if (Net.WorkNet.Client) { Net.WorkNet.Ask("improve " + it.name); cs?.Beep(true); return; }
            Net.WorkNet.Changed();
            D.pay -= it.cost;
            switch (it.kind)
            {
                case Kind.ChuteKit: PlaceKit(D.chuteKits++, true); break;
                case Kind.Truck: PlaceTruck(D.trucks++, true); break;
                case Kind.RentReview: D.rates++; break;
                case Kind.Wing:
                    D.wings |= 1 << it.wing;
                    LeasePlan.Wings = D.wings;
                    LeaseGate.Refresh();
                    // The Poolrooms door moves with the first wing leased: its district is planned again.
                    if (D.poolrooms) RemakePoolDoor();
                    break;
                case Kind.Poolrooms:
                    D.poolrooms = true;
                    LeasePlan.Poolrooms = true;
                    RemakePoolDoor();
                    break;
            }
            CamcorderOSD.Notice(Backrooms.UI.Words.Get(it.kind == Kind.ChuteKit || it.kind == Kind.Truck ? "line.ordered_lift" : "line.ordered", it.name));
            cs?.Beep(true);
            cs?.Changed();
            _dirty = true;
        }

        void RemakePoolDoor()
        {
            if (!streamer) return;
            for (int w = 0; w < 4; w++)
            {
                // (Every place it could be: the first leased wing's changes as wings are leased.)
                var o = LeasePlan.Centre(LeasePlan.Current) - new Vector2Int(LeasePlan.Block / 2, LeasePlan.Block / 2);
                var at = o + (w == 0 ? new Vector2Int(3, 7) : w == 1 ? new Vector2Int(7, 4) : w == 2 ? new Vector2Int(4, 0) : new Vector2Int(0, 3));
                streamer.ForgetDistrict(at);
            }
        }

        // ---------------- Kits and trucks ----------------

        public static ulong KitId(int n) => 0xC4E7000000000000ul | (ulong)(uint)n;
        public static ulong TruckId(int n) => 0xC4E8000000000000ul | (ulong)(uint)n;
        public static bool IsKit(ulong id) => (id >> 48) == 0xC4E7ul;
        public static bool HasChute(ulong seam) { var d = GameSession.Data; return d != null && d.chutes.Contains(seam); }

        /// <summary>The supply counter's intake chute kit, waiting at a spot by the lift.</summary>
        public ulong SendChuteKit(Vector3 at, Quaternion rot)
        {
            var mgr = MovablePropManager.Instance;
            if (!mgr || D == null) return 0;
            ulong id = KitId(D.chuteKits++);
            var spec = Contracts.BoxSpec(id, at, rot.eulerAngles.y);
            if (spec == null) return 0;
            mgr.Place(spec, at, rot);
            return id;
        }

        /// <summary>The supply counter's platform truck, by the lift.</summary>
        public void SendTruck() { if (D != null) PlaceTruck(D.trucks++, true); }

        /// <summary>Beside the lift on the floor: in front of its gate, off to one side.</summary>
        internal static Vector3 ByTheLift(float across, float outward, out Quaternion rot)
        {
            var cab = HubPlan.WorldCab;
            rot = cab.rotation;
            return cab.position + cab.rotation * new Vector3(across, 0f, outward);
        }

        void PlaceKit(int n, bool fresh)
        {
            var mgr = MovablePropManager.Instance;
            if (!mgr) return;
            ulong id = KitId(n);
            var at = ByTheLift(1.6f + 0.8f * (n % 3), 2.2f + 0.6f * (n / 3 % 2), out var rot);
            var spec = Contracts.BoxSpec(id, at, rot.eulerAngles.y);
            if (spec != null) mgr.Place(spec, at, rot);
        }

        void PlaceTruck(int n, bool fresh)
        {
            var mgr = MovablePropManager.Instance;
            var a = streamer ? streamer.assets : null;
            if (!mgr || !a) return;
            ulong id = TruckId(n);
            if (!fresh && mgr.Where(id, out _, out _)) return;
            var at = ByTheLift(-1.8f - 1.2f * n, 2.4f, out var rot);
            rot *= Quaternion.Euler(0f, 90f, 0f);
            var spec = new MovableSpec { id = id, kind = MovableKind.PlatformTruck, renderMask = uint.MaxValue, proxyMaterial = a.baseboard, position = at, yaw = rot.eulerAngles.y };
            mgr.Place(spec, at, rot);
        }

        void Update()
        {
            if (D == null) return;
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.4f;
            // A kit set down in front of a seam (on the floor's side, within a metre and a half): the chute is fitted.
            var mgr = MovablePropManager.Instance;
            if (mgr)
                for (int n = 0; n < D.chuteKits; n++)
                {
                    ulong id = KitId(n);
                    if (!mgr.Where(id, out var p, out bool held) || held) continue;
                    foreach (var s in Seam.All)
                    {
                        if (!s || s.fitted || s.kind > 3) continue;
                        var l = s.transform.InverseTransformPoint(p);
                        if (Mathf.Abs(l.x) > 1.3f || l.z < 0.05f || l.z > 1.8f || Mathf.Abs(l.y) > 1.2f) continue;
                        s.Fit();
                        if (!D.chutes.Contains(s.id)) D.chutes.Add(s.id);
                        mgr.Forget(id);
                        var live = MovableProp.Find(id);
                        if (live) Destroy(live.gameObject);
                        CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.chute_fitted", LeaseSeams.Kinds[s.kind]));
                        break;
                    }
                }
            if (_dirty) { _dirty = false; Draw(); }
        }

        // ---------------- The board ----------------

        const int BW = 480, BH = 252;
        Texture2D _tex;
        Color32[] _px;
        ElevatorButton _next, _order;

        void BuildBoard()
        {
            // HubBoard on the lobby's south wall, east of the way to the lift, facing north (OverflowRooms).
            float face = OfficeBiome.WallThickness * 0.5f;
            var foot = HubPlan.World(17.3f, 7.2f + face, 0.7f);
            var rot = Quaternion.identity;
            _tex = Contracts.Screen(transform, "ImprovementsBoardScreen", foot, rot, BW, BH, out _px);
            // (Facing north, the board's left as you look at it is to the east, +x.)
            _next = Contracts.Button(transform, "ImprovementsNext", foot + new Vector3(0.35f, -0.2f, 0f), rot, Next);
            _order = Contracts.Button(transform, "ImprovementsOrder", foot + new Vector3(-0.35f, -0.2f, 0f), rot, Order);
            _next.SetLit(true);
        }

        void Draw()
        {
            if (_px == null) return;
            List();
            var G = Contracts.Ground; var A = Contracts.Amber; var Dm = Contracts.Dim;
            for (int i = 0; i < _px.Length; i++) _px[i] = G;
            PixelFont.Fill(_px, BW, BH, 0, 0, BW, 34, new Color32(124, 138, 46, 255));
            PixelFont.DrawCentred(_px, BW, BH, "IMPROVEMENTS", BW / 2, 6, 3, Contracts.Choc);
            bool bought = false;
            int top = 40;
            // (2026-11-10, U14/U15: six rows, so the picked one's note fits on two lines at 2 dots; at 1 dot it could not be
            // read from 3 m.)
            int rows = Mathf.Min(_items.Count, 6);
            int first = Mathf.Clamp(_pick - rows + 1, 0, Mathf.Max(0, _items.Count - rows));
            for (int r = 0; r < rows; r++)
            {
                int i = first + r;
                var it = _items[i];
                int y = top + r * 21;
                bool sel = i == _pick;
                bool afford = D.pay >= it.cost && !bought;
                if (sel) PixelFont.Fill(_px, BW, BH, 6, y - 2, BW - 6, y + 18, Contracts.Flap);
                var ink = it.locked ? (sel ? Contracts.LockedSel : Contracts.Locked) : sel ? A : afford ? new Color32(205, 160, 72, 255) : Dm;
                string cost = it.locked ? WorkOrders.LockedText(it.unlock) : "$" + it.cost;
                int costW = PixelFont.Width(cost, 2);
                PixelFont.Draw(_px, BW, BH, Generation.PixelScreen.Fit((sel ? "> " : "  ") + it.name, 2, BW - 34 - costW), 10, y + 1, 2, ink);
                PixelFont.Draw(_px, BW, BH, cost, BW - 12 - costW, y + 1, 2, ink);
            }
            // The picked one's note, and the buttons' labels.
            if (_items.Count > 0)
            {
                var pk = _items[_pick];
                var (n1, n2) = Generation.PixelScreen.TwoLines(pk.locked ? WorkOrders.FinishText(pk.unlock) + " (J)" : pk.note, 2, BW - 20);
                PixelFont.DrawCentred(_px, BW, BH, n1, BW / 2, 168, 2, pk.locked ? Contracts.Red : Dm);
                PixelFont.DrawCentred(_px, BW, BH, n2, BW / 2, 185, 2, pk.locked ? Contracts.Red : Dm);
            }
            else PixelFont.DrawCentred(_px, BW, BH, "NOTHING YET", BW / 2, 100, 3, Dm);
            int bx(float dx) => Mathf.RoundToInt((0.5f - dx / 2.3f) * BW);
            PixelFont.DrawCentred(_px, BW, BH, "NEXT", bx(0.35f), 212, 2, A);
            PixelFont.DrawCentred(_px, BW, BH, "ORDER", bx(-0.35f), 212, 2, bought ? Dm : A);
            PixelFont.Fill(_px, BW, BH, 0, 246, BW, 248, new Color32(217, 102, 31, 255));
            PixelFont.Fill(_px, BW, BH, 0, 248, BW, 250, new Color32(232, 178, 58, 255));
            PixelFont.Fill(_px, BW, BH, 0, 250, BW, 252, new Color32(124, 138, 46, 255));
            string left = "LASTS FOR GOOD";
            PixelFont.Draw(_px, BW, BH, left, 12, 230, 2, A);
            string right = "IN HAND $" + D.pay;
            PixelFont.Draw(_px, BW, BH, right, BW - 12 - PixelFont.Width(right, 2), 230, 2, A);
            if (_order) _order.SetLit(!bought && _items.Count > 0 && !_items[_pick].locked && D.pay >= _items[_pick].cost);
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }
    }
}
