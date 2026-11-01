// The Poolrooms' caustics (PoolCausticMap.cs): sunlight bent by the rippled water surface onto the basin floor, made the
// way Evan Wallace's WebGL water does it. A fine grid over one tile of the (tileable) wave field is drawn into the caustic
// map with each vertex moved to where its refracted sun ray meets a floor at the basin's depth; each fragment adds the
// ratio of the grid's area before and after (light spread out: dimmer, gathered together: bright lines). A flat surface
// gives exactly 1 everywhere. Additive, cleared to 0 each frame.
Shader "Hidden/Backrooms/PoolCausticSplat"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend One One
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"

            float _SplatDepth, _SplatGain;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 before : TEXCOORD0; float2 after : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float T = POOL_WAVE_TILE;
                float2 x = i.uv * T;
                float2 s = PoolWaveSlope(x, _PoolWaveTime) * _SplatGain;
                float3 N = normalize(float3(-s.x, 1.0, -s.y));
                float3 I = -POOL_SUN;
                float3 r = refract(I, N, 1.0 / 1.333);
                float3 r0 = refract(I, float3(0, 1, 0), 1.0 / 1.333);
                float2 off = r.xz * (_SplatDepth / max(-r.y, 0.05)) - r0.xz * (_SplatDepth / -r0.y);
                float2 after = (x + off) / T;
                o.before = i.uv;
                o.after = after;
                float2 c = after * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                    c.y = -c.y;
                #endif
                o.positionCS = float4(c, 0.5, 1.0);
                return o;
            }

            float frag(Varyings i) : SV_Target
            {
                float2 bx = ddx(i.before), by = ddy(i.before), ax = ddx(i.after), ay = ddy(i.after);
                float a0 = abs(bx.x * by.y - bx.y * by.x), a1 = abs(ax.x * ay.y - ax.y * ay.x);
                return min(a0 / max(a1, 1e-14), 10.0);
            }
            ENDHLSL
        }
    }
}
