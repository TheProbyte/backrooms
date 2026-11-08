// A seam's glow (Interaction.Seam, Docs/ContractFloorsPlan.md): the real world's light through a tear in the wall. Unlit
// and bright (it blooms), cut to the crack's shape by _MaskTex, brightest down its middle, with slow bands drifting up
// it and a stutter now and then, so it moves like light seen through a gap. Drawn both sides. In Resources so builds
// include it.
Shader "Backrooms/SeamGlow"
{
    Properties
    {
        _MaskTex ("Crack shape", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 0.8, 0.5, 1)
        _Glow ("Brightness", Float) = 2
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
            float4 _MaskTex_TexelSize;
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Glow, _Seed;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half m = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv).r;
                clip(m - 0.5h);
                // Depth into the crack: the mask blurred a little (nearer an edge, dimmer).
                float2 d = _MaskTex_TexelSize.xy * 3.0;
                half core = (SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv + float2(d.x, 0)).r + SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv - float2(d.x, 0)).r
                           + SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv + float2(0, d.y)).r + SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv - float2(0, d.y)).r) * 0.25h;
                float t = _Time.y + _Seed;
                float bands = 0.65 + 0.35 * sin(i.uv.y * 23.0 - t * 1.7 + sin(i.uv.y * 7.0 + t * 0.6) * 2.0);
                float flick = frac(sin(floor(t * 12.0) * 91.7 + _Seed) * 43758.5) > 0.93 ? 0.6 : 1.0;
                half3 col = _Color.rgb * _Glow * (0.45 + 0.9 * core * core) * bands * flick;
                col += core * core * core * 0.6 * _Glow;            // white-hot down the middle
                return half4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; return o; }
            half frag(Varyings i) : SV_Target { clip(SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv).r - 0.5h); return 0; }
            ENDHLSL
        }
    }
}
