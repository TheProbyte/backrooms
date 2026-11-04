using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// What an office building has on its walls (2026-11-03), rare, seeded like the wear (world seed and cell), at the
    /// heights the codes give (ADA, NFPA 72):
    ///  - Fire gear: an extinguisher in a white or red cabinet, or hung on a bracket (now and then gone, the empty bracket
    ///    left), with its red FIRE EXTINGUISHER sign; a red manual pull station beside some doorways, its handle at 1.2 m;
    ///    a horn strobe high on the wall (2.03 m, or 15 cm under a low ceiling), dead.
    ///  - A thermostat at about 1.45 m: a beige box with a little display, or an old round dial.
    ///  - A low return-air grille just above the baseboard, its louvres dark behind.
    ///  - A drinking fountain in a built-out alcove: two wing walls and a soffit in the wall's own finish, a stainless
    ///    basin with its bubbler and push bar, a backsplash, the shroud below.
    ///  - A window filled in: its aluminium frame and sill left in the wall, painted board where the glass was, a mullion
    ///    in some, the joints showing.
    ///  - Corner guards on the jambs of some openings (more in hallways, where carts go): steel or grey vinyl angles to
    ///    1.3 m.
    /// One thing a wall face at most, none where a face has an outlet or switch, nothing where furniture stands in front.
    /// Geometry in the chunk's own meshes (finishes: FurnitureMaterials, FittingMaterials); colliders for what stands out.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        static readonly MeshWriter.UVProjector MU = MeshWriter.MetreUV;

        bool PlateOn(int x, int z, int side)
        {
            foreach (var p in _L.plates) if (p.cell.x == x && p.cell.y == z && p.side == side) return true;
            return false;
        }

        /// <summary>How a wall face ends (as WallDirt): k 0 the - end, 1 the + end.</summary>
        WallEnd FaceEnd(int x, int z, int side, int k)
        {
            int ax = side < 2 ? 0 : 1, az = side < 2 ? 1 : 0;
            int s = k == 0 ? -1 : 1;
            int endSide = side < 2 ? (k == 0 ? 2 : 3) : (k == 0 ? 0 : 1);
            if (OfficeLayout.IsSolid(_L.EdgeOf(x, z, endSide))) return WallEnd.Corner;
            int nx = x + s * ax, nz = z + s * az;
            if (!_L.InChunk(nx, nz) || OfficeLayout.IsSolid(_L.EdgeOf(nx, nz, side))) return WallEnd.Runs;
            var e2 = _L.EdgeOf(nx, nz, side);
            return e2 == EdgeKind.Door || e2 == EdgeKind.DoorHeader ? WallEnd.Doorway : WallEnd.Open;
        }

        /// <summary>An axis-aligned collider for a box given in a wall's frame (rv along, up, into).</summary>
        void WallBox(Vector3 c, Vector3 rv, Vector3 into, float w, float h, float d)
        {
            var s = new Vector3(Mathf.Abs(rv.x) * w + Mathf.Abs(into.x) * d, h, Mathf.Abs(rv.z) * w + Mathf.Abs(into.z) * d);
            _boxes.Add((c, s));
        }

        /// <summary>
        /// The fittings on one intact wall face seen from cell (x, z). face: a point on the face at the cell's middle, floor
        /// level; along: the face's horizontal axis; into: into the room; top: the wall's top.
        /// </summary>
        void Fittings(int x, int z, int side, Vector3 face, Vector3 along, Vector3 into, Room r, uint m, float top)
        {
            if (r.custom || r.kind == RoomKind.Crawl || r.special != GoodRoom.None || !FurnitureMaterials.Ready || !FittingMaterials.Ready) return;
            int gx = GX(x), gz = GZ(z);
            var style = DistrictGenerator.StyleOf(r);
            bool hall = r.kind == RoomKind.Hallway;
            float ww = DetailRates.WallWear;
            Vector3 rv = Vector3.Cross(Vector3.up, -into);                 // the viewer's right, facing the wall
            float yb = NoBaseboard ? 0f : BB;
            var ends = new[] { FaceEnd(x, z, side, 0), FaceEnd(x, z, side, 1) };

            // Corner guards on the jambs of openings (each end on its own).
            for (int k = 0; k < 2; k++)
            {
                if (ends[k] != WallEnd.Doorway && ends[k] != WallEnd.Open) continue;
                if (Dice(gx, gz, 3060 + side * 2 + k) >= (hall ? 0.006f : 0.002f) * ww) continue;
                CornerGuard(face, along, into, k, Mathf.Min(1.3f, top - 0.3f), yb, m, Dice(gx, gz, 3068 + side) < 0.6f);
            }

            if (PlateOn(x, z, side)) return;
            float clearAhead(float a, float d) => Clear(face + along * a + into * d, 0.2f) ? 1f : 0f;

            // A pull station beside a doorway.
            if (style != RoomStyle.LivingRoom)
                for (int k = 0; k < 2; k++)
                {
                    if (ends[k] != WallEnd.Doorway || Dice(gx, gz, 3070 + side * 2 + k) >= 0.009f * ww) continue;
                    float ae = k == 0 ? -(C * 0.5f + T * 0.5f) : C * 0.5f + T * 0.5f, sgn = k == 0 ? 1f : -1f;
                    PullStation(face + along * (ae + sgn * 0.24f), into, rv, m);
                    Saw(face + along * (ae + sgn * 0.24f) + Vector3.up * 1.2f, into, "pull_station");
                    return;
                }

            // One other thing at most on the face, by its kind of room.
            float u = Dice(gx, gz, 3080 + side) / Mathf.Max(0.05f, ww);
            float t = 0f;
            bool wide = !hall || Mathf.Min(r.fullRect.width, r.fullRect.height) >= 2;
            if (style == RoomStyle.LivingRoom)
            {
                var at = face + along * Mathf.Lerp(-0.3f, 0.3f, Dice(gx, gz, 3081 + side));
                if (u < (t += 0.004f)) { Thermostat(at, into, rv, m, Dice(gx, gz, 3082 + side) < 0.6f); Saw(at + Vector3.up * 1.45f, into, "thermostat"); }
                else if (u < (t += 0.003f) && Clear(at + into * 0.2f, 0.2f)) { LowVent(at, into, rv, m, yb); Saw(at + Vector3.up * 0.4f, into, "wall_vent"); }
                return;
            }
            bool warehouse = style == RoomStyle.Warehouse;
            float pExt = warehouse ? 0.006f : hall ? 0.0045f : 0.0017f, pStrobe = warehouse ? 0.003f : hall ? 0.0035f : 0.0015f;
            float pVent = warehouse ? 0f : hall ? 0.0035f : 0.0045f, pThermo = warehouse ? 0f : hall ? 0.0008f : 0.0018f;
            float pFountain = warehouse || !wide ? 0f : hall ? 0.003f : 0.001f, pWindow = warehouse ? 0f : hall ? 0.0012f : 0.0036f;
            float a0 = Mathf.Lerp(-0.25f, 0.25f, Dice(gx, gz, 3081 + side));
            if (u < (t += pExt))
            {
                if (clearAhead(a0, 0.3f) < 1f) return;
                bool cabinet = Dice(gx, gz, 3083 + side) < 0.6f, empty = Dice(gx, gz, 3084 + side) < 0.3f;
                Extinguisher(face + along * a0, into, rv, m, cabinet, empty, Dice(gx, gz, 3085 + side) < 0.3f);
                Saw(face + along * a0 + Vector3.up * 1.2f, into, "extinguisher");
            }
            else if (u < (t += pStrobe))
            {
                float yc = Mathf.Min(2.03f, top - 0.15f) - 0.065f;
                Strobe(face + along * a0 + Vector3.up * yc, into, rv, m);
                Saw(face + along * a0 + Vector3.up * yc, into, "strobe");
            }
            else if (u < (t += pVent))
            {
                if (clearAhead(a0, 0.2f) < 1f) return;
                LowVent(face + along * a0, into, rv, m, yb);
                Saw(face + along * a0 + Vector3.up * 0.4f, into, "wall_vent");
            }
            else if (u < (t += pThermo))
            {
                Thermostat(face + along * a0, into, rv, m, Dice(gx, gz, 3082 + side) < 0.6f);
                Saw(face + along * a0 + Vector3.up * 1.45f, into, "thermostat");
            }
            else if (u < (t += pFountain))
            {
                if (top < 2.4f || clearAhead(0f, 0.45f) < 1f || ends[0] == WallEnd.Doorway || ends[1] == WallEnd.Doorway) return;
                Fountain(face, into, rv, r, m);
                Saw(face + Vector3.up * 1.1f, into, "fountain");
            }
            else if (u < (t += pWindow))
            {
                if (top < 2.35f) return;
                FilledWindow(face, along, into, rv, m, Mathf.Min(2.05f, top - 0.3f), gx * 31 + gz * 7 + side);
                Saw(face + Vector3.up * 1.45f, into, "window_filled");
            }
        }

        // ---------------- Fire ----------------

        /// <summary>An extinguisher in its cabinet, or hung on a bracket; empty: only the cabinet or the bracket. foot: on the wall face at floor level.</summary>
        void Extinguisher(Vector3 foot, Vector3 into, Vector3 rv, uint m, bool cabinet, bool empty, bool redCabinet)
        {
            var up = Vector3.up;
            var q = Quaternion.LookRotation(into);
            var red = WMask(FittingMaterials.RedPaint, m);
            var sign = WMask(FittingMaterials.SignExtinguisher, m);
            float signY;
            if (cabinet)
            {
                const float W = 0.28f, H = 0.62f, D = 0.14f, y0 = 0.88f, th = 0.012f;
                var shell = WMask(redCabinet ? FittingMaterials.RedPaint : FurnitureMaterials.Enamel, m);
                OpenBox(shell, foot + up * (y0 + H * 0.5f), rv, into, W * 0.5f, H * 0.5f, D, th);
                // The door: a frame round where the glass is (seen through at this size as nothing at all).
                var frame = WMask(FurnitureMaterials.Steel, m);
                const float fw = 0.022f, fd = 0.012f;
                Vector3 F(float uu, float yy) => foot + rv * uu + up * yy + into * (D + fd * 0.5f);
                frame.Box(F(0f, y0 + H - fw * 0.5f), new Vector3(W, fw, fd), q, 63, MU);
                frame.Box(F(0f, y0 + fw * 0.5f), new Vector3(W, fw, fd), q, 63, MU);
                frame.Box(F(-W * 0.5f + fw * 0.5f, y0 + H * 0.5f), new Vector3(fw, H - 2f * fw, fd), q, 63, MU);
                frame.Box(F(W * 0.5f - fw * 0.5f, y0 + H * 0.5f), new Vector3(fw, H - 2f * fw, fd), q, 63, MU);
                frame.Box(F(W * 0.5f - fw - 0.012f, y0 + H * 0.5f), new Vector3(0.012f, 0.07f, 0.02f), q, 63, MU);      // the pull
                if (!empty) ExtinguisherBody(foot + up * (y0 + th) + into * (D * 0.5f), into, rv, m);
                WallBox(foot + up * (y0 + H * 0.5f) + into * (D * 0.5f), rv, into, W, H, D);
                signY = y0 + H + 0.12f;
            }
            else
            {
                // A strap bracket at 1.2 m, the extinguisher hanging on it.
                var steel = WMask(FurnitureMaterials.Steel, m);
                steel.Box(foot + up * 1.16f + into * 0.012f, new Vector3(0.05f, 0.09f, 0.024f), q, 47, MU);
                steel.Box(foot + up * 1.2f + into * 0.04f, new Vector3(0.04f, 0.012f, 0.06f), q, 63, MU);
                if (!empty) ExtinguisherBody(foot + up * 0.73f + into * 0.08f, into, rv, m);
                signY = 1.72f;
            }
            Vector3 sc = foot + up * signY + into * 0.003f;
            const float shw = 0.13f, shh = 0.065f;
            sign.Quad(sc - rv * shw - up * shh, sc + rv * shw - up * shh, sc + rv * shw + up * shh, sc - rv * shw + up * shh, into,
                      new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            red.Box(sc - into * 0.0015f, new Vector3(shw * 2f + 0.006f, shh * 2f + 0.006f, 0.003f), q, 1 | 2 | 4 | 8, MU);
        }

        /// <summary>A red extinguisher standing at foot: the cylinder, its shoulder, valve, lever, gauge and hose.</summary>
        void ExtinguisherBody(Vector3 foot, Vector3 into, Vector3 rv, uint m)
        {
            var up = Vector3.up;
            var q = Quaternion.LookRotation(into);
            var red = WMask(FittingMaterials.RedPaint, m);
            var dark = WMask(FittingMaterials.Rubber, m);
            var metal = WMask(FurnitureMaterials.Stainless, m);
            const float rr = 0.064f, hh = 0.38f;
            red.Frustum(foot, rr, rr, hh, 16, true, MU);
            red.Frustum(foot + up * hh, rr, 0.03f, 0.045f, 16, true, MU);
            red.Disc(foot + up * 0.002f, rr, 16, Vector3.down, MU);
            metal.Frustum(foot + up * (hh + 0.045f), 0.016f, 0.016f, 0.03f, 10, true, MU);
            metal.Box(foot + up * (hh + 0.085f), new Vector3(0.05f, 0.03f, 0.035f), q, 63, MU);
            metal.Box(foot + up * (hh + 0.105f) + rv * 0.035f, new Vector3(0.11f, 0.008f, 0.022f), q * Quaternion.Euler(0f, 0f, 12f), 63, MU);   // the lever
            metal.Box(foot + up * (hh + 0.07f) + rv * 0.03f, new Vector3(0.08f, 0.007f, 0.02f), q, 63, MU);                                     // the carrying handle
            WMask(FurnitureMaterials.Enamel, m).Disc(foot + up * (hh + 0.085f) + into * 0.0178f, 0.011f, 10, into, MU);                    // the gauge
            // The hose, from the valve down the side to a nozzle clipped low.
            Vector3 prev = foot + up * (hh + 0.08f) - rv * 0.03f;
            for (int s = 1; s <= 5; s++)
            {
                float k = s / 5f;
                var p = foot + up * Mathf.Lerp(hh + 0.06f, 0.12f, k) - rv * (rr + 0.012f) + into * (0.02f * Mathf.Sin(k * 3f));
                var d = p - prev;
                dark.Box((p + prev) * 0.5f, new Vector3(0.014f, 0.014f, d.magnitude + 0.004f), Quaternion.LookRotation(d.normalized, into), 63, MU);
                prev = p;
            }
            dark.Box(prev + Vector3.down * 0.02f, new Vector3(0.02f, 0.05f, 0.02f), Quaternion.identity, 63, MU);
        }

        /// <summary>A red pull station with its handle at 1.2 m. foot: on the wall face at floor level.</summary>
        void PullStation(Vector3 foot, Vector3 into, Vector3 rv, uint m)
        {
            var up = Vector3.up;
            var q = Quaternion.LookRotation(into);
            const float w = 0.12f, h = 0.17f, d = 0.06f, yc = 1.17f;
            var c = foot + up * yc;
            WMask(FittingMaterials.RedPaint, m).Box(c + into * (d * 0.5f), new Vector3(w, h, d), q, 47, MU);
            var f = c + into * (d + 0.0006f);
            WMask(FittingMaterials.PullFace, m).Quad(f - rv * (w * 0.5f - 0.004f) - up * (h * 0.5f - 0.004f), f + rv * (w * 0.5f - 0.004f) - up * (h * 0.5f - 0.004f),
                f + rv * (w * 0.5f - 0.004f) + up * (h * 0.5f - 0.004f), f - rv * (w * 0.5f - 0.004f) + up * (h * 0.5f - 0.004f), into,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            // The handle's lip under the white bar.
            WMask(FurnitureMaterials.Enamel, m).Box(c + into * (d + 0.008f) - up * 0.032f, new Vector3(0.08f, 0.01f, 0.016f), q, 63, MU);
        }

        /// <summary>A horn strobe, dead: a red box, FIRE on its face, the strobe's lens above. c: on the wall face at its centre.</summary>
        void Strobe(Vector3 c, Vector3 into, Vector3 rv, uint m)
        {
            var up = Vector3.up;
            var q = Quaternion.LookRotation(into);
            const float w = 0.13f, h = 0.13f, d = 0.05f;
            WMask(FittingMaterials.RedPaint, m).Box(c + into * (d * 0.5f), new Vector3(w, h, d), q, 47, MU);
            var f = c + into * (d + 0.0006f) - up * 0.035f;
            const float tw = 0.055f, th = 0.0275f;
            WMask(FittingMaterials.SignFire, m).Quad(f - rv * tw - up * th, f + rv * tw - up * th, f + rv * tw + up * th, f - rv * tw + up * th, into,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            WMask(_a.lensOff ? _a.lensOff : FurnitureMaterials.Enamel, m).Box(c + into * (d + 0.012f) + up * 0.03f, new Vector3(0.07f, 0.036f, 0.024f), q, 47, MU);
        }

        // ---------------- Air ----------------

        /// <summary>A thermostat at about 1.45 m: a beige box with a display and two buttons, or an old round dial on a plate.</summary>
        void Thermostat(Vector3 foot, Vector3 into, Vector3 rv, uint m, bool box)
        {
            var up = Vector3.up;
            var q = Quaternion.LookRotation(into);
            var beige = WMask(FurnitureMaterials.Beige, m);
            var c = foot + up * 1.45f;
            if (box)
            {
                const float w = 0.125f, h = 0.085f, d = 0.028f;
                beige.Box(c + into * (d * 0.5f), new Vector3(w, h, d), q, 47, MU);
                beige.Box(c + into * (d + 0.002f), new Vector3(w - 0.014f, h - 0.014f, 0.004f), q, 1 | 2 | 4 | 8 | 32, MU);
                if (FittingMaterials.Display)
                {
                    var f = c + into * (d + 0.0045f) - rv * 0.022f + up * 0.012f;
                    const float dw = 0.024f, dh = 0.012f;
                    WMask(FittingMaterials.Display, m).Quad(f - rv * dw - up * dh, f + rv * dw - up * dh, f + rv * dw + up * dh, f - rv * dw + up * dh, into,
                        new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
                }
                var dark = WMask(FurnitureMaterials.Dark, m);
                for (int k = 0; k < 2; k++)
                    dark.Box(c + into * (d + 0.006f) + rv * (0.03f + k * 0.022f) - up * 0.015f, new Vector3(0.014f, 0.01f, 0.004f), q, 63, MU);
            }
            else
            {
                beige.Box(c + into * 0.005f, new Vector3(0.1f, 0.1f, 0.01f), q, 47, MU);
                // The round cover: a short drum built upright, turned to face the room.
                int start = beige.VertexCount;
                var pivot = c + into * 0.01f;
                beige.Frustum(pivot, 0.043f, 0.04f, 0.022f, 20, true, MU);
                beige.Disc(pivot + Vector3.up * 0.022f, 0.04f, 20, Vector3.up, MU);
                beige.RotateFrom(start, pivot, Quaternion.FromToRotation(Vector3.up, into));
                WMask(FurnitureMaterials.Dark, m).Box(c + into * 0.0325f + up * 0.03f, new Vector3(0.004f, 0.012f, 0.002f), q, 63, MU);   // the pointer
            }
        }

        /// <summary>A low return-air grille just above the baseboard: a frame, angled louvres, dark behind.</summary>
        void LowVent(Vector3 foot, Vector3 into, Vector3 rv, uint m, float yb)
        {
            var up = Vector3.up;
            var q = Quaternion.LookRotation(into);
            const float w = 0.32f, h = 0.16f, fw = 0.016f, fd = 0.01f;
            var c = foot + up * (yb + 0.04f + h * 0.5f);
            if (MaterialMaps.Plenum)
            {
                var back = c + into * 0.0015f;
                WMask(MaterialMaps.Plenum, m).Quad(back - rv * (w * 0.5f) - up * (h * 0.5f), back + rv * (w * 0.5f) - up * (h * 0.5f), back + rv * (w * 0.5f) + up * (h * 0.5f), back - rv * (w * 0.5f) + up * (h * 0.5f), into, MU);
            }
            var metal = WMask(FurnitureMaterials.Enamel, m);
            metal.Box(c + up * (h * 0.5f - fw * 0.5f) + into * (fd * 0.5f), new Vector3(w, fw, fd), q, 47, MU);
            metal.Box(c - up * (h * 0.5f - fw * 0.5f) + into * (fd * 0.5f), new Vector3(w, fw, fd), q, 47, MU);
            metal.Box(c - rv * (w * 0.5f - fw * 0.5f) + into * (fd * 0.5f), new Vector3(fw, h - 2f * fw, fd), q, 47, MU);
            metal.Box(c + rv * (w * 0.5f - fw * 0.5f) + into * (fd * 0.5f), new Vector3(fw, h - 2f * fw, fd), q, 47, MU);
            var lq = q * Quaternion.Euler(-35f, 0f, 0f);
            for (int k = 0; k < 7; k++)
            {
                float y = -h * 0.5f + fw + (k + 0.5f) * (h - 2f * fw) / 7f;
                metal.Box(c + up * y + into * 0.006f, new Vector3(w - 2f * fw, 0.0025f, 0.016f), lq, 63, MU);
            }
        }

        // ---------------- Water ----------------

        /// <summary>A drinking fountain in a built-out alcove at the middle of the face (wing walls and a soffit in the wall's finish).</summary>
        void Fountain(Vector3 face, Vector3 into, Vector3 rv, Room r, uint m)
        {
            var up = Vector3.up;
            var q = Quaternion.LookRotation(into);
            var wallMat = _a.WallMaterial(r);
            var wall = WMask(wallMat ? wallMat : FurnitureMaterials.Enamel, m);
            const float depth = 0.32f, wing = 0.11f, half = 0.47f, height = 2.05f, soffit = 0.35f;
            for (int s = -1; s <= 1; s += 2)
            {
                var wc = face + rv * (s * half) + up * (height * 0.5f) + into * (depth * 0.5f);
                wall.Box(wc, new Vector3(wing, height, depth), q, 63 & ~16 & ~4, MeshWriter.WallUV);
                WallBox(wc, rv, into, wing, height, depth);
            }
            wall.Box(face + up * (height - soffit * 0.5f) + into * (depth * 0.5f), new Vector3(2f * half - wing, soffit, depth), q, 4 | 8 | 32, MeshWriter.WallUV);
            // The fountain: backsplash, basin with its bowl, bubbler and push bar, the shroud under it.
            var steel = WMask(FurnitureMaterials.Stainless, m);
            var bs = face + up * 1.06f + into * 0.002f;
            steel.Quad(bs - rv * 0.22f - up * 0.2f, bs + rv * 0.22f - up * 0.2f, bs + rv * 0.22f + up * 0.2f, bs - rv * 0.22f + up * 0.2f, into, MU);
            const float by = 0.82f, bd = 0.3f;
            steel.Box(face + up * by + into * (bd * 0.5f), new Vector3(0.4f, 0.07f, bd), q, 63 & ~16, MU);
            var bowl = face + up * (by + 0.0355f) + into * (bd * 0.5f + 0.01f);
            WMask(FurnitureMaterials.Dark, m).Quad(bowl - rv * 0.14f - into * 0.1f, bowl + rv * 0.14f - into * 0.1f, bowl + rv * 0.14f + into * 0.1f, bowl - rv * 0.14f + into * 0.1f, up, MU);
            steel.Disc(bowl + up * 0.0005f, 0.018f, 10, up, MU);
            steel.Frustum(face + up * (by + 0.035f) + into * 0.1f + rv * 0.1f, 0.012f, 0.009f, 0.04f, 10, true, MU);
            WMask(FurnitureMaterials.Dark, m).Box(face + up * by + into * (bd + 0.007f), new Vector3(0.12f, 0.025f, 0.012f), q, 63, MU);
            WMask(FurnitureMaterials.Steel, m).Box(face + up * 0.62f + into * 0.12f, new Vector3(0.34f, 0.3f, 0.24f), q, 63 & ~16, MU);
            WallBox(face + up * 0.68f + into * (bd * 0.5f), rv, into, 0.4f, 0.4f, bd);
            // Water down the wall under it, long dried.
            _wallWear.Rect(face + up * 0.42f + into * 0.0013f, rv, up, 0.08f, 0.22f, new Color32(170, 160, 136, 120), new Vector2(0.4f, 0.5f));
        }

        // ---------------- Windows ----------------

        /// <summary>
        /// A window filled in: its frame and sill left in the wall, painted board where the glass was (a shade off the wall),
        /// a mullion in some, the board's joints and screws showing faintly. head: the top of the frame.
        /// </summary>
        void FilledWindow(Vector3 face, Vector3 along, Vector3 into, Vector3 rv, uint m, float head, int seed)
        {
            var up = Vector3.up;
            var q = Quaternion.LookRotation(into);
            var rng = new System.Random(seed);
            float w = rng.NextDouble() < 0.5 ? 0.9f : 1f, sill = 0.86f + 0.1f * (float)rng.NextDouble();
            float h = head - sill;
            const float fw = 0.05f, fd = 0.04f;
            var frame = WMask(MaterialMaps.Aluminium ? MaterialMaps.Aluminium : FurnitureMaterials.Stainless, m);
            Vector3 F(float uu, float yy, float dd) => face + rv * uu + up * yy + into * dd;
            frame.Box(F(0f, head - fw * 0.5f, fd * 0.5f), new Vector3(w, fw, fd), q, 47, MU);
            frame.Box(F(0f, sill + fw * 0.5f, fd * 0.5f), new Vector3(w, fw, fd), q, 47, MU);
            frame.Box(F(-w * 0.5f + fw * 0.5f, sill + h * 0.5f, fd * 0.5f), new Vector3(fw, h - 2f * fw, fd), q, 47, MU);
            frame.Box(F(w * 0.5f - fw * 0.5f, sill + h * 0.5f, fd * 0.5f), new Vector3(fw, h - 2f * fw, fd), q, 47, MU);
            if (rng.NextDouble() < 0.4) frame.Box(F(0f, sill + h * 0.5f, fd * 0.5f), new Vector3(0.035f, h - 2f * fw, fd * 0.8f), q, 47, MU);
            if (rng.NextDouble() < 0.3) frame.Box(F(0f, sill + h * 0.72f, fd * 0.5f), new Vector3(w - 2f * fw, 0.035f, fd * 0.8f), q, 47, MU);
            // The board where the glass was, a little behind the frame's face.
            var panel = WMask(FittingMaterials.WindowPaint ? FittingMaterials.WindowPaint : FurnitureMaterials.Enamel, m);
            var pc = F(0f, sill + h * 0.5f, 0.012f);
            float pw = w * 0.5f - fw, ph = h * 0.5f - fw;
            panel.Quad(pc - rv * pw - up * ph, pc + rv * pw - up * ph, pc + rv * pw + up * ph, pc - rv * pw + up * ph, into, MeshWriter.WallUV);
            // The inner sides of the frame down to it (the window's depth).
            frame.Quad(F(-pw, sill + fw, 0.012f), F(pw, sill + fw, 0.012f), F(pw, sill + fw, fd), F(-pw, sill + fw, fd), up, MU);
            // The sill (the stool) under it, painted, and its apron.
            var stool = WMask(FurnitureMaterials.Enamel, m);
            stool.Box(F(0f, sill - 0.014f, 0.065f), new Vector3(w + 0.1f, 0.028f, 0.13f), q, 63 & ~16, MU);
            stool.Box(F(0f, sill - 0.06f, 0.008f), new Vector3(w, 0.065f, 0.016f), q, 63 & ~16, MU);
            WallBox(F(0f, sill - 0.014f, 0.065f), rv, into, w + 0.1f, 0.028f, 0.13f);
            // Joints and screws.
            var joint = new Color32(168, 160, 146, 110);
            var lift = into * 0.0132f;
            _wallWear.Rect(F(0f, sill + h * 0.5f, 0f) + lift, rv, up, 0.006f, ph * 0.95f, joint, new Vector2(0.3f, 0.8f));
            for (int k = 0; k < 6; k++)
            {
                float yy = sill + fw + 0.08f + (h - 2f * fw - 0.16f) * k / 5f;
                foreach (float uu in new[] { -pw + 0.04f, pw - 0.04f })
                    _wallWear.Rect(F(uu, yy, 0f) + lift, rv, up, 0.004f, 0.004f, new Color32(120, 114, 104, 160), new Vector2(-0.3f, 0f));
            }
        }

        // ---------------- Corners ----------------

        /// <summary>An angle guarding a jamb's corner (end k of the face): one leg on the face, one round the corner.</summary>
        void CornerGuard(Vector3 face, Vector3 along, Vector3 into, int k, float height, float yb, uint m, bool steel)
        {
            var mat = steel ? FurnitureMaterials.Stainless : FurnitureMaterials.Beige;
            if (!mat) return;
            var w = WMask(mat, m);
            float ae = k == 0 ? -(C * 0.5f + T * 0.5f) : C * 0.5f + T * 0.5f, sgn = k == 0 ? 1f : -1f;
            var up = Vector3.up;
            float y0 = yb + 0.01f, ym = (y0 + height) * 0.5f, hh = height - y0;
            var corner = face + along * ae + up * ym;
            const float leg = 0.045f, th = 0.003f;
            var q = Quaternion.LookRotation(into);
            w.Box(corner + along * (sgn * leg * 0.5f) + into * (th * 0.5f), new Vector3(leg, hh, th), q, 63, MU);
            w.Box(corner - along * (sgn * th * 0.5f) - into * (leg * 0.5f - th), new Vector3(th, hh, leg), q, 63, MU);
            Saw(corner + into * 0.02f, into, "corner_guard");
        }

        /// <summary>A box open at the front, standing out from a wall: its outside, rim and inside. c: the middle of its back, on the wall.</summary>
        static void OpenBox(MeshWriter w, Vector3 c, Vector3 rv, Vector3 into, float hw, float hh, float depth, float th)
        {
            var up = Vector3.up;
            Vector3 P(float u, float v, float d) => c + rv * u + up * v + into * d;
            void Ring(float ou, float ov, float d0, float d1, bool outward)
            {
                // Four sides between depths d0 and d1 at half sizes ou, ov.
                var n0 = outward ? up : -up;
                w.Quad(P(-ou, ov, d0), P(ou, ov, d0), P(ou, ov, d1), P(-ou, ov, d1), n0, MU);
                w.Quad(P(-ou, -ov, d0), P(ou, -ov, d0), P(ou, -ov, d1), P(-ou, -ov, d1), -n0, MU);
                var n1 = outward ? rv : -rv;
                w.Quad(P(ou, -ov, d0), P(ou, ov, d0), P(ou, ov, d1), P(ou, -ov, d1), n1, MU);
                w.Quad(P(-ou, -ov, d0), P(-ou, ov, d0), P(-ou, ov, d1), P(-ou, -ov, d1), -n1, MU);
            }
            Ring(hw, hh, 0f, depth, true);
            Ring(hw - th, hh - th, th, depth, false);
            // The rim at the front.
            float iu = hw - th, iv = hh - th;
            w.Quad(P(-hw, iv, depth), P(hw, iv, depth), P(hw, hh, depth), P(-hw, hh, depth), into, MU);
            w.Quad(P(-hw, -hh, depth), P(hw, -hh, depth), P(hw, -iv, depth), P(-hw, -iv, depth), into, MU);
            w.Quad(P(-hw, -iv, depth), P(-iu, -iv, depth), P(-iu, iv, depth), P(-hw, iv, depth), into, MU);
            w.Quad(P(iu, -iv, depth), P(hw, -iv, depth), P(hw, iv, depth), P(iu, iv, depth), into, MU);
            // The back inside.
            w.Quad(P(-iu, -iv, th), P(iu, -iv, th), P(iu, iv, th), P(-iu, iv, th), into, MU);
        }
    }
}
