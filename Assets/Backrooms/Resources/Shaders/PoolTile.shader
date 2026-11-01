// The Poolrooms' tiles: small square glazed ceramic tiles on every surface (larger ones on the dry decks), lit by the
// traced sun, the sky through the openings and the baked bounce (see PoolCommon.hlsl). Each tile is shaded as a slightly
// domed pillow with its edges rolled into a recessed joint, set a little off true, so the glaze catches light and breaks
// up reflections tile by tile (2026-10-31, after the owner's reference pictures: pale sage walls, cream decks, glossy).
// The glaze reflects what a ray traced through the cell map meets (PoolTrace: walls, columns, the water, the sky in the
// openings), blurred with distance. Below the waterline: wet, with the caustic map (PoolCausticLight). Above it: the
// wavy light that sunlit water throws up onto walls and ceilings (PoolWaterLight).
// 2026-11-01 (the realism plan 4: "it is too clean"): weathering, all from the cell map and the position, no textures:
// a crust of white scale above the waterline over a grime line at it, biofilm in the joints under water, dirt in the
// joints at the foot of walls, puddles under the roof openings (rain) with dried rims, streaks down the light wells and
// under the high windows, and the glaze worn matte in front of doorways.
Shader "Backrooms/PoolTile"
{
    Properties
    {
        _TileColor ("Tile (walls, ceilings, basins)", Color) = (0.78, 0.84, 0.77, 1)
        _DeckColor ("Tile (dry floors)", Color) = (0.86, 0.85, 0.78, 1)
        _GroutColor ("Joint", Color) = (0.70, 0.74, 0.69, 1)
        _Grout ("Joint half width (tile units)", Range(0.0, 0.2)) = 0.02
        _Smoothness ("Glaze smoothness", Range(0, 1)) = 0.9
        _Variation ("Tile to tile variation", Range(0, 0.2)) = 0.06
        _Bevel ("Rounded tile edges", Range(0, 2)) = 0.65
        _Dome ("Domed tile faces", Range(0, 0.3)) = 0.04
        _Tilt ("Tiles set off true", Range(0, 0.2)) = 0.06
        _JointShade ("Light in the joints", Range(0, 1)) = 0.8
        _ReflBlur ("Reflection blur per metre", Range(0, 0.3)) = 0.05
        _WetDarken ("Underwater darkening", Range(0, 1)) = 0.12
        _PatchBounce ("Bounce from the sun patches", Range(0, 2)) = 0.8
        _Chips ("Chipped tile corners (the coping)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ BACKROOMS_PORTAL_CLIP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"
            #include "Assets/Backrooms/Shaders/PoolCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TileColor, _DeckColor, _GroutColor;
                half _Grout, _Smoothness, _Variation, _Bevel, _Dome, _Tilt, _JointShade, _ReflBlur, _WetDarken, _PatchBounce, _Chips;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 tangentWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.tangentWS = float4(TransformObjectToWorldDir(i.tangentOS.xyz), i.tangentOS.w * GetOddNegativeScale());
                o.uv = i.uv;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                float3 P = i.positionWS;
                float3 N0 = normalize(i.normalWS);
                int storey = PoolStorey(P.y);
                PoolCell here = PoolLoad(PoolCellOf(P + N0 * 0.05), storey);
                float waterY = PoolBase(storey) + POOL_WATER;
                // In a pool: its cell is a basin, or the surface is below the deck (2026-11-01: a free pool's curved edge
                // runs through cells whose middle is dry, and those parts of its floor and walls were drawn dry, in
                // squares: the owner's "weird 90 degree angles" in those pools). Nothing dry lies below the deck here.
                bool inPool = here.basin || P.y < PoolBase(storey) - 0.025;
                float under = inPool ? saturate((waterY - P.y) / 0.02) : 0.0;
                bool deck = N0.y > 0.7 && under < 0.5;              // a dry floor: tiles twice the size, cream

                // Tiles: the grid sits half a wall thickness off the cell lines, so whole tiles meet at every corner.
                float size = deck ? POOL_TILE * 2.0 : POOL_TILE;
                float grout = deck ? _Grout * 0.5 : _Grout;
                float2 g = (i.uv - 0.075) / size;
                float2 id = floor(g);
                float2 f = frac(g) - 0.5;
                float2 d = 0.5 - abs(f);                               // to the tile's edge, tile units
                float2 fw = max(fwidth(g), 1e-4);
                float2 m = smoothstep(grout - fw, grout + fw, d);
                float tileMask = m.x * m.y;
                float far = saturate(max(fw.x, fw.y) * 2.5 - 0.5);      // joints thinner than a pixel: their average
                tileMask = lerp(tileMask, 1.0 - 2.0 * grout * 0.8, far);
                float2 h = PoolHash22(id + (deck ? 91.0 : 17.0));
                float3 tint = (deck ? _DeckColor.rgb : _TileColor.rgb) * (1.0 + (h.x - 0.5) * _Variation * 2.0);
                tint.g *= 1.0 + (h.y - 0.5) * _Variation * 0.6;          // a touch of hue from tile to tile
                half3 albedo = lerp(_GroutColor.rgb, tint, tileMask);

                // Shape: a slightly domed face, the edges rolled down into the joint, each tile set a little off true.
                float2 e = saturate(1.0 - (d - grout) / 0.14);
                float near = 1.0 - far;
                float2 tilt = (sign(f) * e * e * _Bevel + f * _Dome * 2.0 + (h - 0.5) * _Tilt) * near;
                float2 wave = float2(sin(f.x * 6.2832 + h.y * 6.0), sin(f.y * 6.2832 + h.x * 6.0)) * 0.01 * near;
                tilt += wave;
                // Wear (2026-10-31): now and then a tile cracked across, or missing (the mortar bed showing).
                float2 h3 = PoolHash22(id + (deck ? 413.0 : 913.0));
                if (h3.x < 0.0007 && near > 0.5) { tint = float3(0.36, 0.36, 0.33); tileMask = 0.0; tilt = 0; }
                else if (h3.x < 0.004)
                {
                    float2 cd = normalize(float2(cos(h3.y * 6.28), sin(h3.y * 6.28)));
                    float crack = 1.0 - smoothstep(0.006, 0.02, abs(dot(f - (h3 - 0.5) * 0.3, cd)));
                    tint *= 1.0 - 0.45 * crack * tileMask * near;
                }
                // (2026-11-01, the review) Repairs: now and then a ragged patch of a few tiles replaced with a near match,
                // slightly whiter or greener, their joints cleaner; and on the coping, chipped tile corners showing the pale
                // body under the glaze.
                float2 blk = floor(id / 5.0);
                float2 rh = PoolHash22(blk + (deck ? 3.7 : 9.1));
                if (rh.x < 0.05)
                {
                    float2 rc = (id - blk * 5.0) - 2.0 - (rh - 0.5) * 2.0;
                    if (dot(rc, rc) < 2.2 + 1.5 * PoolHash22(id + 5.5).x)
                        tint *= lerp(float3(1.06, 1.04, 1.0), float3(0.97, 1.05, 1.0), step(0.5, rh.y));
                }
                albedo = lerp(_GroutColor.rgb, tint, tileMask);
                if (_Chips > 0.0 && near > 0.3)
                {
                    float2 ch = PoolHash22(id + 77.0);
                    float2 corner = sign(f) * 0.5 - f;
                    float cd = length(corner);
                    if (ch.x < _Chips && cd < 0.12 + 0.1 * ch.y && dot(sign(f), float2(ch.y > 0.5 ? 1 : -1, ch.x * 4.0 > _Chips * 2.0 ? 1 : -1)) > 1.5)
                    { albedo = float3(0.82, 0.8, 0.74); tileMask = 0.4; }
                }
                // Pool floors: dark lane lines (a tile wide, every 2.4 m) and a round drain in some cells.
                if (under > 0.5 && N0.y > 0.7)
                {
                    if (here.lanes != 0u)
                    {
                        float tz = floor(((here.lanes == 1u ? P.z : P.x) - 0.075) / POOL_TILE);
                        if (fmod(tz + 4096.0, 16.0) == 8.0) albedo = lerp(_GroutColor.rgb * 0.6, float3(0.1, 0.24, 0.27), tileMask);
                    }
                    if (here.drain)
                    {
                        float2 dc = P.xz - (PoolCellOf(P) + 0.5) * POOL_C;
                        float dr = length(dc);
                        if (dr < 0.13)
                        {
                            float slots = step(0.5, frac(dc.x * 40.0));
                            albedo = dr > 0.11 ? float3(0.55, 0.56, 0.55) : lerp(float3(0.06, 0.07, 0.07), float3(0.3, 0.31, 0.3), slots * step(dr, 0.1));
                            tileMask = dr > 0.11 ? 0.8 : 0.2;
                        }
                    }
                }
                // ---------------- Weathering ----------------
                float baseY = PoolBase(storey);
                bool wall = abs(N0.y) < 0.5;
                float2 wallUV = float2(dot(P.xz, float2(N0.z, -N0.x)), P.y);    // along the wall, up
                float glaze = 1.0;                                             // how much of the gloss is left
                float puddle = 0.0;
                float2 ripple = 0;                                              // drops landing in a puddle (world x, z)
                if (wall && inPool)
                {
                    // At the waterline a grime line; over it a white crust of scale (evaporation leaves it), ragged, thicker
                    // in places, filling the joints.
                    float rel = P.y - waterY;
                    float gb = smoothstep(-0.05, -0.008, rel) * (1.0 - smoothstep(-0.004, 0.006, rel));
                    albedo *= lerp(float3(1, 1, 1), float3(0.78, 0.72, 0.56), gb * 0.8);
                    float n1 = PoolValueNoise(float2(wallUV.x * 2.5, 4.1)), n2 = PoolValueNoise(wallUV * float2(16.0, 34.0));
                    float top = 0.012 + 0.05 * n1 * n1;
                    float crust = smoothstep(-0.002, 0.004, rel) * (1.0 - smoothstep(top - 0.01, top + 0.002, rel + 0.012 * (n2 - 0.5)));
                    crust *= 0.5 + 0.5 * n2;
                    albedo = lerp(albedo, float3(0.87, 0.87, 0.82), crust * 0.85);
                    glaze *= 1.0 - crust * 0.8;
                    tileMask = lerp(tileMask, 1.0, crust * 0.7);
                }
                if (under > 0.5)
                {
                    // Biofilm in the joints under water, patchy.
                    float nb = PoolValueNoise(P.xz * 1.3 + P.y * 2.0 + 7.0);
                    albedo = lerp(albedo, albedo * float3(0.55, 0.68, 0.56), (1.0 - tileMask) * (0.35 + 0.65 * nb));
                }
                else if (wall)
                {
                    // Dirt in the joints at the foot of the walls.
                    float low = 1.0 - smoothstep(0.0, 0.4, P.y - baseY);
                    albedo = lerp(albedo, albedo * float3(0.68, 0.66, 0.58), (1.0 - tileMask) * low * 0.85);
                    // Streaks where rain runs: down the light wells over the openings, and under a high window from its sill.
                    float streak = 0.0;
                    if (P.y - baseY > here.ceiling - 0.02) streak = 0.75;
                    else
                    {
                        int2 cc = PoolCellOf(P + N0 * 0.05);
                        bool xw = abs(N0.x) > 0.5;
                        int2 other = cc + (xw ? int2(N0.x > 0 ? -1 : 1, 0) : int2(0, N0.z > 0 ? -1 : 1));
                        PoolCell oc = PoolLoad(other, storey);
                        uint edge = xw ? (N0.x > 0 ? here.west : oc.west) : (N0.z > 0 ? here.south : oc.south);
                        if (edge == 3u && here.ceiling > oc.ceiling)
                        {
                            float along = xw ? P.z - cc.y * POOL_C : P.x - cc.x * POOL_C;
                            float sill = min(here.ceiling, oc.ceiling) + POOL_WIN_ROOF;
                            float y = P.y - baseY;
                            streak = smoothstep(0.2, 0.35, along) * smoothstep(1.0, 0.85, along) * (1.0 - smoothstep(sill - 0.01, sill, y)) * smoothstep(sill - 2.6, sill - 0.3, y);
                        }
                    }
                    // White efflorescence: salts the damp brings out, a powdery bloom low on walls by the water and in
                    // patches where a leak once ran.
                    float bloomN = PoolValueNoise(wallUV * float2(3.0, 5.0) + 13.0);
                    float leak = step(PoolHash22(floor(wallUV.x / 1.2) + float2(17.0, 3.0)).x, 0.06) * smoothstep(0.0, 0.6, PoolValueNoise(float2(wallUV.x * 6.0, wallUV.y * 0.6)) - 0.3);
                    float bloom = saturate((1.0 - smoothstep(0.05, 0.5, P.y - baseY)) * here.fill * 2.0 * bloomN + leak * bloomN);
                    albedo = lerp(albedo, float3(0.9, 0.9, 0.86), bloom * 0.4);
                    glaze *= 1.0 - bloom * 0.6;
                    if (streak > 0.0)
                    {
                        float s = PoolValueNoise(float2(wallUV.x * 11.0, wallUV.y * 0.3 + 3.0)) * 0.7 + PoolValueNoise(float2(wallUV.x * 27.0, wallUV.y * 0.6)) * 0.3;
                        s = smoothstep(0.5, 0.85, s) * streak;
                        albedo = lerp(albedo, albedo * float3(0.74, 0.7, 0.6), s);
                        glaze *= 1.0 - s * 0.5;
                    }
                }
                if (deck)
                {
                    int2 cc = PoolCellOf(P);
                    float2 lc = P.xz - cc * POOL_C;
                    // Rain under a roof opening: a puddle with a damp margin and a dried, chalky rim. (2026-11-01, the owner
                    // liked the puddles and asked for better ones: they had taken the opening's own outline, a round one
                    // drew a round puddle. Now the opening only makes them likelier, over a metre round it, and their
                    // shape is the floor's: broad lobes, a ragged edge.)
                    if (here.sky != 0u)
                    {
                        float inHole = saturate(PoolHoleSDC(here, lc) / 0.9 + 0.55);
                        float pn = PoolValueNoise(P.xz * 0.55 + 31.7) * 0.55 + PoolValueNoise(P.xz * 1.7 + 4.2) * 0.3 + PoolValueNoise(P.xz * 5.1) * 0.15;
                        float v = pn * inHole + 0.12 * inHole;
                        puddle = smoothstep(0.55, 0.585, v);
                        float damp = smoothstep(0.47, 0.55, v) * (1.0 - puddle);
                        float rim = smoothstep(0.42, 0.47, v) * (1.0 - smoothstep(0.47, 0.5, v));
                        albedo = lerp(albedo, float3(0.85, 0.84, 0.79), rim * 0.4);
                        albedo *= 1.0 - damp * 0.16 - puddle * 0.22;
                    }
                    // A floor drain (PoolPlan: under the openings, and now and then elsewhere): the floor falls to it a
                    // little, so what water there is stands round it, more under the sky; a damp ring beyond.
                    if (here.drain)
                    {
                        float2 dc = lc - 0.5 * POOL_C;
                        float dr = length(dc);
                        float wob = (PoolValueNoise(P.xz * 1.6 + 3.3) - 0.5) * 0.32 + (PoolValueNoise(P.xz * 4.3 + 9.1) - 0.5) * 0.12;
                        float reach = here.sky != 0u ? 0.6 : 0.3;
                        float dv = 1.0 - smoothstep(reach - 0.05, reach + 0.01, dr + wob);
                        float dampD = (1.0 - dv) * (1.0 - smoothstep(reach, reach + 0.3, dr + wob));
                        dv *= smoothstep(0.1, 0.13, dr);
                        puddle = max(puddle, dv);
                        albedo *= 1.0 - dampD * 0.16 - dv * 0.2;
                        // The drain: a round stainless grate in a ring, its slots dark, a little grime round it.
                        if (dr < 0.115)
                        {
                            float slots = step(0.5, frac(dc.x * 36.0));
                            albedo = dr > 0.098 ? float3(0.58, 0.6, 0.6) : lerp(float3(0.05, 0.055, 0.055), float3(0.5, 0.52, 0.52), slots * step(dr, 0.09));
                            tileMask = dr > 0.098 ? 0.85 : 0.3;
                            glaze = 0.7;
                        }
                        else albedo *= 1.0 - 0.18 * (1.0 - smoothstep(0.115, 0.17, dr));
                    }
                    // Drops falling from the opening into the puddles: a ring spreading now and then from a point.
                    if (here.sky != 0u && puddle > 0.01)
                    {
                        float2 cellR = floor(P.xz / 0.45);
                        float2 hr = PoolHash22(cellR + 41.3);
                        float period = 2.5 + 6.0 * hr.x;
                        float ph = frac(_Time.y / period + hr.y);
                        float2 at = (cellR + 0.2 + 0.6 * PoolHash22(cellR + floor(_Time.y / period + hr.y) * 1.37)) * 0.45;
                        float2 to = P.xz - at;
                        float rr = length(to);
                        float rad = ph * 0.22;
                        float ring = exp(-pow((rr - rad) / 0.012, 2.0)) * (1.0 - ph) * (1.0 - ph);
                        ripple = (to / max(rr, 1e-4)) * ring * 0.35 * puddle;
                    }
                    // The glaze worn matte in front of doorways, where everyone walks.
                    PoolCell ce = PoolLoad(cc + int2(1, 0), storey), cn = PoolLoad(cc + int2(0, 1), storey);
                    float wear = 0.0;
                    float2 dw = float2(lc.x, POOL_C - lc.x), dz = float2(lc.y, POOL_C - lc.y);
                    float acrossZ = smoothstep(0.05, 0.35, lc.y) * smoothstep(1.15, 0.85, lc.y), acrossX = smoothstep(0.05, 0.35, lc.x) * smoothstep(1.15, 0.85, lc.x);
                    if (here.west == 2u) wear = max(wear, exp(-dw.x * dw.x / 0.9) * acrossZ);
                    if (ce.west == 2u) wear = max(wear, exp(-dw.y * dw.y / 0.9) * acrossZ);
                    if (here.south == 2u) wear = max(wear, exp(-dz.x * dz.x / 0.9) * acrossX);
                    if (cn.south == 2u) wear = max(wear, exp(-dz.y * dz.y / 0.9) * acrossX);
                    wear *= 0.55 + 0.45 * PoolValueNoise(P.xz * 3.0);
                    glaze *= 1.0 - 0.6 * wear;
                    albedo *= 1.0 - 0.05 * wear;
                }
                tilt *= 1.0 - puddle;
                float3 T = normalize(i.tangentWS.xyz), B = cross(N0, T) * i.tangentWS.w;
                float2 grit = (PoolHash22(floor(i.uv * 260.0)) - 0.5) * 0.35 * near * (1.0 - puddle);
                float3 N = normalize(N0 + (T * tilt.x + B * tilt.y) * tileMask + (T * grit.x + B * grit.y) * (1.0 - tileMask) + float3(ripple.x, 0, ripple.y));
                float3 V = GetWorldSpaceNormalizeViewDir(P);
                albedo *= 1.0 - _WetDarken * under;

                // Light: the sun (through the caustic map under water), the sky and bounce, the light off sunlit water.
                float sun = PoolSunVisibility(P + N0 * 0.02);
                float NdotL = saturate(dot(N, POOL_SUN));
                float3 caustic = under > 0.0 ? PoolCausticLight(P, waterY) : 1.0;
                float3 sunLight = _PoolSunColor.rgb * sun * NdotL * caustic;
                float ao = 1.0;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor aof = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    // The Poolrooms are lit mostly by soft bounce, which made full-strength SSAO draw hard dark bands at
                    // every floor-wall join and in corners: a gentle share of it on the bounce, none on the sun.
                    ao = lerp(1.0, aof.indirectAmbientOcclusion, 0.38);
                #endif
                float joint = lerp(_JointShade, 1.0, saturate(tileMask + far));       // the recessed joint gets less light
                float3 ambient = (PoolAmbientAt(P, N0) + PoolPatchBounce(P, N0) * _PatchBounce) * ao * joint;
                float3 waterLight = under < 0.5 ? PoolWaterLight(P, N0) * joint : 0.0;

                // The glaze: what it mirrors (traced), strongest at grazing angles, and the sun's own highlight.
                float smooth = lerp(0.3, _Smoothness, tileMask) * glaze;
                smooth = lerp(smooth, 0.97, puddle);                          // standing water: a mirror
                // Specular anti-aliasing (Tokuyoshi & Kaplanyan's geometric kind, as HDRP's): where the normal turns faster
                // than a pixel shows (tile edges and tilts far off), widen the highlight instead of letting it sparkle.
                if (_PoolSpecAA > 0.5)
                {
                    float3 dnx = ddx(N), dny = ddy(N);
                    float kernel = min(2.0 * 0.25 * (dot(dnx, dnx) + dot(dny, dny)), 0.18);
                    float r0 = 1.0 - smooth, a2s = r0 * r0 * r0 * r0;
                    smooth = 1.0 - sqrt(sqrt(min(a2s + kernel, 1.0)));
                }
                float NdotV = saturate(dot(N, V));
                float Fv = 0.04 + 0.96 * pow(1.0 - NdotV, 5.0);
                float dist = length(P - GetCameraPositionWS());
                float k = Fv * smooth * smooth * (1.0 - under) * saturate((48.0 - dist) / 12.0);
                float3 refl = 0;
                [branch] if (k > 0.003)
                {
                    float3 R = reflect(-V, N);
                    float rn = dot(R, N0);
                    if (rn < 0.03) R = normalize(R + N0 * (0.03 - rn));      // bumps at grazing angles: back out
                    PoolHit hit = PoolTrace(P + N0 * 0.012, R, 34.0);
                    refl = PoolShadeHit(hit, R, _ReflBlur * (1.15 - smooth));
                }
                float rough = max(1.0 - smooth, 0.05), a2 = rough * rough * rough * rough;
                float3 H = normalize(V + POOL_SUN);
                float NdotH = saturate(dot(N, H));
                float dd = NdotH * NdotH * (a2 - 1.0) + 1.0;
                float D = a2 / (PI * dd * dd);
                float F = 0.04 + 0.96 * pow(1.0 - saturate(dot(V, H)), 5.0);
                float3 spec = _PoolSunColor.rgb * sun * NdotL * min(D * F * 0.25, 40.0) * caustic * smooth;

                float3 color = albedo * (sunLight + ambient + waterLight) * (1.0 - k) + refl * k + spec;
                return half4(PoolFarFade(MixFog(color, i.fogFactor), P), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _PortalClipPlane;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); return o; }
            half frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ BACKROOMS_PORTAL_CLIP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _PortalClipPlane;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings vert(Attributes i) { Varyings o; o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target
            {
                #if defined(BACKROOMS_PORTAL_CLIP)
                    clip(dot(float4(i.positionWS, 1), _PortalClipPlane));
                #endif
                return half4(normalize(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
