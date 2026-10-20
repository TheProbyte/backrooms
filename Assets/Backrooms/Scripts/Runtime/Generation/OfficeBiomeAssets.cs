using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>Materials used to build the office biome. Created by the editor builder.</summary>
    [CreateAssetMenu(menuName = "Backrooms/Office Biome Assets")]
    public class OfficeBiomeAssets : ScriptableObject
    {
        public Material carpet, wallpaper, ceilingTile, ceilingGrid, baseboard;
        public Material lensOn, lensOff, fixtureHousing;
        public Material wallPlate, halfWallCap;
        public Material[] sofaFabrics;
        public Material wood, laminate, chairFabric, lampMetal, lampShadeOn, lampShadeOff;
        public Material paintedWall, ceilingPlaster, domeOn, domeOff;

        [Header("Blender-built props (Tools/Blender/build_props.py)")]
        [Tooltip("Sofas: [model * fabricCount + fabric]. Models: chesterfield, modern.")]
        public GameObject[] sofaPrefabs;
        public GameObject[] armchairPrefabs;
        public GameObject floorLampPrefab, officeChairPrefab, tableModulePrefab, tableLegPrefab, wallClockPrefab;
    }
}
