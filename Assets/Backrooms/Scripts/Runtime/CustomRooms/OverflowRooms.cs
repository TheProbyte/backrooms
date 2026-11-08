using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Overflow's own rooms (2026-11-07, Docs/HubPlan.md), made in code as build-mode blueprints so the world's chunk builder
    /// builds them like any custom room: the hub (Overflow's office, on a storey of its own below every world, HubPlan) and
    /// the service lift's shaft at the world's start. They are never in the player's library; CustomRoomStamp finds them here.
    /// Made once and only read afterwards, so the generator's worker threads can share them.
    /// </summary>
    public static class OverflowRooms
    {
        public const string HubId = "overflow:hub", LiftId = "overflow:lift";
        const float C = OfficeBiome.CellSize;

        public static RoomBlueprint Get(string id) => id == HubId ? s_hub : id == LiftId ? s_lift : null;

        // ---------------- The hub's areas (cells; x east, z north) ----------------

        public enum Area { Void, Lift, LiftHall, Lobby, FirstAid, Lockers, Training, PayOffice, Manager, Vending }
        public const int HubW = 18, HubD = 18;

        /// <summary>Each area's cells: x, z, width, depth (the rest of the 18 x 18 is sealed void).</summary>
        public static readonly Dictionary<Area, RectInt> Areas = new Dictionary<Area, RectInt>
        {
            { Area.Lift, new RectInt(8, 0, 3, 3) },
            { Area.LiftHall, new RectInt(6, 3, 7, 3) },
            { Area.Lobby, new RectInt(4, 6, 12, 6) },
            { Area.FirstAid, new RectInt(0, 3, 4, 4) },
            { Area.Lockers, new RectInt(0, 7, 4, 5) },
            { Area.Training, new RectInt(2, 12, 6, 6) },
            { Area.PayOffice, new RectInt(8, 12, 4, 3) },
            { Area.Manager, new RectInt(12, 12, 4, 4) },
            { Area.Vending, new RectInt(16, 7, 2, 3) },
        };

        /// <summary>The hub lift's doorway on the line z = 3: from, to (m along x), its top.</summary>
        public const float HubDoorFrom = 10.0f, HubDoorTo = 12.8f, LiftDoorTop = 2.5f;
        /// <summary>The cots' return chutes: the hole in the wall behind each (Tools/Blender/build_hub.py, HubChute).</summary>
        public const float ChuteW = 0.6f, ChuteSill = 0.42f, ChuteTop = 0.92f;
        /// <summary>Where each player wakes in the first-aid room (blueprint metres): at the foot of their cot, facing north to the door.</summary>
        public static readonly Vector2[] WakeSpots = { new Vector2(0.6f, 6.2f), new Vector2(1.8f, 6.2f), new Vector2(3.0f, 6.2f), new Vector2(4.2f, 6.2f) };

        static RoomSettings Settings(Area a)
        {
            var s = new RoomSettings { name = a.ToString(), kind = RoomKind.Office, lights = LightMode.AllOn, ceiling = OfficeBiome.CeilingHeight };
            // Every room of the hub: the panelled wall with the company stripe and a bare concrete slab (the owner, 2026-11-07).
            s.wallFinish = WallFinish.HubPanel; s.floorFinish = FloorFinish.Concrete;
            switch (a)
            {
                case Area.Void: s.lights = LightMode.Dark; s.wallFinish = WallFinish.PaintedPlaster; s.floorFinish = FloorFinish.Default; break;
                case Area.Lift: s.kind = RoomKind.Hallway; break;
                case Area.LiftHall: s.kind = RoomKind.Hallway; break;
                case Area.Lobby: s.kind = RoomKind.BigOffice; break;
                case Area.Training: s.ceiling = 3.9f; break;
                case Area.PayOffice: s.lights = LightMode.Dark; break;
                case Area.Manager: break;                  // (one dim light left on: its fixtures are set one by one)
                case Area.Vending: s.kind = RoomKind.Hallway; break;
            }
            return s;
        }

        static RoomBlueprint MakeHub()
        {
            var v = RoomVariant.CreateDefault(HubW, HubD, "Hub");
            v.fixtures.Clear();
            v.rooms = new List<RoomSettings>();
            var areas = (Area[])System.Enum.GetValues(typeof(Area));
            foreach (var a in areas) v.rooms.Add(Settings(a));
            for (int i = 0; i < v.cellRoom.Length; i++) v.cellRoom[i] = (int)Area.Void;
            foreach (var kv in Areas)
                for (int x = kv.Value.xMin; x < kv.Value.xMax; x++)
                for (int z = kv.Value.yMin; z < kv.Value.yMax; z++) v.cellRoom[z * HubW + x] = (int)kv.Key;
            // Walls wherever two areas meet, and all round; open inside an area.
            for (int x = 0; x <= HubW; x++)
            for (int z = 0; z < HubD; z++)
                v.SetV(x, z, x == 0 || x == HubW || v.RoomOf(x - 1, z) != v.RoomOf(x, z) ? EdgeCode.Wall : EdgeCode.Open);
            for (int x = 0; x < HubW; x++)
            for (int z = 0; z <= HubD; z++)
                v.SetH(x, z, z == 0 || z == HubD || v.RoomOf(x, z - 1) != v.RoomOf(x, z) ? EdgeCode.Wall : EdgeCode.Open);

            // Doorways (metres along their line).
            void Door(bool vertical, int line, float from, float to, float top) => v.openings.Add(new OpeningDef { vertical = vertical, line = line, from = from, to = to, top = top });
            Door(false, 3, HubDoorFrom, HubDoorTo, LiftDoorTop);      // the lift's gate
            Door(false, 6, 8.4f, 14.4f, 2.6f);                         // lift hall into the lobby, wide
            Door(true, 16, 8.55f, 11.85f, 2.6f);                       // the vending alcove
            // Doorways with doors in (the world's own wooden doors: OfficeChunkBuilder.Doors puts one in each of the hub's).
            v.SetH(2, 7, EdgeCode.Header);                             // first aid into the lockers
            v.SetV(4, 10, EdgeCode.Header);                            // lockers into the lobby
            v.SetH(5, 12, EdgeCode.Header); v.SetH(6, 12, EdgeCode.Header);   // the training room's double doors
            // Windows: the pay window (its shutter down) and the manager's (blinds), both onto the lobby.
            v.openings.Add(new OpeningDef { vertical = false, line = 12, from = 12.6f, to = 13.8f, sill = 0.95f, top = 1.85f });
            v.openings.Add(new OpeningDef { vertical = false, line = 12, from = 16.4f, to = 18.2f, sill = 1.0f, top = 2.0f });
            // The return chutes behind the cots' heads (HubWake brings you out of yours): a hole in first aid's south wall each.
            foreach (var s in WakeSpots)
                v.openings.Add(new OpeningDef { vertical = false, line = 3, from = s.x - ChuteW * 0.5f, to = s.x + ChuteW * 0.5f, sill = ChuteSill, top = ChuteTop });

            // Lights: one every two cells, the generator's spacing; a couple dead or stuttering in the lobby.
            foreach (var kv in Areas)
            {
                var r = kv.Value;
                // The shaft: one light over the cab's roof (hidden by it), so the world's lighting reaches inside the cab.
                if (kv.Key == Area.Lift) { v.fixtures.Add(new FixtureDef { x = r.xMin + 1, z = r.yMin + 1, on = true }); continue; }
                bool on = v.rooms[(int)kv.Key].lights != LightMode.Dark;
                int ox = ((r.width - 1) % 2 + 1) / 2, oz = ((r.height - 1) % 2 + 1) / 2;
                for (int x = r.xMin + ox; x < r.xMax; x += 2)
                for (int z = r.yMin + oz; z < r.yMax; z += 2)
                {
                    var f = new FixtureDef { x = x, z = z, on = on };
                    // Each room its own light: the lobby's warm and yellowed, first aid's and the lockers' a harsh sickly
                    // green, the manager's one dim tube left on (enough to see in through the blinds).
                    if (kv.Key == Area.Lobby || kv.Key == Area.Vending) f.mood = 3;
                    if (kv.Key == Area.FirstAid || kv.Key == Area.Lockers) f.mood = 2;
                    if (kv.Key == Area.Manager) { f.on = x == 13 && z == 13; f.mood = 1; }
                    if (kv.Key == Area.Lobby && ((x == 9 && z == 9) || (x == 15 && z == 11))) f.on = false;
                    if (kv.Key == Area.Lobby && x == 13 && z == 7) f.faulty = true;
                    if (kv.Key == Area.Lockers && x == 1 && z == 9) { f.faulty = true; f.buzz = true; }
                    v.fixtures.Add(f);
                }
            }

            // Furniture: the hub's own models (Tools/Blender/build_hub.py, the Overflow look) and the game's Overflow remakes.
            void Prop(PropKind k, float x, float z, float yaw, Vector3 size, int variant = 0) =>
                v.props.Add(new PropDef { kind = k, x = x, z = z, yaw = yaw, size = size, variant = variant });
            // A named fitting: on the floor (solid to size), or on a wall (elevation, no collider, origin on the wall's face).
            void Fit(string model, float x, float z, float yaw, Vector3 size, float elevation = 0f) =>
                v.props.Add(new PropDef { kind = PropKind.Fitting, model = model, x = x, z = z, yaw = yaw, size = size, elevation = elevation, pinned = true });
            const float face = OfficeBiome.WallThickness * 0.5f;      // a wall's face off its line
            // First aid: four cots along the south wall, heads to the wall; a cabinet by the door.
            foreach (float x in new[] { 0.6f, 1.8f, 3.0f, 4.2f }) Fit("HubCot", x, 3.6f + face + 1.02f, -90f, new Vector3(1.9f, 0.5f, 0.75f));
            Fit("HubFirstAid", 4.2f, 8.4f - face, 180f, Vector3.zero, 1.25f);
            foreach (var s in WakeSpots) Fit("HubChute", s.x, 3.6f + face, 0f, Vector3.zero);           // a cot's return chute
            Fit("ExitSign", 3.0f, 8.4f - face, 180f, Vector3.zero);                                      // over the door out
            Fit("HubSpeaker", 2.4f, 5.4f, 0f, Vector3.zero, OfficeBiome.CeilingHeight);
            // Lockers: three banks along the west wall, one on the north wall, a bench down the middle.
            foreach (float z in new[] { 9.6f, 10.8f, 12.0f }) Fit("HubLockers", face + 0.25f, z, 90f, new Vector3(1.2f, 1.9f, 0.45f));
            Fit("HubLockers", 2.6f, 14.4f - face - 0.25f, 180f, new Vector3(1.2f, 1.9f, 0.45f));
            Fit("HubBench", 2.4f, 10.8f, 90f, new Vector3(1.5f, 0.45f, 0.32f));
            Fit("HubMat", 3.0f, 9.05f, 0f, Vector3.zero);
            Fit("HubSignFirstAid", 1.2f, 8.4f + face, 0f, Vector3.zero, 1.75f);
            Fit("HubSpeaker", 3.0f, 12.6f, 0f, Vector3.zero, OfficeBiome.CeilingHeight);
            // Lift hall: the punch clock and the time cards on the way to the lift, uncollected cards on the floor, a mat.
            Fit("HubPunchClock", 7.2f + face, 5.0f, 90f, Vector3.zero, 1.15f);
            Fit("HubCardRack", 7.2f + face, 5.75f, 90f, Vector3.zero, 0.85f);
            Fit("HubCardPile", 7.6f, 6.15f, 20f, Vector3.zero);
            Fit("HubMat", 11.4f, 4.3f, 0f, Vector3.zero);
            Fit("HubMap", 15.6f - face, 5.9f, -90f, Vector3.zero, 1.1f);                                // YOU ARE HERE
            Fit("DrinkingFountain", 15.6f - face, 4.35f, -90f, Vector3.zero, 0.5f);
            Fit("HubSpeaker", 10.2f, 4.8f, 0f, Vector3.zero, OfficeBiome.CeilingHeight);
            // Lobby, what you see stepping out of the lift: the OVERFLOW sign over the contracts board, the pay window
            // beside it (shut), the manager's locked door and his window (blinds). A rubber runner worn down the middle.
            Fit("HubLogo", 11.0f, 14.4f - face, 180f, Vector3.zero, 2.1f);
            Fit("HubBoard", 11.0f, 14.4f - face, 180f, Vector3.zero, 0.7f);
            Fit("HubPayWindow", 13.2f, 14.4f - face, 180f, new Vector3(1.4f, 1.95f, 0.16f));
            Fit("HubDoorLocked", 15.1f, 14.4f - face, 180f, Vector3.zero);
            Fit("HubBlinds", 17.3f, 14.4f - face, 180f, new Vector3(1.95f, 2.05f, 0.16f));
            Fit("HubRunner", 11.4f, 9.6f, 90f, Vector3.zero);
            // The sofas (the hub's 70s one, sat on and moved like any) round a coffee table; reception with nobody at it.
            var sofa = new Vector3(2.1f, 0.78f, 0.9f);
            v.props.Add(new PropDef { kind = PropKind.Sofa, model = "HubSofa", x = 6.45f, z = 9.3f, yaw = 90f, size = sofa });
            v.props.Add(new PropDef { kind = PropKind.Sofa, model = "HubSofa", x = 9.55f, z = 9.3f, yaw = -90f, size = sofa });
            Fit("HubCoffeeTable", 8.0f, 9.3f, 90f, new Vector3(1.1f, 0.42f, 0.6f));
            // Reception in the corner, facing the lift; its desk lamp behind the counter, plugged into the wall behind it.
            Fit("HubReception", 17.75f, 13.15f, -90f, new Vector3(2.4f, 1.05f, 0.75f));
            Prop(PropKind.OfficeChair, 18.6f, 12.9f, -65f, new Vector3(0.6f, 1.0f, 0.6f));
            Prop(PropKind.DeskLamp, 17.95f, 13.7f, -120f, new Vector3(0f, 0.775f, 0f));         // on the desk behind: lit (cord to the wall)
            Fit("HubClockStopped", 5.3f, 14.4f - face, 180f, Vector3.zero, 1.85f);             // stopped at five: closing time
            foreach (var (x, z) in new[] { (5.35f, 13.85f), (18.7f, 9.0f), (5.35f, 7.75f) })
                Fit("HubPlant", x, z, x * 37f, new Vector3(0.5f, 1.0f, 0.5f));
            Fit("HubPosterA", 4.8f + face, 9.2f, 90f, Vector3.zero, 1.15f);
            Fit("HubPosterB", 6.6f, 7.2f + face, 0f, Vector3.zero, 1.15f);
            // The improvements board (Interaction.Improvements) on the south wall, east of the way to the lift, facing the
            // contracts board across the lobby.
            Fit("HubBoard", 17.3f, 7.2f + face, 0f, Vector3.zero, 0.7f);
            Fit("Bucket", 14.1f, 12.7f, 30f, new Vector3(0.32f, 0.36f, 0.32f));                 // under the leak
            Fit("ExtinguisherCabinet", 19.2f - face, 12.35f, -90f, Vector3.zero, 1.15f);
            Fit("ThermostatRound", 4.8f + face, 10.6f, 90f, Vector3.zero, 1.5f);
            Fit("HubSpeaker", 7.8f, 10.2f, 0f, Vector3.zero, OfficeBiome.CeilingHeight);
            Fit("HubSpeaker", 15.0f, 10.2f, 0f, Vector3.zero, OfficeBiome.CeilingHeight);
            // The way: signs to the training room, the lockers and first aid, the lift.
            Fit("HubSignTraining", 7.2f, 14.4f - face, 180f, Vector3.zero, 2.2f);
            Fit("HubSignLockers", 4.8f + face, 13.75f, 90f, Vector3.zero, 1.75f);
            Fit("HubSignLift", 15.0f, 7.2f + face, 0f, Vector3.zero, 1.75f);
            // The vending corner: two drinks machines (one unplugged) and the coffee machine.
            float vx = 21.6f - face - 0.42f - 0.03f;
            Prop(PropKind.VendingMachine, vx, 9.1f, -90f, SpecialRooms.VendingSize, 1);
            Prop(PropKind.VendingMachine, vx, 10.6f, -90f, SpecialRooms.VendingSize, 1 | 2);     // unplugged
            Fit("HubCoffee", 21.6f - face - 0.39f, 11.55f, -90f, new Vector3(0.75f, 1.85f, 0.72f));
            Fit("HubOutOfOrder", 21.6f - face - 0.39f - 0.38f, 11.55f, -90f, Vector3.zero, 1.15f);   // taped to it
            // Training room: three rows of four, bolted down, an aisle down the middle, facing the TV.
            foreach (float z in new[] { 16.4f, 17.6f, 18.8f })
                foreach (float x in new[] { 3.7f, 4.6f, 6.6f, 7.5f })
                    v.props.Add(new PropDef { kind = PropKind.SchoolChair, x = x, z = z, yaw = 0f, size = new Vector3(0.47f, 0.85f, 0.58f), variant = 1, pinned = true });
            Fit("HubTvCart", 5.6f, 21.6f - face - 0.4f, 180f, new Vector3(0.66f, 1.5f, 0.48f));
            Fit("HubPosterC", 9.6f - face, 18.0f, -90f, Vector3.zero, 1.15f);
            Fit("HubChairStack", 2.95f, 21.0f, 20f, new Vector3(0.5f, 1.2f, 0.5f));               // the spares, stacked
            Fit("ExitSign", 7.2f, 14.4f + face, 0f, Vector3.zero);                                       // over the doors out
            Fit("HubSpeaker", 4.2f, 15.6f, 0f, Vector3.zero, 3.9f);
            // The manager's office (seen through the blinds): the desk and chair under dust sheets, a shelf, the calendar
            // stopped at March 1979.
            Fit("HubSheetDesk", 16.8f, 17.3f, 180f, new Vector3(1.8f, 0.8f, 0.82f));
            Fit("HubSheetChair", 16.8f, 18.3f, 15f, new Vector3(0.65f, 1.05f, 0.65f));
            Prop(PropKind.Shelving, 19.2f - face - 0.21f - 0.03f, 15.4f, -90f, new Vector3(1.0f, 1.85f, 0.42f));
            Fit("HubCalendar", 17.3f, 19.2f - face, 180f, Vector3.zero, 1.3f);

            // Damp where the building leaks, a stain or two.
            void Mark(bool vertical, int line, int side, float along, float height, float size, DecalKind kind, int seed) =>
                v.decals.Add(new DecalDef { vertical = vertical, line = line, side = side, along = along, height = height, size = size, kind = kind, seed = seed });
            Mark(false, 12, -1, 17.4f, 1.9f, 0.55f, DecalKind.WaterStain, 15);
            Mark(true, 8, -1, 19.8f, 2.6f, 0.6f, DecalKind.Damp, 16);
            Mark(false, 6, 1, 6.0f, 1.9f, 0.4f, DecalKind.Notice, 12);
            Mark(false, 12, -1, 14.1f, 2.3f, 0.45f, DecalKind.WaterStain, 18);                   // above the bucket
            Mark(true, 16, -1, 13.7f, 0.3f, 0.1f, DecalKind.Outlet, 19);                          // the desk lamp's outlet

            // The building's sounds: the lights' hum, air in the vents.
            v.sounds.Add(new SoundDef { x = 10.8f, z = 10.8f, y = 2.5f, kind = SoundKind.Hum, volume = 0.35f, range = 12f });
            v.sounds.Add(new SoundDef { x = 11.4f, z = 5.4f, y = 2.5f, kind = SoundKind.Air, volume = 0.3f, range = 8f });
            v.sounds.Add(new SoundDef { x = 5.4f, z = 19.8f, y = 3.6f, kind = SoundKind.Air, volume = 0.25f, range = 9f });

            var bp = new RoomBlueprint { id = HubId, name = "Overflow", enabled = false, allowRotate = false, variants = new List<RoomVariant> { v } };
            bp.Validate();
            return bp;
        }

        // ---------------- The lift's shaft in the world ----------------

        public const int LiftW = 3, LiftD = 3;
        /// <summary>Where the shaft stands in district (0, 0) (cells): at the north of the spawn hall, its gate opening south.</summary>
        public static readonly RectInt WorldLiftRect = new RectInt(4, 9, LiftW, LiftD);
        public const float WorldDoorFrom = 0.4f, WorldDoorTo = 3.2f;

        static RoomBlueprint MakeLift()
        {
            var v = RoomVariant.CreateDefault(LiftW, LiftD, "Lift");
            v.SetH(LiftW / 2, 0, EdgeCode.Wall);
            v.rooms = new List<RoomSettings> { Settings(Area.Lift) };
            for (int i = 0; i < v.cellRoom.Length; i++) v.cellRoom[i] = 0;
            v.fixtures.Clear();
            v.fixtures.Add(new FixtureDef { x = 1, z = 1, on = true });          // over the cab's roof, as in the hub
            v.openings.Add(new OpeningDef { vertical = false, line = 0, from = WorldDoorFrom, to = WorldDoorTo, top = LiftDoorTop });
            var bp = new RoomBlueprint { id = LiftId, name = "Service lift", enabled = false, allowRotate = false, variants = new List<RoomVariant> { v } };
            bp.Validate();
            return bp;
        }

        /// <summary>The world's lift placement in district (0, 0) (the spawn hall's north side).</summary>
        public static LandmarkPlacement WorldLiftPlacement(Vector2Int district = default) => new LandmarkPlacement
        {
            name = "Service lift", kind = LandmarkKind.Custom, district = district, rect = WorldLiftRect, core = WorldLiftRect,
            storeys = 1, seed = 0x11F7, blueprint = LiftId, variant = 0, rot = 0,
        };
        // (Last: static fields start in the order they are written, and these need the tables above.)
        static readonly RoomBlueprint s_hub = MakeHub(), s_lift = MakeLift();
    }
}
