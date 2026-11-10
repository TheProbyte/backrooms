// A soft contact shadow under a small item lying on something (Interaction/Items/ContactShadow.cs): a quad on the
// surface that darkens it with a smooth falloff, multiplied onto whatever is there (so it takes that surface's light and
// colour). Small items cast no shadow-map shadows: in the fixtures' maps they were too small to be stable.
Shader "Backrooms/ContactShadow"
{
    Properties
    {
        _Strength ("Strength", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+460" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off ZTest LEqual Cull Off
            Offset -1, -4
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Strength;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv * 2.0 - 1.0; o.color = i.color;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float r = length(i.uv);
                float a = saturate(1.0 - r);
                a = a * a * (3.0 - 2.0 * a);                 // smooth falloff to the edge
                a *= _Strength * i.color.a;
                return half4(1.0 - a, 1.0 - a, 1.0 - a, 1.0);
            }
            ENDHLSL
        }
    }
}
