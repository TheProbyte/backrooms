using System;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// Rendering-layer bookkeeping for unshadowed lights. Each light zone (rooms joined by open-plan
    /// edges) has a colour; a surface only receives lights whose layer bit it carries, so an unshadowed
    /// light cannot shine through a wall into a room of another colour.
    /// Bit 0 is Unity's "Default" layer: every renderer has it, no fixture light uses it.
    /// </summary>
    public static class LightLayers
    {
        /// <summary>Colours per chunk colour set.</summary>
        public const int Colors = 7;

        /// <summary>Colour sets: chunks use set (x &amp; 1) + 2 (z &amp; 1), so all eight neighbours differ.</summary>
        public const int Sets = 4;

        /// <summary>Rendering layers used by lights (bits 1..28; bit 0 is Default).</summary>
        public const int LayerCount = Colors * Sets;

        /// <summary>Renderer mask for a room colour (always includes Default).</summary>
        public static uint LayerBit(int color) => color < 0 ? 1u : (1u | (1u << (color + 1)));

        /// <summary>
        /// Layer mask per cell: its zone's colour, plus the colour of any open-plan zone it continues into
        /// across a chunk border (so lights on the far side light the shared floor). The extra layer is
        /// given to that zone only, never to other rooms that happen to share its colour.
        /// </summary>
        /// <param name="neighbour">Layout of the chunk at (dx, dz) from this one, or null if unknown.</param>
        public static uint[,] CellMasks(OfficeLayout L, Func<int, int, OfficeLayout> neighbour)
        {
            int n = L.N, R = L.rooms.Count;
            var mask = new uint[n, n];
            // Light zones inside the chunk (rooms joined by open edges).
            var parent = new int[R];
            for (int i = 0; i < R; i++) parent[i] = i;
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                if (x + 1 < n && L.vEdges[x + 1, z] == EdgeKind.Open) parent[Find(L.roomOf[x, z])] = Find(L.roomOf[x + 1, z]);
                if (z + 1 < n && L.hEdges[x, z + 1] == EdgeKind.Open) parent[Find(L.roomOf[x, z])] = Find(L.roomOf[x, z + 1]);
            }
            var extra = new uint[R];
            if (neighbour != null)
            {
                void Link(int x, int z, EdgeKind e, int dx, int dz)
                {
                    if (e != EdgeKind.Open) return;
                    var nl = neighbour(dx, dz);
                    if (nl == null) return;
                    int nx = dx < 0 ? n - 1 : dx > 0 ? 0 : x, nz = dz < 0 ? n - 1 : dz > 0 ? 0 : z;
                    if (nl.EdgeOf(nx, nz, Opposite(dx, dz)) != EdgeKind.Open) return;   // both copies must be open
                    int nc = nl.RoomAt(nx, nz).color;
                    if (nc >= 0) extra[Find(L.roomOf[x, z])] |= 1u << (nc + 1);
                }
                for (int i = 0; i < n; i++)
                {
                    Link(0, i, L.vEdges[0, i], -1, 0);
                    Link(n - 1, i, L.vEdges[n, i], 1, 0);
                    Link(i, 0, L.hEdges[i, 0], 0, -1);
                    Link(i, n - 1, L.hEdges[i, n], 0, 1);
                }
            }
            for (int x = 0; x < n; x++)
            for (int z = 0; z < n; z++)
            {
                int room = L.roomOf[x, z];
                mask[x, z] = LayerBit(L.rooms[room].color) | extra[Find(room)];
            }
            return mask;
        }

        /// <summary>Side of the neighbouring cell that faces back across a step of (dx, dz).</summary>
        static int Opposite(int dx, int dz) => dx < 0 ? 1 : dx > 0 ? 0 : dz < 0 ? 3 : 2;

        // ---- Light model (defaults of FixtureLightManager; URP's falloff) ----

        public const float TubeRange = 6f, BounceRange = 4.4f, DoorwayRange = 4.5f, LampRange = 5f;
        public const float TubeIntensity = 7f, LampIntensity = 1.8f, DoorwayMaxIntensity = 2.5f * 0.55f, DomeHeightBoost = 0.3f;

        /// <summary>
        /// Irradiance (linear, on a surface facing the light) below which a light is treated as not reaching a
        /// surface. Directly under a 2.7 m troffer it is about 2.4, so this is about 2% of that.
        /// </summary>
        public const float VisibleIrradiance = 0.05f;

        /// <summary>One unshadowed light, as URP evaluates it.</summary>
        public struct Spec
        {
            public Vector3 position;
            public Vector3 direction;      // spot axis (unit); zero = point light
            public float intensity, range;
            public float cosOuter, cosInner;
        }

        /// <summary>Range of a ceiling fixture's main light (domes and strips throw farther in tall rooms).</summary>
        public static float FixtureRange(in Fixture f)
        {
            float extra = f.height - OfficeBiome.CeilingHeight;
            return f.dome || f.strip ? TubeRange + extra * 1.3f : TubeRange;
        }

        /// <summary>The main light of a ceiling fixture at chunk-local cell centre (metres), as FixtureLightManager sets it up.</summary>
        public static Spec FixtureSpec(in Fixture f, Vector2 centre)
        {
            float extra = f.height - OfficeBiome.CeilingHeight;
            float boost = f.dome || f.strip ? 1f + extra * DomeHeightBoost : 1f;
            if (f.dome) boost *= 1.6f;
            float outer = f.dome ? 170f : f.strip ? 160f : 150f, inner = f.dome ? 40f : 60f;
            float y = f.height - (f.dome ? 0.07f : f.strip ? 0.03f : 0f) - 0.06f;
            return new Spec
            {
                position = new Vector3(centre.x, y, centre.y), direction = Vector3.down, intensity = TubeIntensity * boost, range = FixtureRange(f),
                cosOuter = Mathf.Cos(outer * 0.5f * Mathf.Deg2Rad), cosInner = Mathf.Cos(inner * 0.5f * Mathf.Deg2Rad),
            };
        }

        /// <summary>The soft upward bounce fill under a ceiling fixture (same layer as the fixture).</summary>
        public static Spec BounceSpec(in Fixture f, Vector2 centre)
        {
            float drop = f.dome ? Mathf.Min(f.height * 0.45f, 2.2f) : 1.1f;
            return new Spec
            {
                position = new Vector3(centre.x, f.height - drop, centre.y), direction = Vector3.up,
                intensity = 0.8f * (f.dome ? 1.6f : 1f), range = f.dome || f.strip ? BounceRange * 1.6f : BounceRange,
                cosOuter = Mathf.Cos(86f * Mathf.Deg2Rad), cosInner = Mathf.Cos(55f * Mathf.Deg2Rad),
            };
        }

        public static Spec LampSpec(Vector2 at) => new Spec { position = new Vector3(at.x, 1.48f, at.y), intensity = LampIntensity, range = LampRange };

        /// <summary>A doorway light at full strength (the manager scales it by the lit fixtures behind the opening).</summary>
        public static Spec DoorwaySpec(Vector2 onEdge, Vector2 inward, float top)
        {
            var dir = (new Vector3(inward.x, 0f, inward.y) + Vector3.down * 0.9f).normalized;
            return new Spec
            {
                position = new Vector3(onEdge.x + inward.x * 0.05f, top - 0.3f, onEdge.y + inward.y * 0.05f), direction = dir,
                intensity = DoorwayMaxIntensity, range = DoorwayRange,
                cosOuter = Mathf.Cos(75f * Mathf.Deg2Rad), cosInner = Mathf.Cos(20f * Mathf.Deg2Rad),
            };
        }

        /// <summary>URP distance attenuation: inverse square, smoothly reaching zero at the range.</summary>
        public static float DistanceAttenuation(float distSqr, float range)
        {
            float factor = distSqr / (range * range);
            float smooth = Mathf.Clamp01(1f - factor * factor);
            return smooth * smooth / Mathf.Max(distSqr, 0.0001f);
        }

        /// <summary>URP spot attenuation.</summary>
        public static float AngleAttenuation(float cosAngle, float cosOuter, float cosInner)
        {
            float inv = 1f / Mathf.Max(cosInner - cosOuter, 0.001f);
            float a = Mathf.Clamp01(cosAngle * inv - cosOuter * inv);
            return a * a;
        }

        /// <summary>Irradiance at a point on a surface facing the light (no occlusion).</summary>
        public static float Irradiance(in Spec s, Vector3 p)
        {
            Vector3 d = p - s.position;
            float d2 = d.sqrMagnitude;
            if (d2 >= s.range * s.range) return 0f;
            float k = s.intensity * DistanceAttenuation(d2, s.range);
            if (s.direction != Vector3.zero) k *= AngleAttenuation(Vector3.Dot(s.direction, d / Mathf.Sqrt(Mathf.Max(d2, 1e-8f))), s.cosOuter, s.cosInner);
            return k;
        }

        /// <summary>
        /// Brightest point a light puts on the surfaces of a cell column (floor, or walls up to the ceiling):
        /// the nearest point of the cell, sampled at a few heights.
        /// </summary>
        public static float MaxOnCell(in Spec s, float x0, float z0, float size, float ceiling)
        {
            float nx = Mathf.Clamp(s.position.x, x0, x0 + size), nz = Mathf.Clamp(s.position.z, z0, z0 + size);
            float best = 0f;
            for (int i = 0; i <= 6; i++)
                best = Mathf.Max(best, Irradiance(s, new Vector3(nx, ceiling * i / 6f, nz)));
            return best;
        }
    }
}
