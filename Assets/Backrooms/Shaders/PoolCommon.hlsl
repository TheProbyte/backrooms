// The Poolrooms: shared by PoolTile, PoolWater and PoolSky (Resources/Shaders).
// Light here is the sun, traced through a per-cell map of the loaded Poolrooms (see PoolWorld.cs and PoolSun.cs, the
// C# copy of the same test): a ray toward the sun walks the 1.2 m cell grid until it meets the ceiling, and the point
// is sunlit if it gets there inside a roof opening and leaves the light well above it. Walls, door headers, lower
// ceilings and round columns stop it. Bounce light was baked per cell from where the sun lands (map alpha).
#ifndef BACKROOMS_POOL_COMMON
#define BACKROOMS_POOL_COMMON

TEXTURE2D(_PoolMap);                 // RGBA32 linear, toroidal by world cell: r flags, g ceiling/10, b opening, a bounce
float4 _PoolMapParams;               // x map size in cells, y storey floor height
TEXTURE2D(_PoolRipple); SAMPLER(sampler_PoolRipple);
float4 _PoolRippleRect;              // xy world xz of the window corner, z window size (m), w slope gain (0 = none)
float4 _PoolSunColor;                // rgb: sun (already times intensity)
float4 _PoolAmbient;                 // rgb: light everywhere (sky through the openings, scattered)
float4 _PoolBounce;                  // rgb: bounce at full baked strength
float4 _PoolSkyZenith, _PoolSkyHorizon;
float _PoolScreenTextures;           // 1: this camera has the opaque and depth textures (the main view)
float4 _PortalClipPlane;             // zero outside a portal-camera render

#define POOL_C 1.2
#define POOL_TILE 0.15
#define POOL_SHAFT 1.2
#define POOL_HEADER 2.7
#define POOL_PILLAR_R 0.4
#define POOL_ROUND_R 0.45
#define POOL_WATER (-0.12)
#define POOL_BASIN 0.45
static const float3 POOL_SUN = float3(0.420764, 0.781419, 0.460837);   // normalize(0.42, 0.78, 0.46), as PoolBiome.SunDirection

struct PoolCell
{
    bool valid, basin, pillar, pillarNear;
    uint west, south;    // 0 open, 1 wall, 2 headed doorway
    uint sky, joined;
    float ceiling, fill;
};

PoolCell PoolLoad(int2 cell)
{
    uint n = (uint)_PoolMapParams.x;
    uint2 s = (uint2)(cell + (int)n * 4096) % n;      // cells are well within 4096 maps of the origin
    uint4 v = (uint4)(LOAD_TEXTURE2D(_PoolMap, s) * 255.0 + 0.5);
    PoolCell c;
    c.valid = (v.r & 64u) != 0;
    c.west = v.r & 3u; c.south = (v.r >> 2) & 3u;
    c.basin = (v.r & 16u) != 0; c.pillar = (v.r & 32u) != 0; c.pillarNear = (v.r & 128u) != 0;
    c.ceiling = v.g * 0.1;
    c.sky = v.b & 7u; c.joined = (v.b >> 3) & 15u;
    c.fill = v.a / 255.0;
    return c;
}

int2 PoolCellOf(float3 p) { return (int2)floor(p.xz / POOL_C); }

// Signed distance (m) from a point inside the cell (0..1.2 each way) to the edge of its roof opening, > 0 inside.
// Shapes as PoolBiome.HoleRect: 1 full, 2 slot along x, 3 slot along z, 4 square, 5 round.
float PoolHoleSD(uint shape, uint joined, float2 uv)
{
    if (shape == 0) return -1.0;
    if (shape == 5) return POOL_ROUND_R - length(uv - 0.6);
    const float rim = 0.15;
    float x0 = (joined & 1u) ? -1.0 : rim, x1 = (joined & 2u) ? 2.2 : 1.2 - rim;
    float z0 = (joined & 4u) ? -1.0 : rim, z1 = (joined & 8u) ? 2.2 : 1.2 - rim;
    float4 r = shape == 1 ? float4(x0, z0, x1, z1) : shape == 2 ? float4(x0, 0.45, x1, 0.75)
             : shape == 3 ? float4(0.45, z0, 0.75, z1) : float4(0.3, 0.3, 0.9, 0.9);
    return min(min(uv.x - r.x, r.z - uv.x), min(uv.y - r.y, r.w - uv.y));
}

bool PoolPillarHit(int2 c, float3 p, float t0, float t1)
{
    const float3 L = POOL_SUN;
    [unroll] for (int k = 0; k < 4; k++)
    {
        int2 v = c + int2(k & 1, k >> 1);
        if (!PoolLoad(v).pillar) continue;
        float2 d = p.xz - v * POOL_C;
        float a = dot(L.xz, L.xz), b = dot(d, L.xz), cc = dot(d, d) - POOL_PILLAR_R * POOL_PILLAR_R;
        float disc = b * b - a * cc;
        if (disc < 0) continue;
        float s = sqrt(disc);
        float tin = (-b - s) / a, tout = (-b + s) / a;
        if (tout > t0 && tin < t1 && tout > 1e-3) return true;
    }
    return false;
}

// Sunlight reaching a world point: 0 shade .. 1 full sun (soft only at the edges of the openings).
float PoolSunVisibility(float3 pw)
{
    const float3 L = POOL_SUN;        // x and z are positive: the ray only steps +x / +z
    float3 p = pw; p.y -= _PoolMapParams.y;
    int2 c = PoolCellOf(p);
    PoolCell cell = PoolLoad(c);
    float t = 0;
    [loop] for (int i = 0; i < 20; i++)
    {
        if (!cell.valid) return 0;
        float tCeil = (cell.ceiling - p.y) / L.y;
        float tx = ((c.x + 1) * POOL_C - p.x) / L.x;
        float tz = ((c.y + 1) * POOL_C - p.z) / L.z;
        float tExit = min(tx, tz);
        if (cell.pillarNear && PoolPillarHit(c, p, t, min(tExit, tCeil))) return 0;
        if (tCeil <= tExit)
        {
            float3 q = p + L * tCeil;
            float d1 = PoolHoleSD(cell.sky, cell.joined, q.xz - c * POOL_C);
            float t2 = tCeil + POOL_SHAFT / L.y;
            float3 q2 = p + L * t2;
            int2 c2 = (int2)floor(q2.xz / POOL_C);
            PoolCell cell2 = PoolLoad(c2);
            float d2 = (cell2.valid && cell2.sky == cell.sky) ? PoolHoleSD(cell2.sky, cell2.joined, q2.xz - c2 * POOL_C) : -1.0;
            // The sun is half a degree wide: the edge of each opening blurs with distance.
            return saturate(d1 / (0.006 + 0.009 * tCeil) + 0.5) * saturate(d2 / (0.006 + 0.009 * t2) + 0.5);
        }
        float y = p.y + L.y * tExit;
        int2 nc = c + (tx <= tz ? int2(1, 0) : int2(0, 1));
        PoolCell n = PoolLoad(nc);
        uint edge = tx <= tz ? n.west : n.south;
        if (edge == 1u || (edge == 2u && y > POOL_HEADER)) return 0;
        if (!n.valid || y >= n.ceiling) return 0;
        c = nc; cell = n; t = tExit;
    }
    return 0;
}

// Baked bounce at a point: bilinear between cell centres, never across a wall.
float PoolFill(float3 p)
{
    float2 g = p.xz / POOL_C - 0.5;
    int2 b = (int2)floor(g);
    float2 f = g - b;
    int2 o = PoolCellOf(p) - b;   // which of the four is the point's own cell (0/1 each way)
    PoolCell c00 = PoolLoad(b), c10 = PoolLoad(b + int2(1, 0)), c01 = PoolLoad(b + int2(0, 1)), c11 = PoolLoad(b + int2(1, 1));
    bool h0 = c10.west != 1u, h1 = c11.west != 1u;      // between the columns, rows 0 / 1
    bool v0 = c01.south != 1u, v1 = c11.south != 1u;    // between the rows, columns 0 / 1
    bool k00, k10, k01, k11;
    if (o.x == 0 && o.y == 0) { k00 = true; k10 = h0; k01 = v0; k11 = (h0 && v1) || (v0 && h1); }
    else if (o.x == 1 && o.y == 0) { k00 = h0; k10 = true; k01 = (h0 && v0) || (v1 && h1); k11 = v1; }
    else if (o.x == 0 && o.y == 1) { k00 = v0; k10 = (v0 && h0) || (h1 && v1); k01 = true; k11 = h1; }
    else { k00 = (v1 && h0) || (h1 && v0); k10 = v1; k01 = h1; k11 = true; }
    float4 ok = float4(k00 && c00.valid ? 1 : 0, k10 && c10.valid ? 1 : 0, k01 && c01.valid ? 1 : 0, k11 && c11.valid ? 1 : 0);
    float4 F = float4(c00.fill, c10.fill, c01.fill, c11.fill);
    float mine = o.x == 0 ? (o.y == 0 ? F.x : F.z) : (o.y == 0 ? F.y : F.w);
    F = lerp(mine.xxxx, F, ok);
    return lerp(lerp(F.x, F.y, f.x), lerp(F.z, F.w, f.x), f.y);
}

float3 PoolSkyColor(float3 dir)
{
    float up = saturate(dir.y);
    float3 sky = lerp(_PoolSkyHorizon.rgb, _PoolSkyZenith.rgb, sqrt(up));
    float s = saturate(dot(dir, POOL_SUN));
    sky += _PoolSunColor.rgb * (pow(s, 2000.0) * 40.0 + pow(s, 24.0) * 0.12);
    return sky;
}

// Ambient light at a point: the sky's share everywhere, plus the baked bounce (warm, from sunlit tiles).
float3 PoolAmbientAt(float3 p, float3 n)
{
    float fill = PoolFill(p + n * 0.05);
    // Bounce comes up off the sunlit floors: ceilings and walls take it, floors a little less.
    float facing = lerp(1.08, 0.82, saturate(n.y));
    return _PoolAmbient.rgb + _PoolBounce.rgb * fill * facing;
}

float2 PoolHash22(float2 p)
{
    float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    q += dot(q, q.yzx + 33.33);
    return frac((q.xx + q.yz) * q.zy);
}

// Distance to the nearest cell border of drifting points: thin bright lines where it is near zero.
float PoolCellEdge(float2 p, float t)
{
    float2 ip = floor(p), fp = frac(p);
    float d1 = 8, d2 = 8;
    [unroll] for (int j = -1; j <= 1; j++)
    [unroll] for (int i = -1; i <= 1; i++)
    {
        float2 o = float2(i, j);
        float2 h = PoolHash22(ip + o);
        float2 pt = o + 0.5 + 0.38 * sin(t * (0.5 + 0.5 * h) + 6.2831 * h) - fp;
        float d = dot(pt, pt);
        if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
    }
    return sqrt(d2) - sqrt(d1);
}

// Light focused by the water surface onto the basin floor and walls.
float PoolCaustics(float2 xz, float t)
{
    float a = PoolCellEdge(xz * 2.3 + float2(0.0, t * 0.05), t * 0.9);
    float b = PoolCellEdge(xz * 3.1 + float2(t * 0.04, 3.7), t * 1.1);
    float la = pow(saturate(1.0 - a * 3.0), 6.0), lb = pow(saturate(1.0 - b * 3.0), 6.0);
    return 0.55 + 1.3 * (la + lb) + 1.6 * la * lb;
}

// Slope of the rippled water (from the ripple simulation around the player), per metre.
float2 PoolRippleSlope(float2 xz)
{
    if (_PoolRippleRect.w <= 0) return 0;
    float2 uv = (xz - _PoolRippleRect.xy) / _PoolRippleRect.z;
    if (any(uv < 0.02) || any(uv > 0.98)) return 0;
    const float e = 1.0 / 256.0;
    float hx = SAMPLE_TEXTURE2D_LOD(_PoolRipple, sampler_PoolRipple, uv + float2(e, 0), 0).r - SAMPLE_TEXTURE2D_LOD(_PoolRipple, sampler_PoolRipple, uv - float2(e, 0), 0).r;
    float hz = SAMPLE_TEXTURE2D_LOD(_PoolRipple, sampler_PoolRipple, uv + float2(0, e), 0).r - SAMPLE_TEXTURE2D_LOD(_PoolRipple, sampler_PoolRipple, uv - float2(0, e), 0).r;
    float edge = saturate(min(min(uv.x, uv.y), min(1 - uv.x, 1 - uv.y)) * 12.0 - 0.25);
    return float2(hx, hz) / (2.0 * e * _PoolRippleRect.z) * _PoolRippleRect.w * edge;
}

#endif
