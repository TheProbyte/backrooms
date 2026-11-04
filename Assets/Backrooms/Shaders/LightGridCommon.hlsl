// Shared by the light grid compute shader and the Backrooms Lit shader.
// The grid is a 3D clipmap of fixture light around the player: 0.6 m texels, toroidal in x and z.
// Occupancy describes the layout (walls, openings, ceilings) per 1.2 m layout cell, toroidal too.
#ifndef BACKROOMS_LIGHT_GRID_COMMON
#define BACKROOMS_LIGHT_GRID_COMMON

#define LG_TEXEL 0.6
#define LG_CELL 1.2

// Occupancy bits (one uint per layout cell and storey)
#define LG_EDGE_OPEN 0u
#define LG_EDGE_WALL 1u
#define LG_EDGE_DOOR 2u
#define LG_EDGE_HEADER 3u
#define LG_LOADED (1u << 24)
#define LG_NOFLOOR (1u << 25)
#define LG_OPENABOVE (1u << 26)

uint LG_WestKind(uint o) { return o & 3u; }
uint LG_SouthKind(uint o) { return (o >> 2) & 3u; }
// Opening tops: 7 bits of tenths of a metre (6 low bits, then bit 27 / 28), up to 12.7 m.
float LG_WestTop(uint o) { return (((o >> 4) & 63u) | (((o >> 27) & 1u) << 6)) * 0.1; }
float LG_SouthTop(uint o) { return (((o >> 10) & 63u) | (((o >> 28) & 1u) << 6)) * 0.1; }
float LG_Ceiling(uint o) { return ((o >> 16) & 255u) * 0.1; }
bool LG_Loaded(uint o) { return (o & LG_LOADED) != 0u; }

// ---------------- Broken walls (Destruction.WallDamage) ----------------
// A wall edge with pieces knocked out of it is still a wall, flagged as holed. Lights that cast shadows (the real lights)
// pass it except at its two posts, and the broken panel's pieces stop them in the shadow map, so their light comes
// through the holes exactly. Everything else (the grid's lights, bounce) tests the hole table: per holed edge, which
// 0.15 m blocks of the panel (8 along, 18 up) are open right through.
#define LG_WEST_HOLED (1u << 29)
#define LG_SOUTH_HOLED (1u << 30)
#define LG_HOLE_POST 0.075

StructuredBuffer<uint> _LGridHoleTable;   // 16 uints a record: key (2), blocks open right through (5), blocks with any opening (5), open share x 65535, 3 spare
StructuredBuffer<uint> _LGridHoleCells;   // per occupancy cell (the same layout): record + 1 of its west wall (low 16 bits), of its south wall (high 16 bits)
uint _LGridHoleN;                          // occupancy cells per side (0: no broken walls bound)

bool LG_Holed(uint o, bool west) { return (o & (west ? LG_WEST_HOLED : LG_SOUTH_HOLED)) != 0u; }

// The record of a cell's broken west (vertical) or south wall, plus one; 0 if none. One read, no search: the shaders that
// walk lines of sight call this inside their loops, and the old FXC compiler is very slow with loops nested in loops.
uint LG_HoleRecord1(int2 cell, bool west, uint storey)
{
    uint n = max(_LGridHoleN, 1u);
    uint i = (storey * n + ((uint)cell.y & (n - 1u))) * n + ((uint)cell.x & (n - 1u));
    uint w = _LGridHoleN > 0u ? _LGridHoleCells[i] : 0u;
    return west ? (w & 0xFFFFu) : (w >> 16);
}

// Is a holed wall open at a point (along: metres along the edge from its start, height: above its storey's floor)? any:
// the block has any opening at all (lights with shadow maps: the broken pieces' shadows shape the rest); otherwise it
// must be open right through for the most part.
bool LG_HoleOpen(int2 cell, bool west, uint storey, float along, float height, bool any)
{
    uint r = LG_HoleRecord1(cell, west, storey);
    int col = (int)floor(along / 0.15), row = (int)floor(height / 0.15);
    bool inside = r != 0u && col >= 0 && col <= 7 && row >= 0 && row <= 17;
    uint bit = (uint)clamp(row * 8 + col, 0, 143);
    uint word = inside ? _LGridHoleTable[(max(r, 1u) - 1u) * 16u + (any ? 7u : 2u) + (bit >> 5)] : 0u;
    return ((word >> (bit & 31u)) & 1u) != 0u;
}

// How much of a box on a holed wall is open (0..1): along u0..u1 (metres from the edge's start), up v0..v1 (metres above
// its storey's floor). The blocks open right through, weighted by how much of each the box covers (columns exactly, rows
// by four samples), over the part of the box on this edge (the next edges are tested on their own); above the panel's
// blocks (2.7 m) is wall. any: 1 if a block in the box has any opening at all (lights with shadow maps: their shadows of
// the pieces shape the light there), else 0. A box the size of a light's footprint on the wall gives that light a soft
// edge (it is not a point); a box of one block rounds off the blocks' steps. No loop, for the reason above.
float LG_HoleOpenBox(int2 cell, bool west, uint storey, float u0, float u1, float v0, float v1, bool any)
{
    uint r = LG_HoleRecord1(cell, west, storey);
    float c0 = clamp(u0 / 0.15, 0.0, 8.0), c1 = clamp(u1 / 0.15, 0.0, 8.0);
    float b0 = v0 / 0.15, height = max((v1 - v0) / 0.15, 1e-4);
    if (r == 0u || c1 - c0 < 1e-4) return 0;
    uint base = (r - 1u) * 16u + (any ? 7u : 2u);
    int i0 = (int)floor(c0), i1 = clamp((int)ceil(c1) - 1, i0, 7);
    // Columns: the first and last by how much of them the box covers, the ones between in full.
    float wFirst = i0 == i1 ? c1 - c0 : (i0 + 1) - c0;
    float wLast = i0 == i1 ? 0.0 : c1 - i1;
    uint between = i1 - i0 > 1 ? ((1u << (uint)(i1 - i0 - 1)) - 1u) << (uint)(i0 + 1) : 0u;
    float sum = 0;
    [unroll] for (int s = 0; s < 4; s++)
    {
        int row = (int)floor(b0 + (s + 0.5) * 0.25 * height);
        if (row >= 0 && row <= 17)
        {
            uint bits = (_LGridHoleTable[base + ((uint)row >> 2)] >> (((uint)row & 3u) * 8u)) & 0xFFu;
            sum += ((bits >> (uint)i0) & 1u) * wFirst + ((bits >> (uint)i1) & 1u) * wLast + countbits(bits & between);
        }
    }
    float open = sum * 0.25 / (c1 - c0);
    return any ? (open > 0 ? 1.0 : 0.0) : saturate(open);
}

// How open a holed wall is as a whole (0..1).
float LG_HoleShare(int2 cell, bool west, uint storey)
{
    uint r = LG_HoleRecord1(cell, west, storey);
    uint v = r != 0u ? _LGridHoleTable[(max(r, 1u) - 1u) * 16u + 12u] : 0u;
    return v / 65535.0;
}

// Traced bounce: heights of a cell's two probes above its storey's floor (low, high), shared by the tracer and the shader.
float2 LG_ProbeHeights(uint o, float base)
{
    float ceiling = max(LG_Ceiling(o), 0.6);
    return base + float2(min(0.85, 0.35 * ceiling), max(ceiling - 0.5, min(1.6, 0.8 * ceiling)));
}

// Energy-normalised 2x2 Gauss quadrature over the visible fitting (horizontal X/Z).
// At distance its angular size is negligible, so one central sample avoids redundant work.
float3 LG_EmitterPoint(float3 centre, float2 halfSize, int sampleIndex, bool quad)
{
    float2 q = quad ? float2((sampleIndex & 1) ? 0.57735027 : -0.57735027,
                            (sampleIndex & 2) ? 0.57735027 : -0.57735027) : 0;
    return centre + float3(q.x * halfSize.x, 0, q.y * halfSize.y);
}

#endif
