// Glass block in some of the Poolrooms' high windows (2026-11-01, the realism plan 6; OfficeChunkBuilder.WindowFaces):
// 19 cm blocks in pale mortar, each glowing with the daylight behind it, rippled inside as pressed glass is (the light
// pooling toward each block's middle, wavy lines through it), dimmer while a cloud passes.
Shader "Backrooms/PoolGlassBlock"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half fog : TEXCOORD2; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                float3 P = i.positionWS, N = normalize(i.normalWS);
                float2 w = float2(dot(P.xz, float2(N.z, -N.x)), P.y);
                const float B = 0.195;
                float2 g = w / B;
                float2 f = frac(g) - 0.5;
                float2 id = floor(g);
                float2 e = 0.5 - abs(f);
                float px = max(fwidth(g.x), fwidth(g.y));
                float mortar = 1.0 - smoothstep(0.035, 0.035 + px * 1.5, min(e.x, e.y));
                // Daylight through the block: brightest in its middle, rippled lines across it.
                float sky = dot(_PoolSkyHorizon.rgb, float3(0.3, 0.5, 0.2));
                float centre = 1.0 - dot(f, f) * 1.6;
                float2 h = PoolHash22(id);
                float ripple = 0.85 + 0.15 * sin((f.x * 2.0 + f.y * 0.6 + h.x) * 18.0) * sin((f.y * 2.2 - f.x * 0.4 + h.y) * 15.0);
                float cloud = PoolCloudShade(P);
                float3 glow = sky * float3(0.88, 0.95, 0.95) * 0.17 * (0.55 + 0.45 * centre) * ripple * (0.85 + 0.3 * h.x) * lerp(0.75, 1.0, cloud) + _PoolSunColor.rgb * 0.012 * cloud;
                float3 grout = float3(0.8, 0.8, 0.76) * PoolAmbientAt(P, N);
                float3 c = lerp(glow, grout, mortar);
                return half4(PoolFarFade(MixFog(c, i.fog), P), 1);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
