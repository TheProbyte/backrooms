using System.Collections.Generic;
using Backrooms.Generation;
using Backrooms.Interaction;
using UnityEngine;

namespace Backrooms.Audio
{
    /// <summary>
    /// A building at rest is never silent: every so often a ceiling tile shifts in its grid somewhere near you (a dry
    /// crackle above, a few metres off), or a piece of furniture nearby settles with a small knock. They come from real
    /// places (a drop ceiling over a room within reach, a sofa or table that is there) and go through Steam Audio, so one
    /// in the next room is heard through the wall. Also tells the marks shader where air comes out of vents (the crawl
    /// holes near the camera), for the ends of tape strips to flutter.
    /// </summary>
    public class BuildingSettling : MonoBehaviour
    {
        public WorldStreamer streamer;
        [Tooltip("Seconds between settling sounds (random in this range).")] public Vector2 interval = new Vector2(10f, 35f);

        readonly List<AudioSource> _pool = new List<AudioSource>();
        readonly List<MovableProp> _props = new List<MovableProp>();
        readonly Vector4[] _air = new Vector4[8];
        static readonly int AirId = Shader.PropertyToID("_MarksAir"), AirCountId = Shader.PropertyToID("_MarksAirCount");
        float _next, _nextProps, _nextAir;
        int _pick;

        void Start()
        {
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Settling" + i);
                go.transform.SetParent(transform, false);
                _pool.Add(ItemModels.Source(go, 18f));
            }
            _next = Time.time + Random.Range(6f, 15f);
        }

        void Update()
        {
            var cam = Camera.main;
            if (!cam) return;
            Vector3 ear = cam.transform.position;
            if (Time.time >= _nextAir) { _nextAir = Time.time + 0.5f; UpdateAir(ear); }
            if (streamer && streamer.MenuMode) return;
            if (Time.time < _next) return;
            _next = Time.time + Random.Range(interval.x, interval.y);
            if (Random.value < 0.35f && Tick(ear)) return;
            Creak(ear);
        }

        AudioSource Free()
        {
            foreach (var s in _pool) if (!s.isPlaying) return s;
            return _pool[_pick++ % _pool.Count];
        }

        /// <summary>A ceiling tile over a room near you (not in plain sight above your head: 2.5 to 11 m away).</summary>
        bool Creak(Vector3 ear)
        {
            if (!streamer) return false;
            for (int tries = 0; tries < 8; tries++)
            {
                Vector2 r = Random.insideUnitCircle.normalized * Random.Range(2.5f, 11f);
                Vector3 p = ear + new Vector3(r.x, 0f, r.y);
                var L = streamer.LayoutAt(WorldStreamer.ChunkOf(p));
                if (L == null || L.style == ZoneStyle.Pool) continue;
                int x = Mathf.FloorToInt(p.x / OfficeBiome.CellSize) - L.coord.x * L.N, z = Mathf.FloorToInt(p.z / OfficeBiome.CellSize) - L.coord.y * L.N;
                if (!L.InChunk(x, z)) continue;
                var room = L.RoomAt(x, z);
                if (room.Tall || room.openAbove || room.noFloor) continue;                    // a drop ceiling only
                p.y = L.BaseY + room.ceiling - 0.03f;
                var s = Free();
                s.transform.position = p;
                s.pitch = Random.Range(0.9f, 1.12f);
                s.PlayOneShot(SoundBank.CeilingCreak(Random.Range(0, 4)), Random.Range(0.25f, 0.45f));
                return true;
            }
            return false;
        }

        /// <summary>A sofa, chair or table near you settles.</summary>
        bool Tick(Vector3 ear)
        {
            if (Time.time >= _nextProps)
            {
                _nextProps = Time.time + 5f;
                _props.Clear();
                foreach (var p in FindObjectsByType<MovableProp>(FindObjectsSortMode.None))
                    if ((p.transform.position - ear).sqrMagnitude < 10f * 10f && p.Spec != null && p.Spec.kind != MovableKind.Box) _props.Add(p);
            }
            if (_props.Count == 0) return false;
            var prop = _props[Random.Range(0, _props.Count)];
            if (!prop || prop.held) return false;
            var s = Free();
            s.transform.position = prop.transform.position + Vector3.up * 0.35f;
            s.pitch = Random.Range(0.85f, 1.15f);
            s.PlayOneShot(SoundBank.FurnitureTick(Random.Range(0, 4)), Random.Range(0.22f, 0.38f));
            return true;
        }

        /// <summary>The nearest crawl holes (vents) to the camera: where air moves, for tape ends to flutter.</summary>
        void UpdateAir(Vector3 ear)
        {
            int n = 0;
            var all = CrawlPortalView.All;
            for (int i = 0; i < all.Count && n < _air.Length; i++)
            {
                var v = all[i];
                if (!v) continue;
                Vector3 p = v.transform.position;
                if ((p - ear).sqrMagnitude > 12f * 12f) continue;
                _air[n++] = new Vector4(p.x, p.y, p.z, 1f);
            }
            for (int i = n; i < _air.Length; i++) _air[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(AirId, _air);
            Shader.SetGlobalFloat(AirCountId, n);
        }
    }
}
