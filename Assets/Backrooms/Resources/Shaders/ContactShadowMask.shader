// Contact shadows (F5 > LIGHT > Contact shadows), worked out once per pixel before the opaque pass by ContactShadows.cs:
// for the two fixtures that light each pixel most, a short march toward the light through the camera depth (8 steps,
// a few centimetres to 0.3 m) finds whether something stands right in front of it: a cup on a table, your hand on a wall,
// chair legs on the carpet, the small things shadow maps miss. Output: x, y the two shadows (1 = lit, already faded with
// distance and strength), z, w those lights' index in the light grid's list + 1 (0 = none). Backrooms/Lit looks them up.
Shader "Hidden/Backrooms/ContactShadowMask"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ContactShadowMask"
            ZTest Always ZWrite Off Cull Off Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Assets/Backrooms/Shaders/LightGridSample.hlsl"

            float4 _BRContactParams;   // x: strength, y: 1 / mask width, z: 1 / mask height

            float DeviceDepth(float2 uv) { return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).r; }
            bool IsSky(float d)
            {
            #if UNITY_REVERSED_Z
                return d <= 0.0;
            #else
                return d >= 1.0;
            #endif
            }
            float3 WorldAt(float2 uv, float d)
            {
            #if !UNITY_REVERSED_Z
                d = d * 2.0 - 1.0;
            #endif
                return ComputeWorldSpacePosition(uv, d, UNITY_MATRIX_I_VP);
            }
            float Noise(float2 pixel) { return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715)))); }

            half March(float3 P, float3 N, float3 toLight, float eyeDistance, float jitter)
            {
                float reach = lerp(0.12, 0.3, saturate(eyeDistance / 6.0));
                float3 start = P + N * (0.006 + 0.002 * eyeDistance);
                [loop] for (int i = 0; i < 8; i++)
                {
                    float3 q = start + toLight * (reach * (i + jitter) / 8.0);
                    float2 uv = ComputeNormalizedDeviceCoordinates(q, UNITY_MATRIX_VP);
                    if (any(uv < 0) || any(uv > 1)) break;
                    float d = DeviceDepth(uv);
                    if (IsSky(d)) continue;
                    float scene = LinearEyeDepth(d, _ZBufferParams);
                    float mine = -TransformWorldToView(q).z;
                    float behind = mine - scene;
                    // Something in front of the march point and close behind it: an occluder (far behind it is the background).
                    if (behind > 0.004 + 0.0015 * eyeDistance && behind < 0.25) return 0;
                }
                return 1;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float d = DeviceDepth(uv);
                if (IsSky(d) || _LGridParams.x < 0.5) return float4(1, 1, 0, 0);
                float3 P = WorldAt(uv, d);
                float3 eye = P - _WorldSpaceCameraPos;
                float eyeDistance = length(eye);
                if (eyeDistance > 12.0 || any(P.xz < _LGridWindow.xy) || any(P.xz > _LGridWindow.zw)) return float4(1, 1, 0, 0);
                // Normal from the nearer of each pair of neighbours (so silhouettes do not tear it).
                float2 px = float2(_BRContactParams.y, 0), py = float2(0, _BRContactParams.z);
                float3 r = WorldAt(uv + px, DeviceDepth(uv + px)) - P, l = P - WorldAt(uv - px, DeviceDepth(uv - px));
                float3 u = WorldAt(uv + py, DeviceDepth(uv + py)) - P, dn = P - WorldAt(uv - py, DeviceDepth(uv - py));
                float3 dx = dot(r, r) < dot(l, l) ? r : l, dy = dot(u, u) < dot(dn, dn) ? u : dn;
                float3 N = normalize(cross(dy, dx));
                if (dot(N, eye) > 0) N = -N;

                // The two fixtures that light this point most (walls already stop the others).
                uint2 tile = LGrid_TileRange(P);
                float bestA = 0, bestB = 0; uint idA = 0, idB = 0;
                [loop] for (uint i = 0; i < tile.y; i++)
                {
                    uint id = _LGridTileLights[tile.x + i];
                    LGridLight f = _LGridLights[id];
                    float3 toL = f.pos - P;
                    float d2 = dot(toL, toL);
                    if (d2 > 25.0 || dot(f.colorAll, f.colorAll) < 1e-10) continue;
                    float3 L = toL * rsqrt(max(d2, 1e-4));
                    float a = LGrid_DistAtten(max(d2, 0.01), f.range) * saturate(dot(N, L));
                    if (dot(f.dir, f.dir) > 0.5) a *= LGrid_Profile(dot(f.dir, -L), f.cosOuter, f.cosInner, f.padding.y);
                    float w = a * dot(f.colorAll, float3(0.2126, 0.7152, 0.0722));
                    if (w <= bestB) continue;
                    if (LGrid_LightVisible(P, N, f.pos) < 0.5) continue;
                    if (w > bestA) { bestB = bestA; idB = idA; bestA = w; idA = id + 1; }
                    else { bestB = w; idB = id + 1; }
                }
                float jitter = Noise(input.positionCS.xy);
                float fade = _BRContactParams.x * (1 - smoothstep(8.0, 12.0, eyeDistance));
                float sa = 1, sb = 1;
                if (idA > 0) sa = lerp(1, March(P, N, normalize(_LGridLights[idA - 1].pos - P), eyeDistance, jitter), fade);
                if (idB > 0) sb = lerp(1, March(P, N, normalize(_LGridLights[idB - 1].pos - P), eyeDistance, jitter), fade);
                return float4(sa, sb, idA, idB);
            }
            ENDHLSL
        }
    }
}
