// The sky seen up through the Poolrooms' light wells: a bright gradient with the sun in it. No fog: it is outside.
// 2026-11-01 (the realism plan): clouds drifting over it (the same deck that shades the sun, PoolCloudCover), and in the
// rooms whose roof lights are glazed (PoolGlazed) a dusty, slightly milky pane in a frame of bars every 0.6 m.
Shader "Backrooms/PoolSky"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); return o; }
            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                float3 P = i.positionWS, dir = -GetWorldSpaceNormalizeViewDir(P);
                float3 sky = PoolSkyColor(dir);
                if (_PoolCloud.w > 0 && dir.y > 0.05)
                {
                    float2 deck = P.xz + dir.xz / dir.y * (300.0 - P.y);
                    float cover = PoolCloudCover(deck);
                    float lum = dot(_PoolSkyHorizon.rgb, float3(0.3, 0.5, 0.2));
                    float3 cloud = lum * float3(0.95, 0.96, 0.98) * (0.85 + 0.6 * pow(saturate(dot(dir, POOL_SUN)), 6.0));
                    sky = lerp(sky, cloud, cover * 0.9);
                }
                int storey = PoolStorey(P.y);
                int2 c = PoolCellOf(P);
                PoolCell cell = PoolLoad(c, storey);
                if (PoolGlazed(c, cell.sky))
                {
                    float px = max(length(fwidth(P.xz)) * 1.5, 0.004);
                    float2 d2 = abs(frac(P.xz / 0.6 + 0.5) - 0.5) * 0.6;
                    float pane = smoothstep(0.02 - px, 0.02 + px, min(d2.x, d2.y));    // 0 on a bar, 1 in the glass
                    float3 milky = dot(sky, 1.0 / 3.0) * float3(0.92, 0.95, 0.93);
                    // Dust gathered toward the frames, streaks where rain ran, and now and then a pane broken out.
                    float2 local = frac(P.xz / 0.6);
                    float edge = 1.0 - min(min(local.x, 1.0 - local.x), min(local.y, 1.0 - local.y)) * 2.0;
                    float streak = PoolValueNoise(float2(P.x * 30.0, P.z * 2.0)) * PoolValueNoise(float2(P.x * 3.0 + 5.0, P.z * 22.0));
                    float dirt = saturate(edge * edge * 0.6 + streak * 0.5);
                    float3 glass = lerp(lerp(sky, milky, 0.45) * 0.8, milky * 0.45, dirt * 0.5);
                    if (PoolPaneBroken(P.xz))
                    {
                        // Shards left in the frame: a jagged rim of glass round a clear hole.
                        float2 c = local - 0.5;
                        float ang = atan2(c.y, c.x);
                        float jag = 0.3 + 0.09 * sin(ang * 7.0 + PoolHash22(floor(P.xz / 0.6)).x * 6.0) + 0.05 * sin(ang * 17.0);
                        glass = max(abs(c.x), abs(c.y)) > jag ? glass : sky;
                    }
                    float3 bar = float3(0.2, 0.21, 0.2) * PoolAmbientAt(P - float3(0, 0.3, 0), float3(0, -1, 0));
                    sky = lerp(bar, glass, pane);
                }
                return half4(PoolFarFade(sky, P), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Off
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
    }
}
