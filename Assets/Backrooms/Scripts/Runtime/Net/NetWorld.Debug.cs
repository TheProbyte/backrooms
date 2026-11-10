using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.Net
{
    /// <summary>
    /// For testing co-op with two copies of the game on one machine (2026-11-04): a copy started with -netdebug does what
    /// the host tells it (walk, look, pick up, drop, sit, press, take a picture) and answers. Nothing happens without that
    /// flag on the joining copy's command line. -netjoin ADDRESS:PORT and -netname NAME join a host straight away.
    /// </summary>
    public static class NetDebug
    {
        static bool? s_on;
        public static bool Enabled => s_on ??= Has("-netdebug");
        public static readonly List<string> Replies = new List<string>();

        public static bool Has(string flag)
        {
            foreach (var a in System.Environment.GetCommandLineArgs()) if (string.Equals(a, flag, System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string Arg(string flag)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (string.Equals(args[i], flag, System.StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        /// <summary>The host: a command to a player's game (it must have been started with -netdebug).</summary>
        public static void Send(int peer, string command)
        {
            if (!NetSession.IsHost) return;
            NetSession.Write(Msg.Debug).Str(command);
            NetSession.SendTo(peer, true);
        }
    }

    public partial class NetWorld
    {
        void OnDebug(int from, NetReader r)
        {
            string cmd = r.Str();
            if (Host) { NetDebug.Replies.Add(cmd); Debug.Log($"[NetDebug] {NetSession.NameOf(from)}: {cmd}"); return; }
            if (!NetDebug.Enabled) return;
            string reply;
            try { reply = RunDebug(cmd); }
            catch (System.Exception e) { reply = "error: " + e.Message; }
            Debug.Log($"[NetDebug] {cmd} -> {reply}");
            NetSession.Write(Msg.Debug).Str(cmd.Split(' ')[0] + ": " + reply);
            NetSession.SendWorld(true);
        }

        static float F(string[] a, int i, float d = 0f) => i < a.Length && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;
        static string V(Vector3 v) => $"({v.x:0.00}, {v.y:0.00}, {v.z:0.00})";

        /// <summary>The work as this game has it (for comparing two games' logs).</summary>
        public static string WorkReport()
        {
            var d = Saves.GameSession.Data;
            if (d == null) return "no world";
            var cs = Contracts.Instance;
            var sb = new System.Text.StringBuilder($"pay {d.pay} earned {d.earned} in {d.batchesIn} done {d.batchesDone} shelves {d.shelves.Count} delivered {d.delivered} running {(cs && cs.Running)}");
            foreach (var b in d.batches) sb.Append($" [b{b.id} c{b.client} s{b.state} n{b.boxes.Count}]");
            return sb.ToString();
        }

        string RunDebug(string cmd)
        {
            var a = cmd.Split(' ');
            var cam = Camera.main;
            switch (a[0])
            {
                case "pos":
                {
                    string held = _hand && _hand.HeldItem ? _hand.HeldItem.displayName + "#" + (TagOf(_hand.HeldItem) ? TagOf(_hand.HeldItem).id : 0) : "-";
                    string pockets = "";
                    if (_hand) for (int i = 0; i < PropGrabber.PocketCount; i++) pockets += (_hand.Pocket(i) ? _hand.Pocket(i).displayName : "-") + (i < 3 ? "," : "");
                    return $"{V(_player.position)} yaw {_player.eulerAngles.y:0} pitch {_ctrl.Pitch:0} eye {_ctrl.Eye:0.00} held {held} pockets {pockets} prop {(_hand && _hand.Held ? _hand.Held.Spec.kind.ToString() : "-")} seated {_ctrl.seated} items {_items.Count} avatars {_avatars.Count}";
                }
                case "tp": _ctrl.TeleportSafe(new Vector3(F(a, 1), F(a, 2), F(a, 3)), F(a, 4, _player.eulerAngles.y), 0f); return V(_player.position);
                case "walk": StartCoroutine(Walk(new Vector2(F(a, 1), F(a, 2)), F(a, 3, 1f))); return "walking";
                case "look":
                    _ctrl.SetYaw(F(a, 1, _player.eulerAngles.y));
                    _ctrl.AddLook(0f, F(a, 2) - _ctrl.Pitch);
                    return $"yaw {_player.eulerAngles.y:0} pitch {_ctrl.Pitch:0}";
                case "lookat":
                {
                    Vector3 p = new Vector3(F(a, 1), F(a, 2), F(a, 3));
                    Vector3 eye = cam ? cam.transform.position : _player.position + Vector3.up * 1.5f;
                    Vector3 d = p - eye;
                    float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    float pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                    _ctrl.SetYaw(yaw); _ctrl.AddLook(0f, pitch - _ctrl.Pitch);
                    return $"yaw {yaw:0} pitch {_ctrl.Pitch:0}";
                }
                case "items":
                {
                    var s = new System.Text.StringBuilder();
                    foreach (var ni in _items.Values)
                    {
                        if (!ni || !ni.item) continue;
                        float d = Vector3.Distance(ni.item.transform.position, _player.position);
                        if (d > F(a, 1, 15f)) continue;
                        s.Append($"{ni.id}:{ni.item.itemId}@{V(ni.item.transform.position)} {ni.mode}{(ni.holder >= 0 ? "/" + ni.holder : "")}{(ni.item.NetRemote ? " remote" : "")}{(ni.item.NetForeign ? " foreign" : "")}; ");
                    }
                    return s.Length == 0 ? "none near" : s.ToString();
                }
                case "pick":
                {
                    WorldItem best = null;
                    if (a.Length > 1 && uint.TryParse(a[1], out uint id)) best = Find(id)?.item;
                    else
                    {
                        float bd = 3f;
                        foreach (var it in WorldItem.Live)
                        {
                            if (!it || it.Held || it.NetForeign || it is Plug) continue;
                            float d = Vector3.Distance(it.transform.position, _player.position);
                            if (d < bd) { bd = d; best = it; }
                        }
                    }
                    if (!best) return "nothing near";
                    _hand.PickUp(best);
                    return "picking up " + best.displayName;
                }
                case "drop": _hand.Drop(); return "dropped";
                case "prop":
                {
                    if (a.Length < 2 || !ulong.TryParse(a[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong pid)) return "prop HEXID";
                    var mp = MovableProp.Find(pid);
                    return mp ? $"{mp.Spec.kind} at {V(mp.transform.position)} yaw {mp.transform.eulerAngles.y:0} remote {mp.NetRemote} kinematic {mp.Body.isKinematic}" : "not here";
                }
                case "door":
                {
                    var d = Door.Find(a.Length > 1 ? a[1] : "");
                    return d ? $"angle {d.Angle:0.0} latched {d.Latched} bolted {d.Bolted}" : "not here";
                }
                case "light":
                {
                    var c = new Vector3Int((int)F(a, 1), (int)F(a, 2), (int)F(a, 3));
                    return "off " + Generation.LightZones.IsOff(c, (int)F(a, 4));
                }
                case "paint": { var sp = SprayPaint.Existing; return sp ? "canvases " + sp.Canvases : "none"; }
                case "throw":
                {
                    var it = _hand.HeldItem;
                    if (!it) return "nothing held";
                    _hand.Drop();
                    if (it.Body && !it.Body.isKinematic && cam) it.Body.linearVelocity = cam.transform.forward * F(a, 1, 6f) + Vector3.up * 1.5f;
                    return "thrown " + it.displayName;
                }
                case "pocket": _hand.botPocket = (int)F(a, 1); return "pocket key " + a[1];
                case "hold": _hand.botHold = true; _hand.botMouse = new Vector2(F(a, 1), F(a, 2)); return "holding";
                case "unhold": _hand.botHold = false; StartCoroutine(Unbot()); return "let go";
                case "use": StartCoroutine(Use(F(a, 1, 1f))); return "using";
                case "e": StartCoroutine(PressE()); return "E";
                case "sit": { var s = _player.GetComponent<Seating>(); if (s) s.botSit = true; return "sit"; }
                case "press":
                {
                    if (!cam || !Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 3f, ~0, QueryTriggerInteraction.Collide)) return "nothing in view";
                    var t = hit.collider.GetComponentInParent<IPokeTarget>();
                    if (t == null) return "not a button: " + hit.collider.name;
                    if (NetHooks.On) NetHooks.Poke?.Invoke(t);
                    t.Press();
                    return "pressed " + t.GetType().Name;
                }
                case "shot":
                {
                    string path = a.Length > 1 ? cmd.Substring(5) : Application.persistentDataPath + "/netshot.png";
                    ScreenCapture.CaptureScreenshot(path);
                    return "picture to " + path;
                }
                case "work": return WorkReport();
                case "ask": WorkNet.Ask(cmd.Substring(4)); return "asked";
                case "worklot":
                {
                    // worklot store|return <client>: as if this player carried the client's lot onto the first shelf, or back
                    // to the client's pallet (the boxes put there as props this game moved).
                    var d = Saves.GameSession.Data; var mgr = MovablePropManager.Instance;
                    if (d == null || !mgr || a.Length < 3) return "no world";
                    int client = (int)F(a, 2);
                    bool store = a[1] == "store";
                    Saves.BatchData b = null;
                    foreach (var o in d.batches) if (o.client == client && (store ? o.state <= 1 : o.state == 3)) b = o;
                    if (b == null) return "no such batch";
                    int i = 0;
                    if (store)
                    {
                        if (d.shelves.Count == 0 || !Shelves.Pose(d.shelves[0], out var at, out var rot, out int v)) return "no shelf";
                        var t = ShelfTypes.Get(v);
                        _ctrl.TeleportSafe(at + rot * new Vector3(0f, 0.06f, 1.6f), rot.eulerAngles.y + 180f, 0f);
                        foreach (var id in b.boxes)
                        {
                            int level = Mathf.Min(i / 2, t.levels - 1);
                            var p = at + rot * new Vector3(i % 2 == 0 ? -0.27f : 0.27f, ShelfTypes.DeckTop(t, level) + 0.01f, 0f);
                            var spec = Contracts.BoxSpec(id, p, rot.eulerAngles.y);
                            if (spec != null) mgr.Place(spec, p, rot);
                            i++;
                        }
                        return "stored " + i + " boxes on shelf " + d.shelves[0];
                    }
                    foreach (var id in b.boxes)
                    {
                        var p = Generation.SeamPlan.Stack(client, i, Contracts.BoxSize(id));
                        var spec = Contracts.BoxSpec(id, p, 180f);
                        if (spec != null) mgr.Place(spec, p, Quaternion.Euler(0f, 180f, 0f));
                        i++;
                    }
                    return "returned " + i + " boxes";
                }
                case "leave": NetSession.Stop(null); return "leaving";
                case "quit": Application.Quit(); return "quitting";
            }
            return "unknown command";
        }

        IEnumerator Walk(Vector2 input, float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until && _ctrl) { _ctrl.botInput = input; yield return null; }
            if (_ctrl) _ctrl.botInput = null;
        }

        IEnumerator Unbot() { yield return null; yield return null; if (_hand) _hand.botHold = null; }

        IEnumerator Use(float seconds)
        {
            _hand.botHold = true;
            yield return new WaitForSeconds(seconds);
            _hand.botHold = false;
            yield return null; yield return null;
            _hand.botHold = null;
        }

        IEnumerator PressE()
        {
            _hand.botE = true;
            yield return new WaitForSeconds(0.15f);
            _hand.botE = false;
            yield return null;
            _hand.botE = null;
        }
    }
}
