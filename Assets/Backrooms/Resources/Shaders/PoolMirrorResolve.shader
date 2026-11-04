// The Poolrooms' planar mirror, made ready for the water (PoolMirror.cs, 2026-11-01): the mirror camera's picture in rgb
// and, in a, how far each mirrored thing is from that camera along its view (its eye depth), from the mirror's own depth.
// The mirror's projection is oblique (its near plane lies on the water), so the depth is undone by its inverse projection,
// not by _ZBufferParams. The result is mipmapped: the water reads a blurrier level the farther away what it mirrors is.
Shader "Hidden/Backrooms/PoolMirrorResolve"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off
        Pass
        {
            Name "Resolve"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X_FLOAT(_PoolMirrorDepthSrc);
            float4x4 _PoolMirrorInvP;          // the mirror camera's GPU projection, inverted

            float4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float3 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                float raw = SAMPLE_TEXTURE2D_X_LOD(_PoolMirrorDepthSrc, sampler_PointClamp, uv, 0).r;
                float4 ndc = float4(uv * 2.0 - 1.0, raw, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                    ndc.y = -ndc.y;
                #endif
                float4 v = mul(_PoolMirrorInvP, ndc);
                float eye = abs(v.w) > 1e-6 ? -v.z / v.w : 1000.0;
                if (!isfinite(eye) || eye <= 0.0) eye = 1000.0;
                if (!all(isfinite(c))) c = 0;
                return float4(max(c, 0.0), min(eye, 1000.0));
            }
            ENDHLSL
        }
    }
}
