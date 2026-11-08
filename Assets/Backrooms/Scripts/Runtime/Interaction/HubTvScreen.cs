using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The training room's TV (HubTvCart, 2026-11-07): left on with no tape in, so it shows snow: static redrawn about
    /// fifteen times a second with a slow rolling band, a cold blue glow on whoever sits in front of it, and a faint hiss.
    /// (The orientation tape plays on it later: Docs/HubPlan.md, phase C.)
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
            Draw();
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
            if (Time.time < _next) return;
            _next = Time.time + 1f / 15f;
            Draw();
            if (_light) _light.intensity = 0.55f + (float)_rng.NextDouble() * 0.3f;     // the glow flickers with the snow
        }

        void OnDestroy()
        {
            if (_tex) Destroy(_tex);
            if (_mat) Destroy(_mat);
        }
    }
}
