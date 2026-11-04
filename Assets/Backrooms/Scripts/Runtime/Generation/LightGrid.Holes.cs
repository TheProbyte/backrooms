using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Broken walls for the grid and the shaders (Destruction.WallDamage): the occupancy flags a holed wall, and the hole
    /// table (LightGridCommon.hlsl) says which 0.15 m blocks of it are open right through. A wall that breaks is encoded
    /// again and the light around it drawn again.
    /// </summary>
    public partial class LightGrid
    {
        ComputeBuffer _holes, _holeCells;
        uint[] _holeData;
        readonly uint[] _holeCellData = new uint[OccN * OccN * 2];
        int _holeRecords, _holesVersion = -1;
        Vector2Int _holesMin = new Vector2Int(int.MinValue, 0);
        readonly List<(bool vertical, int line, int along, int storey, uint[] mask, uint[] maskAny, float share)> _holeList = new List<(bool, int, int, int, uint[], uint[], float)>();
        static readonly int IdHoleTable = Shader.PropertyToID("_LGridHoleTable"), IdHoleCells = Shader.PropertyToID("_LGridHoleCells"), IdHoleN = Shader.PropertyToID("_LGridHoleN");

        void InitHoles()
        {
            _holeData = LightGridMath.BuildHoleTable(_holeList, null, _holeCellData, OccN, Vector2Int.zero);
            _holes = new ComputeBuffer(_holeData.Length, 4);
            _holes.SetData(_holeData);
            _holeCells = new ComputeBuffer(_holeCellData.Length, 4);
            _holeCells.SetData(_holeCellData);
            _holeRecords = 0; _holesVersion = -1;
            Destruction.WallDamage.Changed += OnWallChanged;
        }

        void ReleaseHoles()
        {
            Destruction.WallDamage.Changed -= OnWallChanged;
            _holes?.Release(); _holes = null;
            _holeCells?.Release(); _holeCells = null;
        }

        /// <summary>A wall broke more: its cell is encoded again (holed or not), and the light around it is drawn again.</summary>
        void OnWallChanged(Destruction.WallKey k)
        {
            // The wall is the west (vertical) or south edge of the cell on its side 1.
            _encoded.Remove(k.Chunk(1));
            _occDirty = true;
            _bounceDirty = true;
            MarkDirty(k.ToWorld(OfficeBiome.CellSize * 0.5f, 1.35f, 0f, _pitch), 14f);
        }

        /// <summary>The hole table for the broken walls in and around the window (rebuilt when walls change or the window moves).</summary>
        void UpdateHoles()
        {
            if (_holes == null || _holeCells == null) return;
            int version = Destruction.WallDamage.Version;
            if (version == _holesVersion && _holesMin == _texMin) return;
            _holesVersion = version; _holesMin = _texMin;
            _holeList.Clear();
            var cellMin = new Vector2Int(_texMin.x / 2, _texMin.y / 2);
            foreach (var kv in Destruction.WallDamage.All)
            {
                var p = kv.Value; var k = kv.Key;
                if (!p.holed) continue;
                var c = k.Cell(1);
                if (c.x < cellMin.x - 1 || c.y < cellMin.y - 1 || c.x > cellMin.x + OccN || c.y > cellMin.y + OccN) continue;
                _holeList.Add((k.vertical, k.line, k.along, k.storey, p.mask, p.maskAny, p.openFraction));
            }
            _holeData = LightGridMath.BuildHoleTable(_holeList, _holeData, _holeCellData, OccN, cellMin);
            if (_holes.count != _holeData.Length) { _holes.Release(); _holes = new ComputeBuffer(_holeData.Length, 4); }
            _holes.SetData(_holeData);
            _holeCells.SetData(_holeCellData);
            _holeRecords = _holeList.Count;
            BufferUploadsLastFrame += 2;
        }

        void BindHolesTo(ComputeShader shader, int kernel)
        {
            if (_holes == null) return;
            shader.SetBuffer(kernel, IdHoleTable, _holes);
            shader.SetBuffer(kernel, IdHoleCells, _holeCells);
            shader.SetInt(IdHoleN, _holeRecords > 0 ? OccN : 0);
        }

        void BindHoleGlobals()
        {
            if (_holes == null) return;
            Shader.SetGlobalBuffer(IdHoleTable, _holes);
            Shader.SetGlobalBuffer(IdHoleCells, _holeCells);
            Shader.SetGlobalInteger(IdHoleN, _holeRecords > 0 ? OccN : 0);
        }
    }
}
