using System;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Every number the layout generator uses for one biome (authored as <see cref="BiomeDefinition"/> assets). Biomes are blended by lerping these values,
    /// so transitions between biomes are gradual (offices slowly opening up into a warehouse).
    /// All chances are 0..1 probabilities.
    /// </summary>
    [Serializable]
    public class BiomeRules
    {
        public string name = "Offices";
        public Color mapColor = new Color(0.86f, 0.74f, 0.36f);

        [Header("Partition")]
        [Tooltip("Chance a whole chunk is one big room.")] public float bigHallChance = 0.35f;
        [Tooltip("Chance to stop splitting a region whose longest side is <= 3 cells.")] public float stopSmall = 0.9f;
        [Tooltip("... <= 6 cells.")] public float stopMedium = 0.7f;
        [Tooltip("... <= 9 cells.")] public float stopLarge = 0.5f;
        [Tooltip("... 10-12 cells.")] public float stopHuge = 0.25f;
        [Tooltip("Largest room side in cells: districts always split regions longer than this.")] public float maxRoomCells = 12f;

        [Header("Hallways")]
        [Tooltip("Chance a split near the top of the tree carves a hallway.")] public float hallChanceShallow = 0.3f;
        [Tooltip("Chance a deeper split carves a hallway.")] public float hallChanceDeep = 0.1f;
        [Tooltip("Chance a hallway is two cells (2.4 m) wide.")] public float hallWideChance = 0.3f;
        [Tooltip("Chance a long one-wide hallway ends in a crawl slot.")] public float crawlChance = 0.35f;

        [Header("Connections")]
        [Tooltip("Chance a shared wall between big spaces is opened up completely.")] public float openPlanChance = 0.6f;
        [Tooltip("Chance of extra doors that form loops.")] public float loopChance = 0.5f;

        [Header("Room types")]
        public float loungeChance = 0.18f;
        public float conferenceChance = 0.12f;

        [Header("Ceilings and walls")]
        [Tooltip("Chance a big room gets a tall ceiling.")] public float tallChanceBig = 0.6f;
        [Tooltip("Chance a medium room gets a tall ceiling.")] public float tallChanceSmall = 0.2f;
        [Tooltip("Tall ceiling range, metres.")] public float tallMin = 3.9f, tallMax = 5.4f;
        [Tooltip("Chance of painted plaster instead of wallpaper (tall rooms are always painted).")] public float paintedChance = 0.3f;
        [Tooltip("Chance a doorway has a round arched top instead of a flat header.")] public float archedDoors = 0.35f;
        [Tooltip("Chance a square-topped doorway between two rooms has a wooden door in it.")] public float doorChance = 0.3f;

        [Header("Lighting")]
        [Tooltip("Share of rooms with every light working.")] public float lightsAllOn = 0.52f;
        [Tooltip("Share of rooms with some lights working (the rest are dark).")] public float lightsMixed = 0.33f;
        [Tooltip("In mixed rooms, fraction of fixtures that work.")] public float mixedOnFraction = 0.5f;
        [Tooltip("Chance a working tube stutters now and then.")] public float faultyChance = 0.025f;

        [Header("Dressing")]
        [Tooltip("Chance a big room gets a pillar grid.")] public float pillarChance = 1f;
        [Tooltip("Pillar grid spacing in cells.")] public float pillarSpacing = 3.5f;
        [Tooltip("Half-height partitions per big room (max).")] public float halfWalls = 2f;
        [Tooltip("Chance a big room has abandoned seating scattered in it.")] public float scatteredSeating = 0.3f;
        [Tooltip("Chance a big room is filled with rows of storage racks.")] public float rackChance = 0f;
        [Tooltip("Outlets per wall face.")] public float outletChance = 0.09f;
        [Tooltip("Chance a tall room is lit by hanging cool-white fluorescent strips instead of round domes.")] public float stripLights = 0f;

        [Header("Height and depth (world version 14, DepthPlan)")]
        [Tooltip("Chance a district has a sunken wing: one to three rooms 0.45-0.9 m below the rest, steps down at their doorways.")]
        public float sunkenChance = 0.45f;
        [Tooltip("Chance a district has a pit room: a big room's floor turned into narrow walkways over square shafts.")]
        public float pitRoomChance = 0.12f;
        [Tooltip("Chance a room has its free corners cut off at 45 degrees.")]
        public float chamferChance = 0.18f;
        [Tooltip("Chance a hallway that ends in a crawl slot has its ceiling come down toward it (you crouch to get there).")]
        public float ceilingDropChance = 0.4f;
        [Tooltip("Chance a district has a sloping room: an office whose whole floor slopes down away from its doors.")]
        public float tiltedChance = 0.18f;
        [Tooltip("Chance a district has a flight of steps that climbs into a blank wall.")]
        public float nowhereChance = 0.15f;
        [Tooltip("Chance a tall hall (6.4 m and up) has a steel walkway along one wall, a stair up to it.")]
        public float mezzanineChance = 0.3f;

        [Header("Poolrooms (pool-style zones only)")]
        [Tooltip("Chance a room has openings in its roof (the sun comes in through them).")] public float skylights = 0f;
        [Tooltip("Share of rooms that are flooded basins (the rest are dry tiled decks).")] public float flooded = 0f;
        [Tooltip("Chance a flooded room has a raised walkway along one wall.")] public float ledges = 0f;

        [Header("Homes (the living-rooms biome)")]
        [Tooltip("Chance a room is an emptied-out early-2000s living room: orange-peel painted drywall, carpet, baseboards, " +
                 "a flat plaster ceiling with round flush-mount lights, little or no furniture.")]
        public float homeChance = 0f;
        [Tooltip("Share of those rooms papered with the blue-sky clouds wallpaper instead.")] public float cloudWallpaper = 0f;

        public BiomeRules Clone() => (BiomeRules)MemberwiseClone();

        static readonly System.Reflection.FieldInfo[] Fields = typeof(BiomeRules).GetFields();

        /// <summary>Blend of two biomes. Text and colour come from whichever weighs more.</summary>
        public static BiomeRules Lerp(BiomeRules a, BiomeRules b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            var r = (t < 0.5f ? a : b).Clone();
            foreach (var f in Fields)
            {
                if (f.FieldType == typeof(float)) f.SetValue(r, Mathf.Lerp((float)f.GetValue(a), (float)f.GetValue(b), t));
                else if (f.FieldType == typeof(Color)) f.SetValue(r, Color.Lerp((Color)f.GetValue(a), (Color)f.GetValue(b), t));
            }
            return r;
        }

        // ---------------- Presets ----------------

        public static BiomeRules Offices() => new BiomeRules();

        /// <summary>Endless corridors: hallways at every split, small side rooms, more crawl slots, dimmer.</summary>
        public static BiomeRules Hallways() => new BiomeRules
        {
            name = "Hallways", mapColor = new Color(0.45f, 0.62f, 0.85f),
            bigHallChance = 0.02f, stopSmall = 0.95f, stopMedium = 0.75f, stopLarge = 0.15f, stopHuge = 0.02f,
            hallChanceShallow = 0.95f, hallChanceDeep = 0.7f, hallWideChance = 0.15f, crawlChance = 0.5f,
            openPlanChance = 0.05f, loopChance = 0.5f, loungeChance = 0.05f, conferenceChance = 0.03f,
            tallChanceBig = 0.1f, tallChanceSmall = 0f, paintedChance = 0.15f, archedDoors = 0.12f, doorChance = 0.4f,
            lightsAllOn = 0.4f, lightsMixed = 0.4f, mixedOnFraction = 0.45f, faultyChance = 0.05f,
            pillarChance = 0.3f, halfWalls = 0f, scatteredSeating = 0f, outletChance = 0.12f,
        };

        /// <summary>The Poolrooms: big tall tiled rooms, mostly flooded, round columns, wide openings, sun through the roof.</summary>
        public static BiomeRules Poolrooms() => new BiomeRules
        {
            name = "Poolrooms", mapColor = new Color(0.45f, 0.8f, 0.76f),
            bigHallChance = 0.45f, stopSmall = 0.95f, stopMedium = 0.85f, stopLarge = 0.55f, stopHuge = 0.3f, maxRoomCells = 16f,
            hallChanceShallow = 0.35f, hallChanceDeep = 0.12f, hallWideChance = 0.75f, crawlChance = 0f,
            openPlanChance = 0.4f, loopChance = 0.7f, loungeChance = 0f, conferenceChance = 0f,
            tallChanceBig = 1f, tallChanceSmall = 1f, tallMin = 3.6f, tallMax = 5.4f, paintedChance = 1f, archedDoors = 0.85f, doorChance = 0f,
            lightsAllOn = 0f, lightsMixed = 0f, mixedOnFraction = 0f, faultyChance = 0f,
            pillarChance = 0.6f, pillarSpacing = 3f, halfWalls = 0f, scatteredSeating = 0f, rackChance = 0f,
            outletChance = 0f, stripLights = 0f,
            skylights = 0.85f, flooded = 0.8f, ledges = 0.35f,
        };

        /// <summary>
        /// The channels (2026-11-01, the review's item 15): a biome of the Poolrooms zone of long narrow corridors, all of
        /// them vaulted, flooded channels with a walkway along one side, and halls of water crossed by a winding walkway
        /// (PoolPlan.Channels). Few big halls, many loops, the light mostly from slots in the vaults' crowns.
        /// </summary>
        public static BiomeRules Channels() => new BiomeRules
        {
            name = "Channels", mapColor = new Color(0.3f, 0.55f, 0.55f),
            bigHallChance = 0.2f, stopSmall = 0.9f, stopMedium = 0.75f, stopLarge = 0.45f, stopHuge = 0.25f, maxRoomCells = 12f,
            hallChanceShallow = 0.75f, hallChanceDeep = 0.45f, hallWideChance = 0.5f, crawlChance = 0f,
            openPlanChance = 0.2f, loopChance = 0.85f, loungeChance = 0f, conferenceChance = 0f,
            tallChanceBig = 1f, tallChanceSmall = 1f, tallMin = 3.6f, tallMax = 4.8f, paintedChance = 1f, archedDoors = 0.85f, doorChance = 0f,
            lightsAllOn = 0f, lightsMixed = 0f, mixedOnFraction = 0f, faultyChance = 0f,
            pillarChance = 0.3f, pillarSpacing = 3f, halfWalls = 0f, scatteredSeating = 0f, rackChance = 0f,
            outletChance = 0f, stripLights = 0f,
            skylights = 0.7f, flooded = 0.9f, ledges = 0.1f,
        };

        /// <summary>
        /// The water park (2026-11-01, the owner: "indoor water park, slides, mushroom water things with water going down
        /// them"): a biome of the Poolrooms zone. Bigger, taller halls with more dry tiled decks for the attractions (PoolPlan
        /// .WaterPark: slide towers over plunge pools, a lazy river round an island, a wave pool with a beach, kiddie pools
        /// with mushroom fountains, water arches), fewer columns, light from above everywhere.
        /// </summary>
        public static BiomeRules WaterPark() => new BiomeRules
        {
            name = "WaterPark", mapColor = new Color(0.35f, 0.62f, 0.9f),
            bigHallChance = 0.7f, stopSmall = 0.97f, stopMedium = 0.9f, stopLarge = 0.65f, stopHuge = 0.35f, maxRoomCells = 20f,
            hallChanceShallow = 0.3f, hallChanceDeep = 0.1f, hallWideChance = 0.85f, crawlChance = 0f,
            openPlanChance = 0.55f, loopChance = 0.8f, loungeChance = 0f, conferenceChance = 0f,
            tallChanceBig = 1f, tallChanceSmall = 1f, tallMin = 4.8f, tallMax = 7.2f, paintedChance = 1f, archedDoors = 0.85f, doorChance = 0f,
            lightsAllOn = 0f, lightsMixed = 0f, mixedOnFraction = 0f, faultyChance = 0f,
            pillarChance = 0.25f, pillarSpacing = 4f, halfWalls = 0f, scatteredSeating = 0f, rackChance = 0f,
            outletChance = 0f, stripLights = 0f,
            skylights = 0.95f, flooded = 0.35f, ledges = 0.2f,
        };

        /// <summary>
        /// Living rooms (2026-10-31, the owner's reference photos): the emptied-out living rooms of an early-2000s house, room
        /// after room. Bigger rooms than the offices, few hallways, a door in most doorways, square headers; orange-peel
        /// painted drywall (now and then the blue-sky clouds wallpaper), carpet, baseboards, flat plaster ceilings with round
        /// flush-mount lights, many of them off; outlets and switches; a sofa or a box at most.
        /// </summary>
        public static BiomeRules LivingRooms() => new BiomeRules
        {
            name = "Living rooms", mapColor = new Color(0.78f, 0.62f, 0.5f),
            bigHallChance = 0.3f, stopSmall = 0.95f, stopMedium = 0.85f, stopLarge = 0.6f, stopHuge = 0.35f, maxRoomCells = 13f,
            hallChanceShallow = 0.12f, hallChanceDeep = 0.04f, hallWideChance = 0.4f, crawlChance = 0.12f,
            openPlanChance = 0.25f, loopChance = 0.55f, loungeChance = 0f, conferenceChance = 0f,
            tallChanceBig = 0.08f, tallChanceSmall = 0f, tallMin = 3.0f, tallMax = 3.3f, paintedChance = 1f, archedDoors = 0.04f, doorChance = 0.55f,
            lightsAllOn = 0.3f, lightsMixed = 0.4f, mixedOnFraction = 0.45f, faultyChance = 0.03f,
            pillarChance = 0f, halfWalls = 0f, scatteredSeating = 0f, rackChance = 0f,
            outletChance = 0.16f, stripLights = 0f,
            sunkenChance = 0.1f, pitRoomChance = 0f, chamferChance = 0.05f, ceilingDropChance = 0.1f, tiltedChance = 0.05f,
            nowhereChance = 0.08f, mezzanineChance = 0f,
            homeChance = 1f, cloudWallpaper = 0.12f,
        };

        /// <summary>Huge, tall, open floors with rows of storage racks and sparse high-bay lights.</summary>
        public static BiomeRules Warehouse() => new BiomeRules
        {
            name = "Warehouse", mapColor = new Color(0.62f, 0.55f, 0.48f),
            bigHallChance = 0.75f, stopSmall = 0.95f, stopMedium = 0.85f, stopLarge = 0.7f, stopHuge = 0.55f,
            hallChanceShallow = 0.15f, hallChanceDeep = 0.05f, hallWideChance = 0.8f, crawlChance = 0.1f,
            openPlanChance = 0.8f, loopChance = 0.5f, loungeChance = 0.02f, conferenceChance = 0.02f,
            tallChanceBig = 1f, tallChanceSmall = 0.5f, tallMin = 6.0f, tallMax = 8.1f, paintedChance = 1f, archedDoors = 0f, doorChance = 0.12f,
            lightsAllOn = 0.35f, lightsMixed = 0.45f, mixedOnFraction = 0.4f, faultyChance = 0.04f,
            pillarChance = 1f, pillarSpacing = 4f, halfWalls = 0f, scatteredSeating = 0.1f, rackChance = 0.7f,
            outletChance = 0.03f, stripLights = 1f, maxRoomCells = 20f,
        };
    }
}
