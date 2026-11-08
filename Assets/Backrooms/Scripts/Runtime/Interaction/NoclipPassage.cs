using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// One noclip door in a loaded chunk (generation: Generation.NoclipDoors): where its opening is and where it leads.
    /// <see cref="NoclipTravel"/> watches the camera cross it. Its look is separate (<see cref="NoclipDoorMarking"/>).
    /// Frame: <see cref="centre"/> is the middle of the wall line at floor level, <see cref="normal"/> one of its two faces.
    /// </summary>
    public class NoclipPassage : MonoBehaviour
    {
        public static readonly List<NoclipPassage> All = new List<NoclipPassage>();

        public Vector3 centre, normal;
        public ulong id;
        /// <summary>A Poolrooms door: it leads back to the door the player came in by.</summary>
        public bool arrival;
        public bool hasPartner;
        public Vector3 partnerCentre, partnerNormal;
        public bool inPoolrooms;
        /// <summary>A seam's tear (Docs/ContractFloorsPlan.md): narrower than a door; shut from closing time (<see cref="SeamsShut"/>).</summary>
        public bool seam;
        public static bool SeamsShut;

        public float Width => seam ? NoclipDoors.SeamWidth : NoclipDoors.Width;
        public float Height => seam ? NoclipDoors.SeamHeight : NoclipDoors.Height;
        public Vector3 Right => Vector3.Cross(Vector3.up, normal);

        public void Set(NoclipDoor d, bool pool)
        {
            centre = d.position; normal = d.normal; id = d.id; arrival = d.arrival; inPoolrooms = pool; seam = d.seam;
            hasPartner = d.resolved; partnerCentre = d.partnerPosition; partnerNormal = d.partnerNormal;
        }

        void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>Signed distance of a point from the wall line (positive on the normal's side), and whether it is within the opening.</summary>
        public float Distance(Vector3 p, out bool inOpening, float margin = 0.1f)
        {
            Vector3 d = p - centre;
            float lateral = Vector3.Dot(d, Right), up = d.y;
            inOpening = Mathf.Abs(lateral) < Width * 0.5f + margin && up > -0.2f && up < Height + margin;
            return Vector3.Dot(d, normal);
        }
    }

    /// <summary>
    /// How noclip doors are marked, kept apart from what they do so it can be swapped for something subtler later.
    /// For now: <see cref="TapeOutline"/>, a rough door outline in blue painter's tape on both faces of the wall.
    /// </summary>
    public static class NoclipDoorMarking
    {
        public interface IMarker { void Mark(NoclipPassage door); }

        /// <summary>The marking in use (set to null for unmarked doors).</summary>
        public static IMarker Current = new TapeOutline();

        /// <summary>A seam's marking (Interaction.Seam): silver duct tape, not the blue painter's tape.</summary>
        public static IMarker Seams = new DuctTape();

        public static void Mark(NoclipPassage door) { if (door.seam) Seams?.Mark(door); else Current?.Mark(door); }

        /// <summary>
        /// A seam's doorway (2026-11-07, the owner: "just duct taped doorway zones"): Overflow's crew taped it out in silver
        /// duct tape, two strips up each side laid over each other a little apart, two across the top, and one across the
        /// middle at chest height for the company's label (Interaction.Seam puts it on), on both faces of the wall.
        /// </summary>
        public class DuctTape : IMarker
        {
            public static readonly Color32 Silver = new Color32(204, 188, 150, 255);      // (warm: the camcorder's grade turns a true grey blue)
            const float Wd = 0.05f + 2f * Marks.TapeMargin;

            public void Mark(NoclipPassage door)
            {
                var batch = Marks.NewBatch("DuctTape", door.transform);
                var rng = new System.Random(unchecked((int)(door.id ^ (door.id >> 32)) * 31 + 7));
                float J() => ((float)rng.NextDouble() - 0.5f) * 2f;
                float half = door.Width * 0.5f + 0.025f, top = door.Height + 0.025f;
                float T = OfficeBiome.WallThickness;
                for (int face = -1; face <= 1; face += 2)
                {
                    Vector3 n = door.normal * face, r = Vector3.Cross(Vector3.up, n), up = Vector3.up;
                    Vector3 o = door.centre + n * (T * 0.5f);
                    Vector3 P(float x, float y) => o + r * x + up * y;
                    var c2 = new Color32((byte)(Silver.r - 12), (byte)(Silver.g - 12), (byte)(Silver.b - 10), 255);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        float x = s * half;
                        Marks.Strip(batch, P(x + J() * 0.008f, 0.01f), P(x + J() * 0.014f, top + 0.04f + J() * 0.015f), n, Wd, Marks.KindTape, Silver);
                        Marks.Strip(batch, P(x + s * 0.035f + J() * 0.01f, 0.012f), P(x + s * 0.03f + J() * 0.016f, top * 0.62f + J() * 0.2f), n, Wd, Marks.KindTape, c2);
                    }
                    Marks.Strip(batch, P(-half - 0.07f - J() * 0.02f, top + J() * 0.01f), P(half + 0.07f + J() * 0.02f, top + J() * 0.012f), n, Wd, Marks.KindTape, Silver);
                    Marks.Strip(batch, P(-half - 0.03f - J() * 0.02f, top + 0.045f + J() * 0.01f), P(half * 0.4f + J() * 0.08f, top + 0.05f + J() * 0.012f), n, Wd, Marks.KindTape, c2);
                    // Across at chest height, under the label.
                    Marks.Strip(batch, P(-half - 0.02f, 1.42f + J() * 0.01f), P(half + 0.02f, 1.42f + J() * 0.012f), n, Wd, Marks.KindTape, Silver);
                }
                Marks.Upload(batch);
            }
        }

        /// <summary>
        /// Two strips up the sides and one across the top, just outside the opening, overlapping a little at the corners,
        /// each a touch crooked like tape put up by hand (the same marks as the tape roll: <see cref="Marks"/>).
        /// </summary>
        public class TapeOutline : IMarker
        {
            public void Mark(NoclipPassage door)
            {
                var batch = Marks.NewBatch("TapeOutline", door.transform);
                var rng = new System.Random(unchecked((int)(door.id ^ (door.id >> 32))));
                float J() => ((float)rng.NextDouble() - 0.5f) * 2f;
                float half = door.Width * 0.5f + Marks.TapeWidth * 0.5f, top = door.Height + Marks.TapeWidth * 0.5f;
                float T = OfficeBiome.WallThickness;
                for (int face = -1; face <= 1; face += 2)
                {
                    Vector3 n = door.normal * face, r = Vector3.Cross(Vector3.up, n), up = Vector3.up;
                    Vector3 o = door.centre + n * (T * 0.5f);
                    Vector3 P(float x, float y) => o + r * x + up * y;
                    // Left and right uprights, from just above the floor to a little past the top strip.
                    Marks.Strip(batch, P(-half + J() * 0.006f, 0.015f + J() * 0.004f), P(-half + J() * 0.012f, top + 0.03f + J() * 0.012f), n, Marks.TapeStripWidth, Marks.KindTape, Marks.TapeColor);
                    Marks.Strip(batch, P(half + J() * 0.006f, 0.015f + J() * 0.004f), P(half + J() * 0.012f, top + 0.03f + J() * 0.012f), n, Marks.TapeStripWidth, Marks.KindTape, Marks.TapeColor);
                    // Across the top, overhanging both uprights.
                    Marks.Strip(batch, P(-half - 0.05f - J() * 0.015f, top + J() * 0.008f), P(half + 0.05f + J() * 0.015f, top + J() * 0.012f), n, Marks.TapeStripWidth, Marks.KindTape, Marks.TapeColor);
                }
                Marks.Upload(batch);
            }
        }
    }
}
