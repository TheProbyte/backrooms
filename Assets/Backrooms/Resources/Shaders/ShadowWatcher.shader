// The shadow (Atmosphere/ShadowStalker.cs, 2026-11-02): a tall, thin, faceless figure standing perfectly still in the
// dark, after the owner's "a scarier shadow, still tall and thin, maybe shimmering, not glistening but like heat waves,
// a slenderman style shadow". One quad, turned to the camera about the vertical (x -0.5..0.5 across, y 0..1 up, feet to
// a little above the head), sized to the figure.
//  - The figure is a distance field (a small, long head bowed a little to one side, a thin neck, narrow sloping
//    shoulders, a long narrow body, arms hanging past the knees with long fingers, long thin legs).
//  - Heat haze (the usual screen-space way: the picture behind is looked up at a place shifted by noise that rises over
//    time, from URP's opaque texture): its edge boils upward and the room just round it ripples, as air does over a hot
//    road. Inside it is near black, with a little of the warped room seeping through in rising streaks.
//  - _Watch 0..1 (how long you have been looking): the boiling grows and quickens, its edge frays.
// Alpha-blended in the transparent queue (the room round it is drawn by the scene itself, untouched). Unlit, no keywords.
// In Resources so builds keep it.
Shader "Backrooms/ShadowWatcher"
{
    Properties
    {
        _Strength ("Strength", Range(0, 1)) = 1
        _Watch ("Looked at (0..1)", Range(0, 1)) = 0
        _Aspect ("Width / height", Float) = 0.42
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Strength, _Watch, _Aspect, _Seed;
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
            // A capsule whose radius goes from ra at a to rb at b (a tapering limb).
            float Taper(float2 p, float2 a, float2 b, float ra, float rb)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h) - lerp(ra, rb, h);
            }
            float Ellipse(float2 p, float2 c, float2 r, float ang)
            {
                float2 q = p - c;
                float s = sin(ang), co = cos(ang);
                q = float2(co * q.x - s * q.y, s * q.x + co * q.y) / r;
                return (length(q) - 1.0) * min(r.x, r.y);
            }
            float SMin(float a, float b, float k) { float h = saturate(0.5 + 0.5 * (b - a) / k); return lerp(b, a, h) - k * h * (1.0 - h); }
            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + 1), u.x), u.y);
            }
            float Fbm(float2 p) { return Noise(p) * 0.55 + Noise(p * 2.07 + 13.1) * 0.3 + Noise(p * 4.13 + 7.7) * 0.15; }

            // The figure, 1 tall (feet 0, crown about 0.97), x in units of its height. A suited, Slender-like silhouette:
            // shoulders drawn up round a sunk neck, a small head bowed and tilted, a narrow jacket that pinches at the waist
            // and flares a little over the hips, arms far too long hanging below the knees with long fingers, thin legs a
            // little knock-kneed.
            float Figure(float2 p)
            {
                // Head: small, egg-shaped, bowed and tilted to one side, sunk between raised shoulders.
                float d = Ellipse(p, float2(0.018, 0.905), float2(0.03, 0.05), 0.24);
                d = SMin(d, Capsule(p, float2(0.006, 0.845), float2(0.012, 0.872), 0.009), 0.01);
                // Shoulders drawn up, peaking at the neck, the cap of each a little rounded; one higher than the other.
                float torso = SMin(Capsule(p, float2(-0.074, 0.835), float2(-0.012, 0.858), 0.017),
                                   Capsule(p, float2(0.012, 0.86), float2(0.071, 0.842), 0.017), 0.02);
                // The jacket: chest narrowing to a pinched waist, then a slight flare over the hips.
                torso = SMin(torso, Taper(p, float2(0.0, 0.84), float2(0.0, 0.68), 0.052, 0.03), 0.03);
                torso = SMin(torso, Taper(p, float2(0.0, 0.68), float2(0.0, 0.55), 0.03, 0.04), 0.025);
                d = SMin(d, torso, 0.012);
                for (int s = -1; s <= 1; s += 2)
                {
                    // Arms far too long, hanging below the knees, slightly bent, one a little further out; long fingers.
                    float reach = s > 0 ? 0.01 : 0.0;
                    float2 sh = float2(s * 0.074, 0.83), el = float2(s * (0.088 + reach), 0.585), wr = float2(s * (0.092 + reach * 1.4), 0.3);
                    float arm = Taper(p, sh, el, 0.013, 0.008);
                    arm = SMin(arm, Taper(p, el, wr, 0.008, 0.0055), 0.006);
                    float2 hand = wr + float2(s * 0.003, -0.032);
                    arm = SMin(arm, Taper(p, wr, hand, 0.0065, 0.0055), 0.004);
                    [unroll] for (int f = -1; f <= 1; f++)
                        arm = SMin(arm, Taper(p, hand, hand + float2(s * 0.005 + f * 0.0065, -0.1 + abs(f) * 0.016), 0.003, 0.001), 0.003);
                    d = SMin(d, arm, 0.01);
                    // Thin legs, the knees drawn a little in.
                    float leg = Taper(p, float2(s * 0.02, 0.56), float2(s * 0.014, 0.29), 0.017, 0.0095);
                    leg = SMin(leg, Taper(p, float2(s * 0.014, 0.29), float2(s * 0.026, 0.012), 0.0095, 0.007), 0.006);
                    d = SMin(d, leg, 0.01);
                }
                return d;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y + _Seed * 17.0;
                float w = saturate(_Watch);
                float2 p = float2((i.uv.x - 0.5) * _Aspect, i.uv.y);
                // Heat haze: noise rising over time (faster and stronger while looked at), more toward the top.
                float rise = t * (0.45 + 1.4 * w);
                float2 q = float2(p.x * 26.0, p.y * 9.0 - rise);
                float2 h = float2(Fbm(q + _Seed), Fbm(q * 1.13 + 31.7 + _Seed)) - 0.5;
                float boil = (0.7 + 0.5 * p.y) * (1.25 + 2.2 * w);
                // Its edge wavers and boils (fraying into fine tongues the longer you look).
                float edge = h.x * 0.016 * boil + (Noise(float2(p.x * 90.0, p.y * 40.0 - rise * 3.0)) - 0.5) * 0.006 * w;
                float d = Figure(p + float2(h.x, h.y * 0.3) * 0.006 * boil) + edge;
                float soft = lerp(0.004, 0.008, saturate(p.y));
                float body = 1.0 - smoothstep(-soft, soft, d);
                // The air round it ripples too (up to about 7 % of its height out), most just at its edge.
                float aura = exp(-max(d, 0.0) / (0.022 + 0.02 * w)) * (1.0 - body);

                // The room behind, looked up where the haze bends it.
                float2 suv = i.positionCS.xy / _ScaledScreenParams.xy;
                float2 bend = h * (0.009 + 0.012 * w) * (aura + body);
                float3 scene = SampleSceneColor(saturate(suv + bend));
                // Inside: near black, a little of the bent room seeping through in rising streaks.
                float streak = smoothstep(0.55, 0.95, Fbm(float2(p.x * 40.0, p.y * 6.0 - rise * 1.6) + 5.3));
                float3 dark = scene * (0.035 + 0.1 * streak) + float3(0.004, 0.0035, 0.003);
                float3 col = lerp(scene, dark, body);
                // Ground the feet: it fades out into the floor over the last few centimetres.
                float feet = smoothstep(0.0, 0.015, i.uv.y);
                float a = saturate(max(body * 0.97 * feet, aura * (0.55 + 0.4 * w))) * _Strength;
                a *= 1.0 - smoothstep(0.985, 1.0, i.uv.y);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
