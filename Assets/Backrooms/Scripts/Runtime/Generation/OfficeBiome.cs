using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>Dimensions shared by every office-biome chunk (metres). Matches the milestone-1 room module.</summary>
    public static class OfficeBiome
    {
        public const float CellSize = 1.2f;          // one wall segment / two ceiling tiles
        public const int ChunkCells = 12;            // 14.4 m chunks
        public const float ChunkSize = CellSize * ChunkCells;
        public const float CeilingHeight = 2.7f;
        public const float MaxCeilingHeight = 8.1f;  // tallest hall (warehouse); posts and colliders always reach this
        public const float WallThickness = 0.15f;
        public const float DoorHeaderHeight = 2.1f;
        public const float HalfWallHeight = 1.15f;
        public const float HalfWallThickness = 0.12f;
        public const float BaseboardHeight = 0.1f;
        public const float BaseboardDepth = 0.014f;
        public const float CrawlGap = 0.42f;         // narrower than the player's 0.6 m capsule
        public const float TileGridOffset = 0.3f;    // ceiling runners sit at z = 0.3 + 0.6k
    }
}
