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
    }
}
