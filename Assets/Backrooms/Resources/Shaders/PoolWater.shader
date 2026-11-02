// Still water over the basins. Drawn after the opaque pass so it can see what is under it (refraction, depth for the
// colour of the water) and what it mirrors (screen-space reflection), falling back to a ray traced through the cell map
// where the screen holds nothing (PoolTrace: walls, columns, the sky in the openings). Ripples come from the simulation
// around the player (PoolRipples); a fine shimmer from the small waves that make the caustics (PoolWaveSlope).
// 2026-10-31, after the owner's reference pictures: a deeper emerald green.
// 2026-11-01, the water and effects pass (after Unreal's single-layer water and planar reflections with contact hardening):
// refraction traced through the surface against the depth (deep things shift, shallow edges barely), split a little by
// colour; the sun scattered inside the water column (sunlit water glows, shaded water stays dark); the mirror bent and
// blurred by how far away the mirrored thing is (sharp where things meet the water, softer far off); highlight AA.
Shader "Backrooms/PoolWater"
{
    Properties
    {
        _Absorb ("Absorption per metre", Vector) = (1.25, 0.36, 0.5, 0)
        _Scatter ("In-scattered colour", Color) = (0.02, 0.18, 0.13, 1)
        _Shimmer ("Small waves on the surface", Range(0, 1)) = 0.05
        _RefractScale ("Refraction (1 = water's own)", Range(0, 1.5)) = 1
        _Dispersion ("Colour split in refraction", Range(0, 0.1)) = 0.03
        _SunScatter ("Sun scattered in the water", Range(0, 2)) = 1
        _SunPhaseG ("Forward scattering in the water", Range(0, 0.9)) = 0.4
        _MirrorBend ("Ripples bend far reflections", Range(0, 2)) = 1
        _MirrorBlur ("Far reflections soften", Range(0, 4)) = 1
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
                half _RefractScale, _Dispersion, _SunScatter, _SunPhaseG, _MirrorBend, _MirrorBlur, _Roughness, _Micro, _Shimmer;
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
            TEXTURE2D(_PoolMirrorTex);           // rgb the mirrored room, a its eye depth in the mirror's view (mipmapped)
            float4 _PoolMirrorParams;            // x on, y the water's height, z mip levels, w 1 when a holds the depth
            float _PoolMirrorPass;               // 1 while the mirror itself is drawn (the water is left out of it)
            float DepthAt(float2 uv) { return LinearEyeDepth(SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, UnityStereoTransformScreenSpaceTex(uv), 0).r, _ZBufferParams); }
            float3 ColorAt(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, UnityStereoTransformScreenSpaceTex(uv), 0).rgb; }
            float RawAt(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, UnityStereoTransformScreenSpaceTex(uv), 0).r; }

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
                float3 c = PoolShadeHit(hit, R, 0.012);
                // (2026-11-01) Some traced hits shaded to NaN or infinity, which turned the water black and, through the
                // exposure, the whole picture white whenever the planar mirror was not in use: only finite light.
                return (any(isnan(c)) || any(isinf(c))) ? PoolAmbientAt(P, float3(0, 1, 0)) * POOL_WALL_ALBEDO : min(c, 32.0);
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
                // Highlight anti-aliasing (Tokuyoshi & Kaplanyan, as on the tiles): where the ripples turn the normal faster
                // than a pixel shows, the glint widens instead of sparkling; the wider lobe also softens the mirror.
                float3 dNx = ddx(N), dNy = ddy(N);
                float a2 = saturate(pow(_Roughness, 4.0) + min(2.0 * 0.25 * (dot(dNx, dNx) + dot(dNy, dNy)), 0.18));

                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float eye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float3 below, refl;
                float thick, contact = 0, contactNear = 0, viewDown = max(V.y, 0.08);
                PoolCell here = PoolLoad(PoolCellOf(P), PoolStorey(P.y));
                float3 ambient = PoolAmbientAt(P, float3(0, 1, 0));
                float3 R = reflect(-V, N);
                if (R.y < 0.02) R = normalize(float3(R.x, 0.02, R.z));
                bool planar = _PoolScreenTextures > 0.5 && _PoolMirrorParams.x > 0.5 && abs(P.y - _PoolMirrorParams.y) < 0.05;
                refl = planar ? 0 : Mirror(P, R);

                [branch] if (_PoolScreenTextures > 0.5)
                {
                    // Refraction traced through the surface (2026-11-01; before, a fixed shift of the picture whatever the
                    // depth): the view ray bent by the water's own index along the real normal, walked to what it meets by a
                    // few fixed-point steps against the depth (where it lands, how far that is, again), so the deep end's
                    // floor shifts and wobbles and the shallow edge's barely; never onto something above the water.
                    float3 I = -V;
                    float3 Tr = refract(I, N, 1.0 / 1.333);
                    if (dot(Tr, Tr) < 1e-4) Tr = I;
                    Tr = normalize(lerp(I, Tr, _RefractScale));
                    float3 Ps = ComputeWorldSpacePosition(suv, RawAt(suv), UNITY_MATRIX_I_VP);
                    float tl = min(length(Ps - P), 8.0);
                    float2 ruv = suv;
                    [unroll] for (int r = 0; r < 3; r++)
                    {
                        float2 u = ScreenUV(P + Tr * tl);
                        if (any(u < 0.001) || any(u > 0.999)) break;
                        float rawQ = RawAt(u);
                        if (LinearEyeDepth(rawQ, _ZBufferParams) < eye) break;       // something above the water there
                        float3 Pq = ComputeWorldSpacePosition(u, rawQ, UNITY_MATRIX_I_VP);
                        if (Pq.y > P.y + 0.02) break;                                 // (a wall above the waterline)
                        ruv = u;
                        tl = min(length(Pq - P), 8.0);
                    }
                    // A faint split by colour (water's index is a little higher for blue), only onto what is under water.
                    float2 uvR = lerp(suv, ruv, 1.0 - _Dispersion), uvB = lerp(suv, ruv, 1.0 + _Dispersion);
                    if (DepthAt(uvR) < eye) uvR = ruv;
                    if (any(uvB < 0.0) || any(uvB > 1.0) || DepthAt(uvB) < eye) uvB = ruv;
                    below = float3(ColorAt(uvR).r, ColorAt(ruv).g, ColorAt(uvB).b);
                    thick = tl;
                    viewDown = max(-Tr.y, 0.05);
                    // How close the surface is to what stands in it (Sea of Thieves finds where things cut its water by
                    // comparing depths): unrefracted, so it hugs the walls, rails and legs exactly.
                    float flat = max(DepthAt(suv) - eye, 0) * length(P - GetCameraPositionWS()) / max(eye, 1e-3);
                    contact = 1.0 - smoothstep(0.0, _ContactWidth, flat * max(V.y, 0.25));
                    // (2026-11-01: over a step's top just under the surface it drew a bright bar.) Only against walls, legs
                    // and rails: the thing behind must stand up (its normal, from the depth, near horizontal).
                    float rawS = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, UnityStereoTransformScreenSpaceTex(suv), 0).r;
                    float3 sw = ComputeWorldSpacePosition(suv, rawS, UNITY_MATRIX_I_VP);
                    float3 sn = cross(ddy(sw), ddx(sw));
                    float upness = abs(sn.y) / max(length(sn), 1e-6);
                    contact *= 1.0 - smoothstep(0.45, 0.75, upness);
                    contactNear = (1.0 - smoothstep(0.0, 0.45, flat * max(V.y, 0.25))) * (1.0 - smoothstep(0.45, 0.75, upness));
                    [branch] if (planar)
                    {
                        // Exact: the mirror (drawn mirrored in x: PoolMirror, read at 1 - u). Bent and blurred by how far away the
                        // mirrored thing is (2026-11-01; was a fixed share): a tilt of the surface turns the reflected ray, and the
                        // image of something d metres beyond the surface moves by d x that, seen from len + d away; the same for
                        // the lobe's width. So a leg or a wall's foot meets its reflection exactly and far walls ripple and soften.
                        float len = length(P - GetCameraPositionWS());
                        float tanHalf = 1.0 / max(UNITY_MATRIX_P[1][1], 1e-3);
                        float bendK = 0.25 + 0.75 * saturate(len / 8.0);              // without the depth: about as before
                        if (_PoolMirrorParams.w > 0.5)
                        {
                            float mEye = SAMPLE_TEXTURE2D_LOD(_PoolMirrorTex, sampler_LinearClamp, float2(1.0 - suv.x, suv.y), 0).a;
                            float dr = clamp(mEye * len / max(eye, 1e-3) - len, 0.0, 60.0);
                            bendK = dr / (dr + len);
                        }
                        float3 tilt = float3(slope.x, 0.0, slope.y);
                        float2 tiltS = float2(dot(tilt, UNITY_MATRIX_V[0].xyz), dot(tilt, UNITY_MATRIX_V[2].xyz));
                        float2 muv = suv + tiltS * (bendK / tanHalf) * _MirrorBend;
                        float lobe = sqrt(a2) + 0.012 * _MirrorBlur;                  // GGX alpha, and the micro ripples
                        float px = lobe * bendK / (2.0 * tanHalf) * _ScreenParams.y * 0.5;
                        float lod = _PoolMirrorParams.w > 0.5 ? clamp(log2(max(px, 1e-3)), 0.0, max(_PoolMirrorParams.z - 1.0, 0.0)) : 0.0;
                        refl = SAMPLE_TEXTURE2D_LOD(_PoolMirrorTex, sampler_LinearClamp, float2(1.0 - muv.x, muv.y), lod).rgb;
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
                float sun = PoolSunVisibility(P + float3(0, 0.01, 0));
                // The sun scattered in the water itself (2026-11-01, as Unreal's single-layer water): along the view's path
                // under the surface the refracted sunbeam is scattered toward the eye, both thinned by the water on the way
                // (the light going down to each depth, then up the view to the surface). Sunlit water glows green from
                // within, water in the shade does not; deep water more. Henyey-Greenstein, mostly forward.
                [branch] if (sun > 0.001 && _SunScatter > 0.0)
                {
                    float3 Ts = refract(-POOL_SUN, float3(0, 1, 0), 1.0 / 1.333);
                    float cosS = max(-Ts.y, 0.2);
                    float3 Vw = normalize(float3(V.x / 1.333, viewDown, V.z / 1.333));   // toward the eye, under the surface
                    float g = _SunPhaseG, cosP = dot(Ts, Vw);
                    float phase = (1.0 - g * g) / pow(max(1.0 + g * g - 2.0 * g * cosP, 1e-3), 1.5);    // 1 when isotropic
                    float slow = 1.0 + viewDown / cosS;                           // the sun's way down per metre of view path
                    float3 sunIn = _PoolSunColor.rgb * sun * 0.98 * phase * (1.0 - exp(-_Absorb.rgb * slow * thick)) / slow;
                    water += _Scatter.rgb * sunIn * _SunScatter * 0.12;
                }

                // The sun's glint off the surface.
                float3 H = normalize(V + POOL_SUN);
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
                // A faint film of dust and hair drifts to the edges (2026-11-01, the review): a matte, speckled sheen in the
                // last half metre of water by a wall (the thin water there in the depth), dulling the mirror.
                if (contactNear > 0.01)
                {
                    float sp = PoolValueNoise(P.xz * 9.0 + t * 0.02) * PoolValueNoise(P.xz * 23.0 - t * 0.03);
                    float filmA = contactNear * (0.35 + 0.65 * sp);
                    color = lerp(color, color * 0.92 + ambient * float3(0.05, 0.05, 0.04), filmA * 0.5);
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
                if (any(isnan(color)) || any(isinf(color))) color = ambient * 0.5;
                return half4(PoolFarFade(MixFog(color, i.fogFactor), P), 1);
            }
            ENDHLSL
        }
    }
}
