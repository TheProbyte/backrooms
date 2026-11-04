// Rust in the Poolrooms (2026-11-01, the realism plan 4 and 5; OfficeChunkBuilder.Stain): multiplied over the tiles.
// A tear runs down a wall from a metal fitting (uv: x across 0..1, y down 0..1): uneven trickles, strongest at the top,
// thinning out below. A ring (_Ring 1) stains the deck round a rail's escutcheon.
Shader "Backrooms/PoolStain"
{
    Properties
    {
        _Color ("Rust", Color) = (0.55, 0.32, 0.17, 1)
        _Strength ("Strength", Range(0, 1)) = 0.6
        _Ring ("Ring (0 tear, 1 ring)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+20" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off ZTest LEqual Cull Back
            Offset -1, -1
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; half _Strength, _Ring;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = i.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                // Each stain's own seed rides in its u (whole part), so the pattern is one piece across the quad.
                float2 uv = float2(frac(i.uv.x), i.uv.y);
                float seed = floor(i.uv.x) * 0.618;
                float a;
                if (_Ring > 0.5)
                {
                    float r = length(uv * 2.0 - 1.0);
                    float n = PoolValueNoise(uv * 7.0 + seed);
                    a = smoothstep(1.0, 0.55, r) * (0.4 + 0.6 * n) * smoothstep(0.12, 0.3, r);
                }
                else
                {
                    // Trickles (2026-11-01, the owner: the rust "cuts off oddly and doesnt look smooth"): a few thin runs, each
                    // a soft line across with its own width and length, heaviest at the top and thinning out well before the
                    // quad's bottom; all of it fades in at the sides, so no edge of the quad shows.
                    a = 0;
                    [unroll] for (int k = 0; k < 5; k++)
                    {
                        float2 h = PoolHash22(float2(seed, k * 7.13));
                        float x0 = 0.2 + 0.6 * h.x, wdt = 0.025 + 0.05 * h.y, len = 0.3 + 0.6 * frac(h.x * 3.7 + h.y);
                        float wob = (PoolValueNoise(float2(uv.y * 3.0, seed + k)) - 0.5) * 0.05;
                        float across = exp(-pow((uv.x - x0 - wob) / wdt, 2.0));
                        float down = exp(-uv.y / (len * 0.45)) * smoothstep(0.0, 0.04, uv.y);
                        a = max(a, across * down * (0.55 + 0.45 * h.y));
                    }
                    a *= smoothstep(0.0, 0.15, uv.x) * smoothstep(1.0, 0.85, uv.x) * smoothstep(1.0, 0.7, uv.y);
                }
                float3 m = lerp(float3(1, 1, 1), _Color.rgb, saturate(a) * _Strength);
                return half4(m, 1);
            }
            ENDHLSL
        }
    }
}
