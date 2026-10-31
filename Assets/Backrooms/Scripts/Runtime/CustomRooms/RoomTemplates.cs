using System;
using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>Rooms to start from (build mode's NEW ROOM): each made in code with the builder's own operations.</summary>
    public static class RoomTemplates
    {
        public sealed class Template { public string name, about; public Func<RoomBlueprint> make; }

        const float C = OfficeBiome.CellSize;

        public static readonly List<Template> All = new List<Template>
        {
            new Template { name = "Empty office", about = "Four walls, a door, the usual lights. Start from nothing.", make = () => RoomBlueprint.CreateNew("New room") },
            new Template { name = "Furnished lounge", about = "Painted walls, a floral sofa, armchairs, lamps, a notice and a buzzing light.", make = Lounge },
            new Template { name = "Maze of hallways", about = "Narrow wallpapered corridors that double back; some lights dead, some failing.", make = Maze },
            new Template { name = "Warehouse", about = "A tall hall with racks along the walls, pallets and boxes.", make = Warehouse },
            new Template { name = "L-shaped office", about = "An office with a corner cut away (the world's hallway wraps round it), a door into the notch.", make = LShape },
            new Template { name = "Endless hallway", about = "A long hallway whose far door brings you back in at the near one: it never ends.", make = Endless },
            new Template { name = "Something is wrong", about = "Walk in and the door slams; the lights go; knocking. Furniture on the ceiling, a mess.", make = Haunted },
        };

        static RoomBlueprint New(string name, int w, int d)
        {
            var bp = RoomBlueprint.CreateNew(name);
            var v = bp.variants[0];
            v.Resize(w, d);
            v.openings.Clear();
            v.ConvertGridOpenings();
            v.openings.Clear();
            v.WallPerimeter();
            v.AutoFixtures();
            return bp;
        }

        static OpeningDef Door(bool vertical, int line, float centre, float width = 0.9f) =>
            new OpeningDef { vertical = vertical, line = line, from = centre - width * 0.5f, to = centre + width * 0.5f, top = 2.1f };

        static PropDef Prop(PropKind k, float x, float z, float yaw, Vector3 size, int style = 0) =>
            new PropDef { kind = k, x = x, z = z, yaw = yaw, size = size, style = style, variant = UnityEngine.Random.Range(0, 1 << 16), lit = true };

        static RoomBlueprint Lounge()
        {
            var bp = New("Lounge", 7, 6);
            var v = bp.variants[0];
            var r = v.rooms[0]; r.name = "Lounge"; r.kind = RoomKind.Lounge; r.painted = true; r.wallFinish = WallFinish.PaintedPlaster;
            v.openings.Add(Door(false, 0, 3.5f * C));
            v.props.Add(Prop(PropKind.Sofa, 3.5f * C, 6 * C - 0.55f, 180f, new Vector3(2.25f, 0.94f, 0.93f), Generation.Prop.FloralSofaStyle));
            v.props.Add(Prop(PropKind.Armchair, 1.1f, 3.6f, 110f, new Vector3(0.95f, 0.85f, 0.9f), 1));
            v.props.Add(Prop(PropKind.Armchair, 7 * C - 1.1f, 3.6f, 250f, new Vector3(0.95f, 0.85f, 0.9f), 2));
            v.props.Add(Prop(PropKind.FloorLamp, 0.45f, 6 * C - 0.45f, 0f, new Vector3(0.4f, 1.6f, 0.4f)));
            v.props.Add(Prop(PropKind.FloorLamp, 7 * C - 0.45f, 6 * C - 0.45f, 0f, new Vector3(0.4f, 1.6f, 0.4f)));
            v.decals.Add(new DecalDef { vertical = true, line = 0, side = 1, along = 3.6f, height = 1.5f, size = 0.3f, kind = DecalKind.Notice, seed = 11 });
            v.decals.Add(new DecalDef { vertical = true, line = 7, side = -1, along = 2.0f, height = 2.2f, size = 0.45f, kind = DecalKind.WaterStain, seed = 12 });
            v.decals.Add(new DecalDef { vertical = false, line = 0, side = 1, along = 2.2f, height = 1.2f, size = 0.06f, kind = DecalKind.Switch, seed = 13 });
            if (v.fixtures.Count > 0) { v.fixtures[0].buzz = true; v.fixtures[0].mood = 3; }
            return bp;
        }

        static RoomBlueprint Maze()
        {
            const int N = 9;
            var bp = New("Maze", N, N);
            var v = bp.variants[0];
            v.rooms[0].name = "Maze"; v.rooms[0].kind = RoomKind.Hallway;
            // Every inside edge a wall, then a winding path carved through (a depth-first maze), and a few walls more knocked out.
            for (int x = 1; x < N; x++) for (int z = 0; z < N; z++) v.SetV(x, z, EdgeCode.Wall);
            for (int x = 0; x < N; x++) for (int z = 1; z < N; z++) v.SetH(x, z, EdgeCode.Wall);
            var rng = new System.Random(1234);
            var seen = new bool[N, N]; var stack = new Stack<Vector2Int>();
            stack.Push(new Vector2Int(N / 2, 0)); seen[N / 2, 0] = true;
            while (stack.Count > 0)
            {
                var c = stack.Peek();
                var next = new List<Vector2Int>();
                foreach (var d in new[] { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up })
                { var n = c + d; if (n.x >= 0 && n.y >= 0 && n.x < N && n.y < N && !seen[n.x, n.y]) next.Add(n); }
                if (next.Count == 0) { stack.Pop(); continue; }
                var to = next[rng.Next(next.Count)];
                if (to.x != c.x) v.SetV(Mathf.Max(to.x, c.x), c.y, EdgeCode.Open); else v.SetH(c.x, Mathf.Max(to.y, c.y), EdgeCode.Open);
                seen[to.x, to.y] = true; stack.Push(to);
            }
            for (int k = 0; k < 6; k++) { int x = rng.Next(1, N), z = rng.Next(0, N); v.SetV(x, z, EdgeCode.Open); }
            v.openings.Add(Door(false, 0, (N / 2 + 0.5f) * C));
            v.openings.Add(Door(false, N, (N / 2 + 0.5f) * C));
            v.fixtures.Clear();
            for (int x = 0; x < N; x += 2) for (int z = 0; z < N; z += 2)
                v.fixtures.Add(new FixtureDef { x = x, z = z, on = rng.NextDouble() > 0.2, faulty = rng.NextDouble() < 0.2, mood = rng.NextDouble() < 0.3 ? 1 : 0 });
            v.sounds.Add(new SoundDef { x = N * C * 0.5f, z = N * C * 0.5f, kind = SoundKind.Hum, volume = 0.5f, range = 14f });
            return bp;
        }

        static RoomBlueprint Warehouse()
        {
            var bp = New("Warehouse", 10, 8);
            var v = bp.variants[0];
            var r = v.rooms[0]; r.name = "Warehouse"; r.kind = RoomKind.BigOffice; r.ceiling = 4.5f; r.painted = true;
            v.openings.Add(new OpeningDef { vertical = false, line = 0, from = 4.5f * C - 1.2f, to = 4.5f * C + 1.2f, top = 3.0f });
            for (int i = 0; i < 4; i++)
            {
                float x = 1.4f + i * 2.6f;
                v.props.Add(Prop(PropKind.StorageRack, x, 8 * C - 0.6f, 180f, new Vector3(2.3f, 2.4f, 0.95f)));
            }
            v.props.Add(Prop(PropKind.StorageRack, 0.6f, 4.2f, 90f, new Vector3(2.3f, 2.4f, 0.95f)));
            v.props.Add(Prop(PropKind.StorageRack, 10 * C - 0.6f, 4.2f, 270f, new Vector3(2.3f, 2.4f, 0.95f)));
            v.props.Add(Prop(PropKind.Pallet, 4.0f, 4.6f, 12f, new Vector3(1.2f, 1.6f, 1.0f)));
            v.props.Add(Prop(PropKind.Pallet, 7.6f, 4.0f, -8f, new Vector3(1.2f, 1.6f, 1.0f)));
            foreach (var (x, z) in new[] { (3.0f, 6.2f), (3.5f, 6.3f), (8.2f, 2.0f), (8.6f, 2.3f), (2.0f, 2.2f) })
                v.props.Add(Prop(PropKind.Box, x, z, UnityEngine.Random.Range(0f, 360f), new Vector3(0.5f, 0.36f, 0.4f)));
            v.sounds.Add(new SoundDef { x = 6f, z = 4f, y = 4f, kind = SoundKind.Air, volume = 0.5f, range = 16f });
            return bp;
        }

        static RoomBlueprint LShape()
        {
            var bp = New("L-shaped office", 8, 7);
            var v = bp.variants[0];
            v.rooms[0].name = "Office";
            for (int x = 5; x < 8; x++) for (int z = 4; z < 7; z++) v.SetCell(x, z, -1);
            v.FixOutsideEdges();
            v.openings.Add(Door(false, 0, 2.5f * C));
            v.openings.Add(Door(true, 5, 5.5f * C));                     // into the notch: the world's hallway
            v.props.Add(Prop(PropKind.ConferenceTable, 2.6f, 4.4f, 0f, new Vector3(2.4f, 0.74f, 1.1f)));
            v.props.Add(Prop(PropKind.OfficeChair, 1.6f, 3.5f, 0f, new Vector3(0.6f, 1.0f, 0.6f)));
            v.props.Add(Prop(PropKind.OfficeChair, 3.6f, 3.5f, 0f, new Vector3(0.6f, 1.0f, 0.6f)));
            v.props.Add(Prop(PropKind.OfficeChair, 2.6f, 5.4f, 180f, new Vector3(0.6f, 1.0f, 0.6f)));
            v.AutoFixtures();
            v.FixOutsideEdges();
            return bp;
        }

        static RoomBlueprint Endless()
        {
            var bp = New("Endless hallway", 2, 12);
            var v = bp.variants[0];
            var r = v.rooms[0]; r.name = "Hallway"; r.kind = RoomKind.Hallway;
            var south = Door(false, 0, C, 1.2f); var north = Door(false, 12, C, 1.2f);
            v.openings.Add(south); v.openings.Add(north);
            v.Validate();                                                // ids
            north.link = south.id; south.link = north.id;
            v.fixtures.Clear();
            for (int z = 0; z < 12; z += 2) v.fixtures.Add(new FixtureDef { x = z % 4 == 0 ? 0 : 1, z = z, on = true, mood = 1, faulty = z == 6 });
            v.sounds.Add(new SoundDef { x = C, z = 6 * C, kind = SoundKind.Hum, volume = 0.6f, range = 10f });
            for (int z = 1; z < 12; z += 3) v.decals.Add(new DecalDef { vertical = true, line = z % 2 == 0 ? 0 : 2, side = z % 2 == 0 ? 1 : -1, along = (z + 0.5f) * C, height = 0.9f + (z % 3) * 0.4f, size = 0.3f, kind = z % 2 == 0 ? DecalKind.Damp : DecalKind.WaterStain, seed = z });
            return bp;
        }

        static RoomBlueprint Haunted()
        {
            var bp = New("Something is wrong", 7, 7);
            var v = bp.variants[0];
            v.rooms[0].name = "The room";
            var door = Door(false, 0, 3.5f * C, 1.0f);
            v.openings.Add(door);
            v.Validate();
            v.props.Add(Prop(PropKind.OfficeChair, 3.0f, 4.0f, 30f, new Vector3(0.6f, 1.0f, 0.6f)));
            v.props.Add(Prop(PropKind.Armchair, 5.5f, 5.8f, 200f, new Vector3(0.95f, 0.85f, 0.9f), 1));
            var ceilingChair = Prop(PropKind.OfficeChair, 4.2f, 2.8f, 120f, new Vector3(0.6f, 1.0f, 0.6f));
            v.props.Add(ceilingChair); v.ToCeiling(ceilingChair);
            var ceilingTable = Prop(PropKind.ConferenceTable, 4.2f, 5.2f, 90f, new Vector3(2.4f, 0.74f, 1.1f));
            v.props.Add(ceilingTable); v.ToCeiling(ceilingTable);
            for (int i = 0; i < 5; i++) v.props.Add(Prop(PropKind.Box, 1.0f + i * 0.5f, 7 * C - 0.6f, UnityEngine.Random.Range(0f, 360f), new Vector3(0.5f, 0.36f, 0.4f)));
            v.Mess(0.7f, 99);
            v.triggers.Add(new TriggerDef { area = 0, action = TriggerAction.DoorSlam, door = door.id, once = true, delay = 1.5f });
            v.triggers.Add(new TriggerDef { area = 0, action = TriggerAction.Flicker, once = true, delay = 2.5f });
            v.triggers.Add(new TriggerDef { area = 0, action = TriggerAction.LightsOut, once = true, delay = 5.5f });
            v.triggers.Add(new TriggerDef { area = 0, action = TriggerAction.Sound, sound = SoundKind.Knocking, once = true, delay = 7f });
            v.decals.Add(new DecalDef { vertical = false, line = 7, side = -1, along = 4.0f, height = 1.3f, size = 0.35f, kind = DecalKind.Handprints, seed = 5 });
            v.decals.Add(new DecalDef { vertical = true, line = 0, side = 1, along = 5.0f, height = 1.8f, size = 0.6f, kind = DecalKind.Mould, seed = 6 });
            return bp;
        }
    }
}
