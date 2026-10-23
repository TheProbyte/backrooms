using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Warehouse dressing: steel pallet racking (slotted blue uprights, orange box beams, deck boards, end
    /// bracing, base plates) loaded with neatly packed cardboard boxes, and wooden pallets with stacked boxes.
    /// Everything is merged into the chunk's meshes; shadows come from a few proxy boxes.
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
            w.Box((a + b) * 0.5f, new Vector3(thickness, thickness * 1.4f, len), Quaternion.LookRotation(d / len), 63, MeshWriter.MetreUV);
        }

        /// <summary>Steel pallet rack: four slotted uprights, orange box beams, deck boards, end bracing, loaded shelves.</summary>
        void Rack(Prop p, Vector3 at)
        {
            int col = p.roomColor;
            float L = p.size.x, h = p.size.y, D = p.size.z;
            var rng = new System.Random(p.variant ^ 0x5EED);
            var steel = W(UprightMat, col); var beams = W(BeamMat, col); var deck = W(DeckMat, col);
            var body = W(BoardMat, col); var tape = W(TapeMat, col); var print = W(PrintMat, col);
            float ux = L * 0.5f - UprightSize * 0.5f, uz = D * 0.5f - UprightSize * 0.5f;

            // Uprights, base plates.
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1 : 1) * ux, z = (i < 2 ? -1 : 1) * uz;
                Upright(steel, at + new Vector3(x, 0, z), h);
                beams.Box(at + new Vector3(x, 0.006f, z), new Vector3(0.14f, 0.012f, 0.14f), 63, MeshWriter.MetreUV);
            }

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
                    beams.Box(at + new Vector3(0, yb - BeamHeight * 0.5f, z), new Vector3(L - UprightSize * 2f, BeamHeight, 0.05f), 63, MeshWriter.MetreUV);
                    beams.Box(at + new Vector3(0, yb + 0.005f, z - s * 0.012f), new Vector3(L - UprightSize * 2f, 0.01f, 0.026f), 63, MeshWriter.MetreUV);
                }
                // Deck: two boards laid across the beams.
                float bz = Da * 0.5f - 0.02f;
                for (int s = -1; s <= 1; s += 2)
                    deck.Box(at + new Vector3(0, yb + DeckThickness * 0.5f + 0.01f, s * (bz * 0.5f + 0.01f)), new Vector3(La, DeckThickness, bz), 63, MeshWriter.MetreUV);
                ProxyBox(at, 0f, new Vector3(0, yb - 0.04f, 0), new Vector3(L - 0.1f, BeamHeight + DeckThickness, D - 0.1f));

                // Load the shelf.
                float top = yb + DeckThickness + 0.01f;
                float free = (l == levels - 1 ? Mathf.Min(1.0f, h - top - 0.1f) : pitch - BeamHeight - DeckThickness - 0.07f);
                if (rng.NextDouble() < 0.88) LoadShelf(rng, body, tape, print, at, top, La, Da, free);
            }

            _boxes.Add((at + Vector3.up * h * 0.5f, new Vector3(L, h, D)));
        }

        /// <summary>Packs one shelf level with cartons in one or two rows: usually matching sizes, some stacked.</summary>
        void LoadShelf(System.Random rng, MeshWriter body, MeshWriter tape, MeshWriter print, Vector3 at, float top, float La, float Da, float free)
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
                    if (rng.NextDouble() < 0.12) { x += 0.25f + (float)rng.NextDouble() * 0.35f; continue; }   // an empty gap
                    float pull = (float)rng.NextDouble() * 0.04f;                                  // not all flush
                    float z = rowZ + (rows == 1 ? (float)(rng.NextDouble() - 0.5) * (Da - s.z - 0.04f) * 0.5f : (row == 0 ? 1f : -1f) * (pull * 0.5f));
                    float yaw = (float)(rng.NextDouble() - 0.5) * (rng.NextDouble() < 0.1 ? 14f : 4f);
                    int stack = (2f * s.y + 0.02f <= free && rng.NextDouble() < 0.4) ? (3f * s.y + 0.03f <= free && rng.NextDouble() < 0.3 ? 3 : 2) : 1;
                    for (int k = 0; k < stack; k++)
                    {
                        var foot = at + new Vector3(x + s.x * 0.5f + (k > 0 ? (float)(rng.NextDouble() - 0.5) * 0.02f : 0f), top + k * (s.y + 0.002f), z);
                        BoxAt(rng, body, tape, print, foot, s, yaw + (k > 0 ? (float)(rng.NextDouble() - 0.5) * 3f : 0f));
                    }
                    ProxyBox(at, 0f, new Vector3(x + s.x * 0.5f, top + stack * (s.y + 0.002f) * 0.5f, z), new Vector3(s.x, stack * (s.y + 0.002f), s.z));
                    x += s.x + 0.015f + (float)rng.NextDouble() * 0.05f;
                }
            }
        }

        /// <summary>Wooden pallet (1.2 x 1.0 m) loaded with stacked cartons; p.size.y is unused (the load is rolled here).</summary>
        void Pallet(Prop p, Vector3 at, Quaternion rot)
        {
            int col = p.roomColor;
            var rng = new System.Random(p.variant ^ 0x9A11);
            var wood = W(DeckMat, col);
            var body = W(BoardMat, col); var tape = W(TapeMat, col); var print = W(PrintMat, col);
            const float PL = 1.2f, PW = 1.0f, PH = 0.145f;
            Vector3 World(Vector3 local) => at + rot * local;
            void Plank(Vector3 centre, Vector3 size) => wood.Box(World(centre), size, rot, 63, MeshWriter.MetreUV);

            // Three runners (each with two notches would be overkill; blocks keep it simple), three bottom boards, top deck.
            for (int i = -1; i <= 1; i++) Plank(new Vector3(i * 0.5f, 0.0525f, 0f), new Vector3(0.095f, 0.09f, PW - 0.02f));      // runners along z
            for (int i = -1; i <= 1; i++) Plank(new Vector3(0f, 0.01f, i * 0.44f), new Vector3(PL, 0.02f, 0.1f));                  // bottom boards
            for (int i = 0; i < 7; i++) Plank(new Vector3(0f, PH - 0.011f, -PW * 0.5f + 0.05f + i * ((PW - 0.1f) / 6f)), new Vector3(PL, 0.022f, 0.1f));   // top deck

            float loadTop = PH;
            if (rng.NextDouble() < 0.85)
            {
                var s = Cartons[rng.Next(Cartons.Length)];
                if (s.x > PL * 0.55f || s.y < 0.2f) s = new Vector3(0.40f, 0.30f, 0.30f);
                int nx = Mathf.Max(1, Mathf.FloorToInt((PL - 0.02f) / s.x)), nz = Mathf.Max(1, Mathf.FloorToInt((PW - 0.02f) / s.z));
                int layers = rng.Next(1, 5);
                float gx = (PL - nx * s.x) * 0.5f, gz = (PW - nz * s.z) * 0.5f;
                for (int layer = 0; layer < layers; layer++)
                {
                    float y = PH + layer * (s.y + 0.002f);
                    bool last = layer == layers - 1;
                    for (int ix = 0; ix < nx; ix++)
                    for (int iz = 0; iz < nz; iz++)
                    {
                        if (last && layer > 0 && rng.NextDouble() < 0.3) continue;                 // a part-filled top layer
                        var local = new Vector3(-PL * 0.5f + gx + (ix + 0.5f) * s.x, y, -PW * 0.5f + gz + (iz + 0.5f) * s.z);
                        BoxAt(rng, body, tape, print, World(local), s, p.yaw + (float)(rng.NextDouble() - 0.5) * 3f);
                    }
                    loadTop = y + s.y;
                }
            }
            ProxyBox(at, p.yaw, new Vector3(0, loadTop * 0.5f, 0), new Vector3(PL, loadTop, PW));
            _rotBoxes.Add((at + Vector3.up * loadTop * 0.5f, new Vector3(PL, loadTop, PW), p.yaw));
        }
    }
}
