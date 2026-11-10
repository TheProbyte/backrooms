// The style lab's look (StyleLabLook): the camera's finished picture, banded, outlined and graded.
Shader "Hidden/Backrooms/StyleLab"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "StyleLab"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_SL_Depth);
            TEXTURE2D_X(_SL_Normals);
            float4 _SL_P0;   // pixel size, bands, band blend, dither
            float4 _SL_P1;   // outline, depth, normal, contrast sensitivity
            float4 _SL_P2;   // outline fade (m), outline width, saturation, contrast
            float4 _SL_P3;   // shadow tint amount
            float4 _SL_Res;  // w, h, 1/w, 1/h
            float4 _SL_OutlineColor, _SL_Tint, _SL_ShadowTint;

            float3 Col(float2 uv) { return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb; }
            float Eye(float2 uv) { return LinearEyeDepth(SAMPLE_TEXTURE2D_X(_SL_Depth, sampler_PointClamp, uv).r, _ZBufferParams); }
            float3 Nrm(float2 uv) { return SAMPLE_TEXTURE2D_X(_SL_Normals, sampler_PointClamp, uv).xyz; }
            float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            static const float BAYER[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float px = max(1.0, _SL_P0.x);
                float2 cell = px * _SL_Res.zw;
                float2 cid = floor(uv / cell);
                float2 suv = (cid + 0.5) * cell;
                float3 c = Col(suv);
                if (_SL_P3.y > 0.5 && _SL_P3.y < 1.5) return half4(frac(Eye(suv) / 4.0).xxx, 1);
                if (_SL_P3.y > 1.5) return half4(Nrm(suv) * 0.5 + 0.5, 1);

                // outlines: a cross of samples one outline-width away
                float2 o = cell * _SL_P2.y;
                float d0 = Eye(suv), dA = Eye(suv + o), dB = Eye(suv - o), dC = Eye(suv + float2(o.x, -o.y)), dD = Eye(suv + float2(-o.x, o.y));
                float dEdge = (abs(dA - dB) + abs(dC - dD)) / max(d0, 0.05);
                float3 nA = Nrm(suv + o), nB = Nrm(suv - o), nC = Nrm(suv + float2(o.x, -o.y)), nD = Nrm(suv + float2(-o.x, o.y));
                float nEdge = length(nA - nB) + length(nC - nD);
                float3 cA = Col(suv + o), cB = Col(suv - o), cC = Col(suv + float2(o.x, -o.y)), cD = Col(suv + float2(-o.x, o.y));
                float lEdge = abs(Luma(LinearToSRGB(cA)) - Luma(LinearToSRGB(cB))) + abs(Luma(LinearToSRGB(cC)) - Luma(LinearToSRGB(cD)));
                float edge = max(saturate((dEdge - 0.06) * 6.0 * _SL_P1.y), saturate((nEdge - 0.35) * 2.0 * _SL_P1.z));
                edge = max(edge, saturate((lEdge - 0.12) * 4.0 * _SL_P1.w));
                edge *= _SL_P1.x * saturate(1.0 - d0 / max(_SL_P2.x, 0.1));

                // bands: brightness cut into steps in display space, keeping each colour's hue
                float3 s = LinearToSRGB(max(c, 0.0));
                float l = Luma(s);
                uint2 b = (uint2)cid & 3u;
                float dth = (BAYER[b.y * 4 + b.x] / 16.0 - 0.5) * _SL_P0.w;
                float n = max(2.0, _SL_P0.y);
                float lq = clamp(floor(l * n + 0.5 + dth) / n, 0.0, 1.0);
                float3 sq = s * (lq / max(l, 1e-4));
                s = lerp(s, sq, _SL_P0.z);

                // grade: contrast, saturation, shadow tint, tint
                s = (s - 0.5) * _SL_P2.w + 0.5;
                float l2 = Luma(s);
                s = lerp(l2.xxx, s, _SL_P2.z);
                s *= lerp(_SL_ShadowTint.rgb, 1.0.xxx, saturate(l2 * 1.8 + (1.0 - _SL_P3.x)));
                s *= _SL_Tint.rgb;
                s = lerp(s, LinearToSRGB(_SL_OutlineColor.rgb), saturate(edge) * _SL_OutlineColor.a);
                return half4(SRGBToLinear(saturate(s)), 1.0);
            }
            ENDHLSL
        }
    }
}
