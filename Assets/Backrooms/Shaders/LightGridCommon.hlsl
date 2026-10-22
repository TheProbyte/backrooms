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
float LG_WestTop(uint o) { return ((o >> 4) & 63u) * 0.1; }
float LG_SouthTop(uint o) { return ((o >> 10) & 63u) * 0.1; }
float LG_Ceiling(uint o) { return ((o >> 16) & 255u) * 0.1; }
bool LG_Loaded(uint o) { return (o & LG_LOADED) != 0u; }

#endif
