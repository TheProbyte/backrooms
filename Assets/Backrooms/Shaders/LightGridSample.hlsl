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
#include "LightGridOcclusion.hlsl"

TEXTURE3D(_LGridA);
TEXTURE3D(_LGridB);
SAMPLER(sampler_LGridA);
TEXTURE2D(_LGridBounce);
SAMPLER(sampler_LGridBounce);
TEXTURE2D(_LGridWallBounce);
TEXTURE2D(_LGridBounceDirection);
TEXTURE2D(_LGridProbeR);               // traced bounce: L1 SH of incoming light per colour channel, OccN x (OccN * 4)
TEXTURE2D(_LGridProbeG);               // (slab = storey * 2 + low/high probe; see LightGrid.Probes.cs)
TEXTURE2D(_LGridProbeB);
float4 _LGridGI;                       // x: traced bounce on (0/1)
StructuredBuffer<uint> _LGridOcc;      // [storey][z][x], toroidal

// The grid's own light list, so surfaces can also evaluate its lights per pixel (no 0.6 m texels).
struct LGridLight
{
    float3 pos;      float range;
    float3 color;    float cosOuter;     // colour x intensity x the grid's share (0 while it is a real URP light)
    float3 dir;      float cosInner;
    float3 colorAll; float storey;
    float2 halfSize; float2 padding;
};
StructuredBuffer<LGridLight> _LGridLights;
StructuredBuffer<uint2> _LGridTiles;     // (first index, count) per 4.8 m tile, toroidal
StructuredBuffer<uint> _LGridTileLights;
StructuredBuffer<uint> _LGridFixtureLookup;
uint _LGridFixtureMask;
float4 _LGridFar;                        // x: per-pixel evaluation on (0/1), y: texels per tile, z: tiles per axis, w: near/far split radius (0 = all per pixel)

// Near/far split of grid light (per-pixel mode with w > 0): each fixture is worked out exactly per pixel on surfaces
// within about w metres of it, and comes from the 0.6 m texture farther away, where its light is weak and smooth. The two
// weights add up to one across a 2 m band (the compute pass uses the same split), so nothing is counted twice.
half LGrid_NearWeight(float distanceToLight)
{
    if (_LGridFar.w <= 0) return 1;
    return (half)(1 - smoothstep(_LGridFar.w - 1, _LGridFar.w + 1, distanceToLight));
}
bool LGrid_TextureDirect() { return _LGridFar.x < 0.5 || _LGridFar.w > 0; }

float4 _LGridSize;      // texels x, y, z, occupancy cells
float4 _LGridParams;    // x: on (0/1), y: direct gain, z: bounce gain, w: storey pitch
float4 _LGridWindow;    // world xz min, xz max of the valid window
float _LGridFillAmt;    // room fill 0..1: how much light bounced off floors, walls and ceilings evens out the walls (lifts the low-wall bounce term)
float4 _LGridShape;     // x: directional wrap (0..1), y: normal offset, z: texel smoothing (0..1), w: contrast of dim light (0..1)

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

half LGrid_LightVisible(float3 P, float3 N, float3 L);

// Conservative range/cone test for the complete horizontal emitter rectangle, including its finite samples.
bool LGrid_FixtureMayReach(LGridLight f, float3 receiver)
{
    float3 delta = f.pos - receiver;
    float distanceSquared = dot(delta, delta);
    if (distanceSquared >= f.range * f.range) return false;
    if (f.dir.y < -0.9999 && dot(f.dir.xz, f.dir.xz) < 1e-8)
    {
        float sizeWeight = 1.0 - smoothstep(36.0, 64.0, distanceSquared);
        float2 nearest = max(abs(delta.xz) - f.halfSize * sizeWeight, 0);
        float bestCosine = delta.y * rsqrt(max(delta.y * delta.y + dot(nearest, nearest), 1e-6));
        if (bestCosine <= f.cosOuter) return false;
    }
    return true;
}

float LGrid_DistAtten(float d2, float range)
{
    float f = d2 / (range * range);
    float s = saturate(1.0 - f * f);
    return s * s / max(d2, 0.0001);
}

// How a ceiling fixture's intensity falls off away from straight down. A flat diffuser panel is Lambertian: intensity goes
// as cos(angle), so a wall beside a panel is brightest just under the ceiling and fades downward, and a floor is lit
// evenly. (A squared spot falloff, cos^2-like, made round hot spots halfway down walls and darker rooms.) With the cone
// TrofferCone gives at full spread (outer 179, inner 0) this is cos(angle); a lower spread narrows it.
float LGrid_AngleAtten(float cosAngle, float cosOuter, float cosInner)
{
    float inv = 1.0 / max(cosInner - cosOuter, 0.001);
    return saturate(cosAngle * inv - cosOuter * inv);
}

// Shared tile lookup for the grid and real-light material-response paths.
uint2 LGrid_TileRange(float3 P)
{
    float tileM = LG_TEXEL * _LGridFar.y;
    int tiles = (int)_LGridFar.z;
    int tx = ((int)floor(P.x / tileM)) & (tiles - 1), tz = ((int)floor(P.z / tileM)) & (tiles - 1);
    return _LGridTiles[tz * tiles + tx];
}

bool LGrid_FindFixtureIndex(float3 position, float3 receiver, out LGridLight fixture, out uint index)
{
    fixture = (LGridLight)0; index = 0;
    if (_LGridParams.x < 0.5 || any(receiver.xz < _LGridWindow.xy) || any(receiver.xz > _LGridWindow.zw)) return false;
    int3 cell = (int3)floor(position * 4.0);
    uint slot = ((uint)cell.x * 73856093u ^ (uint)cell.y * 83492791u ^ (uint)cell.z * 19349663u) & _LGridFixtureMask;
    // Expected constant-time lookup instead of a second complete tile scan for every real light and pixel.
    [loop] for (uint probe = 0; probe <= _LGridFixtureMask; probe++)
    {
        uint entry = _LGridFixtureLookup[slot];
        if (entry == 0) return false;
        LGridLight f = _LGridLights[entry - 1];
        float3 d = f.pos - position;
        if (dot(d, d) < 0.0001) { fixture = f; index = entry - 1; return true; }
        slot = (slot + 1) & _LGridFixtureMask;
    }
    return false;
}

bool LGrid_FindFixture(float3 position, float3 receiver, out LGridLight fixture)
{
    uint index;
    return LGrid_FindFixtureIndex(position, receiver, fixture, index);
}

// Wrap a seam inside one atlas layer; texture objects stay explicit at the call site.
void LGrid_BounceCoordinates(float2 metresXZ, uint storey, out float2 uvA, out float2 uvB, out float blend)
{
    float n = _LGridSize.w;
    float2 uv = frac(metresXZ / (LG_CELL * n));
    uvA = float2(uv.x, (uv.y + storey) * 0.5); uvB = uvA; blend = 0;
    if (uv.y < 0.5 / n || uv.y > 1 - 0.5 / n)
    {
        float row = uv.y * n - 0.5;
        int y0 = ((int)floor(row)) & ((int)n - 1), y1 = (y0 + 1) & ((int)n - 1);
        uvA.y = (y0 + storey * n + 0.5) / (n * 2);
        uvB.y = (y1 + storey * n + 0.5) / (n * 2); blend = frac(row);
    }
}

// Traced bounce atlas: like the bounce field's, with four slabs (storey x low/high probe) stacked in v.
void LGrid_ProbeCoordinates(float2 metresXZ, uint slab, out float2 uvA, out float2 uvB, out float blend)
{
    float n = _LGridSize.w;
    float2 uv = frac(metresXZ / (LG_CELL * n));
    uvA = float2(uv.x, (uv.y + slab) * 0.25); uvB = uvA; blend = 0;
    if (uv.y < 0.5 / n || uv.y > 1 - 0.5 / n)
    {
        float row = uv.y * n - 0.5;
        int y0 = ((int)floor(row)) & ((int)n - 1), y1 = (y0 + 1) & ((int)n - 1);
        uvA.y = (y0 + slab * n + 0.5) / (n * 4);
        uvB.y = (y1 + slab * n + 0.5) / (n * 4); blend = frac(row);
    }
}

half3 LGrid_ProbeSlab(float2 q, uint slab, half3 N)
{
    float2 uvA, uvB; float blend;
    LGrid_ProbeCoordinates(q, slab, uvA, uvB, blend);
    float4 r = SAMPLE_TEXTURE2D_LOD(_LGridProbeR, sampler_LinearRepeat, uvA, 0);
    float4 g = SAMPLE_TEXTURE2D_LOD(_LGridProbeG, sampler_LinearRepeat, uvA, 0);
    float4 b = SAMPLE_TEXTURE2D_LOD(_LGridProbeB, sampler_LinearRepeat, uvA, 0);
    [branch] if (blend > 0)
    {
        r = lerp(r, SAMPLE_TEXTURE2D_LOD(_LGridProbeR, sampler_LinearRepeat, uvB, 0), blend);
        g = lerp(g, SAMPLE_TEXTURE2D_LOD(_LGridProbeG, sampler_LinearRepeat, uvB, 0), blend);
        b = lerp(b, SAMPLE_TEXTURE2D_LOD(_LGridProbeB, sampler_LinearRepeat, uvB, 0), blend);
    }
    float4 basis = float4(0.282095, 0.325735 * (float3)N);   // irradiance from L1 radiance: Y00 and (2/3) Y1(n)
    return (half3)max(float3(dot(r, basis), dot(g, basis), dot(b, basis)), 0);
}

// Traced bounce at a (clamped) sample point: the cell's low and high probes blended by height. Across walls the sample stays
// at its own cell's probes; across open floor and openings it blends with the neighbours.
half3 LGrid_ProbeIrradiance(float3 p, half3 N)
{
    float pitch = _LGridParams.w;
    int2 cell = (int2)floor(p.xz / LG_CELL);
    uint storey = p.y >= pitch ? 1u : 0u;
    uint o = LGrid_Occ(cell, storey);
    if (!LG_Loaded(o)) return 0;
    uint oe = LGrid_Occ(cell + int2(1, 0), storey), on = LGrid_Occ(cell + int2(0, 1), storey);
    float2 q = p.xz, mid = (cell + 0.5) * LG_CELL;
    if (LG_WestKind(o) == LG_EDGE_WALL || !LG_Loaded(LGrid_Occ(cell - int2(1, 0), storey))) q.x = max(q.x, mid.x);
    if (LG_WestKind(oe) == LG_EDGE_WALL || !LG_Loaded(oe)) q.x = min(q.x, mid.x);
    if (LG_SouthKind(o) == LG_EDGE_WALL || !LG_Loaded(LGrid_Occ(cell - int2(0, 1), storey))) q.y = max(q.y, mid.y);
    if (LG_SouthKind(on) == LG_EDGE_WALL || !LG_Loaded(on)) q.y = min(q.y, mid.y);
    float2 h = LG_ProbeHeights(o, storey * pitch);
    float t = saturate((p.y - h.x) / max(h.y - h.x, 0.1));
    half3 low = LGrid_ProbeSlab(q, storey * 2u, N);
    half3 high = LGrid_ProbeSlab(q, storey * 2u + 1u, N);
    return lerp(low, high, (half)t);
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

    // Per-pixel direct fixtures use the same PBR path as real lights in the forward pass.
    // Only the deliberately coarse texture fallback contributes diffuse irradiance here.
    half3 direct = 0;
    [branch] if (LGrid_TextureDirect())
    {
        float3 uvw = float3(p.x / (LG_TEXEL * _LGridSize.x), p.y / (LG_TEXEL * _LGridSize.y), p.z / (LG_TEXEL * _LGridSize.z));
        // Plain trilinear ramps linearly between texel centres, which shows as boxy light pools at 0.6 m texels.
        // An ease curve on the blend weight (still only the two nearest texels, so wall clamping holds) rounds them.
        float3 tex = uvw * _LGridSize.xyz - 0.5;
        float3 ti = floor(tex), tf = tex - ti;
        tf = lerp(tf, tf * tf * (3.0 - 2.0 * tf), _LGridShape.z);
        uvw = (ti + tf + 0.5) / _LGridSize.xyz;
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
        direct = (half3)(E * facing * notCeiling * _LGridParams.y);

    }

    // Bounce: traced probes, or (the older field) light that hit the floor nearby, mostly lighting ceilings and upper walls.
    half3 bounce = 0;
    [branch] if (_LGridGI.x > 0.5)
    {
        half3 lit = direct + LGrid_ProbeIrradiance(p, normalWS) * (half)_LGridParams.z;
        half lumLit = max(dot(lit, half3(0.2126, 0.7152, 0.0722)), 1e-5);
        lit *= pow(saturate(lumLit), _LGridShape.w * 1.2);
        return lit * edge;
    }
    int2 bcell = (int2)floor(p.xz / LG_CELL);
    uint bounceStorey = p.y >= pitch ? 1u : 0u;
    uint bocc = LGrid_Occ(bcell, bounceStorey);
    // A floorless upper well receives the lower floor's bounce, ordinary upper rooms use their own layer.
    if (bounceStorey == 1u && (bocc & LG_NOFLOOR) != 0u && (LGrid_Occ(bcell, 0u) & LG_OPENABOVE) != 0u)
    { bounceStorey = 0u; bocc = LGrid_Occ(bcell, 0u); }
    if (LG_Loaded(bocc))
    {
        // Bounce is stored per 1.2 m cell: keep the sample at the cell centre on sides that are walls.
        float2 q = p.xz;
        int2 cell = bcell;
        uint o = bocc, oe = LGrid_Occ(cell + int2(1, 0), bounceStorey), on = LGrid_Occ(cell + int2(0, 1), bounceStorey);
        // Bounce is stored per 1.2 m cell and blends smoothly with its neighbours: keep the sample at the cell centre on every side that is not open floor (walls, doors and arches alike), so a lit room never bleeds into a dark one.
        float2 mid = (cell + 0.5) * LG_CELL;
        if (LG_WestKind(o) != LG_EDGE_OPEN) q.x = max(q.x, mid.x);
        if (LG_WestKind(oe) != LG_EDGE_OPEN) q.x = min(q.x, mid.x);
        if (LG_SouthKind(o) != LG_EDGE_OPEN) q.y = max(q.y, mid.y);
        if (LG_SouthKind(on) != LG_EDGE_OPEN) q.y = min(q.y, mid.y);
        float2 uvA, uvB; float atlasBlend;
        LGrid_BounceCoordinates(q, bounceStorey, uvA, uvB, atlasBlend);
        float3 B = SAMPLE_TEXTURE2D_LOD(_LGridBounce, sampler_LGridBounce, uvA, 0).rgb;
        float3 wallB = SAMPLE_TEXTURE2D_LOD(_LGridWallBounce, sampler_LGridBounce, uvA, 0).rgb;
        float4 moment = SAMPLE_TEXTURE2D_LOD(_LGridBounceDirection, sampler_LGridBounce, uvA, 0);
        [branch] if (atlasBlend > 0)
        {
            B = lerp(B, SAMPLE_TEXTURE2D_LOD(_LGridBounce, sampler_LGridBounce, uvB, 0).rgb, atlasBlend);
            wallB = lerp(wallB, SAMPLE_TEXTURE2D_LOD(_LGridWallBounce, sampler_LGridBounce, uvB, 0).rgb, atlasBlend);
            moment = lerp(moment, SAMPLE_TEXTURE2D_LOD(_LGridBounceDirection, sampler_LGridBounce, uvB, 0), atlasBlend);
        }
        // Light from the floor reaches the ceiling and upper walls; floors get very little, and tall
        // ceilings less than low ones (as the old upward bounce lights did).
        float ceilingH = max(LG_Ceiling(o), 2.0);
        float h = saturate((p.y - bounceStorey * pitch) / ceilingH);
        float wallTerm = lerp(lerp(0.12, 0.42, _LGridFillAmt), lerp(0.55, 0.72, _LGridFillAmt), h);
        float facing = normalWS.y < 0.0 ? lerp(wallTerm, 1.0, -normalWS.y) : lerp(wallTerm, 0.04, normalWS.y);
        float height = 3.0 / max(3.0, ceilingH);
        float momentLength = length(moment.xyz);
        float directional = momentLength > 1e-5 ? saturate(dot(normalWS, moment.xyz / momentLength)) : 0.5;
        float wallFacing = lerp(0.5, directional, saturate(momentLength / max(moment.w, 1e-5)));
        bounce = (half3)((B * facing * height + wallB * wallFacing) * _LGridParams.z);
    }
    half3 lit = direct + bounce;
    // Contrast: weakly lit spots fall away faster than bright ones (reference level about a lit wall).
    half lumLit = max(dot(lit, half3(0.2126, 0.7152, 0.0722)), 1e-5);
    lit *= pow(saturate(lumLit), _LGridShape.w * 1.2);
    return lit * edge;
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

// ---------------- Real panels: how much of the panel a point sees past the walls ----------------
// A ceiling panel is not a point. Past a wall end its light does not stop on a line: the point sees less and less of the
// panel, so the shadow edge is a penumbra that is sharp at the wall and widens away from it (panel width x distance from
// the wall end / distance from the wall end to the panel). The same holds under a door header, where the near part of the
// panel (the steeper rays) is hidden first.
//
// Walls are full height on 1.2 m cell edges, and a fitting lies inside its own cell. So, in plan, the walls that can hide
// any part of the panel lie on the cell lines that the line to its centre crosses: it is the same walk as
// LGrid_LightVisible. At each crossing, the wall pieces inside the cone to the panel hide the share of the panel's width
// they cover as seen from P, and an opening's header hides the share of the panel's depth whose rays pass above its top.
// Returns the fraction of the panel seen (0..1); halfSize zero is the point test.

// The interval across the line of sight (metres at the panel, centre 0) that the wall piece q0-q1 covers, seen from p.
float2 LG_AcrossInterval(float2 p, float2 along, float2 across, float D, float2 q0, float2 q1)
{
    float a0 = dot(q0 - p, along), a1 = dot(q1 - p, along);
    float b0 = dot(q0 - p, across), b1 = dot(q1 - p, across);
    const float aMin = 0.02;
    if (max(a0, a1) <= aMin || min(a0, a1) >= D) return float2(1, -1);   // beside or behind P, or beyond the panel
    // Only the part in front of P and short of the panel counts.
    if (a0 < aMin) { b0 = lerp(b0, b1, (aMin - a0) / (a1 - a0)); a0 = aMin; }
    if (a1 < aMin) { b1 = lerp(b1, b0, (aMin - a1) / (a0 - a1)); a1 = aMin; }
    if (a0 > D) { b0 = lerp(b0, b1, (a0 - D) / (a0 - a1)); a0 = D; }
    if (a1 > D) { b1 = lerp(b1, b0, (a1 - D) / (a1 - a0)); a1 = D; }
    float s0 = b0 * D / a0, s1 = b1 * D / a1;
    return float2(min(s0, s1), max(s0, s1));
}

// Share of a rectangle's area lying at or below s across the line of sight: the panel's half sizes seen across it are a
// and b, so its area is spread across as a trapezoid (two box widths convolved). A panel seen square-on fades out
// linearly past a wall end; one seen corner-on fades in an S, as its area is.
float LG_PanelCdf(float s, float a, float b)
{
    float hi = max(a, b), lo = max(min(a, b), 1e-4);
    if (s <= lo - hi) return s <= -(hi + lo) ? 0 : (s + hi + lo) * (s + hi + lo) / (8 * hi * lo);
    if (s >= hi - lo) return s >= hi + lo ? 1 : 1 - (hi + lo - s) * (hi + lo - s) / (8 * hi * lo);
    return 0.5 + s / (2 * hi);
}

half LGrid_EmitterVisible(float3 P, float3 N, float3 L, float2 halfSize)
{
    if (dot(halfSize, halfSize) < 1e-6) return LGrid_LightVisible(P, N, L);
    if (_LGridParams.x < 0.5) return 1;
    float pitch = _LGridParams.w;
    P += N * 0.06;
    if (any(P.xz < _LGridWindow.xy) || any(P.xz > _LGridWindow.zw)) return 1;
    uint sp = P.y >= pitch ? 1u : 0u, sl = L.y >= pitch ? 1u : 0u;
    if (sp != sl)
    {
        float ts = (pitch - P.y) / (L.y - P.y);
        float3 X = lerp(P, L, ts);
        if ((LGrid_Occ((int2)floor(X.xz / LG_CELL), 0u) & LG_OPENABOVE) == 0u) return 0;
    }
    float2 p = P.xz, toL = L.xz - P.xz;
    float D = length(toL);
    if (D < 1e-3) return 1;
    float2 along = toL / D, across = float2(-along.y, along.x);
    float wx = abs(across.x) * halfSize.x, wz = abs(across.y) * halfSize.y;
    float hw = wx + wz;                                                   // the panel's half width across the line of sight
    float lo = -hw, hi = hw, hidden = 0;                                  // the part of the width still seen; inner parts hidden
    float rise = L.y - P.y;
    bool done = false;                                                    // fully hidden, or past the loaded area
    // A header that hides part of the panel (the most): the rows of the panel beyond `limit` (panel-local metres) along
    // x (axis 0) or z (axis 1), on the side toward P (side +1: below the limit), for the width `range` it spans.
    float hAxis = -1, hLimit = 0, hSide = 0, hFrac = 0;
    float2 hRange = float2(1, -1);
    float2 a = p / LG_CELL, b = L.xz / LG_CELL;
    int2 c = (int2)floor(a), e = (int2)floor(b);
    float2 d = b - a;
    int2 stp = int2(d.x > 0 ? 1 : (d.x < 0 ? -1 : 0), d.y > 0 ? 1 : (d.y < 0 ? -1 : 0));
    float2 tDelta = float2(stp.x != 0 ? abs(1.0 / d.x) : 1e30, stp.y != 0 ? abs(1.0 / d.y) : 1e30);
    float2 tMax = float2(stp.x > 0 ? (c.x + 1 - a.x) * tDelta.x : (stp.x < 0 ? (a.x - c.x) * tDelta.x : 1e30),
                         stp.y > 0 ? (c.y + 1 - a.y) * tDelta.y : (stp.y < 0 ? (a.y - c.y) * tDelta.y : 1e30));
    [loop] for (int i = 0; i < 32 && !done; i++)
    {
        if (c.x == e.x && c.y == e.y) break;
        bool stepX = tMax.x < tMax.y;
        float t = stepX ? tMax.x : tMax.y;
        if (t > 1.0) break;
        uint storey = P.y + rise * t >= pitch ? 1u : 0u;
        int2 n = c + (stepX ? int2(stp.x, 0) : int2(0, stp.y));
        int2 owner = stepX ? int2(max(c.x, n.x), c.y) : int2(c.x, max(c.y, n.y));
        // The cone to the panel meets this cell line over about t x hw / |sin| of the angle between them: the wall piece
        // crossed, and the next ones along the line when the cone reaches them.
        float local = (stepX ? (p.y + toL.y * t) : (p.x + toL.x * t)) / LG_CELL - (stepX ? owner.y : owner.x);
        float span = t * hw / max(stepX ? abs(along.x) : abs(along.y), 0.2) / LG_CELL;
        int first = local < span ? -1 : 0, last = 1.0 - local < span ? 1 : 0;
        float aCross = t * D;
        [loop] for (int k = first; k <= last && !done; k++)
        {
            int2 cell = stepX ? int2(owner.x, owner.y + k) : int2(owner.x + k, owner.y);
            uint o = LGrid_Occ(cell, storey);
            if (!LG_Loaded(o)) { done = k == 0; continue; }     // the edge of the loaded area: what is known so far
            float2 q0 = stepX ? float2(owner.x, cell.y) * LG_CELL : float2(cell.x, owner.y) * LG_CELL;
            float2 q1 = q0 + (stepX ? float2(0, LG_CELL) : float2(LG_CELL, 0));
            float opacity = 1;
            if ((stepX ? LG_WestKind(o) : LG_SouthKind(o)) != LG_EDGE_WALL)
            {
                // An opening: its header hides the rays that cross this line above its top. A ray to a panel point crosses
                // at the fraction (line - P) / (point - P) of the way along the line's normal, so the hidden part is the
                // panel's rows nearer P than a limit: a strip of the panel (panels and cell lines both run along x and z).
                float top = (stepX ? LG_WestTop(o) : LG_SouthTop(o)) + storey * pitch + 0.02;
                if (rise <= 0.01) opacity = P.y + rise * t > top ? 1 : 0;
                else if (top <= P.y) opacity = 1;
                else
                {
                    float r = (top - P.y) / rise;                 // how far along a ray it reaches the top
                    if (r >= 1) continue;                         // the opening reaches above the panel
                    float pc = stepX ? P.x : P.z, at = stepX ? q0.x : q0.y, h = stepX ? halfSize.x : halfSize.y;
                    float limit = pc + (at - pc) / r - (stepX ? L.x : L.z);
                    float side = at > pc ? 1 : -1;
                    opacity = saturate(side > 0 ? (limit + h) / (2 * h) : (h - limit) / (2 * h));
                    if (opacity > 0 && opacity < 0.999)
                    {
                        float2 sr = LG_AcrossInterval(p, along, across, D, q0, q1);
                        if (opacity > hFrac) { hAxis = stepX ? 0 : 1; hLimit = limit; hSide = side; hFrac = opacity; }
                        hRange = float2(min(hRange.x, sr.x), max(hRange.y, sr.y));
                        continue;                                 // worked out at the end, against the final width
                    }
                }
                if (opacity <= 0) continue;
            }
            float2 s = LG_AcrossInterval(p, along, across, D, q0, q1);
            s.x = max(s.x, lo); s.y = min(s.y, hi);
            if (s.y <= s.x) continue;
            if (s.x <= lo + 1e-4) lo = s.y;               // a wall end on one side: the panel is cut from that side
            else if (s.y >= hi - 1e-4) hi = s.x;
            else hidden += s.y - s.x;                       // a short piece in the middle
            if (hi - lo - hidden <= 1e-4) { hi = lo; done = true; }
        }
        c = n;
        if (stepX) tMax.x += tDelta.x; else tMax.y += tDelta.y;
    }
    // The share of the panel's area across [lo, hi], less the inner pieces (taken as an even share of the width)...
    float seen = LG_PanelCdf(hi, wx, wz) - LG_PanelCdf(lo, wx, wz) - hidden / (2 * hw);
    // ...less the header's strip of the panel where the header spans that width: a smaller rectangle, the same way.
    if (hAxis >= 0 && hi > lo)
    {
        float h = hAxis < 0.5 ? halfSize.x : halfSize.y;
        float from = hSide > 0 ? -h : max(hLimit, -h), to = hSide > 0 ? min(hLimit, h) : h;
        float ra = max(lo, hRange.x), rb = min(hi, hRange.y);
        if (to > from && rb > ra)
        {
            float centre = 0.5 * (from + to), strip = 0.5 * (to - from);
            float s0 = centre * (hAxis < 0.5 ? across.x : across.y);
            float a2 = abs(across.x) * (hAxis < 0.5 ? strip : halfSize.x), b2 = abs(across.y) * (hAxis < 0.5 ? halfSize.y : strip);
            seen -= (to - from) / (2 * h) * (LG_PanelCdf(rb - s0, a2, b2) - LG_PanelCdf(ra - s0, a2, b2));
        }
    }
    return (half)saturate(seen);
}

// Bounded, single-hop diffuse transport through wall apertures. No extra real lights, shadow maps,
// CPU readback or recursive contribution: the source samples only the ordinary destination lighting.
int _PortalSpillCount;
float4 _PortalSpillPosition[8], _PortalSpillNormal[8], _PortalSpillPartner[8], _PortalSpillPartnerNormal[8];

TEXTURE2D(_PortalIrradiance);
half3 PortalCachedIncident(int end, half3 N)
{
    half3 w = N * N;
    return LOAD_TEXTURE2D(_PortalIrradiance, int2(N.x >= 0 ? 0 : 1, end)).rgb * w.x +
           LOAD_TEXTURE2D(_PortalIrradiance, int2(N.y >= 0 ? 2 : 3, end)).rgb * w.y +
           LOAD_TEXTURE2D(_PortalIrradiance, int2(N.z >= 0 ? 4 : 5, end)).rgb * w.z;
}

half3 BackroomsPortalSpill(float3 P, half3 N, out half localWeight)
{
    localWeight = 1;
    if (_LGridParams.x < 0.5 || _PortalSpillCount == 0) return 0;
    half3 result = 0;
    [loop] for (int i = 0; i < _PortalSpillCount; i++)
    {
        float3 origin = _PortalSpillPosition[i].xyz, normal = _PortalSpillNormal[i].xyz;
        float3 delta = P - origin;
        float d2 = dot(delta, delta), depth = dot(delta, normal);
        if (d2 >= 9.0 || depth < -0.005) continue;
        float3 target = _PortalSpillPartner[i].xyz + _PortalSpillPartnerNormal[i].xyz * 0.12;
        if (any(target.xz < _LGridWindow.xy + 1) || any(target.xz > _LGridWindow.zw - 1)) continue;
        float3 direction = delta * rsqrt(max(d2, 0.01));
        float area = _PortalSpillPosition[i].w * _PortalSpillPosition[i].w;
        // A finite aperture's softened form factor; compact support has zero slope at its outer edge.
        float weight = area / (area + 3.141593 * d2) * saturate(dot(N, -direction)) * saturate(dot(normal, direction));
        weight *= 1 - smoothstep(4.0, 9.0, d2);
        if (LGrid_LightVisible(P, N, origin + normal * 0.12) < 0.5) continue;
        weight *= LocalLightVisibility(P, N, origin + normal * 0.12);
        float3 targetNormal = _PortalSpillPartnerNormal[i].xyz;
        half3 incident = weight > 0.0001 ? PortalCachedIncident(i, targetNormal) : 0;
        float3 right = cross(float3(0, 1, 0), normal);
        float lateral = dot(delta, right);
        float halfSize = _PortalSpillPosition[i].w * 0.5;
        // Reciprocal half-weight at the shared plane joins both rooms' illumination continuously.
        // This narrow bridge is limited to the aperture/reveals; the wider spill remains additive.
        half seam = 0.5 * (1 - smoothstep(0, 0.6, max(depth, 0))) *
            (1 - smoothstep(halfSize, halfSize + 0.15, max(abs(lateral), abs(delta.y))));
        if (seam > 0.001)
        {
            float3 remoteRight = cross(float3(0, 1, 0), -targetNormal);
            half3 remoteN = remoteRight * dot(N, right) + half3(0, N.y, 0) - targetNormal * dot(N, normal);
            result += PortalCachedIncident(i, remoteN) * seam;
            localWeight *= 1 - seam;
        }
        result += incident * weight * (1 - seam * 2);
    }
    return result;
}

#endif
