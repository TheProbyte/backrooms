// Running water in the Poolrooms (a spout's falling sheet, a cascade's film over steps): streaks pouring along the flow
// (the mesh's v grows downstream), thin and bright where the water is fast, see-through between; lit by the traced sun
// and the sky like everything there (per vertex), with a glint. Transparent, both sides.
Shader "Backrooms/PoolFall"
{
    Properties
    {
        _Speed ("Flow speed", Float) = 1.6
        _Opacity ("Opacity", Range(0, 1)) = 0.7
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-40" "IgnoreProjector" = "True" }
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
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Speed, _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float2 uv : TEXCOORD1; float3 light : TEXCOORD2; float3 normalWS : TEXCOORD3; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = i.uv;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                float sun = PoolSunVisibility(o.positionWS);
                o.light = PoolAmbientAt(o.positionWS, float3(0, 1, 0)) + _PoolSunColor.rgb * sun * 0.5;
                return o;
            }

            float N1(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(PoolHash22(i).x, PoolHash22(i + float2(1, 0)).x, f.x), lerp(PoolHash22(i + float2(0, 1)).x, PoolHash22(i + 1).x, f.x), f.y); }

            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                float t = _Time.y * _Speed;
                // Streaks: long along the flow, narrow across it, racing downstream.
                float s1 = N1(float2(i.uv.x * 22.0, i.uv.y * 2.5 - t * 2.2));
                float s2 = N1(float2(i.uv.x * 47.0 + 3.1, i.uv.y * 4.0 - t * 3.1));
                float streak = saturate(s1 * 0.65 + s2 * 0.5);
                float a = _Opacity * (0.35 + 0.65 * smoothstep(0.35, 0.85, streak));
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float fres = 0.3 + 0.7 * pow(1.0 - abs(dot(normalize(i.normalWS), V)), 3.0);
                float3 c = float3(0.82, 0.92, 0.9) * i.light * (0.75 + 0.6 * streak) * (0.7 + 0.5 * fres);
                return half4(PoolFarFade(c, i.positionWS), a);
            }
            ENDHLSL
        }
    }
}
