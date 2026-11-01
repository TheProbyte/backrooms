// Ordinary lit things (the arm and body, items, furniture) while they are in the Poolrooms: the same light as the tiles
// (traced sun, sky and bounce, see PoolCommon.hlsl) on a simple albedo / smoothness / metallic surface. Their usual
// Backrooms/Lit materials are swapped for copies with this shader by PoolLitSwitch.
Shader "Backrooms/PoolLit"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.4
        _Metallic ("Metallic", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Smoothness, _Metallic;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; half fogFactor : TEXCOORD3; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                float3 N = normalize(i.normalWS), V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float ao = 1.0, aoDirect = 1.0;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor aof = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    // The Poolrooms are lit mostly by soft bounce, which made full-strength SSAO draw hard dark bands at
                    // every floor-wall join and in corners: a gentle share of it on the bounce, none on the sun.
                    ao = lerp(1.0, aof.indirectAmbientOcclusion, 0.38); aoDirect = 1.0;
                #endif
                float sun = PoolSunVisibility(i.positionWS + N * 0.02);
                float NdotL = saturate(dot(N, POOL_SUN));
                float3 ambient = PoolAmbientAt(i.positionWS, N) * ao;
                half3 diffuse = albedo * (1.0 - _Metallic);
                half3 f0 = lerp(half3(0.04, 0.04, 0.04), albedo, _Metallic);
                float rough = max(1.0 - _Smoothness, 0.05), a2 = rough * rough * rough * rough;
                float3 H = normalize(V + POOL_SUN);
                float NdotH = saturate(dot(N, H)), dd = NdotH * NdotH * (a2 - 1.0) + 1.0;
                float3 F = f0 + (1.0 - f0) * pow(1.0 - saturate(dot(V, H)), 5.0);
                float3 spec = _PoolSunColor.rgb * sun * NdotL * (a2 / (PI * dd * dd)) * F * 0.25;
                float3 Fv = f0 + (1.0 - f0) * pow(1.0 - saturate(dot(N, V)), 5.0);
                float3 env = ambient;
                // Metal (the Poolrooms' chrome ladders and rails): what it mirrors, traced through the cell map.
                [branch] if (_Metallic > 0.5)
                {
                    float3 R = reflect(-V, N);
                    PoolHit hit = PoolTrace(i.positionWS + N * 0.02, R, 25.0);
                    env = PoolShadeHit(hit, R, 0.03 * (1.1 - _Smoothness));
                }
                float3 color = diffuse * (_PoolSunColor.rgb * sun * NdotL * aoDirect + ambient) + spec + env * Fv * (_Metallic > 0.5 ? 1.0 : _Smoothness);
                return half4(PoolFarFade(MixFog(color, i.fogFactor), i.positionWS), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _PortalClipPlane;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); return o; }
            half frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _PortalClipPlane;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings vert(Attributes i) { Varyings o; o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                return half4(normalize(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
