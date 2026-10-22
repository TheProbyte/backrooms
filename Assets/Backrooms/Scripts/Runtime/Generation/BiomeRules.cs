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
            tallChanceBig = 0.1f, tallChanceSmall = 0f, paintedChance = 0.15f, archedDoors = 0.12f,
            lightsAllOn = 0.4f, lightsMixed = 0.4f, mixedOnFraction = 0.45f, faultyChance = 0.05f,
            pillarChance = 0.3f, halfWalls = 0f, scatteredSeating = 0f, outletChance = 0.12f,
        };

        /// <summary>Huge, tall, open floors with rows of storage racks and sparse high-bay lights.</summary>
        public static BiomeRules Warehouse() => new BiomeRules
        {
            name = "Warehouse", mapColor = new Color(0.62f, 0.55f, 0.48f),
            bigHallChance = 0.75f, stopSmall = 0.95f, stopMedium = 0.85f, stopLarge = 0.7f, stopHuge = 0.55f,
            hallChanceShallow = 0.15f, hallChanceDeep = 0.05f, hallWideChance = 0.8f, crawlChance = 0.1f,
            openPlanChance = 0.8f, loopChance = 0.5f, loungeChance = 0.02f, conferenceChance = 0.02f,
            tallChanceBig = 1f, tallChanceSmall = 0.5f, tallMin = 6.0f, tallMax = 8.1f, paintedChance = 1f, archedDoors = 0f,
            lightsAllOn = 0.35f, lightsMixed = 0.45f, mixedOnFraction = 0.4f, faultyChance = 0.04f,
            pillarChance = 1f, pillarSpacing = 4f, halfWalls = 0f, scatteredSeating = 0.1f, rackChance = 0.7f,
            outletChance = 0.03f, stripLights = 1f, maxRoomCells = 20f,
        };
    }
}
