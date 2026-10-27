using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>
    /// Stops drawing chunks the camera cannot see (behind walls), every frame, from the layout itself: a fan of rays across
    /// the view is walked over the cell grid (2D) and stops at solid walls. Every chunk a ray enters is visible, and so is
    /// the chunk on the far side of the wall it stops at (that wall's face may be built by either chunk). The chunk the eye
    /// is in, and any within a little of it, are always drawn.
    ///
    /// A hidden chunk still casts its shadows: its shadow-casting renderers draw shadows only, the rest are switched off.
    /// Everything comes back the frame it can be seen, so there is no pop-in. Items (and furniture moved out of a chunk)
    /// are never touched. Upper storeys and debug noclip flying are always drawn in full.
    /// Cost: ~250 rays of at most ~60 cells, a fraction of a millisecond; skipped while the view has not changed.
    /// </summary>
    [DefaultExecutionOrder(30000)]                   // after the camera has moved for this frame
    public class ChunkVisibility : MonoBehaviour
    {
        public static bool Enabled = true;
        public WorldStreamer streamer;
        [Tooltip("Degrees between rays (finer = safer for thin gaps far away).")] public float rayStep = 0.35f;
        [Tooltip("Extra degrees on each side of the view.")] public float margin = 10f;
        [Tooltip("Furthest a ray goes (m).")] public float maxDistance = 70f;

        class Entry
        {
            public GameObject go;
            public bool hidden;
            public readonly List<(Renderer r, ShadowCastingMode mode, bool off)> changed = new List<(Renderer, ShadowCastingMode, bool)>();
        }

        readonly Dictionary<Vector3Int, Entry> _chunks = new Dictionary<Vector3Int, Entry>();
        readonly HashSet<Vector2Int> _visible = new HashSet<Vector2Int>();
        readonly List<Renderer> _scratch = new List<Renderer>();
        Vector3 _lastEye; Quaternion _lastRot; bool _dirty = true;
        public int HiddenChunks { get; private set; }
        public int VisibleChunks { get; private set; }

        void OnEnable()
        {
            if (!streamer) streamer = GetComponent<WorldStreamer>();
            if (streamer) { streamer.ChunkLoaded += Loaded; streamer.ChunkUnloaded += Unloaded; }
            Destruction.WallDamage.Changed += WallChanged;
        }

        void WallChanged(Destruction.WallKey k) => _dirty = true;           // a hole may show what was behind the wall

        void OnDisable()
        {
            if (streamer) { streamer.ChunkLoaded -= Loaded; streamer.ChunkUnloaded -= Unloaded; }
            Destruction.WallDamage.Changed -= WallChanged;
            foreach (var e in _chunks.Values) Show(e);
            _chunks.Clear();
        }

        void Loaded(Vector3Int key, GameObject go) { if (_chunks.TryGetValue(key, out var old)) Show(old); _chunks[key] = new Entry { go = go }; _dirty = true; }
        void Unloaded(Vector3Int key, GameObject go)
        {
            if (_chunks.TryGetValue(key, out var e) && e.go == go) { Show(e); _chunks.Remove(key); }
            _dirty = true;
        }

        /// <summary>Recomputes now (tests: after moving the camera by hand).</summary>
        public void ForceUpdate(Camera view = null) { _dirty = true; Refresh(view ? view : Camera.main); }

        void LateUpdate() => Refresh(Camera.main);

        void Refresh(Camera cam)
        {
            var player = streamer && streamer.player ? streamer.player.GetComponent<CamcorderPlayerController>() : null;
            bool on = Enabled && cam && streamer && !streamer.MenuMode && !(player && player.noclip)
                      && cam.transform.position.y < (streamer.Settings != null ? streamer.Settings.storeyPitch * 0.9f : 8f);
            if (!on)
            {
                foreach (var e in _chunks.Values) if (e.hidden) Show(e);
                HiddenChunks = 0; _dirty = true;
                return;
            }
            var t = cam.transform;
            if (!_dirty && (t.position - _lastEye).sqrMagnitude < 1e-4f && Quaternion.Angle(t.rotation, _lastRot) < 0.2f) return;
            _lastEye = t.position; _lastRot = t.rotation; _dirty = false;
            Compute(cam);
            int hidden = 0, shown = 0;
            foreach (var kv in _chunks)
            {
                var e = kv.Value;
                if (!e.go) continue;
                bool vis = kv.Key.y != 0 || _visible.Contains(new Vector2Int(kv.Key.x, kv.Key.z));
                if (vis && e.hidden) Show(e);
                else if (!vis && !e.hidden) Hide(e);
                if (e.hidden) hidden++; else shown++;
            }
            HiddenChunks = hidden; VisibleChunks = shown;
        }

        // ---------------- Which chunks ----------------

        void Compute(Camera cam)
        {
            _visible.Clear();
            float C = OfficeBiome.CellSize;
            Vector3 eye = cam.transform.position;
            // The eye's chunk and any chunk within reach of it (a body's width, a lens' near plane).
            const float near = 1.5f;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var p = eye + new Vector3(dx * near, 0f, dz * near);
                    _visible.Add(WorldStreamer.ChunkOf(p));
                }

            // The fan: the view's width on the ground, widened as it tilts (looking down sees more to the sides).
            Vector3 f = cam.transform.forward;
            Vector2 fwd = new Vector2(f.x, f.z);
            float pitchCos = fwd.magnitude;
            float vHalf = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float hHalf = Mathf.Atan(Mathf.Tan(vHalf) * cam.aspect) * Mathf.Rad2Deg;
            float half = (hHalf + margin) / Mathf.Max(0.2f, pitchCos);
            if (pitchCos < 0.35f) half = 180f;
            half = Mathf.Min(half, 180f);
            float yaw0 = pitchCos > 1e-4f ? Mathf.Atan2(fwd.x, fwd.y) * Mathf.Rad2Deg : 0f;
            int rays = Mathf.CeilToInt(half * 2f / rayStep);
            float ox = eye.x / C, oz = eye.z / C;
            float maxCells = maxDistance / C;
            _cacheKey = new Vector2Int(int.MinValue, 0);
            for (int i = 0; i <= rays; i++)
            {
                float a = (yaw0 - half + i * (half * 2f / rays)) * Mathf.Deg2Rad;
                March(ox, oz, Mathf.Sin(a), Mathf.Cos(a), maxCells);
            }
        }

        Vector2Int _cacheKey; OfficeLayout _cache;

        OfficeLayout Layout(Vector2Int chunk)
        {
            if (chunk == _cacheKey) return _cache;
            _cacheKey = chunk; _cache = streamer.LayoutAt(chunk);
            return _cache;
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        /// <summary>The edge kind on one side of a global cell (Wall if its chunk is not loaded).</summary>
        bool Solid(int gx, int gz, int side, out bool loaded)
        {
            int n = OfficeBiome.ChunkCells;
            var ck = new Vector2Int(FloorDiv(gx, n), FloorDiv(gz, n));
            var L = Layout(ck);
            loaded = L != null;
            if (L == null) return true;
            int lx = gx - ck.x * n, lz = gz - ck.y * n;
            if (!L.InChunk(lx, lz)) return true;
            if (!OfficeLayout.IsSolid(L.EdgeOf(lx, lz, side))) return false;
            // A broken wall with a hole right through it can be seen through.
            if (Destruction.WallDamage.Count == 0) return true;
            var key = side < 2 ? new Destruction.WallKey(true, side == 0 ? gx : gx + 1, gz, 0) : new Destruction.WallKey(false, side == 2 ? gz : gz + 1, gx, 0);
            return !Destruction.WallDamage.SeeThrough(key);
        }

        /// <summary>Walks one ray over the cells (Amanatides-Woo), marking chunks, until a wall, an unloaded chunk or the range.</summary>
        void March(float ox, float oz, float dx, float dz, float maxCells)
        {
            int n = OfficeBiome.ChunkCells;
            int x = Mathf.FloorToInt(ox), z = Mathf.FloorToInt(oz);
            int sx = dx > 0f ? 1 : -1, sz = dz > 0f ? 1 : -1;
            float tdx = Mathf.Abs(dx) > 1e-6f ? Mathf.Abs(1f / dx) : float.MaxValue;
            float tdz = Mathf.Abs(dz) > 1e-6f ? Mathf.Abs(1f / dz) : float.MaxValue;
            float tx = Mathf.Abs(dx) > 1e-6f ? ((sx > 0 ? x + 1 - ox : ox - x) * tdx) : float.MaxValue;
            float tz = Mathf.Abs(dz) > 1e-6f ? ((sz > 0 ? z + 1 - oz : oz - z) * tdz) : float.MaxValue;
            var lastChunk = new Vector2Int(int.MinValue, 0);
            for (int guard = 0; guard < 400; guard++)
            {
                var ck = new Vector2Int(FloorDiv(x, n), FloorDiv(z, n));
                if (ck != lastChunk) { _visible.Add(ck); lastChunk = ck; }
                bool stepX = tx < tz;
                float t = stepX ? tx : tz;
                if (t > maxCells) return;
                int side = stepX ? (sx > 0 ? 1 : 0) : (sz > 0 ? 3 : 2);
                int nx = stepX ? x + sx : x, nz = stepX ? z : z + sz;
                if (Solid(x, z, side, out bool loaded))
                {
                    // The wall's far face may belong to the next chunk: draw it too.
                    if (loaded) _visible.Add(new Vector2Int(FloorDiv(nx, n), FloorDiv(nz, n)));
                    return;
                }
                x = nx; z = nz;
                if (stepX) tx += tdx; else tz += tdz;
            }
        }

        // ---------------- Hiding ----------------

        void Hide(Entry e)
        {
            e.changed.Clear();
            _scratch.Clear();
            e.go.GetComponentsInChildren(false, _scratch);
            foreach (var r in _scratch)
            {
                if (!r || r.forceRenderingOff) continue;
                var mode = r.shadowCastingMode;
                if (mode == ShadowCastingMode.ShadowsOnly) continue;                       // shadow proxies: as they are
                if (r.GetComponentInParent<Interaction.WorldItem>()) continue;               // items (loot can be carried off)
                if (mode == ShadowCastingMode.Off) { r.forceRenderingOff = true; e.changed.Add((r, mode, true)); }
                else { r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; e.changed.Add((r, mode, false)); }
            }
            e.hidden = true;
        }

        static void Show(Entry e)
        {
            foreach (var (r, mode, off) in e.changed)
            {
                if (!r) continue;
                if (off) r.forceRenderingOff = false;
                else if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) r.shadowCastingMode = mode;
            }
            e.changed.Clear();
            e.hidden = false;
        }
    }
}
