using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// A leak in the ceiling (OfficeChunkBuilder.Damp): a drop gathers under the stained tile, swelling for a few seconds,
    /// then falls (under gravity, stretching as it goes) and lands: in water a plink and a ring spreading over it, on carpet
    /// a dull tap. Sleeps while the camera is far off. Placed at the tile; fall is how far down it lands.
    /// </summary>
    public class CeilingLeak : MonoBehaviour
    {
        public float fall = 2.6f;
        public bool water;
        public float interval = 3f;
        public int seed;

        const float MaxRadius = 0.004f, Gravity = 9.81f, Near = 16f;
        static Mesh s_drop, s_ring;
        static Material s_ringMat;

        Transform _drop, _ring;
        Renderer _dropR, _ringR;
        MaterialPropertyBlock _mpb;
        float _t, _y, _v, _ringT = -1f, _nextCheck, _wait;
        bool _falling, _near;
        int _pick;

        void Start()
        {
            if (!FittingMaterials.Water) { enabled = false; return; }
            if (!s_drop) s_drop = DropMesh();
            _drop = new GameObject("Drop").transform;
            _drop.SetParent(transform, false);
            _drop.gameObject.AddComponent<MeshFilter>().sharedMesh = s_drop;
            _dropR = _drop.gameObject.AddComponent<MeshRenderer>();
            _dropR.sharedMaterial = FittingMaterials.Drop ? FittingMaterials.Drop : FittingMaterials.Water;
            _dropR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // (No motion vectors: the camcorder's shutter blur drew the falling drop as a long dark streak.)
            _dropR.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            if (water && RingMaterial())
            {
                if (!s_ring) s_ring = RingMesh();
                _ring = new GameObject("Ring").transform;
                _ring.SetParent(transform, false);
                _ring.localPosition = Vector3.down * (fall - 0.001f);
                _ring.gameObject.AddComponent<MeshFilter>().sharedMesh = s_ring;
                _ringR = _ring.gameObject.AddComponent<MeshRenderer>();
                _ringR.sharedMaterial = s_ringMat;
                _ringR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _ringR.enabled = false;
                _mpb = new MaterialPropertyBlock();
            }
            var rng = new System.Random(seed);
            _t = (float)rng.NextDouble() * interval;
            _wait = interval * (0.8f + 0.4f * (float)rng.NextDouble());
            _pick = rng.Next(16);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float now = Time.time;
            if (now >= _nextCheck)
            {
                _nextCheck = now + 0.5f;
                var cam = Camera.main;
                _near = cam && (cam.transform.position - transform.position).sqrMagnitude < Near * Near;
                if (_dropR.enabled != _near) _dropR.enabled = _near;
            }
            if (!_near) return;
            if (!_falling)
            {
                // Gathering: the bead swells, hanging a little lower as it grows.
                _t += dt;
                float k = Mathf.Clamp01(_t / _wait);
                float r = MaxRadius * Mathf.Pow(k, 0.6f);
                _drop.localPosition = Vector3.down * (r * 0.9f);
                _drop.localScale = new Vector3(r, r * (1f + 0.6f * k * k), r);
                if (_t >= _wait) { _falling = true; _y = 0f; _v = 0f; }
            }
            else
            {
                _v += Gravity * dt;
                _y += _v * dt;
                float stretch = 1f + Mathf.Min(1.8f, _v * 0.35f);
                _drop.localScale = new Vector3(MaxRadius / Mathf.Sqrt(stretch), MaxRadius * stretch, MaxRadius / Mathf.Sqrt(stretch));
                _drop.localPosition = Vector3.down * (MaxRadius + _y);
                if (_y >= fall)
                {
                    Land();
                    _falling = false;
                    _t = 0f;
                    _wait = interval * (0.75f + 0.5f * Random.value);
                    _drop.localScale = Vector3.zero;
                }
            }
            if (_ringR && _ringT >= 0f)
            {
                _ringT += dt;
                const float life = 0.9f;
                if (_ringT > life) { _ringR.enabled = false; _ringT = -1f; }
                else
                {
                    float u = _ringT / life;
                    _ring.localScale = Vector3.one * (0.015f + 0.13f * Mathf.Sqrt(u));
                    _mpb.SetFloat("_Fade", (1f - u) * (1f - u));
                    _ringR.SetPropertyBlock(_mpb);
                }
            }
        }

        void Land()
        {
            var at = transform.position + Vector3.down * fall;
            if (water)
            {
                Audio.WorldSound.PlayAt(Audio.SoundBank.Drip(_pick++), at, 0.32f, 10f, Random.Range(0.75f, 1.05f));
                if (_ringR) { _ringT = 0f; _ringR.enabled = true; }
            }
            else Audio.WorldSound.PlayAt(Audio.SoundBank.Drip(_pick++), at, 0.12f, 6f, Random.Range(0.35f, 0.45f));   // into carpet: a dull tap
        }

        static Material RingMaterial()
        {
            if (s_ringMat) return s_ringMat;
            var sh = Shader.Find("Backrooms/Ripple");
            if (sh) s_ringMat = new Material(sh) { name = "Leak ripple", hideFlags = HideFlags.DontSave };
            return s_ringMat;
        }

        /// <summary>A unit drop: a low sphere (radius 1), scaled each frame.</summary>
        static Mesh DropMesh()
        {
            const int rings = 5, segs = 8;
            var v = new System.Collections.Generic.List<Vector3>();
            var n = new System.Collections.Generic.List<Vector3>();
            var t = new System.Collections.Generic.List<int>();
            for (int i = 0; i <= rings; i++)
            {
                float th = Mathf.PI * i / rings;
                for (int j = 0; j <= segs; j++)
                {
                    float ph = Mathf.PI * 2f * j / segs;
                    var p = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(p); n.Add(p);
                }
            }
            for (int i = 0; i < rings; i++)
            for (int j = 0; j < segs; j++)
            {
                int a = i * (segs + 1) + j, b = a + segs + 1;
                t.Add(a); t.Add(a + 1); t.Add(b);
                t.Add(a + 1); t.Add(b + 1); t.Add(b);
            }
            var m = new Mesh { name = "Leak drop" };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0);
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return m;
        }

        /// <summary>A flat quad (uv -1..1) the ripple shader draws a ring on.</summary>
        static Mesh RingMesh()
        {
            var m = new Mesh { name = "Leak ripple" };
            m.vertices = new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(1, 0, 1), new Vector3(-1, 0, 1) };
            m.uv = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateBounds();
            return m;
        }
    }
}
