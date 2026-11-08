using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Noclip doors (see NoclipDoors): the wall is drawn as usual (it looks solid), but the merged wall colliders leave the
    /// door's edge out and only the wall around the opening (NoclipDoors.Width x Height) is solid. Each door gets an
    /// Interaction.NoclipPassage (what NoclipTravel watches) and, separately, its marking (Interaction.NoclipDoorMarking).
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Each door's edge, and its opening's width and height (a seam's tear is narrower than a door).</summary>
        readonly Dictionary<(bool, int, int), (float w, float h)> _noclipEdges = new Dictionary<(bool, int, int), (float, float)>();

        void IndexNoclip()
        {
            _noclipEdges.Clear();
            foreach (var d in _L.noclipDoors)
            {
                int x = d.cell.x, z = d.cell.y;
                var size = d.seam ? (NoclipDoors.SeamWidth, NoclipDoors.SeamHeight) : (NoclipDoors.Width, NoclipDoors.Height);
                switch (d.side)
                {
                    case 0: _noclipEdges[(true, x, z)] = size; break;
                    case 1: _noclipEdges[(true, x + 1, z)] = size; break;
                    case 2: _noclipEdges[(false, z, x)] = size; break;
                    default: _noclipEdges[(false, z + 1, x)] = size; break;
                }
            }
        }

        bool NoclipAt(bool vertical, int line, int along) => _noclipEdges.Count > 0 && _noclipEdges.ContainsKey((vertical, line, along));

        /// <summary>Colliders of the wall around each door's opening (the opening itself has none).</summary>
        void BuildNoclipDoors()
        {
            const float HC = OfficeBiome.MaxCeilingHeight;
            foreach (var kv in _noclipEdges)
            {
                var (vertical, line, along) = kv.Key;
                float W = kv.Value.w, DH = kv.Value.h;
                float l = line * C, mid = (along + 0.5f) * C, d0 = mid - W * 0.5f, d1 = mid + W * 0.5f;
                float e0 = along * C - T * 0.5f, e1 = (along + 1) * C + T * 0.5f;
                void Box(float a0, float a1, float y0, float y1)
                {
                    var s = vertical ? new Vector3(T, y1 - y0, a1 - a0) : new Vector3(a1 - a0, y1 - y0, T);
                    var c = vertical ? new Vector3(l, (y0 + y1) * 0.5f, (a0 + a1) * 0.5f) : new Vector3((a0 + a1) * 0.5f, (y0 + y1) * 0.5f, l);
                    _boxes.Add((c, s));
                }
                Box(e0, d0, -0.2f, HC + 0.2f);
                Box(d1, e1, -0.2f, HC + 0.2f);
                Box(d0, d1, DH, HC + 0.2f);
            }
        }

        void InstantiateNoclipDoors(Transform root)
        {
            foreach (var d in _L.noclipDoors)
            {
                var go = new GameObject("NoclipDoor_" + d.id.ToString("X"));
                go.transform.SetParent(root, true);
                go.transform.SetPositionAndRotation(d.position, Quaternion.LookRotation(d.normal, Vector3.up));
                var p = go.AddComponent<Interaction.NoclipPassage>();
                p.Set(d, _L.style == ZoneStyle.Pool);
                Interaction.NoclipDoorMarking.Mark(p);
            }
        }
    }
}
