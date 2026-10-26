using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A place a plug goes: one of the two sockets of a wall outlet, or one of the three on an extension cord's box.
    /// Wall sockets are fixed in the world and powered when their zone has power (Level 0 does, the Poolrooms do not);
    /// a box's sockets are powered when its own plug is in a powered socket.
    /// </summary>
    public class Socket
    {
        public Transform anchor;            // null: a wall socket (position and normal are in world space)
        public Vector3 local, localNormal;
        public CordBox box;                 // the extension cord it belongs to, or null (wall)
        public bool wallPowered;
        public bool dead;                   // its chunk unloaded
        public Plug occupant;

        public bool IsWall => box == null;
        public Vector3 Position => anchor ? anchor.TransformPoint(local) : local;
        public Vector3 Normal => anchor ? anchor.TransformDirection(localNormal) : localNormal;
        public bool Powered => !dead && (box ? box.Powered : wallPowered);
        /// <summary>Extension cords between this socket and the wall.</summary>
        public int Depth => box ? box.Depth : 0;

        /// <summary>How a plug sits in it: prongs (the plug's forward) into the socket, the plug's up along the wall's up.</summary>
        public Quaternion PlugRotation
        {
            get
            {
                Vector3 n = Normal;
                Vector3 up = Mathf.Abs(n.y) > 0.9f ? (anchor ? anchor.forward : Vector3.forward) : Vector3.up;
                return Quaternion.LookRotation(-n, up);
            }
        }
    }

    /// <summary>Every socket that is loaded: wall outlets per chunk, and the boxes of extension cords.</summary>
    public static class Sockets
    {
        static readonly Dictionary<Vector3Int, List<Socket>> s_wall = new Dictionary<Vector3Int, List<Socket>>();
        static readonly List<Socket> s_boxes = new List<Socket>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { s_wall.Clear(); s_boxes.Clear(); }

        public static void AddChunk(Vector3Int key, List<Socket> list) { s_wall[key] = list; }

        public static void RemoveChunk(Vector3Int key, List<Socket> list)
        {
            foreach (var s in list) s.dead = true;
            if (s_wall.TryGetValue(key, out var have) && have == list) s_wall.Remove(key);   // a rebuilt chunk may have registered already
        }

        public static void AddBox(Socket s) { if (!s_boxes.Contains(s)) s_boxes.Add(s); }
        public static void RemoveBox(Socket s) { s_boxes.Remove(s); }

        public static int WallCount { get { int n = 0; foreach (var l in s_wall.Values) n += l.Count; return n; } }

        static IEnumerable<Socket> Near(Vector3 p)
        {
            var c = WorldStreamer.ChunkOf(p);
            foreach (var kv in s_wall)
                if (Mathf.Abs(kv.Key.x - c.x) <= 1 && Mathf.Abs(kv.Key.z - c.y) <= 1)
                    foreach (var s in kv.Value) yield return s;
            foreach (var s in s_boxes) yield return s;
        }

        /// <summary>The free socket nearest a point (within maxDistance), or null.</summary>
        public static Socket Nearest(Vector3 p, float maxDistance, CordBox except = null)
        {
            Socket best = null; float bd = maxDistance * maxDistance;
            foreach (var s in Near(p))
            {
                if (s.occupant != null || s.dead || (except && s.box == except)) continue;
                float d = (s.Position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        /// <summary>The loaded wall socket at a saved position (a plug left in it while its chunk was away), or null.</summary>
        public static Socket WallAt(Vector3 p, float tolerance = 0.01f)
        {
            var c = WorldStreamer.ChunkOf(p);
            foreach (var kv in s_wall)
            {
                if (kv.Key.x != c.x || kv.Key.z != c.y) continue;
                foreach (var s in kv.Value) if ((s.local - p).sqrMagnitude < tolerance * tolerance) return s;
            }
            return null;
        }

        /// <summary>The wall outlet nearest a point (for the F5 debug places), or false.</summary>
        public static bool NearestWall(Vector3 p, out Vector3 position, out Vector3 normal)
        {
            position = normal = default;
            float bd = float.MaxValue;
            foreach (var l in s_wall.Values)
                foreach (var s in l)
                {
                    float d = (s.local - p).sqrMagnitude;
                    if (d < bd) { bd = d; position = s.local; normal = s.localNormal; }
                }
            return bd < float.MaxValue;
        }
    }

    /// <summary>A chunk's wall outlets (two sockets each), registered while the chunk is loaded. Added by the chunk builder.</summary>
    public class ChunkOutlets : MonoBehaviour
    {
        Vector3Int _key;
        readonly List<Socket> _sockets = new List<Socket>();
        bool _registered;

        /// <summary>Outlets at world positions (the face of the plate) facing out of their walls.</summary>
        public void Set(Vector3Int chunk, List<(Vector3 pos, Vector3 normal)> outlets, bool powered)
        {
            _key = chunk;
            _sockets.Clear();
            foreach (var (pos, n) in outlets)
                for (int k = -1; k <= 1; k += 2)
                    _sockets.Add(new Socket { local = pos + Vector3.up * (0.019f * k), localNormal = n, wallPowered = powered });
            Sockets.AddChunk(_key, _sockets);
            _registered = true;
        }

        void OnDestroy()
        {
            if (!_registered) return;
            // A plug left in one of these keeps its place; it finds the socket again when the chunk comes back.
            foreach (var s in _sockets) if (s.occupant) s.occupant.SocketLost();
            Sockets.RemoveChunk(_key, _sockets);
        }
    }
}
