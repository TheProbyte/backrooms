// The Poolrooms: shared by PoolTile, PoolWater and PoolSky (Resources/Shaders).
// Light here is the sun, traced through a per-cell map of the loaded Poolrooms (see PoolWorld.cs and PoolSun.cs, the
// C# copy of the same test): a ray toward the sun walks the 1.2 m cell grid until it meets the ceiling, and the point
// is sunlit if it gets there inside a roof opening and leaves the light well above it. Walls, door headers, lower
// ceilings and round columns stop it. Bounce light was baked per cell from where the sun lands (map alpha).
#ifndef BACKROOMS_POOL_COMMON
#define BACKROOMS_POOL_COMMON

TEXTURE2D(_PoolMap);                 // RGBA32 linear, toroidal by world cell: r flags, g ceiling/10, b opening (bit 7: has extras), a bounce
TEXTURE2D(_PoolMap2);                // the extras (read only where flagged): r depth/0.005, g gallery/0.05, b lanes|drain|disc radius, a disc centre
float4 _PoolMapParams;               // x map size in cells, y height between storeys, z storeys in the map (stacked rows)
TEXTURE2D(_PoolRipple); SAMPLER(sampler_PoolRipple);
TEXTURE2D(_PoolFlow);                // the simulation's flow (xy, m/s) and foam (z), same window as the ripples
float4 _PoolRippleRect;              // xy world xz of the window corner, z window size (m), w slope gain (0 = none)
float4 _PoolSunColor;                // rgb: sun (already times intensity)
float4 _PoolAmbient;                 // rgb: light everywhere (sky through the openings, scattered)
float4 _PoolBounce;                  // rgb: bounce at full baked strength
float4 _PoolSkyZenith, _PoolSkyHorizon;
float _PoolScreenTextures;           // 1: this camera has the opaque and depth textures (the main view)
float _PoolLitBlend;                 // 1: this camera looks at the Poolrooms (Backrooms/Lit adds their light)
float _PoolSpecAA;                   // 1: specular anti-aliasing on the glossy tiles (Graphics menu)
float _BROverflow;                   // 1: the Overflow art style (2026-11-07): the Poolrooms painted (ArtStyle, PoolMaterials)

// Soft steps (the painted look): x held flat at multiples of s, rising to the next over the last quarter of each step.
float PoolSoftSteps(float x, float s)
{
    float q = x / s, f = frac(q);
    return (floor(q) + smoothstep(0.72, 1.0, f)) * s;
}
#ifndef BACKROOMS_HAS_PORTAL_CLIP_PLANE
float4 _PortalClipPlane;             // zero outside a portal-camera render
// Screen-space light (ScreenLighting.cs, the player's view only): the near bounce found on screen. Where it is on, the
// SSAO these shaders read is its visibility, used in full: what hides part of a surface's hemisphere gives back its own
// light here, so corners and floor-wall joins no longer go dark (why SSAO used to be cut to 38 % in the Poolrooms).
TEXTURE2D(_BRScreenGI);
float4 _BRScreen;                    // x: on for this camera, y: bounce strength
float4 _BRScreenSize;                // xy: size in pixels
float3 PoolScreenBounce(float4 positionCS)
{
    if (_BRScreen.x < 0.5) return 0;
    int2 px = clamp((int2)positionCS.xy, 0, (int2)_BRScreenSize.xy - 1);
    return max(LOAD_TEXTURE2D(_BRScreenGI, px).rgb, 0) * _BRScreen.y;
}
// (2026-11-01, the water and effects pass) Its screen-space reflection: what a glossy surface mirrors that is on screen
// (props, people, arches, curved walls, slides: none of them are in the cell map the trace walks). rgb the light, a how
// much was found (0 where the screen had nothing; the trace is kept there). offsetPx bends where it is read (the tiles'
// own tilt, which the depth-normals the trace used do not have).
TEXTURE2D(_BRScreenRefl);
float4 PoolScreenReflection(float4 positionCS, float2 offsetPx)
{
    if (_BRScreen.x < 0.5 || _BRScreen.z <= 0.0) return 0;
    int2 px = clamp((int2)(positionCS.xy + offsetPx), 0, (int2)_BRScreenSize.xy - 1);
    float4 r = LOAD_TEXTURE2D(_BRScreenRefl, px);
    if (!all(isfinite(r))) return 0;
    return float4(max(r.rgb, 0), saturate(r.a * _BRScreen.z));
}
#endif

#define POOL_C 1.2
#define POOL_TILE 0.15
#define POOL_SHAFT 1.2
// A light well's height: under another storey it stops 1.15 m below that storey's floor (PoolBiome.Shaft, 2026-11-01).
float PoolShaft(float ceiling, int storey);
#define POOL_HEADER 2.7
#define POOL_PILLAR_R 0.4
#define POOL_ROUND_R 0.5
#define POOL_WATER (-0.12)
#define POOL_BASIN 0.45
// High windows in walls (edge code 3; PoolBiome.Window*): clerestories, from the lower room's roof (its ceiling + ROOF) to
// HEAD under the taller ceiling, inset from the cell's ends.
#define POOL_WIN_ROOF 0.35
#define POOL_WIN_HEAD 0.45
#define POOL_WIN_IN 0.3
float4 _PoolFar;                     // x, y: distance (m) the far fade starts and ends (0: none), so nothing ends in black
static const float3 POOL_SUN = float3(0.420764, 0.781419, 0.460837);   // normalize(0.42, 0.78, 0.46), as PoolBiome.SunDirection

struct PoolCell
{
    bool valid, basin, pillar, pillarNear;
    uint west, south;    // 0 open, 1 wall, 2 headed doorway, 3 wall with a high window
    uint sky, joined;
    float ceiling, fill;
    // Extras (2026-10-31): pool depth (0: the usual), gallery floor (0: none), lanes (0 none, 1 along X, 2 along Z), a
    // drain, a round opening's centre (m from the cell's corner) and radius.
    float depth, gallery, discR;
    float2 disc;
    uint lanes;
    bool drain;
};

// Storey a point belongs to (its floor is up to 0.6 m below a storey line: the basins).
int PoolStorey(float y) { return clamp((int)floor((y + 0.6) / max(_PoolMapParams.y, 1.0)), 0, max((int)_PoolMapParams.z - 1, 0)); }
float PoolBase(int storey) { return storey * _PoolMapParams.y; }

PoolCell PoolLoad(int2 cell, int storey)
{
    uint n = (uint)_PoolMapParams.x;
    uint2 s = (uint2)(cell + (int)n * 4096) % n;      // cells are well within 4096 maps of the origin
    s.y += (uint)storey * n;
    uint4 v = (uint4)(LOAD_TEXTURE2D(_PoolMap, s) * 255.0 + 0.5);
    PoolCell c;
    c.valid = (v.r & 64u) != 0;
    c.west = v.r & 3u; c.south = (v.r >> 2) & 3u;
    c.basin = (v.r & 16u) != 0; c.pillar = (v.r & 32u) != 0; c.pillarNear = (v.r & 128u) != 0;
    c.ceiling = v.g * 0.1;
    c.sky = v.b & 7u; c.joined = (v.b >> 3) & 15u;
    c.fill = v.a / 255.0;
    c.depth = 0; c.gallery = 0; c.discR = 0; c.disc = 0; c.lanes = 0; c.drain = false;
    [branch] if ((v.b & 128u) != 0)
    {
        uint4 e = (uint4)(LOAD_TEXTURE2D(_PoolMap2, s) * 255.0 + 0.5);
        c.depth = e.r * 0.005; c.gallery = e.g * 0.05;
        c.lanes = e.b & 3u; c.drain = (e.b & 4u) != 0; c.discR = (e.b >> 4) * 0.3;
        c.disc = float2(((int)(e.a & 15u) - 8) * 0.5, ((int)(e.a >> 4) - 8) * 0.5) * POOL_C;
    }
    return c;
}

int2 PoolCellOf(float3 p) { return (int2)floor(p.xz / POOL_C); }

// A wall, with or without a high window in it.
bool PoolSolidEdge(uint e) { return e == 1u || e == 3u; }

// Signed distance (m) from a point on a window's edge line (along: 0..1.2 within the cell; y above the deck) to the
// window's border, > 0 inside: above the lower room's roof, under the taller ceiling.
float PoolWindowSD(float along, float y, float ceilA, float ceilB)
{
    return min(min(along - POOL_WIN_IN, POOL_C - POOL_WIN_IN - along), min(y - min(ceilA, ceilB) - POOL_WIN_ROOF, max(ceilA, ceilB) - POOL_WIN_HEAD - y));
}

// Far off, everything fades into the fog's colour before the loaded world ends (it showed black there).
float3 PoolFarFade(float3 color, float3 pw)
{
    if (_PoolFar.y <= 0) return color;
    float f = smoothstep(_PoolFar.x, _PoolFar.y, length(pw - GetCameraPositionWS()));
    return lerp(color, unity_FogColor.rgb, f);
}

float PoolHoleSD(uint shape, uint joined, float2 uv);
// The same for a loaded cell (a round hall's big round opening knows its own centre).
float PoolHoleSDC(PoolCell c, float2 uv)
{
    if (c.sky == 6u) return c.discR - length(uv - c.disc);
    return PoolHoleSD(c.sky, c.joined, uv);
}

// Signed distance (m) from a point inside the cell (0..1.2 each way) to the edge of its roof opening, > 0 inside.
// Shapes as PoolBiome.HoleRect: 1 full, 2 slot along x, 3 slot along z, 4 square, 5 round.
float PoolHoleSD(uint shape, uint joined, float2 uv)
{
    if (shape == 0) return -1.0;
    if (shape == 5) return POOL_ROUND_R - length(uv - 0.6);
    const float rim = 0.15;
    float x0 = (joined & 1u) ? -1.0 : rim, x1 = (joined & 2u) ? 2.2 : 1.2 - rim;
    float z0 = (joined & 4u) ? -1.0 : rim, z1 = (joined & 8u) ? 2.2 : 1.2 - rim;
    float4 r = shape == 1 ? float4(x0, z0, x1, z1) : shape == 2 ? float4(x0, 0.3, x1, 0.9)
             : shape == 3 ? float4(0.3, z0, 0.9, z1) : float4(0.2, 0.2, 1.0, 1.0);
    return min(min(uv.x - r.x, r.z - uv.x), min(uv.y - r.y, r.w - uv.y));
}

bool PoolPillarHit(int2 c, int storey, float3 p, float t0, float t1)
{
    const float3 L = POOL_SUN;
    [unroll] for (int k = 0; k < 4; k++)
    {
        int2 v = c + int2(k & 1, k >> 1);
        if (!PoolLoad(v, storey).pillar) continue;
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

// The player's shadow (PlayerShadow.cs, 2026-10-31): a few capsules standing in for a body that is not shown, sent each
// frame. There are no shadow maps here, so as Unreal's capsule shadows do: the sun's ray from a point passes each
// capsule's axis at some distance; within its radius it is shadowed, softening the further the capsule is from the point.
float4 _PoolCapsA[12], _PoolCapsB[12];  // a.xyz, radius; b.xyz (1: the torso, the player's middle)
float _PoolCapsN;

// The body's own shadow map (2026-11-01; PlayerShadow.cs, PlayerShadowMap.shader; the owner found the capsules'
// shadow "disjointed and not connected"): the body as the sun sees it, its exact outline, as Unreal's per-object shadows.
// Soft as PCSS: the blockers' average distance sets the filter's width (the sun's disc: about 1 cm + 2.2 cm a metre).
TEXTURE2D(_PoolBodyMap); SAMPLER(sampler_PoolBodyMap);
float4x4 _PoolBodyMtx;               // world -> (u, v, distance toward the sun)
float4 _PoolBodyParams;              // x on (0/1), y the map's width (m)

float PoolBodySun(float3 p)
{
    if (_PoolBodyParams.x < 0.5) return 1;
    float3 m = mul(_PoolBodyMtx, float4(p, 1)).xyz;
    if (any(m.xy < 0.002) || any(m.xy > 0.998)) return 1;
    const float bias = 0.03, GA = 2.39996323;
    float perM = 1.0 / _PoolBodyParams.y;
    // Blockers: eight taps over 5 cm.
    float blk = 0, nb = 0;
    [unroll] for (int i = 0; i < 8; i++)
    {
        float r = sqrt((i + 0.5) / 8.0) * 0.05 * perM, a = i * GA;
        float o = SAMPLE_TEXTURE2D_LOD(_PoolBodyMap, sampler_PoolBodyMap, m.xy + r * float2(cos(a), sin(a)), 0).r;
        if (o > m.z + bias) { blk += o - m.z; nb += 1; }
    }
    if (nb < 0.5) return 1;
    float pen = (0.008 + 0.022 * blk / nb) * perM;
    float lit = 0;
    [unroll] for (int j = 0; j < 16; j++)
    {
        float r = sqrt((j + 0.5) / 16.0) * pen, a = j * GA + 0.7;
        float o = SAMPLE_TEXTURE2D_LOD(_PoolBodyMap, sampler_PoolBodyMap, m.xy + r * float2(cos(a), sin(a)), 0).r;
        lit += o > m.z + bias ? 0 : 1;
    }
    return lit / 16.0;
}

float PoolCapsuleSun(float3 p)
{
    float body = PoolBodySun(p);
    if (_PoolCapsN < 0.5) return body;
    // Far from the player: nothing to test (the shadow falls within a few metres).
    float3 near = _PoolCapsA[1].xyz - p;
    if (dot(near, near) > 25.0) return body;
    const float3 L = POOL_SUN;
    float vis = 1;
    [loop] for (int i = 0; i < (int)_PoolCapsN; i++)
    {
        float3 a = _PoolCapsA[i].xyz, d = _PoolCapsB[i].xyz - a;
        float r = _PoolCapsA[i].w, dd = dot(d, d);
        // The point on the axis nearest the ray, then the ray's point nearest that (twice: close enough for capsules).
        float s = dd > 1e-6 ? saturate(dot(p + L * max(0.0, dot(a - p, L)) - a, d) / dd) : 0;
        float t = max(0.0, dot(a + d * s - p, L));
        s = dd > 1e-6 ? saturate(dot(p + L * t - a, d) / dd) : 0;
        t = max(0.0, dot(a + d * s - p, L));
        float dist = length(p + L * t - (a + d * s));
        // The penumbra: a little wider the further the capsule is (the sun's disc and the sky's spread; much more and the
        // thighs, highest of the limbs, faded out).
        float pen = 0.01 + t * 0.022;
        vis *= lerp(1.0, smoothstep(r - pen, r + pen, dist), saturate(t * 8.0));
    }
    return vis * body;
}

// ---------------- Passing clouds, glazed roof lights (2026-11-01, the realism plan 2 and 6) ----------------

// Clouds drifting over the sun: a deck of noise 300 m up, read where the sun's ray from a point crosses it, so a cloud's
// shadow sweeps across every opening, beam and caustic together (as Unreal's cloud shadows). Set by PoolWorld.
float4 _PoolCloud;                   // xy drift (m), z cover (0..1), w how much a cloud takes of the sun (0: off)
float PoolValueNoise(float2 p);
float2 PoolHash22(float2 p);
float PoolCloudCover(float2 deck)
{
    // (2026-11-01, the review: soft blobs.) Warped by a second noise for billowing shapes, a crisper edge, and over some
    // regions (a few hundred metres across, fixed in the world) the sky is overcast all day.
    float2 s = (deck + _PoolCloud.xy) * 0.004;
    float2 warp = float2(PoolValueNoise(s * 0.7 + 3.1), PoolValueNoise(s * 0.7 + 8.4)) - 0.5;
    s += warp * 1.2;
    float n = PoolValueNoise(s) * 0.5 + PoolValueNoise(s * 2.03 + 17.1) * 0.3 + PoolValueNoise(s * 4.1 + 5.3) * 0.2;
    float cover = _PoolCloud.z + 0.55 * smoothstep(0.62, 0.78, PoolValueNoise(deck * 0.0011 + 41.0));
    return smoothstep(1.0 - cover, 1.0 - cover + 0.12, n);
}
float PoolCloudShade(float3 pw)
{
    if (_PoolCloud.w <= 0) return 1;
    return 1.0 - _PoolCloud.w * PoolCloudCover(pw.xz + POOL_SUN.xz / POOL_SUN.y * (300.0 - pw.y));
}

// Some blocks of rooms have glazed roof lights: a frame of bars every 0.6 m and a dusty pane at the top of each light
// well (the frames are built by OfficeChunkBuilder.GlazedFrames with the same hash, PoolBiome.Glazed).
bool PoolGlazed(int2 cell, uint shape)
{
    if (shape == 0u || shape >= 5u) return false;
    int2 b = (int2)floor(cell / 12.0);
    uint h = ((uint)b.x * 73856093u) ^ ((uint)b.y * 19349663u);
    h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
    return (h % 100u) < 45u;
}
// A pane now and then is broken out (2026-11-01, the review; a Backrooms tell): the sun comes through it whole.
bool PoolPaneBroken(float2 xz)
{
    float2 id = floor(xz / 0.6);
    return PoolHash22(id + 71.3).x < 0.035;
}
float PoolMullions(float2 xz, float blur)
{
    float2 d = abs(frac(xz / 0.6 + 0.5) - 0.5) * 0.6;          // to the nearest bar (m)
    return (PoolPaneBroken(xz) ? 1.0 : 0.82) * smoothstep(0.02 - blur, 0.02 + blur, min(d.x, d.y));
}
// What happens to the sun at the top of a light well (world point there, the cell below, its opening).
float PoolSkyGate(float3 top, int2 cell, uint shape, float blur)
{
    float g = PoolCloudShade(top);
    if (PoolGlazed(cell, shape)) g *= PoolMullions(top.xz, 0.003 + blur);
    return g;
}

// Some high windows are glass block (70s and 80s pool halls; OfficeChunkBuilder.WindowFaces, the same hash): the sun
// comes through them scattered, a dim even glow where the clear ones throw a beam.
bool PoolGlassBlock(int2 cellWithEdge, uint side)
{
    uint h = ((uint)cellWithEdge.x * 2654435761u) ^ ((uint)cellWithEdge.y * 40503u) ^ (side * 97u);
    h ^= h >> 15; h *= 0x2c1b3c6du; h ^= h >> 12;
    return (h % 100u) < 40u;
}

float PoolSunVisibilityMap(float3 pw);
// Sunlight reaching a world point: the traced sun (below), and the player's shadow.
float PoolSunVisibility(float3 pw)
{
    float v = PoolSunVisibilityMap(pw);
    return v > 0.001 ? v * PoolCapsuleSun(pw) : v;
}

// Sunlight reaching a world point: 0 shade .. 1 full sun (soft only at the edges of the openings).
float PoolSunVisibilityMap(float3 pw)
{
    const float3 L = POOL_SUN;        // x and z are positive: the ray only steps +x / +z
    int storey = PoolStorey(pw.y);
    float3 p = pw; p.y -= PoolBase(storey);
    int2 c = PoolCellOf(p);
    PoolCell cell = PoolLoad(c, storey);
    float t = 0;
    [loop] for (int i = 0; i < 20; i++)
    {
        if (!cell.valid) return 0;
        float tCeil = (cell.ceiling - p.y) / L.y;
        float tx = ((c.x + 1) * POOL_C - p.x) / L.x;
        float tz = ((c.y + 1) * POOL_C - p.z) / L.z;
        float tExit = min(tx, tz);
        if (cell.pillarNear && PoolPillarHit(c, storey, p, t, min(tExit, tCeil))) return 0;
        // A gallery overhead: below it, it is this cell's ceiling (with no opening).
        if (cell.gallery > 0 && p.y + L.y * t < cell.gallery - 0.01 && (cell.gallery - 0.2 - p.y) / L.y <= tExit) return 0;
        if (tCeil <= tExit)
        {
            float3 q = p + L * tCeil;
            float d1 = PoolHoleSDC(cell, q.xz - c * POOL_C);
            float t2 = tCeil + PoolShaft(cell.ceiling, storey) / L.y;
            float3 q2 = p + L * t2;
            int2 c2 = (int2)floor(q2.xz / POOL_C);
            PoolCell cell2 = PoolLoad(c2, storey);
            float d2 = (cell2.valid && cell2.sky == cell.sky) ? PoolHoleSDC(cell2, q2.xz - c2 * POOL_C) : -1.0;
            // The sun is half a degree wide: the edge of each opening blurs with distance. Passing clouds and, in some
            // rooms, a glazed roof light's frame and pane (PoolSkyGate).
            float vis = saturate(d1 / (0.006 + 0.009 * tCeil) + 0.5) * saturate(d2 / (0.006 + 0.009 * t2) + 0.5);
            return vis > 0.001 ? vis * PoolSkyGate(q2 + float3(0, PoolBase(storey), 0), c, cell.sky, 0.009 * t2) : 0;
        }
        float y = p.y + L.y * tExit;
        int2 nc = c + (tx <= tz ? int2(1, 0) : int2(0, 1));
        PoolCell n = PoolLoad(nc, storey);
        uint edge = tx <= tz ? n.west : n.south;
        if (edge == 3u)
        {
            // A high window: the sky beyond (its edges soften with distance, as an opening's do).
            float along = tx <= tz ? p.z + L.z * tExit - c.y * POOL_C : p.x + L.x * tExit - c.x * POOL_C;
            float wv = saturate(PoolWindowSD(along, y, cell.ceiling, n.ceiling) / (0.006 + 0.009 * tExit) + 0.5) * PoolCloudShade(pw);
            return PoolGlassBlock(nc, tx <= tz ? 1u : 2u) ? wv * 0.3 : wv;
        }
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
    int s = PoolStorey(p.y);
    PoolCell c00 = PoolLoad(b, s), c10 = PoolLoad(b + int2(1, 0), s), c01 = PoolLoad(b + int2(0, 1), s), c11 = PoolLoad(b + int2(1, 1), s);
    bool h0 = !PoolSolidEdge(c10.west), h1 = !PoolSolidEdge(c11.west);      // between the columns, rows 0 / 1
    bool v0 = !PoolSolidEdge(c01.south), v1 = !PoolSolidEdge(c11.south);    // between the rows, columns 0 / 1
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

// The water's flow (xy, m/s) and foam (z, 0..1.5) from the simulation round the player; none outside its window.
float3 PoolFlowAt(float2 xz)
{
    if (_PoolRippleRect.w <= 0) return 0;
    float2 uv = (xz - _PoolRippleRect.xy) / _PoolRippleRect.z;
    if (any(uv < 0.02) || any(uv > 0.98)) return 0;
    float edge = saturate(min(min(uv.x, uv.y), min(1 - uv.x, 1 - uv.y)) * 12.0 - 0.25);
    return SAMPLE_TEXTURE2D_LOD(_PoolFlow, sampler_PoolRipple, uv, 0).xyz * edge;
}

float PoolFoamAt(float2 xz) { return PoolFlowAt(xz).z; }

// Smooth value noise (0..1).
float PoolValueNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = PoolHash22(i).x, b = PoolHash22(i + float2(1, 0)).x, c = PoolHash22(i + float2(0, 1)).x, d = PoolHash22(i + 1).x;
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// Round bubbles of mixed sizes, one per cell at most (1 in a bubble's middle, 0 between them).
float PoolBubbles(float2 p)
{
    float2 ip = floor(p), fp = frac(p);
    float v = 0;
    [unroll] for (int j = -1; j <= 1; j++)
    [unroll] for (int i = -1; i <= 1; i++)
    {
        float2 o = float2(i, j);
        float2 h = PoolHash22(ip + o);
        float r = 0.22 + 0.3 * h.y;
        float d = length(o + 0.2 + 0.6 * h - fp);
        v = max(v, 1.0 - smoothstep(r * 0.35, r, d));
    }
    return v;
}

// Foam's pattern (0..1): clumps (two octaves of noise, ~30 cm) with bubbles in them at two sizes (~10 and ~4 cm); the
// small bubbles give way to their average where they would be finer than a pixel.
float PoolFoamPattern(float2 xz, float pix)
{
    float clump = PoolValueNoise(xz * 3.3) * 0.65 + PoolValueNoise(xz * 7.9 + 4.1) * 0.35;
    float big = PoolBubbles(xz * 10.0);
    float small = lerp(PoolBubbles(xz * 24.0 + 7.3), 0.3, saturate(pix * 24.0 / 0.7));
    return clump * 0.6 + max(big, small * 0.85) * 0.5;
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
    float2 sl = float2(hx, hz) / (2.0 * e * _PoolRippleRect.z) * _PoolRippleRect.w * edge;
    // (2026-11-01) A broken or runaway simulation must not tip the whole surface over into a mirror: real ripples are
    // gentle (a slope of a few tenths at most).
    if (!(abs(sl.x) < 100.0) || !(abs(sl.y) < 100.0)) return 0;
    float m = length(sl);
    return m > 0.6 ? sl * (0.6 / m) : sl;
}

// Height of the rippled water above its rest (m, as the slopes are scaled), for the crests' glow.
float PoolRippleHeight(float2 xz)
{
    if (_PoolRippleRect.w <= 0) return 0;
    float2 uv = (xz - _PoolRippleRect.xy) / _PoolRippleRect.z;
    if (any(uv < 0.02) || any(uv > 0.98)) return 0;
    float edge = saturate(min(min(uv.x, uv.y), min(1 - uv.x, 1 - uv.y)) * 12.0 - 0.25);
    float rh = SAMPLE_TEXTURE2D_LOD(_PoolRipple, sampler_PoolRipple, uv, 0).r * _PoolRippleRect.w * edge;
    return abs(rh) < 1.0 ? rh : 0.0;
}

// ---------------- Small waves, caustics, reflections (2026-10-31) ----------------

TEXTURE2D(_PoolCaustic); SAMPLER(sampler_PoolCaustic);
float4 _PoolCausticParams;           // x tile size (m), y 1 when the map exists, z strength under water, w reflected
float _PoolWaveTime;                 // the waves' clock (PoolCausticMap: slowed to a calm)

#define POOL_WAVE_TILE 2.4
#define POOL_WATER_DEPTH 0.33        // surface to basin floor
// The water's small waves: whole numbers of waves across the 2.4 m tile (so it tiles), 11-38 cm long.
static const float3 POOL_WAVES[12] =
{
    float3(6, -2, 0.3), float3(-3, 7, 2.1), float3(9, 4, 4.0), float3(-8, 9, 1.2), float3(4, -13, 5.3), float3(14, 5, 0.7),
    float3(-11, -12, 3.3), float3(17, -4, 2.6), float3(-15, 10, 5.9), float3(6, 19, 1.9), float3(21, 7, 4.4), float3(-13, -17, 0.2)
};

// Slope (dh/dx, dh/dz) of the small waves at a point. Each wave curves the surface about the same (amplitude falls with
// the square of its number: 5 mm at 24 cm), so together they bring the sun to a focus a little below the basin floor:
// a soft network of bright lines on it. Capillary-gravity speeds, slowed by the clock.
float2 PoolWaveSlope(float2 x, float t)
{
    float2 s = 0;
    [unroll] for (int i = 0; i < 12; i++)
    {
        float2 n = POOL_WAVES[i].xy;
        float2 k = n * (6.2831853 / POOL_WAVE_TILE);
        float kl = length(k);
        float w = sqrt(9.81 * kl + 7.3e-5 * kl * kl * kl);
        float A = 0.5 / dot(n, n);
        s += A * k * cos(dot(k, x) - w * t + POOL_WAVES[i].z);
    }
    return s;
}

// How the player's ripples focus the sun at a depth below them (1 none, more where a wave's crest gathers the light,
// less under its troughs): the surface's curvature (the height's Laplacian) bends the light as a lens would, by the
// refraction's share (1 - 1/1.333) times the depth (the Jacobian of the refracted rays; 2026-10-31: in place of warping
// the caustic map, which the owner saw as a "spiderweb" round the body).
float PoolRippleFocus(float2 xz, float below)
{
    if (_PoolRippleRect.w <= 0) return 1;
    float2 uv = (xz - _PoolRippleRect.xy) / _PoolRippleRect.z;
    if (any(uv < 0.02) || any(uv > 0.98)) return 1;
    const float e = 1.5 / 256.0;
    float h = SAMPLE_TEXTURE2D_LOD(_PoolRipple, sampler_PoolRipple, uv, 0).r;
    float n4 = SAMPLE_TEXTURE2D_LOD(_PoolRipple, sampler_PoolRipple, uv + float2(e, 0), 0).r + SAMPLE_TEXTURE2D_LOD(_PoolRipple, sampler_PoolRipple, uv - float2(e, 0), 0).r
             + SAMPLE_TEXTURE2D_LOD(_PoolRipple, sampler_PoolRipple, uv + float2(0, e), 0).r + SAMPLE_TEXTURE2D_LOD(_PoolRipple, sampler_PoolRipple, uv - float2(0, e), 0).r;
    float dx = e * _PoolRippleRect.z;
    float lap = (n4 - 4.0 * h) / (dx * dx);
    float edge = saturate(min(min(uv.x, uv.y), min(1 - uv.x, 1 - uv.y)) * 12.0 - 0.25);
    return lerp(1.0, clamp(1.0 / max(0.3, 1.0 + below * 0.25 * lap), 0.45, 2.2), edge);
}

// The sun as it reaches a point under the water: the caustic map at the spot on the surface its light came through,
// split very slightly by colour (blue bends more), and the player's ripples focusing it; full contrast at the basin's
// depth, none at the surface. Falls back to the drawn pattern without the map.
float3 PoolCausticLight(float3 p, float waterY)
{
    float below = waterY - p.y;
    if (below <= 0) return 1;
    float3 up = -refract(-POOL_SUN, float3(0, 1, 0), 1.0 / 1.333);     // back along the refracted sun ray
    float3 s = p + up * (below / up.y);
    float k = saturate(below / POOL_WATER_DEPTH);
    if (_PoolCausticParams.y < 0.5) return lerp(1.0, PoolCaustics(s.xz, _Time.y), k);
    float2 slope = PoolRippleSlope(s.xz);
    float2 uv = (s.xz + slope * below * 0.15) / _PoolCausticParams.x;
    float2 split = normalize(POOL_SUN.xz) * (0.0035 * k / _PoolCausticParams.x);
    float3 c;
    c.r = SAMPLE_TEXTURE2D(_PoolCaustic, sampler_PoolCaustic, uv - split).r;
    c.g = SAMPLE_TEXTURE2D(_PoolCaustic, sampler_PoolCaustic, uv).r;
    c.b = SAMPLE_TEXTURE2D(_PoolCaustic, sampler_PoolCaustic, uv + split).r;
    return lerp(1.0, c, k * _PoolCausticParams.z) * PoolRippleFocus(s.xz, below);
}

// Is the straight path from p toward +x/+z (going down) clear of walls, headers and lower ceilings for t metres?
bool PoolPathClear(float3 pw, float3 dir, float tEnd)
{
    int storey = PoolStorey(pw.y);
    float3 p = pw; p.y -= PoolBase(storey);
    int2 c = PoolCellOf(p);
    PoolCell cell = PoolLoad(c, storey);
    [loop] for (int i = 0; i < 8; i++)
    {
        if (!cell.valid) return false;
        float tx = ((c.x + 1) * POOL_C - p.x) / dir.x;
        float tz = ((c.y + 1) * POOL_C - p.z) / dir.z;
        float tExit = min(tx, tz);
        if (tExit >= tEnd) return true;
        float y = p.y + dir.y * tExit;
        int2 nc = c + (tx <= tz ? int2(1, 0) : int2(0, 1));
        PoolCell n = PoolLoad(nc, storey);
        uint edge = tx <= tz ? n.west : n.south;
        if (PoolSolidEdge(edge) || (edge == 2u && y > POOL_HEADER) || !n.valid || y >= n.ceiling) return false;
        c = nc; cell = n;
    }
    return false;
}

// Sunlight thrown up off sunlit water onto a wall or ceiling (the wavy light that moves on pool walls): from the point,
// back along the reflected sun ray to the water; lit if that water is in the sun and the way is clear. Rippled by the
// caustic map seen from further off (larger, softer).
float3 PoolWaterLight(float3 p, float3 n)
{
    if (_PoolCausticParams.w <= 0) return 0;
    const float3 back = float3(POOL_SUN.x, -POOL_SUN.y, POOL_SUN.z);        // from the point down toward the water
    float facing = saturate(dot(n, back));                                   // faces the light coming up off the water
    if (facing <= 0) return 0;
    int storey = PoolStorey(p.y);
    float waterY = PoolBase(storey) + POOL_WATER;
    float t = (p.y - waterY) / POOL_SUN.y;
    if (t <= 0.02 || t > 7.0) return 0;
    float3 q = p + back * t;
    PoolCell qc = PoolLoad(PoolCellOf(q), storey);
    if (!qc.basin || !qc.valid) return 0;
    float sun = PoolSunVisibility(q + float3(0, 0.01, 0));
    if (sun <= 0.01) return 0;
    if (!PoolPathClear(p + n * 0.03, back, t)) return 0;
    float2 uv = q.xz / _PoolCausticParams.x * 0.45;
    float c = _PoolCausticParams.y > 0.5 ? SAMPLE_TEXTURE2D_LOD(_PoolCaustic, sampler_PoolCaustic, uv, 1.5 + t * 0.4).r : 1.0;
    float spread = 1.0 / (1.0 + t * 0.35);                   // the further it goes, the softer and fainter
    c = lerp(1.0, c, spread);
    return _PoolSunColor.rgb * sun * facing * 0.024 * 3.0 * c * spread * _PoolCausticParams.w;
}

// ---- Reflections: a ray traced through the cell map (walls, headers, lower ceilings, columns, floors, the water, the
// sky through the openings), what it meets lit as everything else is. Used by the glazed tiles and, where the screen
// has nothing, by the water.

#define POOL_HIT_NONE 0
#define POOL_HIT_TILE 1
#define POOL_HIT_SKY 2
#define POOL_HIT_WATER 3
#define POOL_HIT_WELL 4
static const float3 POOL_WALL_ALBEDO = float3(0.78, 0.84, 0.77);
static const float3 POOL_DECK_ALBEDO = float3(0.86, 0.85, 0.78);

struct PoolHit { int kind; float t; float3 p; float3 n; };

// Where a ray from a column's side meets it (round columns stand on cell corners), or a large t.
float PoolPillarT(int2 c, int storey, float3 p, float3 R, out float3 nrm)
{
    nrm = 0;
    float best = 1e9;
    [unroll] for (int k = 0; k < 4; k++)
    {
        int2 v = c + int2(k & 1, k >> 1);
        if (!PoolLoad(v, storey).pillar) continue;
        float2 d = p.xz - v * POOL_C;
        float a = dot(R.xz, R.xz), b = dot(d, R.xz), cc = dot(d, d) - POOL_PILLAR_R * POOL_PILLAR_R;
        float disc = b * b - a * cc;
        if (disc < 0 || a < 1e-6) continue;
        float tin = (-b - sqrt(disc)) / a;
        if (tin > 1e-3 && tin < best) { best = tin; float2 h = d + R.xz * tin; nrm = normalize(float3(h.x, 0, h.y)); }
    }
    return best;
}

PoolHit PoolTrace(float3 pw, float3 R, float maxT)
{
    PoolHit h; h.kind = POOL_HIT_NONE; h.t = maxT; h.p = pw + R * maxT; h.n = -R;
    int storey = PoolStorey(pw.y);
    float base = PoolBase(storey);
    float3 p = pw; p.y -= base;
    int2 c = PoolCellOf(p);
    PoolCell cell = PoolLoad(c, storey);
    int2 st = int2(R.x > 0 ? 1 : -1, R.z > 0 ? 1 : -1);
    float t = 0;
    [loop] for (int i = 0; i < 26; i++)
    {
        if (!cell.valid || t > maxT) return h;
        float floorY = cell.basin ? POOL_WATER : 0.0, ceilY = cell.ceiling;
        bool underGallery = false;
        if (cell.gallery > 0)
        {
            if (p.y + R.y * t < cell.gallery - 0.1) { ceilY = cell.gallery - 0.2; underGallery = true; }
            else floorY = cell.gallery;
        }
        float tCeil = R.y > 1e-4 ? (ceilY - p.y) / R.y : 1e9;
        float tFloor = R.y < -1e-4 ? (floorY - p.y) / R.y : 1e9;
        float tx = abs(R.x) > 1e-5 ? ((c.x + (st.x > 0 ? 1 : 0)) * POOL_C - p.x) / R.x : 1e9;
        float tz = abs(R.z) > 1e-5 ? ((c.y + (st.y > 0 ? 1 : 0)) * POOL_C - p.z) / R.z : 1e9;
        float tExit = min(tx, tz);
        float tV = min(tCeil, tFloor);
        float3 pn;
        float tP = cell.pillarNear ? PoolPillarT(c, storey, p, R, pn) : 1e9;
        if (tP < min(tV, tExit) && tP > t - 1e-3)
        {
            h.kind = POOL_HIT_TILE; h.t = tP; h.p = pw + R * tP; h.n = pn; return h;
        }
        if (tV <= tExit)
        {
            float3 q = p + R * tV;
            h.t = tV; h.p = q + float3(0, base, 0);
            if (tCeil < tFloor)
            {
                h.n = float3(0, -1, 0);
                float d1 = underGallery ? -1.0 : PoolHoleSDC(cell, q.xz - c * POOL_C);
                if (d1 > 0)
                {
                    // Up the light well: the sky if the ray leaves its top inside the opening, else its sunny inside.
                    float3 q2 = q + R * (PoolShaft(cell.ceiling, storey) / R.y);
                    int2 c2 = (int2)floor(q2.xz / POOL_C);
                    PoolCell cell2 = PoolLoad(c2, storey);
                    float d2 = (cell2.valid && cell2.sky == cell.sky) ? PoolHoleSDC(cell2, q2.xz - c2 * POOL_C) : -1.0;
                    h.kind = d2 > 0 ? POOL_HIT_SKY : POOL_HIT_WELL;
                }
                else h.kind = POOL_HIT_TILE;
            }
            else { h.n = float3(0, 1, 0); h.kind = cell.basin && cell.gallery <= 0 ? POOL_HIT_WATER : POOL_HIT_TILE; }
            return h;
        }
        // Into the next cell, unless a wall, a header, a lower ceiling or a higher floor is in the way.
        bool alongX = tx <= tz;
        int2 nc = c + (alongX ? int2(st.x, 0) : int2(0, st.y));
        PoolCell n = PoolLoad(nc, storey);
        uint edge = alongX ? (st.x > 0 ? n.west : cell.west) : (st.y > 0 ? n.south : cell.south);
        float y = p.y + R.y * tExit;
        float nFloor = n.basin ? POOL_WATER : 0.0;
        if (edge == 3u)
        {
            float along = alongX ? p.z + R.z * tExit - c.y * POOL_C : p.x + R.x * tExit - c.x * POOL_C;
            if (PoolWindowSD(along, y, cell.ceiling, n.ceiling) > 0)
            {
                h.kind = POOL_HIT_SKY; h.t = tExit; h.p = pw + R * tExit; h.n = alongX ? float3(-st.x, 0, 0) : float3(0, 0, -st.y);
                return h;
            }
        }
        if (PoolSolidEdge(edge) || (edge == 2u && y > POOL_HEADER) || !n.valid || y >= n.ceiling || y < nFloor)
        {
            // The wall's face is half a wall's thickness short of the cell line.
            float tw = max(tExit - 0.075 / max(abs(alongX ? R.x : R.z), 0.05), t);
            h.kind = POOL_HIT_TILE; h.t = tw; h.p = pw + R * tw;
            h.n = alongX ? float3(-st.x, 0, 0) : float3(0, 0, -st.y);
            return h;
        }
        c = nc; cell = n; t = tExit;
    }
    return h;
}

// The sun on a point as a blurred reflection sees it: the opening's edge softened by blur (m) more.
float PoolSunVisibilityBlurMap(float3 pw, float blur);
float PoolSunVisibilityBlur(float3 pw, float blur)
{
    float v = PoolSunVisibilityBlurMap(pw, blur);
    return v > 0.001 ? v * PoolCapsuleSun(pw) : v;
}
float PoolSunVisibilityBlurMap(float3 pw, float blur)
{
    const float3 L = POOL_SUN;
    int storey = PoolStorey(pw.y);
    float3 p = pw; p.y -= PoolBase(storey);
    int2 c = PoolCellOf(p);
    PoolCell cell = PoolLoad(c, storey);
    float t = 0;
    [loop] for (int i = 0; i < 20; i++)
    {
        if (!cell.valid) return 0;
        float tCeil = (cell.ceiling - p.y) / L.y;
        float tx = ((c.x + 1) * POOL_C - p.x) / L.x;
        float tz = ((c.y + 1) * POOL_C - p.z) / L.z;
        float tExit = min(tx, tz);
        if (cell.gallery > 0 && p.y + L.y * t < cell.gallery - 0.01 && (cell.gallery - 0.2 - p.y) / L.y <= tExit) return 0;
        if (tCeil <= tExit)
        {
            float3 q = p + L * tCeil;
            float d1 = PoolHoleSDC(cell, q.xz - c * POOL_C);
            float t2 = tCeil + PoolShaft(cell.ceiling, storey) / L.y;
            float3 q2 = p + L * t2;
            int2 c2 = (int2)floor(q2.xz / POOL_C);
            PoolCell cell2 = PoolLoad(c2, storey);
            float d2 = (cell2.valid && cell2.sky == cell.sky) ? PoolHoleSDC(cell2, q2.xz - c2 * POOL_C) : -1.0;
            float vis = saturate(d1 / (0.006 + 0.009 * tCeil + blur) + 0.5) * saturate(d2 / (0.006 + 0.009 * t2 + blur) + 0.5);
            return vis > 0.001 ? vis * PoolSkyGate(q2 + float3(0, PoolBase(storey), 0), c, cell.sky, 0.009 * t2 + blur) : 0;
        }
        float y = p.y + L.y * tExit;
        int2 nc = c + (tx <= tz ? int2(1, 0) : int2(0, 1));
        PoolCell n = PoolLoad(nc, storey);
        uint edge = tx <= tz ? n.west : n.south;
        if (edge == 3u)
        {
            float along = tx <= tz ? p.z + L.z * tExit - c.y * POOL_C : p.x + L.x * tExit - c.x * POOL_C;
            float wv = saturate(PoolWindowSD(along, y, cell.ceiling, n.ceiling) / (0.006 + 0.009 * tExit + blur) + 0.5) * PoolCloudShade(pw);
            return PoolGlassBlock(nc, tx <= tz ? 1u : 2u) ? wv * 0.3 : wv;
        }
        if (edge == 1u || (edge == 2u && y > POOL_HEADER)) return 0;
        if (!n.valid || y >= n.ceiling) return 0;
        c = nc; cell = n; t = tExit;
    }
    return 0;
}

// What a reflected ray sees (radiance), blur: how soft the reflection is per metre travelled (glaze roughness).
float3 PoolShadeHit(PoolHit h, float3 R, float blur)
{
    float soft = h.t * blur;
    if (h.kind == POOL_HIT_SKY) return PoolSkyColor(R);
    float3 amb = PoolAmbientAt(h.p, h.n);
    if (h.kind == POOL_HIT_NONE) return POOL_WALL_ALBEDO * amb;
    if (h.kind == POOL_HIT_WELL) return POOL_WALL_ALBEDO * (amb * 1.5 + _PoolSunColor.rgb * 0.18);
    if (h.kind == POOL_HIT_WATER)
    {
        // The basin floor through the water (its sunny patches green and dimmed), and the ceiling's light on top.
        PoolCell wc = PoolLoad(PoolCellOf(h.p), PoolStorey(h.p.y));
        float3 fl = h.p - float3(0, wc.depth > 0 ? wc.depth + POOL_WATER : POOL_WATER_DEPTH, 0);
        float sunF = PoolSunVisibilityBlur(fl + float3(0, 0.02, 0), soft);
        float3 tr = exp(-float3(2.6, 0.9, 1.3) * 0.7);
        float3 floorLit = POOL_WALL_ALBEDO * (_PoolSunColor.rgb * sunF * POOL_SUN.y + amb) * tr;
        return floorLit + float3(0.03, 0.16, 0.12) * amb + 0.06 * amb;
    }
    float3 alb = h.n.y > 0.7 ? POOL_DECK_ALBEDO : POOL_WALL_ALBEDO;
    float sun = PoolSunVisibilityBlur(h.p + h.n * 0.02, soft) * saturate(dot(h.n, POOL_SUN));
    return alb * (_PoolSunColor.rgb * sun + amb);
}

#endif

// ---------------- Warm bounce from the sun patches (2026-11-01, the realism plan 7) ----------------

// Is the way between two points (same storey) clear of walls? A walk through the cell grid in any direction.
bool PoolWallsClear(float3 a, float3 b)
{
    int storey = PoolStorey(a.y);
    float base = PoolBase(storey);
    float3 d = b - a;
    int2 c = PoolCellOf(a), e = PoolCellOf(b);
    PoolCell cell = PoolLoad(c, storey);
    int2 st = int2(d.x >= 0 ? 1 : -1, d.z >= 0 ? 1 : -1);
    float2 inv = 1.0 / max(abs(d.xz), 1e-5);
    float2 next = float2(st.x > 0 ? (c.x + 1) * POOL_C - a.x : a.x - c.x * POOL_C, st.y > 0 ? (c.y + 1) * POOL_C - a.z : a.z - c.y * POOL_C) * inv;
    float2 dt = POOL_C * inv;
    [loop] for (int i = 0; i < 8; i++)
    {
        if (c.x == e.x && c.y == e.y) return true;
        bool xs = next.x < next.y;
        float t = xs ? next.x : next.y;
        if (t > 1.0) return true;
        int2 nc = c + (xs ? int2(st.x, 0) : int2(0, st.y));
        PoolCell n = PoolLoad(nc, storey);
        uint edge = xs ? (st.x > 0 ? n.west : cell.west) : (st.y > 0 ? n.south : cell.south);
        float y = a.y + d.y * t - base;
        if (PoolSolidEdge(edge) || (edge == 2u && y > POOL_HEADER) || !n.valid) return false;
        if (xs) next.x += dt.x; else next.y += dt.y;
        c = nc; cell = n;
    }
    return true;
}

// The light a lambertian quad (four corners, any winding) throws on a point with normal n, as a share of its own
// radiance times pi (Lambert's polygon formula: the edges' angles projected on the normal).
float PoolQuadFormFactor(float3 p, float3 n, float3 q0, float3 q1, float3 q2, float3 q3)
{
    float3 v[4] = { normalize(q0 - p), normalize(q1 - p), normalize(q2 - p), normalize(q3 - p) };
    float sum = 0;
    [unroll] for (int i = 0; i < 4; i++)
    {
        float3 a = v[i], b = v[(i + 1) % 4];
        float c = clamp(dot(a, b), -0.9999, 0.9999);
        float3 g = cross(a, b);
        float gl = length(g);
        if (gl > 1e-6) sum += acos(c) * dot(n, g / gl);
    }
    return abs(sum) / (2.0 * PI);
}

// The warm light off the patches of sun on the floor near a point. Each patch is a known rectangle: the part of an
// opening the sun gets through both ends of its light well, moved down along the sun to the floor. Its light is a
// lambertian area light's (as Unity's and Unreal's area lights), checked for walls between; up to two patches.
float3 PoolPatchBounce(float3 P, float3 N)
{
    int storey = PoolStorey(P.y);
    float base = PoolBase(storey);
    int2 pc = PoolCellOf(P);
    PoolCell me = PoolLoad(pc, storey);
    if (!me.valid) return 0;
    const float3 L = POOL_SUN;
    float2 run = L.xz / L.y;                                  // across per metre down
    float2 shaft = run * PoolShaft(me.ceiling, storey);
    // The openings whose patches may lie near this point are about a ceiling's height toward the sun from it.
    float2 look = P.xz + run * max(me.ceiling - (P.y - base), 0.5);
    int2 lc = (int2)floor(look / POOL_C);
    float3 sum = 0;
    int found = 0;
    [loop] for (int j = -1; j <= 1; j++)
    [loop] for (int i = -1; i <= 1; i++)
    {
        if (found >= 2) break;
        int2 hc = lc + int2(i, j);
        PoolCell h = PoolLoad(hc, storey);
        if (!h.valid || h.sky == 0u || h.sky >= 5u) continue;
        float4 r = h.sky == 1u ? float4(0.15, 0.15, 1.05, 1.05) : h.sky == 2u ? float4(0.15, 0.3, 1.05, 0.9) : h.sky == 3u ? float4(0.3, 0.15, 0.9, 1.05) : float4(0.2, 0.2, 1.0, 1.0);
        if (h.joined & 1u) r.x = 0.0; if (h.joined & 2u) r.z = 1.2; if (h.joined & 4u) r.y = 0.0; if (h.joined & 8u) r.w = 1.2;
        // Through both ends of the light well; joined openings have no well wall between their cells.
        if (!(h.joined & 2u)) r.z -= shaft.x; if (!(h.joined & 8u)) r.w -= shaft.y;
        if (r.z - r.x < 0.05 || r.w - r.y < 0.05) continue;
        float floorY = base + (h.basin ? POOL_WATER : 0.0);
        float2 o = hc * POOL_C - run * (h.ceiling - (floorY - base));
        float3 q0 = float3(o.x + r.x, floorY, o.y + r.y), q2 = float3(o.x + r.z, floorY, o.y + r.w);
        float3 q1 = float3(q2.x, floorY, q0.z), q3 = float3(q0.x, floorY, q2.z);
        float3 mid = (q0 + q2) * 0.5;
        float3 toMid = mid - P;
        float dist2 = dot(toMid, toMid);
        if (dist2 > 49.0 || dot(N, toMid) <= 0) continue;
        // The patch is really sunlit (not on the far side of a wall), and nothing stands between it and the point.
        if (PoolSunVisibilityMap(mid + float3(0, 0.02, 0)) < 0.3) continue;
        if (!PoolWallsClear(P + N * 0.05, mid + float3(0, 0.05, 0))) continue;
        float ff = PoolQuadFormFactor(P, N, q0, q1, q2, q3);
        float albedo = h.basin ? 0.25 : 0.78;                   // a sunlit deck, or water over the pool floor
        sum += albedo * ff * PoolCloudShade(mid);
        found++;
    }
    return _PoolSunColor.rgb * L.y * sum;
}

float PoolShaft(float ceiling, int storey)
{
    return storey < (int)_PoolMapParams.z - 1 ? clamp(_PoolMapParams.y - 1.15 - ceiling, 0.15, POOL_SHAFT) : POOL_SHAFT;
}
