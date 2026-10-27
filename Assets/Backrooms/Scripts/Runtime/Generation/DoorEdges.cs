using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The doorways whose door is shut right now. A shut door's edge counts as a wall for light (the light grid's
    /// occupancy, so the bounce field and the real lights' wall test both stop at it) and for sound (the building
    /// audio's paths, and Steam Audio's geometry). An edge is named in world cells: vertical edges lie on the line
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

        public static void Clear() { s_shut.Clear(); s_edges.Clear(); Version++; }
    }
}
