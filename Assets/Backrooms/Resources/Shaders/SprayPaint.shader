// Spray paint on walls (SprayPaint.cs): soft speckled dots and drip runs, multiplied onto whatever is under them, so
// the paint takes the surface's own light (dark in a dark room, bright in the sun) without lighting of its own.
// Overlapping dots multiply again, so the paint's strongest channel is kept at 1: coats strip the other channels and
// settle on the pure colour (lit like the wall) instead of sinking toward black.
// Drawn right after the opaque geometry, before the opaque copy, so water still shows paint under it.
Shader "Backrooms/SprayPaint"
{
    Properties
    {
        _PaintColor ("Paint (keep the main channel at 1)", Color) = (1, 0.05, 0.04, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+450" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off ZTest LEqual Cull Back
            Offset -1, -2
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _PortalClipPlane;
            CBUFFER_START(UnityPerMaterial)
                half4 _PaintColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; float3 positionWS : TEXCOORD1; half fogFactor : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = i.uv; o.color = i.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }

            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                float2 q = i.uv.xy;          // -1..1 across a dot; drips: x across (-1..1), y 0 at the top .. 1 at the tip
                float kind = i.uv.z, seed = i.uv.w;
                float a;
                if (kind < 0.5)
                {
                    // A dot of mist: dense middle, soft edge, a speckle of separate droplets toward the rim.
                    float r = length(q);
                    float speck = Hash(floor(q * 7.0) + seed * 17.0);
                    float core = smoothstep(1.0, 0.25, r);
                    a = core * lerp(1.0, step(0.55, speck), smoothstep(0.35, 0.95, r));
                }
                else
                {
                    // A run: thin at its source, fuller toward the tip, ending in a round bead.
                    float y = saturate(q.y);
                    float w = lerp(0.45, 1.0, sqrt(y));
                    float body = smoothstep(1.0, 0.55, abs(q.x) / w);
                    float bead = smoothstep(1.0, 0.6, length(float2(q.x, (q.y - 1.0) * 2.4)) / 1.25);
                    a = max(body * smoothstep(1.05, 0.98, q.y), bead);
                }
                a = saturate(a * i.color.a);
                // Fade with the fog, so distant paint goes the way of its wall.
                float visible = 1.0;
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    visible = saturate(ComputeFogIntensity(i.fogFactor));
                #endif
                half3 m = lerp(half3(1, 1, 1), _PaintColor.rgb, a * visible);
                return half4(m, 1);
            }
            ENDHLSL
        }
    }
}
