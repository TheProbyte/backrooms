// Consumer-camcorder video processing, applied after URP post-processing as a full-screen pass.
// Models the parts of a camcorder signal chain that make footage read as "video":
//   optical/sensor softness -> luma edge enhancement (sharpening halos) -> 4:2:x chroma subsampling
//   and bleed -> highlight knee/clip -> pedestal (lifted blacks) -> sensor gain noise (luma + blotchy
//   chroma) -> optional scanlines and tape tracking noise.
//
// Noise quality: a PCG integer hash (no visible structure or repetition) generates Gaussian-like
// noise on a virtual sensor grid (~480-720 lines) with smooth interpolation, so grain has the soft,
// slightly clumped look of a CCD rather than per-pixel digital speckle, and never aligns into patterns.
// A new noise field is drawn every rendered frame.
// Scanlines are box-filtered analytically over each pixel's footprint, so their contrast falls off
// automatically when line spacing approaches the pixel size: no moire at any resolution or window size.
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
            float4 _CamVideo3;   // x pedestal, y knee point, z time (s), w enabled (0/1)
            float4 _CamVideo4;   // x frame index, y sensor lines (noise grid), z scan lines, w grain size multiplier
            float4 _CamVideo5;   // x OSD curvature, y OSD texture enabled
            float4 _CamVideo6;   // xy: motion smear vector in uv units (from the camera's turn rate)
            float4 _CamVideo7;   // x head-switching band, y compression blotches
            TEXTURE2D(_CamOSDTex);

            // ---------------- Noise ----------------

            uint3 Pcg3d(uint3 v)
            {
                v = v * 1664525u + 1013904223u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                v ^= v >> 16u;
                v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
                return v;
            }

            float3 Rand3(int2 p, uint frame, uint salt)
            {
                return float3(Pcg3d(uint3(asuint(p.x), asuint(p.y), frame * 2654435761u + salt))) * (1.0 / 4294967296.0);
            }

            // Approximately Gaussian (sum of three uniforms), zero mean, unit-ish variance.
            float Gauss(int2 p, uint frame, uint salt)
            {
                float3 r = Rand3(p, frame, salt);
                return (r.x + r.y + r.z - 1.5) * 2.0;
            }

            float2 Gauss2(int2 p, uint frame, uint salt)
            {
                float3 a = Rand3(p, frame, salt), b = Rand3(p, frame, salt + 7919u);
                return float2(a.x + a.y + a.z - 1.5, b.x + b.y + b.z - 1.5) * 2.0;
            }

            // Smoothly interpolated noise on a virtual grid; variance-corrected for the bilinear blend.
            float GridNoise(float2 cell, uint frame, uint salt)
            {
                int2 i = (int2)floor(cell);
                float2 f = frac(cell);
                f = f * f * (3.0 - 2.0 * f);
                float a = Gauss(i, frame, salt), b = Gauss(i + int2(1, 0), frame, salt);
                float c = Gauss(i + int2(0, 1), frame, salt), d = Gauss(i + int2(1, 1), frame, salt);
                float n = lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
                float w = (1 - f.x) * (1 - f.x) + f.x * f.x;      // variance of the blend
                float h = (1 - f.y) * (1 - f.y) + f.y * f.y;
                return n * rsqrt(max(w * h, 0.45));
            }

            float2 GridNoise2(float2 cell, uint frame, uint salt)
            {
                int2 i = (int2)floor(cell);
                float2 f = frac(cell);
                f = f * f * (3.0 - 2.0 * f);
                float2 a = Gauss2(i, frame, salt), b = Gauss2(i + int2(1, 0), frame, salt);
                float2 c = Gauss2(i + int2(0, 1), frame, salt), d = Gauss2(i + int2(1, 1), frame, salt);
                float2 n = lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
                float w = (1 - f.x) * (1 - f.x) + f.x * f.x;
                float h = (1 - f.y) * (1 - f.y) + f.y * f.y;
                return n * rsqrt(max(w * h, 0.45));
            }

            // ---------------- Signal helpers ----------------

            float3 Fetch(float2 uv)
            {
                uv = clamp(uv, 0.0005, 0.9995);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            float3 FetchG(float2 uv) { return LinearToSRGB(Fetch(uv)); }

            float3 ToYCC(float3 c)
            {
                float y = dot(c, float3(0.299, 0.587, 0.114));
                return float3(y, (c.b - y) * 0.565, (c.r - y) * 0.713);
            }

            float3 FromYCC(float3 v)
            {
                return float3(v.x + 1.403 * v.z, v.x - 0.344 * v.y - 0.714 * v.z, v.x + 1.770 * v.y);
            }

            float Sinc(float x) { x *= PI; return abs(x) < 1e-4 ? 1.0 : sin(x) / x; }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                if (_CamVideo3.w < 0.5) return float4(Fetch(uv), 1);

                float2 res = _ScreenParams.xy;
                float2 px = 1.0 / res;
                float t = _CamVideo3.z;
                uint frame = (uint)_CamVideo4.x;

                // Tape tracking: a soft band of horizontal displacement drifting up the frame,
                // smooth (low-frequency) jitter so the image wobbles instead of tearing into stripes.
                float tracking = _CamVideo2.w;
                float bandY = frac(t * 0.071);
                float bandDist = abs(uv.y - bandY);
                bandDist = min(bandDist, 1.0 - bandDist);
                float band = tracking > 0.001 ? exp(-bandDist * bandDist / 0.0004) : 0;
                if (band > 0.001)
                {
                    float wob = GridNoise(float2(uv.y * 90.0, t * 12.0), frame, 101u);
                    uv.x += band * wob * 0.004 * tracking;
                }

                // Head-switching band along the bottom: the picture tears sideways there.
                float hsw = _CamVideo7.x;
                float hsK = hsw > 0.001 ? smoothstep(0.045, 0.0, uv.y) * hsw : 0;
                if (hsK > 0.001)
                    uv.x += GridNoise(float2(uv.y * 400.0, t * 40.0), frame, 303u) * 0.03 * hsK;

                // Work in display (gamma) space like the camera's own signal processing.
                float soft = _CamVideo1.y;
                float r1 = max(soft, 0.75);
                float3 c0 = FetchG(uv);
                // 8-tap disc for softness, rotated so the kernel never aligns with the pixel grid.
                float3 blurN = 0;
                [unroll] for (int k = 0; k < 8; k++)
                {
                    float a = k * (PI * 0.25) + 0.3926991;
                    blurN += FetchG(uv + float2(cos(a), sin(a)) * px * r1);
                }
                blurN *= 0.125;
                float softW = saturate(soft * 0.5);
                float3 softC = lerp(c0, (c0 + blurN * 2) / 3, softW);

                // Edge enhancement on luma only (classic halos around edges), with coring so noise and
                // fine texture are not sharpened into crunch.
                float r2 = r1 + 1.5;
                float3 wide = 0;
                [unroll] for (int j = 0; j < 8; j++)
                {
                    float a = j * (PI * 0.25);
                    wide += FetchG(uv + float2(cos(a), sin(a)) * px * r2);
                }
                wide *= 0.125;
                float yS = ToYCC(softC).x;
                float edge = yS - ToYCC(wide).x;
                float cored = sign(edge) * max(abs(edge) - 0.008, 0);
                cored = sign(cored) * min(abs(cored), 0.12);
                float y = yS + cored * _CamVideo1.x;

                // Chroma: horizontally subsampled and smeared, slightly delayed relative to luma.
                float bleed = _CamVideo1.z, shift = _CamVideo1.w;
                float2 cc = 0; float wsum = 0;
                [unroll] for (int i = -3; i <= 3; i++)
                {
                    float w = exp(-i * i / 4.5);
                    cc += ToYCC(FetchG(uv + float2((i * bleed / 3.0 + shift) * px.x, 0))).yz * w;
                    wsum += w;
                }
                cc /= wsum;

                // Compression blotches: chroma and brightness held in 12 px blocks that re-draw every few frames.
                float blk = _CamVideo7.y;
                if (blk > 0.001)
                {
                    float2 bpx = floor(uv * res / 12.0);
                    float2 buv = (bpx + 0.5) * 12.0 / res;
                    float2 cb = ToYCC(FetchG(buv)).yz;
                    float rnd = Rand3(int2(bpx), frame / 3u, 55u).x;
                    cc = lerp(cc, cb, saturate(blk * (0.3 + 0.9 * rnd)));
                    y += (Rand3(int2(bpx), frame / 5u, 77u).y - 0.5) * 0.045 * blk;
                }

                float3 col = FromYCC(float3(y, cc));

                // Sensor motion blur: smear along the camera's turn, mostly trailing behind the motion.
                float2 mv = _CamVideo6.xy;
                float mlen = length(mv * res);
                if (mlen > 1.5)
                {
                    float3 acc = 0;
                    [unroll] for (int m = 0; m < 16; m++)
                        acc += FetchG(uv + mv * (m / 15.0 - 0.7));
                    float3 mb = acc * (1.0 / 16.0);
                    col = lerp(col, mb, saturate((mlen - 1.5) / 5.0));
                }

                // Highlight knee: compress above the knee point and clip at 100%.
                float knee = _CamVideo3.y;
                float3 over = max(col - knee, 0);
                float range = max(1.0 - knee, 0.02);
                col = min(col, knee) + range * over / (over + range);

                // Pedestal: blacks sit slightly above zero.
                col = col * (1 - _CamVideo3.x) + _CamVideo3.x;

                // Sensor gain noise on a virtual sensor grid. Luma grain is finer; chroma noise is coarser
                // (half the chroma resolution), as in real 4:2:x video. Strongest in the shadows.
                float aspect = res.x / res.y;
                float lines = _CamVideo4.y / max(_CamVideo4.w, 0.25);
                float2 cell = uv * float2(lines * aspect, lines);
                float nL = GridNoise(cell, frame, 11u);
                float2 nC = GridNoise2(cell * 0.5, frame, 23u);
                float lum = saturate(dot(col, float3(0.299, 0.587, 0.114)));
                float shadow = 0.35 + 0.65 * (1 - lum) * (1 - lum) + 0.15 * lum;
                col += nL * _CamVideo2.x * 0.3 * shadow;
                col += FromYCC(float3(0, nC * _CamVideo2.y * 0.26 * shadow));

                // Scanlines, box-filtered over the pixel footprint (contrast * sinc) to avoid moire.
                float scanLines = _CamVideo4.z;
                float period = res.y / scanLines;                      // pixels per video line
                float yPix = uv.y * res.y;
                float aa = Sinc(1.0 / period);                         // loss from integrating one pixel
                float profile = 0.5 + 0.5 * cos(2 * PI * yPix / period) * aa;
                col *= 1 - _CamVideo2.z * profile;

                // Tracking band: faint brightening and soft dropout noise.
                if (band > 0.001)
                {
                    float spark = GridNoise(float2(uv.x * 160.0, uv.y * res.y * 0.5), frame, 211u);
                    col += (0.04 + max(spark, 0) * 0.12) * band * tracking;
                }

                // Head-switching noise: a band of blue / magenta static that changes colour and flickers.
                if (hsK > 0.001)
                {
                    float hue = 0.5 + 0.5 * sin(t * 2.3 + uv.x * 5.0);
                    float3 tint = lerp(float3(0.22, 0.3, 1.0), float3(1.0, 0.25, 0.75), hue);
                    float sp = GridNoise(float2(uv.x * 220.0, uv.y * res.y * 0.6), frame, 331u);
                    col = lerp(col, tint * (0.55 + 0.45 * saturate(sp * 0.5 + 0.5)), saturate(hsK * 0.55));
                    col += max(sp, 0) * 0.25 * hsK * step(uv.y, 0.012);          // the bottom few lines: bright static
                }

                // Viewfinder OSD (drawn by its own camera), bent with a gentle barrel warp so the text
                // and icons curve like an overlay seen through the eyepiece lens. A faint glow keeps
                // the characters from looking pasted on.
                if (_CamVideo5.y > 0.5)
                {
                    float2 c = (input.texcoord - 0.5) * float2(aspect, 1);
                    float r2 = dot(c, c) / (0.25 * (aspect * aspect + 1));      // 0 centre .. 1 corner
                    float k = _CamVideo5.x * 0.09;
                    float2 ouv = 0.5 + (input.texcoord - 0.5) * (1 + k * r2) / (1 + k * 0.35);
                    if (all(ouv > 0) && all(ouv < 1))
                    {
                        float4 o = SAMPLE_TEXTURE2D(_CamOSDTex, sampler_LinearClamp, ouv);
                        float glowA = 0;
                        [unroll] for (int g = 0; g < 4; g++)
                        {
                            float2 d = float2(g < 2 ? (g == 0 ? 1.5 : -1.5) : 0, g >= 2 ? (g == 2 ? 1.5 : -1.5) : 0) * px;
                            glowA += SAMPLE_TEXTURE2D(_CamOSDTex, sampler_LinearClamp, ouv + d).a;
                        }
                        glowA *= 0.25;
                        float3 osd = LinearToSRGB(o.rgb / max(o.a, 1e-3));
                        col += 0.08 * glowA * (1 - o.a);                         // soft halo
                        col = lerp(col, osd, o.a);
                    }
                }

                return float4(SRGBToLinear(saturate(col)), 1);
            }
            ENDHLSL
        }
    }
}
