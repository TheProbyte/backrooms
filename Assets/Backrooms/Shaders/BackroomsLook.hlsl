// Backrooms/Lit's look features (F5 > LIGHT). All are uniform branches on _BRLook, never shader keywords; zero (what
// LightGridGlobals binds when no GraphicsController runs) turns every one off.
//  - Ceiling panels lit as rectangles: exact diffuse from the panel's polygon (clipped to the surface's horizon) and a
//    representative-point specular, so a glossy floor shows a long rectangular reflection.
//  - Contact-hardening soft shadows for the real (shadow-mapped) fixtures: a blocker search in URP's own shadow atlas,
//    then a filter as wide as the penumbra a panel of that size casts. Sharp where things touch, soft farther away.
//  - Contact shadows: a short march toward the light through the camera depth, for the small things shadow maps miss
//    (items, a hand on a table, chair legs). Marched in ContactShadows.cs's own pass; looked up here.
// Included by BackroomsLitForwardPass.hlsl after URP's Lighting.hlsl and the light grid.
#ifndef BACKROOMS_LOOK_INCLUDED
#define BACKROOMS_LOOK_INCLUDED

float4 _BRLook;          // x: rectangular panels (0/1), y: shadow softness (0 = URP's filter), z: contact shadows (0..1), w: set (1)
float4 _BREmissionTint;  // rgb: the look's colour shift x panel glow, a: set (1)
// Real ceiling lights' shadow maps cover a narrower cone than their light (FixtureLightManager.ShadowConeAngle), so the map's
// texels are small. x: cosine of the half angle the real light draws (inside its map, with a margin), y: tangent of the
// map's half angle, z: on (0/1). Outside x the light grid draws the real light's share too (BackroomsDistantPBR).
float4 _BRShadowCone;

// Whether this real fixture's own URP light draws P (inside its shadow-mapped cone). Lamps (no direction) always.
bool BR_InShadowCone(LGridLight f, float3 P)
{
    if (_BRShadowCone.z < 0.5 || dot(f.dir, f.dir) < 0.5) return true;
    float3 v = P - f.pos;
    return dot(f.dir, v) >= _BRShadowCone.x * length(v);
}

#include "Assets/Backrooms/Shaders/BackroomsAreaLight.hlsl"

// ---------------- Contact-hardening soft shadows ----------------

float BR_Noise(float2 pixel) { return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)))); }

// URP's shadow for a real spot light, filtered by the penumbra the panel casts at this point. A uniform rectangle of light
// casts a penumbra that is the occluder's shadow averaged over the panel's own shape, scaled by (receiver - blocker) /
// blocker depth: so the filter is a rectangle of the panel's proportions (a tube: long along it, narrow across), not a
// disc. halfSize: the panel's half sizes along world x and z. Spot lights pointing down only (the ceiling fixtures); the
// light loop gives point-light shadows (lamps) to URP's own sampling.
half BackroomsSoftShadow(int lightIndex, half4 params, float3 positionWS, half3 normalWS, half3 lightDirection, float3 lightPosition, float2 halfSize, float2 pixel)
{
#if defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
    int slice = (int)params.w;
    if (slice < 0) return 1;
    #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
        float4x4 m = _AdditionalLightsWorldToShadow_SSBO[slice];
    #else
        float4x4 m = _AdditionalLightsWorldToShadow[slice];
    #endif
    float4 c = mul(m, float4(positionWS, 1));
    if (c.w <= 1e-5) return 1;
    float3 uvz = c.xyz / c.w;
    if (BEYOND_SHADOW_FAR(float4(uvz, 1))) return 1;
    // Along one texel's ray from the light, clip z is affine in the view depth w: z = A w + B, and w is zero at the light.
    float B = mul(m, float4(lightPosition, 1)).z;
    float A = (c.z - B) / c.w;
    // Shadow-map uv per metre along world x and along world z at this depth (the panel's own axes).
    float4 cx = mul(m, float4(positionWS + float3(0.1, 0, 0), 1));
    float4 cz = mul(m, float4(positionWS + float3(0, 0, 0.1), 1));
    float2 ux = (cx.xy / cx.w - uvz.xy) * 10.0, uz = (cz.xy / cz.w - uvz.xy) * 10.0;
    float uvPerMetre = 0.5 * (length(ux) + length(uz));
    float texel = _AdditionalShadowmapSize.x;
    // The map's tile in the atlas: taps stay inside it (its frustum is _BRShadowCone.y wide each way at depth 1, centred
    // straight below the light).
    float2 tileLo = -1e6, tileHi = 1e6;
    if (_BRShadowCone.z > 0.5)
    {
        float4 ca = mul(m, float4(lightPosition.x, positionWS.y, lightPosition.z, 1));
        float h = _BRShadowCone.y * uvPerMetre * c.w - 1.5 * texel;
        tileLo = ca.xy / ca.w - h; tileHi = ca.xy / ca.w + h;
    }
    // A jittered grid over the rectangle (one shift per pixel; the camcorder's raster averages it).
    float2 jitter = float2(BR_Noise(pixel), BR_Noise(pixel.yx + float2(23.17, 5.31)));
    // Samples away from this point see its own surface nearer or farther from the light, the more so the more the light
    // skims along it (a wall under a fixture): their depth reference is moved toward the light by that slope, or the
    // surface would shadow itself in streaks.
    float cosL = saturate(dot(float3(normalWS), float3(lightDirection)));
    float slope = min(sqrt(1 - cosL * cosL) / max(cosL, 0.1), 10.0) / uvPerMetre;   // metres of depth per uv of offset

    // Blocker search over the part of the map the panel could be hidden by (the panel's size at this point, capped):
    // average linear depth of what is in front. The fitting's own body, within 0.3 m of the light, is not a blocker.
    float2 search = min(halfSize, 0.6);
    float2 sx = ux * search.x, sz = uz * search.y;
    float blockerW = 0, blockers = 0;
    [loop] for (int i = 0; i < 9; i++)
    {
        float2 g = (float2(i % 3, i / 3) + jitter) * (2.0 / 3.0) - 1.0;
        float2 offset = g.x * sx + g.y * sz;
        float d = SAMPLE_TEXTURE2D_LOD(_AdditionalLightsShadowmapTexture, sampler_PointClamp, clamp(uvz.xy + offset, tileLo, tileHi), 0).r;
        float w = B / (d - A);
        float limit = c.w - 0.03 - length(offset) * slope;
        if (w > 0.3 && w < limit) { blockerW += w; blockers += 1; }
    }
    half attenuation = 1;
    if (blockers > 0)
    {
        // The penumbra: the panel scaled by (receiver - blocker) / blocker depth, on each of its axes (capped).
        float wb = blockerW / blockers;
        float2 extent = min(halfSize * min((c.w - wb) / wb, 2.5), 0.6);
        float2 fx = ux * extent.x, fz = uz * extent.y;
        fx *= max(1.0, texel / max(length(fx), 1e-6));                  // at least a texel each way: a smooth edge
        fz *= max(1.0, texel / max(length(fz), 1e-6));
        float sum = 0;
        [loop] for (int k = 0; k < 16; k++)
        {
            float2 g = (float2(k & 3, k >> 2) + jitter) * 0.5 - 1.0;
            float2 offset = g.x * fx + g.y * fz;
            float reference = A + B / max(c.w - length(offset) * slope, 0.05);   // this point's depth, moved by the slope
            sum += SAMPLE_TEXTURE2D_SHADOW(_AdditionalLightsShadowmapTexture, sampler_LinearClampCompare,
                                           float3(clamp(uvz.xy + offset, tileLo, tileHi), reference));
        }
        attenuation = (half)(sum / 16.0);
    }
    attenuation = LerpWhiteTo(attenuation, params.x);
    return lerp(attenuation, (half)1, GetAdditionalLightShadowFade(positionWS));
#else
    return 1;
#endif
}

// ---------------- Haze ----------------

float4 _LGridHaze;   // x: extinction per metre, y: in-scatter per metre, z: march distance, w: on

// Thin haze lit by every fixture: the light grid's _LGridB.z holds all light's luminance per 0.6 m texel (walls already
// stop it), marched in 8 jittered steps from the camera to the surface. The light colour is the fixtures' white.
half3 BackroomsHaze(float3 P, half3 color, float2 pixel)
{
    if (_LGridHaze.w < 0.5 || _LGridParams.x < 0.5) return color;
    float3 cam = _WorldSpaceCameraPos;
    float3 v = P - cam;
    float D = length(v);
    if (D < 1e-3) return color;
    float3 dir = v / D;
    float march = min(D, _LGridHaze.z);
    float dt = march / 8.0;
    float jitter = BR_Noise(pixel + 5.3);
    float scatter = 0;
    [loop] for (int i = 0; i < 8; i++)
    {
        float t = (i + jitter) * dt;
        float3 q = cam + dir * t;
        if (any(q.xz < _LGridWindow.xy + 0.6) || any(q.xz > _LGridWindow.zw - 0.6)) continue;
        float3 uvw = float3(q.x / (LG_TEXEL * _LGridSize.x), q.y / (LG_TEXEL * _LGridSize.y), q.z / (LG_TEXEL * _LGridSize.z));
        float light = SAMPLE_TEXTURE3D_LOD(_LGridB, sampler_LGridA, uvw, 0).z;
        scatter += light * exp(-_LGridHaze.x * t);
    }
    // Isotropic: radiance scattered toward the eye is sigma x (light from all around) / 4, in URP's units.
    half3 tint = _BREmissionTint.w > 0.5 ? (half3)(_BREmissionTint.rgb / max(dot(_BREmissionTint.rgb, float3(0.2126, 0.7152, 0.0722)), 1e-3)) : half3(1, 1, 1);
    half3 inScatter = (half3)(scatter * dt * _LGridHaze.y * 0.25 * _LGridParams.y) * tint * half3(1, 0.93, 0.75);
    return color * (half)exp(-_LGridHaze.x * D) + inScatter;
}

// ---------------- Contact shadows ----------------
// Worked out before the opaque pass by ContactShadows.cs, in a render pass of its own that reads the camera depth the way
// the render graph expects (the opaque pass does not declare that texture, so reading it here is not safe), for the two
// strongest fixtures at each pixel. The lit shader only looks them up: x, y their shadow, z, w their index in the light
// grid's list + 1 (0 = none).
TEXTURE2D(_BRContactMask);
float4 _BRContact;   // x: on for this camera, y, z: mask size in pixels

// ---------------- Ray-traced shadows (RayTracedLighting.cs, Resources/RayTracedLighting.compute) ----------------
// Worked out before the opaque pass on the player's camera with hardware ray queries: the share of the fixtures' light that
// reaches each pixel past everything that casts shadows (each pixel traces rays to fixtures picked by the light they give,
// to random points on their panels; blended over frames). The fixtures are lit here exactly as before and their sum is
// multiplied by it. Where it runs, the contact shadows and the furniture boxes' direct-light test are left out (they would
// count the same chair twice). Transparent surfaces are not in the depth it was traced from: they keep the old way.
TEXTURE2D(_BRRTShadow);
float4 _BRRT;   // x: ray-traced shadows on for this camera, y, z: size in pixels

bool BR_RTShadows()
{
#if defined(_SURFACE_TYPE_TRANSPARENT)
    return false;
#else
    return _BRRT.x > 0.5;
#endif
}

half BackroomsRTShadow(float2 screenUV)
{
    if (!BR_RTShadows()) return 1;
    int2 size = (int2)_BRRT.yz;
    return LOAD_TEXTURE2D(_BRRTShadow, clamp((int2)(screenUV * _BRRT.yz), 0, size - 1)).r;
}

half BackroomsContact(uint lightIndex, float2 screenUV)
{
    if (_BRContact.x < 0.5 || BR_RTShadows()) return 1;
    int2 size = (int2)_BRContact.yz;
    float4 m = LOAD_TEXTURE2D(_BRContactMask, clamp((int2)(screenUV * _BRContact.yz), 0, size - 1));
    float id = lightIndex + 1.0;
    if (abs(m.z - id) < 0.5) return (half)m.x;
    if (abs(m.w - id) < 0.5) return (half)m.y;
    return 1;
}

// ---------------- Screen-space light (ScreenLighting.cs, Resources/ScreenLighting.compute) ----------------
// Worked out before the opaque pass on the player's camera: the near bounce found on screen (what hides part of a
// surface's hemisphere, with the light it had last frame), and glossy reflections found on screen. The visibility itself
// arrives as URP's SSAO (the occlusion this function is given), so here it scales the probe bounce, and the bounce from
// what hides the rest is added unscaled: a corner between lit walls takes their light instead of going dark.
// Reflection occlusion (Lagarde & de Rousiers 2014) from the visibility keeps cached probe reflections out of corners
// and from under furniture; where a reflection was found on screen it replaces the probe's.
TEXTURE2D(_BRScreenGI);    // rgb: near bounce (radiance), a: visibility
TEXTURE2D(_BRScreenRefl);  // rgb: reflected light, a: how much of the reflection was found
float4 _BRScreen;          // x: on for this camera, y: bounce strength, z: reflection strength, w: reflection occlusion
float4 _BRScreenSize;      // xy: size in pixels

half3 BackroomsGlobalIllumination(BRDFData brdfData, BRDFData brdfDataClearCoat, float clearCoatMask,
    half3 bakedGI, half occlusion, float3 positionWS, half3 normalWS, half3 viewDirectionWS, float2 screenUV)
{
    if (_BRScreen.x < 0.5)
        return GlobalIllumination(brdfData, brdfDataClearCoat, clearCoatMask, bakedGI, occlusion, positionWS, normalWS, viewDirectionWS, screenUV);
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half NoV = saturate(dot(normalWS, viewDirectionWS));
    half fresnelTerm = Pow4(1.0 - NoV);
    int2 size = (int2)_BRScreenSize.xy;
    int2 px = clamp((int2)(screenUV * _BRScreenSize.xy), 0, size - 1);
    half4 gi = LOAD_TEXTURE2D(_BRScreenGI, px);
    half4 rf = LOAD_TEXTURE2D(_BRScreenRefl, px);
    half rough = brdfData.perceptualRoughness;
    half specOcc = saturate(pow(abs(NoV + occlusion), exp2(-16.0 * rough * rough - 1.0)) - 1.0 + occlusion);
    specOcc = lerp((half)1.0, specOcc, (half)_BRScreen.w);
    half3 indirectDiffuse = bakedGI * occlusion + max(gi.rgb, 0) * (half)_BRScreen.y;
    half3 probe = GlossyEnvironmentReflection(reflectVector, positionWS, rough, 1.0h, screenUV) * specOcc;
    half3 indirectSpecular = lerp(probe, max(rf.rgb, 0), saturate(rf.a * (half)_BRScreen.z));
    half3 color = EnvironmentBRDF(brdfData, indirectDiffuse, indirectSpecular, fresnelTerm);
#if defined(_CLEARCOAT) || defined(_CLEARCOATMAP)
    half3 coatSpecular = GlossyEnvironmentReflection(reflectVector, positionWS, brdfDataClearCoat.perceptualRoughness, 1.0h, screenUV) * specOcc;
    coatSpecular = lerp(coatSpecular, max(rf.rgb, 0), saturate(rf.a * (half)_BRScreen.z));
    half3 coatColor = EnvironmentBRDFClearCoat(brdfDataClearCoat, clearCoatMask, coatSpecular, fresnelTerm);
    half coatFresnel = kDielectricSpec.x + kDielectricSpec.a * fresnelTerm;
    return color * (1.0 - coatFresnel * clearCoatMask) + coatColor;
#else
    return color;
#endif
}

// Specular anti-aliasing (Tokuyoshi & Kaplanyan's geometric kind, as HDRP's and the Poolrooms' tiles have it): where the
// normal turns faster than a pixel shows (bevels, door handles, fittings far off), the highlight widens instead of
// sparkling and crawling. Graphics > Specular anti-aliasing (_PoolSpecAA).
float _PoolSpecAA;
half BackroomsSpecularAA(half smoothness, float3 normalWS)
{
    if (_PoolSpecAA < 0.5) return smoothness;
    float3 dnx = ddx(normalWS), dny = ddy(normalWS);
    float kernel = min(2.0 * 0.25 * (dot(dnx, dnx) + dot(dny, dny)), 0.18);
    float r = 1.0 - smoothness, a2 = r * r * r * r;
    return (half)(1.0 - sqrt(sqrt(min(a2 + kernel, 1.0))));
}

#endif
