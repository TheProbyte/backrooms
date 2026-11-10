// The player's body seen from the Poolrooms' sun (PlayerShadow.cs, 2026-11-01): a small shadow map of the body alone, as
// Unreal's per-object (inset) shadows give a character its own map. Each texel keeps the distance along the sun of the
// body's part nearest the sun there (blended by max); PoolCommon.hlsl (PoolBodySun) tests points against it.
Shader "Hidden/Backrooms/PlayerShadowMap"
{
    Properties { }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "BodyDepth"
            BlendOp Max
            Blend One One
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4x4 _PoolBodyMtx;          // world -> (u, v, distance toward the sun from the body's middle)
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float depth : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 w = TransformObjectToWorld(i.positionOS.xyz);
                float3 m = mul(_PoolBodyMtx, float4(w, 1)).xyz;
                float2 c = m.xy * 2 - 1;
                #if UNITY_UV_STARTS_AT_TOP
                c.y = -c.y;
                #endif
                o.positionCS = float4(c, 0.5, 1);
                o.depth = m.z;
                return o;
            }

            float4 frag(Varyings i) : SV_Target { return float4(i.depth, 0, 0, 0); }
            ENDHLSL
        }
    }
}
