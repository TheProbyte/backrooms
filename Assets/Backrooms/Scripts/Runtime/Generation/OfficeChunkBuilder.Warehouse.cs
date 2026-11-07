using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Warehouse dressing: steel pallet racking (slotted blue uprights, orange box beams, deck boards, end
    /// bracing, base plates) loaded with cardboard boxes, and wooden pallets, some with stacked boxes. The racks are merged
    /// into the chunk's meshes (solid where the steel and the decks are, so boxes rest on the shelves); the boxes and the
    /// pallets are movable props (Interaction.MovableProp): taken off the shelves, pushed, carried.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        // Fall back to the older materials if the asset file predates the warehouse materials.
        Material UprightMat => _a.rackUpright ? _a.rackUpright : _a.lampMetal;
        Material BeamMat => _a.rackBeam ? _a.rackBeam : _a.lampMetal;
        Material DeckMat => _a.pallet ? _a.pallet : _a.wood;
        Material BoardMat => _a.cardboard ? _a.cardboard : _a.wood;
        Material TapeMat => _a.boxTape ? _a.boxTape : BoardMat;
        Material PrintMat => _a.boxPrint ? _a.boxPrint : BoardMat;

        /// <summary>Common carton sizes (width, height, depth in metres).</summary>
        static readonly Vector3[] Cartons =
        {
            new Vector3(0.30f, 0.22f, 0.24f), new Vector3(0.30f, 0.30f, 0.30f), new Vector3(0.40f, 0.28f, 0.30f),
            new Vector3(0.46f, 0.30f, 0.34f), new Vector3(0.40f, 0.40f, 0.40f), new Vector3(0.60f, 0.34f, 0.38f),
            new Vector3(0.58f, 0.20f, 0.40f), new Vector3(0.40f, 0.56f, 0.38f), new Vector3(0.78f, 0.36f, 0.40f),
            new Vector3(0.50f, 0.45f, 0.35f),
        };

        const float UprightSize = 0.07f, BeamHeight = 0.10f, DeckThickness = 0.022f;

        /// <summary>A carton merged into the chunk's meshes (fixed: the special rooms' utility shelves).</summary>
        void BoxAt(System.Random rng, MeshWriter body, MeshWriter tape, MeshWriter print, Vector3 foot, Vector3 size, float yaw)
            => CardboardBoxGeometry.Build(body, tape, print, foot, size, yaw, rng);

        /// <summary>One face of a slotted steel upright, texture mapped over its full width and in 0.5 m repeats up its height.</summary>
        void UprightFace(MeshWriter w, Vector3 bl, Vector3 br, float h, Vector3 n)
        {
            float v = h / 0.5f;
            w.Quad(bl, br, br + Vector3.up * h, bl + Vector3.up * h, n, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, v), new Vector2(0, v));
        }

        void Upright(MeshWriter w, Vector3 baseCentre, float h)
        {
            float s = UprightSize * 0.5f;
            Vector3 c = baseCentre;
            UprightFace(w, c + new Vector3(-s, 0, s), c + new Vector3(s, 0, s), h, Vector3.forward);
            UprightFace(w, c + new Vector3(s, 0, -s), c + new Vector3(-s, 0, -s), h, Vector3.back);
            UprightFace(w, c + new Vector3(s, 0, s), c + new Vector3(s, 0, -s), h, Vector3.right);
            UprightFace(w, c + new Vector3(-s, 0, -s), c + new Vector3(-s, 0, s), h, Vector3.left);
            var solid = new Vector2(0.1f, 0.05f);
            w.Quad(c + new Vector3(-s, h, s), c + new Vector3(s, h, s), c + new Vector3(s, h, -s), c + new Vector3(-s, h, -s), Vector3.up, solid, solid, solid, solid);
        }

        /// <summary>A thin brace from a to b (rotated box).</summary>
        void Brace(MeshWriter w, Vector3 a, Vector3 b, float thickness)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            Steel(w, (a + b) * 0.5f, new Vector3(thickness, thickness * 1.4f, len), Quaternion.LookRotation(d / len));
        }

        /// <summary>A rack's steel or deck piece: a plain box, or in the Overflow look one with its edges rounded (StyleSheet).</summary>
        static void Steel(MeshWriter w, Vector3 centre, Vector3 size, Quaternion rot)
        {
            if (ArtStyle.Current == ArtStyle.Look.Overflow) w.RoundBox(centre, size, rot, 0.008f, 63, MeshWriter.MetreUV);
            else w.Box(centre, size, rot, 63, MeshWriter.MetreUV);
        }

        /// <summary>Steel pallet rack: four slotted uprights, orange box beams, deck boards, end bracing, loaded shelves.</summary>
        void Rack(Prop p, Vector3 at)
        {
            if (Mathf.Abs(p.yaw) < 0.001f) { RackUnrotated(p, at); return; }
            var starts = new Dictionary<MeshWriter, int>();
            foreach (var w in _writers.Values) starts[w] = w.VertexCount;
            int boxes = _boxes.Count, movables = _movables.Count;
            RackUnrotated(p, at);
            var rotation = Quaternion.Euler(0f, p.yaw, 0f);
            foreach (var w in _writers.Values)
            { starts.TryGetValue(w, out int start); w.RotateFrom(start, at, rotation); }
            for (int i = boxes; i < _boxes.Count; i++)
            { var b = _boxes[i]; _rotBoxes.Add((at + rotation * (b.c - at), b.s, p.yaw)); }
            _boxes.RemoveRange(boxes, _boxes.Count - boxes);
            Vector3 pivot = _origin + at;
            for (int i = movables; i < _movables.Count; i++)
            { var m = _movables[i]; m.position = pivot + rotation * (m.position - pivot); m.yaw += p.yaw; }
        }

        /// <summary>A movable carton standing at foot (chunk-local), never the heavy paper-filled kind (MovableShapes).</summary>
        void Carton(ulong id, Vector3 foot, float yaw, Vector3 size, int color, System.Random rng)
        {
            var s = Movable(id, Interaction.MovableKind.Box, foot + Vector3.up * 0.002f, yaw, color);
            s.size = size;
            s.variant = (rng.Next(1, 1 << 12) << 3) | (1 + rng.Next(7));
            s.cardboard = true;
            s.materials = new[] { BoardMat, TapeMat, PrintMat };
        }

        void RackUnrotated(Prop p, Vector3 at)
        {
            int col = p.roomColor;
            float L = p.size.x, h = p.size.y, D = p.size.z;
            var rng = new System.Random(p.variant ^ 0x5EED);
            var steel = W(UprightMat, col); var beams = W(BeamMat, col); var deck = W(DeckMat, col);
            int boxId = 0;
            float ux = L * 0.5f - UprightSize * 0.5f, uz = D * 0.5f - UprightSize * 0.5f;

            // Uprights, base plates. (Solid, and casting shadows: they and the decks are what is solid of a rack now that
            // its boxes can be taken off it; it was one solid box.)
            uint m = LayerBit(col);
            bool hardware = FittingModels.Ready && FittingModels.Get("RackConnector") != null;
            var hrng = new System.Random(p.variant ^ 0x7A11);           // (its own dice: the load stays as it was)
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * ux, z = (i < 2 ? -1 : 1) * uz;
                Upright(steel, at + new Vector3(x, 0, z), h);
                // Its foot plate and anchor bolts (RackBasePlate, 2026-11-04); now and then a yellow guard round the aisle side.
                if (!hardware || !Model("RackBasePlate", m, at + new Vector3(x, 0f, z), Quaternion.Euler(0f, 90f * (i & 1), 0f)))
                    beams.Box(at + new Vector3(x, 0.006f, z), new Vector3(0.14f, 0.012f, 0.14f), 63, MeshWriter.MetreUV);
                if (hardware && hrng.NextDouble() < 0.3)
                    Model("ColumnGuard", m, at + new Vector3(x, 0f, z), Quaternion.Euler(0f, z > 0f ? 0f : 180f, 0f));
                _boxes.Add((at + new Vector3(x, h * 0.5f, z), new Vector3(UprightSize, h, UprightSize)));
            }
            // The capacity plate on one end frame's ties.
            if (hardware && hrng.NextDouble() < 0.6)
                Model("LoadSign", m, at + new Vector3(ux + UprightSize * 0.5f + 0.004f, 1.45f, 0f), Quaternion.Euler(0f, 90f, 0f));
            // The end frames' bracing, as a thin wall each end (boxes do not slide out past them).
            for (int side = -1; side <= 1; side += 2)
                _boxes.Add((at + new Vector3(side * ux, h * 0.5f, 0f), new Vector3(0.03f, h, D - UprightSize)));

            // End frames (the short sides): ties and a diagonal between the two uprights.
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * ux;
                float y0 = 0.32f, y1 = Mathf.Min(h - 0.25f, y0 + 1.25f);
                Brace(beams, at + new Vector3(x, y0, -uz), at + new Vector3(x, y0, uz), 0.022f);
                Brace(beams, at + new Vector3(x, y1, -uz), at + new Vector3(x, y1, uz), 0.022f);
                Brace(beams, at + new Vector3(x, y0, -uz), at + new Vector3(x, y1, uz), 0.02f);
                if (h > 3.6f)
                {
                    float y2 = Mathf.Min(h - 0.2f, y1 + 1.25f);
                    Brace(beams, at + new Vector3(x, y1, uz), at + new Vector3(x, y2, -uz), 0.02f);
                    Brace(beams, at + new Vector3(x, y2, -uz), at + new Vector3(x, y2, uz), 0.022f);
                }
            }

            // Shelf levels.
            int levels = Mathf.Clamp(Mathf.RoundToInt(h / 0.95f), 2, 6);
            float first = 0.2f, pitch = (h - 0.3f - first) / (levels - 1);
            float La = L - UprightSize * 2f - 0.02f, Da = D - UprightSize * 2f - 0.04f;
            for (int l = 0; l < levels; l++)
            {
                float yb = first + l * pitch;                          // top of the beam
                // Front and back beams: a box section with a small upper lip.
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = s * (uz - UprightSize * 0.5f - 0.0125f);
                    Steel(beams, at + new Vector3(0, yb - BeamHeight * 0.5f, z), new Vector3(L - UprightSize * 2f, BeamHeight, 0.05f), Quaternion.identity);
                    Steel(beams, at + new Vector3(0, yb + 0.005f, z - s * 0.012f), new Vector3(L - UprightSize * 2f, 0.01f, 0.026f), Quaternion.identity);
                    if (hardware)
                    {
                        // The beam's end plates on the uprights, riveted, their safety locks; the bay's location label.
                        for (int e = -1; e <= 1; e += 2)
                            Model("RackConnector", m, at + new Vector3(e * (L * 0.5f - UprightSize), yb, z), Quaternion.Euler(0f, e > 0 ? 0f : 180f, 0f));
                        if (hrng.NextDouble() < 0.85)
                            Model("RackLabel_" + hrng.Next(4), m, at + new Vector3((float)(hrng.NextDouble() - 0.5) * 0.3f, yb - BeamHeight * 0.5f, z + s * 0.0252f),
                                Quaternion.Euler(0f, s > 0 ? 0f : 180f, 0f));
                    }
                }
                // Deck: two boards laid across the beams.
                float bz = Da * 0.5f - 0.02f;
                for (int s = -1; s <= 1; s += 2)
                    Steel(deck, at + new Vector3(0, yb + DeckThickness * 0.5f + 0.01f, s * (bz * 0.5f + 0.01f)), new Vector3(La, DeckThickness, bz), Quaternion.identity);
                // Load the shelf: what the boxes stand on is the deck's top.
                float top = yb + DeckThickness + 0.01f;
                float bottom = yb - BeamHeight;
                _boxes.Add((at + new Vector3(0f, (top + bottom) * 0.5f, 0f), new Vector3(L - 0.02f, top - bottom, D - 0.05f)));
                float free = (l == levels - 1 ? Mathf.Min(1.0f, h - top - 0.1f) : pitch - BeamHeight - DeckThickness - 0.07f);
                if (rng.NextDouble() < 0.6) LoadShelf(rng, p, at, top, La, Da, free, ref boxId);
            }
        }

        /// <summary>Puts cartons on one shelf level in one or two rows, with gaps: usually matching sizes, a few stacked. Each
        /// is a movable box (ids from the rack's).</summary>
        void LoadShelf(System.Random rng, Prop p, Vector3 at, float top, float La, float Da, float free, ref int boxId)
        {
            int rows = Da >= 0.82f ? 2 : 1;
            float rowDepth = Da / rows - 0.03f;
            bool matching = rng.NextDouble() < 0.6;
            Vector3 sizeA = Cartons[rng.Next(Cartons.Length)];
            for (int row = 0; row < rows; row++)
            {
                float rowZ = rows == 1 ? 0f : (row == 0 ? 1f : -1f) * (Da * 0.25f);
                float x = -La * 0.5f + 0.02f + (float)rng.NextDouble() * 0.06f;
                int guard = 0;
                while (x < La * 0.5f - 0.2f && guard++ < 12)
                {
                    var s = matching ? sizeA : Cartons[rng.Next(Cartons.Length)];
                    if (s.z > rowDepth) s = new Vector3(s.x, s.y, rowDepth);                      // squeezed into the depth
                    if (s.y > free) { if (matching) break; continue; }
                    if (x + s.x > La * 0.5f - 0.01f) break;
                    if (rng.NextDouble() < 0.3) { x += 0.3f + (float)rng.NextDouble() * 0.5f; continue; }   // an empty gap
                    float pull = (float)rng.NextDouble() * 0.04f;                                  // not all flush
                    float z = rowZ + (rows == 1 ? (float)(rng.NextDouble() - 0.5) * (Da - s.z - 0.04f) * 0.5f : (row == 0 ? 1f : -1f) * (pull * 0.5f));
                    float yaw = (float)(rng.NextDouble() - 0.5) * (rng.NextDouble() < 0.1 ? 14f : 4f);
                    int stack = (2f * s.y + 0.02f <= free && rng.NextDouble() < 0.25) ? (3f * s.y + 0.03f <= free && rng.NextDouble() < 0.15 ? 3 : 2) : 1;
                    for (int k = 0; k < stack; k++)
                    {
                        var foot = at + new Vector3(x + s.x * 0.5f + (k > 0 ? (float)(rng.NextDouble() - 0.5) * 0.02f : 0f), top + k * (s.y + 0.004f), z);
                        Carton(Interaction.MovableSpec.SubId(p.id, boxId++), foot, yaw + (k > 0 ? (float)(rng.NextDouble() - 0.5) * 3f : 0f), s, p.roomColor, rng);
                    }
                    x += s.x + 0.015f + (float)rng.NextDouble() * 0.05f;
                }
            }
        }

        /// <summary>A wooden pallet (1.2 x 1.0 m, movable), loaded with stacked cartons more often than not (each movable too);
        /// p.size.y is unused (the load is rolled here).</summary>
        void Pallet(Prop p, Vector3 at, Quaternion rot)
        {
            int col = p.roomColor;
            var rng = new System.Random(p.variant ^ 0x9A11);
            const float PL = PalletGeometry.Length, PW = PalletGeometry.Width, PH = PalletGeometry.Height;
            var pallet = Movable(p.id, Interaction.MovableKind.Pallet, at, p.yaw, col);
            pallet.materials = new[] { DeckMat };
            Vector3 World(Vector3 local) => at + rot * local;
            if (rng.NextDouble() >= 0.6) return;                                                 // an empty one
            var s = Cartons[rng.Next(Cartons.Length)];
            if (s.x > PL * 0.55f || s.y < 0.2f) s = new Vector3(0.40f, 0.30f, 0.30f);
            int nx = Mathf.Max(1, Mathf.FloorToInt((PL - 0.02f) / s.x)), nz = Mathf.Max(1, Mathf.FloorToInt((PW - 0.02f) / s.z));
            int layers = rng.Next(1, 4);
            float gx = (PL - nx * s.x) * 0.5f, gz = (PW - nz * s.z) * 0.5f;
            int boxId = 0;
            for (int layer = 0; layer < layers; layer++)
            {
                float y = PH + layer * (s.y + 0.004f);
                bool last = layer == layers - 1;
                for (int ix = 0; ix < nx; ix++)
                for (int iz = 0; iz < nz; iz++)
                {
                    if (last && layer > 0 && rng.NextDouble() < 0.4) continue;                 // a part-filled top layer
                    var local = new Vector3(-PL * 0.5f + gx + (ix + 0.5f) * s.x, y, -PW * 0.5f + gz + (iz + 0.5f) * s.z);
                    Carton(Interaction.MovableSpec.SubId(p.id, boxId++), World(local), p.yaw + (float)(rng.NextDouble() - 0.5) * 3f, s, col, rng);
                }
            }
        }
    }
}
