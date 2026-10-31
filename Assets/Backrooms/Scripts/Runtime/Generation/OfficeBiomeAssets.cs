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
                case WallFinish.OrangePeel: if (MaterialMaps.OrangePeel) return MaterialMaps.OrangePeel; return paintedWall ? paintedWall : wallpaper;
                case WallFinish.SkyWallpaper: if (MaterialMaps.SkyWallpaper) return MaterialMaps.SkyWallpaper; return wallpaper;
            }
            return room.painted && paintedWall ? paintedWall : wallpaper;
        }

        /// <summary>
        /// A warehouse hall (the warehouse biome's halls are 6 m and taller; offices top out at 5.4 m) with no finish of its
        /// own: a concrete slab, not carpet (MaterialMaps.Concrete, from a photo). Footsteps there sound on concrete.
        /// </summary>
        public static bool ConcreteFloor(Room room) =>
            room.floorFinish == FloorFinish.Default && room.ceiling >= 5.95f && !room.custom && string.IsNullOrEmpty(room.landmark) && room.kind != RoomKind.Crawl;

        public Material FloorMaterial(Room room)
        {
            if (ConcreteFloor(room) && MaterialMaps.Concrete) return MaterialMaps.Concrete;
            switch (room.floorFinish)
            {
                case FloorFinish.ReferenceCarpet: if (referenceCarpet) return referenceCarpet; break;
                case FloorFinish.DarkWood: if (wood) return wood; break;
                case FloorFinish.Laminate: if (laminate) return laminate; break;
                case FloorFinish.HomeCarpet:
                    var shades = MaterialMaps.HomeCarpets;
                    if (shades != null && shades.Length > 0) return shades[(room.globalId & 0x7FFFFFFF) % shades.Length];
                    break;
            }
            return carpet;
        }

        [Header("Blender-built props (Tools/Blender/build_props.py)")]
        [Tooltip("Sofas: [model * fabricCount + fabric]. Models: chesterfield, modern.")]
        public GameObject[] sofaPrefabs;
        [Tooltip("The floral sofa (Tools/Blender/build_floral_sofa.py, Editor/FloralSofaBuilder): only where build mode's catalog puts it.")]
        public GameObject floralSofaPrefab;
        public GameObject[] armchairPrefabs;
        public GameObject floorLampPrefab, officeChairPrefab, tableModulePrefab, tableLegPrefab, wallClockPrefab;
    }
}
