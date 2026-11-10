using System.Collections.Generic;
using System.Text;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The work on the camcorder's screen (2026-11-08, the owner: "a way to see the contract or next tier ... instead of
    /// forgetting and having to go back to hub"; batches 2026-11-09). Pinned in the top right as Satisfactory pins its
    /// milestone: the stage's orders with their progress and what they unlock; then your batches, each with where it is and
    /// how long is left; and what to do next. J hides it; hidden, it still pops up for a few seconds when something moves on.
    /// </summary>
    public partial class Contracts
    {
        readonly StringBuilder _tb = new StringBuilder();

        /// <summary>
        /// The tracker's text (rich text, sizes only). 2026-11-09, the owner: "J to show work details is just words and way too
        /// complicated". Now, like a game's pinned objective: the one next step on top; this stage's work orders as a
        /// checklist with a count where there is one, and what finishing them unlocks; your batches one short line each (the
        /// board's words). The long explanations stay on the boards and in the notices.
        /// </summary>
        public string TrackerText(bool batches = true)
        {
            if (D == null) return "";
            _tb.Clear();
            Step(1f);
            Orders(1f, false);
            if (batches) Batches(1f, false);
            return _tb.ToString().TrimEnd('\n');
        }

        /// <summary>Only the one next step (the picture's overlay when there are no side bars to hold the list).</summary>
        public string StepText()
        {
            if (D == null) return "";
            _tb.Clear();
            Step(1f);
            return _tb.ToString().TrimEnd('\n');
        }

        /// <summary>The right side panel: the next step and this stage's orders. Sizes scaled for a narrow bar.</summary>
        public string SideWork(float scale)
        {
            if (D == null) return "";
            _tb.Clear();
            _tb.Append("<size=").Append(Sz(18, scale)).Append("><color=#F0A21E>OVERFLOW WORK</color></size>\n");
            Step(scale);
            Orders(scale, true);
            return _tb.ToString().TrimEnd('\n');
        }

        /// <summary>The left side panel: your batches, what clients need, what is in hand, and whether time is running.</summary>
        public string SideBatches(float scale)
        {
            if (D == null) return "";
            _tb.Clear();
            _tb.Append("<size=").Append(Sz(18, scale)).Append("><color=#F0A21E>").Append(DateText).Append("</color></size>\n");
            _tb.Append("<size=").Append(Sz(30, scale)).Append(">IN HAND  $").Append(D.pay).Append("</size>\n");
            _tb.Append("<size=").Append(Sz(18, scale)).Append(">").Append(Running ? "BUSINESS TIME RUNNING" : "<color=#968A6E>TIME STOPPED - OFF THE FLOOR</color>").Append("</size>\n");
            Batches(scale, true);
            return _tb.ToString().TrimEnd('\n');
        }

        static int Sz(int size, float scale) => Mathf.Max(13, Mathf.RoundToInt(size * scale));

        // ---- The side panels as data (2026-11-10, Docs/UiPlan.md U4): UI.SidePanels draws them as cards and rows. ----

        /// <summary>A batch as a card: whose (colour, pictogram), how many, what to do now in a word or two and in what
        /// colour, how long, and a bar (0..1, -1 none) that drains as the time runs out or fills as the boxes go up.</summary>
        public struct BatchView { public int client, count; public string name, state, time; public bool rush, dim; public Color ink; public float fill; }

        /// <summary>A work order as a row: its title, its count against its target (target 0: no count), done or not.</summary>
        public struct OrderView { public string title; public int have, need; public bool done; }

        public sealed class SideView
        {
            public string date = "", next = "", stage = "", unlocks = "";
            public int pay, stageDone, stageTotal;
            public bool running;
            public readonly List<BatchView> batches = new List<BatchView>();
            public readonly List<OrderView> orders = new List<OrderView>();
            /// <summary>Clients whose work needs something you lack, or who are on hold.</summary>
            public readonly List<string> notes = new List<string>();
        }

        public void FillSide(SideView v)
        {
            v.batches.Clear(); v.orders.Clear(); v.notes.Clear();
            v.date = v.next = v.stage = v.unlocks = ""; v.pay = v.stageDone = v.stageTotal = 0;
            if (D == null) return;
            v.date = DateText; v.pay = D.pay; v.running = Running; v.next = NextStep;
            var list = new List<BatchData>(D.batches);
            list.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            var mgr = MovablePropManager.Instance;
            foreach (var b in list)
            {
                if (b.state == 0 && !D.seamsFound.Contains(b.client)) continue;
                if (b.state == 0 && (NeedOf(b.client) != null || OnHold(b.client))) continue;
                var c = new BatchView { client = Mathf.Clamp(b.client, 0, 3), name = Name(b.client), count = b.count, rush = b.rush, fill = -1f, time = "", ink = UI.UiKit.Tan };
                switch (b.state)
                {
                    case 0: c.state = "PICK UP"; c.dim = true; break;
                    case 1:
                    {
                        int safe = mgr ? SafeCount(b, mgr) : 0, total = Mathf.Max(1, b.boxes.Count);
                        int left = b.boxes.Count - safe;
                        c.state = left > 0 ? $"SHELVE {left}" : "ALL SHELVED"; c.ink = UI.UiKit.Amber; c.fill = safe / (float)total;
                        break;
                    }
                    case 2:
                        c.state = "STORED"; c.ink = UI.UiKit.Good; c.time = MinSec(b.left);
                        c.fill = b.storeFor > 0f ? Mathf.Clamp01(b.left / b.storeFor) : 0f;
                        break;
                    default:
                        bool late = b.since > b.window;
                        c.state = late ? "LATE" : "TAKE BACK"; c.ink = late ? UI.UiKit.Bad : UI.UiKit.Amber;
                        c.time = late ? "" : MinSec(b.window - b.since);
                        c.fill = late ? 0f : b.window > 0f ? Mathf.Clamp01(1f - b.since / b.window) : 0f;
                        break;
                }
                v.batches.Add(c);
            }
            foreach (int k in D.seamsFound)
            {
                string need = NeedOf(k);
                if (need != null) v.notes.Add($"{Name(k)} NEEDS {need}");
                else if (OnHold(k)) v.notes.Add($"{Name(k)} ON HOLD - RETURN THEIR LATE BOXES");
            }
            if (!WorkOrders.Active) return;
            v.stage = WorkOrders.StageName; v.unlocks = WorkOrders.UnlocksOf(D.woStage);
            for (int i = 0; i < WorkOrders.All.Length; i++)
            {
                var o = WorkOrders.All[i];
                if (o.stage != D.woStage) continue;
                bool done = WorkOrders.Done(i);
                v.stageTotal++; if (done) v.stageDone++;
                bool counted = WorkOrders.CountText(i).Length > 0 && o.target > 1;
                v.orders.Add(new OrderView { title = o.title, done = done, need = counted ? o.target : 0, have = counted ? Mathf.Min(WorkOrders.Progress(i), o.target) : 0 });
            }
        }

        static float Rank(BatchData b) => b.state == 3 ? -1000f + (b.window - b.since) : b.state == 2 ? b.left : b.state == 1 ? 100000f : 200000f;

        void Step(float scale)
        {
            string next = NextStep;
            if (next.Length > 0) _tb.Append("<size=").Append(Sz(26, scale)).Append(">> ").Append(next).Append("</size>\n");
        }

        void Orders(float scale, bool side)
        {
            if (!WorkOrders.Active) return;
            int inStage = 0, doneInStage = 0;
            for (int i = 0; i < WorkOrders.All.Length; i++)
                if (WorkOrders.All[i].stage == D.woStage) { inStage++; if (WorkOrders.Done(i)) doneInStage++; }
            _tb.Append("\n<size=").Append(Sz(20, scale)).Append(">").Append(side ? "<color=#F0A21E>" : "").Append(WorkOrders.StageName).Append("  ").Append(doneInStage).Append("/").Append(inStage)
               .Append(side ? "</color>" : "").Append("</size>\n");
            for (int i = 0; i < WorkOrders.All.Length; i++)
            {
                var o = WorkOrders.All[i];
                if (o.stage != D.woStage) continue;
                bool done = WorkOrders.Done(i);
                string count = done ? "" : WorkOrders.CountText(i);
                _tb.Append("<size=").Append(Sz(23, scale)).Append(">");
                if (side && done) _tb.Append("<color=#8CD26E>");
                _tb.Append(done ? "[X] " : "[  ] ").Append(o.title);
                if (count.Length > 0) _tb.Append("  ").Append(count);
                if (side && done) _tb.Append("</color>");
                _tb.Append("</size>\n");
            }
            string unlocks = WorkOrders.UnlocksOf(D.woStage);
            if (unlocks.Length > 0) _tb.Append("<size=").Append(Sz(19, scale)).Append(">UNLOCKS: ").Append(unlocks).Append("</size>\n");
        }

        void Batches(float scale, bool side)
        {
            int n = 0;
            foreach (var b in D.batches)
            {
                if (b.state == 0 && !D.seamsFound.Contains(b.client)) continue;
                if (b.state == 0 && (NeedOf(b.client) != null || OnHold(b.client))) continue;
                if (n++ == 0) _tb.Append("\n<size=").Append(Sz(20, scale)).Append(">").Append(side ? "<color=#F0A21E>YOUR BATCHES</color>" : "YOUR BATCHES").Append("</size>\n");
                var row = BatchRow(b);
                _tb.Append("<size=").Append(Sz(23, scale)).Append(">");
                if (side)
                {
                    // The client's colour as a block, then two lines: whose and how many; what to do and how long.
                    var c = (Color32)ClientColours[Mathf.Clamp(b.client, 0, 3)];
                    var ink = row.rightInk;
                    _tb.Append("<color=#").Append(c.r.ToString("X2")).Append(c.g.ToString("X2")).Append(c.b.ToString("X2")).Append(">\u2588</color> ")
                       .Append(row.text).Append("\n    <color=#").Append(ink.r.ToString("X2")).Append(ink.g.ToString("X2")).Append(ink.b.ToString("X2")).Append(">").Append(row.right).Append("</color>");
                }
                else _tb.Append(row.text).Append(" - ").Append(row.right);
                _tb.Append("</size>\n");
            }
            // Found clients whose work needs something you do not have yet.
            foreach (int k in D.seamsFound)
            {
                string need = NeedOf(k);
                if (need != null) _tb.Append("<size=").Append(Sz(20, scale)).Append(">").Append(Name(k)).Append(" NEEDS ").Append(need).Append("</size>\n");
                else if (OnHold(k)) _tb.Append("<size=").Append(Sz(20, scale)).Append(">").Append(Name(k)).Append(" ON HOLD - RETURN THEIR LATE BOXES</size>\n");
            }
        }
    }

    /// <summary>Feeds the tracker to the camcorder's screen (CamcorderOSD.Tracker) and takes J to hide and show it.</summary>
    public class OrderTracker : MonoBehaviour
    {
        const string Pref = "OrderTrackerMode";
        const float PopFor = 6f;
        /// <summary>0 hidden; 1 shown (in the side bars: the panels; with no bars: the one next step in the picture);
        /// 2 the whole list in the picture (only with no bars).</summary>
        int _mode = 1;
        float _next, _popUntil = -100f;
        string _last = "", _full = "", _step = "";
        Contracts _c;

        /// <summary>Is the work pinned (J)? Saved between sessions.</summary>
        public static bool Shown { get; private set; } = true;

        void Start()
        {
            _c = GetComponent<Contracts>();
            try { _mode = Mathf.Clamp(PlayerPrefs.GetInt(Pref, 1), 0, 2); } catch { _mode = 1; }
            Shown = _mode != 0;
        }

        public void SetMode(int mode)
        {
            _mode = Mathf.Clamp(mode, 0, 2);
            Shown = _mode != 0;
            try { PlayerPrefs.SetInt(Pref, _mode); } catch { }
        }

        void Update()
        {
            bool side = CamcorderOSD.SideWidth >= CamcorderOSD.MinSide;
            var kb = Keyboard.current;
            if (kb != null && kb.jKey.wasPressedThisFrame && !CamcorderPlayerController.InputBlocked && GUIUtility.keyboardControl == 0)
                SetMode(side ? (_mode == 0 ? 1 : 0) : (_mode + 1) % 3);          // (no bars: next step, the whole list, hidden)
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + 0.25f;
                // (2026-11-10, Docs/UiPlan.md U17) With a compact left panel (a 16:10 screen) the batches are there: the
                // list in the picture keeps to the step and the orders.
                bool leftPanel = CamcorderOSD.SideWidth >= Backrooms.UI.SidePanels.MinCompact;
                _full = _c ? _c.TrackerText(!leftPanel) : "";
                _step = _c ? _c.StepText() : "";
                // Hidden, it still shows for a while when something on it changes (an order moved on, new boxes).
                if (_full != _last && _last.Length > 0 && _full.Length > 0) _popUntil = Time.unscaledTime + PopFor;
                _last = _full;
                // (The side panels read the work themselves: UI.SidePanels, from Contracts.FillSide.)
            }
            float pop = Mathf.Clamp01((_popUntil - Time.unscaledTime) / 0.6f);
            bool popping = pop > 0.01f;
            // With no bars: the next step alone (mode 1), the whole list in mode 2 or while something on it has just changed.
            CamcorderOSD.Tracker = _mode == 1 && !popping ? _step : _full;
            CamcorderOSD.TrackerAlpha = _mode != 0 ? 1f : pop;
            CamcorderOSD.TrackerHint = _mode == 0 ? "J - WORK" : _mode == 1 ? "J - ALL WORK" : "J - HIDE";
            CamcorderOSD.SideAlpha = _mode != 0 ? 1f : pop;
        }

        void OnDisable() { CamcorderOSD.Tracker = ""; CamcorderOSD.TrackerAlpha = 0f; }
    }
}
