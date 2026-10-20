// Consumer-camcorder video processing, applied after URP post-processing as a full-screen pass.
// Models the parts of a camcorder signal chain that make footage read as "video":
//   optical/sensor softness -> luma edge enhancement (sharpening halos) -> 4:2:x chroma subsampling
//   and bleed -> highlight knee/clip -> pedestal (lifted blacks) -> gain noise (luma + blotchy chroma)
//   -> optional scanlines and tape tracking noise.
// All parameters are globals driven by CamcorderRig so no material asset is modified at runtime.
Shader "Backrooms/CamcorderVideo"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "CamcorderVideo"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _CamVideo1;   // x sharpen amount, y softness (px), z chroma bleed (px), w chroma shift (px)
            float4 _CamVideo2;   // x luma noise, y chroma noise, z scanlines, w tracking noise
            float4 _CamVideo3;   // x pedestal, y knee point, z time, w enabled (0/1)

            float Hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            float3 Fetch(float2 uv)
            {
                uv = clamp(uv, 0.0005, 0.9995);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            float3 ToYCC(float3 c)
            {
                float y = dot(c, float3(0.299, 0.587, 0.114));
                return float3(y, (c.b - y) * 0.565, (c.r - y) * 0.713);
            }

            float3 FromYCC(float3 v)
            {
                return float3(v.x + 1.403 * v.z, v.x - 0.344 * v.y - 0.714 * v.z, v.x + 1.770 * v.y);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float3 src = Fetch(uv);
                if (_CamVideo3.w < 0.5) return float4(src, 1);

                float2 px = 1.0 / _ScreenParams.xy;
                float t = _CamVideo3.z;

                // Tape tracking: a faint band of horizontal displacement and noise drifting up the frame.
                float tracking = _CamVideo2.w;
                if (tracking > 0.001)
                {
                    float bandY = frac(t * 0.071);
                    float band = saturate(1 - abs(uv.y - bandY) / 0.025);
                    float jitter = (Hash(float2(floor(uv.y * _ScreenParams.y), floor(t * 30))) - 0.5);
                    uv.x += band * jitter * 0.012 * tracking;
                }

                // Work in display (gamma) space like the camera's own signal processing.
                float soft = _CamVideo1.y;
                float3 c0 = LinearToSRGB(Fetch(uv));
                float3 blurN = 0;
                float2 o = px * max(soft, 0.75);
                blurN += LinearToSRGB(Fetch(uv + float2(o.x, 0)));
                blurN += LinearToSRGB(Fetch(uv - float2(o.x, 0)));
                blurN += LinearToSRGB(Fetch(uv + float2(0, o.y)));
                blurN += LinearToSRGB(Fetch(uv - float2(0, o.y)));
                blurN *= 0.25;
                // Softness: lens + sensor resolution limit.
                float softW = saturate(soft * 0.5);
                float3 softC = lerp(c0, (c0 + blurN * 2) / 3, softW);

                // Edge enhancement on luma only (classic white/black halos around edges).
                float2 o2 = px * (max(soft, 0.75) + 1.5);
                float3 wide = LinearToSRGB(Fetch(uv + float2(o2.x, 0))) + LinearToSRGB(Fetch(uv - float2(o2.x, 0)))
                            + LinearToSRGB(Fetch(uv + float2(0, o2.y))) + LinearToSRGB(Fetch(uv - float2(0, o2.y)));
                wide *= 0.25;
                float yS = ToYCC(softC).x;
                float yW = ToYCC(wide).x;
                float edge = yS - yW;
                edge = sign(edge) * min(abs(edge), 0.12);   // limit halo strength like a real coring circuit
                float y = yS + edge * _CamVideo1.x;

                // Chroma: horizontally subsampled and smeared, slightly delayed (shifted) relative to luma.
                float bleed = _CamVideo1.z, shift = _CamVideo1.w;
                float2 cc = 0;
                [unroll] for (int i = -2; i <= 2; i++)
                {
                    float w = 1.0 - abs(i) * 0.25;
                    cc += ToYCC(LinearToSRGB(Fetch(uv + float2((i * bleed * 0.5 + shift) * px.x, 0)))).yz * w;
                }
                cc /= 3.0;

                float3 col = FromYCC(float3(y, cc));

                // Highlight knee: compress above the knee point and clip at 100%.
                float knee = _CamVideo3.y;
                float3 over = max(col - knee, 0);
                float range = max(1.0 - knee, 0.02);
                col = min(col, knee) + range * over / (over + range);

                // Pedestal: blacks sit slightly above zero.
                col = col * (1 - _CamVideo3.x) + _CamVideo3.x;

                // Gain noise: fine luma grain plus coarser, blotchy chroma noise; stronger in shadows.
                float2 p = floor(uv * _ScreenParams.xy);
                float tf = floor(t * 30);
                float nL = (Hash(p + tf * 1.37) + Hash(p.yx + tf * 2.11) - 1.0);
                float2 pc = floor(uv * _ScreenParams.xy * 0.5);
                float2 nC = float2(Hash(pc + tf * 3.7), Hash(pc.yx + tf * 5.3)) - 0.5;
                float shadow = 0.45 + 0.55 * (1 - saturate(y));
                col += nL * _CamVideo2.x * shadow;
                col += FromYCC(float3(0, nC * _CamVideo2.y * shadow)) ;

                // Scanlines (very subtle line structure of interlaced video).
                float lines = 0.5 + 0.5 * cos(uv.y * _ScreenParams.y * PI);
                col *= 1 - _CamVideo2.z * lines;

                if (tracking > 0.001)
                {
                    float bandY = frac(t * 0.071);
                    float band = saturate(1 - abs(uv.y - bandY) / 0.012);
                    col += (Hash(p + tf) - 0.5) * band * 0.35 * tracking;
                }

                return float4(SRGBToLinear(saturate(col)), 1);
            }
            ENDHLSL
        }
    }
}
