// Spray paint on the world's surfaces (Interaction/Items/SprayPaint.cs, 2026-11-04): a canvas of paint (coats in a
// byte texture, 64 a coat) on a quad a millimetre off its surface.
// - Covers as paint does: opacity 1 - exp(-2.3 coats) (one coat nearly hides what is under it).
// - Thin paint is separate droplets: every 0.7 mm cell of the surface has its droplet (a round dot at a random place in
//   it), bigger the more paint there is, so a dusting from far off is a fine speckle and a full coat is solid; where the
//   cells are smaller than a pixel the speckle gives way to the plain coverage (no sparkle in the distance).
// - Lit by the light on the surface under it, as chalk and tape are (Marks.shader): the luminance of the camera's
//   opaque copy there, a few samples across 2 cm (so the wallpaper's pattern does not show through the paint), relative
//   to a typical lit wall.
// - Not past the surface's edge: where the depth under the quad is more than 2.5 cm behind it, there is no surface there.
// - A little gloss: gloss enamel's sheen, brighter at a grazing look.
Shader "Backrooms/SprayCanvas"
{
    Properties
    {
        _Coverage ("Coverage (coats / 4)", 2D) = "black" {}
        _PaintColor ("Paint (linear)", Color) = (0.52, 0.014, 0.009, 1)
        _Reference ("Luminance of a typical lit wall", Float) = 0.42
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-70" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            Offset -1, -2
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            float _PoolScreenTextures;
            float4 _PortalClipPlane;
            TEXTURE2D(_Coverage); SAMPLER(sampler_Coverage);
            CBUFFER_START(UnityPerMaterial)
                half4 _PaintColor;
                float _Reference;
                float4 _Coverage_ST;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float4 screen : TEXCOORD2; float eye : TEXCOORD3; half fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screen = ComputeScreenPos(o.positionCS);
                o.eye = -TransformWorldToView(o.positionWS).z;
                o.uv = i.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                float coats = SAMPLE_TEXTURE2D(_Coverage, sampler_Coverage, i.uv).r * (255.0 / 64.0);
                clip(coats - 0.004);
                float cover = 1.0 - exp(-2.3 * coats);

                // Droplets: 0.7 mm cells over the 0.5 m canvas (714 a side).
                float2 c = i.uv * 714.0;
                float2 cell = floor(c);
                float2 h = Hash2(cell);
                float2 centre = 0.25 + 0.5 * h;
                float r = length(frac(c) - centre);
                // the droplet's size grows with the paint here (a full coat: the cells' dots overlap into a film)
                float drop = sqrt(saturate(cover)) * (0.42 + 0.35 * Hash2(cell + 17.0).x) * 1.25;
                float speck = smoothstep(drop + 0.06, drop - 0.06, r);
                // a little of everything thin as a faint tint between droplets (the finest mist)
                speck = max(speck, cover * 0.35);
                float cellPx = max(fwidth(c.x), fwidth(c.y));                 // cells a pixel, or smaller
                float a = lerp(lerp(speck, cover, smoothstep(0.85, 0.98, cover)), cover, saturate((cellPx - 0.4) / 0.8));

                // Not past the edge of what it is on.
                float2 suv = i.screen.xy / i.screen.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                clip(0.025 - (scene - i.eye));

                // The light on the surface under it (the opaque copy, a few samples across ~2 cm).
                float lum = 0.5 * _Reference / 0.42;
                [branch] if (_PoolScreenTextures > 0.5)
                {
                    float px = length(ddx(i.positionWS)) + length(ddy(i.positionWS));
                    float rr = clamp(0.02 / max(px * 0.5, 1e-5), 1.0, 40.0);
                    float2 st = rr / _ScreenParams.xy;
                    float m = 0.0;
                    [unroll] for (int k = 0; k < 6; k++)
                    {
                        float ang = k * 1.0472;
                        float2 o = float2(cos(ang), sin(ang)) * st;
                        float3 s = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(suv + o), 0).rgb;
                        m += dot(s, float3(0.2126, 0.7152, 0.0722));
                    }
                    lum = m / 6.0;
                }
                float light = min(lum / _Reference, 2.5);
                // gloss: a faint sheen toward a grazing look, where the coat is full
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 n = normalize(cross(ddx(i.positionWS), ddy(i.positionWS)));
                float graze = pow(1.0 - saturate(abs(dot(n, v))), 4.0) * smoothstep(0.6, 1.0, cover);
                half3 col = _PaintColor.rgb * light + graze * 0.06 * light;
                col = MixFog(col, i.fogFactor);
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
}
