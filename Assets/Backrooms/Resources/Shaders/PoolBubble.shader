// Bubbles rising under the Poolrooms' water (PoolSplashes.Bubbles): each a little ring of light (a bright rim and a glint,
// clear in the middle), lit by the sky and the traced sun, greener and fainter the deeper it is. They are drawn after the
// water (which hides what is under it from the ordinary depth test), so each tests itself against the scene's depth
// before the water (the floor and walls under it) by hand.
Shader "Backrooms/PoolBubble"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-30" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float3 light : TEXCOORD1; float3 positionWS : TEXCOORD2; float under : TEXCOORD3; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.color = i.color; o.uv = i.uv;
                int storey = PoolStorey(p.y);
                float waterY = PoolBase(storey) + POOL_WATER;
                o.under = max(0.0, waterY - p.y);
                float sun = PoolSunVisibility(float3(p.x, waterY + 0.01, p.z));
                o.light = PoolAmbientAt(p, float3(0, 1, 0)) + _PoolSunColor.rgb * sun * 0.4;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_PoolScreenTextures < 0.5) discard;              // (no scene depth in this view)
                // Behind the floor or a wall: hidden.
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float mine = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                if (mine > scene + 0.01) discard;
                float2 q = (i.uv - 0.5) * 2.0;
                float r = length(q);
                if (r > 1.0) discard;
                float rim = smoothstep(0.55, 0.92, r) * (1.0 - smoothstep(0.92, 1.0, r));
                float glint = smoothstep(0.35, 0.0, length(q - float2(-0.35, 0.35)));
                float a = saturate(rim * 0.9 + glint * 0.8) * i.color.a;
                // Seen through the water: greener and fainter with depth.
                float3 tint = exp(-float3(2.6, 0.95, 1.35) * (i.under * 2.0 + 0.1));
                float3 c = float3(0.9, 0.97, 0.95) * i.light * tint * (0.8 + 0.6 * glint);
                a *= saturate(0.35 + tint.g);
                return half4(PoolFarFade(c, i.positionWS), a);
            }
            ENDHLSL
        }
    }
}
