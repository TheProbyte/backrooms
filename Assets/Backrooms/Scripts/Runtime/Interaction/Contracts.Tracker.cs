using System.Text;
using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The work orders on the camcorder's screen (2026-11-08, the owner: "a way to see the contract or next tier ... instead
    /// of forgetting and having to go back to hub"). The board's orders, pinned in the top right as Satisfactory pins its
    /// milestone: each open order with its instruction, its progress and today's boxes, what the tier unlocks when it is
    /// done, and what to do next. J hides it; hidden, it still pops up for a few seconds when an order moves on.
    /// </summary>
    public partial class Contracts
    {
        readonly StringBuilder _tb = new StringBuilder();

        static string SeamName(int kind) => LeaseSeams.Kinds[Mathf.Clamp(kind, 0, LeaseSeams.Kinds.Length - 1)];

        /// <summary>The tracker's text (rich text: the order titles bigger, the rest small), or "" with nothing to show.</summary>
        public string TrackerText()
        {
            if (D == null) return "";
            _tb.Clear();
            if (WorkOrders.Active)
            {
                EnsureStorage();
                var open = WorkOrders.Open();
                int inStage = 0, doneInStage = 0;
                for (int i = 0; i < WorkOrders.All.Length; i++)
                    if (WorkOrders.All[i].stage == D.woStage) { inStage++; if (WorkOrders.Done(i)) doneInStage++; }
                _tb.Append("<size=22>").Append(WorkOrders.StageName).Append(" - ").Append(doneInStage).Append(" OF ").Append(inStage).Append(" DONE</size>\n");
                foreach (int idx in open)
                {
                    var o = WorkOrders.All[idx];
                    int client = WorkOrders.ClientFor(idx, _seams, LiftPoint);
                    RowText(idx, client, out string line, out string prog, out string today, out var c);
                    // Which seam (the board shows it by the client's colour; here it is named).
                    if (c != null && c.state == 1)
                        today = today.Replace("AT THE SEAM", c.kind == 2 ? "FOR THE " + SeamName(c.toKind) + " SEAM" : "AT THE " + SeamName(c.fromKind) + " SEAM" + (c.kind == 1 ? " > " + SeamName(c.toKind) : ""));
                    _tb.Append("<size=29>").Append(o.title).Append("  $").Append(o.pay).Append("</size>\n");
                    _tb.Append("<size=22>").Append(line).Append("</size>\n");
                    if (prog.Length > 0) _tb.Append("<size=22>").Append(prog).Append("</size>\n");
                    if (today.Length > 0) _tb.Append("<size=22>").Append(today).Append("</size>\n");
                }
                string unlocks = WorkOrders.UnlocksOf(D.woStage);
                if (unlocks.Length > 0) _tb.Append("<size=22>WHEN DONE: ").Append(unlocks).Append("</size>\n");
            }
            else
            {
                int n = 0;
                foreach (var c in D.board)
                {
                    if (c.state != 1 && c.state != 2) continue;
                    if (n++ == 0) _tb.Append("<size=22>TODAY'S JOBS</size>\n");
                    string from = LeaseSeams.Kinds[Mathf.Clamp(c.fromKind, 0, 3)], to = LeaseSeams.Kinds[Mathf.Clamp(c.toKind, 0, 3)];
                    string what = c.kind == 0 ? $"STORE {c.count} BOXES" : c.kind == 1 ? $"SHIP {c.count} BOXES" : $"RETURN {c.count} BOXES";
                    string route = c.kind == 0 ? from + " > STORAGE" : c.kind == 1 ? from + " > " + to : "STORAGE > " + to;
                    _tb.Append("<size=29>").Append(what).Append("  $").Append(c.pay).Append("</size>\n");
                    _tb.Append("<size=22>").Append(route).Append("</size>\n");
                    _tb.Append("<size=22>").Append(c.state == 2 ? "DONE - COLLECT PAY" : $"{c.done} OF {c.count}").Append("</size>\n");
                }
                if (n == 0) return NextStep.Length > 0 ? "<size=24>> " + NextStep + "</size>" : "";
            }
            string next = NextStep;
            if (next.Length > 0) _tb.Append("<size=24>> ").Append(next).Append("</size>");
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
            CamcorderOSD.TrackerHint = _shown ? "J - HIDE" : "J - WORK ORDERS";
        }

        void OnDisable() { CamcorderOSD.Tracker = ""; CamcorderOSD.TrackerAlpha = 0f; }
    }
}
