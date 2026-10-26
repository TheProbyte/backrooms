using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Crawl spaces (CrawlPortals.PlaceSpaces): the special room each vent leads to, built by the vent's chunk in the space
    /// above the drop ceiling over it. A sealed box (floor, four walls, ceiling, all inward-facing, with colliders) with a
    /// vent in its back wall straight above the hole below; the vent is the other end of the hole's crawl portal (its own
    /// CrawlPortalView, partnered with the hole). Its surfaces use <see cref="SpecialRoomMaterials"/> and only the Default
    /// rendering layer: no fixture light reaches them, only the room's own light. The dressing and the things lying in it
    /// are made on the main thread (Interaction.SpecialRoom).
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        public struct SpecialRoomData
        {
            public CrawlPortal portal;
            public Vector3 min, max;          // the room's inside, chunk-local
            public Vector3 back;              // normal of its back wall (into the room)
        }

        readonly List<SpecialRoomData> _specials = new List<SpecialRoomData>();

        void BuildCrawlSpaces()
        {
            _specials.Clear();
            if (Pool) return;
            foreach (var p in _L.portals)
            {
                if (!p.space) continue;
                float I = CrawlPortals.RoomInset, y0 = p.roomFloor, y1 = y0 + CrawlPortals.RoomHeight;
                var R = p.room;
                Vector3 min = new Vector3(R.xMin * C + I, y0, R.yMin * C + I), max = new Vector3(R.xMax * C - I, y1, R.yMax * C - I);
                Vector3 back = p.side == 0 ? Vector3.right : p.side == 1 ? Vector3.left : p.side == 2 ? Vector3.forward : Vector3.back;
                _specials.Add(new SpecialRoomData { portal = p, min = min, max = max, back = back });
                Vector3 vent = p.partnerPosition - _origin;
                SpecialRoomShell(min, max, p.side, p.side < 2 ? vent.z : vent.x);
            }
        }

        /// <summary>The room's shell: inward faces, the back wall open over the vent, and its colliders.</summary>
        void SpecialRoomShell(Vector3 min, Vector3 max, int backSide, float ventAlong)
        {
            const uint mask = 1u;               // Default only: lit by the room's own light, never by the fixtures below
            var wall = WMask(SpecialRoomMaterials.Wall, mask);
            var floor = WMask(SpecialRoomMaterials.Floor, mask);
            var ceil = WMask(SpecialRoomMaterials.Ceiling, mask);
            float x0 = min.x, x1 = max.x, z0 = min.z, z1 = max.z, y0 = min.y, y1 = max.y;
            const float HS = CrawlPortals.HoleSize, wt = 0.12f;

            floor.Quad(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), Vector3.up, MeshWriter.MetreUV);
            ceil.Quad(new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.down, MeshWriter.MetreUV);
            _boxes.Add((new Vector3((x0 + x1) * 0.5f, y0 - 0.1f, (z0 + z1) * 0.5f), new Vector3(x1 - x0 + 2 * wt, 0.2f, z1 - z0 + 2 * wt)));
            _boxes.Add((new Vector3((x0 + x1) * 0.5f, y1 + 0.1f, (z0 + z1) * 0.5f), new Vector3(x1 - x0 + 2 * wt, 0.2f, z1 - z0 + 2 * wt)));

            // One wall: on line X = at (vertical) or Z = at, spanning a0..a1 along it, facing n; the back wall is cut over the vent.
            void Wall(bool vertical, float at, float a0, float a1, Vector3 n, bool vent)
            {
                Vector3 P(float along, float y) => vertical ? new Vector3(at, y, along) : new Vector3(along, y, at);
                float outward = vertical ? -n.x : -n.z;
                Vector3 Box(float b0, float b1, float c0, float c1, out Vector3 size)
                {
                    float t = at + outward * wt * 0.5f;
                    size = vertical ? new Vector3(wt, c1 - c0, b1 - b0) : new Vector3(b1 - b0, c1 - c0, wt);
                    return vertical ? new Vector3(t, (c0 + c1) * 0.5f, (b0 + b1) * 0.5f) : new Vector3((b0 + b1) * 0.5f, (c0 + c1) * 0.5f, t);
                }
                void Part(float b0, float b1, float c0, float c1)
                {
                    if (b1 - b0 < 1e-4f || c1 - c0 < 1e-4f) return;
                    wall.Quad(P(b0, c0), P(b1, c0), P(b1, c1), P(b0, c1), n, MeshWriter.WallUV);
                    var c = Box(b0 - (vertical ? 0f : 0f), b1, c0, c1, out var s);
                    _boxes.Add((c, s));
                }
                if (!vent) { Part(a0 - wt, a1 + wt, y0, y1); return; }
                float h0 = ventAlong - HS * 0.5f, h1 = ventAlong + HS * 0.5f;
                Part(a0 - wt, h0, y0, y1);
                Part(h1, a1 + wt, y0, y1);
                Part(h0, h1, y0 + HS, y1);
            }
            Wall(true, x0, z0, z1, Vector3.right, backSide == 0);
            Wall(true, x1, z0, z1, Vector3.left, backSide == 1);
            Wall(false, z0, x0, x1, Vector3.forward, backSide == 2);
            Wall(false, z1, x0, x1, Vector3.back, backSide == 3);
        }

        void InstantiateCrawlSpaces(Transform root)
        {
            foreach (var sr in _specials)
            {
                var p = sr.portal;
                // The room's vent: the other end of the hole's portal.
                var vent = new CrawlPortal
                {
                    cell = p.cell, side = p.side, position = p.partnerPosition, normal = p.partnerNormal,
                    partnerPosition = p.position, partnerNormal = p.normal, id = p.id ^ 0x5EC0000000000000ul,
                };
                Interaction.CrawlPortalView.Create(root, vent, _a.tunnel ? _a.tunnel : SpecialRoomMaterials.Wall, SpecialRoomMaterials.Floor, null, 1u, 1u);
                Interaction.SpecialRoom.Create(root, sr.min + _origin, sr.max + _origin, sr.back, p.partnerPosition, p.template, p.seed, p.id, _a);
            }
        }
    }
}
