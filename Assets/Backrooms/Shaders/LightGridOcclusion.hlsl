#ifndef BACKROOMS_LOCAL_OCCLUSION
#define BACKROOMS_LOCAL_OCCLUSION

struct LocalOccBox { float4 centre; float4 halfSize; float4 inverseRotation; };
StructuredBuffer<LocalOccBox> _LocalOccBoxes;
StructuredBuffer<uint2> _LocalOccTiles;
StructuredBuffer<uint> _LocalOccIndices;
float4 _LocalOccInfo; // tile metres, tiles per axis, box count, strength

float3 LocalOccRotate(float3 v, float4 q) { return v + 2 * cross(q.xyz, cross(q.xyz, v) + q.w * v); }
float2 LocalOccIntersection(float3 origin, float3 direction, LocalOccBox box)
{
    float3 p = LocalOccRotate(origin - box.centre.xyz, box.inverseRotation);
    float3 d = LocalOccRotate(direction, box.inverseRotation);
    float3 safeD = (step(0.0, d) * 2 - 1) * max(abs(d), 1e-6);
    float3 a = (-box.halfSize.xyz - p) / safeD, b = (box.halfSize.xyz - p) / safeD;
    float3 nearT = min(a, b), farT = max(a, b);
    return float2(max(nearT.x, max(nearT.y, nearT.z)), min(farT.x, min(farT.y, farT.z)));
}

uint2 LocalOccRange(float3 P)
{
    int n = (int)_LocalOccInfo.y;
    int2 tile = ((int2)floor(P.xz / _LocalOccInfo.x)) & (n - 1);
    return _LocalOccTiles[tile.y * n + tile.x];
}

// Four bounded hemisphere rays for indirect contact. It also sees offscreen furniture.
float LocalIndirectVisibility(float3 P, float3 N)
{
    if (_LocalOccInfo.z < 0.5 || _LocalOccInfo.w <= 0) return 1;
    uint2 range = LocalOccRange(P);
    if (range.y == 0) return 1;
    float3 tangent = normalize(cross(N, abs(N.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
    float3 bitangent = cross(N, tangent);
    float4 nearest = 1.15;
    [loop] for (uint i = 0; i < range.y; i++)
    {
        LocalOccBox box = _LocalOccBoxes[_LocalOccIndices[range.x + i]];
        // Cheap sphere rejection before four slab tests.
        if (dot(P - box.centre.xyz, P - box.centre.xyz) > (box.centre.w + 1.2) * (box.centre.w + 1.2)) continue;
        [unroll] for (int ray = 0; ray < 4; ray++)
        {
            float3 axis = ray < 2 ? tangent : bitangent;
            float3 dir = N * 0.65 + axis * ((ray & 1) ? 0.759934 : -0.759934);
            float2 hit = LocalOccIntersection(P + N * 0.035, dir, box);
            if (hit.y > max(hit.x, 0.08) && hit.x < nearest[ray]) nearest[ray] = max(hit.x, 0.08);
        }
    }
    float4 blocked = 1 - smoothstep(0.08, 1.15, nearest);
    return 1 - _LocalOccInfo.w * dot(blocked, 0.25);
}

// Only local occlusion (2 m) for lights without furniture shadow maps. Architecture has its own ray test.
float LocalLightVisibility(float3 P, float3 N, float3 emitter)
{
    if (_LocalOccInfo.z < 0.5 || _LocalOccInfo.w <= 0) return 1;
    uint2 range = LocalOccRange(P);
    float3 delta = emitter - P; float distance = length(delta);
    if (distance < 0.1) return 1;
    float visibility = 1;
    [loop] for (uint i = 0; i < range.y; i++)
    {
        LocalOccBox box = _LocalOccBoxes[_LocalOccIndices[range.x + i]];
        if (dot(P - box.centre.xyz, P - box.centre.xyz) > (box.centre.w + 2.0) * (box.centre.w + 2.0)) continue;
        float2 hit = LocalOccIntersection(P + N * 0.035, delta / distance, box);
        if (hit.y > max(hit.x, 0.08) && hit.x < min(distance - 0.05, 2.0))
            visibility = min(visibility, smoothstep(1.2, 2.0, max(hit.x, 0)));
    }
    return lerp(1, visibility, _LocalOccInfo.w);
}
#endif
