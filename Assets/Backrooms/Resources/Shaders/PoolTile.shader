// The Poolrooms' tiles: small square ceramic tiles with dark joints on every surface, lit by the traced sun, the sky
// through the openings and the baked bounce (see PoolCommon.hlsl). Below the waterline: wet, with caustics.
Shader "Backrooms/PoolTile"
{
    Properties
    {
        _TileColor ("Tile", Color) = (0.9, 0.91, 0.87, 1)
        _GroutColor ("Joint", Color) = (0.42, 0.45, 0.44, 1)
        _Grout ("Joint half width (tile units)", Range(0.0, 0.2)) = 0.03
        _Smoothness ("Glaze smoothness", Range(0, 1)) = 0.86
        _Variation ("Tile to tile variation", Range(0, 0.2)) = 0.05
        _Bevel ("Rounded tile edges", Range(0, 2)) = 0.7
        _WetDarken ("Underwater darkening", Range(0, 1)) = 0.12
        _Caustics ("Caustic strength", Range(0, 3)) = 1.0
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
                half4 _TileColor, _GroutColor;
                half _Grout, _Smoothness, _Variation, _Bevel, _WetDarken, _Caustics;
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
                // Tiles: the grid sits half a wall thickness off the cell lines, so whole tiles meet at every corner.
                float2 g = (i.uv - 0.075) / POOL_TILE;
                float2 id = floor(g);
                float2 f = frac(g) - 0.5;
                float2 d = 0.5 - abs(f);                               // to the tile's edge, tile units
                float2 fw = max(fwidth(g), 1e-4);
                float2 m = smoothstep(_Grout - fw, _Grout + fw, d);
                float tileMask = m.x * m.y;
                float far = saturate(max(fw.x, fw.y) * 2.5 - 0.5);      // joints thinner than a pixel: their average
                tileMask = lerp(tileMask, 1.0 - 2.0 * _Grout * 0.8, far);
                float2 h = PoolHash22(id + 17.0);
                half3 albedo = lerp(_GroutColor.rgb, _TileColor.rgb * (1.0 + (h.x - 0.5) * _Variation * 2.0), tileMask);

                // Normal: rounded tile edges into the joint, and each tile set very slightly off true.
                float2 e = saturate(1.0 - (d - _Grout) / 0.12);
                float2 tilt = sign(f) * e * e * _Bevel * (1.0 - far) + (h - 0.5) * 0.03;
                float3 N0 = normalize(i.normalWS);
                float3 T = normalize(i.tangentWS.xyz), B = cross(N0, T) * i.tangentWS.w;
                // Glaze: a gentle wave across each tile (a little different on each); grout: sandy grit, set back from the tiles.
                float2 wave = float2(sin(f.x * 6.2832 + h.y * 6.0), sin(f.y * 6.2832 + h.x * 6.0)) * 0.012 * (1.0 - far);
                float2 gp = i.uv * 260.0;
                float2 grit = (PoolHash22(floor(gp)) - 0.5) * 0.35 * (1.0 - far);
                tilt += wave * tileMask;
                float3 N = normalize(N0 + (T * tilt.x + B * tilt.y) * tileMask + (T * grit.x + B * grit.y) * (1.0 - tileMask));
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);

                // Below the waterline of a basin: wet glaze and caustics.
                int storey = PoolStorey(i.positionWS.y);
                PoolCell here = PoolLoad(PoolCellOf(i.positionWS + N0 * 0.05), storey);
                float depth = POOL_WATER - (i.positionWS.y - PoolBase(storey));
                float under = here.basin ? saturate(depth / 0.02) : 0.0;
                albedo *= 1.0 - _WetDarken * under;

                float sun = PoolSunVisibility(i.positionWS + N0 * 0.02);
                float NdotL = saturate(dot(N, POOL_SUN));
                float caustic = under > 0 ? lerp(1.0, PoolCaustics(i.positionWS.xz, _Time.y), under * _Caustics) : 1.0;
                float3 sunLight = _PoolSunColor.rgb * sun * NdotL * caustic;

                float ao = 1.0, aoDirect = 1.0;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor aof = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS));
                    ao = aof.indirectAmbientOcclusion; aoDirect = aof.directAmbientOcclusion;
                #endif
                float3 ambient = PoolAmbientAt(i.positionWS, N0) * ao;

                // Glaze: sharp sun highlight, and a sheen of the surroundings at grazing angles.
                float smooth = lerp(0.25, _Smoothness, tileMask);
                float rough = max(1.0 - smooth, 0.04), a2 = rough * rough * rough * rough;
                float3 H = normalize(V + POOL_SUN);
                float NdotH = saturate(dot(N, H)), NdotV = saturate(dot(N, V));
                float dd = NdotH * NdotH * (a2 - 1.0) + 1.0;
                float D = a2 / (PI * dd * dd);
                float F = 0.04 + 0.96 * pow(1.0 - saturate(dot(V, H)), 5.0);
                float3 spec = _PoolSunColor.rgb * sun * NdotL * D * F * 0.25 * caustic;
                float Fv = 0.04 + 0.96 * pow(1.0 - NdotV, 5.0);
                float3 sheen = ambient * Fv * smooth * 1.4;

                float3 color = albedo * (sunLight * aoDirect + ambient) + spec + sheen;
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
