// Wear and dirt (Generation/OfficeChunkBuilder.Wear.cs, .Details.cs): soft patches multiplied onto the carpet, the walls
// and the doors after they are lit, so they take their light and colour. Each quad carries its tint and strength (vertex colour), a profile in uv (-1..1
// across and along; |uv| past the inner edge in uv2 fades out) and is broken up by world-space blotches, so no two metres
// look alike: grime along the bottoms of walls, a seam at each doorway, trodden paths between doorways. One small unlit
// pass, a mesh a chunk; nothing to do with the lit shader's variants.
Shader "Backrooms/FloorWear"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+455" "IgnoreProjector" = "True" }
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 inner : TEXCOORD1; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 inner : TEXCOORD1; float2 xz : TEXCOORD2; half4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = i.uv; o.inner = i.inner; o.xz = ws.xz; o.color = i.color;
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), u.x), u.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float profile;
                if (i.inner.x < 0.0)
                {
                    // Round (a spot, a nail hole): solid to -inner.x, soft to the edge; inner.y: a darker ring at the rim
                    // (a dried coffee spill's edge).
                    float r = length(i.uv);
                    float e = (r - 0.82) / 0.12;
                    profile = saturate((1.0 - smoothstep(-i.inner.x, 1.0, r)) * (1.0 - i.inner.y * 0.55) + exp(-e * e) * i.inner.y);
                }
                else
                {
                    float2 a = abs(i.uv);
                    float pu = 1.0 - smoothstep(i.inner.x, 1.0, a.x);
                    float pv = 1.0 - smoothstep(i.inner.y, 1.0, a.y);
                    profile = pu * pv;
                }
                // Blotches: a metre scale and a hand's-width scale.
                float n = Noise(i.xz * 1.7) * 0.65 + Noise(i.xz * 7.3) * 0.35;
                float k = saturate(i.color.a * profile * (0.45 + n * 1.1));
                return half4(lerp(1.0.xxx, i.color.rgb, k), 1.0);
            }
            ENDHLSL
        }
    }
}
