// A ring spreading over standing water where a drop landed (Generation/CeilingLeak.cs): a faint additive ring of the
// light caught on the wave's crest, a thinner darker trough inside it, fading out as it spreads (_Fade, per leak).
// uv -1..1 across the quad, which is scaled to the ring's size.
Shader "Backrooms/Ripple"
{
    Properties
    {
        _Fade ("Fade", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Ripple"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            Offset -1, -2
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Fade;
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
                float r = length(i.uv);
                float crest = exp(-pow((r - 0.86) / 0.06, 2.0));
                float inner = exp(-pow((r - 0.6) / 0.05, 2.0)) * 0.45;
                half k = (crest + inner) * _Fade * 0.09;
                return half4(k, k, k * 0.95, 1.0);
            }
            ENDHLSL
        }
    }
}
