// Falling water in the water park (2026-11-01; OfficeChunkBuilder.WaterPark): a mushroom fountain's curtain, an arch's
// stream, the water down a flume. uv.x runs across the sheet, uv.y is the metres the water has fallen. The pattern is
// keyed to when the water left the lip (now minus the time to fall that far: sqrt(2y/g)), so features speed up as they
// fall and stretch as real falling water does. It leaves the lip as a clear glassy sheet (the room behind it refracted),
// then breaks into ropes and drops lower down, whiter where air is mixed in, with a Fresnel sheen and the sun's glints.
// After the usual waterfall approach in games (UVs along the fall, a scrolling breakup mask, edge breakup the weak point).
Shader "Backrooms/PoolSheet"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2;
                float4 screen : TEXCOORD3; half fog : TEXCOORD4; float3 light : TEXCOORD5;
            };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv;
                o.screen = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.light = PoolAmbientAt(o.positionWS, float3(0, 1, 0)) + _PoolSunColor.rgb * PoolSunVisibility(o.positionWS) * 0.35;
                return o;
            }
            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                float3 P = i.positionWS;
                float3 N = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                float3 V = normalize(_WorldSpaceCameraPos - P);
                float y = max(i.uv.y, 0.0);
                float age = sqrt(2.0 * y / 9.81);                 // seconds since this water left the lip
                float s = _Time.y - age;                            // when it left: the pattern travels with the water
                float br = saturate((age - 0.06) / 0.38);           // 0 glassy at the lip, 1 broken (about 0.7 m down)
                float u = i.uv.x;
                // Ropes and gaps across the sheet, and smaller ripples riding on them.
                float n1 = PoolValueNoise(float2(u * 6.0, s * 5.0));
                float n2 = PoolValueNoise(float2(u * 17.0 + 3.1, s * 13.0 + 7.0));
                float n3 = PoolValueNoise(float2(u * 41.0 + 9.7, s * 31.0));
                float ropes = n1 * 0.6 + n2 * 0.3 + n3 * 0.1;
                float px = fwidth(ropes) + 0.02;
                float cut = br * 0.6;
                float cover = smoothstep(cut - px, cut + px, ropes);
                // Drops thrown off the broken sheet below.
                float2 g = float2(u * 26.0, s * 34.0);
                float2 h = PoolHash22(floor(g));
                float2 f = frac(g) - 0.5 - (h - 0.5) * 0.5;
                float drop = step(0.78, h.x) * (1.0 - smoothstep(0.12, 0.2, length(f * float2(1.0, 0.5)))) * br;
                cover = max(cover, drop);
                if (cover < 0.004) discard;
                // The sheet's ripples bend its normal (more as it breaks up).
                float2 tilt = (float2(n2, n3) - 0.5) * (0.5 + 1.2 * br);
                float3 T = normalize(cross(N, float3(0, 1, 0)) + 1e-4);
                float3 Nb = normalize(N + T * tilt.x + float3(0, 1, 0) * tilt.y * 0.4);
                float nv = saturate(abs(dot(Nb, V)));
                float fres = 0.02 + 0.98 * pow(1.0 - nv, 5.0);
                // Behind it: the room, refracted, where the screen's opaque copy exists; else a pale tint.
                float3 behind;
                float a;
                float3 tint = float3(0.86, 0.95, 0.94);
                if (_PoolScreenTextures > 0.5)
                {
                    float2 suv = i.screen.xy / i.screen.w + Nb.xz * 0.025 * (1.0 - br * 0.5);
                    behind = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(suv), 0).rgb * tint;
                    a = cover;
                }
                else { behind = i.light * tint * 0.6; a = cover * 0.45; }
                float3 R = reflect(-V, Nb);
                float3 refl = PoolSkyColor(R) * 0.35 + i.light * 0.5;
                float glint = pow(saturate(dot(R, POOL_SUN)), 180.0) * 6.0;
                // Air mixed in where it breaks: white, lit by the room.
                float white = saturate(br * (0.25 + 0.55 * n2) + drop * 0.5);
                float3 c = lerp(behind, i.light * 0.95, white * 0.75);
                c = lerp(c, refl, fres) + _PoolSunColor.rgb * glint * PoolSunVisibility(P) * 0.05;
                a *= lerp(1.0, 0.85, br);
                return half4(PoolFarFade(MixFog(c, i.fog), P), saturate(a));
            }
            ENDHLSL
        }
    }
}
