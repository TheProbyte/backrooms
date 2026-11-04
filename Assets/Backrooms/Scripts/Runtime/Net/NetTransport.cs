using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace Backrooms.Net
{
    /// <summary>What the transport saw this frame.</summary>
    public struct NetEvent
    {
        public enum Kind { Connected, Data, Disconnected }
        public Kind kind;
        public int link;
        public byte[] data;
        public string reason;
    }

    /// <summary>One connection: its TCP stream (everything that must arrive, in order) and, once both ends have heard each
    /// other, its UDP address (the frequent updates that may be lost: where a player is, what is moving).</summary>
    public sealed class NetLink
    {
        public int Id;
        public Socket Tcp;
        public IPEndPoint Remote;
        public IPEndPoint Udp;
        public uint Token;
        /// <summary>The other end has said it gets our datagrams: unreliable messages go by UDP (else by TCP).</summary>
        public bool UdpOut;
        public bool Connecting, Closed;
        public double LastHeard, LastSent, LastHello;
        public bool AckedTheirUdp;
        public long BytesIn, BytesOut;
        internal byte[] In = new byte[64 * 1024];
        internal int InStart, InEnd;
        internal byte[] Out = new byte[64 * 1024];
        internal int OutStart, OutEnd;
        public int Queued => OutEnd - OutStart;
    }

    /// <summary>
    /// The co-op connection layer (2026-11-04): plain sockets, polled once a frame on the main thread (no threads, no
    /// packages). The host listens on one port for TCP and UDP (7777 by default: Hamachi, a LAN or a forwarded port); a
    /// client connects to its address. Messages are framed on TCP by their length; on UDP each datagram carries the
    /// connection's token (sent by the host on TCP when it accepts). If UDP does not get through either way (a firewall),
    /// the frequent updates go by TCP instead. Silence for <see cref="Timeout"/> seconds closes a connection.
    /// The way Minecraft and Terraria (TCP for everything) and most co-op games (UDP for movement) are built, kept small.
    /// </summary>
    public sealed class NetTransport : IDisposable
    {
        public const int DefaultPort = 7777;
        public const int MaxFrame = 64 << 20;
        public const int MaxDatagram = 1200;
        public const double Timeout = 15.0;
        const byte Internal = 0, IHello = 1, IAck = 2, IToken = 3, IKeep = 4;

        public bool IsHost { get; private set; }
        public int Port { get; private set; }
        public string Error { get; private set; }
        public int LinkCount => _links.Count;

        Socket _listen, _udp;
        IPEndPoint _hostUdp;                  // client: where the host's datagrams come from
        readonly Dictionary<int, NetLink> _links = new Dictionary<int, NetLink>();
        readonly List<NetEvent> _events = new List<NetEvent>();
        readonly byte[] _dgram = new byte[2048];
        readonly System.Random _rng = new System.Random();
        int _nextId = 1;

        static double Now => Time.realtimeSinceStartupAsDouble;

        public IEnumerable<NetLink> Links => _links.Values;
        public NetLink Link(int id) => _links.TryGetValue(id, out var l) ? l : null;

        // ---------------- Starting ----------------

        /// <summary>Listens on the port (TCP and UDP). Null and an error if the port is taken.</summary>
        public static NetTransport Host(int port, out string error)
        {
            var t = new NetTransport { IsHost = true, Port = port };
            try
            {
                t._listen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { Blocking = false, NoDelay = true };
                t._listen.Bind(new IPEndPoint(IPAddress.Any, port));
                t._listen.Listen(8);
                t._udp = NewUdp(port);
            }
            catch (Exception e)
            {
                t.Dispose();
                error = e is SocketException se && se.SocketErrorCode == SocketError.AddressAlreadyInUse ? $"PORT {port} IS ALREADY IN USE" : "COULD NOT OPEN PORT " + port + ": " + e.Message;
                return null;
            }
            error = null;
            return t;
        }

        /// <summary>Starts connecting to a host (an IPv4 address or a name). Null and an error if the address is no good.</summary>
        public static NetTransport Connect(string address, int port, out string error)
        {
            error = null;
            IPAddress ip = null;
            if (!IPAddress.TryParse(address.Trim(), out ip))
            {
                try { foreach (var a in Dns.GetHostAddresses(address.Trim())) if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; } }
                catch (Exception) { ip = null; }
            }
            if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) { error = "NOT AN ADDRESS: " + address; return null; }
            var t = new NetTransport { IsHost = false, Port = port };
            try
            {
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { Blocking = false, NoDelay = true };
                var ep = new IPEndPoint(ip, port);
                var link = new NetLink { Id = 0, Tcp = s, Remote = ep, Connecting = true, LastHeard = Now };
                try { s.Connect(ep); link.Connecting = false; }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.WouldBlock || e.SocketErrorCode == SocketError.InProgress || e.SocketErrorCode == SocketError.AlreadyInProgress) { }
                t._links[0] = link;
                t._udp = NewUdp(0);
                t._hostUdp = new IPEndPoint(ip, port);
                if (!link.Connecting) t._events.Add(new NetEvent { kind = NetEvent.Kind.Connected, link = 0 });
            }
            catch (Exception e) { t.Dispose(); error = "COULD NOT CONNECT: " + e.Message; return null; }
            return t;
        }

        static Socket NewUdp(int port)
        {
            var u = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            // Windows reports an earlier datagram's "port unreachable" on the next receive; that must not stop the socket.
            try { u.IOControl(unchecked((IOControlCode)(-1744830452)), new byte[] { 0, 0, 0, 0 }, null); } catch (Exception) { }
            u.Bind(new IPEndPoint(IPAddress.Any, port));
            return u;
        }

        // ---------------- Each frame ----------------

        /// <summary>Accepts, reads and writes; what happened goes into the list.</summary>
        public void Poll(List<NetEvent> into)
        {
            into.Clear();
            into.AddRange(_events); _events.Clear();
            double now = Now;
            if (IsHost) Accept(into, now);
            ReadUdp(into, now);
            foreach (var link in _links.Values)
            {
                if (link.Closed) continue;
                if (link.Connecting)
                {
                    if (link.Tcp.Poll(0, SelectMode.SelectError)) { Drop(link, "COULD NOT CONNECT (NO ANSWER AT " + link.Remote + ")", into); continue; }
                    if (link.Tcp.Poll(0, SelectMode.SelectWrite)) { link.Connecting = false; link.LastHeard = now; into.Add(new NetEvent { kind = NetEvent.Kind.Connected, link = link.Id }); }
                    else if (now - link.LastHeard > 10.0) { Drop(link, "COULD NOT CONNECT (TIMED OUT)", into); continue; }
                    else continue;
                }
                ReadTcp(link, into, now);
                if (link.Closed) continue;
                // Keep the line open; say hello by UDP until the host hears it.
                if (now - link.LastSent > 1.0) SendInternal(link, IKeep);
                if (!IsHost && link.Token != 0 && !link.UdpOut && now - link.LastHello > 0.4) { link.LastHello = now; SendDatagram(link, new[] { Internal, IHello }); }
                Flush(link, into);
                if (!link.Closed && now - link.LastHeard > Timeout) Drop(link, "CONNECTION LOST (NOTHING HEARD FOR " + (int)Timeout + " S)", into);
            }
            List<int> gone = null;
            foreach (var l in _links.Values) if (l.Closed) (gone ??= new List<int>()).Add(l.Id);
            if (gone != null) foreach (var id in gone) _links.Remove(id);
        }

        void Accept(List<NetEvent> into, double now)
        {
            while (_listen != null && _listen.Poll(0, SelectMode.SelectRead))
            {
                Socket s;
                try { s = _listen.Accept(); } catch (SocketException) { break; }
                s.Blocking = false; s.NoDelay = true;
                uint token = (uint)_rng.Next(1, int.MaxValue) ^ ((uint)_rng.Next() << 1);
                if (token == 0) token = 1;
                var link = new NetLink { Id = _nextId++, Tcp = s, Remote = s.RemoteEndPoint as IPEndPoint, Token = token, LastHeard = now };
                _links[link.Id] = link;
                var w = new byte[6]; w[0] = Internal; w[1] = IToken; BitConverter.GetBytes(token).CopyTo(w, 2);
                Frame(link, w);
                into.Add(new NetEvent { kind = NetEvent.Kind.Connected, link = link.Id });
            }
        }

        void ReadTcp(NetLink link, List<NetEvent> into, double now)
        {
            string closedWhy = null;
            while (true)
            {
                if (link.InEnd == link.In.Length)
                {
                    if (link.InStart > 0) { Buffer.BlockCopy(link.In, link.InStart, link.In, 0, link.InEnd - link.InStart); link.InEnd -= link.InStart; link.InStart = 0; }
                    else Array.Resize(ref link.In, link.In.Length * 2);
                }
                int n;
                SocketError err;
                try { n = link.Tcp.Receive(link.In, link.InEnd, link.In.Length - link.InEnd, SocketFlags.None, out err); }
                catch (Exception e) { Drop(link, "CONNECTION LOST (" + e.Message + ")", into); return; }
                if (err == SocketError.WouldBlock) break;
                if (err == SocketError.ConnectionReset || err == SocketError.ConnectionAborted) { closedWhy = IsHost ? "LEFT" : "THE HOST CLOSED THE CONNECTION"; break; }
                if (err != SocketError.Success) { closedWhy = "CONNECTION LOST (" + err + ")"; break; }
                if (n == 0) { closedWhy = IsHost ? "LEFT" : "THE HOST CLOSED THE CONNECTION"; break; }
                link.InEnd += n; link.BytesIn += n; link.LastHeard = now;
            }
            // (What arrived before the connection closed is read first: a goodbye says why.)
            // Whole frames out.
            while (link.InEnd - link.InStart >= 4)
            {
                int len = BitConverter.ToInt32(link.In, link.InStart);
                if (len < 1 || len > MaxFrame) { Drop(link, "BAD DATA FROM THE OTHER GAME", into); return; }
                if (link.InEnd - link.InStart - 4 < len)
                {
                    if (4 + len > link.In.Length) { var bigger = new byte[Mathf.NextPowerOfTwo(4 + len)]; Buffer.BlockCopy(link.In, link.InStart, bigger, 0, link.InEnd - link.InStart); link.InEnd -= link.InStart; link.InStart = 0; link.In = bigger; }
                    break;
                }
                var data = new byte[len];
                Buffer.BlockCopy(link.In, link.InStart + 4, data, 0, len);
                link.InStart += 4 + len;
                if (data[0] == Internal) Internal_(link, data, false);
                else into.Add(new NetEvent { kind = NetEvent.Kind.Data, link = link.Id, data = data });
            }
            if (link.InStart == link.InEnd) link.InStart = link.InEnd = 0;
            if (closedWhy != null) Drop(link, closedWhy, into);
        }

        void ReadUdp(List<NetEvent> into, double now)
        {
            if (_udp == null) return;
            int guard = 0;
            while (guard++ < 512)
            {
                int avail;
                try { avail = _udp.Available; } catch (Exception) { return; }
                if (avail <= 0) return;
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int n;
                try { n = _udp.ReceiveFrom(_dgram, 0, _dgram.Length, SocketFlags.None, ref from); }
                catch (SocketException) { continue; }
                HandleDatagram(n, from as IPEndPoint, into, now);
            }
        }

        void HandleDatagram(int n, IPEndPoint from, List<NetEvent> into, double now)
        {
            if (n < 6 || from == null) return;
            uint token = BitConverter.ToUInt32(_dgram, 0);
            NetLink link = null;
            if (IsHost)
            {
                foreach (var l in _links.Values) if (l.Token == token && !l.Closed) { link = l; break; }
                if (link == null || link.Remote == null || !link.Remote.Address.Equals(from.Address)) return;
                link.Udp = from;
            }
            else
            {
                if (!_links.TryGetValue(0, out link) || link.Token != token || _hostUdp == null || !_hostUdp.Address.Equals(from.Address)) return;
            }
            link.LastHeard = now; link.BytesIn += n;
            if (_dgram[4] == Internal) { Internal_(link, _dgram, true, 4); return; }
            var data = new byte[n - 4];
            Buffer.BlockCopy(_dgram, 4, data, 0, n - 4);
            into.Add(new NetEvent { kind = NetEvent.Kind.Data, link = link.Id, data = data });
        }

        void Internal_(NetLink link, byte[] d, bool udp, int at = 0)
        {
            if (d.Length < at + 2) return;
            switch (d[at + 1])
            {
                case IToken: if (!IsHost && d.Length >= at + 6) link.Token = BitConverter.ToUInt32(d, at + 2); break;
                case IHello:
                    // They hear us by UDP only once they say so; we say so once we hear them.
                    if (!link.AckedTheirUdp) { link.AckedTheirUdp = true; SendInternal(link, IAck); }
                    if (IsHost) SendDatagram(link, new[] { Internal, IHello });
                    break;
                case IAck: link.UdpOut = true; break;
                case IKeep: break;
            }
        }

        // ---------------- Sending ----------------

        /// <summary>Sends a message: reliable ones by TCP (in order, never lost); the others by UDP once it works.</summary>
        public void Send(int linkId, byte[] data, bool reliable) => Send(linkId, data, 0, data.Length, reliable);

        public void Send(int linkId, byte[] data, int offset, int count, bool reliable)
        {
            if (!_links.TryGetValue(linkId, out var link) || link.Closed || link.Connecting) return;
            if (!reliable && link.UdpOut && count + 4 <= MaxDatagram && (IsHost ? link.Udp != null : _hostUdp != null))
            {
                SendDatagram(link, data, offset, count);
                return;
            }
            Frame(link, data, offset, count);
        }

        void SendInternal(NetLink link, byte kind) => Frame(link, new[] { Internal, kind });

        void SendDatagram(NetLink link, byte[] data) => SendDatagram(link, data, 0, data.Length);

        void SendDatagram(NetLink link, byte[] data, int offset, int count)
        {
            var to = IsHost ? link.Udp : _hostUdp;
            if (to == null || _udp == null || link.Token == 0) return;
            var d = new byte[count + 4];
            BitConverter.GetBytes(link.Token).CopyTo(d, 0);
            Buffer.BlockCopy(data, offset, d, 4, count);
            try { _udp.SendTo(d, to); link.BytesOut += d.Length; }
            catch (SocketException) { }
        }

        void Frame(NetLink link, byte[] data) => Frame(link, data, 0, data.Length);

        void Frame(NetLink link, byte[] data, int offset, int count)
        {
            int need = count + 4;
            if (link.Out.Length - link.OutEnd < need)
            {
                int used = link.OutEnd - link.OutStart;
                if (link.OutStart > 0) { Buffer.BlockCopy(link.Out, link.OutStart, link.Out, 0, used); link.OutStart = 0; link.OutEnd = used; }
                if (link.Out.Length - link.OutEnd < need) { var bigger = new byte[Mathf.NextPowerOfTwo(used + need + 1024)]; Buffer.BlockCopy(link.Out, 0, bigger, 0, used); link.Out = bigger; }
            }
            BitConverter.GetBytes(count).CopyTo(link.Out, link.OutEnd);
            Buffer.BlockCopy(data, offset, link.Out, link.OutEnd + 4, count);
            link.OutEnd += need;
            link.LastSent = Now;
        }

        void Flush(NetLink link, List<NetEvent> into)
        {
            while (link.OutEnd > link.OutStart)
            {
                int n;
                SocketError err;
                try { n = link.Tcp.Send(link.Out, link.OutStart, link.OutEnd - link.OutStart, SocketFlags.None, out err); }
                catch (Exception e) { Drop(link, "CONNECTION LOST (" + e.Message + ")", into); return; }
                if (err == SocketError.WouldBlock) return;
                if (err != SocketError.Success) { Drop(link, "CONNECTION LOST (" + err + ")", into); return; }
                if (n <= 0) return;
                link.OutStart += n; link.BytesOut += n;
            }
            link.OutStart = link.OutEnd = 0;
            // Very big buffers (the world sent on joining) shrink back once sent.
            if (link.Out.Length > (1 << 20)) link.Out = new byte[64 * 1024];
        }

        /// <summary>Sends what is waiting now (before closing a connection on purpose).</summary>
        public void FlushAll()
        {
            var none = new List<NetEvent>();
            foreach (var l in _links.Values) if (!l.Closed && !l.Connecting) Flush(l, none);
        }

        // ---------------- Closing ----------------

        void Drop(NetLink link, string reason, List<NetEvent> into)
        {
            if (link.Closed) return;
            link.Closed = true;
            try { link.Tcp.Close(); } catch (Exception) { }
            into.Add(new NetEvent { kind = NetEvent.Kind.Disconnected, link = link.Id, reason = reason });
        }

        /// <summary>Closes one connection (what is queued is sent first, as far as it goes at once).</summary>
        public void Close(int linkId)
        {
            if (!_links.TryGetValue(linkId, out var link) || link.Closed) return;
            var none = new List<NetEvent>();
            if (!link.Connecting) Flush(link, none);
            try { link.Tcp.Shutdown(SocketShutdown.Both); } catch (Exception) { }
            try { link.Tcp.Close(); } catch (Exception) { }
            link.Closed = true;
            _links.Remove(linkId);
        }

        /// <summary>
        /// Ends every connection as TCP likes it: what is queued is sent, the sending side shut (the other end reads all of it
        /// and then the end), what came in meanwhile is read and dropped (Windows resets a connection closed with unread data,
        /// and a reset can lose the goodbye), then closed. Waits at most `ms` for it.
        /// </summary>
        public void CloseGracefully(int ms)
        {
            var junk = new byte[8192];
            var none = new List<NetEvent>();
            foreach (var l in _links.Values) if (!l.Closed && !l.Connecting) Flush(l, none);
            var until = Now + ms / 1000.0;
            bool shut = false;
            while (Now < until)
            {
                bool pending = false;
                foreach (var l in _links.Values)
                {
                    if (l.Closed || l.Connecting) continue;
                    if (l.OutEnd > l.OutStart) { Flush(l, none); pending = true; }
                    try { while (l.Tcp.Available > 0 && l.Tcp.Receive(junk, 0, junk.Length, SocketFlags.None, out var e) > 0 && e == SocketError.Success) { } } catch (Exception) { }
                }
                if (!pending && !shut)
                {
                    foreach (var l in _links.Values) if (!l.Closed && !l.Connecting) { try { l.Tcp.Shutdown(SocketShutdown.Send); } catch (Exception) { } }
                    shut = true;
                }
                if (shut && !pending) { System.Threading.Thread.Sleep(15); if (Now > until - ms / 2000.0) break; }
                else System.Threading.Thread.Sleep(5);
            }
            foreach (var l in _links.Values) { try { while (l.Tcp.Available > 0) l.Tcp.Receive(junk); } catch (Exception) { } }
            Dispose();
        }

        public void Dispose()
        {
            foreach (var l in _links.Values) { try { l.Tcp?.Close(); } catch (Exception) { } }
            _links.Clear();
            try { _listen?.Close(); } catch (Exception) { }
            try { _udp?.Close(); } catch (Exception) { }
            _listen = null; _udp = null;
        }

        // ---------------- This machine's addresses ----------------

        /// <summary>This machine's IPv4 addresses, the Hamachi one (25.x.x.x) first, then the others (LAN, other VPNs).</summary>
        public static List<(string address, string what)> LocalAddresses()
        {
            var list = new List<(string, string)>();
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string a = ua.Address.ToString();
                        if (a.StartsWith("169.254.")) continue;
                        string name = (ni.Description ?? "") + " " + (ni.Name ?? "");
                        string what = a.StartsWith("25.") || name.IndexOf("hamachi", StringComparison.OrdinalIgnoreCase) >= 0 ? "HAMACHI"
                                    : name.IndexOf("radmin", StringComparison.OrdinalIgnoreCase) >= 0 ? "RADMIN VPN"
                                    : name.IndexOf("zerotier", StringComparison.OrdinalIgnoreCase) >= 0 ? "ZEROTIER"
                                    : name.IndexOf("tailscale", StringComparison.OrdinalIgnoreCase) >= 0 || a.StartsWith("100.") ? "TAILSCALE"
                                    : "LAN";
                        list.Add((a, what));
                    }
                }
            }
            catch (Exception) { }
            list.Sort((x, y) => Rank(x.Item2).CompareTo(Rank(y.Item2)));
            return list;
        }

        static int Rank(string what) => what == "HAMACHI" ? 0 : what == "LAN" ? 2 : 1;
    }
}
