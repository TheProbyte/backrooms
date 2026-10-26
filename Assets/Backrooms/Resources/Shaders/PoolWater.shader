// Still water over the basins. Drawn after the opaque pass so it can see what is under it (refraction, depth for the
// colour of the water) and what it mirrors (screen-space reflection), falling back to the ceiling or the sky straight
// above where the screen holds nothing. Ripples come from the simulation around the player (PoolRipples).
Shader "Backrooms/PoolWater"
{
    Properties
    {
        _Absorb ("Absorption per metre", Vector) = (1.7, 0.36, 0.42, 0)
        _Scatter ("In-scattered colour", Color) = (0.10, 0.42, 0.38, 1)
        _Refraction ("Refraction", Range(0, 0.3)) = 0.15
        _Roughness ("Surface roughness", Range(0.01, 0.3)) = 0.05
        _Micro ("Stillness (micro waves)", Range(0, 0.05)) = 0.004
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back ZWrite On ZTest LEqual Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Absorb;
                half4 _Scatter;
                half _Refraction, _Roughness, _Micro;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fogFactor : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // Explicit LOD: these are read inside loops.
            float DepthAt(float2 uv) { return LinearEyeDepth(SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, UnityStereoTransformScreenSpaceTex(uv), 0).r, _ZBufferParams); }
            float3 ColorAt(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(uv), 0).rgb; }

            float2 ScreenUV(float3 pw)
            {
                float4 cs = TransformWorldToHClip(pw);
                float4 sp = ComputeScreenPos(cs);
                return sp.xy / max(sp.w, 1e-5);
            }

            // March the mirrored ray across the depth buffer; returns the colour it lands on and how much to trust it.
            float3 ScreenReflection(float3 P, float3 R, out float hit)
            {
                hit = 0;
                float step = 0.12;
                float3 pos = P;
                float3 prev = P;
                [loop] for (int k = 0; k < 28; k++)
                {
                    prev = pos;
                    pos += R * step; step *= 1.13;
                    float4 cs = TransformWorldToHClip(pos);
                    if (cs.w <= 0.05) return 0;
                    float2 uv = ScreenUV(pos);
                    if (any(uv < 0) || any(uv > 1)) return 0;
                    float scene = DepthAt(uv);
                    float ray = cs.w;
                    if (ray > scene + 0.01)
                    {
                        if (ray - scene > step * 1.5 + 0.25) return 0;     // went behind something: no hit
                        // Refine between the last two points.
                        float3 a = prev, b = pos;
                        [unroll] for (int r = 0; r < 5; r++)
                        {
                            float3 m = (a + b) * 0.5;
                            float2 muv = ScreenUV(m);
                            float ms = DepthAt(muv);
                            if (TransformWorldToHClip(m).w > ms) b = m; else a = m;
                        }
                        uv = ScreenUV(b);
                        float2 edge = min(uv, 1 - uv);
                        hit = saturate(min(edge.x, edge.y) * 10.0) * (1.0 - k / 28.0);
                        return ColorAt(uv);
                    }
                }
                return 0;
            }

            // What the water mirrors where the screen has nothing: the ceiling above, or the sky through an opening.
            float3 Mirror(float3 P, float3 R)
            {
                int storey = PoolStorey(P.y);
                PoolCell here = PoolLoad(PoolCellOf(P), storey);
                float h = here.ceiling + PoolBase(storey);
                float3 Q = P + R * ((h - P.y) / max(R.y, 0.02));
                int2 qc = PoolCellOf(Q);
                PoolCell q = PoolLoad(qc, storey);
                float open = (q.valid && abs(q.ceiling - here.ceiling) < 0.05) ? saturate(PoolHoleSD(q.sky, q.joined, Q.xz - qc * POOL_C) / 0.02 + 0.5) : 0;
                float3 ceiling = float3(0.8, 0.82, 0.78) * PoolAmbientAt(Q - float3(0, 0.05, 0), float3(0, -1, 0));
                return lerp(ceiling, PoolSkyColor(R), open);
            }

            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                float3 P = i.positionWS;
                float3 V = GetWorldSpaceNormalizeViewDir(P);
                float t = _Time.y;

                // Surface: the player's ripples, and the faintest slow movement so it never looks frozen.
                float2 slope = PoolRippleSlope(P.xz);
                slope += _Micro * float2(sin(P.x * 5.1 + t * 0.9) + sin(P.z * 6.9 - t * 0.7 + P.x * 2.1),
                                         sin(P.z * 5.7 + t * 0.8) + sin(P.x * 6.3 + t * 0.6 - P.z * 1.8)) * 0.5;
                float3 N = normalize(float3(-slope.x, 1.0, -slope.y));
                float NdotV = saturate(dot(N, V));
                float F = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);

                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float eye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float3 below, refl;
                float thick;
                PoolCell here = PoolLoad(PoolCellOf(P), PoolStorey(P.y));
                float3 ambient = PoolAmbientAt(P, float3(0, 1, 0));
                float3 R = reflect(-V, N);
                if (R.y < 0.02) R = normalize(float3(R.x, 0.02, R.z));
                refl = Mirror(P, R);

                [branch] if (_PoolScreenTextures > 0.5)
                {
                    float2 ruv = suv + slope * _Refraction;
                    float scene = DepthAt(ruv);
                    if (scene < eye) { ruv = suv; scene = DepthAt(ruv); }
                    below = ColorAt(ruv);
                    thick = max(scene - eye, 0) * length(P - GetCameraPositionWS()) / max(eye, 1e-3);
                    float hit;
                    float3 ssr = ScreenReflection(P, R, hit);
                    refl = lerp(refl, ssr, hit);
                }
                else
                {
                    // No screen copies in this view (a portal window): the tiled floor below, roughly.
                    thick = (POOL_WATER + POOL_BASIN) / max(V.y, 0.08);
                    below = float3(0.78, 0.8, 0.75) * ambient;
                }
                float3 absorb = exp(-thick * _Absorb.rgb);
                float3 water = below * absorb + _Scatter.rgb * ambient * (1.0 - absorb.g);

                // The sun's glint off the surface.
                float sun = PoolSunVisibility(P + float3(0, 0.01, 0));
                float3 H = normalize(V + POOL_SUN);
                float a2 = pow(_Roughness, 4.0);
                float NdotH = saturate(dot(N, H));
                float dd = NdotH * NdotH * (a2 - 1.0) + 1.0;
                float3 glint = _PoolSunColor.rgb * sun * saturate(dot(N, POOL_SUN)) * (a2 / (PI * dd * dd)) * F * 0.25;

                float3 color = lerp(water, refl, F) + glint;
                return half4(MixFog(color, i.fogFactor), 1);
            }
            ENDHLSL
        }
    }
}
