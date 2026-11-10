using System.Collections;
using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.CustomRooms;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Getting paid is a moment (2026-11-09, Docs/ImprovementPlan.md F1). A payment was a line of text and a beep at the
    /// hub's board, which nobody on the floor could hear. Now, the instant a payment lands:
    /// <list type="bullet">
    /// <item>the camcorder's IN HAND figure counts up, a tick a step and a till's bell at the end (longer and brighter for
    ///   a bigger payment: a count-up makes the amount seen, as a score tallying does);</item>
    /// <item>stored: the batch's shelf tickets are stamped STORED with a thunk (ShelfTickets);</item>
    /// <item>returned: a receipt prints from the small printer beside the OVERFLOW PICKUP sign, and tears off.</item>
    /// </list>
    /// Nothing floats in the air: it all happens on things, or on the camcorder's own display.
    /// </summary>
    public partial class Contracts
    {
        AudioSource _till;
        Coroutine _count;

        AudioSource Till
        {
            get
            {
                if (_till) return _till;
                var go = new GameObject("CamcorderTill");
                go.transform.SetParent(transform, false);
                _till = go.AddComponent<AudioSource>();
                _till.spatialBlend = 0f; _till.playOnAwake = false; _till.volume = 0.5f;
                return _till;
            }
        }

        /// <summary>Money in: the figure counts up from what you had. (The money is already yours: this is the show.)</summary>
        internal void PayMoment(int before, int amount)
        {
            if (amount <= 0) return;
            CamcorderOSD.Money(before, before + amount, CountTime(amount));
            if (_count != null) StopCoroutine(_count);
            _count = StartCoroutine(Count(amount));
        }

        static float CountTime(int amount) => Mathf.Clamp(0.5f + amount * 0.012f, 0.6f, 1.6f);

        IEnumerator Count(int amount)
        {
            int ticks = Mathf.Clamp(amount / 5, 4, 16);
            float step = CountTime(amount) / ticks;
            for (int i = 0; i < ticks; i++)
            {
                Till.PlayOneShot(Tone(900f + i * 55f, 900f + i * 55f, 0.03f), 0.35f);
                yield return new WaitForSecondsRealtime(step);
            }
            Till.PlayOneShot(Bell(), Mathf.Clamp(0.35f + amount * 0.004f, 0.4f, 0.8f));
            _count = null;
        }

        static AudioClip s_bell;

        /// <summary>A till's bell: one struck note with its overtones, ringing out over most of a second.</summary>
        static AudioClip Bell()
        {
            if (s_bell) return s_bell;
            int sr = 44100, n = sr * 9 / 10;
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float v = Mathf.Sin(2f * Mathf.PI * 1568f * t) * Mathf.Exp(-t * 5f) * 0.5f
                        + Mathf.Sin(2f * Mathf.PI * 3136f * t) * Mathf.Exp(-t * 8f) * 0.22f
                        + Mathf.Sin(2f * Mathf.PI * 4290f * t) * Mathf.Exp(-t * 14f) * 0.14f;
                d[i] = v * Mathf.Clamp01(t * 900f) * 0.6f;
            }
            s_bell = AudioClip.Create("TillBell", n, 1, sr, false);
            s_bell.SetData(d, 0);
            return s_bell;
        }
    }

    /// <summary>
    /// The receipt printers (one beside each client's OVERFLOW PICKUP sign, on their north wall): a small thermal printer on
    /// the company's bracket. When a batch is signed back in, its receipt prints (the client, RETURNED or SETTLED, the
    /// pay) and hangs there until the next one. Made while someone is in the clients' rooms, dropped when nobody is.
    /// </summary>
    public class ReceiptPrinters : MonoBehaviour
    {
        const int PW = 64, PH = 96;
        const float PaperW = 0.105f, PaperH = PaperW * PH / PW;
        static readonly Color32 Paper = new Color32(244, 240, 228, 255), Ink = new Color32(40, 34, 30, 255), Red = new Color32(176, 52, 32, 255);

        class Printer
        {
            public GameObject root;
            public Transform paper;
            public Mesh mesh;
            public int step = -1;
            public Texture2D tex;
            public float printAt = -100f;
            public bool torn = true;
        }

        readonly Printer[] _printers = new Printer[4];
        readonly string[][] _pending = new string[4][];
        static Material s_body, s_trim;
        static AudioClip s_print, s_tear;
        float _next;

        public static ReceiptPrinters Instance { get; private set; }
        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; for (int k = 0; k < 4; k++) Drop(k); }

        /// <summary>The printer's place in a client's room: on the north wall between the pickup sign and the shop's sign.</summary>
        public static Vector3 Place(int kind) =>
            SeamPlan.World(kind, 2.75f, SeamRooms.D * OfficeBiome.CellSize - OfficeBiome.WallThickness * 0.5f - 0.065f, 1.2f);

        /// <summary>A receipt for this client: printed now if their room is there, else when someone next walks in.</summary>
        public void Print(int kind, params string[] lines)
        {
            if (kind < 0 || kind > 3) return;
            _pending[kind] = lines;
            Ensure(kind);
            if (_printers[kind] != null) Run(kind);
        }

        void Update()
        {
            for (int k = 0; k < 4; k++)
            {
                var p = _printers[k];
                if (p == null) continue;
                float t = Time.time - p.printAt;
                // The paper feeds out over 1.3 s (in steps, as a thermal head prints a line at a time), then tears.
                float k01 = Mathf.Clamp01(t / 1.3f);
                int step = Mathf.FloorToInt(k01 * 16f);
                if (p.paper && step != p.step)
                {
                    // (Only the part fed out so far: what was printed first hangs lowest, the newest line is at the slot.)
                    p.step = step;
                    float fed = step / 16f;
                    p.paper.localScale = new Vector3(PaperW, Mathf.Max(0.0001f, PaperH * fed), 1f);
                    p.mesh.uv = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f - fed), new Vector2(1f, 1f - fed) };
                    p.paper.GetComponent<MeshRenderer>().enabled = step > 0;
                }
                if (!p.torn && t > 1.45f)
                {
                    p.torn = true;
                    Audio.WorldSound.PlayAt(Tear(), p.root.transform.position, 0.5f, 10f, Random.Range(0.95f, 1.05f));
                }
            }
            if (Time.time < _next) return;
            _next = Time.time + 1f;
            var cam = Camera.main;
            bool near = cam && SeamPlan.InZone(cam.transform.position);
            for (int k = 0; k < 4; k++)
            {
                if (near) { Ensure(k); if (_pending[k] != null && _printers[k] != null && SeamPlan.KindAt(cam.transform.position) == k) Run(k); }
                else Drop(k);
            }
        }

        void Run(int kind)
        {
            var p = _printers[kind];
            var lines = _pending[kind];
            if (p == null || lines == null) return;
            _pending[kind] = null;
            var px = new Color32[PW * PH];
            for (int i = 0; i < px.Length; i++) px[i] = Paper;
            PixelFont.Fill(px, PW, PH, 0, 0, PW, 12, new Color32(240, 162, 30, 255));
            PixelFont.DrawCentred(px, PW, PH, "OVERFLOW", PW / 2, 3, 1, Ink);
            int y = 17;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) { y += 5; continue; }
                bool big = line.StartsWith("+$") || line.StartsWith("$");
                string text = line;
                int dot = big && PixelFont.Width(text, 2) <= PW - 4 ? 2 : 1;
                while (text.Length > 2 && PixelFont.Width(text, dot) > PW - 4) text = text.Substring(0, text.Length - 1);
                PixelFont.DrawCentred(px, PW, PH, text, PW / 2, y, dot, line.Contains("LATE") || line.Contains("LOST") ? Red : Ink);
                y += dot == 2 ? 18 : 10;
                if (y > PH - 16) break;
            }
            p.tex.SetPixels32(px);
            p.tex.Apply(false);
            p.printAt = Time.time;
            p.torn = false;
            p.step = -1;
            Audio.WorldSound.PlayAt(PrintSound(), p.root.transform.position, 0.6f, 12f, 1f);
        }

        void Ensure(int kind)
        {
            if (_printers[kind] != null) return;
            Materials();
            var at = Place(kind);
            var root = new GameObject("ReceiptPrinter_" + kind);
            root.transform.SetParent(transform, false);
            root.transform.SetPositionAndRotation(at, Quaternion.identity);      // (on the north wall: its face to -Z, into the room)
            Block(root.transform, "Bracket", new Vector3(0f, -0.02f, 0.045f), new Vector3(0.2f, 0.24f, 0.02f), s_trim);
            Block(root.transform, "Body", Vector3.zero, new Vector3(0.17f, 0.13f, 0.11f), s_body);
            Block(root.transform, "Lid", new Vector3(0f, 0.07f, -0.005f), new Vector3(0.17f, 0.014f, 0.1f), s_trim);
            Block(root.transform, "Slot", new Vector3(0f, -0.066f, -0.035f), new Vector3(0.115f, 0.006f, 0.012f), s_trim);
            // The paper: a strip hanging from the slot (its top edge there), seen from the room (-Z).
            var q = new GameObject("Receipt", typeof(MeshFilter), typeof(MeshRenderer));
            q.transform.SetParent(root.transform, false);
            q.transform.localPosition = new Vector3(0f, -0.07f, -0.036f);
            var mesh = new Mesh { name = "Receipt" };
            mesh.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, -1f, 0f), new Vector3(0.5f, -1f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.MarkDynamic();
            q.GetComponent<MeshFilter>().sharedMesh = mesh;
            var tex = new Texture2D(PW, PH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Receipt" };
            var sh = Shader.Find("Backrooms/TvScreen") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(sh) { name = "Receipt" };
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", new Color(0.82f, 0.8f, 0.76f));
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.renderingLayerMask = uint.MaxValue; r.enabled = false;
            _printers[kind] = new Printer { root = root, paper = q.transform, tex = tex, mesh = mesh };
        }

        void Drop(int kind)
        {
            var p = _printers[kind];
            if (p == null) return;
            if (p.tex) Destroy(p.tex);
            if (p.mesh) Destroy(p.mesh);
            if (p.root) Destroy(p.root);
            _printers[kind] = null;
        }

        static void Materials()
        {
            if (s_body) return;
            var sh = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            Material M(string n, Color c, float smooth)
            {
                var m = new Material(sh) { name = n, hideFlags = HideFlags.DontSave };
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
                return m;
            }
            s_body = M("Printer_Cream", new Color(0.9f, 0.86f, 0.74f), 0.4f);
            s_trim = M("Printer_Amber", new Color(0.94f, 0.64f, 0.12f), 0.35f);
        }

        static void Block(Transform parent, string name, Vector3 at, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at; go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m; r.renderingLayerMask = uint.MaxValue; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>A thermal printer: a motor's chunk, then the head buzzing a line at a time.</summary>
        static AudioClip PrintSound()
        {
            if (s_print) return s_print;
            int sr = 44100, n = sr * 14 / 10;
            var d = new float[n];
            var rng = new System.Random(5);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                float chunk = t < 0.08f ? (Mathf.Sin(2f * Mathf.PI * 140f * t) * 0.6f + noise * 0.3f) * Mathf.Exp(-t * 40f) : 0f;
                // Sixteen lines: each a short buzz and a step.
                float line = (t - 0.06f) * 16f / 1.3f, f = line - Mathf.Floor(line);
                float buzz = t > 0.06f && t < 1.36f ? Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 620f * t)) * (f < 0.62f ? 0.16f : 0f) + noise * (f < 0.62f ? 0.05f : 0.0f)
                                                       + (f > 0.62f && f < 0.7f ? noise * 0.22f : 0f) : 0f;
                d[i] = (chunk + buzz) * 0.7f;
            }
            s_print = AudioClip.Create("ReceiptPrint", n, 1, sr, false);
            s_print.SetData(d, 0);
            return s_print;
        }

        /// <summary>Paper torn against the cutter: a short bright rip.</summary>
        static AudioClip Tear()
        {
            if (s_tear) return s_tear;
            int sr = 44100, n = sr * 22 / 100;
            var d = new float[n];
            var rng = new System.Random(9);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (noise - lp) * 0.35f;
                d[i] = (noise - lp) * Mathf.Clamp01(t * 60f) * Mathf.Exp(-t * 16f) * 0.7f * (0.6f + 0.4f * Mathf.Sin(t * 260f));
            }
            s_tear = AudioClip.Create("ReceiptTear", n, 1, sr, false);
            s_tear.SetData(d, 0);
            return s_tear;
        }
    }
}
