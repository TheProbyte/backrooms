// Light spilling under a shut door: a thin additive glow on the floor that is brightest at the door line and fades
// over a few tens of centimetres. uv.x across the doorway (0..1), uv.y away from the door (0 at it, 1 at the far end).
// Colour and strength come from _Color (set per door by Interaction.Door from the room behind it).
Shader "Backrooms/LightSpill"
{
    Properties
    {
        _Color ("Colour (rgb x strength)", Color) = (1, 0.95, 0.82, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Spill"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
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
                half across = saturate(1.0 - pow(abs(i.uv.x * 2.0 - 1.0), 8.0));       // soft at the jambs
                half edgeLine = exp(-i.uv.y * 40.0) * 0.6;                                  // the bright line at the gap
                half fall = exp(-i.uv.y * 5.0) * (1.0 - i.uv.y);                        // the spill fading out
                return half4(_Color.rgb * (edgeLine + fall * 0.35) * across, 1.0);
            }
            ENDHLSL
        }
    }
}
