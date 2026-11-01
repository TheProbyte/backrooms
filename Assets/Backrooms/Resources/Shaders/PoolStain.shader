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
                float2 uv = i.uv;
                float seed = dot(floor(i.positionWS * 3.0), float3(1.3, 7.1, 3.7));
                float a;
                if (_Ring > 0.5)
                {
                    float r = length(uv * 2.0 - 1.0);
                    float n = PoolValueNoise(uv * 7.0 + seed);
                    a = smoothstep(1.0, 0.55, r) * (0.4 + 0.6 * n) * smoothstep(0.12, 0.3, r);
                }
                else
                {
                    // Trickles: a few uneven columns, each its own length.
                    float col = PoolValueNoise(float2(uv.x * 7.0 + seed, 0.5));
                    float len = 0.35 + 0.65 * PoolValueNoise(float2(uv.x * 5.0 + seed * 1.7, 3.0));
                    float run = 1.0 - smoothstep(len * 0.6, len, uv.y);
                    float fine = PoolValueNoise(float2(uv.x * 23.0 + seed, uv.y * 2.0));
                    float edges = smoothstep(0.0, 0.25, uv.x) * smoothstep(1.0, 0.75, uv.x);
                    a = smoothstep(0.35, 0.75, col * 0.7 + fine * 0.3) * run * edges * (1.0 - uv.y * 0.5);
                }
                float3 m = lerp(float3(1, 1, 1), _Color.rgb, saturate(a) * _Strength);
                return half4(m, 1);
            }
            ENDHLSL
        }
    }
}
