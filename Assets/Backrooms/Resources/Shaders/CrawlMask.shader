// Invisible quad over a crawl hole's opening: marks the screen pixels where the hole is seen (stencil bit 64), so the
// duct behind it is drawn there and nowhere else. Drawn after opaque geometry, so whatever stands in front of the hole
// still hides it.
Shader "Backrooms/CrawlMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-410" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            ColorMask 0 ZWrite Off ZTest LEqual Cull Back
            ZClip Off
            Stencil { Ref 64 WriteMask 64 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _PortalClipPlane;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); return o; }
            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                return 0;
            }
            ENDHLSL
        }
    }
}
