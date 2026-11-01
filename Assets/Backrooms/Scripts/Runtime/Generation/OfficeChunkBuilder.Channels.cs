using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The channels' curving walkways (2026-11-01, the owner: "connections of curves"; planned in PoolPlan.Channels): a
    /// tiled path 1.2 m wide standing in the flooded hall from the pool floor to deck level, its top tiled as the decks are
    /// (a band of accent tiles along each edge, a rounded bullnose over the water), its sides tiled down into the water.
    /// Built whole by the chunk its first end is in.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        readonly List<Vector3> _walkC = new List<Vector3>(96), _walkD = new List<Vector3>(96), _walkMain = new List<Vector3>(96);

        void Walkway(PoolFeature f)
        {
            // The path, about every 25 cm.
            float approx = (f.to - f.centre).magnitude * 1.3f + 1f;
            int K = Mathf.Clamp(Mathf.CeilToInt(approx / 0.25f), 8, 400);
            _walkC.Clear(); _walkD.Clear();
            for (int k = 0; k <= K; k++)
            {
                var p = PoolPlan.WalkwayPoint(f, k / (float)K, out var d);
                _walkC.Add(new Vector3(p.x, 0f, p.y));
                _walkD.Add(new Vector3(d.x, 0f, d.y));
            }
            const float W = PoolPlan.WalkwayHalf, band = 0.14f;
            var cx = Mathf.Clamp(Mathf.FloorToInt(f.centre.x / C + f.size.x * 0.5f), 0, _L.N - 1);
            var cz = Mathf.Clamp(Mathf.FloorToInt(f.centre.y / C + f.size.y * 0.5f), 0, _L.N - 1);
            var deck = WMask(ZoneAt(cx, cz), 1u);
            var accent = WMask(AccentMat, 1u);
            // A branch runs onto the main way at its far end: there it draws no edges (the main way's own run on), and
            // its top a few millimetres low, under the main way's, so the two never fight.
            _walkMain.Clear();
            if (f.shape == 1)
                foreach (var m in _L.poolFeatures)
                    if (m.kind == PoolFeatureKind.Walkway && m.shape == 0 && m.room == f.room)
                        for (int k = 0; k <= 80; k++) { var q = PoolPlan.WalkwayPoint(m, k / 80f, out _); _walkMain.Add(new Vector3(q.x, 0f, q.y)); }
            bool OnMain(Vector3 p)
            {
                for (int k = 0; k < _walkMain.Count; k++) { var d = _walkMain[k] - p; d.y = 0f; if (d.sqrMagnitude < (W - 0.02f) * (W - 0.02f)) return true; }
                return false;
            }
            Vector3 prevL = default, prevR = default, prevS = default;
            float prevFL = 0f, prevFR = 0f;
            for (int k = 0; k <= K; k++)
            {
                var c = _walkC[k];
                var side = Vector3.Cross(Vector3.up, _walkD[k]).normalized;      // to the left
                Vector3 L = c + side * W, R = c - side * W;
                float fL = BasinFloorY(L.x + side.x * 0.1f, L.z + side.z * 0.1f) - 0.02f, fR = BasinFloorY(R.x - side.x * 0.1f, R.z - side.z * 0.1f) - 0.02f;
                bool inL = _walkMain.Count > 0 && OnMain(L) && OnMain(prevL), inR = _walkMain.Count > 0 && OnMain(R) && OnMain(prevR);
                if (k > 0 && inL && inR && OnMain(c))
                {
                    // Wholly on the main way: only a top, just under its.
                    var dn = Vector3.down * 0.004f;
                    deck.Quad(prevL + dn, L + dn, R + dn, prevR + dn, Vector3.up, MeshWriter.MetreUV);
                }
                else if (k > 0)
                {
                    // The top: accent bands along the edges, deck tiles between.
                    var dn = _walkMain.Count > 0 && (inL || inR) ? Vector3.down * 0.004f : Vector3.zero;
                    Vector3 pLi = prevL - prevS * band, pRi = prevR + prevS * band, Li = L - side * band, Ri = R + side * band;
                    (inL ? deck : accent).Quad(prevL + dn, L + dn, Li + dn, pLi + dn, Vector3.up, MeshWriter.MetreUV);
                    deck.Quad(pLi + dn, Li + dn, Ri + dn, pRi + dn, Vector3.up, MeshWriter.MetreUV);
                    (inR ? deck : accent).Quad(pRi + dn, Ri + dn, R + dn, prevR + dn, Vector3.up, MeshWriter.MetreUV);
                    // The bullnose over the water on each edge, and the sides down into it.
                    if (!inL) Nosing(prevL, L, (prevS + side).normalized);
                    if (!inR) Nosing(R, prevR, -(prevS + side).normalized);
                    const float lip = 0.2f;
                    if (!inL) accent.SmoothQuad(prevL + Vector3.down * 0.025f, L + Vector3.down * 0.025f, L + Vector3.down * lip, prevL + Vector3.down * lip, prevS, side, side, prevS,
                        new Vector2(0f, -0.025f), new Vector2((L - prevL).magnitude, -0.025f), new Vector2((L - prevL).magnitude, -lip), new Vector2(0f, -lip));
                    if (!inL) deck.SmoothQuad(prevL + Vector3.down * lip, L + Vector3.down * lip, new Vector3(L.x, fL, L.z), new Vector3(prevL.x, prevFL, prevL.z), prevS, side, side, prevS,
                        MeshUV(prevL, prevS, -lip), MeshUV(L, side, -lip), MeshUV(L, side, fL), MeshUV(prevL, prevS, prevFL));
                    if (!inR) accent.SmoothQuad(R + Vector3.down * 0.025f, prevR + Vector3.down * 0.025f, prevR + Vector3.down * lip, R + Vector3.down * lip, -side, -prevS, -prevS, -side,
                        new Vector2(0f, -0.025f), new Vector2((R - prevR).magnitude, -0.025f), new Vector2((R - prevR).magnitude, -lip), new Vector2(0f, -lip));
                    if (!inR) deck.SmoothQuad(R + Vector3.down * lip, prevR + Vector3.down * lip, new Vector3(prevR.x, prevFR, prevR.z), new Vector3(R.x, fR, R.z), -side, -prevS, -prevS, -side,
                        MeshUV(R, -side, -lip), MeshUV(prevR, -prevS, -lip), MeshUV(prevR, -prevS, prevFR), MeshUV(R, -side, fR));
                    // Its body, from the pool floor to the deck.
                    var mid = (prevL + prevR + L + R) * 0.25f;
                    var along = (c - _walkC[k - 1]);
                    float floor = Mathf.Min(Mathf.Min(fL, fR), Mathf.Min(prevFL, prevFR));
                    _rotBoxes.Add((new Vector3(mid.x, floor * 0.5f, mid.z), new Vector3(2f * W, -floor, along.magnitude + 0.06f), Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg));
                }
                prevL = L; prevR = R; prevS = side; prevFL = fL; prevFR = fR;
            }
            // A branch's far end lies on the other walkway; the main way's ends are at doorways (the deck goes on there).
            // Where an end stands in water (a doorway onto a flooded channel), it is closed with a tiled end.
            for (int e = 0; e < 2; e++)
            {
                if (e == 1 && f.shape == 1) continue;
                var c = _walkC[e == 0 ? 0 : K];
                var d = _walkD[e == 0 ? 0 : K] * (e == 0 ? -1f : 1f);
                var probe = c + d * 0.3f;
                if (!_L.WaterAtPoint(probe.x, probe.z)) continue;
                var side = Vector3.Cross(Vector3.up, d).normalized;
                Vector3 a = c - side * W, b = c + side * W;
                float fa = BasinFloorY(a.x, a.z) - 0.02f, fb = BasinFloorY(b.x, b.z) - 0.02f;
                deck.Quad(new Vector3(a.x, fa, a.z), new Vector3(b.x, fb, b.z), b, a, d, MeshWriter.MetreUV);
                Nosing(a, b, d);
            }
        }

        static Vector2 MeshUV(Vector3 p, Vector3 n, float y) => MeshWriter.MetreUV(new Vector3(p.x, y, p.z), n);
    }
}
