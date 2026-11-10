using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    public partial class LightGrid
    {
        [Range(0f, 1f)] public float contactOcclusion = 0.65f;
        public const int MaxOccluders = 160;
        /// <summary>
        /// The most boxes one tile hands the shaders (2026-11-09). Every pixel on a surface in a tile tests every box in
        /// it (four rays each, and again for each light without a shadow map), so a pile of things in one place cost by the
        /// pile: a storage room of 12 shelves and 96 cartons ran at 19 fps without ray tracing and 48 with it, on a card
        /// that draws the empty room at over 100. The boxes are sorted nearest the eye first, so a full tile keeps its
        /// nearest.
        /// </summary>
        public const int MaxPerTile = 24;
        public int OccluderCount { get; private set; }
        public int OcclusionRevision { get; private set; }
        struct OccluderRecord { public Vector4 centre, halfSize, inverseRotation; }
        readonly OccluderRecord[] _occluderData = new OccluderRecord[MaxOccluders];
        readonly List<(BoxCollider box, float distance)> _nearBoxes = new List<(BoxCollider, float)>(512);
        static readonly System.Comparison<(BoxCollider box, float distance)> BoxDistance = (a, b) => a.distance.CompareTo(b.distance);
        static readonly System.Comparison<(BoxCollider box, float distance)> BoxId = (a, b) => a.box.GetEntityId().CompareTo(b.box.GetEntityId());
        readonly List<uint>[] _occBins = new List<uint>[Tiles * Tiles];
        readonly List<uint> _occIndices = new List<uint>(2048);
        readonly uint[] _occTiles = new uint[Tiles * Tiles * 2];
        ComputeBuffer _occluders, _occluderTiles, _occluderIndices;
        Vector2Int _occluderMin;
        bool _occludersValid;
        float _contactPrevious = -1;

        // Which boxes are near is found by a search over every furniture owner in the world (1,500 and more where boxes
        // fill a warehouse: it was 1.7 ms a frame, the most expensive script). It now runs in slices of ScanSlice owners a
        // frame, and only once the eye has moved ScanMove metres or ScanEvery seconds have passed; the chosen boxes are still
        // read every frame (a carried box keeps its occlusion with it).
        const int ScanSlice = 256;
        const float ScanMove = 0.5f, ScanEvery = 0.5f;
        readonly List<LightingOccluder> _scanOwners = new List<LightingOccluder>(2048);
        readonly List<(BoxCollider box, float distance)> _scanFound = new List<(BoxCollider, float)>(512);
        int _scanAt = -1;
        Vector3 _scanEye, _selectedEye = new Vector3(float.MaxValue, 0f, 0f);
        float _selectedAt = -999f;
        /// <summary>Owner searches finished (for tests).</summary>
        public int OccluderScans { get; private set; }

        /// <summary>
        /// One slice of the search for boxes near the eye (all of it at once when complete is set). True when a search has
        /// just finished and its boxes are in _nearBoxes, nearest first, at most MaxOccluders.
        /// </summary>
        bool ScanOccluders(Vector3 eye, bool complete)
        {
            if (_scanAt < 0)
            {
                _scanOwners.Clear(); _scanOwners.AddRange(LightingOccluder.All);
                _scanFound.Clear(); _scanAt = 0; _scanEye = eye;
            }
            int end = complete ? _scanOwners.Count : Mathf.Min(_scanOwners.Count, _scanAt + ScanSlice);
            for (; _scanAt < end; _scanAt++)
            {
                var owner = _scanOwners[_scanAt];
                if (!owner || (owner.transform.position - _scanEye).sqrMagnitude > 22f * 22f) continue;
                foreach (var box in owner.Boxes)
                {
                    if (!box || !box.enabled || box.isTrigger) continue;
                    float distance = box.bounds.SqrDistance(_scanEye);
                    if (distance < 14f * 14f) _scanFound.Add((box, distance));
                }
            }
            if (_scanAt < _scanOwners.Count) return false;
            _scanAt = -1;
            _nearBoxes.Clear(); _nearBoxes.AddRange(_scanFound);
            _nearBoxes.Sort(BoxDistance);
            if (_nearBoxes.Count > MaxOccluders) _nearBoxes.RemoveRange(MaxOccluders, _nearBoxes.Count - MaxOccluders);
            _nearBoxes.Sort(BoxId); // selection may change, but distance ranking alone must not invalidate bounce
            _selectedEye = _scanEye; _selectedAt = Time.time;
            OccluderScans++;
            return true;
        }

        void InitOccluders()
        {
            _occluders = new ComputeBuffer(MaxOccluders, 48);
            _occluderTiles = new ComputeBuffer(Tiles * Tiles, 8);
            _occluderIndices = new ComputeBuffer(MaxOccluders * 16, 4);
            for (int i = 0; i < _occBins.Length; i++) _occBins[i] = new List<uint>(8);
            _occludersValid = false;
        }

        void ReleaseOccluders()
        {
            _occluders?.Release(); _occluderTiles?.Release(); _occluderIndices?.Release();
            _occluders = _occluderTiles = _occluderIndices = null;
            _nearBoxes.Clear(); OccluderCount = 0; _occludersValid = false; _scanAt = -1;
        }

        void UpdateOccluders(Vector3 eye)
        {
            if (contactOcclusion <= 0) { _nearBoxes.Clear(); _scanAt = -1; }
            else if (!_occludersValid || _contactPrevious != contactOcclusion) { _scanAt = -1; ScanOccluders(eye, true); }
            else if (_scanAt >= 0 || (eye - _selectedEye).sqrMagnitude > ScanMove * ScanMove || Time.time - _selectedAt > ScanEvery)
                ScanOccluders(eye, false);
            // Boxes gone since they were chosen (a prop unloaded, broken or switched off) drop out at once.
            for (int i = _nearBoxes.Count - 1; i >= 0; i--)
            {
                var b = _nearBoxes[i].box;
                if (!b || !b.enabled) _nearBoxes.RemoveAt(i);
            }
            int count = _nearBoxes.Count;
            bool changed = !_occludersValid || count != OccluderCount || _contactPrevious != contactOcclusion;
            for (int i = 0; i < count; i++)
            {
                var box = _nearBoxes[i].box; var t = box.transform;
                var scale = t.lossyScale; scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                var centre = t.TransformPoint(box.center); var half = Vector3.Scale(box.size, scale) * 0.5f;
                var rotation = Quaternion.Inverse(t.rotation);
                var record = new OccluderRecord { centre = new Vector4(centre.x, centre.y, centre.z, half.magnitude), halfSize = new Vector4(half.x, half.y, half.z, 0), inverseRotation = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w) };
                var old = _occluderData[i];
                changed |= !record.centre.Equals(old.centre) || !record.halfSize.Equals(old.halfSize) || !record.inverseRotation.Equals(old.inverseRotation);
                _occluderData[i] = record;
            }
            bool bins = changed || _occluderMin != _texMin;
            if (changed)
            {
                if (count > 0) { _occluders.SetData(_occluderData, 0, 0, count); BufferUploadsLastFrame++; }
                _bounceDirty = true; OcclusionRevision++;
            }
            OccluderCount = count; _contactPrevious = contactOcclusion;
            if (bins)
            {
                foreach (var bin in _occBins) bin.Clear();
                float tileSize = T * TileTexels;
                int xMin = FloorDiv(_texMin.x, TileTexels), zMin = FloorDiv(_texMin.y, TileTexels);
                for (int i = 0; i < count; i++)
                {
                    var bounds = _nearBoxes[i].box.bounds; bounds.Expand(4f);
                    int x0 = Mathf.Max(xMin, Mathf.FloorToInt(bounds.min.x / tileSize)), x1 = Mathf.Min(xMin + Tiles - 1, Mathf.FloorToInt(bounds.max.x / tileSize));
                    int z0 = Mathf.Max(zMin, Mathf.FloorToInt(bounds.min.z / tileSize)), z1 = Mathf.Min(zMin + Tiles - 1, Mathf.FloorToInt(bounds.max.z / tileSize));
                    for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) { var bin = _occBins[Wrap(z, Tiles) * Tiles + Wrap(x, Tiles)]; if (bin.Count < MaxPerTile) bin.Add((uint)i); }
                }
                _occIndices.Clear();
                for (int i = 0; i < _occBins.Length; i++) { _occTiles[i * 2] = (uint)_occIndices.Count; _occTiles[i * 2 + 1] = (uint)_occBins[i].Count; _occIndices.AddRange(_occBins[i]); }
                if (_occIndices.Count > _occluderIndices.count) { _occluderIndices.Release(); _occluderIndices = new ComputeBuffer(Mathf.NextPowerOfTwo(_occIndices.Count), 4); }
                _occluderTiles.SetData(_occTiles); if (_occIndices.Count > 0) _occluderIndices.SetData(_occIndices);
                BufferUploadsLastFrame += _occIndices.Count > 0 ? 2 : 1;
                _occluderMin = _texMin;
            }
            _occludersValid = true;
            var info = new Vector4(T * TileTexels, Tiles, count, contactOcclusion);
            if (bindGlobals) BindOccluderGlobals();
            compute.SetBuffer(_kBounceSrc, "_LocalOccBoxes", _occluders); compute.SetBuffer(_kBounceSrc, "_LocalOccTiles", _occluderTiles); compute.SetBuffer(_kBounceSrc, "_LocalOccIndices", _occluderIndices);
            compute.SetVector("_LocalOccInfo", info);
        }
        void BindOccluderGlobals()
        {
            if (_occluders == null) return;
            Shader.SetGlobalBuffer("_LocalOccBoxes", _occluders); Shader.SetGlobalBuffer("_LocalOccTiles", _occluderTiles); Shader.SetGlobalBuffer("_LocalOccIndices", _occluderIndices);
            Shader.SetGlobalVector("_LocalOccInfo", new Vector4(T * TileTexels, Tiles, OccluderCount, contactOcclusion));
        }
    }
}
