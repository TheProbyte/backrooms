// The shadow (Atmosphere/ShadowStalker.cs): a long, tall human shadow with nobody casting it, laid flat on a wall (or,
// when it charges, standing in the dark facing you). One quad: x -0.5..0.5 across, y 0..1 up (feet to the top), sized to
// the shadow. The figure is drawn as a distance field (head, neck, shoulders, a thin body, arms to the knee, legs), its
// edge soft and growing softer toward the head (a penumbra, as a real cast shadow has), wavering slowly.
//  - _Mode 0: the shadow, multiplied onto what is behind it (Blend DstColor Zero, like FloorWear): it takes the wall's
//    light and colour and darkens it; _Strength 0..1.
//  - _Mode 1: its eyes, added (Blend One One): two red points with a glow, _Glow (0 none).
// Unlit, no keywords: nothing to do with the lit shader's variants. In Resources so builds keep it.
Shader "Backrooms/ShadowFigure"
{
    Properties
    {
        _Strength ("Strength", Range(0, 1)) = 1
        _Glow ("Eye glow", Float) = 0
        _Aspect ("Width / height", Float) = 0.33
        _Seed ("Seed", Float) = 0
        _Mode ("0 shadow, 1 eyes", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+460" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off ZTest LEqual Cull Off
            Offset -1, -6
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Strength, _Glow, _Aspect, _Seed, _Mode, _SrcBlend, _DstBlend;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            float Capsule(float2 p, float2 a, float2 b, float r)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h) - r;
            }
            float Ellipse(float2 p, float2 c, float2 r) { float2 q = (p - c) / r; return (length(q) - 1.0) * min(r.x, r.y); }
            float SMin(float a, float b, float k) { float h = saturate(0.5 + 0.5 * (b - a) / k); return lerp(b, a, h) - k * h * (1.0 - h); }
            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), u.x), u.y);
            }

            // The figure, 1 tall (feet 0, crown 1), x in units of its height. Long arms, a narrow body, a small head.
            float Figure(float2 p)
            {
                float d = Ellipse(p, float2(0.0, 0.925), float2(0.046, 0.058));                          // head
                d = SMin(d, Capsule(p, float2(0, 0.85), float2(0, 0.89), 0.019), 0.02);                   // neck
                float torso = Capsule(p, float2(-0.066, 0.815), float2(0.066, 0.815), 0.03);              // shoulders
                torso = SMin(torso, Capsule(p, float2(0, 0.79), float2(0, 0.63), 0.062), 0.04);          // chest
                torso = SMin(torso, Capsule(p, float2(-0.022, 0.565), float2(0.022, 0.565), 0.047), 0.05); // hips
                d = SMin(d, torso, 0.025);
                for (int s = -1; s <= 1; s += 2)
                {
                    // An arm: shoulder, elbow, a long forearm, the hand a little out at the knee.
                    float arm = Capsule(p, float2(s * 0.082, 0.8), float2(s * 0.093, 0.585), 0.021);
                    arm = SMin(arm, Capsule(p, float2(s * 0.093, 0.585), float2(s * 0.104, 0.345), 0.017), 0.01);
                    d = SMin(d, arm, 0.035);                                                              // the armpit filled
                    float leg = Capsule(p, float2(s * 0.034, 0.57), float2(s * 0.042, 0.29), 0.03);
                    leg = SMin(leg, Capsule(p, float2(s * 0.042, 0.29), float2(s * 0.047, 0.01), 0.024), 0.01);
                    d = SMin(d, leg, 0.03);
                }
                return d;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y + _Seed * 17.0;
                float2 p = float2((i.uv.x - 0.5) * _Aspect, i.uv.y);
                // A slow waver, more toward the top (as a flame-lit shadow moves).
                p.x += (sin(p.y * 7.0 + t * 1.1) * 0.006 + (Noise(float2(p.y * 9.0, t * 0.7)) - 0.5) * 0.012) * p.y;
                if (_Mode > 0.5)
                {
                    float k = 0.0;
                    for (int e = -1; e <= 1; e += 2)
                    {
                        float2 q = p - float2(e * 0.016, 0.932);
                        float r2 = dot(q * float2(1.0, 1.6), q * float2(1.0, 1.6));
                        k += exp(-r2 / (2.0 * 0.0035 * 0.0035)) * 1.0 + exp(-r2 / (2.0 * 0.012 * 0.012)) * 0.25;
                    }
                    float flick = 0.85 + 0.15 * Noise(float2(t * 3.0, _Seed));
                    return half4(float3(1.0, 0.08, 0.03) * k * _Glow * flick, 1.0);
                }
                float soft = lerp(0.01, 0.026, saturate(p.y));                                            // the penumbra
                float d = Figure(p) + (Noise(p * 60.0 + t * 0.3) - 0.5) * 0.006;
                float a = 1.0 - smoothstep(-soft, soft, d);
                a *= smoothstep(0.0, 0.02, i.uv.y) * (1.0 - smoothstep(0.985, 1.0, i.uv.y));
                float k = saturate(a * _Strength);
                return half4(lerp(1.0.xxx, float3(0.07, 0.065, 0.06), k), 1.0);
            }
            ENDHLSL
        }
    }
}
