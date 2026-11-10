using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Interaction;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Net
{
    /// <summary>
    /// The work, shared (2026-11-09, Docs/ImprovementPlan.md F3; the owner: "you can move coop up"). Until now each
    /// player's game ran the work for itself: its own lots, its own money, its own shelves. Now there is one job:
    /// <list type="bullet">
    /// <item><b>The host runs the work</b> (Interaction.Contracts: lots, countdowns, payments, work orders, the power
    ///   bill) and sends its state to everyone whenever it changes, at most twice a second. A joining player's game stops
    ///   simulating and shows what the host says: the same batches, the same money, the same tickets, board and cards.</item>
    /// <item><b>One wallet, anyone may spend.</b> Ordering at the supply counter or the improvements board from a
    ///   joined game asks the host, which pays and delivers.</item>
    /// <item><b>Anyone may build.</b> A shelf or machine put up (or packed away) by anyone is told to the host; the
    ///   light each player's game measures at the shelves near them is too, so a shelf lit by a friend's lamp far from
    ///   the host still counts.</item>
    /// <item><b>What the game itself puts in the world</b> (a lot stacked behind a seam, a kit delivered, a shelf) is sent
    ///   to every game as it is made, and taken away from every game when it goes (MovablePropManager.Place / Forget).</item>
    /// <item><b>The moments</b> (the STORED stamp, the receipt, the count-up) and the notices are sent as they happen, so
    ///   everyone gets paid at the same instant.</item>
    /// </list>
    /// Lots grow with the crew: half as many boxes again for each extra player (Contracts.EnsureLot).
    /// </summary>
    public static class WorkNet
    {
        /// <summary>This game follows a host's work (joined a session).</summary>
        public static bool Client => NetSession.Active && !NetSession.IsHost;
        /// <summary>This game runs the work for others (hosting a session).</summary>
        public static bool Hosting => NetSession.Active && NetSession.IsHost;
        /// <summary>Business time is running, as the host says (a joined game).</summary>
        public static bool HostRunning;
        /// <summary>How many players the work is sized for.</summary>
        public static int Players => NetSession.Active ? Mathf.Clamp(NetSession.Peers.Count, 1, 4) : 1;

        /// <summary>A joined game asks the host to do something to the work (a word and its arguments).</summary>
        public static void Ask(string command)
        {
            if (!Client || !NetWorld.Ready) return;
            NetSession.Write(Msg.WorkAsk).Str(command);
            NetSession.SendWorld(true);
        }

        /// <summary>The host's notice, on everyone's screen.</summary>
        public static void Say(string text)
        {
            if (!Hosting || !NetWorld.Ready || string.IsNullOrEmpty(text)) return;
            NetSession.Write(Msg.WorkSay).Str(text);
            NetSession.SendWorld(true);
        }

        /// <summary>The host: a batch was stored (its tickets stamped, the intake paid).</summary>
        public static void Stored(int batch, int amount)
        {
            if (!Hosting || !NetWorld.Ready) return;
            NetWorld.Instance.SendWorkNow();
            var w = NetSession.Write(Msg.WorkEvent);
            w.Byte(1); w.Int(batch); w.Int(amount);
            NetSession.SendWorld(true);
        }

        /// <summary>The host: a batch was signed back in (its receipt, the completion paid).</summary>
        public static void Returned(int client, int amount, string[] lines)
        {
            if (!Hosting || !NetWorld.Ready) return;
            NetWorld.Instance.SendWorkNow();
            var w = NetSession.Write(Msg.WorkEvent);
            w.Byte(2); w.Int(client); w.Int(amount);
            w.Str(string.Join("\n", lines ?? new string[0]));
            NetSession.SendWorld(true);
        }

        /// <summary>The host: something about the work changed by hand (a purchase): send it now rather than at the next beat.</summary>
        public static void Changed() { if (Hosting && NetWorld.Instance) NetWorld.Instance.WorkChanged(); }
    }

    public partial class NetWorld
    {
        /// <summary>The save's fields that are the work (everything the host decides and a joined game only shows).</summary>
        static readonly string[] WorkFieldNames =
        {
            "pay", "earned", "day", "nextBox", "stored", "wings", "poolrooms", "chutes", "chuteKits", "trucks", "rates",
            "shelves", "nextShelf", "shelfLightIds", "shelfLight", "starterSet", "foundIn", "lastSlip", "delivered", "nextKit",
            "kitShelves", "unlocked", "woStage", "woProgress", "woDone", "tapeWatched", "fragile", "workVersion", "batches",
            "nextBatch", "business", "lotAt", "seamsFound", "otherSeamFound", "batchesIn", "batchesDone", "batchesOnTime",
            "clientsServed", "scuffed", "collect", "trip", "trips", "powerOwed", "tripPower", "generators", "conveyors",
            "floodlights", "plugMachine", "plugGenerator",
        };
        static FieldInfo[] s_workFields;
        static FieldInfo[] WorkFields()
        {
            if (s_workFields != null) return s_workFields;
            var list = new List<FieldInfo>();
            foreach (var n in WorkFieldNames)
            {
                var f = typeof(WorldSaveData).GetField(n);
                if (f != null) list.Add(f); else Debug.LogWarning("[Net] The work state names a field the save does not have: " + n);
            }
            return s_workFields = list.ToArray();
        }

        string _workSent = "";
        float _nextWork;
        bool _workNow;

        public void WorkChanged() => _workNow = true;

        static string WorkJson(WorldSaveData d)
        {
            var copy = new WorldSaveData();
            foreach (var f in WorkFields()) f.SetValue(copy, f.GetValue(d));
            return JsonUtility.ToJson(copy);
        }

        /// <summary>The host: the work's state to everyone, if it has changed (twice a second at most, or now when asked).</summary>
        void TickWork()
        {
            if (!Host || GameSession.Data == null) return;
            if (!_workNow && Time.unscaledTime < _nextWork) return;
            SendWorkNow();
        }

        public void SendWorkNow()
        {
            var d = GameSession.Data;
            if (!Host || d == null || !_ready) return;
            _nextWork = Time.unscaledTime + 0.5f; _workNow = false;
            var cs = Contracts.Instance;
            bool running = cs && cs.Running;
            string key = (running ? "1" : "0") + WorkJson(d);
            if (key == _workSent) return;
            _workSent = key;
            var w = W(Msg.Work);
            w.Bool(running);
            w.Bytes(NetPack.PackString(key.Substring(1)));
            NetSession.SendWorld(true);
        }

        /// <summary>A joined game: the host's work, taken as it is.</summary>
        void OnWork(NetReader r)
        {
            bool running = r.Bool();
            string json = NetPack.UnpackString(r.Bytes());
            var d = GameSession.Data;
            if (Host || d == null || string.IsNullOrEmpty(json)) return;
            var from = JsonUtility.FromJson<WorldSaveData>(json);
            if (from == null) return;
            int wings = d.wings; bool pool = d.poolrooms;
            foreach (var f in WorkFields()) f.SetValue(d, f.GetValue(from));
            WorkNet.HostRunning = running;
            Shelves.Reloaded();
            if (d.wings != wings || d.poolrooms != pool)
            {
                LeasePlan.Wings = d.wings; LeasePlan.Poolrooms = d.poolrooms;
                LeaseGate.Refresh();
            }
            Contracts.Instance?.Changed();
            Supplies.Instance?.Changed();
            Improvements.Instance?.Changed();
        }

        /// <summary>The host: a joined game asks for something to be done to the work.</summary>
        void OnWorkAsk(int from, NetReader r)
        {
            string cmd = r.Str();
            var d = GameSession.Data;
            if (!Host || d == null || string.IsNullOrEmpty(cmd)) return;
            int sp = cmd.IndexOf(' ');
            string verb = sp < 0 ? cmd : cmd.Substring(0, sp), arg = sp < 0 ? "" : cmd.Substring(sp + 1);
            string who = NetSession.NameOf(from);
            switch (verb)
            {
                case "order": if (Supplies.Instance && Supplies.Instance.OrderByName(arg)) WorkNet.Say($"{who} ORDERED - {arg} - WAITING BY THE LIFT"); break;
                case "improve": if (Improvements.Instance && Improvements.Instance.OrderByName(arg)) WorkNet.Say($"{who} BOUGHT - {arg}"); break;
                case "shelf+":
                    if (ulong.TryParse(arg, out var added) && Shelves.IsShelfId(added) && !d.shelves.Contains(added)) { d.shelves.Add(added); d.kitShelves++; }
                    break;
                case "shelf-":
                    if (ulong.TryParse(arg, out var removed)) { d.shelves.Remove(removed); Shelves.SetLevel(removed, -1f); }
                    break;
                case "light":
                {
                    var a = arg.Split(' ');
                    if (a.Length == 2 && ulong.TryParse(a[0], out var shelf) && d.shelves.Contains(shelf)
                        && float.TryParse(a[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var level))
                        // (A shelf the host's own game is measuring keeps the host's reading.)
                        if (!MovableProp.Find(shelf)) Shelves.SetLevel(shelf, Mathf.Clamp01(level));
                    break;
                }
                case "machine+": if (ulong.TryParse(arg, out var m) && Machines.Is(m)) Machines.Register(m); break;
                case "machine-": if (ulong.TryParse(arg, out var gone) && Machines.Is(gone)) Machines.Unregister(gone); break;
                case "tape": d.tapeWatched = true; break;
            }
            Contracts.Instance?.Changed();
            _workNow = true;
        }

        void OnWorkSay(NetReader r)
        {
            string text = r.Str();
            if (!Host) CamcorderOSD.Title(text);
        }

        /// <summary>A joined game: a moment happened on the host (a batch stored, a batch signed back in).</summary>
        void OnWorkEvent(NetReader r)
        {
            int kind = r.Byte();
            var d = GameSession.Data;
            var cs = Contracts.Instance;
            if (kind == 1)
            {
                int batch = r.Int(), amount = r.Int();
                if (Host || d == null || !cs) return;
                BatchData b = null;
                foreach (var o in d.batches) if (o.id == batch) { b = o; break; }
                if (b != null) ShelfTickets.Stamp(b, "STORED", "+$" + amount);
                cs.PayMoment(d.pay - amount, amount);
            }
            else if (kind == 2)
            {
                int client = r.Int(), amount = r.Int();
                string lines = r.Str();
                if (Host || d == null || !cs) return;
                cs.PayMoment(d.pay - amount, amount);
                if (ReceiptPrinters.Instance) ReceiptPrinters.Instance.Print(client, lines.Split('\n'));
            }
        }
    }
}
