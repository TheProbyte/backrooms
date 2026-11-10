using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>
    /// Ripples where the player wades through the Poolrooms' water: a small wave simulation (PoolRipples.compute) on a
    /// 20 m square that follows the player, stepped at 60 Hz while the player is in the Poolrooms. Legs push the water
    /// as they move; stepping in makes a splash. The water shader reads the heights (PoolCommon.hlsl, PoolRippleSlope).
    /// </summary>
    public class PoolRipples : MonoBehaviour
    {
        public const int Size = 256;
        [Tooltip("Texel size, metres.")] public float texel = 0.08f;
        [Range(0.9f, 1f)] public float damping = 0.992f;
        [Tooltip("How strongly the heights tilt the surface.")] public float slopeGain = 1.5f;
        [Tooltip("Height pushed by a leg moving at walking pace, per frame (the scheme spreads it out fast, so it is large).")] public float wake = 0.008f;
        public float splash = 0.02f;

        ComputeShader _cs;
        int _kernel;
        RenderTexture _a, _b, _fa, _fb;      // heights; flow and foam
        [Tooltip("Smoothing of the water's motion: small ripples calm first.")] public float viscosity = 0.035f;
        [Tooltip("Foam left per second (of what there was).")] [Range(0.5f, 1f)] public float foamKeep = 0.6f;
        [Tooltip("Foam made by churned water (2026-10-31: less from wading, which only stirs clean pool water).")] public float foamGain = 5f;
        [Tooltip("Foam made where something goes in.")] public float foamDrop = 7f;
        Vector2Int _origin;              // world texel of the window's (0, 0)
        bool _have;
        float _acc;
        Transform _player;
        CamcorderPlayerController _ctrl;
        readonly Vector4[] _drops = new Vector4[16];
        int _dropCount;
        bool _wasIn;
        float _stride;
        int _stepsSeen;
        int _foot;

        /// <summary>The player is standing in water (for footsteps).</summary>
        public static bool PlayerInWater { get; private set; }
        /// <summary>How deep the water is where the player stands (m; 0 out of it).</summary>
        public static float PlayerWaterDepth { get; private set; }

        // ---------------- Heights on the CPU (2026-11-01, for the lane ropes: PoolLaneRopes) ----------------

        static float[] s_h;
        static Vector2 s_hOrigin;
        static float s_hTexel = 0.08f, s_wantUntil;
        static bool s_pending;

        /// <summary>Something wants the surface's heights on the CPU for a while (copied back a few times a second).</summary>
        public static void WantHeights() => s_wantUntil = Time.time + 0.5f;

        /// <summary>The ripples' height at a point (m above the still surface; 0 outside the window or before a copy).</summary>
        public static float HeightAt(float x, float z)
        {
            if (s_h == null) return 0f;
            float fx = (x - s_hOrigin.x) / s_hTexel - 0.5f, fz = (z - s_hOrigin.y) / s_hTexel - 0.5f;
            int ix = Mathf.FloorToInt(fx), iz = Mathf.FloorToInt(fz);
            if (ix < 0 || iz < 0 || ix >= Size - 1 || iz >= Size - 1) return 0f;
            float tx = fx - ix, tz = fz - iz;
            int i = iz * Size + ix;
            return Mathf.Lerp(Mathf.Lerp(s_h[i], s_h[i + 1], tx), Mathf.Lerp(s_h[i + Size], s_h[i + Size + 1], tx), tz);
        }

        void RequestHeights()
        {
            if (s_pending || Time.time > s_wantUntil || !_a || !SystemInfo.supportsAsyncGPUReadback) return;
            s_pending = true;
            var origin = new Vector2(_origin.x * texel, _origin.y * texel);
            float tx = texel;
            AsyncGPUReadback.Request(_a, 0, req =>
            {
                s_pending = false;
                if (req.hasError) return;
                var data = req.GetData<Vector2>();
                if (s_h == null || s_h.Length != data.Length) s_h = new float[data.Length];
                for (int i = 0; i < data.Length; i++) s_h[i] = data[i].x;
                s_hOrigin = origin; s_hTexel = tx;
            });
        }

        static readonly System.Collections.Generic.List<Vector4> s_extra = new System.Collections.Generic.List<Vector4>();
        /// <summary>Something else disturbing the water (a thing thrown in): a dip of height (negative) and radius at a point,
        /// taken into the next step of the simulation.</summary>
        public static void Splash(Vector3 at, float radius, float height)
        {
            if (s_extra.Count < 14) s_extra.Add(new Vector4(at.x, at.z, radius, height));
        }

        void Update()
        {
            var world = PoolWorld.Instance;
            if (!_player)
            {
                var ws = world ? world.streamer : null;
                _player = ws ? ws.player : null;
                if (_player) _ctrl = _player.GetComponent<CamcorderPlayerController>();
            }
            bool active = world && _player && world.IsPool(_player.position);
            PlayerInWater = false; PlayerWaterDepth = 0f;
            if (!active) { s_extra.Clear(); if (_have) Shader.SetGlobalVector("_PoolRippleRect", Vector4.zero); return; }
            if (!Ensure()) return;

            // Follow the player: move the window in whole texels once they are a few metres off centre.
            var p = _player.position;
            var want = new Vector2Int(Mathf.FloorToInt(p.x / texel) - Size / 2, Mathf.FloorToInt(p.z / texel) - Size / 2);
            var shift = Vector2Int.zero;
            if (!_have || Mathf.Abs(want.x - _origin.x) > 40 || Mathf.Abs(want.y - _origin.y) > 40)
            {
                shift = _have ? want - _origin : new Vector2Int(Size * 4, 0);   // first time: everything starts flat
                _origin = want;
                _have = true;
            }

            // The player's legs in the water.
            _dropCount = 0;
            bool inWater = world.WaterAt(p, out float level) && p.y < level + 0.05f;
            PlayerInWater = inWater;
            PlayerWaterDepth = inWater ? level - p.y : 0f;
            if (inWater)
            {
                var v = _ctrl ? _ctrl.PlanarVelocity : Vector3.zero;
                float speed = v.magnitude;
                if (!_wasIn)
                {
                    Drop(p, 0.45f, -splash);
                    // Stepping in: spray, more for a jump (and a drop or two on the lens if it was a hard landing).
                    float land = _ctrl ? _ctrl.LandedThisFrame : 0f;
                    var surf = new Vector3(p.x, level, p.z);
                    if (land > 0.5f) PoolSplashes.Splash(surf, Mathf.Clamp01(land / 6f), v * 0.3f, true);
                    else { PoolSplashes.Burst(surf, 14, 1.2f, v * 0.3f, 0.8f, 1f); PoolSplashes.Bubbles(surf, 8, 0.2f, 0.25f); }
                    if (land > 2f) Camcorder.LensDrops.Add(3, 0.8f);
                }
                else if (speed > 0.05f)
                {
                    // Alternate legs, a little to each side and ahead of the body.
                    var side = Vector3.Cross(Vector3.up, v / speed) * 0.12f;
                    _stride += speed * Time.deltaTime;
                    bool stepped = _ctrl ? _ctrl.WadeSteps != _stepsSeen : _stride > 0.35f;
                    if (_ctrl) _stepsSeen = _ctrl.WadeSteps;
                    if (stepped)
                    {
                        _stride = 0f; _foot ^= 1;
                        // Each stride throws a little water forward off the shin; running throws more (and now and then some
                        // reaches the lens).
                        float k = Mathf.Clamp01(speed / 3.6f);
                        var shin = p + v / speed * 0.25f + (_foot == 0 ? side : -side);
                        PoolSplashes.Burst(new Vector3(shin.x, level, shin.z), Mathf.RoundToInt(Mathf.Lerp(2f, 18f, k * k)), Mathf.Lerp(0.6f, 2.2f, k), v * 0.55f, 0.55f);
                        PoolSplashes.Bubbles(new Vector3(shin.x, level, shin.z), Mathf.RoundToInt(Mathf.Lerp(2f, 8f, k)), 0.08f, 0.3f);
                        if (k > 0.8f && Random.value < 0.08f) Camcorder.LensDrops.Add(1, 0.6f);
                    }
                    var leg = p + v.normalized * 0.12f + (_foot == 0 ? side : -side);
                    // A shin moving through water: it piles up in front (the bow wave) and leaves a hollow behind.
                    float wk = Mathf.Clamp(speed / 1.4f, 0.2f, 2.2f);
                    var dir = v / speed;
                    Drop(leg + dir * 0.1f, 0.12f, wake * 0.6f * wk);
                    Drop(leg - dir * 0.08f, 0.12f, -wake * wk);
                }
            }
            _wasIn = inWater;

            PoolCirculation.Stir(p, Time.deltaTime);
            foreach (var e in s_extra) if (_dropCount < _drops.Length) _drops[_dropCount++] = e;
            s_extra.Clear();
            _acc = Mathf.Min(_acc + Time.deltaTime, 4f / 60f);
            // A move of the window must reach the simulation this frame (the drawing uses the new origin now), or the
            // waves would jump by the shift and seem to start somewhere else.
            if (shift != Vector2Int.zero && _acc < 1f / 60f) _acc = 1f / 60f;
            bool first = true;
            while (_acc >= 1f / 60f)
            {
                _acc -= 1f / 60f;
                Step(first ? shift : Vector2Int.zero, first ? _dropCount : 0);
                first = false;
            }
            Shader.SetGlobalTexture("_PoolRipple", _a);
            Shader.SetGlobalTexture("_PoolFlow", _fa);
            Shader.SetGlobalVector("_PoolRippleRect", new Vector4(_origin.x * texel, _origin.y * texel, Size * texel, slopeGain));
            RequestHeights();
        }

        readonly Vector4[] _jets = new Vector4[PoolCirculation.MaxJets], _sinks = new Vector4[PoolCirculation.MaxSinks];

        void Drop(Vector3 at, float radius, float height)
        {
            if (_dropCount >= _drops.Length) return;
            _drops[_dropCount++] = new Vector4(at.x, at.z, radius, height);
        }

        bool Ensure()
        {
            if (_a) return true;
            if (!SystemInfo.supportsComputeShaders) return false;
            _cs = Resources.Load<ComputeShader>("PoolRipples");
            if (!_cs) return false;
            _kernel = _cs.FindKernel("Step");
            _a = Make("PoolRippleA"); _b = Make("PoolRippleB");
            _fa = Make("PoolFlowA", RenderTextureFormat.ARGBHalf); _fb = Make("PoolFlowB", RenderTextureFormat.ARGBHalf);
            return true;
        }

        static RenderTexture Make(string name, RenderTextureFormat format = RenderTextureFormat.RGFloat)
        {
            var rt = new RenderTexture(Size, Size, 0, format, RenderTextureReadWrite.Linear)
            { name = name, enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            rt.Create();
            return rt;
        }

        void Step(Vector2Int shift, int drops)
        {
            _cs.SetTexture(_kernel, "_Prev", _a);
            _cs.SetTexture(_kernel, "_Next", _b);
            _cs.SetTexture(_kernel, "_PoolMap", Shader.GetGlobalTexture("_PoolMap"));
            _cs.SetTexture(_kernel, "_PoolMap2", Shader.GetGlobalTexture("_PoolMap2"));
            _cs.SetTexture(_kernel, "_FlowPrev", _fa);
            _cs.SetTexture(_kernel, "_FlowNext", _fb);
            _cs.SetFloat("_Viscosity", viscosity);
            _cs.SetFloat("_Dt", 1f / 60f);
            _cs.SetFloat("_FoamDecay", Mathf.Pow(foamKeep, 1f / 60f));
            _cs.SetFloat("_FoamGain", foamGain);
            _cs.SetFloat("_FoamDrop", foamDrop);
            _cs.SetVector("_PoolMapParams", Shader.GetGlobalVector("_PoolMapParams"));
            _cs.SetInts("_Shift", shift.x, shift.y);
            _cs.SetInt("_Size", Size);
            _cs.SetFloat("_Damping", damping);
            _cs.SetVector("_OriginWS", new Vector4(_origin.x * texel, _origin.y * texel, 0f, 0f));
            _cs.SetFloat("_Texel", texel);
            _cs.SetVectorArray("_Drops", _drops);
            _cs.SetInt("_DropCount", drops);
            PoolCirculation.Fill(_player ? _player.position : Vector3.zero, _jets, out int jn, _sinks, out int sn);
            _cs.SetVectorArray("_Jets", _jets);
            _cs.SetInt("_JetCount", jn);
            _cs.SetVectorArray("_Sinks", _sinks);
            _cs.SetInt("_SinkCount", sn);
            var w = PoolWorld.Instance;
            _cs.SetInt("_Storey", w && _player ? w.StoreyOf(_player.position.y) : 0);
            _cs.Dispatch(_kernel, Size / 8, Size / 8, 1);
            (_a, _b) = (_b, _a);
            (_fa, _fb) = (_fb, _fa);
        }

        void OnDestroy()
        {
            if (_a) { _a.Release(); Destroy(_a); }
            if (_b) { _b.Release(); Destroy(_b); }
            if (_fa) { _fa.Release(); Destroy(_fa); }
            if (_fb) { _fb.Release(); Destroy(_fb); }
        }
    }
}
