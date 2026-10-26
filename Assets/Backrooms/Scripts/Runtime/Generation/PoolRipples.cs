using UnityEngine;

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
        RenderTexture _a, _b;
        Vector2Int _origin;              // world texel of the window's (0, 0)
        bool _have;
        float _acc;
        Transform _player;
        CamcorderPlayerController _ctrl;
        readonly Vector4[] _drops = new Vector4[8];
        int _dropCount;
        bool _wasIn;
        float _stride;
        int _foot;

        /// <summary>The player is standing in water (for footsteps).</summary>
        public static bool PlayerInWater { get; private set; }

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
            PlayerInWater = false;
            if (!active) { if (_have) Shader.SetGlobalVector("_PoolRippleRect", Vector4.zero); return; }
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
            if (inWater)
            {
                var v = _ctrl ? _ctrl.PlanarVelocity : Vector3.zero;
                float speed = v.magnitude;
                if (!_wasIn) Drop(p, 0.45f, -splash);
                else if (speed > 0.05f)
                {
                    // Alternate legs, a little to each side and ahead of the body.
                    var side = Vector3.Cross(Vector3.up, v / speed) * 0.12f;
                    _stride += speed * Time.deltaTime;
                    if (_stride > 0.35f) { _stride = 0f; _foot ^= 1; }
                    var leg = p + v.normalized * 0.12f + (_foot == 0 ? side : -side);
                    Drop(leg, 0.13f, -wake * Mathf.Clamp(speed / 1.4f, 0.2f, 2.2f));
                }
            }
            _wasIn = inWater;

            _acc = Mathf.Min(_acc + Time.deltaTime, 4f / 60f);
            bool first = true;
            while (_acc >= 1f / 60f)
            {
                _acc -= 1f / 60f;
                Step(first ? shift : Vector2Int.zero, first ? _dropCount : 0);
                first = false;
            }
            Shader.SetGlobalTexture("_PoolRipple", _a);
            Shader.SetGlobalVector("_PoolRippleRect", new Vector4(_origin.x * texel, _origin.y * texel, Size * texel, slopeGain));
        }

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
            return true;
        }

        static RenderTexture Make(string name)
        {
            var rt = new RenderTexture(Size, Size, 0, RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear)
            { name = name, enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            rt.Create();
            return rt;
        }

        void Step(Vector2Int shift, int drops)
        {
            _cs.SetTexture(_kernel, "_Prev", _a);
            _cs.SetTexture(_kernel, "_Next", _b);
            _cs.SetTexture(_kernel, "_PoolMap", Shader.GetGlobalTexture("_PoolMap"));
            _cs.SetVector("_PoolMapParams", Shader.GetGlobalVector("_PoolMapParams"));
            _cs.SetInts("_Shift", shift.x, shift.y);
            _cs.SetInt("_Size", Size);
            _cs.SetFloat("_Damping", damping);
            _cs.SetVector("_OriginWS", new Vector4(_origin.x * texel, _origin.y * texel, 0f, 0f));
            _cs.SetFloat("_Texel", texel);
            _cs.SetVectorArray("_Drops", _drops);
            _cs.SetInt("_DropCount", drops);
            _cs.Dispatch(_kernel, Size / 8, Size / 8, 1);
            (_a, _b) = (_b, _a);
        }

        void OnDestroy()
        {
            if (_a) { _a.Release(); Destroy(_a); }
            if (_b) { _b.Release(); Destroy(_b); }
        }
    }
}
