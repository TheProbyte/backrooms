using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Stable 64-bit fingerprint of everything in a chunk layout (cells, edges, rooms, fixtures, dressing).
    /// Two layouts with the same digest build the same geometry. Used by determinism tests.
    /// </summary>
    public static class LayoutDigest
    {
        public static ulong Of(OfficeLayout L)
        {
            var h = new Fnv();
            h.Add(L.N); h.Add(L.coord.x); h.Add(L.coord.y);
            for (int x = 0; x < L.N; x++) for (int z = 0; z < L.N; z++) h.Add(L.roomOf[x, z]);
            foreach (var e in L.vEdges) h.Add((int)e);
            foreach (var e in L.hEdges) h.Add((int)e);
            foreach (var r in L.rooms)
            {
                h.Add(r.id); h.Add(r.rect.x); h.Add(r.rect.y); h.Add(r.rect.width); h.Add(r.rect.height);
                h.Add((int)r.kind); h.Add((int)r.lights); h.Add(r.color); h.Add(r.ceiling); h.Add(r.painted ? 1 : 0);
            }
            foreach (var f in L.fixtures)
            {
                h.Add(f.cell.x); h.Add(f.cell.y); h.Add(f.on ? 1 : 0); h.Add(f.roomColor); h.Add(f.dome ? 1 : 0);
                h.Add(f.strip ? 1 : 0); h.Add(f.height); h.Add(f.faulty ? 1 : 0);
            }
            foreach (var p in L.pillars) { h.Add(p.vertex.x); h.Add(p.vertex.y); h.Add(p.size); }
            foreach (var w in L.halfWalls) { h.Add(w.alongX ? 1 : 0); h.Add(w.line); h.Add(w.start); h.Add(w.length); }
            foreach (var p in L.plates) { h.Add(p.cell.x); h.Add(p.cell.y); h.Add(p.side); h.Add(p.t); h.Add(p.height); h.Add(p.isSwitch ? 1 : 0); }
            foreach (var p in L.props)
            {
                h.Add((int)p.kind); h.Add(p.position.x); h.Add(p.position.y); h.Add(p.yaw);
                h.Add(p.size.x); h.Add(p.size.y); h.Add(p.size.z); h.Add(p.variant); h.Add(p.lit ? 1 : 0);
            }
            return h.Value;
        }

        struct Fnv
        {
            ulong _h;
            public ulong Value => _h == 0 ? 14695981039346656037UL : _h;
            public void Add(int v)
            {
                if (_h == 0) _h = 14695981039346656037UL;
                unchecked { for (int i = 0; i < 4; i++) { _h ^= (byte)(v >> (i * 8)); _h *= 1099511628211UL; } }
            }
            public void Add(float v) => Add(System.BitConverter.SingleToInt32Bits(v));
        }
    }
}
