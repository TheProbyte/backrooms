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
            string next = NextStep;
            if (next.Length > 0) _tb.Append("<size=26>> ").Append(next).Append("</size>\n");
            if (WorkOrders.Active)
            {
                int inStage = 0, doneInStage = 0;
                for (int i = 0; i < WorkOrders.All.Length; i++)
                    if (WorkOrders.All[i].stage == D.woStage) { inStage++; if (WorkOrders.Done(i)) doneInStage++; }
                _tb.Append("\n<size=20>").Append(WorkOrders.StageName).Append("  ").Append(doneInStage).Append("/").Append(inStage).Append("</size>\n");
                for (int i = 0; i < WorkOrders.All.Length; i++)
                {
                    var o = WorkOrders.All[i];
                    if (o.stage != D.woStage) continue;
                    bool done = WorkOrders.Done(i);
                    string count = done ? "" : WorkOrders.CountText(i);
                    _tb.Append("<size=23>").Append(done ? "[X] " : "[  ] ").Append(o.title);
                    if (count.Length > 0) _tb.Append("  ").Append(count);
                    _tb.Append("</size>\n");
                }
                string unlocks = WorkOrders.UnlocksOf(D.woStage);
                if (unlocks.Length > 0) _tb.Append("<size=19>UNLOCKS: ").Append(unlocks).Append("</size>\n");
            }
            int n = 0;
            foreach (var b in D.batches)
            {
                if (b.state == 0 && !D.seamsFound.Contains(b.client)) continue;
                if (b.state == 0 && (NeedOf(b.client) != null || OnHold(b.client))) continue;
                if (n++ == 0) _tb.Append("\n<size=20>YOUR BATCHES</size>\n");
                var row = BatchRow(b);
                _tb.Append("<size=23>").Append(row.text).Append(" - ").Append(row.right).Append("</size>\n");
            }
            // Found clients whose work needs something you do not have yet.
            foreach (int k in D.seamsFound)
            {
                string need = NeedOf(k);
                if (need != null) _tb.Append("<size=20>").Append(Name(k)).Append(" NEEDS ").Append(need).Append("</size>\n");
                else if (OnHold(k)) _tb.Append("<size=20>").Append(Name(k)).Append(" ON HOLD - RETURN THEIR LATE BOXES</size>\n");
            }
            return _tb.ToString().TrimEnd('\n');
        }
    }

    /// <summary>Feeds the tracker to the camcorder's screen (CamcorderOSD.Tracker) and takes J to hide and show it.</summary>
    public class OrderTracker : MonoBehaviour
    {
        const string Pref = "OrderTrackerShown";
        const float PopFor = 6f;
        bool _shown = true;
        float _next, _popUntil = -100f;
        string _last = "";
        Contracts _c;

        /// <summary>Is the tracker pinned (J)? Saved between sessions.</summary>
        public static bool Shown { get; private set; } = true;

        void Start()
        {
            _c = GetComponent<Contracts>();
            try { _shown = PlayerPrefs.GetInt(Pref, 1) != 0; } catch { _shown = true; }
            Shown = _shown;
        }

        public void SetShown(bool on)
        {
            _shown = Shown = on;
            try { PlayerPrefs.SetInt(Pref, on ? 1 : 0); } catch { }
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.jKey.wasPressedThisFrame && !CamcorderPlayerController.InputBlocked && GUIUtility.keyboardControl == 0)
                SetShown(!_shown);
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + 0.25f;
                string text = _c ? _c.TrackerText() : "";
                // Hidden, it still shows for a while when something on it changes (an order moved on, new boxes).
                if (text != _last && _last.Length > 0 && text.Length > 0) _popUntil = Time.unscaledTime + PopFor;
                _last = text;
                CamcorderOSD.Tracker = text;
            }
            float pop = Mathf.Clamp01((_popUntil - Time.unscaledTime) / 0.6f);
            CamcorderOSD.TrackerAlpha = _shown ? 1f : pop;
            CamcorderOSD.TrackerHint = _shown ? "J - HIDE" : "J - WORK";
        }

        void OnDisable() { CamcorderOSD.Tracker = ""; CamcorderOSD.TrackerAlpha = 0f; }
    }
}
