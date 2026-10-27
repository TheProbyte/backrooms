// URP 17.4 LitForwardPass plus runtime indirect light and consistent finite-fixture PBR lighting.
#ifndef UNIVERSAL_FORWARD_LIT_PASS_INCLUDED
#define UNIVERSAL_FORWARD_LIT_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Assets/Backrooms/Shaders/LightGridSample.hlsl"   // Backrooms: light grid
float4 _PortalClipPlane; // zero outside an explicit portal-camera render
#include "Assets/Backrooms/Shaders/BackroomsLook.hlsl"     // Backrooms: rectangular panels, soft and contact shadows, haze

#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

#if defined(_PARALLAXMAP)
#define REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR
#endif

#if (defined(_NORMALMAP) || (defined(_PARALLAXMAP) && !defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR))) || defined(_DETAIL)
#define REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR
#endif

// keep this file in sync with LitGBufferPass.hlsl

struct Attributes
{
    float4 positionOS   : POSITION;
    float3 normalOS     : NORMAL;
    float4 tangentOS    : TANGENT;
    float2 texcoord     : TEXCOORD0;
    float2 staticLightmapUV   : TEXCOORD1;
    float2 dynamicLightmapUV  : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv                       : TEXCOORD0;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    float3 positionWS               : TEXCOORD1;
#endif

    float3 normalWS                 : TEXCOORD2;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    half4 tangentWS                : TEXCOORD3;    // xyz: tangent, w: sign
#endif

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    half4 fogFactorAndVertexLight   : TEXCOORD5; // x: fogFactor, yzw: vertex light
#else
    half  fogFactor                 : TEXCOORD5;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord              : TEXCOORD6;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS                : TEXCOORD7;
#endif

    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
#ifdef DYNAMICLIGHTMAP_ON
    float2  dynamicLightmapUV : TEXCOORD9; // Dynamic lightmap UVs
#endif

#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD10;
#endif

    float4 positionCS               : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    inputData.positionWS = input.positionWS;
#endif

#if defined(DEBUG_DISPLAY)
    inputData.positionCS = input.positionCS;
#endif

    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#if defined(_NORMALMAP) || defined(_DETAIL)
    float sgn = input.tangentWS.w;      // should be either +1 or -1
    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz);

    #if defined(_NORMALMAP)
    inputData.tangentToWorld = tangentToWorld;
    #endif
    inputData.normalWS = TransformTangentToWorld(normalTS, tangentToWorld);
#else
    inputData.normalWS = input.normalWS;
#endif

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    inputData.viewDirectionWS = viewDirWS;

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    inputData.shadowCoord = input.shadowCoord;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
#else
    inputData.shadowCoord = float4(0, 0, 0, 0);
#endif
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactorAndVertexLight.x);
    inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
#else
    inputData.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
#endif

#if defined(UNITY_PRETRANSFORM_TO_DISPLAY_ORIENTATION)
    float2 preRotatedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    switch (UNITY_DISPLAY_ORIENTATION_PRETRANSFORM)
    {
    default:
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_0: inputData.normalizedScreenSpaceUV = preRotatedScreenSpaceUV; break;
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_90: inputData.normalizedScreenSpaceUV = float2(1 - preRotatedScreenSpaceUV.y, preRotatedScreenSpaceUV.x); break;
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_180: inputData.normalizedScreenSpaceUV = float2(1 - preRotatedScreenSpaceUV.x, 1 - preRotatedScreenSpaceUV.y); break;
        case UNITY_DISPLAY_ORIENTATION_PRETRANSFORM_270: inputData.normalizedScreenSpaceUV = float2(preRotatedScreenSpaceUV.y, 1 - preRotatedScreenSpaceUV.x); break;
    }
#else
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
#endif

    #if defined(DEBUG_DISPLAY)
    #if defined(DYNAMICLIGHTMAP_ON)
    inputData.dynamicLightmapUV = input.dynamicLightmapUV;
    #endif
    #if defined(LIGHTMAP_ON)
    inputData.staticLightmapUV = input.staticLightmapUV;
    #else
    inputData.vertexSH = input.vertexSH;
    #endif
    #if defined(USE_APV_PROBE_OCCLUSION)
    inputData.probeOcclusion = input.probeOcclusion;
    #endif
    #endif
}

void InitializeBakedGIData(Varyings input, inout InputData inputData)
{
    #if defined(_SCREEN_SPACE_IRRADIANCE)
    inputData.bakedGI = SAMPLE_GI(_ScreenSpaceIrradiance, input.positionCS.xy);
    #elif defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    inputData.bakedGI = SAMPLE_GI(input.vertexSH,
        GetAbsolutePositionWS(inputData.positionWS),
        inputData.normalWS,
        inputData.viewDirectionWS,
        input.positionCS.xy,
        input.probeOcclusion,
        inputData.shadowMask);
    #else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
    #endif
}

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //

// Backrooms: URP's UniversalFragmentPBR (17.4, Lighting.hlsl) with one change: in light grid mode each
// additional light is also blocked by walls (LGrid_LightVisible), so real lights cannot leak through them.
// Re-copy with Lit when URP is upgraded and keep the two marked lines.
float4 BackroomsRealLightPosition(uint lightIndex)
{
#if USE_CLUSTER_LIGHT_LOOP
    int idx = lightIndex;
#else
    int idx = GetPerObjectLightIndex(lightIndex);
#endif
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float4 lp = _AdditionalLightsBuffer[idx].position;
#else
    float4 lp = _AdditionalLightsPosition[idx];
#endif
    return lp;
}

// A ceiling panel as the rectangle it is (F5 > LIGHT > Rectangular panels): diffuse from the exact form factor of the
// panel (clipped to the surface's horizon), specular toward the point of the panel a mirror reflection comes nearest to.
// Same light as the point fixture far away: the panel's radiance is the fixture's intensity x pi / area, and its spread
// (the spot cone) and range window are taken at the panel's centre.
half3 BackroomsRectFixture(LGridLight f, half3 energy, half shadow, float sizeWeight, float distanceSquared, InputData data,
                           BRDFData brdf, bool noSpecular)
{
    float2 h = f.halfSize * sizeWeight;
    float area = max(4.0 * h.x * h.y, 1e-4);
    float3 P = data.positionWS;
    half3 N = data.normalWS;
    float dC = sqrt(max(distanceSquared, 1e-6));
    float3 dirC = (f.pos - P) / dC;
    float cosE = dot(f.dir, -dirC);
    float cone = LGrid_AngleAtten(cosE, f.cosOuter, f.cosInner);
    float window = distanceSquared / (f.range * f.range);
    window = saturate(1.0 - window * window); window *= window;
    float formFactor = BR_RectFormFactor(P, N, f.pos, h);
    float attenuation = formFactor * PI / area * cone / max(cosE, 0.02) * window;
    if (attenuation < 1e-5) return 0;
    // (How much of the panel walls hide is in shadow: LGrid_EmitterVisible.)
    // Contrast is based on full output, independent of the handover share (as for the point fixture).
    float lum = max(dot(f.colorAll, float3(0.2126, 0.7152, 0.0722)) * attenuation, 1e-5);
    float contrast = pow(saturate(lum), _LGridShape.w * 1.2);
    half3 color = energy * _LGridParams.y * (half)(shadow * contrast);
    half3 lit = brdf.diffuse * color * (half)attenuation;
    // Optional artistic diffuse wrap, shared with the point fixture (taken toward the centre).
    float noL = saturate(dot(N, dirC));
    float wrapped = saturate((dot(N, dirC) + _LGridShape.x) / (1 + _LGridShape.x));
    float pointAttenuation = LGrid_DistAtten(max(distanceSquared, 0.01), f.range) * cone;
    lit += brdf.diffuse * color * (half)(pointAttenuation * max(0, wrapped - noL));
    if (!noSpecular)
    {
        float3 R = reflect(-data.viewDirectionWS, N);
        float3 toS = BR_RectSpecularPoint(P, R, f.pos, h) - P;
        float dS = max(length(toS), 1e-3);
        half3 Ls = (half3)(toS / dS);
        if (dot(N, Ls) > 0)
        {
            // The lobe widened by the panel's size, energy kept (Karis 2013, with the panel as a disc of the same area).
            float a = brdf.roughness;
            float wide = saturate(a + 0.5 * sqrt(area / PI) / dS);
            float keep = (a / wide) * (a / wide);
            lit += brdf.specular * DirectBRDFSpecular(brdf, N, Ls, data.viewDirectionWS) * color * (half)(attenuation * keep);
        }
    }
    return lit;
}

// Identical light model (rectangle or point), attenuation, diffuse/specular BRDF and visibility in both tiers.
// The shadow map is the real light's own (contact-hardening when F5 > LIGHT > Shadow softness is on).
half3 BackroomsFixturePBR(LGridLight f, uint lightId, half3 energy, half shadow, bool localContact, bool knownVisible, InputData data,
                         BRDFData brdf, BRDFData coat, SurfaceData surface, bool noSpecular)
{
    float3 delta = f.pos - data.positionWS;
    float distanceSquared = dot(delta, delta);
    if (dot(energy, energy) < 1e-10 || shadow <= 0) return 0;
    shadow *= BackroomsContact(lightId, data.normalizedScreenSpaceUV);
    if (!knownVisible && !LGrid_FixtureMayReach(f, data.positionWS)) return 0;
    float sizeWeight = 1.0 - smoothstep(36.0, 64.0, distanceSquared);
    if (!knownVisible)
    {
        // The share of the panel seen past the walls (a penumbra past wall ends and under headers, not a cut).
        half seen = LGrid_EmitterVisible(data.positionWS, data.normalWS, f.pos, f.halfSize);
        if (seen <= 0) return 0;
        shadow *= seen;
    }
    if (localContact) shadow *= LocalLightVisibility(data.positionWS, data.normalWS, f.pos);
    // Ceiling panels as rectangles (F5 > LIGHT > Rectangular panels); otherwise, and for lamps and far fixtures, a point.
    [branch] if (_BRLook.x > 0.5 && dot(f.halfSize, f.halfSize) > 0 && sizeWeight > 0 && f.dir.y < -0.99)
        return BackroomsRectFixture(f, energy, shadow, sizeWeight, distanceSquared, data, brdf, noSpecular);
    float3 dir = delta * rsqrt(max(distanceSquared, 1e-6));
    float attenuation = LGrid_DistAtten(max(distanceSquared, 0.01), f.range);
    if (dot(f.dir, f.dir) > 0.5) attenuation *= LGrid_AngleAtten(dot(f.dir, -dir), f.cosOuter, f.cosInner);
    if (attenuation < 1e-5) return 0;
    // Contrast is based on full output, independent of the handover share.
    float lum = max(dot(f.colorAll, float3(0.2126, 0.7152, 0.0722)) * attenuation, 1e-5);
    attenuation *= pow(saturate(lum), _LGridShape.w * 1.2);
    Light light = (Light)0;
    light.color = energy * _LGridParams.y;
    light.direction = dir;
    light.distanceAttenuation = attenuation;
    light.shadowAttenuation = shadow;
    half3 lit = LightingPhysicallyBased(brdf, coat, light, data.normalWS, data.viewDirectionWS, surface.clearCoatMask, noSpecular);
    // Optional artistic diffuse wrap is shared by both tiers; specular keeps the physical normal.
    float noL = saturate(dot(data.normalWS, dir));
    float wrapped = saturate((dot(data.normalWS, dir) + _LGridShape.x) / (1 + _LGridShape.x));
    lit += brdf.diffuse * light.color * (half)(attenuation * shadow * max(0, wrapped - noL));
    return lit;
}

half3 BackroomsDistantPBR(InputData data, BRDFData brdf, BRDFData coat, SurfaceData surface, bool noSpecular, half ao)
{
    if (_LGridParams.x < 0.5 || _LGridFar.x < 0.5) return 0;
    float2 inside = min(data.positionWS.xz - _LGridWindow.xy, _LGridWindow.zw - data.positionWS.xz);
    half edge = saturate(min(inside.x, inside.y) / 4.0);
    if (edge <= 0) return 0;
    uint2 tile = LGrid_TileRange(data.positionWS);
    half3 sum = 0;
    [loop] for (uint i = 0; i < tile.y; i++)
    {
        uint id = _LGridTileLights[tile.x + i];
        LGridLight f = _LGridLights[id];
        float distanceToLight = distance(f.pos, data.positionWS);
        // Farther fixtures come from the grid texture (see LGrid_NearWeight): skip them before any visibility work.
        half3 energy = f.color * LGrid_NearWeight(distanceToLight);
        // A real light's own share outside its shadow-mapped cone is drawn here (the real light skips it: BR_InShadowCone).
        // Far out in that ring (the floor several metres away) it adds under about 0.5 % of the light right under the
        // fixture: faded out there, not worked out.
        if (_BRShadowCone.z > 0.5 && distanceToLight < f.range && !BR_InShadowCone(f, data.positionWS))
        {
            float3 v = data.positionWS - f.pos;
            float d2 = max(dot(v, v), 0.01);
            float reach = LGrid_DistAtten(d2, f.range) * LGrid_AngleAtten(dot(f.dir, v) * rsqrt(d2), f.cosOuter, f.cosInner);
            energy += max(f.colorAll - f.color, 0) * (half)smoothstep(0.0002, 0.0008, reach);
        }
        if (dot(energy, energy) < 1e-10) continue;
        sum += BackroomsFixturePBR(f, id, energy, ao, true, false, data, brdf, coat, surface, noSpecular);
    }
    return sum * edge;
}

half4 BackroomsFragmentPBR(InputData inputData, SurfaceData surfaceData)
{
    #if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularHighlightsOff = true;
    #else
    bool specularHighlightsOff = false;
    #endif
    BRDFData brdfData;
    InitializeBRDFData(surfaceData, brdfData);

    #if defined(DEBUG_DISPLAY)
    half4 debugColor;
    if (CanDebugOverrideOutputColor(inputData, surfaceData, brdfData, debugColor))
    {
        return debugColor;
    }
    #endif

    BRDFData brdfDataClearCoat = CreateClearCoatBRDFData(surfaceData, brdfData);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
#if defined(BACKROOMS_AO_HOOK)
    BACKROOMS_AO_HOOK(aoFactor);   // Backrooms: CrawlDuct decides where screen-space AO applies (not defined for Backrooms/Lit)
#endif
    uint meshRenderingLayers = GetMeshRenderingLayer();
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    half portalLocalWeight;
    half3 portalLight = BackroomsPortalSpill(inputData.positionWS, inputData.normalWS, portalLocalWeight);
    inputData.bakedGI = inputData.bakedGI * portalLocalWeight + portalLight;
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);
    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    lightingData.additionalLightsColor += BackroomsDistantPBR(inputData, brdfData, brdfDataClearCoat, surfaceData, specularHighlightsOff, aoFactor.directAmbientOcclusion);
    aoFactor.indirectAmbientOcclusion = min(aoFactor.indirectAmbientOcclusion, (half)LocalIndirectVisibility(inputData.positionWS, inputData.normalWS));
    lightingData.giColor = GlobalIllumination(brdfData, brdfDataClearCoat, surfaceData.clearCoatMask,
                                              inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,
                                              inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
#endif
    {
        lightingData.mainLightColor = LightingPhysicallyBased(brdfData, brdfDataClearCoat, mainLight,
                                                              inputData.normalWS, inputData.viewDirectionWS,
                                                              surfaceData.clearCoatMask, specularHighlightsOff);
    }

    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfDataClearCoat, light,
                                                                          inputData.normalWS, inputData.viewDirectionWS,
                                                                          surfaceData.clearCoatMask, specularHighlightsOff);
        }
    }
    #endif

    float2 pixel = inputData.normalizedScreenSpaceUV * _ScreenParams.xy;
    LIGHT_LOOP_BEGIN(pixelLightCount)
        float4 source = BackroomsRealLightPosition(lightIndex);
        LGridLight fixture = (LGridLight)0;
        uint fixtureId = 0;
        bool isFixture = source.w > 0.5 && LGrid_FindFixtureIndex(source.xyz, inputData.positionWS, fixture, fixtureId);
        half3 energy = isFixture ? max(fixture.colorAll - fixture.color, 0) : 0;
        half seen = 1;
#if USE_CLUSTER_LIGHT_LOOP
        int realIndex = lightIndex;
#else
        int realIndex = GetPerObjectLightIndex(lightIndex);
#endif
        half4 shadowParams = GetAdditionalLightShadowParams(realIndex);
        // A light with a shadow map leaves broken walls to its shadows (the broken pieces cast them); others use the holes.
        bool shadowMapped = shadowParams.x > 0 && shadowParams.w >= 0;    // strength, and a slice in the shadow atlas
        // Layout-blocked fixtures cannot contribute. Reject before URP samples their soft shadow maps.
        if (isFixture)
        {
            if (dot(energy, energy) < 1e-10 || !LGrid_FixtureMayReach(fixture, inputData.positionWS)) continue;
            if (!BR_InShadowCone(fixture, inputData.positionWS)) continue;   // outside its shadow map: BackroomsDistantPBR
            seen = LGrid_EmitterVisibleH(inputData.positionWS, inputData.normalWS, fixture.pos, fixture.halfSize, shadowMapped);
            if (seen <= 0) continue;
        }
        Light light;
        [branch] if (isFixture && _BRLook.y > 0 && shadowParams.z < 0.5)
        {
            // Contact-hardening soft shadow instead of URP's fixed filter (the fixture's colour comes from the grid light).
            light = GetAdditionalPerObjectLight(realIndex, inputData.positionWS);
            float2 size = max(fixture.halfSize, 0.02) * _BRLook.y;   // the panel's own half sizes (a tube: long and narrow)
            light.shadowAttenuation = BackroomsSoftShadow(realIndex, shadowParams, inputData.positionWS, inputData.normalWS, light.direction, source.xyz, size, pixel);
        }
        else light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            if (isFixture)
            {
                lightingData.additionalLightsColor += BackroomsFixturePBR(fixture, fixtureId, energy, light.shadowAttenuation * seen * aoFactor.directAmbientOcclusion, false, true,
                    inputData, brdfData, brdfDataClearCoat, surfaceData, specularHighlightsOff);
            }
            else
            {
                if (source.w > 0.5) light.shadowAttenuation *= LGrid_LightVisibleH(inputData.positionWS, inputData.normalWS, source.xyz, shadowMapped);
                if (light.shadowAttenuation > 0)
                    lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, brdfDataClearCoat, light,
                        inputData.normalWS, inputData.viewDirectionWS, surfaceData.clearCoatMask, specularHighlightsOff);
            }
        }
    LIGHT_LOOP_END
    #endif

    #if defined(_ADDITIONAL_LIGHTS_VERTEX)
    lightingData.vertexLightingColor += inputData.vertexLighting * brdfData.diffuse;
    #endif

    lightingData.mainLightColor *= portalLocalWeight;
    lightingData.additionalLightsColor *= portalLocalWeight;
    lightingData.vertexLightingColor *= portalLocalWeight;
#if REAL_IS_HALF
    return min(CalculateFinalColor(lightingData, surfaceData.alpha), HALF_MAX);
#else
    return CalculateFinalColor(lightingData, surfaceData.alpha);
#endif
}
///////////////////////////////////////////////////////////////////////////////

// Used in Standard (Physically Based) shader
Varyings LitPassVertex(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);

    // normalWS and tangentWS already normalize.
    // this is required to avoid skewing the direction during interpolation
    // also required for per-vertex lighting and SH evaluation
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);

    half fogFactor = 0;
    #if !defined(_FOG_FRAGMENT)
        fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
    #endif

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);

    // already normalized from normal transform to WS.
    output.normalWS = normalInput.normalWS;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR) || defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    real sign = input.tangentOS.w * GetOddNegativeScale();
    half4 tangentWS = half4(normalInput.tangentWS.xyz, sign);
#endif
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    output.tangentWS = tangentWS;
#endif

#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
    half3 viewDirTS = GetViewDirectionTangentSpace(tangentWS, output.normalWS, viewDirWS);
    output.viewDirTS = viewDirTS;
#endif

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif
    OUTPUT_SH4(vertexInput.positionWS, output.normalWS.xyz, GetWorldSpaceNormalizeViewDir(vertexInput.positionWS), output.vertexSH, output.probeOcclusion);
#ifdef _ADDITIONAL_LIGHTS_VERTEX
    output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
#else
    output.fogFactor = fogFactor;
#endif

#if defined(REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    output.positionWS = vertexInput.positionWS;
#endif

#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    output.shadowCoord = GetShadowCoord(vertexInput);
#endif

    output.positionCS = vertexInput.positionCS;

    return output;
}

// Used in Standard (Physically Based) shader
void LitPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    #if defined(BACKROOMS_PORTAL_CLIP)
        clip(dot(float4(input.positionWS, 1), _PortalClipPlane));
    #endif

#if defined(_PARALLAXMAP)
#if defined(REQUIRES_TANGENT_SPACE_VIEW_DIR_INTERPOLATOR)
    half3 viewDirTS = input.viewDirTS;
#else
    half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half3 viewDirTS = GetViewDirectionTangentSpace(input.tangentWS, input.normalWS, viewDirWS);
#endif
    ApplyPerPixelDisplacement(viewDirTS, input.uv);
#endif

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);
    if (_BREmissionTint.w > 0.5) surfaceData.emission *= _BREmissionTint.rgb;              // Backrooms: the look's light colour and panel glow

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    InitializeBakedGIData(input, inputData);
    inputData.bakedGI += BackroomsLightGrid(inputData.positionWS, inputData.normalWS);   // Backrooms: light grid

    half4 color = BackroomsFragmentPBR(inputData, surfaceData);                          // Backrooms: walls block real lights
    color.rgb = BackroomsHaze(inputData.positionWS, color.rgb, inputData.normalizedScreenSpaceUV * _ScreenParams.xy);   // Backrooms: lit haze
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
