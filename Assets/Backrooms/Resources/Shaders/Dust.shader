// Gypsum dust and crumbs knocked out of a wall (Destruction.WallBreaker): soft round particles in their own colour, lit
// by the light of what is behind them (the luminance of the camera's opaque copy, relative to a typical lit wall), so a
// cloud is as bright as the room it hangs in, white under a panel, grey in a dark office. Cameras without the opaque copy
// use a fixed middle grey. _Hard: 0 a soft puff, 1 a crumb with a firm edge.
Shader "Backrooms/Dust"
{
    Properties
    {
        _Reference ("Luminance of a typical lit wall", Float) = 0.42
        _Hard ("Hard edge (crumbs)", Range(0, 1)) = 0
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
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            float _PoolScreenTextures;     // 1 where the camera has its opaque copy (set with the Poolrooms' screen textures)
            CBUFFER_START(UnityPerMaterial)
                float _Reference;
                float _Hard;
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
                float hard = saturate(_Hard);
                half a = i.color.a * lerp(smoothstep(1.0, 0.0, r) * smoothstep(1.0, 0.25, r), smoothstep(1.0, 0.7, r), hard);
                // The light here: what is behind, from the opaque copy (cameras without it: a middle grey).
                half light = 0.6;
                [branch] if (_PoolScreenTextures > 0.5)
                {
                    float2 uv = i.screen.xy / max(i.screen.w, 1e-5);
                    half lum = dot(SampleSceneColor(uv), half3(0.2126, 0.7152, 0.0722));
                    light = clamp(lum / max(_Reference, 1e-3), 0.0, 2.5);
                }
                return half4(MixFog(i.color.rgb * light, i.fogFactor), a);
            }
            ENDHLSL
        }
    }
}
