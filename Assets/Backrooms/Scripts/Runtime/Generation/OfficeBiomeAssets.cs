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

        [Header("Existing Reference Match finishes for custom room areas")]
        public Material referenceCream, referenceOlive, referenceCarpet;

        public Material WallMaterial(Room room)
        {
            switch (room.wallFinish)
            {
                case WallFinish.Wallpaper: return wallpaper;
                case WallFinish.PaintedPlaster: return paintedWall ? paintedWall : wallpaper;
                case WallFinish.ReferenceCream: if (referenceCream) return referenceCream; break;
                case WallFinish.ReferenceOlive: if (referenceOlive) return referenceOlive; break;
            }
            return room.painted && paintedWall ? paintedWall : wallpaper;
        }

        public Material FloorMaterial(Room room)
        {
            switch (room.floorFinish)
            {
                case FloorFinish.ReferenceCarpet: if (referenceCarpet) return referenceCarpet; break;
                case FloorFinish.DarkWood: if (wood) return wood; break;
                case FloorFinish.Laminate: if (laminate) return laminate; break;
            }
            return carpet;
        }

        [Header("Blender-built props (Tools/Blender/build_props.py)")]
        [Tooltip("Sofas: [model * fabricCount + fabric]. Models: chesterfield, modern.")]
        public GameObject[] sofaPrefabs;
        public GameObject[] armchairPrefabs;
        public GameObject floorLampPrefab, officeChairPrefab, tableModulePrefab, tableLegPrefab, wallClockPrefab;
    }
}
