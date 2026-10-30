using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The good rooms' furniture (SpecialRooms): counters with a sink and a microwave, a fridge, steel shelving, a desk with
    /// old monitors, a table, a mop sink and the breaker panel, built from boxes in the plain finishes of FurnitureMaterials.
    /// Local frame of each: origin at the middle of its foot, +X along its width, +Z its front (into the room), +Y up.
    /// The breaker panel's switch and the desk lamp are made on the main thread (Instantiate).
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        /// <summary>Breaker panels: chunk-local position and rotation of the panel's middle (its back on the wall).</summary>
        readonly List<(Vector3 pos, Quaternion rot, int color)> _breakers = new List<(Vector3, Quaternion, int)>();
        /// <summary>Desk lamps to make as items (chunk-local foot, yaw, plugged in, the prop's id).</summary>
        readonly List<(Vector3 at, float yaw, bool lit, ulong id)> _deskLamps = new List<(Vector3, float, bool, ulong)>();

        Material Fin(Material m) => m ? m : _a.baseboard;

        void SpecialProp(Prop p, Vector3 at, Quaternion rot)
        {
            int col = p.roomColor;
            var rng = new System.Random(p.variant ^ (int)(p.id & 0x7FFFFFFF));
            switch (p.kind)
            {
                case PropKind.Counter: Counter(p, at, rot, col, rng); break;
                case PropKind.Fridge: Fridge(p, at, rot, col); break;
                case PropKind.Shelving: Shelving(p, at, rot, col, rng); break;
                case PropKind.Desk: Desk(p, at, rot, col); break;
                case PropKind.DiningTable: DiningTable(p, at, rot, col); break;
                case PropKind.MopSink: MopSink(p, at, rot, col); break;
                case PropKind.BreakerPanel: Breaker(p, at, rot, col); break;
                case PropKind.DeskLamp: _deskLamps.Add((at + Vector3.up * p.size.y, p.yaw, p.lit, p.id)); break;
            }
        }

        /// <summary>A box in the prop's frame.</summary>
        void PB(Material m, int col, Vector3 at, Quaternion rot, Vector3 c, Vector3 s) =>
            W(Fin(m), col).Box(at + rot * c, s, rot, 63, MeshWriter.MetreUV);

        void PB(Material m, int col, Vector3 at, Quaternion rot, Vector3 c, Vector3 s, Quaternion tilt) =>
            W(Fin(m), col).Box(at + rot * c, s, rot * tilt, 63, MeshWriter.MetreUV);

        void Solid(Vector3 at, Quaternion rot, float yaw, Vector3 c, Vector3 s) => _rotBoxes.Add((at + rot * c, s, yaw));

        // ---------------- Kitchen ----------------

        void Counter(Prop p, Vector3 at, Quaternion rot, int col, System.Random rng)
        {
            float w = p.size.x, h = p.size.y, d = p.size.z;
            const float top = 0.035f, plinth = 0.1f;
            var F = FurnitureMaterials.Dark;
            // Plinth set back, the cupboards, the worktop overhanging at the front.
            PB(F, col, at, rot, new Vector3(0f, plinth * 0.5f, -0.04f), new Vector3(w - 0.02f, plinth, d - 0.12f));
            float body = h - top - plinth, front = d * 0.5f - 0.03f;
            PB(_a.wood, col, at, rot, new Vector3(0f, plinth + body * 0.5f, -0.015f), new Vector3(w, body, d - 0.03f));
            PB(_a.laminate, col, at, rot, new Vector3(0f, h - top * 0.5f, 0f), new Vector3(w + 0.02f, top, d + 0.02f));
            // Doors: seams every 0.6 m (a drawer line under the top), a pull on each.
            int doors = Mathf.Max(1, Mathf.RoundToInt(w / 0.6f));
            float dw = w / doors;
            PB(F, col, at, rot, new Vector3(0f, h - top - 0.16f, front + 0.002f), new Vector3(w - 0.02f, 0.006f, 0.004f));
            for (int i = 0; i <= doors; i++)
            {
                float x = -w * 0.5f + i * dw;
                if (i > 0 && i < doors) PB(F, col, at, rot, new Vector3(x, plinth + (body - 0.16f) * 0.5f, front + 0.002f), new Vector3(0.006f, body - 0.18f, 0.004f));
                if (i < doors)
                {
                    float px = x + dw * 0.5f;
                    PB(FurnitureMaterials.Stainless, col, at, rot, new Vector3(px, h - top - 0.08f, front + 0.012f), new Vector3(0.12f, 0.012f, 0.018f));
                    PB(FurnitureMaterials.Stainless, col, at, rot, new Vector3(px + (i % 2 == 0 ? 0.2f : -0.2f) * dw / 0.6f, plinth + body * 0.45f, front + 0.012f), new Vector3(0.014f, 0.1f, 0.018f));
                }
            }
            // A sink set into the top, its tap behind it.
            if ((p.variant & 1) != 0)
            {
                float sx = -w * 0.22f;
                PB(FurnitureMaterials.Stainless, col, at, rot, new Vector3(sx, h + 0.002f, 0.02f), new Vector3(0.52f, 0.006f, 0.44f));
                PB(F, col, at, rot, new Vector3(sx, h + 0.004f, 0.03f), new Vector3(0.42f, 0.004f, 0.32f));
                PB(FurnitureMaterials.Stainless, col, at, rot, new Vector3(sx, h + 0.13f, -d * 0.5f + 0.07f), new Vector3(0.032f, 0.26f, 0.032f));
                PB(FurnitureMaterials.Stainless, col, at, rot, new Vector3(sx, h + 0.25f, -d * 0.5f + 0.15f), new Vector3(0.028f, 0.028f, 0.17f));
                PB(FurnitureMaterials.Stainless, col, at, rot, new Vector3(sx + 0.07f, h + 0.05f, -d * 0.5f + 0.07f), new Vector3(0.05f, 0.04f, 0.04f));
            }
            // A microwave on it, near the other end.
            if ((p.variant & 2) != 0)
            {
                float mx = w * 0.5f - 0.36f;
                var mc = new Vector3(mx, h + 0.15f, -0.06f);
                PB(FurnitureMaterials.Beige, col, at, rot, mc, new Vector3(0.5f, 0.29f, 0.37f));
                PB(F, col, at, rot, mc + new Vector3(-0.06f, 0f, 0.186f), new Vector3(0.3f, 0.19f, 0.004f));
                PB(FurnitureMaterials.Steel, col, at, rot, mc + new Vector3(0.17f, 0f, 0.186f), new Vector3(0.1f, 0.22f, 0.004f));
                Solid(at, rot, p.yaw, mc, new Vector3(0.5f, 0.29f, 0.37f));
            }
            Solid(at, rot, p.yaw, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
        }

        void Fridge(Prop p, Vector3 at, Quaternion rot, int col)
        {
            float w = p.size.x, h = p.size.y, d = p.size.z;
            var E = FurnitureMaterials.Enamel; var F = FurnitureMaterials.Dark;
            PB(E, col, at, rot, new Vector3(0f, h * 0.5f + 0.02f, 0f), new Vector3(w, h - 0.04f, d));
            PB(F, col, at, rot, new Vector3(0f, 0.02f, -0.03f), new Vector3(w - 0.06f, 0.04f, d - 0.08f));
            // The freezer door above, the fridge door below: a seam, a gasket line round them, long pulls.
            float split = h * 0.68f, front = d * 0.5f;
            PB(F, col, at, rot, new Vector3(0f, split, front + 0.002f), new Vector3(w - 0.01f, 0.008f, 0.004f));
            PB(F, col, at, rot, new Vector3(0f, split - 0.25f, front + 0.022f), new Vector3(0.026f, 0.32f, 0.026f), Quaternion.identity);
            PB(F, col, at, rot, new Vector3(w * 0.5f - 0.06f, split - 0.25f, front + 0.022f), new Vector3(0.026f, 0.32f, 0.026f));
            PB(F, col, at, rot, new Vector3(w * 0.5f - 0.06f, split + 0.16f, front + 0.022f), new Vector3(0.026f, 0.18f, 0.026f));
            Solid(at, rot, p.yaw, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
        }

        void DiningTable(Prop p, Vector3 at, Quaternion rot, int col)
        {
            float w = p.size.x, h = p.size.y, d = p.size.z;
            PB(_a.laminate, col, at, rot, new Vector3(0f, h - 0.015f, 0f), new Vector3(w, 0.03f, d));
            PB(FurnitureMaterials.Steel, col, at, rot, new Vector3(0f, h - 0.05f, 0f), new Vector3(w - 0.12f, 0.04f, d - 0.12f));
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? -w * 0.5f + 0.06f : w * 0.5f - 0.06f, z = (i & 2) == 0 ? -d * 0.5f + 0.06f : d * 0.5f - 0.06f;
                PB(FurnitureMaterials.Steel, col, at, rot, new Vector3(x, (h - 0.03f) * 0.5f, z), new Vector3(0.035f, h - 0.03f, 0.035f));
            }
            Solid(at, rot, p.yaw, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
        }

        // ---------------- Storage ----------------

        void Shelving(Prop p, Vector3 at, Quaternion rot, int col, System.Random rng)
        {
            float w = p.size.x, h = p.size.y, d = p.size.z;
            var S = FurnitureMaterials.Steel;
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? -w * 0.5f + 0.015f : w * 0.5f - 0.015f, z = (i & 2) == 0 ? -d * 0.5f + 0.015f : d * 0.5f - 0.015f;
                PB(S, col, at, rot, new Vector3(x, h * 0.5f, z), new Vector3(0.03f, h, 0.03f));
            }
            int levels = Mathf.Clamp(Mathf.RoundToInt(h / 0.45f), 4, 5);
            float pitch = (h - 0.12f) / (levels - 1);
            bool full = (p.variant & (1 << 16)) != 0;
            var body = W(BoardMat, col); var tape = W(TapeMat, col); var print = W(PrintMat, col);
            for (int l = 0; l < levels; l++)
            {
                float y = 0.1f + l * pitch;
                PB(S, col, at, rot, new Vector3(0f, y, 0f), new Vector3(w, 0.022f, d));
                PB(S, col, at, rot, new Vector3(0f, y - 0.02f, d * 0.5f - 0.01f), new Vector3(w, 0.04f, 0.01f));
                if (l == levels - 1 && h - y < 0.3f) continue;
                // Load: cartons and a few tins (paint, cleaner), more on a stock room's.
                float x = -w * 0.5f + 0.05f, free = Mathf.Min(pitch - 0.06f, 0.5f);
                while (x < w * 0.5f - 0.12f)
                {
                    double roll = rng.NextDouble();
                    if (roll < (full ? 0.08 : 0.35)) { x += 0.15f + (float)rng.NextDouble() * 0.25f; continue; }
                    if (roll < (full ? 0.75 : 0.7))
                    {
                        var size = new Vector3(0.22f + (float)rng.NextDouble() * 0.25f, Mathf.Min(free, 0.14f + (float)rng.NextDouble() * 0.24f), Mathf.Min(d - 0.06f, 0.24f + (float)rng.NextDouble() * 0.14f));
                        if (x + size.x > w * 0.5f - 0.03f) break;
                        var foot = at + rot * new Vector3(x + size.x * 0.5f, y + 0.012f, ((float)rng.NextDouble() - 0.5f) * 0.04f);
                        BoxAt(rng, body, tape, print, foot, size, p.yaw + ((float)rng.NextDouble() - 0.5f) * 8f);
                        x += size.x + 0.02f;
                    }
                    else
                    {
                        // A tin: a short round can as an octagon-ish pair of boxes.
                        float r = 0.07f + (float)rng.NextDouble() * 0.04f, th = Mathf.Min(free, 0.12f + (float)rng.NextDouble() * 0.08f);
                        var m = rng.NextDouble() < 0.5 ? FurnitureMaterials.Stainless : rng.NextDouble() < 0.5 ? FurnitureMaterials.Enamel : FurnitureMaterials.Beige;
                        var c = new Vector3(x + r, y + 0.012f + th * 0.5f, ((float)rng.NextDouble() - 0.5f) * 0.1f);
                        PB(m, col, at, rot, c, new Vector3(r * 2f, th, r * 1.4f));
                        PB(m, col, at, rot, c, new Vector3(r * 1.4f, th, r * 2f));
                        x += r * 2f + 0.03f;
                    }
                }
            }
            Solid(at, rot, p.yaw, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
        }

        // ---------------- Office ----------------

        void Desk(Prop p, Vector3 at, Quaternion rot, int col)
        {
            float w = p.size.x, h = p.size.y, d = p.size.z;
            PB(_a.laminate, col, at, rot, new Vector3(0f, h - 0.015f, 0f), new Vector3(w, 0.03f, d));
            PB(_a.wood, col, at, rot, new Vector3(-w * 0.5f + 0.02f, (h - 0.03f) * 0.5f, 0f), new Vector3(0.03f, h - 0.03f, d - 0.04f));
            PB(_a.wood, col, at, rot, new Vector3(w * 0.5f - 0.02f, (h - 0.03f) * 0.5f, 0f), new Vector3(0.03f, h - 0.03f, d - 0.04f));
            PB(_a.wood, col, at, rot, new Vector3(0f, h * 0.55f, -d * 0.5f + 0.05f), new Vector3(w - 0.06f, h * 0.5f, 0.02f));
            // A drawer pedestal on one side.
            PB(_a.wood, col, at, rot, new Vector3(w * 0.5f - 0.24f, (h - 0.04f) * 0.5f, 0.02f), new Vector3(0.4f, h - 0.04f, d - 0.1f));
            for (int i = 0; i < 3; i++)
                PB(FurnitureMaterials.Stainless, col, at, rot, new Vector3(w * 0.5f - 0.24f, h - 0.12f - i * 0.22f, d * 0.5f - 0.025f), new Vector3(0.1f, 0.012f, 0.016f));
            Solid(at, rot, p.yaw, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
            if ((p.variant & 1) == 0) return;
            // Old monitors along the back (security screens), dark; a keyboard in front of the middle one.
            int n = Mathf.Clamp((p.variant >> 1) & 3, 1, 3);
            float span = Mathf.Min(w - 0.5f, 0.42f * n);
            for (int i = 0; i < n; i++)
            {
                float x = n == 1 ? -0.15f : -span * 0.5f + span * i / (n - 1) - 0.12f;
                float turn = n == 1 ? 0f : (i - (n - 1) * 0.5f) * -12f;
                var tilt = Quaternion.Euler(0f, turn, 0f);
                var c = new Vector3(x, h + 0.19f, -d * 0.5f + 0.25f);
                PB(FurnitureMaterials.Beige, col, at, rot, c, new Vector3(0.38f, 0.34f, 0.3f), tilt);
                PB(FurnitureMaterials.Beige, col, at, rot, c + tilt * new Vector3(0f, -0.01f, -0.2f), new Vector3(0.28f, 0.26f, 0.14f), tilt);
                PB(FurnitureMaterials.Dark, col, at, rot, c + tilt * new Vector3(0f, 0.01f, 0.151f), new Vector3(0.3f, 0.23f, 0.004f), tilt);
                PB(FurnitureMaterials.Beige, col, at, rot, new Vector3(x, h + 0.01f, -d * 0.5f + 0.27f), new Vector3(0.24f, 0.02f, 0.22f), tilt);
            }
            PB(FurnitureMaterials.Beige, col, at, rot, new Vector3(-0.12f, h + 0.012f, d * 0.5f - 0.17f), new Vector3(0.45f, 0.024f, 0.16f));
            PB(FurnitureMaterials.Dark, col, at, rot, new Vector3(-0.12f, h + 0.026f, d * 0.5f - 0.17f), new Vector3(0.41f, 0.006f, 0.12f));
        }

        // ---------------- Janitor's closet ----------------

        void MopSink(Prop p, Vector3 at, Quaternion rot, int col)
        {
            float w = p.size.x, h = p.size.y, d = p.size.z;
            var E = FurnitureMaterials.Enamel;
            PB(E, col, at, rot, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
            PB(FurnitureMaterials.Dark, col, at, rot, new Vector3(0f, h + 0.001f, 0.01f), new Vector3(w - 0.08f, 0.003f, d - 0.08f));
            // Taps on the wall above it, a mop leaning in the corner.
            PB(FurnitureMaterials.Stainless, col, at, rot, new Vector3(0f, h + 0.3f, -d * 0.5f + 0.03f), new Vector3(0.24f, 0.04f, 0.05f));
            PB(FurnitureMaterials.Stainless, col, at, rot, new Vector3(0f, h + 0.22f, -d * 0.5f + 0.08f), new Vector3(0.03f, 0.03f, 0.1f));
            PB(_a.wood, col, at, rot, new Vector3(w * 0.5f + 0.06f, 0.7f, -d * 0.5f + 0.12f), new Vector3(0.025f, 1.35f, 0.025f), Quaternion.Euler(-8f, 0f, -6f));
            PB(FurnitureMaterials.Beige, col, at, rot, new Vector3(w * 0.5f + 0.02f, 0.06f, -d * 0.5f + 0.1f), new Vector3(0.22f, 0.1f, 0.12f), Quaternion.Euler(0f, 20f, 0f));
            Solid(at, rot, p.yaw, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d));
        }

        /// <summary>The district's breaker panel: a steel box on the wall (the main switch on it is made on the main thread).</summary>
        void Breaker(Prop p, Vector3 at, Quaternion rot, int col)
        {
            float w = p.size.x, hc = p.size.y, d = p.size.z;
            const float tall = 0.5f;
            var S = FurnitureMaterials.Steel; var F = FurnitureMaterials.Dark;
            var c = new Vector3(0f, hc, 0f);
            PB(S, col, at, rot, c, new Vector3(w, tall, d));
            // The door's seam and hinge side, a little label card, the conduit up into the ceiling.
            PB(F, col, at, rot, c + new Vector3(0f, 0f, d * 0.5f + 0.001f), new Vector3(w - 0.03f, tall - 0.03f, 0.002f));
            PB(S, col, at, rot, c + new Vector3(0f, 0f, d * 0.5f + 0.003f), new Vector3(w - 0.045f, tall - 0.045f, 0.003f));
            PB(FurnitureMaterials.Enamel, col, at, rot, c + new Vector3(-0.06f, 0.15f, d * 0.5f + 0.006f), new Vector3(0.12f, 0.06f, 0.002f));
            float roomTop = 2.7f;
            PB(S, col, at, rot, new Vector3(0f, (hc + tall * 0.5f + roomTop) * 0.5f, -d * 0.5f + 0.03f), new Vector3(0.04f, roomTop - hc - tall * 0.5f, 0.04f));
            _breakers.Add((at + rot * new Vector3(0f, hc, d * 0.5f + 0.004f), rot, col));
            Solid(at, rot, p.yaw, c, new Vector3(w, tall, d));
        }
    }
}
