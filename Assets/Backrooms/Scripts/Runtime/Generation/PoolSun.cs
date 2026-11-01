using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>What the sun test needs to know about one cell of the Poolrooms.</summary>
    public struct PoolCellInfo
    {
        public bool valid;               // a loaded Poolrooms cell
        public float ceiling;            // above the deck
        public SkylightShape sky;
        public int joined;               // see PoolBiome.Joined
        public byte west, south;         // edge on the cell's -X and -Z side: 0 open, 1 wall, 2 headed doorway
        public bool basin;
        public bool pillar;              // a round column stands on the cell's -X -Z corner
        public bool pillarNear;          // a column stands on one of the cell's four corners
    }

    public interface IPoolCells { PoolCellInfo Get(int x, int z); }

    /// <summary>
    /// The sun in the Poolrooms is exact geometry, not a shadow map: a ray from a point toward the sun walks the cell grid
    /// (walls, door headers, lower ceilings and round columns stop it) until it reaches the ceiling, and it is sunlit if it
    /// gets there inside a roof opening and also leaves the light well above it. Same algorithm as the shaders
    /// (Shaders/PoolCommon.hlsl, PoolSun); this copy feeds the bounce light baked per cell. Unlimited range, no shadow
    /// passes, identical in portal views.
    /// </summary>
    public static class PoolSun
    {
        const float C = OfficeBiome.CellSize;

        public const byte Open = 0, Wall = 1, Header = 2, Window = 3;

        /// <summary>A wall (with or without a high window in it).</summary>
        public static bool Solid(byte e) => e == Wall || e == Window;

        /// <summary>Sunlight reaching point p (metres in the grid's frame, y above the deck): 0 = shade, 1 = full sun.</summary>
        public static float Visibility(IPoolCells g, Vector3 p)
        {
            Vector3 L = PoolBiome.SunDirection;
            int cx = Mathf.FloorToInt(p.x / C), cz = Mathf.FloorToInt(p.z / C);
            float t = 0f;
            var c = g.Get(cx, cz);
            for (int i = 0; i < 24; i++)
            {
                if (!c.valid) return 0f;
                float tCeil = (c.ceiling - p.y) / L.y;
                float tx = L.x > 0f ? ((cx + 1) * C - p.x) / L.x : L.x < 0f ? (cx * C - p.x) / L.x : float.MaxValue;
                float tz = L.z > 0f ? ((cz + 1) * C - p.z) / L.z : L.z < 0f ? (cz * C - p.z) / L.z : float.MaxValue;
                float tExit = Mathf.Min(tx, tz);
                if (c.pillarNear && PillarHit(g, cx, cz, p, L, t, Mathf.Min(tExit, tCeil))) return 0f;
                if (tCeil <= tExit)
                {
                    var q = p + L * tCeil;
                    if (!PoolBiome.InHole(c.sky, c.joined, q.x - cx * C, q.z - cz * C)) return 0f;
                    var q2 = p + L * (tCeil + PoolBiome.ShaftHeight / L.y);
                    int qx = Mathf.FloorToInt(q2.x / C), qz = Mathf.FloorToInt(q2.z / C);
                    var c2 = qx == cx && qz == cz ? c : g.Get(qx, qz);
                    return c2.valid && c2.sky == c.sky && PoolBiome.InHole(c2.sky, c2.joined, q2.x - qx * C, q2.z - qz * C) ? 1f : 0f;
                }
                float y = p.y + L.y * tExit;
                int nx = cx, nz = cz;
                if (tx <= tz) nx += L.x > 0f ? 1 : -1; else nz += L.z > 0f ? 1 : -1;
                var n = g.Get(nx, nz);
                byte edge = tx <= tz ? (L.x > 0f ? n.west : c.west) : (L.z > 0f ? n.south : c.south);
                if (edge == Window)
                {
                    // A high window: the sky is beyond it.
                    float along = tx <= tz ? p.z + L.z * tExit - cz * C : p.x + L.x * tExit - cx * C;
                    return PoolBiome.WindowSD(along, y, c.ceiling, n.ceiling) > 0f ? 1f : 0f;
                }
                if (edge == Wall || (edge == Header && y > PoolBiome.HeaderHeight)) return 0f;
                if (!n.valid || y >= n.ceiling) return 0f;
                cx = nx; cz = nz; c = n; t = tExit;
            }
            return 0f;
        }

        /// <summary>Does the ray segment [t0, t1] pass through one of the columns on this cell's corners?</summary>
        static bool PillarHit(IPoolCells g, int cx, int cz, Vector3 p, Vector3 L, float t0, float t1)
        {
            float r = PoolBiome.PillarRadius;
            for (int k = 0; k < 4; k++)
            {
                int vx = cx + (k & 1), vz = cz + (k >> 1);
                if (!g.Get(vx, vz).pillar) continue;
                float dx = p.x - vx * C, dz = p.z - vz * C;
                float a = L.x * L.x + L.z * L.z, b = dx * L.x + dz * L.z, cc = dx * dx + dz * dz - r * r;
                float disc = b * b - a * cc;
                if (disc < 0f) continue;
                float s = Mathf.Sqrt(disc);
                float tin = (-b - s) / a, tout = (-b + s) / a;
                if (tout > t0 && tin < t1 && tout > 1e-3f) return true;
            }
            return false;
        }
    }
}
