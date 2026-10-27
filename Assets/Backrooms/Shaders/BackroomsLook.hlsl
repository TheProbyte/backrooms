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

#include "Assets/Backrooms/Shaders/BackroomsAreaLight.hlsl"

// ---------------- Contact-hardening soft shadows ----------------

#define BR_SOFT_TAPS 12
static const float2 BR_Poisson[BR_SOFT_TAPS] =
{
    float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457), float2(-0.203, 0.621),
    float2(0.962, -0.195), float2(0.473, -0.480), float2(0.519, 0.767), float2(0.185, -0.893),
    float2(0.507, 0.064), float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598),
};

float BR_Noise(float2 pixel) { return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)))); }

// URP's shadow for a real spot light, filtered by the penumbra a panel lightSize wide would cast at this point. Spot lights
// only: the light loop gives point-light shadows (lamps) to URP's own sampling.
half BackroomsSoftShadow(int lightIndex, half4 params, float3 positionWS, half3 normalWS, half3 lightDirection, float3 lightPosition, float lightSize, float2 pixel)
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
    // Shadow-map uv per metre sideways at this depth.
    float3 side = normalize(cross(float3(lightDirection), abs(lightDirection.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0)));
    float4 c2 = mul(m, float4(positionWS + side * 0.1, 1));
    float uvPerMetre = length(c2.xy / c2.w - uvz.xy) * 10.0;
    float texel = _AdditionalShadowmapSize.x;
    float2 rot; sincos(BR_Noise(pixel) * 6.2831853, rot.y, rot.x);
    float2x2 spin = float2x2(rot.x, -rot.y, rot.y, rot.x);
    // Samples away from this point see its own surface nearer or farther from the light, the more so the more the light
    // skims along it (a wall under a fixture): their depth reference is moved toward the light by that slope, or the
    // surface would shadow itself in streaks.
    float cosL = saturate(dot(float3(normalWS), float3(lightDirection)));
    float slope = min(sqrt(1 - cosL * cosL) / max(cosL, 0.1), 10.0) / uvPerMetre;   // metres of depth per uv of offset

    // Blocker search over the area the panel could be hidden from (capped): average linear depth of what is in front.
    // The fitting's own body, within 0.3 m of the light, is not a blocker.
    float searchUV = clamp(0.5 * lightSize * uvPerMetre, 2.0 * texel, 14.0 * texel);
    float blockerW = 0, blockers = 0;
    [loop] for (int i = 0; i < 8; i++)
    {
        float2 offset = mul(spin, BR_Poisson[i]) * searchUV;
        float d = SAMPLE_TEXTURE2D_LOD(_AdditionalLightsShadowmapTexture, sampler_PointClamp, uvz.xy + offset, 0).r;
        float w = B / (d - A);
        float limit = c.w - 0.03 - length(offset) * slope;
        if (w > 0.3 && w < limit) { blockerW += w; blockers += 1; }
    }
    half attenuation = 1;
    if (blockers > 0)
    {
        float wb = blockerW / blockers;
        float penumbraMetres = lightSize * saturate((c.w - wb) / wb);
        float filterUV = clamp(0.5 * penumbraMetres * uvPerMetre, 1.0 * texel, 9.0 * texel);
        float sum = 0;
        [loop] for (int k = 0; k < BR_SOFT_TAPS; k++)
        {
            float2 offset = mul(spin, BR_Poisson[k]) * filterUV;
            float reference = A + B / max(c.w - length(offset) * slope, 0.05);   // this point's depth, moved by the slope
            sum += SAMPLE_TEXTURE2D_SHADOW(_AdditionalLightsShadowmapTexture, sampler_LinearClampCompare, float3(uvz.xy + offset, reference));
        }
        attenuation = (half)(sum / BR_SOFT_TAPS);
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

half BackroomsContact(uint lightIndex, float2 screenUV)
{
    if (_BRContact.x < 0.5) return 1;
    int2 size = (int2)_BRContact.yz;
    float4 m = LOAD_TEXTURE2D(_BRContactMask, clamp((int2)(screenUV * _BRContact.yz), 0, size - 1));
    float id = lightIndex + 1.0;
    if (abs(m.z - id) < 0.5) return (half)m.x;
    if (abs(m.w - id) < 0.5) return (half)m.y;
    return 1;
}

#endif
