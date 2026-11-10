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
        public string TrackerText()
        {
            if (D == null) return "";
            _tb.Clear();
            Step(1f);
            Orders(1f, false);
            Batches(1f, false);
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
                _full = _c ? _c.TrackerText() : "";
                _step = _c ? _c.StepText() : "";
                // Hidden, it still shows for a while when something on it changes (an order moved on, new boxes).
                if (_full != _last && _last.Length > 0 && _full.Length > 0) _popUntil = Time.unscaledTime + PopFor;
                _last = _full;
                if (side && _c)
                {
                    // (A 16:9 screen's bars are 240 wide: the text a little smaller there.)
                    float scale = Mathf.Clamp(CamcorderOSD.SideWidth / 420f, 0.72f, 1f);
                    CamcorderOSD.SideRight = _c.SideWork(scale) + "\n\n<size=15><color=#968A6E>J - HIDE</color></size>";
                    CamcorderOSD.SideLeft = _c.SideBatches(scale);
                }
            }
            float pop = Mathf.Clamp01((_popUntil - Time.unscaledTime) / 0.6f);
            bool popping = pop > 0.01f;
            // With no bars: the next step alone (mode 1), the whole list in mode 2 or while something on it has just changed.
            CamcorderOSD.Tracker = _mode == 1 && !popping ? _step : _full;
            CamcorderOSD.TrackerAlpha = _mode != 0 ? 1f : pop;
            CamcorderOSD.TrackerHint = _mode == 0 ? "J - WORK" : _mode == 1 ? "J - ALL WORK" : "J - HIDE";
            CamcorderOSD.SideAlpha = _mode != 0 ? 1f : pop;
        }

        void OnDisable() { CamcorderOSD.Tracker = ""; CamcorderOSD.TrackerAlpha = 0f; CamcorderOSD.SideRight = CamcorderOSD.SideLeft = ""; }
    }
}
