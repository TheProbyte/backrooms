using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>Materials used to build the office biome. Created by the editor builder.</summary>
    [CreateAssetMenu(menuName = "Backrooms/Office Biome Assets")]
    public class OfficeBiomeAssets : ScriptableObject
    {
        [Tooltip("No baseboard trim along the walls (the Reference Match look). The baseboard material is still used for invisible shadow proxies.")]
        public bool noBaseboard = true;
        [Tooltip("Wall finish with a trace of self-light, for the inside of crawl tunnels (made by Backrooms > Apply Reference Look To Office Biome).")]
        public Material tunnel;
        public Material carpet, wallpaper, ceilingTile, ceilingGrid, baseboard;
        public Material lensOn, lensOff, fixtureHousing;
        public Material wallPlate, halfWallCap;
        public Material[] sofaFabrics;
        public Material wood, laminate, chairFabric, lampMetal, lampShadeOn, lampShadeOff;
        public Material paintedWall, ceilingPlaster, domeOn, domeOff, cardboard, stripOn, stripOff;
        [Header("Warehouse dressing (rack steel, pallets, cardboard boxes)")]
        public Material rackUpright, rackBeam, boxTape, boxPrint, pallet;

        [Header("Blender-built props (Tools/Blender/build_props.py)")]
        [Tooltip("Sofas: [model * fabricCount + fabric]. Models: chesterfield, modern.")]
        public GameObject[] sofaPrefabs;
        public GameObject[] armchairPrefabs;
        public GameObject floorLampPrefab, officeChairPrefab, tableModulePrefab, tableLegPrefab, wallClockPrefab;
    }
}
