namespace Backrooms.Generation
{
    /// <summary>
    /// How much of the small stuff the world has (F5 > WORLD GEN > HOW OFTEN), read by the chunk builders and the depth
    /// plan on their worker threads. Copied from the world's <see cref="WorldGenSettings"/> whenever a world is made
    /// (WorldGenerator); 1 is the shipped amount for the multipliers.
    /// </summary>
    public static class DetailRates
    {
        /// <summary>Strength of the dirt on walls and round vents (0 = clean).</summary>
        public static volatile float Dirt = 1f;
        /// <summary>Chance multiplier for wallpaper and paint damage, chips, plates painted over or crooked.</summary>
        public static volatile float WallWear = 1f;
        /// <summary>Chance multiplier for damaged ceiling tiles (missing, stained, holed...), askew and cracked lights.</summary>
        public static volatile float CeilingWear = 1f;
        /// <summary>Chance multiplier for marks on floors: where furniture stood, rust rings, burns, worn doorways.</summary>
        public static volatile float FloorMarks = 1f;
        /// <summary>Share of rooms laid in carpet tiles (not a multiplier).</summary>
        public static volatile float CarpetTileRooms = 0.05f;
        /// <summary>Chance multiplier for sunken wings, pit rooms, sloping rooms, stairs to nowhere, mezzanines, cut corners and ceilings that come down.</summary>
        public static volatile float Depth = 1f;
        /// <summary>Multiplier on each biome's chance of a wooden door in a doorway.</summary>
        public static volatile float Doors = 0.5f;

        public static void From(WorldGenSettings s)
        {
            if (s == null) return;
            Dirt = s.dirt; WallWear = s.wallWear; CeilingWear = s.ceilingWear; FloorMarks = s.floorMarks;
            CarpetTileRooms = s.carpetTileRooms; Depth = s.depthAmount; Doors = s.doors;
        }
    }
}
