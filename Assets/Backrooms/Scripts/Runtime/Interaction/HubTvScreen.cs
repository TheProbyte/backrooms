using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The training room's TV (HubTvCart, 2026-11-07): left on with no tape in, so it shows snow: static redrawn about
    /// fifteen times a second with a slow rolling band, a cold blue glow on whoever sits in front of it, and a faint hiss.
    /// <para>The orientation tape (2026-11-08, a stand-in until the real cut is in): sit down in the training room and a
    /// short tape of title cards plays (about 20 s: the job in a few lines), then goes back to snow. Watching it to the end
    /// is half of the first work order (WorkOrders: watch the tape, clock in). Getting up stops it.</para>
    /// </summary>
    public class HubTvScreen : MonoBehaviour
    {
        const int W = 96, H = 72;
        Texture2D _tex;
        Color32[] _px;
        Material _mat;
        Light _light;
        AudioSource _hiss;
        System.Random _rng = new System.Random(7);
        float _next, _roll;
        // The tape.
        const int TW = 160, TH = 120;
        const float CardTime = 2.3f;
        Texture2D _tapeTex;
        Color32[] _tapePx;
        bool _playing, _wasSeated;
        float _tapeT;
        Seating _seating;

        /// <summary>The stand-in orientation tape: a card a line or a few (no more than 26 letters a line).</summary>
        static readonly string[][] s_cards =
        {
            new[] { "OVERFLOW", "ORIENTATION", "TAPE 1 OF 1" },
            new[] { "THE WORLD IS FULL.", "BEHIND IT THERE IS MORE.", "WE RENT IT OUT." },
            new[] { "1. TAKE A WORK ORDER", "AT THE BOARD." },
            new[] { "2. RIDE THE LIFT DOWN.", "OFFICE HOURS: 9 TO 5." },
            new[] { "3. BOXES GO ON SHELVES.", "OFF A SHELF, NO PAY." },
            new[] { "4. KEEP YOUR SHELVES LIT.", "THE DARK TAKES THE REST." },
            new[] { "WE CHARGE THE CLIENT", "EVERY DAY.", "THE POWER BILL", "IS YOURS." },
            new[] { "BE BACK BY CLOSING.", "THERE'S ALWAYS MORE ROOM." },
            new[] { "END OF TAPE" },
        };

        /// <summary>The cart's foot in the hub (OverflowRooms puts it there, its front to the south).</summary>
        static Vector3 Foot => HubPlan.World(5.6f, 21.6f - OfficeBiome.WallThickness * 0.5f - 0.4f);

        void Start()
        {
            // The screen's middle in the cart's frame (the model's: 6 cm to one side, 1.255 m up, 0.29 m out at the front).
            var centre = Foot + new Vector3(-0.06f, 1.255f, -0.292f);
            _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "TvSnow" };
            _px = new Color32[W * H];
            // Backrooms/TvScreen is in Resources, so builds have it (URP's Unlit is stripped from them).
            var shader = Shader.Find("Backrooms/TvScreen");
            if (!shader) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) { enabled = false; return; }
            _mat = new Material(shader) { name = "TvSnow" };
            _mat.SetTexture("_BaseMap", _tex);
            _mat.SetColor("_BaseColor", new Color(0.85f, 0.92f, 1f));
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "TvSnow";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            q.transform.position = centre;
            q.transform.rotation = Quaternion.identity;                 // the quad faces -z: the room, south of the cart
            q.transform.localScale = new Vector3(0.33f, 0.28f, 1f);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = _mat; r.shadowCastingMode = ShadowCastingMode.Off; r.renderingLayerMask = uint.MaxValue;
            var lg = new GameObject("TvGlow");
            lg.transform.SetParent(transform, false);
            lg.transform.position = centre + new Vector3(0f, 0f, -0.35f);
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point; _light.color = new Color(0.62f, 0.75f, 1f); _light.range = 3.5f; _light.intensity = 0.7f; _light.shadows = LightShadows.None;
            lg.AddComponent<UniversalAdditionalLightData>().renderingLayers = 0xFFFFFFFFu;
            var sg = new GameObject("TvHiss");
            sg.transform.SetParent(transform, false);
            sg.transform.position = centre;
            _hiss = ItemModels.Source(sg, 7f);
            _hiss.clip = MakeHiss(); _hiss.loop = true; _hiss.volume = 0.12f; _hiss.minDistance = 0.8f;
            Audio.SoundRoute.Add(_hiss);
            _hiss.Play();
            _tapeTex = new Texture2D(TW, TH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "TvTape" };
            _tapePx = new Color32[TW * TH];
            Draw();
        }

        /// <summary>Is the player sitting in the training room (on one of its chairs, in front of the TV)?</summary>
        bool SeatedHere()
        {
            if (!_seating) _seating = FindAnyObjectByType<Seating>();
            if (!_seating || !_seating.Seated || !_seating.Chair) return false;
            var c = _seating.Chair.transform.position;
            return new Vector2(c.x - Foot.x, c.z - Foot.z).magnitude < 7f && Mathf.Abs(c.y - Foot.y) < 2f;
        }

        void StartTape()
        {
            _playing = true; _tapeT = 0f;
            _mat.SetTexture("_BaseMap", _tapeTex);
            _mat.SetColor("_BaseColor", new Color(1.1f, 1.05f, 1f));
        }

        void StopTape(bool finished)
        {
            _playing = false;
            _mat.SetTexture("_BaseMap", _tex);
            _mat.SetColor("_BaseColor", new Color(0.85f, 0.92f, 1f));
            var d = Saves.GameSession.Data;
            if (!finished || d == null) return;
            bool first = !d.tapeWatched;
            d.tapeWatched = true;
            if (first) Camcorder.CamcorderOSD.Notice("ORIENTATION DONE - NOW RIDE THE LIFT DOWN TO CLOCK IN");
            Contracts.Instance?.Changed();
        }

        /// <summary>A card of the tape, on a VHS picture: a band of colour, its lines, grain and a tracking line rolling up.</summary>
        void DrawTape()
        {
            int card = Mathf.Min(s_cards.Length - 1, Mathf.FloorToInt(_tapeT / CardTime));
            var lines = s_cards[card];
            bool title = card == 0;
            Color32 bg = title ? new Color32(232, 150, 32, 255) : card == s_cards.Length - 1 ? new Color32(14, 12, 18, 255) : new Color32(52, 36, 26, 255);
            Color32 ink = title ? new Color32(59, 42, 30, 255) : new Color32(245, 214, 150, 255);
            for (int i = 0; i < _tapePx.Length; i++) _tapePx[i] = bg;
            // The company's stripes along the foot (orange, gold, avocado), as on its signs.
            PixelFont.Fill(_tapePx, TW, TH, 0, TH - 12, TW, TH - 8, new Color32(217, 102, 31, 255));
            PixelFont.Fill(_tapePx, TW, TH, 0, TH - 8, TW, TH - 4, new Color32(232, 178, 58, 255));
            PixelFont.Fill(_tapePx, TW, TH, 0, TH - 4, TW, TH, new Color32(124, 138, 46, 255));
            int lineH = 13, y0 = (TH - 12) / 2 - lines.Length * lineH / 2;
            for (int k = 0; k < lines.Length; k++)
            {
                int dot = title && k == 0 ? 3 : 1;
                int y = title && k == 0 ? 22 : title ? 58 + (k - 1) * lineH : y0 + k * lineH;
                PixelFont.DrawCentred(_tapePx, TW, TH, lines[k], TW / 2, y, dot, ink);
            }
            // VHS: grain, and a tracking line rolling up the picture.
            int track = TH - 1 - Mathf.FloorToInt(Mathf.Repeat(_tapeT * 0.35f, 1f) * TH);
            for (int y = 0; y < TH; y++)
            for (int x = 0; x < TW; x++)
            {
                int i = y * TW + x;
                var c = _tapePx[i];
                int n = _rng.Next(-14, 15) + (Mathf.Abs(y - track) < 2 ? 70 : 0);
                _tapePx[i] = new Color32((byte)Mathf.Clamp(c.r + n, 0, 255), (byte)Mathf.Clamp(c.g + n, 0, 255), (byte)Mathf.Clamp(c.b + n, 0, 255), 255);
            }
            _tapeTex.SetPixels32(_tapePx);
            _tapeTex.Apply(false);
        }

        static AudioClip MakeHiss()
        {
            const int rate = 22050; int n = rate * 2;
            var d = new float[n];
            var rng = new System.Random(3);
            float hp = 0f, last = 0f;
            for (int i = 0; i < n; i++)
            {
                float x = (float)(rng.NextDouble() * 2.0 - 1.0);
                hp = 0.92f * (hp + x - last); last = x;                     // hiss: the high end of the noise
                d[i] = hp * 0.35f + 0.04f * Mathf.Sin(2f * Mathf.PI * 3900f * i / rate);   // and a CRT's whine, low
            }
            var clip = AudioClip.Create("TvHiss", n, 1, rate, false);
            clip.SetData(d, 0);
            return clip;
        }

        void Draw()
        {
            int band = Mathf.FloorToInt(_roll * H);
            for (int y = 0; y < H; y++)
            {
                bool inBand = Mathf.Abs(y - band) < 5 || Mathf.Abs(y - band + H) < 5;
                for (int x = 0; x < W; x++)
                {
                    int v = _rng.Next(256);
                    v = v * v / 255;                                        // mostly dark grain, bright specks
                    if (inBand) v = Mathf.Min(255, v + 60);
                    byte b = (byte)v;
                    _px[y * W + x] = new Color32(b, b, (byte)Mathf.Min(255, v + 18), 255);
                }
            }
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        void Update()
        {
            _roll = Mathf.Repeat(_roll + Time.deltaTime * 0.12f, 1f);
            // The hiss is the building's sound: it follows the AMBIENCE setting (quieter under the tape).
            if (_hiss) _hiss.volume = (_playing ? 0.03f : 0.08f) * GameSettings.Ambience;
            // Sitting down in front of it plays the tape; getting up stops it.
            bool seated = SeatedHere();
            if (seated && !_wasSeated && !_playing) StartTape();
            if (!seated && _playing) StopTape(false);
            _wasSeated = seated;
            if (_playing)
            {
                _tapeT += Time.deltaTime;
                if (_tapeT >= CardTime * s_cards.Length) { StopTape(true); return; }
            }
            if (Time.time < _next) return;
            _next = Time.time + 1f / 15f;
            if (_playing) { DrawTape(); if (_light) _light.intensity = 0.9f; return; }
            Draw();
            if (_light) _light.intensity = 0.55f + (float)_rng.NextDouble() * 0.3f;     // the glow flickers with the snow
        }

        void OnDestroy()
        {
            if (_tex) Destroy(_tex);
            if (_tapeTex) Destroy(_tapeTex);
            if (_mat) Destroy(_mat);
        }
    }
}
