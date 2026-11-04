// Dust in the air (Docs/RealismPlan.md, section 7; Generation/AirDust.cs): tiny soft additive motes round the camera,
// each lit only by how far inside the light cone under one of the nearest lit ceiling fixtures it is, so the air under a
// light holds a faint drifting sparkle and the rest stays clear. The fixtures come in _AirDustLights (xyz: the lens,
// w: its power x strength), set every frame; walls are left to the depth test.
// 2026-11-01 (the water and effects pass): with volumetric light on, each mote instead takes the light of the froxel it
// floats in (VolumetricLight's volume: every near fixture with the walls, doorways, shut doors and furniture between and
// the air's forward scattering), so motes sparkle in a shaft through a doorway and not in its shadow, and glow most
// looking toward a light, as real dust does.
Shader "Backrooms/AirDust"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.95, 0.84, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "AirDust"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END
            float4 _AirDustLights[8];
            float _AirDustCount, _AirDustStrength;
            TEXTURE3D(_VolLight);              // rgb in-scatter per metre, a extinction per metre (VolumetricLight)
            float4x4 _VolDustVP;
            float4 _VolDustCam;                // xyz camera, w the y flip of the froxels' screen
            float4 _VolDustFwd;                // xyz forward, w 1 while the volume is drawn for this camera
            float4 _VolDepth;                  // x near, y far, z ln(far / near)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float3 positionWS : TEXCOORD1; half sun : TEXCOORD2; half3 vol : TEXCOORD3; };

            // The light the air holds at a point, toward the eye (the froxel's in-scatter over its scattering).
            half3 FroxelLight(float3 pw)
            {
                float4 c = mul(_VolDustVP, float4(pw, 1.0));
                if (c.w < 1e-3) return 0;
                float2 ndc = c.xy / c.w;
                float2 uv = float2(ndc.x * 0.5 + 0.5, ndc.y * _VolDustCam.w * 0.5 + 0.5);
                float depth = dot(pw - _VolDustCam.xyz, _VolDustFwd.xyz);
                float s = log(max(depth, 1e-3) / _VolDepth.x) / _VolDepth.z;
                if (any(uv < 0.0) || any(uv > 1.0) || s < 0.0 || s > 1.0) return 0;
                float4 v = SAMPLE_TEXTURE3D_LOD(_VolLight, sampler_LinearClamp, float3(uv, s), 0);
                // In-scatter = scattering x light x phase, extinction = scattering / 2.5 (VolumetricLight's haze).
                half3 l = v.a > 1e-6 ? v.rgb / v.a * 0.4 : 0;
                return all(isfinite(l)) ? max(l, 0) : 0;
            }

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = i.uv; o.color = i.color;
                // The Poolrooms (2026-11-01, the realism plan 1): no fixtures; a mote glints only inside a sun beam.
                o.sun = _PoolLitBlend > 0.5 ? PoolSunVisibility(o.positionWS) * 2.2 : 0.0;
                // Scaled to the old under-the-lamp motes where the light is strong (measured in World 3: the froxels' light
                // at the brightest tenth of the motes is about 3.5 times the cone's), and faded faster in dim air (a knee
                // under 0.3), so the motes still sparkle in light and not as an even snow through the whole room.
                half3 fl = _VolDustFwd.w > 0.5 ? FroxelLight(o.positionWS) : 0;
                half flum = dot(fl, half3(0.2126, 0.7152, 0.0722));
                o.vol = fl * (0.3 * _AirDustStrength * saturate(flum / 0.3));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 c = i.uv * 2.0 - 1.0;
                half dot2 = saturate(1.0 - dot(c, c));
                half3 lit = i.sun + i.vol;
                int lights = _VolDustFwd.w > 0.5 ? 0 : (int)_AirDustCount;      // the froxels have the fixtures already
                [loop] for (int k = 0; k < 8; k++)
                {
                    if (k >= lights) break;
                    float3 d = i.positionWS - _AirDustLights[k].xyz;
                    float dist = length(d);
                    half down = smoothstep(0.55, 0.85, -d.y / max(dist, 1e-4));      // inside the cone under the lens
                    half reach = saturate(1.0 - dist / 2.6);
                    lit += down * reach * reach * _AirDustLights[k].w;
                }
                return half4(_Color.rgb * (dot2 * dot2 * lit * i.color.a), 1.0);
            }
            ENDHLSL
        }
    }
}
