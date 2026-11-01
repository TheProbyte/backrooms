// The Poolrooms' tiles: small square glazed ceramic tiles on every surface (larger ones on the dry decks), lit by the
// traced sun, the sky through the openings and the baked bounce (see PoolCommon.hlsl). Each tile is shaded as a slightly
// domed pillow with its edges rolled into a recessed joint, set a little off true, so the glaze catches light and breaks
// up reflections tile by tile (2026-10-31, after the owner's reference pictures: pale sage walls, cream decks, glossy).
// The glaze reflects what a ray traced through the cell map meets (PoolTrace: walls, columns, the water, the sky in the
// openings), blurred with distance. Below the waterline: wet, with the caustic map (PoolCausticLight). Above it: the
// wavy light that sunlit water throws up onto walls and ceilings (PoolWaterLight).
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
                half _Grout, _Smoothness, _Variation, _Bevel, _Dome, _Tilt, _JointShade, _ReflBlur, _WetDarken;
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
                float under = here.basin ? saturate((waterY - P.y) / 0.02) : 0.0;
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
                float3 T = normalize(i.tangentWS.xyz), B = cross(N0, T) * i.tangentWS.w;
                float2 grit = (PoolHash22(floor(i.uv * 260.0)) - 0.5) * 0.35 * near;
                float3 N = normalize(N0 + (T * tilt.x + B * tilt.y) * tileMask + (T * grit.x + B * grit.y) * (1.0 - tileMask));
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
                float3 ambient = PoolAmbientAt(P, N0) * ao * joint;
                float3 waterLight = under < 0.5 ? PoolWaterLight(P, N0) * joint : 0.0;

                // The glaze: what it mirrors (traced), strongest at grazing angles, and the sun's own highlight.
                float smooth = lerp(0.3, _Smoothness, tileMask);
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
                return half4(MixFog(color, i.fogFactor), 1);
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
