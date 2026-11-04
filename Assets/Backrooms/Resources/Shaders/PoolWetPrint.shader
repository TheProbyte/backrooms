// Wet footprints on the Poolrooms' dry decks (PoolWetPrints.cs): a shoe's sole, darkening the tile it is on (multiplied,
// like FloorWear: it takes the tile's light), its wetness per print (_Wet, instanced) fading as it dries. Drawn in one
// instanced call for all of them.
Shader "Backrooms/PoolWetPrint"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+450" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off ZTest LEqual Cull Off
            Offset -1, -4
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Wet)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float wet : TEXCOORD1; float2 seed : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                Varyings o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(p);
                o.uv = i.uv;
                o.wet = UNITY_ACCESS_INSTANCED_PROP(Props, _Wet);
                o.seed = UNITY_MATRIX_M._m03_m23;
                return o;
            }

            float Ellipse(float2 p, float2 c, float2 r) { float2 q = (p - c) / r; return (length(q) - 1.0) * min(r.x, r.y); }
            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

            half4 frag(Varyings i) : SV_Target
            {
                // The sole, in the quad (x across 0..1, y heel 0 .. toe 1): the ball of the foot and the heel, joined.
                float2 p = i.uv;
                float d = min(Ellipse(p, float2(0.52, 0.68), float2(0.4, 0.3)), Ellipse(p, float2(0.5, 0.2), float2(0.34, 0.19)));
                d = min(d, Ellipse(p, float2(0.55, 0.44), float2(0.25, 0.22)));
                float a = 1.0 - smoothstep(-0.03, 0.02, d);
                // Patchy: the tread leaves more water in some places; drier toward the edges as it fades.
                float tread = 0.75 + 0.25 * Hash(floor(p * float2(9, 22)) + i.seed);
                a *= tread * smoothstep(0.0, 0.25, i.wet + (-d) * 2.0);
                float k = saturate(a * i.wet);
                return half4(lerp(1.0, 0.7, k).xxx, 1);
            }
            ENDHLSL
        }
    }
}
