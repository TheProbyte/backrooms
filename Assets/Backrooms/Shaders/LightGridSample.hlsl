// Samples the Backrooms light grid for a surface point. Included by Backrooms/Lit's forward pass.
//
// Wall-aware sampling: walls lie on 1.2 m cell edges, which are texel edges of the 0.6 m grid. The sample
// point is pushed 0.3 m along the normal (off the surface, into the room), then clamped so it never gets
// closer than half a texel to a solid edge of its cell (or to the floor, ceiling or a header). Hardware
// trilinear filtering then only blends texels on the room's own side: no light leaks through a wall,
// while open edges and doorways still blend smoothly.
#ifndef BACKROOMS_LIGHT_GRID_SAMPLE
#define BACKROOMS_LIGHT_GRID_SAMPLE

#include "LightGridCommon.hlsl"

TEXTURE3D(_LGridA);
TEXTURE3D(_LGridB);
SAMPLER(sampler_LGridA);
TEXTURE2D(_LGridBounce);
SAMPLER(sampler_LGridBounce);
StructuredBuffer<uint> _LGridOcc;      // [storey][z][x], toroidal

float4 _LGridSize;      // texels x, y, z, occupancy cells
float4 _LGridParams;    // x: on (0/1), y: direct gain, z: bounce gain, w: storey pitch
float4 _LGridWindow;    // world xz min, xz max of the valid window
float4 _LGridShape;     // x: directional wrap (0..1), y: normal offset, z, w: unused

uint LGrid_Occ(int2 cell, uint storey)
{
    int n = (int)_LGridSize.w;                  // a power of two: wrap with a mask
    uint2 w = uint2(cell.x & (n - 1), cell.y & (n - 1));
    return _LGridOcc[(storey * (uint)n + w.y) * (uint)n + w.x];
}

// Keeps p inside its cell's open sides (x/z) and between floor and ceiling (y).
float3 LGrid_Clamp(float3 p, float pitch)
{
    int2 cell = (int2)floor(p.xz / LG_CELL);
    uint storey = p.y >= pitch ? 1u : 0u;
    uint o = LGrid_Occ(cell, storey);
    if (!LG_Loaded(o)) return p;
    uint oe = LGrid_Occ(cell + int2(1, 0), storey);
    uint on = LGrid_Occ(cell + int2(0, 1), storey);
    float2 lo = cell * LG_CELL + 0.5 * LG_TEXEL, hi = cell * LG_CELL + 1.5 * LG_TEXEL;
    if (LG_WestKind(o) == LG_EDGE_WALL) p.x = max(p.x, lo.x);
    if (LG_WestKind(oe) == LG_EDGE_WALL) p.x = min(p.x, hi.x);
    if (LG_SouthKind(o) == LG_EDGE_WALL) p.z = max(p.z, lo.y);
    if (LG_SouthKind(on) == LG_EDGE_WALL) p.z = min(p.z, hi.y);
    float baseY = storey * pitch;
    p.y = clamp(p.y, baseY + 0.5 * LG_TEXEL, baseY + max(LG_Ceiling(o) - 0.5 * LG_TEXEL, 0.5 * LG_TEXEL));
    return p;
}

half3 BackroomsLightGrid(float3 positionWS, half3 normalWS)
{
    if (_LGridParams.x < 0.5) return 0;
    float pitch = _LGridParams.w;
    float3 p = LGrid_Clamp(positionWS + normalWS * _LGridShape.y, pitch);
    // Fade out at the edge of the window (it is far away, in fog).
    float2 inside = min(p.xz - _LGridWindow.xy, _LGridWindow.zw - p.xz);
    float edge = saturate(min(inside.x, inside.y) / 4.0);
    if (edge <= 0.0) return 0;

    float3 uvw = float3(p.x / (LG_TEXEL * _LGridSize.x), p.y / (LG_TEXEL * _LGridSize.y), p.z / (LG_TEXEL * _LGridSize.z));
    float4 a = SAMPLE_TEXTURE3D_LOD(_LGridA, sampler_LGridA, uvw, 0);
    float4 b = SAMPLE_TEXTURE3D_LOD(_LGridB, sampler_LGridA, uvw, 0);
    float3 E = a.rgb;
    float3 V = float3(b.x, a.w, b.y);
    float lum = max(dot(E, float3(0.2126, 0.7152, 0.0722)), 1e-5);
    float len = length(V);
    float3 dir = len > 1e-6 ? V / len : float3(0, 1, 0);
    // One light: plain Lambert. Light from many directions: tends to half, whatever the normal.
    float spread = saturate(len / lum);
    float wrapped = saturate((dot(normalWS, dir) + _LGridShape.x) / (1.0 + _LGridShape.x));
    float facing = lerp(0.5, wrapped, spread);
    // Ceiling fittings do not light their own ceiling plane (the sample point sits half a texel below it,
    // under the fitting); ceilings get their light from the bounce term.
    float notCeiling = saturate(1.0 + 1.25 * normalWS.y);
    half3 direct = (half3)(E * facing * notCeiling * _LGridParams.y);

    // Bounce: light that hit the floor nearby, mostly lighting ceilings and upper walls.
    half3 bounce = 0;
    int2 bcell = (int2)floor(p.xz / LG_CELL);
    uint bocc = LGrid_Occ(bcell, 0u);
    // Ground storey, or the upper part of a well (its ceiling is lit from the well's floor).
    if (p.y < pitch || (bocc & LG_OPENABOVE) != 0u)
    {
        // Bounce is stored per 1.2 m cell: keep the sample at the cell centre on sides that are walls.
        float2 q = p.xz;
        int2 cell = bcell;
        uint o = bocc, oe = LGrid_Occ(cell + int2(1, 0), 0u), on = LGrid_Occ(cell + int2(0, 1), 0u);
        // Bounce is stored per 1.2 m cell and blends smoothly with its neighbours: keep the sample at the cell centre on every side that is not open floor (walls, doors and arches alike), so a lit room never bleeds into a dark one.
        float2 mid = (cell + 0.5) * LG_CELL;
        if (LG_WestKind(o) != LG_EDGE_OPEN) q.x = max(q.x, mid.x);
        if (LG_WestKind(oe) != LG_EDGE_OPEN) q.x = min(q.x, mid.x);
        if (LG_SouthKind(o) != LG_EDGE_OPEN) q.y = max(q.y, mid.y);
        if (LG_SouthKind(on) != LG_EDGE_OPEN) q.y = min(q.y, mid.y);
        float2 buv = (q - 0.5 * LG_CELL) / (LG_CELL * _LGridSize.w) + 0.5 / _LGridSize.w;
        float3 B = SAMPLE_TEXTURE2D_LOD(_LGridBounce, sampler_LGridBounce, buv, 0).rgb;
        // Light from the floor reaches the ceiling and upper walls; floors get very little, and tall
        // ceilings less than low ones (as the old upward bounce lights did).
        float ceilingH = max(LG_Ceiling(o), 2.0);
        float h = saturate(p.y / ceilingH);
        float wallTerm = lerp(0.12, 0.55, h);
        float facing = normalWS.y < 0.0 ? lerp(wallTerm, 1.0, -normalWS.y) : lerp(wallTerm, 0.04, normalWS.y);
        float height = 3.0 / max(3.0, ceilingH);
        bounce = (half3)(B * _LGridParams.z * facing * height);
    }
    return (direct + bounce) * edge;
}

// ---------------- Real lights: wall line of sight ----------------
// The nearest fixtures are real URP lights whose shadow maps stop them at walls. Shadow maps of wide spot
// lights are coarse (a few cm to over 10 cm per texel), so light crept around wall ends and into corners
// in stair-stepped bands. Walls lie on cell edges and the occupancy buffer knows them exactly, so each
// real light is also tested here: the line from the surface to the light, crossing cell edges, must not
// pass a wall, a door header (by height) or a floor (except down a well). The same test the grid uses.
half LGrid_LightVisible(float3 P, float3 N, float3 L)
{
    if (_LGridParams.x < 0.5) return 1;
    float pitch = _LGridParams.w;
    // Just off the surface, so a wall face counts as being on its own room's side.
    P += N * 0.06;
    if (any(P.xz < _LGridWindow.xy) || any(P.xz > _LGridWindow.zw)) return 1;   // no layout data out there
    uint sp = P.y >= pitch ? 1u : 0u, sl = L.y >= pitch ? 1u : 0u;
    if (sp != sl)
    {
        float ts = (pitch - P.y) / (L.y - P.y);
        float3 X = lerp(P, L, ts);
        if ((LGrid_Occ((int2)floor(X.xz / LG_CELL), 0u) & LG_OPENABOVE) == 0u) return 0;
    }
    float2 a = P.xz / LG_CELL, b = L.xz / LG_CELL;
    int2 c = (int2)floor(a), e = (int2)floor(b);
    float2 d = b - a;
    int2 stp = int2(d.x > 0 ? 1 : (d.x < 0 ? -1 : 0), d.y > 0 ? 1 : (d.y < 0 ? -1 : 0));
    float2 tDelta = float2(stp.x != 0 ? abs(1.0 / d.x) : 1e30, stp.y != 0 ? abs(1.0 / d.y) : 1e30);
    float2 tMax = float2(stp.x > 0 ? (c.x + 1 - a.x) * tDelta.x : (stp.x < 0 ? (a.x - c.x) * tDelta.x : 1e30),
                         stp.y > 0 ? (c.y + 1 - a.y) * tDelta.y : (stp.y < 0 ? (a.y - c.y) * tDelta.y : 1e30));
    [loop] for (int i = 0; i < 32; i++)
    {
        if (c.x == e.x && c.y == e.y) break;
        bool stepX = tMax.x < tMax.y;
        float t = stepX ? tMax.x : tMax.y;
        if (t > 1.0) break;
        float y = lerp(P.y, L.y, t);
        uint storey = y >= pitch ? 1u : 0u;
        int2 n = c + (stepX ? int2(stp.x, 0) : int2(0, stp.y));
        int2 owner = stepX ? int2(max(c.x, n.x), c.y) : int2(c.x, max(c.y, n.y));
        uint o = LGrid_Occ(owner, storey);
        if (!LG_Loaded(o)) return 1;                       // edge of the loaded area: leave it to the shadows
        uint kind = stepX ? LG_WestKind(o) : LG_SouthKind(o);
        float top = (stepX ? LG_WestTop(o) : LG_SouthTop(o)) + storey * pitch;
        if (kind == LG_EDGE_WALL || y > top + 0.02) return 0;
        c = n;
        if (stepX) tMax.x += tDelta.x; else tMax.y += tDelta.y;
    }
    return 1;
}

#endif
