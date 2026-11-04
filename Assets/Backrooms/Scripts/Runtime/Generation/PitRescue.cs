using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Falling into a pit room's shaft (DepthPlan.PitDepth deep, black long before its floor): once your feet are well
    /// down in one you are put back where you last stood on the walkways, facing the way you were. A stand-in until
    /// falling in means something (a death, or rooms below). Pit rooms are only on the ground storey. Added to the player
    /// by the WorldStreamer.
    /// </summary>
    public class PitRescue : MonoBehaviour
    {
        public WorldStreamer streamer;
        CamcorderPlayerController _pc;
        Vector3 _safe;
        bool _haveSafe;

        /// <summary>How far down a shaft the feet go before you are brought back (m): already in the dark.</summary>
        const float Catch = 1.7f;

        void Update()
        {
            if (!_pc) _pc = GetComponent<CamcorderPlayerController>();
            if (!_pc || !streamer) return;
            var p = transform.position;
            // Standing on a floor no lower than the deepest sunken room: somewhere to come back to.
            // (Not over a shaft: held in the air there after a teleport counts as grounded.)
            if (_pc.IsGrounded && p.y > -0.95f && p.y < 1f && !InShaft(p, out _, out _)) { _safe = p; _haveSafe = true; }
            if (p.y > -Catch || p.y < -DepthPlan.PitDepth - 2f) return;
            if (!InShaft(p, out var rect, out var origin)) return;
            Vector3 to = _haveSafe && (new Vector2(_safe.x - p.x, _safe.z - p.z)).sqrMagnitude < 12f * 12f ? _safe : Edge(p, rect, origin);
            _pc.Teleport(to + Vector3.up * 0.05f, transform.eulerAngles.y, 0f);
        }

        bool InShaft(Vector3 p, out Rect rect, out Vector3 origin)
        {
            rect = default; origin = default;
            var c = WorldStreamer.ChunkOf(p);
            var L = streamer.LayoutAt(new Vector3Int(c.x, 0, c.y));
            if (L == null || L.pits.Count == 0) return false;
            origin = new Vector3(c.x * OfficeBiome.ChunkSize, 0f, c.y * OfficeBiome.ChunkSize);
            var local = new Vector2(p.x - origin.x, p.z - origin.z);
            foreach (var s in L.pits)
                if (s.rect.Contains(local)) { rect = s.rect; return true; }
            return false;
        }

        /// <summary>The nearest point on the walkway round a shaft (a little past its nearest edge).</summary>
        static Vector3 Edge(Vector3 p, Rect r, Vector3 origin)
        {
            float x = p.x - origin.x, z = p.z - origin.z;
            float dl = x - r.xMin, dr = r.xMax - x, db = z - r.yMin, dt = r.yMax - z;
            float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
            const float past = 0.25f;
            if (m == dl) x = r.xMin - past; else if (m == dr) x = r.xMax + past; else if (m == db) z = r.yMin - past; else z = r.yMax + past;
            return new Vector3(origin.x + x, 0f, origin.z + z);
        }
    }
}
