using System.Collections;
using System.Collections.Generic;
using Backrooms.Generation;
using Backrooms.Interaction;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Net
{
    /// <summary>
    /// Shares the world between the players in a co-op session (2026-11-04). Added to the world scene by the streamer when
    /// a session is on. Each player's own game streams the building around their own player and simulates what they touch
    /// (as Valheim does: whoever is using a thing simulates it, everyone else sees it follow); the host keeps the record of
    /// the world (what it saves) and settles who has what when two players reach for the same thing.
    ///
    /// Twenty times a second each game sends where its player is (and what they hold, where their hand is), what it is
    /// moving (things thrown, furniture pushed, doors swinging); each change that lasts goes as it happens (a thing picked
    /// up or put down, a light, a door left, chalk, paint, a wall broken). See the partial files for each part.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public partial class NetWorld : MonoBehaviour
    {
        public static NetWorld Instance { get; private set; }
        public static bool Ready => Instance && Instance._ready;

        public WorldStreamer streamer;
        Transform _player;
        CamcorderPlayerController _ctrl;
        PropGrabber _hand;
        FirstPersonBody _fpb;
        bool _ready;
        float _nextTick;
        const float Tick = 0.05f;
        readonly Dictionary<int, RemotePlayer> _avatars = new Dictionary<int, RemotePlayer>();
        Vector3 _lastPos; double _lastPosAt;
        Vector3 _vel;
        Transform _avatarRoot;

        static double Now => Time.realtimeSinceStartupAsDouble;
        static int Me => NetSession.LocalId;
        static bool Host => NetSession.IsHost;
        static NetWriter W(Msg m) => NetSession.Write(m);

        public static IEnumerable<RemotePlayer> Avatars { get { if (Instance) foreach (var a in Instance._avatars.Values) if (a) yield return a; } }

        // ---------------- Lifetime ----------------

        void Awake()
        {
            Instance = this;
            if (!streamer) streamer = GetComponent<WorldStreamer>();
            NetSession.WorldHandler = Handle;
            NetSession.WorldReady = () => Ready;
            NetSession.PeerGone = OnPeerGone;
            NetSession.MakeSnapshot = BuildSnapshot;
            NetSession.Digest = WorldDigest;
            NetSession.BeforeLeave = SendInventoryNow;
            WorldSaver.NetGuests = Guests;
            HookBuilding();
            _avatarRoot = new GameObject("OtherPlayers").transform;
            _avatarRoot.SetParent(transform, false);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            StopVoice();
            Instance = null;
            NetSession.WorldHandler = null; NetSession.WorldReady = null; NetSession.PeerGone = null;
            NetSession.MakeSnapshot = null; NetSession.Digest = null; NetSession.BeforeLeave = null;
            WorldSaver.NetGuests = null;
            UnhookBuilding();
        }

        IEnumerator Start()
        {
            _player = streamer ? streamer.player : null;
            if (_player)
            {
                _ctrl = _player.GetComponent<CamcorderPlayerController>();
                _hand = _player.GetComponent<PropGrabber>();
                _fpb = _player.GetComponent<FirstPersonBody>();
            }
            // After what the save put back (or, joining, the host's world as sent).
            var saver = WorldSaver.Instance;
            while (saver && !saver.Restored) yield return null;
            yield return null;
            if (Host)
            {
                LoadGuests();
                TagUntagged(true);
                _ready = true;
                Debug.Log($"[Net] Hosting: the world is ready for others ({_items.Count} things).");
            }
            else
            {
                var snap = NetSession.Pending;
                NetSession.Pending = null;
                if (snap != null) ApplySnapshot(snap);
                yield return null;
                _ready = true;
                NetSession.ClientReady(WorldDigest());
                Debug.Log($"[Net] Joined: the world is up ({_items.Count} things).");
            }
            if (_player) { _lastPos = _player.position; _lastPosAt = Now; }
            _claimAfter = Time.time + 3f;                  // (things settle where they were put first)
        }

        void Update()
        {
            if (!_ready || !NetSession.Active) { if (_mic || _voices.Count > 0) StopVoice(); return; }
            UpdateVoice();
            UpdateRemoteItems();
            UpdateRemoteProps();
            UpdateRemoteDoors();
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + Tick;
            SendPlayer();
            TickItems();
            TickProps();
            TickDoors();
            TickPaint();
            TickInventory();
            TickWork();
        }

        // ---------------- Messages ----------------

        /// <summary>A world message from player `from` (on a client, always the host passing it on). True: the host passes it on.</summary>
        bool Handle(int from, Msg kind, NetReader r)
        {
            if (kind == Msg.Debug) { OnDebug(from, r); return false; }        // (as if the player did it: its changes go out)
            if (kind == Msg.WorkAsk) { OnWorkAsk(from, r); return false; }    // (the host does it: what it makes goes out)
            bool relay = true;
            NetHooks.Apply(() => relay = HandleNow(from, kind, r));
            return relay;
        }

        bool HandleNow(int from, Msg kind, NetReader r)
        {
            switch (kind)
            {
                case Msg.Player: OnPlayer(r); return true;
                case Msg.Voice: OnVoice(r); return true;
                case Msg.ItemHeld: return OnItemHeld(from, r);
                case Msg.ItemMove: OnItemMove(r); return true;
                case Msg.ItemRest: OnItemRest(from, r); return true;
                case Msg.ItemGone: OnItemGone(r); return true;
                case Msg.LootTaken: return OnLootTaken(from, r);
                case Msg.ItemAmount: OnItemAmount(r); return true;
                case Msg.ItemOwner: return OnItemOwner(from, r);
                case Msg.PropMove: OnPropMove(r); return true;
                case Msg.PropRest: OnPropRest(from, r); return true;
                case Msg.PropOwner: return OnPropOwner(from, r);
                case Msg.PropGone: OnPropGone(r); return true;
                case Msg.Work: OnWork(r); return false;
                case Msg.WorkSay: OnWorkSay(r); return false;
                case Msg.WorkEvent: OnWorkEvent(r); return false;
                case Msg.DoorMove: OnDoorMove(r); return true;
                case Msg.DoorRest: OnDoorRest(r); return true;
                case Msg.DoorBolt: OnDoorBolt(r); return true;
                case Msg.Light: OnLight(r); return true;
                case Msg.Drain: OnDrain(r); return true;
                case Msg.Breaker: OnBreaker(r); return true;
                case Msg.Home: OnHome(r); return true;
                case Msg.Poke: OnPoke(r); return true;
                case Msg.Paint: OnPaint(r); return true;
                case Msg.Mark: OnMark(r); return true;
                case Msg.Strike: OnStrike(r); return true;
                case Msg.Nail: OnNail(r); return true;
                case Msg.Unnail: OnUnnail(r); return true;
                case Msg.Inventory: OnInventory(from, r); return false;
                case Msg.Plug: OnPlug(r); return true;
                case Msg.Debug: OnDebug(from, r); return false;
            }
            return false;
        }

        // ---------------- Players ----------------

        void SendPlayer()
        {
            if (!_player || !_ctrl) return;
            double now = Now;
            Vector3 pos = _player.position;
            float dt = (float)(now - _lastPosAt);
            if (dt > 0.001f)
            {
                var v = (pos - _lastPos) / dt;
                if (v.sqrMagnitude > 400f) v = Vector3.zero;                  // a teleport, not a speed
                _vel = v;
            }
            _lastPos = pos; _lastPosAt = now;
            var rot = Quaternion.Euler(0f, _player.eulerAngles.y, 0f);
            var inv = Quaternion.Inverse(rot);

            var w = W(Msg.Player);
            w.Byte((byte)Me);
            w.Double(now);
            w.Vec(pos);
            w.Float(_player.eulerAngles.y);
            w.Float(_ctrl.Pitch);
            w.Float(_ctrl.Eye);
            byte flags = (byte)((int)_ctrl.CurrentStance & 3);
            if (_ctrl.seated) flags |= RemotePlayer.FSeated;
            if (_ctrl.noclip) flags |= RemotePlayer.FNoclip;
            if (_ctrl.IsRunning) flags |= RemotePlayer.FRun;
            if (_ctrl.IsGrounded) flags |= RemotePlayer.FGround;
            if (_ctrl.Sliding) flags |= RemotePlayer.FSlide;
            var held = _hand ? _hand.HeldItem : null;
            if (held && (held.UseBlend > 0.3f || (held is SprayCan sc && sc.Spraying))) flags |= RemotePlayer.FUse;
            w.Byte(flags);
            w.Vec(_vel);
            bool hand = _hand && _hand.HasContact;
            w.Vec(hand ? inv * (_hand.HandPosition - pos) : Vector3.zero);
            w.Byte((byte)(hand ? 255 : 0));
            w.Vec(held ? inv * (held.transform.position - pos) : Vector3.zero);
            w.Rot(held ? inv * held.transform.rotation : Quaternion.identity);
            w.Vec(_ctrl.seated && _fpb ? inv * (_fpb.SeatHip - pos) : Vector3.zero);
            NetSession.SendWorld(false);
        }

        void OnPlayer(NetReader r)
        {
            int id = r.Byte();
            var s = new RemotePlayer.State
            {
                t = r.Double(), pos = r.Vec(), yaw = r.Float(), pitch = r.Float(), eye = r.Float(), flags = r.Byte(), vel = r.Vec(),
                hand = r.Vec(), handBlend = r.Byte() / 255f, itemPos = r.Vec(), itemRot = r.Rot(), seatHip = r.Vec(),
            };
            if (id == Me) return;
            Avatar(id)?.Push(s);
        }

        RemotePlayer Avatar(int id)
        {
            if (id == Me || id < 0) return null;
            if (_avatars.TryGetValue(id, out var a) && a) return a;
            if (!NetSession.Peers.ContainsKey(id)) return null;
            a = RemotePlayer.Create(id, _avatarRoot);
            _avatars[id] = a;
            return a;
        }

        void OnPeerGone(Peer p)
        {
            DropVoice(p.id);
            if (_avatars.TryGetValue(p.id, out var a))
            {
                // What they held or wore goes with them (the host keeps it in their record).
                if (a)
                {
                    if (a.held) DestroyItem(a.held);
                    if (a.worn) DestroyItem(a.worn);
                    Destroy(a.gameObject);
                }
                _avatars.Remove(p.id);
            }
            ForgetOwner(p.id);
            if (Host) GuestLeft(p);
        }

        /// <summary>Anything another player is near (the dark's changes keep away from everyone).</summary>
        public static bool AnyoneNear(Vector3 p, float metres)
        {
            foreach (var a in Avatars)
            {
                var d = a.transform.position - p;
                if (Mathf.Abs(d.y) < 4f && new Vector2(d.x, d.z).sqrMagnitude < metres * metres) return true;
            }
            return false;
        }

        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        /// <summary>Could another player see this point now (in their view, nothing solid between)?</summary>
        public static bool AnyoneSees(Vector3 p, float radius, Transform ignore)
        {
            foreach (var a in Avatars)
            {
                Vector3 eye = a.Eye, to = p - eye;
                float d = to.magnitude;
                if (d > 40f || d < 0.01f) continue;
                if (Vector3.Angle(a.LookDir, to) > 52f) continue;
                int n = Physics.RaycastNonAlloc(eye, to / d, s_hits, Mathf.Max(0f, d - radius), ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                for (int i = 0; i < n; i++)
                {
                    var t = s_hits[i].collider.transform;
                    if ((ignore && t.IsChildOf(ignore)) || t.IsChildOf(a.transform)) continue;
                    if (s_hits[i].collider.attachedRigidbody && s_hits[i].collider.attachedRigidbody.GetComponent<MovableProp>()) continue;
                    blocked = true; break;
                }
                if (!blocked) return true;
            }
            return false;
        }

        // ---------------- The world's fingerprint ----------------

        /// <summary>The layouts of the chunks round the spawn: two games with the same seed, settings and rooms agree.</summary>
        ulong WorldDigest()
        {
            if (!streamer || streamer.World == null) return 0;
            ulong h = 1469598103934665603UL ^ (ulong)(uint)streamer.Seed;
            foreach (var c in new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(0, -1), new Vector2Int(2, 2) })
                unchecked { h = (h ^ LayoutDigest.Of(streamer.World.Chunk(new Vector3Int(c.x, 0, c.y)))) * 1099511628211UL; }
            return h;
        }

        // ---------------- The copy of the world for a joiner (the host) ----------------

        byte[] BuildSnapshot(Peer p)
        {
            var saver = WorldSaver.Instance;
            if (!saver || !saver.Restored || !streamer) return null;
            if (!saver.Collect(out var paint, out var marks)) return null;
            var copy = JsonUtility.FromJson<WorldSaveData>(JsonUtility.ToJson(GameSession.Data));
            copy.items.Clear();
            copy.guests.Clear();
            copy.slot = -1;
            var snap = new NetSnapshot { save = copy, settings = streamer.Settings, paint = paint, marks = marks, hostTime = Now };
            foreach (var b in CustomRooms.RoomLibrary.All) snap.rooms.Add(b);
            TagUntagged(true);
            snap.items = WorldItems();
            // Where they start: where they left (coming back), else beside the host.
            var g = _guests.Find(x => x.name == p.name);
            snap.returning = g != null;
            Vector3 spawn; float yaw;
            if (g != null && g.position != Vector3.zero) { spawn = g.position; yaw = g.yaw; }
            else BesideMe(out spawn, out yaw);
            copy.hasPlayer = true; copy.playerPosition = spawn; copy.playerYaw = yaw; copy.playerPitch = 0f; copy.stance = 0; copy.noclip = false;
            snap.spawn = spawn; snap.spawnYaw = yaw;
            if (g != null) snap.mine = GuestItems(g, p.id);
            snap.mintFrom = MintFrom(p.id);
            return snap.Write();
        }

        /// <summary>A clear place a step or two from the host's player, on the floor (or the host's own spot).</summary>
        void BesideMe(out Vector3 at, out float yaw)
        {
            at = _player ? _player.position : Vector3.zero;
            yaw = _player ? _player.eulerAngles.y : 0f;
            if (!_player) return;
            for (int ring = 0; ring < 3; ring++)
            {
                float r = 1.1f + ring * 0.6f;
                for (int k = 0; k < 8; k++)
                {
                    float a = (yaw + 180f + k * 45f) * Mathf.Deg2Rad;
                    Vector3 c = _player.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
                    if (!Physics.Raycast(c + Vector3.up * 1.2f, Vector3.down, out var hit, 2.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    Vector3 feet = hit.point + Vector3.up * 0.02f;
                    if (Mathf.Abs(feet.y - _player.position.y) > 0.6f) continue;
                    if (Physics.CheckCapsule(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.6f, 0.32f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    // Nothing solid between (not through a wall).
                    if (Physics.Linecast(_player.position + Vector3.up * 1f, feet + Vector3.up * 1f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    at = feet;
                    yaw = Quaternion.LookRotation(_player.position - feet, Vector3.up).eulerAngles.y;
                    return;
                }
            }
        }

        void ApplySnapshot(NetSnapshot snap)
        {
            _mintNext = snap.mintFrom;
            SpawnSnapshotItems(snap.items);
            if (snap.mine.Count > 0) TakeMine(snap.mine);
            Debug.Log($"[Net] Applied the host's world: {snap.items.Count} things, {snap.mine.Count} of mine{(snap.returning ? " (back again)" : "")}.");
        }

        // ---------------- Name tags ----------------

        static GUIStyle s_tag;

        void OnGUI()
        {
            if (!_ready || _avatars.Count == 0) return;
            var cam = Camera.main;
            if (!cam) return;
            if (s_tag == null) s_tag = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            foreach (var a in _avatars.Values)
            {
                if (!a || !a.HasState) continue;
                Vector3 head = a.transform.position + Vector3.up * (Mathf.Max(0.6f, a.Current.eye) + 0.32f);
                Vector3 to = head - cam.transform.position;
                float d = to.magnitude;
                if (d > 16f || Vector3.Dot(to, cam.transform.forward) <= 0f) continue;
                // Not through walls.
                if (Physics.Linecast(cam.transform.position, head, out var hit, ~0, QueryTriggerInteraction.Ignore) && !hit.collider.transform.IsChildOf(a.transform)) continue;
                var sp = cam.WorldToScreenPoint(head);
                float alpha = Mathf.Clamp01((16f - d) / 6f) * 0.85f;
                var rect = new Rect(sp.x - 120, Screen.height - sp.y - 12, 240, 24);
                var c = NetSession.ColourOf(a.Peer);
                var old = GUI.color;
                GUI.color = new Color(0, 0, 0, alpha * 0.8f);
                GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), NetSession.NameOf(a.Peer), s_tag);
                GUI.color = new Color(c.r, c.g, c.b, alpha);
                GUI.Label(rect, NetSession.NameOf(a.Peer), s_tag);
                GUI.color = old;
            }
        }
    }
}
