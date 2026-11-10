using System;
using System.Collections.Generic;
using Backrooms.CustomRooms;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Net
{
    /// <summary>The kinds of message (the first byte of each).</summary>
    public enum Msg : byte
    {
        // The session (NetSession)
        Hello = 1, Welcome = 2, Refuse = 3, PeerJoined = 4, PeerLeft = 5, World = 6, Ready = 7, PeerReady = 8, Notice = 9, Ping = 10, Pong = 11,
        Look = 12,                       // a player's suit look changed (StyleLab.PlayerLook, packed)
        // The world (NetWorld); from 20 on, a client's go to the host, which applies them and passes them on.
        Player = 20,
        ItemHeld = 21, ItemMove = 22, ItemRest = 23, ItemGone = 24, LootTaken = 25, ItemAmount = 26, ItemOwner = 27,
        PropMove = 30, PropRest = 31, PropOwner = 32,
        PropGone = 33,                   // a prop taken out of the world by the game itself (a kit put up, a batch collected)
        DoorMove = 40, DoorRest = 41, DoorBolt = 42, Light = 43, Drain = 44, Breaker = 45, Home = 46, Poke = 47,
        Paint = 50, Mark = 51, Strike = 52, Nail = 53, Unnail = 54,
        Inventory = 60, Boombox = 61, Plug = 62,
        Voice = 63,                      // 60 ms of a player's voice (NetWorld.Voice, VoiceCodec)
        // The work (NetWorld.Work): the host's state to everyone; a joined game's requests; notices; moments.
        Work = 70, WorkAsk = 71, WorkSay = 72, WorkEvent = 73,
        Debug = 90,
    }

    /// <summary>A player in the session (this machine's own too).</summary>
    public sealed class Peer
    {
        public int id = -1;
        public string name = "";
        /// <summary>Their suit's look (StyleLab.PlayerLook.Pack; 0: none sent).</summary>
        public int look;
        public int link = -1;            // host: the connection; -1 for the host itself
        public bool welcomed, worldSent, inWorld;
        public float ping;
        public double joinedAt;
        public Color Colour => NetSession.ColourOf(id);
    }

    /// <summary>
    /// Co-op for two to four players (2026-11-04, the owner: "plan and work on the full system for multiplayer ... just
    /// for co-op 2-4 players"). One player hosts one of their saved worlds; the others join by typing the host's address
    /// (their Hamachi 25.x.x.x one, a LAN address, or any VPN's) and the port. The host's world is the world: the joiners
    /// are sent its seed, its generation settings and custom rooms (so they build the same building), and everything
    /// changed in it so far (its save, made there and then); they keep nothing when they leave, but the host keeps what
    /// they were carrying for when they come back (by name).
    ///
    /// This object lives across scenes. It owns the connection (<see cref="NetTransport"/>), the list of players and the
    /// join handshake; <see cref="NetWorld"/> (in the world scene) shares the players and the world itself.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class NetSession : MonoBehaviour
    {
        public const int Protocol = 2;                     // 2 (2026-11-06): the players' looks
        public const int MaxPlayers = 4;

        public static NetSession Instance { get; private set; }
        public static bool Active => Instance && Instance._t != null;
        public static bool IsHost => Active && Instance._t.IsHost;
        public static bool IsClient => Active && !Instance._t.IsHost;
        public static int LocalId { get; private set; }
        public static readonly Dictionary<int, Peer> Peers = new Dictionary<int, Peer>();

        public enum Phase { Off, Connecting, Joining, Loading, Playing, Hosting }
        public static Phase Current { get; private set; }
        /// <summary>What is happening (for the menu while joining).</summary>
        public static string Status { get; private set; } = "";
        /// <summary>Why the last session ended or failed (shown once by the main menu, then cleared).</summary>
        public static string LastError;
        public static string HostAddress { get; private set; } = "";

        // What a client is given for the world (WorldStreamer uses them in place of its own).
        public static WorldGenSettings SharedSettings { get; private set; }
        public static List<RoomBlueprint> SharedRooms { get; private set; }
        /// <summary>The joined world's things (items with their ids, who carries what, this player's own things), for NetWorld.</summary>
        public static NetSnapshot Pending;

        NetTransport _t;
        readonly List<NetEvent> _events = new List<NetEvent>();
        readonly NetWriter _w = new NetWriter();
        readonly NetReader _r = new NetReader();
        readonly Dictionary<int, Peer> _byLink = new Dictionary<int, Peer>();
        readonly List<(int from, byte[] data)> _queue = new List<(int, byte[])>();
        double _nextPing;
        readonly List<(string text, float until, Color colour)> _notes = new List<(string, float, Color)>();

        /// <summary>Applies a world message: returns true if the host should pass it on to the others.</summary>
        public static Func<int, Msg, NetReader, bool> WorldHandler;
        /// <summary>Is the world ready for its messages (until then they wait)?</summary>
        public static Func<bool> WorldReady;
        /// <summary>The host: a player has left (their avatar and what they held go).</summary>
        public static Action<Peer> PeerGone;
        /// <summary>The host: the joining player's copy of the world, made now.</summary>
        public static Func<Peer, byte[]> MakeSnapshot;
        /// <summary>The host: this world's fingerprint (a joiner's must match).</summary>
        public static Func<ulong> Digest;
        /// <summary>A client leaving on purpose: last words (what they carry, for the host to keep).</summary>
        public static Action BeforeLeave;

        public static long BytesIn, BytesOut;

        // ---------------- Settings ----------------

        const string NameKey = "Backrooms.Net.Name", AddressKey = "Backrooms.Net.Address", PortKey = "Backrooms.Net.Port";
        public static string PlayerName
        {
            get
            {
                if (!string.IsNullOrEmpty(s_nameOverride)) return s_nameOverride;
                var n = PlayerPrefs.GetString(NameKey, "");
                if (string.IsNullOrWhiteSpace(n)) { try { n = Environment.UserName; } catch (Exception) { n = ""; } }
                return Clean(string.IsNullOrWhiteSpace(n) ? "PLAYER" : n);
            }
            set { PlayerPrefs.SetString(NameKey, Clean(value)); PlayerPrefs.Save(); }
        }
        static string s_nameOverride;
        public static string LastAddress { get => PlayerPrefs.GetString(AddressKey, ""); set { PlayerPrefs.SetString(AddressKey, value ?? ""); PlayerPrefs.Save(); } }
        public static int Port { get => Mathf.Clamp(PlayerPrefs.GetInt(PortKey, NetTransport.DefaultPort), 1024, 65535); set { PlayerPrefs.SetInt(PortKey, Mathf.Clamp(value, 1024, 65535)); PlayerPrefs.Save(); } }

        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "PLAYER";
            var b = new System.Text.StringBuilder();
            foreach (char c in s.ToUpperInvariant()) if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.') b.Append(c);
            var t = b.ToString().Trim();
            if (t.Length > 16) t = t.Substring(0, 16);
            return t.Length == 0 ? "PLAYER" : t;
        }

        static readonly Color[] s_colours =
        {
            new Color(1f, 0.77f, 0.24f), new Color(0.35f, 0.8f, 1f), new Color(0.6f, 0.92f, 0.35f), new Color(1f, 0.47f, 0.67f),
        };
        public static Color ColourOf(int id) => s_colours[((id % 4) + 4) % 4];

        public static string NameOf(int id) => Peers.TryGetValue(id, out var p) ? p.name : "PLAYER " + (id + 1);
        /// <summary>A player's suit look (StyleLab.PlayerLook.Pack), 0 if none was sent.</summary>
        public static int LookOf(int id) => Peers.TryGetValue(id, out var p) ? p.look : 0;

        // ---------------- Lifetime ----------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Instance = null; LocalId = 0; Peers.Clear(); Current = Phase.Off; Status = ""; LastError = null;
            SharedSettings = null; SharedRooms = null; Pending = null; WorldHandler = null; WorldReady = null; PeerGone = null; MakeSnapshot = null; Digest = null; BeforeLeave = null;
            BytesIn = BytesOut = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance) return;
            var go = new GameObject("NetSession");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<NetSession>();
        }

        void OnDestroy() { if (Instance == this) { Stop(null, false); Instance = null; } StyleLab.PlayerLook.Changed -= SendLook; }

        void Awake() { StyleLab.PlayerLook.Changed += SendLook; }

        /// <summary>Your look changed (YOUR LOOK, in a game): the others see it at once (the host passes a client's on).</summary>
        void SendLook()
        {
            if (_t == null) return;
            int look = StyleLab.PlayerLook.Saved.Pack();
            if (Peers.TryGetValue(LocalId, out var me)) me.look = look;
            if (_t.IsHost)
            {
                Begin(Msg.Look).Int(LocalId); _w.Int(look);
                foreach (var o in Peers.Values) if (o.link >= 0 && o.welcomed) SendLink(o.link, true);
            }
            else if (LocalId > 0) { Begin(Msg.Look).Int(look); SendLink(0, true); }
        }

        /// <summary>Testing (NetDebug): -netjoin ADDRESS:PORT joins straight away (trying again while the host starts up).</summary>
        System.Collections.IEnumerator Start()
        {
            string join = NetDebug.Arg("-netjoin");
            if (string.IsNullOrEmpty(join)) yield break;
            string name = NetDebug.Arg("-netname");
            if (!string.IsNullOrEmpty(name)) s_nameOverride = Clean(name);
            string addr = join; int port = Port;
            int c = join.LastIndexOf(':');
            if (c > 0 && int.TryParse(join.Substring(c + 1), out int p)) { addr = join.Substring(0, c); port = p; }
            for (int attempt = 0; attempt < 40; attempt++)
            {
                yield return new WaitForSecondsRealtime(attempt == 0 ? 3f : 3f);
                if (Active || GameSession.NetClient) yield break;
                if (!Join(addr, port, out var err)) Debug.LogWarning("[Net] " + err);
                while (Active && Current == Phase.Connecting) yield return null;
                if (Active) yield break;
            }
        }
        void OnApplicationQuit() => Stop(IsHost ? "THE HOST QUIT" : null, false);

        // ---------------- Hosting and joining ----------------

        /// <summary>Opens a saved world (or a new one in an empty slot) for others to join. False and an error if the port is taken.</summary>
        public static bool Host(int slot, out string error)
        {
            Stop(null, false);
            var t = NetTransport.Host(Port, out error);
            if (t == null) return false;
            var s = Instance;
            s._t = t;
            LocalId = 0;
            Peers.Clear();
            Peers[0] = new Peer { id = 0, name = PlayerName, look = StyleLab.PlayerLook.Saved.Pack(), welcomed = true, worldSent = true, inWorld = true, joinedAt = Now };
            Current = Phase.Hosting; Status = "HOSTING ON PORT " + Port; LastError = null;
            Debug.Log($"[Net] Hosting world {slot + 1} on port {Port} as {PlayerName}.");
            GameSession.Play(slot);
            return true;
        }

        /// <summary>Starts joining a host (address: an IPv4 address, or a name; port: theirs).</summary>
        public static bool Join(string address, int port, out string error)
        {
            Stop(null, false);
            var t = NetTransport.Connect(address, port, out error);
            if (t == null) return false;
            Instance._t = t;
            HostAddress = address.Trim() + ":" + port;
            Peers.Clear();
            Current = Phase.Connecting; Status = "CONNECTING TO " + HostAddress + " ..."; LastError = null;
            Debug.Log($"[Net] Joining {HostAddress} as {PlayerName}.");
            return true;
        }

        /// <summary>
        /// Ends the session: the host tells everyone why (reason), a client says goodbye. backToMenu: a client in the
        /// joined world goes back to the main menu (its world is not its own).
        /// </summary>
        public static void Stop(string reason, bool backToMenu = true)
        {
            var s = Instance;
            if (!s || s._t == null) { Current = Phase.Off; return; }
            bool wasClient = !s._t.IsHost;
            bool inWorld = wasClient && GameSession.NetClient;
            if (wasClient && string.IsNullOrEmpty(reason)) { try { BeforeLeave?.Invoke(); } catch (Exception e) { Debug.LogException(e); } }
            bool told = false;
            if (s._t.IsHost)
            {
                foreach (var p in Peers.Values) if (p.link >= 0) { s.Begin(Msg.Refuse).Str(reason ?? "THE HOST ENDED THE GAME"); s.SendLink(p.link, true); told = true; }
            }
            s._t.CloseGracefully(told ? 200 : 60);
            s._t = null;
            s._byLink.Clear(); s._queue.Clear();
            Peers.Clear();
            Current = Phase.Off; Status = "";
            SharedSettings = null; SharedRooms = null; Pending = null;
            Debug.Log("[Net] Session ended" + (string.IsNullOrEmpty(reason) ? "." : ": " + reason));
            if (wasClient && !string.IsNullOrEmpty(reason)) LastError = reason;
            if (inWorld && backToMenu) GameSession.ToMenu();
        }

        // ---------------- Sending ----------------

        /// <summary>Starts a message (the shared writer).</summary>
        public NetWriter Begin(Msg kind) { _w.Reset(); _w.Byte((byte)kind); return _w; }
        public static NetWriter Write(Msg kind) => Instance.Begin(kind);

        void SendLink(int link, bool reliable)
        {
            if (_t == null) return;
            var data = _w.ToArray();
            BytesOut += data.Length;
            _t.Send(link, data, reliable);
        }

        /// <summary>A world message to everyone else in the world (a client sends it to the host, which passes it on).</summary>
        public static void SendWorld(bool reliable)
        {
            var s = Instance;
            if (!Active) return;
            if (!s._t.IsHost) { s.SendLink(0, reliable); return; }
            var data = s._w.ToArray();
            foreach (var p in Peers.Values)
                if (p.link >= 0 && p.worldSent) { BytesOut += data.Length; s._t.Send(p.link, data, reliable); }
        }

        /// <summary>A world message to one player (the host only).</summary>
        public static void SendTo(int peer, bool reliable)
        {
            var s = Instance;
            if (!IsHost || !Peers.TryGetValue(peer, out var p) || p.link < 0) return;
            s.SendLink(p.link, reliable);
        }

        static void Relay(int fromPeer, byte[] data, bool reliable)
        {
            var s = Instance;
            foreach (var p in Peers.Values)
                if (p.link >= 0 && p.worldSent && p.id != fromPeer) { BytesOut += data.Length; s._t.Send(p.link, data, reliable); }
        }

        /// <summary>Messages that may be lost (a newer one follows soon): by UDP when it works.</summary>
        public static bool Unreliable(Msg m) => m == Msg.Player || m == Msg.ItemMove || m == Msg.PropMove || m == Msg.DoorMove || m == Msg.Voice;

        static double Now => Time.realtimeSinceStartupAsDouble;

        // ---------------- Each frame ----------------

        void Update()
        {
            if (_t == null) { UpdateNotes(); return; }
            _t.Poll(_events);
            foreach (var e in _events)
            {
                if (_t == null) break;
                switch (e.kind)
                {
                    case NetEvent.Kind.Connected: OnConnected(e.link); break;
                    case NetEvent.Kind.Disconnected: OnDisconnected(e.link, e.reason); break;
                    case NetEvent.Kind.Data:
                        BytesIn += e.data.Length;
                        try { OnData(e.link, e.data); }
                        catch (Exception ex) { Debug.LogWarning($"[Net] A message ({(e.data.Length > 0 ? ((Msg)e.data[0]).ToString() : "empty")}) could not be read: {ex.Message}\n{ex.StackTrace}"); }
                        break;
                }
            }
            if (_t == null) { UpdateNotes(); return; }
            // The host: a joiner waits for the host's world before it is sent its copy.
            if (_t.IsHost && MakeSnapshot != null && WorldReady != null && WorldReady())
                foreach (var p in Peers.Values)
                    if (p.link >= 0 && p.welcomed && !p.worldSent) SendSnapshot(p);
            // The client: the world is up; what came meanwhile is applied in order.
            if (!_t.IsHost && _queue.Count > 0 && WorldReady != null && WorldReady() && WorldHandler != null) DrainQueue();
            if (Now > _nextPing)
            {
                _nextPing = Now + 2.0;
                Begin(Msg.Ping).Double(Now);
                if (_t.IsHost) { foreach (var p in Peers.Values) if (p.link >= 0) SendLink(p.link, true); }
                else if (Current == Phase.Playing || Current == Phase.Loading) SendLink(0, true);
            }
            UpdateNotes();
        }

        void DrainQueue()
        {
            var q = _queue.ToArray();
            _queue.Clear();
            foreach (var (from, data) in q)
            {
                try { _r.Set(data, 1, data.Length - 1); WorldHandler(from, (Msg)data[0], _r); }
                catch (Exception ex) { Debug.LogWarning($"[Net] A queued {(Msg)data[0]} could not be applied: {ex.Message}"); }
            }
        }

        void OnConnected(int link)
        {
            if (_t.IsHost)
            {
                var p = new Peer { link = link, joinedAt = Now };
                _byLink[link] = p;
                return;
            }
            Status = "CONNECTED. ASKING TO JOIN ...";
            Begin(Msg.Hello).Int(Protocol);
            _w.Str(Application.version); _w.Str(PlayerName); _w.Int(StyleLab.PlayerLook.Saved.Pack());
            SendLink(0, true);
        }

        void OnDisconnected(int link, string reason)
        {
            if (!_t.IsHost)
            {
                string why = Current == Phase.Connecting ? reason : (string.IsNullOrEmpty(reason) ? "DISCONNECTED" : reason);
                Debug.Log("[Net] Disconnected: " + why);
                Stop(why);
                return;
            }
            if (!_byLink.TryGetValue(link, out var p)) return;
            _byLink.Remove(link);
            if (p.id < 0) return;
            Peers.Remove(p.id);
            Debug.Log($"[Net] {p.name} left: {reason}");
            Note($"{p.name} LEFT" + (reason == "LEFT" || string.IsNullOrEmpty(reason) ? "" : " (" + reason + ")"), p.Colour);
            PeerGone?.Invoke(p);
            Begin(Msg.PeerLeft).Int(p.id); _w.Str(reason ?? "");
            foreach (var o in Peers.Values) if (o.link >= 0 && o.welcomed) SendLink(o.link, true);
        }

        void OnData(int link, byte[] data)
        {
            var kind = (Msg)data[0];
            _r.Set(data, 1, data.Length - 1);
            if (_t.IsHost) OnHostData(link, kind, data);
            else OnClientData(kind, data);
        }

        // ---------------- The host ----------------

        void OnHostData(int link, Msg kind, byte[] data)
        {
            if (!_byLink.TryGetValue(link, out var p)) return;
            switch (kind)
            {
                case Msg.Hello: HostHello(link, p); return;
                case Msg.Ready:
                {
                    ulong theirs = _r.ULong();
                    ulong ours = Digest != null ? Digest() : 0;
                    p.inWorld = true;
                    Debug.Log($"[Net] {p.name} is in the world (world fingerprint {(theirs == ours ? "matches" : $"DIFFERS: {theirs:X} vs {ours:X}")}).");
                    Note($"{p.name} JOINED", p.Colour);
                    if (theirs != ours && ours != 0)
                    {
                        Note($"{p.name}'S WORLD IS NOT THE SAME AS YOURS: ARE YOU BOTH ON THE SAME VERSION?", ImUi.Bad);
                        Begin(Msg.Notice).Str("YOUR WORLD IS NOT THE SAME AS THE HOST'S: ARE YOU BOTH ON THE SAME VERSION OF THE GAME?"); SendLink(link, true);
                    }
                    Begin(Msg.PeerReady).Int(p.id);
                    foreach (var o in Peers.Values) if (o.link >= 0 && o.welcomed && o.id != p.id) SendLink(o.link, true);
                    return;
                }
                case Msg.Ping: { double t = _r.Double(); Begin(Msg.Pong).Double(t); SendLink(link, true); return; }
                case Msg.Pong: { p.ping = (float)(Now - _r.Double()); return; }
                case Msg.Notice: return;
                case Msg.Look:
                {
                    if (p.id < 0) return;
                    p.look = _r.Int();
                    Begin(Msg.Look).Int(p.id); _w.Int(p.look);
                    foreach (var o in Peers.Values) if (o.link >= 0 && o.welcomed && o.id != p.id) SendLink(o.link, true);
                    return;
                }
            }
            if ((byte)kind < 20 || p.id < 0 || !p.worldSent) return;
            bool relay = WorldHandler == null || WorldHandler(p.id, kind, _r);
            if (relay) Relay(p.id, data, !Unreliable(kind));
        }

        void HostHello(int link, Peer p)
        {
            int protocol = _r.Int();
            string version = _r.Str();
            string name = Clean(_r.Str());
            int look = protocol == Protocol ? _r.Int() : 0;
            string refuse = null;
            if (protocol != Protocol || version != Application.version) refuse = $"THE HOST HAS A DIFFERENT VERSION OF THE GAME ({Application.version}; YOURS: {version})";
            else if (Peers.Count >= MaxPlayers) refuse = $"THE WORLD IS FULL ({MaxPlayers} PLAYERS)";
            else if (!GameSession.InWorldForNet) refuse = "THE HOST IS NOT IN A WORLD";
            if (refuse != null)
            {
                Begin(Msg.Refuse).Str(refuse); SendLink(link, true);
                _t.Close(link); _byLink.Remove(link);
                Debug.Log($"[Net] Refused {name}: {refuse}");
                return;
            }
            // A free id (1-3) and a name nobody else has.
            int id = 1;
            while (Peers.ContainsKey(id)) id++;
            string unique = name; int k = 2;
            while (NameTaken(unique)) unique = name + " " + k++;
            p.id = id; p.name = unique; p.look = look; p.welcomed = true;
            Peers[id] = p;
            Begin(Msg.Welcome).Int(id);
            _w.Int(Peers.Count);
            foreach (var o in Peers.Values) { _w.Int(o.id); _w.Str(o.name); _w.Bool(o.inWorld); _w.Int(o.look); }
            SendLink(link, true);
            Begin(Msg.PeerJoined).Int(id); _w.Str(unique); _w.Int(look);
            foreach (var o in Peers.Values) if (o.link >= 0 && o.id != id && o.welcomed) SendLink(o.link, true);
            Debug.Log($"[Net] {unique} is joining from {_t.Link(link)?.Remote} (player {id + 1}).");
            Note($"{unique} IS JOINING ...", p.Colour);
        }

        static bool NameTaken(string n) { foreach (var o in Peers.Values) if (o.name == n) return true; return false; }

        void SendSnapshot(Peer p)
        {
            byte[] snap;
            try { snap = MakeSnapshot(p); }
            catch (Exception e) { Debug.LogException(e); snap = null; }
            if (snap == null) { Begin(Msg.Refuse).Str("THE HOST COULD NOT SEND THE WORLD"); SendLink(p.link, true); return; }
            _w.Reset(); _w.Byte((byte)Msg.World); _w.Raw(snap, 0, snap.Length);
            SendLink(p.link, true);
            p.worldSent = true;
            Debug.Log($"[Net] Sent the world to {p.name} ({snap.Length / 1024} KB).");
        }

        // ---------------- A client ----------------

        void OnClientData(Msg kind, byte[] data)
        {
            switch (kind)
            {
                case Msg.Refuse: { var why = _r.Str(); Stop(why); return; }
                case Msg.Look: { int id = _r.Int(); int look = _r.Int(); if (Peers.TryGetValue(id, out var lp)) lp.look = look; return; }
                case Msg.Welcome:
                {
                    LocalId = _r.Int();
                    int n = _r.Int();
                    Peers.Clear();
                    for (int i = 0; i < n; i++) { var p = new Peer { id = _r.Int(), name = _r.Str() }; p.inWorld = _r.Bool(); p.look = _r.Int(); p.welcomed = true; Peers[p.id] = p; }
                    Current = Phase.Joining; Status = "JOINING " + NameOf(0) + "'S WORLD. RECEIVING IT ...";
                    return;
                }
                case Msg.World: ClientWorld(data); return;
                case Msg.PeerJoined:
                {
                    var p = new Peer { id = _r.Int(), name = _r.Str(), welcomed = true };
                    p.look = _r.Int();
                    Peers[p.id] = p;
                    Note($"{p.name} IS JOINING ...", p.Colour);
                    return;
                }
                case Msg.PeerReady: { int id = _r.Int(); if (Peers.TryGetValue(id, out var p)) { p.inWorld = true; Note($"{p.name} JOINED", p.Colour); } return; }
                case Msg.PeerLeft:
                {
                    int id = _r.Int(); string why = _r.Str();
                    if (Peers.TryGetValue(id, out var p))
                    {
                        Peers.Remove(id);
                        Note($"{p.name} LEFT", p.Colour);
                        PeerGone?.Invoke(p);
                    }
                    return;
                }
                case Msg.Notice: Note(_r.Str(), ImUi.Bad); return;
                case Msg.Ping: { double t = _r.Double(); Begin(Msg.Pong).Double(t); SendLink(0, true); return; }
                case Msg.Pong: { if (Peers.TryGetValue(LocalId, out var me)) me.ping = (float)(Now - _r.Double()); return; }
            }
            if ((byte)kind < 20) return;
            // The world's messages: in order, once the world is up.
            if (_queue.Count > 0 || WorldReady == null || !WorldReady() || WorldHandler == null) { _queue.Add((0, data)); return; }
            WorldHandler(0, kind, _r);
        }

        void ClientWorld(byte[] data)
        {
            var snap = NetSnapshot.Read(data, 1);
            if (snap == null) { Stop("THE WORLD FROM THE HOST COULD NOT BE READ"); return; }
            SharedSettings = snap.settings;
            SharedRooms = snap.rooms;
            Pending = snap;
            Current = Phase.Loading; Status = "LOADING " + NameOf(0) + "'S WORLD ...";
            Debug.Log($"[Net] Received the world ({data.Length / 1024} KB): seed {snap.save.seed}, {snap.items.Count} things, {snap.rooms.Count} custom rooms.");
            GameSession.PlayNet(snap.save, snap.paint, snap.marks);
        }

        /// <summary>The client's world is up (NetWorld): says so to the host, with this world's fingerprint.</summary>
        public static void ClientReady(ulong digest)
        {
            if (!IsClient) return;
            Current = Phase.Playing; Status = "PLAYING IN " + NameOf(0) + "'S WORLD";
            Instance.Begin(Msg.Ready).ULong(digest);
            Instance.SendLink(0, true);
        }

        // ---------------- Notices (who joined, who left) ----------------

        public static void Note(string text, Color colour)
        {
            if (!Instance) return;
            Instance._notes.Add((text, Time.unscaledTime + 6f, colour));
            if (Instance._notes.Count > 5) Instance._notes.RemoveAt(0);
        }

        void UpdateNotes() { _notes.RemoveAll(n => Time.unscaledTime > n.until); }

        static GUIStyle s_note, s_list;

        void OnGUI()
        {
            bool tab = Active && Keyboard.current != null && Keyboard.current.tabKey.isPressed && GameSession.InWorldForNet;
            if (_notes.Count == 0 && !tab) return;
            ImUi.Begin();
            if (s_note == null)
            {
                s_note = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, font = Backrooms.UI.UiKit.Font(Backrooms.UI.UiKit.Face.Paper, true) };
                s_list = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleLeft, font = Backrooms.UI.UiKit.Font(Backrooms.UI.UiKit.Face.Paper, true) };
            }
            float y = 120f;
            foreach (var (text, until, colour) in _notes)
            {
                float a = Mathf.Clamp01((until - Time.unscaledTime) / 1.2f);
                var r = new Rect(0, y, ImUi.Width, 30);
                var old = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.8f * a);
                GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, s_note);
                GUI.color = new Color(colour.r, colour.g, colour.b, a);
                GUI.Label(r, text, s_note);
                GUI.color = old;
                y += 32f;
            }
            if (tab)
            {
                float w = 460, h = 70 + Peers.Count * 40;
                var box = new Rect(ImUi.Width * 0.5f - w * 0.5f, 260, w, h);
                ImUi.Fill(box, new Color(0.04f, 0.09f, 0.27f, 0.9f));
                GUI.Label(new Rect(box.x + 20, box.y + 12, w - 40, 34), IsHost ? "PLAYERS (YOU ARE HOSTING)" : "PLAYERS", s_list);
                float py = box.y + 52;
                var ids = new List<int>(Peers.Keys); ids.Sort();
                foreach (var id in ids)
                {
                    var p = Peers[id];
                    var old = GUI.color; GUI.color = p.Colour;
                    string state = id == 0 ? "HOST" : !p.inWorld ? "JOINING" : (IsHost ? $"{Mathf.RoundToInt(p.ping * 1000f)} MS" : "");
                    if (id == LocalId) state = (state.Length > 0 ? state + " · " : "") + "YOU" + (IsClient ? $" · {Mathf.RoundToInt(p.ping * 1000f)} MS" : "");
                    GUI.Label(new Rect(box.x + 24, py, w - 48, 34), p.name, s_list);
                    GUI.color = new Color(0.75f, 0.82f, 1f);
                    var right = new GUIStyle(s_list) { alignment = TextAnchor.MiddleRight };
                    GUI.Label(new Rect(box.x + 24, py, w - 48, 34), state, right);
                    GUI.color = old;
                    py += 40;
                }
            }
            ImUi.End();
        }
    }
}
