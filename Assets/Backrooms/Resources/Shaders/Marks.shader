// Chalk lines and tape strips on the world's surfaces (Interaction/Items/Marks.cs, and the tape outline of noclip doors).
// The marks have no lighting of their own: each pixel takes the light of the surface right under it from the camera's
// opaque copy (its luminance, relative to a typical wall's), so a mark is as lit as its wall in every zone and under
// any lamp. Cameras without the opaque copy (reflection probes, the scene view) use a fixed middle grey instead.
//
// uv0: x across the mark (-1..1), y along it (metres from its start), z kind (0 chalk, 1 tape), w its length (metres).
// Vertex colour: the mark's colour and strength.
Shader "Backrooms/Marks"
{
    Properties
    {
        _Reference ("Luminance of a typical lit wall", Float) = 0.42
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-60" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Back
            Offset -1, -3
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            float _PoolScreenTextures;
            CBUFFER_START(UnityPerMaterial)
                float _Reference;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float4 uv : TEXCOORD0; half4 color : COLOR;
                float3 positionWS : TEXCOORD1; float4 screen : TEXCOORD2; half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.screen = ComputeScreenPos(o.positionCS);
                o.uv = i.uv; o.color = i.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash(float3 p) { p = frac(p * float3(443.897, 441.423, 437.195)); p += dot(p, p.yzx + 19.19); return frac((p.x + p.y) * p.z); }
            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y);
                float b = lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y);
                return lerp(a, b, f.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float across = i.uv.x, along = i.uv.y, kind = i.uv.z, len = max(i.uv.w, 1e-4);
                float a;
                if (kind < 0.5)
                {
                    // Chalk: a soft-edged line, broken by the grain of the wall (fine noise fixed in the world), thinning
                    // toward its rounded ends.
                    float halfW = 0.0055;
                    float endFade = saturate(min(along, len - along) / halfW + 0.35);
                    float edge = smoothstep(1.0, 0.45, abs(across));
                    float grain = Noise(i.positionWS * 900.0) * 0.6 + Noise(i.positionWS * 260.0) * 0.4;
                    a = edge * endFade * smoothstep(0.28, 0.62, grain + 0.18 * (1.0 - abs(across)));
                    a *= 0.92;
                }
                else
                {
                    // Tape: crisp edges, a ragged tear at each end, slightly see-through where thin.
                    float tear0 = 0.004 + 0.004 * Hash(float3(floor(across * 9.0), 1.0, len));
                    float tear1 = 0.004 + 0.004 * Hash(float3(floor(across * 9.0), 7.0, len));
                    float ends = step(tear0, along) * step(along, len - tear1);
                    float edge = smoothstep(1.0, 0.94, abs(across));
                    a = ends * edge * 0.96;
                }
                a *= i.color.a;
                clip(a - 0.01);

                // Light at this spot: the surface under the mark, from the opaque copy.
                float lum = 0.5 * _Reference / 0.42;
                [branch] if (_PoolScreenTextures > 0.5)
                {
                    float2 suv = i.screen.xy / i.screen.w;
                    float3 under = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(suv), 0).rgb;
                    lum = dot(under, float3(0.2126, 0.7152, 0.0722));
                }
                float light = min(lum / _Reference, 2.5);
                half3 col = i.color.rgb * light;
                if (kind > 0.5) col *= 0.92 + 0.08 * Noise(i.positionWS * 140.0);          // a little crinkle in the tape
                col = MixFog(col, i.fogFactor);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
