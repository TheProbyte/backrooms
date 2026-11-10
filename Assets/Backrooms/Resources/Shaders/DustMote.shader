// Dust motes floating in a lamp's beam: tiny soft additive points (particles), shown only where the beam is. The
// particles fill the air round the lamp; the shader lights each by whether it is inside the spot's cone and how far
// from the bulb it is, so motes drift into the light and out of it. _ConePos/_ConeDir/_ConeCos/_Range are set per lamp.
Shader "Backrooms/DustMote"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.9, 0.72, 1)
        _ConePos ("Cone apex (world)", Vector) = (0, 0, 0, 0)
        _ConeDir ("Cone axis (world)", Vector) = (0, -1, 0, 0)
        _ConeCos ("Cone cos(half angle)", Float) = 0.5
        _Range ("Reach (m)", Float) = 1.6
        _Strength ("Strength", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Dust"
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
                float4 _ConePos, _ConeDir;
                float _ConeCos, _Range, _Strength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = i.uv; o.color = i.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 c = i.uv * 2.0 - 1.0;
                half dot2 = saturate(1.0 - dot(c, c));
                float3 d = i.positionWS - _ConePos.xyz;
                float dist = length(d);
                half inCone = smoothstep(_ConeCos, _ConeCos + 0.12, dot(d / max(dist, 1e-4), _ConeDir.xyz));
                half reach = saturate(1.0 - dist / _Range);
                return half4(_Color.rgb * (dot2 * dot2 * inCone * reach * reach * _Strength * i.color.a), 1.0);
            }
            ENDHLSL
        }
    }
}
