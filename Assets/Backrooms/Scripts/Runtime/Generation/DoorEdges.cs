using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The doorways whose door is shut right now. A shut door's edge counts as a wall for sound (the building audio's
    /// paths, and Steam Audio's geometry); for light (the light grid's occupancy, so the bounce field and the real
    /// lights' wall test) a door is graded by how far it is open (<see cref="LightOpenness"/>, 2026-11-04). An edge is named in world cells: vertical edges lie on the line
    /// x = line (cells), at z = along; the others on z = line, at x = along.
    /// </summary>
    public static class DoorEdges
    {
        public struct Edge
        {
            public bool vertical;
            public int line, along, storey;
            public Vector3Int chunk;     // the chunk the door belongs to (whose light grid encoding changes)
            public Vector3 centre;       // the opening's middle (world), for redrawing the light around it
        }

        static readonly HashSet<(bool, int, int, int)> s_shut = new HashSet<(bool, int, int, int)>();
        static readonly Dictionary<(bool, int, int, int), Edge> s_edges = new Dictionary<(bool, int, int, int), Edge>();

        /// <summary>Goes up whenever a door is shut or opened.</summary>
        public static int Version { get; private set; }
        /// <summary>A door was shut or opened (light grids re-encode that chunk and redraw around it).</summary>
        public static event System.Action<Edge, bool> Changed;

        public static bool IsShut(bool vertical, int line, int along, int storey) =>
            s_shut.Count > 0 && s_shut.Contains((vertical, line, along, storey));

        public static int Count => s_shut.Count;
        public static IEnumerable<Edge> Shut => s_edges.Values;

        public static void Set(Edge e, bool shut)
        {
            var key = (e.vertical, e.line, e.along, e.storey);
            bool was = s_shut.Contains(key);
            if (was == shut) return;
            if (shut) { s_shut.Add(key); s_edges[key] = e; }
            else { s_shut.Remove(key); s_edges.Remove(key); }
            Version++;
            Changed?.Invoke(e, shut);
        }

        public static void Clear() { s_shut.Clear(); s_edges.Clear(); s_light.Clear(); Version++; LightVersion++; }

        // ---------------- Light (2026-11-04) ----------------
        // Light has its own state, graded: a door lets light by in proportion to how far it is open (the light grid
        // narrows the opening to that share of its height), where sound only knows shut or open. Doors not listed are
        // fully open to light.

        static readonly Dictionary<(bool, int, int, int), float> s_light = new Dictionary<(bool, int, int, int), float>();

        /// <summary>Goes up whenever a door's openness to light changes.</summary>
        public static int LightVersion { get; private set; }
        /// <summary>A door's openness to light changed (light grids re-encode that chunk and redraw around it).</summary>
        public static event System.Action<Edge, float> LightChanged;
        public static int LightCount => s_light.Count;

        /// <summary>How open the doorway is to light: 0 shut (a wall), up to 1 (no door, or wide open). An edge shut
        /// (<see cref="Set"/>) with no light state of its own is shut to light too.</summary>
        public static float LightOpenness(bool vertical, int line, int along, int storey)
        {
            var key = (vertical, line, along, storey);
            if (s_light.Count > 0 && s_light.TryGetValue(key, out var o)) return o;
            return s_shut.Count > 0 && s_shut.Contains(key) ? 0f : 1f;
        }

        public static void SetLight(Edge e, float openness)
        {
            var key = (e.vertical, e.line, e.along, e.storey);
            openness = Mathf.Clamp01(openness);
            float was = s_light.TryGetValue(key, out var w) ? w : 1f;
            if (Mathf.Abs(was - openness) < 1e-4f) return;
            if (openness >= 1f) s_light.Remove(key); else s_light[key] = openness;
            LightVersion++;
            LightChanged?.Invoke(e, openness);
        }

        public static void ClearLight(Edge e)
        {
            var key = (e.vertical, e.line, e.along, e.storey);
            if (s_light.Remove(key)) { LightVersion++; LightChanged?.Invoke(e, 1f); }
        }
    }
}
