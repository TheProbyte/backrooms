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
    /// The models (Tools/Blender/build_fittings.py, FittingModels: real sizes, bevelled, printed labels) are copied into the
    /// chunk's own meshes; the alcove's walls are built here in the wall's finish; colliders for what stands out; grime
    /// where hands and carts have been.
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
            if (r.custom || r.kind == RoomKind.Crawl || r.special != GoodRoom.None || !FurnitureMaterials.Ready || !FittingMaterials.Ready || !FittingModels.Ready) return;
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

        // ---------------- Models ----------------

        /// <summary>
        /// Copies a modelled fitting (FittingModels) into this chunk's meshes at a chunk-local position and rotation (its
        /// +Z toward the room for wall things). remap: a slot's material instead of its own (null keeps it).
        /// </summary>
        /// <summary>A model scaled to a size (each axis its own scale).</summary>
        bool ModelScaled(string name, uint m, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var parts = FittingModels.Get(name);
            if (parts == null) return false;
            foreach (var p in parts)
                if (p.material) WMask(p.material, m).AddMesh(p.vertices, p.normals, p.tangents, p.uv, p.triangles, pos, rot, scale);
            return true;
        }

        bool Model(string name, uint m, Vector3 pos, Quaternion rot, System.Func<string, Material> remap = null)
        {
            var parts = FittingModels.Get(name);
            if (parts == null) return false;
            foreach (var p in parts)
            {
                var mat = remap != null ? remap(p.slot) ?? p.material : p.material;
                if (!mat) continue;
                WMask(mat, m).AddMesh(p.vertices, p.normals, p.tangents, p.uv, p.triangles, pos, rot);
            }
            return true;
        }

        // ---------------- Fire ----------------

        /// <summary>An extinguisher in its cabinet (the glass broken out of the door), or hung on its hook; empty: the cabinet
        /// or the hook alone. Its red sign above. foot: on the wall face at floor level.</summary>
        void Extinguisher(Vector3 foot, Vector3 into, Vector3 rv, uint m, bool cabinet, bool empty, bool redCabinet)
        {
            var up = Vector3.up;
            var q = Quaternion.LookRotation(into);
            float signY;
            if (cabinet)
            {
                const float y0 = 0.88f;
                Model("ExtinguisherCabinet", m, foot + up * y0, q, s => s == "CabinetPaint" ? (redCabinet ? FittingMaterials.ForSlot("ExtRed") : null) : null);
                // Standing in it turned a little (its handle is longer than the cabinet is deep), label toward the room.
                if (!empty) Model("Extinguisher", m, foot + up * (y0 + 0.012f) + into * 0.072f + rv * 0.01f, q * Quaternion.Euler(0f, -50f, 0f));
                WallBox(foot + up * (y0 + 0.31f) + into * 0.075f, rv, into, 0.3f, 0.62f, 0.15f);
                signY = y0 + 0.62f + 0.12f;
            }
            else
            {
                // On its hook at 1.2 m, the handle over it, the body hanging close to the wall.
                Model("ExtinguisherBracket", m, foot + up * 1.2f, q);
                if (!empty) Model("Extinguisher", m, foot + up * (1.2f - 0.408f) + into * 0.072f, q);
                signY = 1.72f;
            }
            var sign = WMask(FittingMaterials.SignExtinguisher, m);
            Vector3 sc = foot + up * signY + into * 0.003f;
            const float shw = 0.13f, shh = 0.065f;
            sign.Quad(sc - rv * shw - up * shh, sc + rv * shw - up * shh, sc + rv * shw + up * shh, sc - rv * shw + up * shh, into,
                      new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f));
            WMask(FittingMaterials.RedPaint, m).Box(sc - into * 0.0015f, new Vector3(shw * 2f + 0.006f, shh * 2f + 0.006f, 0.003f), q, 1 | 2 | 4 | 8, MU);
        }

        /// <summary>A pull station with its handle at 1.2 m (its middle at 1.17 m). foot: on the wall face at floor level.</summary>
        void PullStation(Vector3 foot, Vector3 into, Vector3 rv, uint m)
        {
            var c = foot + Vector3.up * 1.17f;
            Model("PullStation", m, c, Quaternion.LookRotation(into));
            // Hands on it over the years: a grubby patch on the wall round it.
            _wallWear.Rect(c + into * 0.0013f, rv, Vector3.up, 0.11f, 0.13f, new Color32(150, 140, 126, 90), new Vector2(0.2f, 0.2f));
        }

        /// <summary>A horn strobe, dead. c: on the wall face at its middle.</summary>
        void Strobe(Vector3 c, Vector3 into, Vector3 rv, uint m) => Model("HornStrobe", m, c, Quaternion.LookRotation(into));

        // ---------------- Air ----------------

        /// <summary>A thermostat at 1.45 m: the beige box or the round dial; the wall round it grubby from hands.</summary>
        void Thermostat(Vector3 foot, Vector3 into, Vector3 rv, uint m, bool box)
        {
            var c = foot + Vector3.up * 1.45f;
            Model(box ? "ThermostatBox" : "ThermostatRound", m, c, Quaternion.LookRotation(into));
            _wallWear.Rect(c + into * 0.0013f + rv * 0.03f, rv, Vector3.up, 0.12f, 0.1f, new Color32(150, 140, 126, 80), new Vector2(0.15f, 0.15f));
        }

        /// <summary>A low return-air grille just above the baseboard; dust caught along its louvres' wall below.</summary>
        void LowVent(Vector3 foot, Vector3 into, Vector3 rv, uint m, float yb)
        {
            var c = foot + Vector3.up * (yb + 0.04f + 0.08f);
            Model("ReturnGrille", m, c, Quaternion.LookRotation(into));
            _wallWear.Rect(c + into * 0.0013f + Vector3.up * 0.1f, rv, Vector3.up, 0.18f, 0.06f, new Color32(128, 120, 108, 110), new Vector2(0.5f, 0.2f));
        }

        // ---------------- Water ----------------

        /// <summary>A drinking fountain in a built-out alcove at the middle of the face (wing walls and a soffit in the wall's finish).</summary>
        void Fountain(Vector3 face, Vector3 into, Vector3 rv, Room r, uint m)
        {
            var up = Vector3.up;
            var q = Quaternion.LookRotation(into);
            var wallMat = _a.WallMaterial(r);
            var wall = WMask(wallMat ? wallMat : FurnitureMaterials.Enamel, m);
            const float depth = 0.36f, wing = 0.11f, half = 0.47f, height = 2.05f, soffit = 0.35f;
            for (int s = -1; s <= 1; s += 2)
            {
                var wc = face + rv * (s * half) + up * (height * 0.5f) + into * (depth * 0.5f);
                wall.Box(wc, new Vector3(wing, height, depth), q, 63 & ~16 & ~4, MeshWriter.WallUV);
                WallBox(wc, rv, into, wing, height, depth);
                // A baseboard round the wing's foot.
                if (!NoBaseboard && _a.baseboard)
                    WMask(_a.baseboard, m).Box(wc + up * (BB * 0.5f - height * 0.5f), new Vector3(wing + 2f * BD, BB, depth + BD), q, 63 & ~16 & ~4, MeshWriter.MetreUV);
            }
            wall.Box(face + up * (height - soffit * 0.5f) + into * (depth * 0.5f), new Vector3(2f * half - wing, soffit, depth), q, 4 | 8 | 32, MeshWriter.WallUV);
            Model("DrinkingFountain", m, face, q);
            WallBox(face + up * 0.68f + into * 0.16f, rv, into, 0.4f, 0.4f, 0.32f);
            // Water down the wall under it, long dried; the wall grubby where people lean to drink.
            _wallWear.Rect(face + up * 0.42f + into * 0.0013f, rv, up, 0.1f, 0.24f, new Color32(160, 148, 120, 130), new Vector2(0.4f, 0.5f));
            _wallWear.Rect(face + up * 1.08f + into * 0.0013f, rv, up, 0.26f, 0.12f, new Color32(150, 140, 126, 70), new Vector2(0.3f, 0.3f));
        }

        // ---------------- Windows ----------------

        /// <summary>A window filled in (the model: frame, bead, board with its joints and screws, stool and apron). head: the
        /// top of the frame; the frame is 1.12 m tall.</summary>
        void FilledWindow(Vector3 face, Vector3 along, Vector3 into, Vector3 rv, uint m, float head, int seed)
        {
            var rng = new System.Random(seed);
            int kind = rng.Next(3);
            float sill = head - 1.12f;
            if (sill < 0.6f) return;
            var q = Quaternion.LookRotation(into);
            Model(kind == 0 ? "FilledWindow" : kind == 1 ? "FilledWindowMullion" : "FilledWindowCross", m, face + Vector3.up * sill, q);
            WallBox(face + Vector3.up * (sill - 0.014f) + into * 0.065f, rv, into, 1.05f, 0.028f, 0.13f);
            // Where the glass met the frame, dirt along the bead.
            _wallWear.Rect(face + Vector3.up * (sill + 0.56f) + into * 0.041f, rv, Vector3.up, 0.47f, 0.56f, new Color32(170, 162, 148, 60), new Vector2(0.9f, 0.9f));
        }

        // ---------------- Corners ----------------

        /// <summary>A corner guard on a jamb's corner (end k of the face); scuffs on the wall beside it, where carts hit.</summary>
        void CornerGuard(Vector3 face, Vector3 along, Vector3 into, int k, float height, float yb, uint m, bool steel)
        {
            float ae = k == 0 ? -(C * 0.5f + T * 0.5f) : C * 0.5f + T * 0.5f, sgn = k == 0 ? 1f : -1f;
            var q = Quaternion.LookRotation(into);
            // The model's leg on the wall runs along its +X (R) or -X (L): toward the face, back from the corner.
            Vector3 mx = q * Vector3.right;
            bool right = Vector3.Dot(mx, along * sgn) > 0f;
            var corner = face + along * ae + Vector3.up * (yb + 0.01f);
            Model(right ? "CornerGuardR" : "CornerGuardL", m, corner, q, s => steel ? null : FurnitureMaterials.Beige);
            for (int i = 0; i < 3; i++)
            {
                float y = 0.25f + 0.25f * i + 0.1f * Mathf.Sin(ae * 13f + i);
                _wallWear.Rect(corner + along * (sgn * (0.07f + 0.04f * i)) + Vector3.up * y + into * 0.0013f, along, Vector3.up, 0.03f + 0.02f * i, 0.004f,
                               new Color32(60, 56, 52, 150), new Vector2(0.4f, 0.1f));
            }
            Saw(corner + Vector3.up * 0.6f + into * 0.02f, into, "corner_guard");
        }
    }
}
