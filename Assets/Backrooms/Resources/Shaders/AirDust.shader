// Dust in the air (Docs/RealismPlan.md, section 7; Generation/AirDust.cs): tiny soft additive motes round the camera,
// each lit only by how far inside the light cone under one of the nearest lit ceiling fixtures it is, so the air under a
// light holds a faint drifting sparkle and the rest stays clear. The fixtures come in _AirDustLights (xyz: the lens,
// w: its power x strength), set every frame; walls are left to the depth test.
Shader "Backrooms/AirDust"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.95, 0.84, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AirDust"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            float4 _AirDustLights[8];
            float _AirDustCount;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float3 positionWS : TEXCOORD1; half sun : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = i.uv; o.color = i.color;
                // The Poolrooms (2026-11-01, the realism plan 1): no fixtures; a mote glints only inside a sun beam.
                o.sun = _PoolLitBlend > 0.5 ? PoolSunVisibility(o.positionWS) * 2.2 : 0.0;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 c = i.uv * 2.0 - 1.0;
                half dot2 = saturate(1.0 - dot(c, c));
                half lit = i.sun;
                [loop] for (int k = 0; k < 8; k++)
                {
                    if (k >= (int)_AirDustCount) break;
                    float3 d = i.positionWS - _AirDustLights[k].xyz;
                    float dist = length(d);
                    half down = smoothstep(0.55, 0.85, -d.y / max(dist, 1e-4));      // inside the cone under the lens
                    half reach = saturate(1.0 - dist / 2.6);
                    lit += down * reach * reach * _AirDustLights[k].w;
                }
                return half4(_Color.rgb * (dot2 * dot2 * lit * i.color.a), 1.0);
            }
            ENDHLSL
        }
    }
}
