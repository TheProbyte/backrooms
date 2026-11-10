using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Kit crates (2026-11-08): what the supply counter sends down that you set up yourself. A kit is a cardboard crate with
    /// Overflow's amber tape (a moved Box prop) whose id says what is in it. The intake chute kit is kind 0 (Improvements,
    /// set down at a seam); the others are put up where you want them with the see-through outline (KitPlacer).
    /// </summary>
    public static class Kits
    {
        public enum Kind { Chute = 0, Shelf = 1, TallShelf = 2, Rack = 3, Generator = 4, Conveyor = 5, Floodlight = 6 }

        public static ulong Id(Kind k, int n) => 0xC4E7000000000000ul | ((ulong)(int)k << 32) | (uint)n;
        public static Kind KindOf(ulong id) => (Kind)(int)((id >> 32) & 0xFF);

        /// <summary>The shelf type a kit puts up, or -1.</summary>
        public static int ShelfVariant(Kind k) => k == Kind.Shelf ? ShelfTypes.Standard : k == Kind.TallShelf ? ShelfTypes.Tall : k == Kind.Rack ? ShelfTypes.Rack : -1;
        public static Kind ForShelf(int variant)
        {
            int t = variant & 0xF;
            return t == ShelfTypes.Tall ? Kind.TallShelf : t == ShelfTypes.Rack ? Kind.Rack : Kind.Shelf;
        }

        /// <summary>A kit crate's size: the chute's square crate, a shelf's long flat pack.</summary>
        public static Vector3 Size(Kind k)
        {
            switch (k)
            {
                case Kind.Shelf: return new Vector3(1.15f, 0.2f, 0.5f);
                case Kind.TallShelf: return new Vector3(1.15f, 0.24f, 0.5f);
                case Kind.Rack: return new Vector3(1.4f, 0.3f, 0.6f);
                case Kind.Generator: return new Vector3(0.75f, 0.6f, 0.55f);
                case Kind.Conveyor: return new Vector3(1.15f, 0.26f, 0.6f);
                case Kind.Floodlight: return new Vector3(0.6f, 0.5f, 0.45f);
                default: return new Vector3(0.7f, 0.5f, 0.5f);
            }
        }

        public static string Name(Kind k)
        {
            switch (k)
            {
                case Kind.Shelf: return "SHELVING";
                case Kind.TallShelf: return "TALL SHELVING";
                case Kind.Rack: return "PALLET RACKING";
                case Kind.Generator: return "GENERATOR";
                case Kind.Conveyor: return "CONVEYOR";
                case Kind.Floodlight: return "FLOODLIGHT";
                default: return "INTAKE CHUTE";
            }
        }

        /// <summary>A new kit crate (not the chute's: Improvements numbers those) at a place.</summary>
        public static ulong Spawn(Kind k, Vector3 at, Quaternion rot)
        {
            var d = GameSession.Data;
            var mgr = MovablePropManager.Instance;
            if (d == null || !mgr) return 0;
            ulong id = Id(k, d.nextKit++);
            var spec = Contracts.BoxSpec(id, at, rot.eulerAngles.y);
            if (spec == null) return 0;
            mgr.Place(spec, at, rot);
            return id;
        }
    }

    /// <summary>What finishing work orders has unlocked (keys in the save; WorkOrders gives them).</summary>
    public static class Unlocks
    {
        public static bool Has(string key)
        {
            var d = GameSession.Data;
            return d != null && d.unlocked.Contains(key);
        }

        public static void Give(string key)
        {
            var d = GameSession.Data;
            if (d != null && !d.unlocked.Contains(key)) d.unlocked.Add(key);
        }
    }

    /// <summary>
    /// The supply counter (2026-11-08, Docs/GameDesign.md section 10): a board on the lobby's south wall, west of the way to
    /// the lift. It sells what you carry down and use: kits and supplies, as many as you can pay for, at any time. NEXT steps
    /// through the list, ORDER buys the one picked; it waits by the lift on the floor. Its goods are unlocked by finishing
    /// work orders (WorkOrders); before onboarding is done the counter is shut.
    /// </summary>
    public class Supplies : MonoBehaviour
    {
        public static Supplies Instance { get; private set; }
        public WorldStreamer streamer;

        WorldSaveData D => GameSession.Data;

        enum What { Kit, DeskLamp, Cord, HammerAndNails, HandTruck, Truck, WorkLamp, Floodlight, RepairKit, SpareTubes }

        struct Item { public What what; public Kits.Kind kit; public string name, note, unlock; public int cost; public bool locked; }

        static readonly Item[] s_all =
        {
            new Item { what = What.Kit, kit = Kits.Kind.Shelf, name = "SHELVING KIT", note = "8 TO 12 BOXES - PUT IT UP ANYWHERE", cost = 60, unlock = "counter" },
            new Item { what = What.DeskLamp, name = "DESK LAMP", note = "PLUGS INTO ANY OUTLET", cost = 25, unlock = "counter" },
            new Item { what = What.Cord, name = "EXTENSION CORD", note = "THREE SOCKETS ON A LONG LEAD", cost = 20, unlock = "counter" },
            new Item { what = What.HammerAndNails, name = "HAMMER AND NAILS", note = "STAND UP KNOCKED-OVER SHELVES", cost = 30, unlock = "counter" },
            new Item { what = What.HandTruck, name = "HAND TRUCK", note = "4 OR 5 BOXES ON WHEELS", cost = 40, unlock = "handtruck" },
            new Item { what = What.Truck, name = "PLATFORM TRUCK", note = "BOXES RIDE ON IT", cost = 120, unlock = "truck" },
            new Item { what = What.WorkLamp, name = "WORK LAMP", note = "BRIGHT - LIGHTS TWO SHELVES", cost = 90, unlock = "worklamp" },
            new Item { what = What.Kit, kit = Kits.Kind.Chute, name = "INTAKE CHUTE KIT", note = "SET IT DOWN AT A SEAM", cost = 150, unlock = "chute" },
            new Item { what = What.Kit, kit = Kits.Kind.TallShelf, name = "TALL SHELVING KIT", note = "A DECK MORE", cost = 110, unlock = "tallshelf" },
            new Item { what = What.Kit, kit = Kits.Kind.Generator, name = "GENERATOR", note = "RUNS BIG MACHINES - ON THE POWER BILL", cost = 250, unlock = "generator" },
            new Item { what = What.RepairKit, name = "REPAIR KIT", note = "FIXES DEAD TUBES AND BROKEN SHELVES", cost = 80, unlock = "repairkit" },
            new Item { what = What.SpareTubes, name = "SPARE TUBES", note = "FOR DEAD CEILING LIGHTS", cost = 15, unlock = "counter" },
            new Item { what = What.Kit, kit = Kits.Kind.Conveyor, name = "CONVEYOR SECTION", note = "PLUGS INTO A GENERATOR", cost = 70, unlock = "conveyor" },
            new Item { what = What.Kit, kit = Kits.Kind.Floodlight, name = "FLOODLIGHT", note = "ON A GENERATOR - LIGHTS A ROW", cost = 200, unlock = "floodlight" },
            new Item { what = What.Kit, kit = Kits.Kind.Rack, name = "PALLET RACKING KIT", note = "BIG LOADS, THREE DECKS", cost = 220, unlock = "rack" },
        };

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
        }

        public void Changed() => _dirty = true;

        void List()
        {
            _items.Clear();
            // (2026-11-09, the owner: "how to get improvements? unlocked by work orders isn't clear". Everything is listed;
            // what is locked shows greyed with the work orders that open it, as Satisfactory shows a milestone's unlocks.)
            var locked = new List<Item>();
            foreach (var it0 in s_all)
            {
                var it = it0;
                if (it.what == What.Truck && D.trucks >= 2) continue;
                if (it.what == What.HandTruck && D.handTrucks.Count >= 8) continue;
                if (it.what == What.SpareTubes || it.what == What.RepairKit) { if (!Fixes.Ready) continue; }
                it.locked = !Unlocks.Has(it.unlock);
                (it.locked ? locked : _items).Add(it);
            }
            locked.Sort((a, b) => WorkOrders.StageOf(a.unlock).CompareTo(WorkOrders.StageOf(b.unlock)));
            _items.AddRange(locked);
            _pick = Mathf.Clamp(_pick, 0, Mathf.Max(0, _items.Count - 1));
        }

        void Next()
        {
            if (D == null) return;
            List();
            if (_items.Count == 0) { Contracts.Instance?.Beep(false); return; }
            _pick = (_pick + 1) % _items.Count;
            Contracts.Instance?.Beep(true);
            _dirty = true;
        }

        /// <summary>The host: orders the named thing for a joined player (the same as pressing ORDER on it). True if it went through.</summary>
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
            var cs = Contracts.Instance;
            if (_items.Count == 0) { cs?.Beep(false); return; }
            var it = _items[_pick];
            if (it.locked) { CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.locked_item", it.name, WorkOrders.FinishText(it.unlock))); cs?.Beep(false); return; }
            if (D.pay < it.cost) { CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.not_enough", D.pay)); cs?.Beep(false); return; }
            // Co-op: the crew's one wallet is the host's; a joined game asks it to pay and deliver.
            if (Net.WorkNet.Client) { Net.WorkNet.Ask("order " + it.name); cs?.Beep(true); return; }
            D.pay -= it.cost;
            Net.WorkNet.Changed();
            Deliver(it);
            CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.ordered_lift", it.name));
            cs?.Beep(true);
            cs?.Changed();
            Improvements.Instance?.Changed();
            _dirty = true;
        }

        // ---------------- Delivery ----------------

        /// <summary>A spot by the lift on the floor for the next thing delivered: either side of its gate, in rows.</summary>
        Vector3 Spot(out Quaternion rot)
        {
            int k = D.delivered++;
            int side = k % 2 == 0 ? 1 : -1;
            float across = side * (1.7f + 0.85f * (k / 2 % 3));
            float outward = 3.2f + 0.8f * (k / 6 % 3);
            return Improvements.ByTheLift(across, outward, out rot);
        }

        void Deliver(Item it)
        {
            var at = Spot(out var rot);
            switch (it.what)
            {
                case What.Kit:
                    if (it.kit == Kits.Kind.Chute) Improvements.Instance?.SendChuteKit(at, rot);
                    else Kits.Spawn(it.kit, at, rot);
                    break;
                case What.DeskLamp: DeskLamp.Create(at + Vector3.up * 0.02f, rot); break;
                case What.HandTruck: HandTruck.Create(at, rot); break;
                case What.Cord: CordBox.Create(at + Vector3.up * 0.02f, rot); break;
                case What.HammerAndNails:
                    Hammer.Create(at + Vector3.up * 0.016f, rot * Quaternion.Euler(-90f, 0f, 0f) * Quaternion.Euler(0f, 90f, 0f));
                    Nails.Create(at + rot * new Vector3(0.3f, 0.02f, 0f), rot);
                    break;
                case What.Truck: Improvements.Instance?.SendTruck(); break;
                case What.WorkLamp: WorkLamp.Create(at + Vector3.up * 0.02f, rot, false); break;
                case What.RepairKit: Fixes.CreateRepairKit(at + Vector3.up * 0.02f, rot); break;
                case What.SpareTubes: Fixes.CreateTubes(at + Vector3.up * 0.02f, rot); break;
            }
        }

        void Update()
        {
            if (D == null || Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.5f;
            if (_dirty) { _dirty = false; Draw(); }
        }

        // ---------------- The board ----------------

        const int BW = 480, BH = 252;
        Texture2D _tex;
        Color32[] _px;
        ElevatorButton _next, _order;

        /// <summary>HubBoard on the lobby's south wall, west of the way to the lift (OverflowRooms), facing north.</summary>
        public static readonly Vector2 BoardAt = new Vector2(6.9f, 7.2f);

        void BuildBoard()
        {
            float face = OfficeBiome.WallThickness * 0.5f;
            var foot = HubPlan.World(BoardAt.x, BoardAt.y + face, 0.7f);
            var rot = Quaternion.identity;
            _tex = Contracts.Screen(transform, "SuppliesBoardScreen", foot, rot, BW, BH, out _px);
            _next = Contracts.Button(transform, "SuppliesNext", foot + new Vector3(0.35f, -0.2f, 0f), rot, Next);
            _order = Contracts.Button(transform, "SuppliesOrder", foot + new Vector3(-0.35f, -0.2f, 0f), rot, Order);
            _next.SetLit(true);
        }

        void Draw()
        {
            if (_px == null) return;
            List();
            var G = Contracts.Ground; var A = Contracts.Amber; var Dm = Contracts.Dim;
            for (int i = 0; i < _px.Length; i++) _px[i] = G;
            PixelFont.Fill(_px, BW, BH, 0, 0, BW, 34, new Color32(217, 102, 31, 255));
            PixelFont.DrawCentred(_px, BW, BH, "SUPPLY COUNTER", BW / 2, 6, 3, Contracts.Choc);
            // (2026-11-10, U14/U15: six rows, so the picked one's note fits on two lines at 2 dots; at 1 dot it could not be
            // read from 3 m.)
            int rows = Mathf.Min(_items.Count, 6);
            int first = Mathf.Clamp(_pick - rows + 1, 0, Mathf.Max(0, _items.Count - rows));
            for (int r = 0; r < rows; r++)
            {
                int i = first + r;
                var it = _items[i];
                int y = 40 + r * 21;
                bool sel = i == _pick;
                if (sel) PixelFont.Fill(_px, BW, BH, 6, y - 2, BW - 6, y + 18, Contracts.Flap);
                var ink = it.locked ? (sel ? Contracts.LockedSel : Contracts.Locked) : sel ? A : D.pay >= it.cost ? new Color32(205, 160, 72, 255) : Dm;
                string cost = it.locked ? WorkOrders.LockedText(it.unlock) : "$" + it.cost;
                int costW = PixelFont.Width(cost, 2);
                PixelFont.Draw(_px, BW, BH, Generation.PixelScreen.Fit((sel ? "> " : "  ") + it.name, 2, BW - 34 - costW), 10, y + 1, 2, ink);
                PixelFont.Draw(_px, BW, BH, cost, BW - 12 - costW, y + 1, 2, ink);
            }
            if (_items.Count > 0)
            {
                var pk = _items[_pick];
                var (n1, n2) = Generation.PixelScreen.TwoLines(pk.locked ? WorkOrders.FinishText(pk.unlock) + " (J)" : pk.note, 2, BW - 20);
                PixelFont.DrawCentred(_px, BW, BH, n1, BW / 2, 168, 2, pk.locked ? Contracts.Red : Dm);
                PixelFont.DrawCentred(_px, BW, BH, n2, BW / 2, 185, 2, pk.locked ? Contracts.Red : Dm);
            }
            int bx(float dx) => Mathf.RoundToInt((0.5f - dx / 2.3f) * BW);
            PixelFont.DrawCentred(_px, BW, BH, "NEXT", bx(0.35f), 212, 2, A);
            bool canPick = _items.Count > 0 && !_items[_pick].locked;
            PixelFont.DrawCentred(_px, BW, BH, "ORDER", bx(-0.35f), 212, 2, canPick ? A : Dm);
            PixelFont.Fill(_px, BW, BH, 0, 246, BW, 248, new Color32(217, 102, 31, 255));
            PixelFont.Fill(_px, BW, BH, 0, 248, BW, 250, new Color32(232, 178, 58, 255));
            PixelFont.Fill(_px, BW, BH, 0, 250, BW, 252, new Color32(124, 138, 46, 255));
            PixelFont.Draw(_px, BW, BH, "DELIVERED BY THE LIFT", 12, 230, 2, A);
            string right = "IN HAND $" + D.pay;
            PixelFont.Draw(_px, BW, BH, right, BW - 12 - PixelFont.Width(right, 2), 230, 2, A);
            if (_order) _order.SetLit(canPick && D.pay >= _items[_pick].cost);
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }
    }
}
