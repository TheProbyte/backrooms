// Spray can mist particles (Interaction/Items/SprayCan.cs, 2026-11-04): the jet's streaks and the overspray's haze, soft
// round (or stretched) puffs in the particle colour, alpha blended, fogged. Lit by what is behind them: the luminance of
// the camera's opaque copy there, relative to a typical lit wall (as chalk and paint are), so the mist is dark in a dark
// room instead of glowing; a little of the light comes through it toward the camera from a bright background.
Shader "Backrooms/SprayMist"
{
    Properties
    {
        _Reference ("Luminance of a typical lit wall", Float) = 0.42
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            float _PoolScreenTextures;
            CBUFFER_START(UnityPerMaterial)
                float _Reference;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; float4 screen : TEXCOORD1; half fogFactor : TEXCOORD2; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.screen = ComputeScreenPos(o.positionCS);
                o.color = i.color; o.uv = i.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv * 2.0 - 1.0);
                half a = i.color.a * smoothstep(1.0, 0.0, r) * smoothstep(1.0, 0.25, r);
                float lum = 0.5 * _Reference / 0.42;
                [branch] if (_PoolScreenTextures > 0.5)
                {
                    float2 suv = i.screen.xy / i.screen.w;
                    float3 behind = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(suv), 2).rgb;
                    lum = dot(behind, float3(0.2126, 0.7152, 0.0722));
                }
                float light = clamp(lum / _Reference, 0.03, 2.2);
                half3 col = i.color.rgb * light;
                return half4(MixFog(col, i.fogFactor), a);
            }
            ENDHLSL
        }
    }
}
