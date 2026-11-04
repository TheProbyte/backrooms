// Backrooms: a horizontal rectangular emitter (a ceiling panel): its form factor and the representative point for its
// specular. Kept small on purpose: the forward pass inlines it twice, and the HLSL compiler is slow with polygon-clipping
// branches and vertex arrays. No dependencies (PI aside), so tests can dispatch it on its own.
// Tested against a brute-force integral: Editor/Tests/LightingLookTests.cs.
#ifndef BACKROOMS_AREA_LIGHT_INCLUDED
#define BACKROOMS_AREA_LIGHT_INCLUDED

#ifndef PI
#define PI 3.14159265359
#endif

// Heitz & Hill's fitted edge integral (a cubic fit of theta / sin(theta) / (2 pi)), as a vector: its component along a
// normal is the edge's share of the polygon's form factor.
float3 BR_EdgeVector(float3 v1, float3 v2)
{
    float x = dot(v1, v2);
    float y = abs(x);
    float a = 0.8543985 + (0.4965155 + 0.0145206 * y) * y;
    float b = 3.4175940 + (4.1616724 + y) * y;
    float v = a / b;
    float thetaSinTheta = x > 0.0 ? v : 0.5 * rsqrt(max(1.0 - x * x, 1e-7)) - v;
    return cross(v1, v2) * thetaSinTheta;
}

// Form factor of a horizontal rectangle (centre c, half sizes h in x and z) seen from P with normal N: the share of a
// cosine-weighted hemisphere it covers (0..1); small and far, A cos(emitter) cos(receiver) / (pi d^2). Two-sided. Exact while
// the whole panel is in front of the surface; where the surface's plane cuts through the panel, the corners behind it are
// moved onto that plane (the part of the polygon lying in the plane adds nothing), which matches clipping without branches.
float BR_RectFormFactor(float3 P, float3 N, float3 c, float2 h)
{
    float3 q0 = c + float3(-h.x, 0, -h.y) - P, q1 = c + float3(h.x, 0, -h.y) - P;
    float3 q2 = c + float3(h.x, 0, h.y) - P, q3 = c + float3(-h.x, 0, h.y) - P;
    float4 heights = float4(dot(q0, N), dot(q1, N), dot(q2, N), dot(q3, N));
    if (all(heights <= 0)) return 0;                // wholly behind the surface
    q0 -= N * min(heights.x, 0); q1 -= N * min(heights.y, 0);
    q2 -= N * min(heights.z, 0); q3 -= N * min(heights.w, 0);
    float3 v0 = normalize(q0 + N * 1e-5), v1 = normalize(q1 + N * 1e-5), v2 = normalize(q2 + N * 1e-5), v3 = normalize(q3 + N * 1e-5);
    float3 F = BR_EdgeVector(v0, v1) + BR_EdgeVector(v1, v2) + BR_EdgeVector(v2, v3) + BR_EdgeVector(v3, v0);
    return abs(dot(F, N));                          // either winding
}

// The point of the rectangle a mirror reflection along R comes nearest to (Karis' representative point).
float3 BR_RectSpecularPoint(float3 P, float3 R, float3 c, float2 h)
{
    float t = (c.y - P.y) / max(R.y, 1e-4);
    float3 hit = P + R * max(t, 0);
    return float3(clamp(hit.x, c.x - h.x, c.x + h.x), c.y, clamp(hit.z, c.z - h.y, c.z + h.y));
}

#endif
