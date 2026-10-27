using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Kane's reference room (Docs/ReferenceMatch.md) rebuilt as a room of the procedural world: walls on the 1.2 m grid,
    /// the world's own materials, square ceiling panels as real fixtures. Escape > Reference room forces it into the world
    /// beside the spawn hall (the same path as build mode's "Test in world"), so it is built by the chunk builder and lit by
    /// exactly the game's lighting: real panel lights with shadows, the light grid, traced bounce, contact shadows, haze.
    /// Whatever is tuned against the reference there carries over to every generated room.
    ///
    /// Compared with the photo: the left wall is straight (the photo's is slanted; the world has no slanted walls), the
    /// opening is three cells (3.6 m, the photo's 3.3 m) under the world's 2.1 m doorway header (the photo's 2.35 m), and
    /// its deep reveals are 0.5 m pillars at the jambs. The camera stands where it stood in the hand-built room, 5.2 m from
    /// the opening wall.
    /// </summary>
    public static class ReferenceBlueprint
    {
        public const string Id = "reference-match";
        const float C = OfficeBiome.CellSize;

        // The "frame": metres, x across the view, z away from the camera, as in the hand-built room. The blueprint covers
        // x -6..6 (10 cells) and z -3.6..13.2 (14 cells); the opening wall is the line z = 4.8, the back wall z = 13.2.
        public const float MinX = -6f, MinZ = -3.6f;
        const int Width = 10, Depth = 14;

        /// <summary>The camera's feet in the frame, and its yaw and pitch (degrees, pitch down positive).</summary>
        public static readonly Vector3 Camera = new Vector3(0.4f, 0f, -0.4f);
        public const float Yaw = -5f, Pitch = 25f;

        public static RoomBlueprint Create()
        {
            var bp = new RoomBlueprint { id = Id, name = "Reference Match (Kane)", enabled = false, allowRotate = false };
            var v = RoomVariant.CreateDefault(Width, Depth, "Reference");
            v.fixtures.Clear();
            var front = new RoomSettings { name = "Front room", kind = RoomKind.Office };
            var back = new RoomSettings { name = "Back room", kind = RoomKind.Office };
            var closed = new RoomSettings { name = "Closed", kind = RoomKind.Office, lights = LightMode.Dark };
            v.rooms.Clear(); v.rooms.Add(front); v.rooms.Add(back); v.rooms.Add(closed);
            for (int z = 0; z < Depth; z++)
            for (int x = 0; x < Width; x++)
                v.cellRoom[z * Width + x] = z >= 7 ? 1 : x >= 2 ? 0 : 2;

            // Perimeter: walls (CreateDefault), the exit doorway behind the camera.
            for (int x = 0; x < Width; x++) v.SetH(x, 0, EdgeCode.Wall);
            v.SetH(5, 0, EdgeCode.Header);
            // The opening wall (z = 4.8): a three-cell opening under one continuous header, x -2.4..1.2.
            for (int x = 0; x < Width; x++) v.SetH(x, 7, x >= 3 && x <= 5 ? EdgeCode.Header : EdgeCode.Wall);
            // The front room's left wall (x = -3.6); the closed space behind it is never seen.
            for (int z = 0; z < 7; z++) v.SetV(2, z, EdgeCode.Wall);

            // The opening's deep reveals, and the two supports on the back wall.
            v.pillars.Add(new PillarDef { x = 3, z = 7, size = 0.5f });
            v.pillars.Add(new PillarDef { x = 6, z = 7, size = 0.5f });
            v.pillars.Add(new PillarDef { x = 4, z = Depth, size = 0.45f });
            v.pillars.Add(new PillarDef { x = 6, z = Depth, size = 0.45f });

            // Front room: panels out of view above the camera; on the left only the far one, 1.5 m from the left wall (the
            // photo's left wall is lit while its floor is darker than the right, measured 0.23 against 0.45). Back room: five
            // columns by two rows (measured through the opening: 3 x 3 was 0.34, 5 x 3 0.56, the photo 0.43), the last row
            // 1.8 m from the back wall (closer, each panel throws a bright scallop on it; the photo's back wall is even).
            foreach (var (x, z) in new[] { (5, 3), (7, 3), (9, 3), (3, 5), (5, 5), (7, 5), (9, 5) }) v.fixtures.Add(new FixtureDef { x = x, z = z });
            foreach (int x in new[] { 1, 3, 5, 7, 9 }) foreach (int z in new[] { 9, 12 }) v.fixtures.Add(new FixtureDef { x = x, z = z });

            bp.variants.Add(v);
            bp.Validate();
            return bp;
        }

        /// <summary>A point of the frame in blueprint metres (from the blueprint's south-west corner).</summary>
        public static Vector3 ToBlueprint(Vector3 frame) => new Vector3(frame.x - MinX, frame.y, frame.z - MinZ);
    }
}
