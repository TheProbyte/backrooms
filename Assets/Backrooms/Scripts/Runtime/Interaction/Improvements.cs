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
    /// At most one a night (until the next day starts), never during the shift.
    /// <list type="bullet">
    /// <item>INTAKE CHUTE KIT: a crate waits by the lift on the floor; set it down in front of a seam and the chute is fitted
    ///   (Seam.Fit): from then on it pulls that place's boxes through by itself.</item>
    /// <item>PLATFORM TRUCK: a trolley by the lift; boxes ride on it (CartWheels).</item>
    /// <item>OVERTIME: closing an hour later (6, 7, 8 PM). BACKUP LIGHTS: a minute more after closing before the dark.</item>
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

        struct Item { public Kind kind; public int wing; public string name, note; public int cost; }

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
            void Add(Kind k, string name, string note, int cost, int wing = -1) => _items.Add(new Item { kind = k, name = name, note = note, cost = cost, wing = wing });
            Add(Kind.ChuteKit, "INTAKE CHUTE KIT", "SET IT DOWN AT A SEAM", 150);
            if (D.trucks < 2) Add(Kind.Truck, "PLATFORM TRUCK", "BOXES RIDE ON IT", 120);
            if (D.overtime < 3) Add(Kind.Overtime, $"OVERTIME TO {6 + D.overtime} PM", "AN HOUR MORE A SHIFT", 200 + 100 * D.overtime);
            if (D.backupLights < 2) Add(Kind.BackupLights, "BACKUP LIGHTS", "A MINUTE MORE AT CLOSING", 150 + 100 * D.backupLights);
            if (D.rentReviews < 2) Add(Kind.RentReview, "RENT REVIEW", "+$1 A STORED BOX A DAY", 250 + 150 * D.rentReviews);
            int owned = LeasePlan.WingCount;
            for (int w = 0; w < 4; w++)
                if ((D.wings & (1 << w)) == 0) Add(Kind.Wing, $"LEASE {LeasePlan.WingNames[w]} WING", "MORE SEAMS AND STORAGE", 400 + 200 * owned, w);
            if (!D.poolrooms) Add(Kind.Poolrooms, "POOLROOMS ACCESS", owned > 0 ? "A DOOR PAST YOUR FIRST WING" : "NEEDS A WING FIRST", 900);
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

        void Order()
        {
            if (D == null) return;
            List();
            if (_items.Count == 0) return;
            var it = _items[_pick];
            var cs = Contracts.Instance;
            string no = cs && cs.ShiftOn ? "NOT DURING THE SHIFT"
                      : D.boughtDay == D.day ? "ONE A NIGHT - ORDER AGAIN TOMORROW"
                      : it.kind == Kind.Poolrooms && LeasePlan.WingCount == 0 ? "LEASE A WING FIRST"
                      : D.pay < it.cost ? $"NOT ENOUGH - ${D.pay} IN HAND" : null;
            if (no != null) { CamcorderOSD.Notice(no); cs?.Beep(false); return; }
            D.pay -= it.cost;
            D.boughtDay = D.day;
            switch (it.kind)
            {
                case Kind.ChuteKit: PlaceKit(D.chuteKits++, true); break;
                case Kind.Truck: PlaceTruck(D.trucks++, true); break;
                case Kind.Overtime: D.overtime++; break;
                case Kind.BackupLights: D.backupLights++; break;
                case Kind.RentReview: D.rentReviews++; break;
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
            CamcorderOSD.Notice(it.kind == Kind.ChuteKit || it.kind == Kind.Truck ? $"ORDERED - {it.name} - WAITING BY THE LIFT" : $"ORDERED - {it.name}");
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

        /// <summary>Beside the lift on the floor: in front of its gate, off to one side.</summary>
        static Vector3 ByTheLift(float across, float outward, out Quaternion rot)
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
            if (spec != null) mgr.NetPlace(spec, at, rot);
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
            mgr.NetPlace(spec, at, rot);
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
                        CamcorderOSD.Notice("INTAKE CHUTE FITTED - " + LeaseSeams.Kinds[s.kind]);
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
            bool bought = D.boughtDay == D.day;
            int top = 40;
            int rows = Mathf.Min(_items.Count, 7);
            int first = Mathf.Clamp(_pick - rows + 1, 0, Mathf.Max(0, _items.Count - rows));
            for (int r = 0; r < rows; r++)
            {
                int i = first + r;
                var it = _items[i];
                int y = top + r * 21;
                bool sel = i == _pick;
                bool afford = D.pay >= it.cost && !bought;
                if (sel) PixelFont.Fill(_px, BW, BH, 6, y - 2, BW - 6, y + 18, Contracts.Flap);
                var ink = sel ? A : afford ? new Color32(205, 160, 72, 255) : Dm;
                PixelFont.Draw(_px, BW, BH, (sel ? "> " : "  ") + it.name, 10, y + 1, 2, ink);
                string cost = "$" + it.cost;
                PixelFont.Draw(_px, BW, BH, cost, BW - 12 - PixelFont.Width(cost, 2), y + 1, 2, ink);
            }
            // The picked one's note, and the buttons' labels.
            if (_items.Count > 0) PixelFont.DrawCentred(_px, BW, BH, _items[_pick].note, BW / 2, 214 - 22, 1, Dm);
            int bx(float dx) => Mathf.RoundToInt((0.5f - dx / 2.3f) * BW);
            PixelFont.DrawCentred(_px, BW, BH, "NEXT", bx(0.35f), 212, 2, A);
            PixelFont.DrawCentred(_px, BW, BH, "ORDER", bx(-0.35f), 212, 2, bought ? Dm : A);
            PixelFont.Fill(_px, BW, BH, 0, 246, BW, 248, new Color32(217, 102, 31, 255));
            PixelFont.Fill(_px, BW, BH, 0, 248, BW, 250, new Color32(232, 178, 58, 255));
            PixelFont.Fill(_px, BW, BH, 0, 250, BW, 252, new Color32(124, 138, 46, 255));
            string left = bought ? "ORDERED TONIGHT" : "ONE A NIGHT";
            PixelFont.Draw(_px, BW, BH, left, 12, 230, 2, bought ? Contracts.Green : A);
            string right = "IN HAND $" + D.pay;
            PixelFont.Draw(_px, BW, BH, right, BW - 12 - PixelFont.Width(right, 2), 230, 2, A);
            if (_order) _order.SetLit(!bought && _items.Count > 0 && D.pay >= _items[_pick].cost);
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }
    }
}
