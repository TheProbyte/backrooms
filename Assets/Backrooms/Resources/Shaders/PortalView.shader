// The window at the far end of a crawl duct: the partner hole's picture (rendered by CrawlPortalSystem) sampled at this
// pixel's screen position, so the duct seems to open onto somewhere else. Unlit: the picture is already lit, and the
// player's camera grades it together with everything else. Like the duct, it only draws where its hole's stencil mask
// marks the screen, unless the player is inside the duct (_StencilComp = Always). Before a view has a picture (or when
// it cannot be rendered this frame) the window shows the light measured just outside the partner hole, so it reads as a
// dim view of that room rather than a dark hole.
Shader "Backrooms/PortalView"
{
    Properties
    {
        _PortalTex ("Portal", 2D) = "black" {}
        _Fade ("Fade", Float) = 1
        _PortalRect ("Screen rectangle", Vector) = (0, 0, 1, 1)
        _Fallback ("Shown before the first picture", Color) = (0.03, 0.027, 0.02, 1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil comparison", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        // Keep the window visible right up to the eye crossing the plane, including oblique approaches.
        ZClip Off
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            Stencil { Ref 64 ReadMask 64 Comp [_StencilComp] }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_PortalTex); SAMPLER(sampler_PortalTex);
            float _Fade;
            float4 _PortalRect;
            half4 _Fallback;
            TEXTURE2D(_DuctMouthLight);   // see CrawlPortalSystem / CrawlDuct: rows 2i+1 = light at this view's partner mouth
            float _DuctMouth;             // < 0: no sample, use _Fallback
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;
                uv = (uv - _PortalRect.xy) / _PortalRect.zw;
                half3 fallback = _Fallback.rgb;
                [branch] if (_Fade < 1 && _DuctMouth >= 0)
                {
                    // Average light over the six directions, on a typical wall/carpet albedo.
                    int row = (int)_DuctMouth * 2 + 1;
                    half3 sum = 0;
                    [unroll] for (int k = 0; k < 6; k++) sum += LOAD_TEXTURE2D(_DuctMouthLight, int2(k, row)).rgb;
                    fallback = max(fallback, sum * (0.45 / 6));
                }
                return half4(lerp(fallback, SAMPLE_TEXTURE2D(_PortalTex, sampler_PortalTex, uv).rgb, _Fade), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
