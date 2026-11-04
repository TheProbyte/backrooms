// Lays the volumetric light (VolumetricLight.compute's integrated froxels) over the opaque picture: what the air in front
// of each pixel scatters toward the eye is added and the surface is dimmed by what the air takes (Blend One SrcAlpha:
// colour = in-scatter + colour x transmittance). The blit source is the camera depth.
Shader "Hidden/Backrooms/VolumetricComposite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Blend One SrcAlpha
        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE3D(_VolIntegrated);
            SAMPLER(sampler_VolIntegrated);
            float4 _VolDepth;      // x near, y far, z ln(far / near)
            float4 _VolDims;

            float4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.texcoord;
                float device = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).r;
                float depth = LinearEyeDepth(device, _ZBufferParams);
                // Slices hold what lies in front of their far end: the slice coordinate is half a slice back, dithered so
                // the slices never show as bands.
                float noise = frac(52.9829189 * frac(dot(i.positionCS.xy, float2(0.06711056, 0.00583715))));
                float s = log(max(depth, 1e-3) / _VolDepth.x) / _VolDepth.z * _VolDims.z;
                float w = (s - 0.5 + (noise - 0.5) * 0.9) / _VolDims.z;
                float4 v = SAMPLE_TEXTURE3D_LOD(_VolIntegrated, sampler_VolIntegrated, float3(uv, saturate(w)), 0);
                float k = saturate(depth / _VolDepth.x);                  // nearer than the first slice: almost no air
                float3 S = v.rgb * k;
                float T = lerp(1.0, v.a, k);
                if (!all(isfinite(float4(S, T)))) return float4(0, 0, 0, 1);
                return float4(max(S, 0.0), saturate(T));
            }
            ENDHLSL
        }
    }
}
