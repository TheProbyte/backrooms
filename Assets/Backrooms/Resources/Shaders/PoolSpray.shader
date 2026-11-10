// Droplets of spray in the Poolrooms (PoolSplashes.cs): soft round drops, stretched along their motion by the particle
// renderer, lit per vertex by the Poolrooms' sun (traced) and sky, catching a glint where the sun is on them.
Shader "Backrooms/PoolSpray"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float3 light : TEXCOORD1; float3 positionWS : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(p);
                o.color = i.color;
                o.positionWS = p;
                o.uv = i.uv;
                float sun = PoolSunVisibility(p);
                // Water is clear: the drop shows the light around it, and glints in the sun.
                o.light = PoolAmbientAt(p, float3(0, 1, 0)) * 0.9 + _PoolSunColor.rgb * sun * 0.55;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 q = (i.uv - 0.5) * 2.0;
                float a = saturate(1.0 - dot(q, q));
                a *= a * i.color.a;
                float3 c = float3(0.86, 0.94, 0.92) * i.light;
                return half4(PoolFarFade(c, i.positionWS), a);
            }
            ENDHLSL
        }
    }
}
