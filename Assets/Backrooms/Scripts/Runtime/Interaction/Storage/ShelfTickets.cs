using System.Collections.Generic;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Shelf tickets (2026-11-09, Docs/ImprovementPlan.md F2: "the state on the shelf"). What a shelf holds, whether it is
    /// lit enough and when its boxes are due used to be only in the J list and on the hub's board. Now every standing
    /// shelf carries a ticket holder on its front (the same pixel display as the seam's card) and one small lamp:
    /// <list type="bullet">
    /// <item><b>The lamp</b>, read from across a room: green, its boxes are safe (dim green: empty and lit, ready);
    ///   amber, the shelf is not lit enough; red and blinking, boxes on it are due back (fast when late).</item>
    /// <item><b>The ticket</b>, read up close: the client's colour and name, how many of the batch's boxes are on this
    ///   shelf, and STORED 2:10 / TAKE BACK 1:40 / LATE / COMING IN.</item>
    /// <item><b>The stamp</b> (F1): the moment a batch is stored its tickets are stamped STORED with a thunk.</item>
    /// </list>
    /// As Hardspace: Shipbreaker's team put it for their fully diegetic interface (GDC 2021): the state lives on the thing.
    /// </summary>
    public static class ShelfTickets
    {
        public struct Info
        {
            public int batch, client, onShelf, count, state, more;
            public float time;
            public bool late;
        }

        static readonly Dictionary<ulong, Info> s_info = new Dictionary<ulong, Info>();
        static readonly Dictionary<ulong, (float until, string top, string bottom)> s_stamp = new Dictionary<ulong, (float, string, string)>();
        static readonly Dictionary<(ulong, int), int> s_tally = new Dictionary<(ulong, int), int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_info.Clear(); s_stamp.Clear(); s_tally.Clear(); }

        public static bool Get(ulong shelf, out Info info) => s_info.TryGetValue(shelf, out info);

        public static bool Stamped(ulong shelf, out string top, out string bottom)
        {
            top = bottom = null;
            if (!s_stamp.TryGetValue(shelf, out var s) || Time.time > s.until) return false;
            top = s.top; bottom = s.bottom;
            return true;
        }

        static float Rank(BatchData b) => b.state == 3 ? -1000f + (b.window - b.since) : b.state == 2 ? b.left : 100000f;

        /// <summary>Which batch each shelf holds (its most urgent one), twice a second (ShelfWatch).</summary>
        public static void Refresh()
        {
            s_info.Clear();
            s_tally.Clear();
            var d = GameSession.Data;
            var mgr = MovablePropManager.Instance;
            if (d == null || !mgr) return;
            foreach (var b in d.batches)
            {
                if (b.state < 1) continue;
                foreach (var id in b.boxes)
                {
                    if (!mgr.Where(id, out var p, out bool held) || held || !Shelves.On(p, out var shelf)) continue;
                    s_tally.TryGetValue((shelf, b.id), out int n);
                    s_tally[(shelf, b.id)] = n + 1;
                }
            }
            foreach (var kv in s_tally)
            {
                ulong shelf = kv.Key.Item1;
                BatchData b = null;
                foreach (var o in d.batches) if (o.id == kv.Key.Item2) { b = o; break; }
                if (b == null) continue;
                var mine = new Info
                {
                    batch = b.id, client = b.client, onShelf = kv.Value, count = b.boxes.Count, state = b.state,
                    time = b.state == 2 ? b.left : b.state == 3 ? b.window - b.since : 0f, late = b.state == 3 && b.since > b.window,
                };
                if (s_info.TryGetValue(shelf, out var have))
                {
                    BatchData hb = null;
                    foreach (var o in d.batches) if (o.id == have.batch) { hb = o; break; }
                    if (hb != null && Rank(hb) <= Rank(b)) { have.more++; s_info[shelf] = have; continue; }
                    mine.more = have.more + 1;
                }
                s_info[shelf] = mine;
            }
        }

        /// <summary>A batch just stored (or settled on the shelf): its shelves' tickets are stamped, with one thunk.</summary>
        public static void Stamp(BatchData b, string top, string bottom)
        {
            var mgr = MovablePropManager.Instance;
            if (b == null || !mgr) return;
            ulong loudest = 0; int most = 0;
            var counts = new Dictionary<ulong, int>();
            foreach (var id in b.boxes)
                if (mgr.Where(id, out var p, out bool held) && !held && Shelves.On(p, out var shelf))
                {
                    counts.TryGetValue(shelf, out int n);
                    counts[shelf] = ++n;
                    if (n > most) { most = n; loudest = shelf; }
                }
            foreach (var kv in counts) s_stamp[kv.Key] = (Time.time + 3.5f, top, bottom);
            if (loudest != 0 && Shelves.Pose(loudest, out var at, out _, out _))
                Audio.WorldSound.PlayAt(Thunk(), at + Vector3.up * 1.3f, 0.7f, 14f, Random.Range(0.96f, 1.04f));
        }

        static AudioClip s_thunk;

        /// <summary>A rubber stamp on a card in a steel holder: the handle's click, a low thud, the holder ringing a little.</summary>
        public static AudioClip Thunk()
        {
            if (s_thunk) return s_thunk;
            int sr = 44100, n = sr * 45 / 100;
            var data = new float[n];
            var rng = new System.Random(11);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                float thud = Mathf.Sin(2f * Mathf.PI * (95f - 60f * t) * t) * Mathf.Exp(-t * 26f) * 0.75f;
                float slap = noise * Mathf.Exp(-t * 90f) * 0.45f;
                float ring = Mathf.Sin(2f * Mathf.PI * 1480f * t) * Mathf.Exp(-t * 18f) * 0.07f;
                data[i] = (thud + slap + ring) * Mathf.Clamp01(t * 2000f);
            }
            s_thunk = AudioClip.Create("TicketStamp", n, 1, sr, false);
            s_thunk.SetData(data, 0);
            return s_thunk;
        }
    }

    /// <summary>One shelf's ticket holder and lamp (added to the shelf by <see cref="ShelfWatch"/> while it is near).</summary>
    public class ShelfTicket : MonoBehaviour
    {
        const int W = 80, H = 54;
        static readonly Color32 Paper = new Color32(236, 226, 200, 255), Ink = new Color32(52, 38, 28, 255), Band = new Color32(240, 162, 30, 255),
            Holder = new Color32(58, 56, 54, 255), Red = new Color32(176, 52, 32, 255), Green = new Color32(42, 112, 44, 255), Cream = new Color32(250, 246, 236, 255);
        static readonly Color LampGreen = new Color(0.25f, 1.6f, 0.35f), LampReady = new Color(0.06f, 0.3f, 0.09f), LampAmber = new Color(1.9f, 1.0f, 0.1f),
            LampRed = new Color(2.2f, 0.16f, 0.1f), LampOff = new Color(0.05f, 0.045f, 0.04f);

        MovableProp _shelf;
        Texture2D _tex;
        Color32[] _px;
        Material _lampMat;
        string _last = "";
        float _next;
        int _lampState = -1;
        bool _blinkOn = true;

        public static void Ensure(MovableProp shelf)
        {
            if (!shelf || ShelfTypes.IsBroken(shelf.Spec.variant) || shelf.GetComponentInChildren<ShelfTicket>()) return;
            var t = ShelfTypes.Get(shelf.Spec.variant);
            // Hung under the front edge of the deck nearest chest height, at the right-hand end.
            int level = 0; float best = float.MaxValue;
            for (int l = 0; l < t.levels; l++) { float e = Mathf.Abs(ShelfTypes.DeckTop(t, l) - 1.4f); if (e < best) { best = e; level = l; } }
            float top = ShelfTypes.DeckTop(t, level) - 0.035f;
            const float w = 0.24f, h = w * H / W;
            var root = new GameObject("ShelfTicket");
            root.transform.SetParent(shelf.transform, false);
            root.transform.localPosition = new Vector3(-(t.size.x * 0.5f - 0.05f - w * 0.5f), top - h * 0.5f, t.size.z * 0.5f + 0.016f);
            var sh = Shader.Find("Backrooms/TvScreen") ?? Shader.Find("Universal Render Pipeline/Unlit");

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Ticket";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(root.transform, false);
            q.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            q.transform.localScale = new Vector3(w, h, 1f);
            var ticket = root.AddComponent<ShelfTicket>();
            ticket._shelf = shelf;
            ticket._tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "ShelfTicket" };
            ticket._px = new Color32[W * H];
            var mat = new Material(sh) { name = "ShelfTicket" };
            mat.SetTexture("_BaseMap", ticket._tex);
            mat.SetColor("_BaseColor", new Color(0.8f, 0.78f, 0.74f));
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.renderingLayerMask = uint.MaxValue;

            // The lamp: a small lens above the holder's corner, bright enough to pick out from across a room.
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lamp.name = "Lamp";
            Destroy(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.localPosition = new Vector3(-(w * 0.5f - 0.03f), h * 0.5f + 0.012f, 0.012f);
            lamp.transform.localScale = new Vector3(0.05f, 0.034f, 0.034f);
            ticket._lampMat = new Material(sh) { name = "ShelfLamp" };
            ticket._lampMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            var lr = lamp.GetComponent<MeshRenderer>();
            lr.sharedMaterial = ticket._lampMat; lr.shadowCastingMode = ShadowCastingMode.Off; lr.renderingLayerMask = uint.MaxValue;
            ticket.Tick(true);
        }

        void Update()
        {
            // The blink (red: due back; faster when late) runs every frame; the rest twice a second.
            if (_lampState >= 3)
            {
                bool on = GameSettings.Steady || (Time.time * (_lampState == 4 ? 3.2f : 1.4f)) % 1f < 0.55f;   // (FLASHING off: steady red)
                if (on != _blinkOn) { _blinkOn = on; _lampMat.SetColor("_BaseColor", on ? LampRed : LampOff); }
            }
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            Tick(false);
        }

        void Tick(bool force)
        {
            if (!_shelf) { Destroy(gameObject); return; }
            ulong id = _shelf.Spec.id;
            bool lit = Shelves.Level(id) >= Shelves.Safe;
            bool has = ShelfTickets.Get(id, out var info);
            // 0 ready (empty, lit), 1 safe, 2 low light, 3 due back, 4 late.
            int lamp = has && info.state == 3 ? (info.late ? 4 : 3) : !lit ? 2 : has ? 1 : 0;
            if (lamp != _lampState)
            {
                _lampState = lamp; _blinkOn = true;
                _lampMat.SetColor("_BaseColor", lamp == 0 ? LampReady : lamp == 1 ? LampGreen : lamp == 2 ? LampAmber : LampRed);
            }
            var cam = Camera.main;
            if (!force && (!cam || (cam.transform.position - transform.position).sqrMagnitude > 9f * 9f)) return;
            bool stamped = ShelfTickets.Stamped(id, out string stampTop, out string stampBottom);
            string key = has ? $"{info.batch}|{info.onShelf}|{info.count}|{info.state}|{Mathf.CeilToInt(info.time)}|{info.late}|{info.more}|{lit}" : "empty|" + lit;
            if (stamped) key += "|S" + stampTop + stampBottom;
            if (!force && key == _last) return;
            _last = key;
            Draw(has, info, lit, stamped, stampTop, stampBottom);
        }

        void Draw(bool has, ShelfTickets.Info info, bool lit, bool stamped, string stampTop, string stampBottom)
        {
            for (int i = 0; i < _px.Length; i++) _px[i] = Paper;
            // The holder's steel lip round the card.
            PixelFont.Fill(_px, W, H, 0, 0, W, 2, Holder); PixelFont.Fill(_px, W, H, 0, H - 2, W, H, Holder);
            PixelFont.Fill(_px, W, H, 0, 0, 2, H, Holder); PixelFont.Fill(_px, W, H, W - 2, 0, W, H, Holder);
            if (!has)
            {
                PixelFont.Fill(_px, W, H, 2, 2, W - 2, 13, Band);
                PixelFont.DrawCentred(_px, W, H, "OVERFLOW", W / 2, 4, 1, Ink);
                PixelFont.DrawCentred(_px, W, H, "EMPTY", W / 2, 19, 2, Ink);
                PixelFont.DrawCentred(_px, W, H, lit ? "LIT - READY" : "LOW LIGHT", W / 2, 40, 1, lit ? Green : Red);
            }
            else
            {
                var tape = (Color32)Contracts.ClientColours[Mathf.Clamp(info.client, 0, 3)];
                PixelFont.Fill(_px, W, H, 2, 2, W - 2, 13, tape);
                string name = Contracts.Clients[Mathf.Clamp(info.client, 0, 3)];
                PixelFont.DrawCentred(_px, W, H, name, W / 2, 4, 1, Cream);
                string big, word; Color32 ink = Ink;
                switch (info.state)
                {
                    case 1: big = info.onShelf + "/" + info.count; word = "COMING IN"; break;
                    case 2: big = Contracts.MinSec(info.time); word = "STORED"; ink = Green; break;
                    default: big = info.late ? "LATE" : Contracts.MinSec(info.time); word = "TAKE BACK"; ink = Red; break;
                }
                PixelFont.DrawCentred(_px, W, H, big, W / 2, 17, 2, ink);
                string foot = info.state == 1 ? word : $"{word} {info.onShelf}/{info.count}";
                if (!lit) foot = "LOW LIGHT";
                else if (info.more > 0 && PixelFont.Width(foot + " +" + info.more, 1) <= W - 6) foot += " +" + info.more;
                PixelFont.DrawCentred(_px, W, H, foot, W / 2, 40, 1, !lit ? Red : Ink);
            }
            if (stamped)
            {
                // The stamp: a red box across the card, the word in it.
                PixelFont.Fill(_px, W, H, 6, 15, W - 6, 49, Red);
                PixelFont.Fill(_px, W, H, 8, 17, W - 8, 47, Paper);
                PixelFont.DrawCentred(_px, W, H, stampTop ?? "", W / 2, 19, 2, Red);
                PixelFont.DrawCentred(_px, W, H, stampBottom ?? "", W / 2, 36, 1, Red);
            }
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        void OnDestroy() { if (_tex) Destroy(_tex); if (_lampMat) Destroy(_lampMat); }
    }
}
