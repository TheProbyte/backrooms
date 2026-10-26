// Inside of a crawl hole's duct (see CrawlHole). The duct overlaps the room behind the wall, so it is only drawn where
// the hole's stencil mask marks the screen (seen through the hole), or everywhere while the player is inside it.
// Lit by the two openings only: light falls off with depth from each end, so the middle of the crawl is dim. The
// room behind's lights never reach it.
Shader "Backrooms/CrawlDuct"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _UVScale ("UV per metre", Vector) = (0.833, 0.4, 0, 0)
        _EntryLight ("Light at the entrance", Color) = (0.5, 0.47, 0.4, 1)
        _ExitLight ("Light at the far end", Color) = (0.5, 0.47, 0.4, 1)
        _Ambient ("Ambient", Float) = 0.02
        _Falloff ("Falloff depth (m)", Float) = 0.55
        _DuctLength ("Length (m)", Float) = 2.2
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil comparison", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-400" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        float4 _PortalClipPlane;
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor; float4 _UVScale; half4 _EntryLight, _ExitLight;
            half _Ambient, _Falloff, _DuctLength, _StencilComp;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back ZWrite On ZTest LEqual
            ZClip Off
            Stencil { Ref 64 ReadMask 64 Comp [_StencilComp] }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionOS : TEXCOORD1; float3 normalOS : TEXCOORD2; float3 positionWS : TEXCOORD3; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = i.uv; o.positionOS = i.positionOS.xyz; o.normalOS = i.normalOS;
                return o;
            }
            half Mouth(float depth) { float k = 1.0 + max(depth, 0.0) / _Falloff; return 1.0 / (k * k); }
            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                float z = i.positionOS.z;                         // metres into the duct from the entrance
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv * _BaseMap_ST.xy + _BaseMap_ST.zw).rgb * _BaseColor.rgb;
                // Light enters from both openings; the floor catches a little more of it than walls and ceiling.
                half facing = lerp(0.8, 1.15, saturate(i.normalOS.y * 0.5 + 0.5));
                half3 light = (_EntryLight.rgb * Mouth(z) + _ExitLight.rgb * Mouth(_DuctLength - z)) * facing + _Ambient;
                return half4(albedo * light, 1);
            }
            ENDHLSL
        }
        // Used only while the player is inside (the material is then in the opaque queue), so depth-based effects see the duct.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back ZClip Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
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
            ZWrite On Cull Back ZClip Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
