// Backrooms/Lit's surface detail (2026-11-02, F5 > LIGHT > Surface detail; Generation/MaterialMaps.SurfaceDetail): the
// relief of a material's own texture, as current AAA shaders do it, on top of its colour and normal maps.
//  - Parallax occlusion mapping (Tatarchuk, GDC 2006): the texture is looked up where the view ray meets its relief, so a
//    carpet's pile, a ceiling tile's fissures and the wallpaper's emboss have depth that shifts as you move (close up only:
//    it fades out with distance, where the camcorder could not show it).
//  - Cavity: how much of the sky each point of the relief sees (worked out from the height map with it, Tools/Textures/
//    make_height_maps.py), darkening the bounce light down in the pile and the fissures (material ambient occlusion).
//  - Micro-shadowing (Chan, "Material Advances in Call of Duty: WWII", 2018): the relief also shades the direct light:
//    deep spots lose it as the light comes in lower, so a ceiling light raking across a wall brings out its texture.
//  - Fabric sheen (Charlie, Estevez & Kulla 2017, with Neubelt & Pettineo's visibility, as Filament and Unreal's cloth do):
//    fibres standing up from a carpet or upholstery catch the light at grazing angles, a soft bright rim toward the light.
// Per material, in properties URP's Lit input declares but this shader never uses (their keywords are never set), so the
// material layout (the SRP batcher's) is unchanged and there is no new variant:
//  _ParallaxMap         R the height (1 = top), G the cavity
//  _Parallax            below 0: on, its size the relief's depth in texture units (URP's own parallax needs a keyword)
//  _OcclusionStrength   how much the cavity darkens the bounce light and micro-shadows the direct light
//  _ClearCoatMask       the fabric sheen's strength (0: none), _ClearCoatSmoothness its smoothness (the fibres' spread)
#ifndef BACKROOMS_SURFACE_INCLUDED
#define BACKROOMS_SURFACE_INCLUDED

float4 _BRSurface;   // x: on (0/1), y: parallax out to (m), z: most parallax steps, w: set

static half g_BRCavity = 1;          // this pixel's cavity (micro-shadowing in the light functions)
static half3 g_BRSheen = 0;          // this pixel's sheen colour x strength
static half g_BRSheenRough = 0.5;

bool BR_SurfaceOn() { return _BRSurface.x > 0.5 && _Parallax < 0; }

// Where the view ray meets the relief: a linear search through the height in layers (more at grazing angles), then the
// crossing between the last two layers (secant). viewTS: toward the camera, in tangent space.
float2 BR_ParallaxOcclusion(float2 uv, half3 viewTS, float depth)
{
    float2 dx = ddx(uv), dy = ddy(uv);
    float z = max(viewTS.z, 0.12);
    float layers = lerp(_BRSurface.z, 6.0, saturate(z));
    float layerStep = 1.0 / layers;
    float2 shift = -viewTS.xy / z * depth * layerStep;
    float2 cur = uv;
    float layer = 0.0;
    float below = 1.0 - SAMPLE_TEXTURE2D_GRAD(_ParallaxMap, sampler_ParallaxMap, cur, dx, dy).r;   // depth below the top
    float2 prevUV = cur;
    float prevGap = below - layer;
    [loop] for (int i = 0; i < 32; i++)
    {
        if (layer >= below || i >= (int)layers) break;
        prevUV = cur; prevGap = below - layer;
        cur += shift;
        layer += layerStep;
        below = 1.0 - SAMPLE_TEXTURE2D_GRAD(_ParallaxMap, sampler_ParallaxMap, cur, dx, dy).r;
    }
    float gap = layer - below;                      // how far the ray went under the relief at the last layer
    float t = prevGap / max(prevGap + gap, 1e-4);
    return lerp(prevUV, cur, saturate(t));
}

// The relief's shadow on the direct light at this pixel (1 off the relief): Chan's micro-shadowing.
half BR_MicroShadow(half NdotL)
{
    if (g_BRCavity >= 0.999) return 1;
    half a = g_BRCavity;
    return saturate(abs(NdotL) + 2.0h * a * a - 1.0h);
}

// The fabric sheen's light toward the eye per unit of light arriving (times pi, URP's diffuse convention), without the
// cosine at the surface (the caller has it, or a rectangle's form factor in its place).
half3 BR_Sheen(half3 N, half3 L, half3 V)
{
    if (dot(g_BRSheen, g_BRSheen) < 1e-8) return 0;
    half3 H = normalize(L + V);
    float NoH = saturate(dot(N, H)), NoL = saturate(dot(N, L)), NoV = saturate(dot(N, V));
    float r = max(g_BRSheenRough, 0.08);
    float inv = 1.0 / r;
    float sin2h = max(1.0 - NoH * NoH, 0.0078125);
    float D = (2.0 + inv) * pow(sin2h, inv * 0.5) / (2.0 * PI);
    float vis = 1.0 / max(4.0 * (NoL + NoV - NoL * NoV), 1e-3);
    return g_BRSheen * (half)(PI * D * vis);
}

// Per pixel, before the light: the parallax-shifted uv (the caller samples the material there), then the cavity and sheen.
void BR_SurfaceSetup(float2 uv, inout SurfaceData s)
{
    g_BRCavity = 1; g_BRSheen = 0;
    if (!BR_SurfaceOn()) return;
    half2 hc = (half2)SAMPLE_TEXTURE2D(_ParallaxMap, sampler_ParallaxMap, uv).rg;
    g_BRCavity = lerp(1.0h, hc.g, (half)saturate(_OcclusionStrength));
    s.occlusion *= g_BRCavity;
    if (_ClearCoatMask > 0)
    {
        // Light catching the fibres keeps some of their colour (dyed fibres), and the deep pile catches less.
        g_BRSheen = lerp(half3(1, 1, 1), s.albedo / max(max(s.albedo.r, max(s.albedo.g, s.albedo.b)), 0.05h), 0.55h) * (half)_ClearCoatMask * g_BRCavity;
        g_BRSheenRough = (half)saturate(1.0 - _ClearCoatSmoothness);
    }
}

#endif
