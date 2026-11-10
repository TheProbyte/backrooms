using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Where the chunks built so far put their rarer wear and details (Docs/RealismPlan.md), so F5 > PLACES > WEAR AND
    /// DETAIL can take you to the nearest one instead of searching. Each chunk's list replaces its last one when it is
    /// built again; lists of chunks that have since unloaded stay (the world is the same each visit, so they still hold).
    /// Main thread only.
    /// </summary>
    public static class RealismSightings
    {
        public struct Sighting
        {
            public Vector3 position;   // world: the thing itself
            public Vector3 facing;     // the way it faces into the room (a wall's normal; down for the ceiling; toward open floor for the floor)
            public string kind;
        }

        static readonly Dictionary<Vector3Int, List<Sighting>> s_chunks = new Dictionary<Vector3Int, List<Sighting>>();

        public static void Set(Vector3Int chunk, List<Sighting> list)
        {
            if (list == null || list.Count == 0) { s_chunks.Remove(chunk); return; }
            s_chunks[chunk] = new List<Sighting>(list);
        }

        public static void Clear() => s_chunks.Clear();

        /// <summary>Every sighting of a kind, nearest to a point first.</summary>
        public static List<Sighting> Of(string kind, Vector3 from)
        {
            var all = new List<Sighting>();
            foreach (var kv in s_chunks)
                foreach (var s in kv.Value)
                    if (s.kind == kind) all.Add(s);
            all.Sort((a, b) => (a.position - from).sqrMagnitude.CompareTo((b.position - from).sqrMagnitude));
            return all;
        }
    }
}
