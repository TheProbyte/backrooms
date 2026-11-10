// Sun shafts in the Poolrooms' humid air (2026-11-01, the realism plan 1; PoolAirPass.cs). Pool halls are humid, so
// every beam from a roof opening shows. Each view ray is marched through the traced sun (PoolSunVisibility: exact to the
// walls, openings, glazing, clouds and the player's body, no shadow maps) at a quarter of the screen's resolution,
// jittered per pixel and frame, then smoothed over frames (reprojected, clamped to the neighbourhood, as temporal
// volumetrics are in Unreal and Frostbite) and laid over the picture upsampled by depth. The air is denser in a thin
// layer over the floor and water (evaporation) and scatters mostly forward (Henyey-Greenstein), so the beams glow most
// when you look toward the sun. They end at the water's surface.
// Under the water (2026-11-01, the realism plan 11; _PoolUnderwater): the beams are the water's own, green and denser,
// up to the surface from below, and everything seen through the water is dimmed and tinted by the length of water
// between (the Murk pass multiplies, the composite adds the water's scattered light).
Shader "Hidden/Backrooms/PoolAir"
{
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"

    TEXTURE2D_X(_PoolAirDepth);         // the camera's depth (full resolution)
    TEXTURE2D(_PoolAirCur);             // this frame's march (quarter): rgb light, a eye distance (m)
    TEXTURE2D(_PoolAirHist);            // the smoothed result of the last frame
    float4x4 _PoolAirInvVP;             // clip -> world (GPU projection)
    float4x4 _PoolAirPrevVP;            // world -> clip, last frame
    float4 _PoolAirParams;              // x density (1/m), y extra near the floor (1/m), z reach (m), w steps
    float4 _PoolAirFrame;               // x frame index, y history weight, zw quarter texel size
    float3 _PoolAirCam;
    float4 _PoolUnderwater;             // x 1 under water, y the surface's height
    static const float3 POOL_MURK_ABSORB = float3(0.75, 0.22, 0.32);
    float3 MurkColor() { return float3(0.07, 0.36, 0.29) * PoolAmbientAt(_PoolAirCam, float3(0, 1, 0)) * 1.6; }
    // Water between the camera and a point along a view ray (m), under the water.
    float WaterPath(float3 dir, float dist)
    {
        float tSurf = dir.y > 1e-4 ? (_PoolUnderwater.y - _PoolAirCam.y) / dir.y : 1e5;
        return min(dist, tSurf);
    }

    float RawDepth(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_PoolAirDepth, sampler_PointClamp, uv, 0).r; }
    float3 WorldAt(float2 uv, float raw) { return ComputeWorldSpacePosition(uv, raw, _PoolAirInvVP); }

    // ---------------- 0 March ----------------

    float4 FragMarch(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float raw = RawDepth(uv);
        float3 cam = _PoolAirCam;
        #if UNITY_REVERSED_Z
            bool sky = raw <= 0.000001;
        #else
            bool sky = raw >= 0.999999;
        #endif
        float3 wp = WorldAt(uv, sky ? (UNITY_REVERSED_Z ? 0.0001 : 0.9999) : raw);
        float3 d = wp - cam;
        float dist = length(d);
        float3 dir = d / max(dist, 1e-4);
        float tMax = min(dist, _PoolAirParams.z);
        // The beams end at the water's surface (the depth texture is taken before the water is drawn).
        int storey = PoolStorey(cam.y);
        float wy = PoolBase(storey) + POOL_WATER;
        bool under = _PoolUnderwater.x > 0.5;
        if (under) tMax = min(tMax, WaterPath(dir, dist));
        else if (cam.y > wy && dir.y < -1e-4) tMax = min(tMax, (wy - cam.y) / dir.y);
        int n = (int)_PoolAirParams.w;
        float2 pix = input.positionCS.xy;
        float jit = frac(52.9829189 * frac(dot(pix + _PoolAirFrame.x * float2(5.588238, 5.588238), float2(0.06711056, 0.00583715))));
        float dt = tMax / n;
        float cosT = dot(dir, POOL_SUN);
        const float g = 0.55;
        float hg = (1.0 - g * g) / (4.0 * PI * pow(max(1.0 + g * g - 2.0 * g * cosT, 1e-4), 1.5));
        float phase = lerp(1.0 / (4.0 * PI), hg, 0.75);
        float acc = 0;
        [loop] for (int k = 0; k < n; k++)
        {
            float3 p = cam + dir * ((k + jit) * dt);
            float s = PoolSunVisibility(p);
            if (s <= 0.0) continue;
            float above = p.y - PoolBase(PoolStorey(p.y));
            float dens = under ? 0.03 * exp(-POOL_MURK_ABSORB.g * (k + jit) * dt) : _PoolAirParams.x + _PoolAirParams.y * exp(-max(above, 0.0) / 0.7);
            // Drifting density (2026-11-01, the review: an even veil): two slow noises, as volumetric fog is textured.
            float t = _Time.y;
            dens *= 0.35 + 1.3 * PoolValueNoise(p.xz * 0.55 + float2(t * 0.06, t * 0.02)) * PoolValueNoise(float2(p.x + p.z, p.y) * 0.8 - t * 0.04 + 7.0) * 2.0;
            if (under) s *= dot(PoolCausticLight(p, _PoolUnderwater.y), 1.0 / 3.0);
            acc += s * dens;
        }
        float3 light = _PoolSunColor.rgb * acc * dt * phase * (under ? float3(0.45, 1.0, 0.82) : 1.0);
        // Seen from above, the beams go on into the water: green, rippled by the caustics, dimming with depth.
        if (!under && cam.y > wy && dir.y < -1e-4 && dist > tMax + 0.02)
        {
            float t0 = tMax, t1 = min(dist, tMax + 3.0), wdt = (t1 - t0) / 6.0;
            float wacc = 0;
            [loop] for (int m = 0; m < 6; m++)
            {
                float3 q = cam + dir * (t0 + (m + jit) * wdt);
                float sq = PoolSunVisibility(q);
                if (sq <= 0.0) continue;
                wacc += sq * dot(PoolCausticLight(q, wy), 1.0 / 3.0) * exp(-0.6 * (wy - q.y));
            }
            light += _PoolSunColor.rgb * float3(0.35, 1.0, 0.8) * wacc * wdt * 0.02 * phase;
        }
        return float4(light, sky ? 1000.0 : dist);
    }

    // ---------------- 1 Temporal ----------------

    float4 FragTemporal(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float4 cur = SAMPLE_TEXTURE2D_LOD(_PoolAirCur, sampler_PointClamp, uv, 0);
        // The neighbourhood's range: history outside it is stale (ghosting).
        float3 lo = cur.rgb, hi = cur.rgb;
        [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int i = -1; i <= 1; i++)
        {
            float3 c = SAMPLE_TEXTURE2D_LOD(_PoolAirCur, sampler_PointClamp, uv + float2(i, j) * _PoolAirFrame.zw, 0).rgb;
            lo = min(lo, c); hi = max(hi, c);
        }
        // Where this pixel's point was on screen last frame.
        float3 cam = _PoolAirCam;
        float3 dir = normalize(WorldAt(uv, 0.5) - cam);
        // Over the water the haze ends at its surface: reproject by that, not by the pool floor under it (2026-11-01: the
        // haze over the water smeared as you walked).
        float wy = PoolBase(PoolStorey(cam.y)) + POOL_WATER;
        float tw = (cam.y > wy && dir.y < -1e-4) ? (wy - cam.y) / dir.y : 1e5;
        float3 wp = cam + dir * min(min(cur.a, tw), 60.0);
        float4 pc = mul(_PoolAirPrevVP, float4(wp, 1));
        float2 puv = pc.xy / max(pc.w, 1e-4) * 0.5 + 0.5;
        #if UNITY_UV_STARTS_AT_TOP
            puv.y = 1.0 - puv.y;
        #endif
        float w = _PoolAirFrame.y;
        if (any(puv < 0) || any(puv > 1) || pc.w <= 0) w = 0;
        float3 hist = SAMPLE_TEXTURE2D_LOD(_PoolAirHist, sampler_LinearClamp, puv, 0).rgb;
        float3 spread = (hi - lo) * 0.25;
        hist = clamp(hist, lo - spread, hi + spread);
        return float4(lerp(cur.rgb, hist, w), cur.a);
    }

    // ---------------- 2 Composite ----------------

    float4 FragComposite(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float raw = RawDepth(uv);
        float3 wp = WorldAt(uv, raw);
        float dist = length(wp - _PoolAirCam);
        #if UNITY_REVERSED_Z
            if (raw <= 0.000001) dist = 1000.0;
        #else
            if (raw >= 0.999999) dist = 1000.0;
        #endif
        // Depth-aware upsampling: the four nearest quarter texels, each by how close its distance is to this pixel's.
        float2 tex = _PoolAirFrame.zw;
        float2 base = (floor(uv / tex - 0.5) + 0.5) * tex;
        float3 sum = 0; float wsum = 0;
        [unroll] for (int j = 0; j < 2; j++)
        [unroll] for (int i = 0; i < 2; i++)
        {
            float2 q = base + float2(i, j) * tex;
            float4 s = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_PointClamp, q, 0);
            float2 f = 1.0 - abs(uv - q) / tex;
            float w = max(f.x * f.y, 1e-3) / (1e-3 + abs(s.a - dist) / max(dist, 0.5));
            sum += s.rgb * w; wsum += w;
        }
        float3 c = sum / max(wsum, 1e-5);
        // Far off it fades with the rest into the fog.
        if (_PoolFar.y > 0 && _PoolUnderwater.x < 0.5) c *= 1.0 - smoothstep(_PoolFar.x, _PoolFar.y, dist);
        if (_PoolUnderwater.x > 0.5)
        {
            float3 dir = normalize(wp - _PoolAirCam);
            float3 T = exp(-POOL_MURK_ABSORB * WaterPath(dir, dist));
            c += MurkColor() * (1.0 - T);
        }
        return float4(c, 0);
    }

    // ---------------- 3 Murk (under the water: what is seen through it, dimmed and tinted) ----------------

    float4 FragMurk(Varyings input) : SV_Target
    {
        float2 uv = input.texcoord;
        float raw = RawDepth(uv);
        float3 wp = WorldAt(uv, raw);
        float dist = length(wp - _PoolAirCam);
        #if UNITY_REVERSED_Z
            if (raw <= 0.000001) dist = 1000.0;
        #else
            if (raw >= 0.999999) dist = 1000.0;
        #endif
        float3 dir = normalize(wp - _PoolAirCam);
        return float4(exp(-POOL_MURK_ABSORB * WaterPath(dir, dist)), 1);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass { Name "March" HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragMarch
            ENDHLSL }
        Pass { Name "Temporal" HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragTemporal
            ENDHLSL }
        Pass { Name "Composite" Blend One One HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragComposite
            ENDHLSL }
        Pass { Name "Murk" Blend DstColor Zero HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragMurk
            ENDHLSL }
    }
}
