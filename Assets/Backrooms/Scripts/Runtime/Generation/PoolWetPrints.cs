using Backrooms.Audio;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>
    /// After the water (2026-10-31, the water interaction plan): walking out of a basin onto a dry deck leaves wet footprints
    /// that dry over half a minute (fainter the longer you have been out: the shoes dry too), and you drip for a few seconds.
    /// There is no body to show (the default is none), so the prints are the shoes you would have: left and right of where
    /// you walk, a stride apart. All prints in one instanced draw (Backrooms/PoolWetPrint, RenderMeshInstanced).
    /// </summary>
    public class PoolWetPrints : MonoBehaviour
    {
        const int Max = 160;
        [Tooltip("Seconds out of the water the shoes still leave prints.")] public float shoesDry = 22f;
        [Tooltip("Seconds a print takes to dry.")] public float printDry = 30f;
        [Tooltip("Seconds of dripping after leaving the water.")] public float dripFor = 6f;

        readonly Matrix4x4[] _m = new Matrix4x4[Max];
        readonly float[] _wet0 = new float[Max], _born = new float[Max], _wet = new float[Max];
        readonly Matrix4x4[] _draw = new Matrix4x4[Max];
        int _next, _count;
        float _exited = -999f, _travel, _nextDrip;
        int _foot, _dripPick;
        Vector3 _last;
        bool _wasIn;
        Mesh _quad;
        Material _mat;
        MaterialPropertyBlock _mpb;
        Transform _player;
        CamcorderPlayerController _ctrl;

        void Awake()
        {
            var sh = Shader.Find("Backrooms/PoolWetPrint");
            if (sh) _mat = new Material(sh) { name = "Wet prints", hideFlags = HideFlags.DontSave, enableInstancing = true };
            _quad = new Mesh { name = "PrintQuad", hideFlags = HideFlags.DontSave };
            // A shoe's sole: 11 cm by 28 cm, lying on the floor, heel at -Z.
            _quad.SetVertices(new[] { new Vector3(-0.055f, 0f, -0.14f), new Vector3(0.055f, 0f, -0.14f), new Vector3(0.055f, 0f, 0.14f), new Vector3(-0.055f, 0f, 0.14f) });
            _quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            _quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            _quad.RecalculateBounds();
            _mpb = new MaterialPropertyBlock();
        }

        void OnDestroy() { if (_mat) Destroy(_mat); if (_quad) Destroy(_quad); }

        void Update()
        {
            var world = PoolWorld.Instance;
            if (!_player && world && world.streamer) { _player = world.streamer.player; if (_player) _ctrl = _player.GetComponent<CamcorderPlayerController>(); }
            if (!_player || !_ctrl) return;
            bool inWater = PoolRipples.PlayerInWater;
            var p = _player.position;
            if (_wasIn && !inWater) { _exited = Time.time; _travel = 0f; _last = p; _nextDrip = Time.time + 0.15f; }
            _wasIn = inWater;

            float since = Time.time - _exited;
            if (!inWater && since < shoesDry && _ctrl.IsGrounded && !_ctrl.noclip && world.IsPool(p))
            {
                Vector3 d = p - _last; d.y = 0f;
                _last = p;
                if (d.magnitude < 1f) _travel += d.magnitude;
                float stride = _ctrl.IsRunning ? 0.52f : 0.38f;
                if (_travel >= stride && d.sqrMagnitude > 1e-6f)
                {
                    _travel = 0f;
                    _foot ^= 1;
                    var fwd = d.normalized;
                    var side = Vector3.Cross(Vector3.up, fwd) * (_foot == 0 ? 0.1f : -0.1f);
                    if (Physics.Raycast(p + side + Vector3.up * 0.5f, Vector3.down, out var hit, 1.2f, ~0, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.9f
                        && !world.WaterAt(hit.point, out _))
                        Put(hit.point + Vector3.up * 0.002f, fwd, _foot == 1, 1f - since / shoesDry);
                }
            }

            // Dripping: quick at first, slower, stopping.
            if (since < dripFor && Time.time >= _nextDrip)
            {
                float k = since / dripFor;
                _nextDrip = Time.time + Mathf.Lerp(0.18f, 1.3f, k) * Random.Range(0.6f, 1.4f);
                var at = p + new Vector3(Random.Range(-0.15f, 0.15f), 0.05f, Random.Range(-0.15f, 0.15f));
                WorldSound.PlayAt(SoundBank.Drip(_dripPick++), at, 0.22f * (1f - k * 0.6f), 8f, Random.Range(0.92f, 1.08f));
            }

            Draw();
        }

        void Put(Vector3 at, Vector3 fwd, bool left, float wet)
        {
            var rot = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(0f, Random.Range(-6f, 6f) + (left ? -4f : 4f), 0f);
            _m[_next] = Matrix4x4.TRS(at, rot, new Vector3(left ? -1f : 1f, 1f, 1f));
            _wet0[_next] = Mathf.Clamp01(wet) * Random.Range(0.85f, 1f);
            _born[_next] = Time.time;
            _next = (_next + 1) % Max;
            _count = Mathf.Min(_count + 1, Max);
        }

        void Draw()
        {
            if (_count == 0 || !_mat) return;
            int n = 0;
            for (int i = 0; i < _count; i++)
            {
                float w = _wet0[i] * (1f - (Time.time - _born[i]) / printDry);
                if (w <= 0.01f) continue;
                _draw[n] = _m[i]; _wet[n] = w; n++;
            }
            if (n == 0) { _count = 0; _next = 0; return; }
            _mpb.SetFloatArray("_Wet", _wet);
            var rp = new RenderParams(_mat) { matProps = _mpb, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = 0,
                                              worldBounds = new Bounds(_player.position, Vector3.one * 200f) };
            Graphics.RenderMeshInstanced(rp, _quad, 0, _draw, n);
        }
    }
}
