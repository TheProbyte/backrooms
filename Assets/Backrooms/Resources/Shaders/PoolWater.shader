// Still water over the basins. Drawn after the opaque pass so it can see what is under it (refraction, depth for the
// colour of the water) and what it mirrors (screen-space reflection), falling back to a ray traced through the cell map
// where the screen holds nothing (PoolTrace: walls, columns, the sky in the openings). Ripples come from the simulation
// around the player (PoolRipples); a fine shimmer from the small waves that make the caustics (PoolWaveSlope).
// 2026-10-31, after the owner's reference pictures: a deeper emerald green.
Shader "Backrooms/PoolWater"
{
    Properties
    {
        _Absorb ("Absorption per metre", Vector) = (2.6, 0.95, 1.35, 0)
        _Scatter ("In-scattered colour", Color) = (0.02, 0.18, 0.13, 1)
        _Shimmer ("Small waves on the surface", Range(0, 1)) = 0.05
        _Refraction ("Refraction", Range(0, 0.3)) = 0.15
        _Roughness ("Surface roughness", Range(0.01, 0.3)) = 0.05
        _Micro ("Stillness (micro waves)", Range(0, 0.05)) = 0.004
        _CrestColor ("Lit crests (sub-surface)", Color) = (0.16, 0.62, 0.5, 1)
        _CrestGain ("Crest height for full glow (m)", Float) = 0.035
        _ContactWidth ("Contact line width (m of water)", Float) = 0.03
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-50" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off ZWrite On ZTest LEqual Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Absorb;
                half4 _Scatter;
                half _Refraction, _Roughness, _Micro, _Shimmer;
                half4 _CrestColor;
                float _CrestGain, _ContactWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fogFactor : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // Explicit LOD: these are read inside loops.
            // The planar reflection (PoolMirror.cs): the room mirrored in this storey's water, at the pixel's own screen
            // position. Off (x = 0) when the Graphics option is off or the camera is not over the water.
            TEXTURE2D(_PoolMirrorTex);
            float4 _PoolMirrorParams;            // x on, y the water's height
            float _PoolMirrorPass;               // 1 while the mirror itself is drawn (the water is left out of it)
            float DepthAt(float2 uv) { return LinearEyeDepth(SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, UnityStereoTransformScreenSpaceTex(uv), 0).r, _ZBufferParams); }
            float3 ColorAt(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(uv), 0).rgb; }

            float2 ScreenUV(float3 pw)
            {
                float4 cs = TransformWorldToHClip(pw);
                float4 sp = ComputeScreenPos(cs);
                return sp.xy / max(sp.w, 1e-5);
            }

            // March the mirrored ray across the depth buffer; returns the colour it lands on and how much to trust it.
            float3 ScreenReflection(float3 P, float3 R, out float hit)
            {
                hit = 0;
                float step = 0.12;
                float3 pos = P;
                float3 prev = P;
                [loop] for (int k = 0; k < 28; k++)
                {
                    prev = pos;
                    pos += R * step; step *= 1.13;
                    float4 cs = TransformWorldToHClip(pos);
                    if (cs.w <= 0.05) return 0;
                    float2 uv = ScreenUV(pos);
                    if (any(uv < 0) || any(uv > 1)) return 0;
                    float scene = DepthAt(uv);
                    float ray = cs.w;
                    if (ray > scene + 0.01)
                    {
                        if (ray - scene > step * 1.5 + 0.25) return 0;     // went behind something: no hit
                        // Refine between the last two points.
                        float3 a = prev, b = pos;
                        [unroll] for (int r = 0; r < 5; r++)
                        {
                            float3 m = (a + b) * 0.5;
                            float2 muv = ScreenUV(m);
                            float ms = DepthAt(muv);
                            if (TransformWorldToHClip(m).w > ms) b = m; else a = m;
                        }
                        uv = ScreenUV(b);
                        float2 edge = min(uv, 1 - uv);
                        hit = saturate(min(edge.x, edge.y) * 10.0) * (1.0 - k / 28.0);
                        return ColorAt(uv);
                    }
                }
                return 0;
            }

            // What the water mirrors where the screen has nothing: a ray traced through the cell map.
            float3 Mirror(float3 P, float3 R)
            {
                PoolHit hit = PoolTrace(P + float3(0, 0.01, 0), R, 40.0);
                return PoolShadeHit(hit, R, 0.012);
            }

            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                if (_PoolMirrorPass > 0.5) discard;
                float3 P = i.positionWS;
                float3 V = GetWorldSpaceNormalizeViewDir(P);
                float t = _Time.y;

                // Surface: the player's ripples, and the faintest slow movement so it never looks frozen.
                float2 slope = PoolRippleSlope(P.xz);
                slope += _Micro * float2(sin(P.x * 5.1 + t * 0.9) + sin(P.z * 6.9 - t * 0.7 + P.x * 2.1),
                                         sin(P.z * 5.7 + t * 0.8) + sin(P.x * 6.3 + t * 0.6 - P.z * 1.8)) * 0.5;
                // The small waves that make the caustics, faded out where they would be finer than a pixel.
                float pix = length(fwidth(P.xz));
                slope += PoolWaveSlope(P.xz, _PoolWaveTime) * _Shimmer * saturate(1.0 - pix / 0.03);
                float3 N = normalize(float3(-slope.x, 1.0, -slope.y));
                // From below (2026-11-01, the realism plan 11): looking up through the surface the air beyond shows only
                // inside Snell's window (a cone of about 49 degrees overhead); outside it the surface mirrors the pool.
                [branch] if (GetCameraPositionWS().y < P.y - 0.002)
                {
                    float3 up = -V;                                            // the view ray, going up
                    float3 tr = refract(up, -N, 1.333);
                    float3 ambUp = PoolAmbientAt(P - float3(0, 0.05, 0), float3(0, -1, 0));
                    float3 murk = float3(0.07, 0.36, 0.29) * ambUp * 1.6;
                    float3 c = murk;
                    if (dot(tr, tr) > 0.01)
                    {
                        float cosI = saturate(dot(up, N)), cosT = saturate(tr.y);
                        float rs = (1.333 * cosI - cosT) / (1.333 * cosI + cosT), rp = (cosI - 1.333 * cosT) / (cosI + 1.333 * cosT);
                        float Fr = saturate(0.5 * (rs * rs + rp * rp));
                        PoolHit hit = PoolTrace(P + float3(0, 0.01, 0), normalize(tr), 30.0);
                        float3 air = PoolShadeHit(hit, normalize(tr), 0.02);
                        c = lerp(air * float3(0.8, 0.95, 0.9), murk, Fr);
                    }
                    return half4(c, 1);
                }
                float NdotV = saturate(dot(N, V));
                float F = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);

                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float eye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float3 below, refl;
                float thick, contact = 0;
                PoolCell here = PoolLoad(PoolCellOf(P), PoolStorey(P.y));
                float3 ambient = PoolAmbientAt(P, float3(0, 1, 0));
                float3 R = reflect(-V, N);
                if (R.y < 0.02) R = normalize(float3(R.x, 0.02, R.z));
                bool planar = _PoolScreenTextures > 0.5 && _PoolMirrorParams.x > 0.5 && abs(P.y - _PoolMirrorParams.y) < 0.05;
                refl = planar ? 0 : Mirror(P, R);

                [branch] if (_PoolScreenTextures > 0.5)
                {
                    float2 ruv = suv + slope * _Refraction;
                    float scene = DepthAt(ruv);
                    if (scene < eye) { ruv = suv; scene = DepthAt(ruv); }
                    below = ColorAt(ruv);
                    thick = max(scene - eye, 0) * length(P - GetCameraPositionWS()) / max(eye, 1e-3);
                    // How close the surface is to what stands in it (Sea of Thieves finds where things cut its water by
                    // comparing depths): unrefracted, so it hugs the walls, rails and legs exactly.
                    float flat = max(DepthAt(suv) - eye, 0) * length(P - GetCameraPositionWS()) / max(eye, 1e-3);
                    contact = 1.0 - smoothstep(0.0, _ContactWidth, flat * max(V.y, 0.25));
                    [branch] if (planar)
                    {
                        // Exact: the mirror, bent by the ripples (more the further the reflected thing; a fixed share here).
                        float2 muv = suv + slope * 0.045;
                        refl = SAMPLE_TEXTURE2D_LOD(_PoolMirrorTex, sampler_LinearClamp, muv, 0).rgb;
                    }
                    else
                    {
                        float hit;
                        float3 ssr = ScreenReflection(P, R, hit);
                        refl = lerp(refl, ssr, hit);
                    }
                }
                else
                {
                    // No screen copies in this view (a portal window): the tiled floor below, roughly.
                    thick = (POOL_WATER + POOL_BASIN) / max(V.y, 0.08);
                    below = float3(0.78, 0.8, 0.75) * ambient;
                }
                float3 absorb = exp(-thick * _Absorb.rgb);
                float3 water = below * absorb + _Scatter.rgb * ambient * (1.0 - absorb.g);

                // The sun's glint off the surface.
                float sun = PoolSunVisibility(P + float3(0, 0.01, 0));
                float3 H = normalize(V + POOL_SUN);
                float a2 = pow(_Roughness, 4.0);
                float NdotH = saturate(dot(N, H));
                float dd = NdotH * NdotH * (a2 - 1.0) + 1.0;
                float3 glint = _PoolSunColor.rgb * sun * saturate(dot(N, POOL_SUN)) * (a2 / (PI * dd * dd)) * F * 0.25;

                float3 color = lerp(water, refl, F) + glint;

                // Lit crests (2026-11-01, Sea of Thieves: Rare's SIGGRAPH 2018 talk; the owner asked what of its water could be
                // here): the sun passes through the thin water of a ripple's crest and comes out turquoise, most when looking
                // toward the sun (forward scattering) and low over the water, a little from anywhere; none in the shade.
                float crest = saturate(PoolRippleHeight(P.xz) / _CrestGain);
                [branch] if (crest > 0.001 && sun > 0.001)
                {
                    float2 toSun = normalize(POOL_SUN.xz), look = -V.xz;
                    float fwd = pow(saturate(dot(look / max(length(look), 1e-4), toSun)), 3.0) * saturate(1.0 - V.y * 0.8);
                    color += _CrestColor.rgb * _PoolSunColor.rgb * sun * crest * crest * (0.05 + 0.3 * fwd) * (1.0 - F);
                }

                // Foam (2026-10-31, remade: the owner found the first, a network of cell borders, "spiderweb like"). As game
                // water draws it (Crest, Sea of Thieves; Valve's flow maps for the motion): a pattern of clumps and bubbles
                // carried along by the water's own flow (two phases of the flow map blended, so it never stretches out),
                // shown through a threshold set by how much foam there is. Thick foam is a creamy solid; as it thins it breaks
                // into clumps, then a scatter of single bubbles that pop (they do not fade), then nothing. Under it the
                // churned water is aerated: lighter and milkier, with no pattern at all.
                float3 flowFoam = PoolFlowAt(P.xz);
                float foam = flowFoam.z;
                [branch] if (foam > 0.01)
                {
                    float3 foamCol = float3(0.9, 0.93, 0.91) * (ambient * 1.15 + _PoolSunColor.rgb * sun * POOL_SUN.y);
                    color = lerp(color, foamCol * 0.8, saturate(foam) * 0.16);
                    float ph = t * 0.6, p0 = frac(ph), p1 = frac(ph + 0.5);
                    float w0 = 1.0 - abs(p0 * 2.0 - 1.0);
                    float2 drift = flowFoam.xy * 1.6;
                    float pix = length(fwidth(P.xz));
                    float n = lerp(PoolFoamPattern(P.xz - drift * p1 + 3.7, pix), PoolFoamPattern(P.xz - drift * p0, pix), w0);
                    float cover = saturate(foam);
                    float thr = 1.02 - cover * 0.95, soft = 0.03 + pix * 3.0;
                    float a = max(smoothstep(thr - soft, thr + soft, n), saturate((foam - 0.95) * 2.0));
                    // Thick foam is not flat: the pattern shows in it as the shade of bubbles over bubbles.
                    color = lerp(color, foamCol * (0.8 + 0.25 * saturate(n)), a * 0.9);
                }
                // The contact line: where walls, rails and legs cut the surface, the water climbs them a little and holds a
                // thin, broken line of foam (as Sea of Thieves foams round whatever is in its water), lifted by the light.
                [branch] if (contact > 0.01)
                {
                    float pixC = length(fwidth(P.xz));
                    float brk = PoolFoamPattern(P.xz * 1.7 + t * 0.03, pixC);
                    float a = contact * saturate(0.35 + brk * 0.9);
                    float3 lineCol = float3(0.88, 0.92, 0.9) * (ambient * 1.1 + _PoolSunColor.rgb * sun * POOL_SUN.y);
                    color = lerp(color, lineCol, a * 0.35);
                }
                return half4(PoolFarFade(MixFog(color, i.fogFactor), P), 1);
            }
            ENDHLSL
        }
    }
}
