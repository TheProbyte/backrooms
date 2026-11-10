using System.Collections.Generic;
using System.Text;
using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The trip's pay slip (2026-11-09, decision 2: "the slip prints by itself every time you ride the lift back up"). Each
    /// payment while you are out (a batch stored, a batch returned, late or scuffed) goes on this trip's slip, and the power
    /// it ran up; when you come back up into the hub it prints on the camcorder's screen (CamcorderOSD.ShowSlip), as each
    /// day used to end on its report. The pay window's PAY SLIPS button shows the last one again. The punch clock on the lift
    /// hall's wall is decoration now.
    /// </summary>
    public partial class Contracts
    {
        AudioSource _slipSound;
        bool _wasOut;
        static AudioClip s_slipClip;

        /// <summary>A line on this trip's slip (left, and the amount on the right).</summary>
        void Trip(string left, string right) => D.trip.Add(left + "\t" + right);

        /// <summary>Each frame: back up in the hub from a trip with something on its slip, it prints.</summary>
        void SlipFrame()
        {
            var player = streamer ? streamer.player : null;
            if (!player) return;
            bool out_ = !HubPlan.InZone(player.position);
            if (_wasOut && !out_ && (D.trip.Count > 0 || D.tripPower > 0) && (!Net.WorkNet.Client || TripKey() != _tripShown)) PrintTrip();
            _wasOut = out_;
        }

        string _tripShown = "";
        string TripKey() => string.Join("|", D.trip) + "|" + D.tripPower;

        void PrintTrip()
        {
            var lines = new List<(string, string)>();
            // Co-op: the crew's trip is the host's to close (it clears the slip when the host comes up); a joined game
            // shows the same lines when its own player comes up, once.
            bool follow = Net.WorkNet.Client;
            if (follow) _tripShown = TripKey(); else D.trips++;
            lines.Add(($"TRIP {D.trips}", DateText));
            lines.Add(("--", ""));
            int total = 0;
            foreach (var l in D.trip)
            {
                int tab = l.IndexOf('\t');
                string a = tab < 0 ? l : l.Substring(0, tab), b = tab < 0 ? "" : l.Substring(tab + 1);
                lines.Add((a, b));
                if (b.StartsWith("$") && int.TryParse(b.Substring(1), out int v)) total += v;
            }
            if (D.tripPower > 0) { lines.Add(("POWER", "-$" + D.tripPower)); total -= D.tripPower; }
            if (D.powerOwed >= 1f) lines.Add(("POWER OWED (FROM NEXT PAY)", "$" + Mathf.FloorToInt(D.powerOwed)));
            lines.Add(("--", ""));
            lines.Add(("THIS TRIP", (total < 0 ? "-$" : "$") + Mathf.Abs(total)));
            lines.Add(("IN HAND", "$" + D.pay));
            if (follow) { CamcorderOSD.ShowSlip(Join(lines)); SlipSound(); return; }
            D.lastSlip = Join(lines);
            D.trip.Clear();
            D.tripPower = 0;
            Net.WorkNet.Changed();
            CamcorderOSD.ShowSlip(D.lastSlip);
            SlipSound();
            _dirty = true;
        }

        static string Join(List<(string left, string right)> lines)
        {
            var sb = new StringBuilder();
            foreach (var (l, r) in lines) sb.Append(l).Append('\t').Append(r).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>The pay window's button: the last trip's slip again.</summary>
        public void PayWindow()
        {
            if (D == null) return;
            if (string.IsNullOrEmpty(D.lastSlip)) { CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.no_slips")); Beep(false); return; }
            CamcorderOSD.ShowSlip(D.lastSlip);
            Beep(true);
        }

        /// <summary>The slip coming out of the window's printer: a short chatter and a tear.</summary>
        void SlipSound()
        {
            if (!_slipSound) return;
            if (!s_slipClip)
            {
                int sr = 44100, n = sr * 9 / 10;
                var d = new float[n];
                var rng = new System.Random(11);
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)sr;
                    float chatter = t < 0.6f ? (Mathf.Sin(2f * Mathf.PI * 34f * t) > 0.3f ? 1f : 0.2f) * (float)(rng.NextDouble() - 0.5) * 0.5f : 0f;
                    float tt = t - 0.66f;
                    float tear = tt < 0f || tt > 0.18f ? 0f : (float)(rng.NextDouble() - 0.5) * Mathf.Exp(-tt * 18f) * 0.9f;
                    d[i] = (chatter + tear) * 0.6f;
                }
                s_slipClip = AudioClip.Create("PaySlip", n, 1, sr, false);
                s_slipClip.SetData(d, 0);
            }
            _slipSound.PlayOneShot(s_slipClip, 0.5f);
        }
    }
}
