using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// What is on the other side of a seam (2026-11-07, Docs/ContractFloorsPlan.md): four small hand-built places in the
    /// real world, one for each kind (LeaseSeams.Kinds), made in code as build-mode blueprints like the hub (OverflowRooms)
    /// and stamped into their sealed zone (SeamPlan). Each is one room, 8.4 x 9.6 m: the way back in the middle of its south
    /// wall (a duct-taped doorway, as on the floor), the job's boxes stacked on a pallet in the north-west corner under an
    /// OVERFLOW PICKUP sign, a taped DELIVERIES bay in the north-east corner, the business's sign over them, and the
    /// place's own things.
    /// <para>Made to read at a glance as somewhere that is not the Backrooms (the owner, 2026-11-07: "interior of those must be
    /// clearly non backrooms esque looking"): the real world's surfaces (RealMaterials: painted block or white drywall,
    /// polished slab or vinyl tile, a steel deck or plaster overhead, never a tile ceiling), its LED lights and daylight
    /// through windows and under a loading door (Tools/Blender/build_real.py), its sounds (a radio, traffic outside), and
    /// its own things (safety signs, a pallet jack, shop shelving full of stock, a showroom). Cool and clean where the
    /// Backrooms are yellow and worn. Nobody is ever there.</para>
    /// </summary>
    public static class SeamRooms
    {
        public const string IdPrefix = "overflow:seam";
        public const int W = 7, D = 8;
        const float C = OfficeBiome.CellSize, Face = OfficeBiome.WallThickness * 0.5f;

        /// <summary>The way back: the middle of the south wall (blueprint metres along it).</summary>
        public const float TearX = 3.5f * C;
        /// <summary>The pickup pallet's middle and the DELIVERIES bay (blueprint metres).</summary>
        public static readonly Vector2 Pickup = new Vector2(1.5f, 8.3f);
        public static readonly Rect Bay = new Rect(5.4f, 7.0f, 2.6f, 2.2f);
        /// <summary>The client's staff door (2026-11-09): on the east wall beside the DELIVERIES bay, shut and locked; how the
        /// client's people come for returned boxes and bring the next lot (Interaction.StaffDoors rattles it).</summary>
        public const float DoorZ = 7.9f;
        public static float DoorX => W * C - Face;
        /// <summary>The pallet's deck height: the boxes stand on it.</summary>
        public const float PalletTop = 0.145f;

        public static string Id(int kind) => IdPrefix + kind;

        public static RoomBlueprint Get(string id)
        {
            if (id == null || !id.StartsWith(IdPrefix)) return null;
            return int.TryParse(id.Substring(IdPrefix.Length), out int k) && k >= 0 && k < s_rooms.Length ? s_rooms[k] : null;
        }

        static RoomBlueprint Make(int kind)
        {
            var v = RoomVariant.CreateDefault(W, D, LeaseSeams.Kinds[kind]);
            v.fixtures.Clear();             // (no Backrooms fittings: the real ones are props, lit by OfficeChunkBuilder.RealLight)
            var s = new RoomSettings { name = LeaseSeams.Kinds[kind], kind = RoomKind.BigOffice, lights = LightMode.AllOn };
            switch (kind)
            {
                case 0: s.ceiling = 4.6f; s.wallFinish = WallFinish.Blockwork; s.floorFinish = FloorFinish.Polished; break;      // warehouse
                case 1: s.ceiling = 3.6f; s.wallFinish = WallFinish.Blockwork; s.floorFinish = FloorFinish.Vinyl; break;         // print shop
                case 2: s.ceiling = 3.2f; s.wallFinish = WallFinish.Drywall; s.floorFinish = FloorFinish.Vinyl; break;           // office supply
                default: s.ceiling = 3.4f; s.wallFinish = WallFinish.Drywall; s.floorFinish = FloorFinish.Polished; break;      // furniture store
            }
            v.rooms = new List<RoomSettings> { s };
            for (int i = 0; i < v.cellRoom.Length; i++) v.cellRoom[i] = 0;
            for (int x = 0; x <= W; x++) for (int z = 0; z < D; z++) v.SetV(x, z, x == 0 || x == W ? EdgeCode.Wall : EdgeCode.Open);
            for (int x = 0; x < W; x++) for (int z = 0; z <= D; z++) v.SetH(x, z, z == 0 || z == D ? EdgeCode.Wall : EdgeCode.Open);

            void Prop(PropKind k, float x, float z, float yaw, Vector3 size, int variant = 0, bool lit = true) =>
                v.props.Add(new PropDef { kind = k, x = x, z = z, yaw = yaw, size = size, variant = variant, lit = lit, pinned = true });
            void Fit(string model, float x, float z, float yaw, Vector3 size, float elevation = 0f, int variant = 0) =>
                v.props.Add(new PropDef { kind = PropKind.Fitting, model = model, x = x, z = z, yaw = yaw, size = size, elevation = elevation, variant = variant, pinned = true });
            float north = D * C - Face, east = W * C - Face, west = Face, south = Face;
            float ceil = s.ceiling;

            // Every one: the way back (a seam seen from this side: kind 4), FIRE EXIT over it, the pickup pallet and its sign,
            // the deliveries bay, the business's sign, a clock.
            Fit("SeamCrack", TearX, south, 0f, Vector3.zero, 0f, 4);
            Fit("RealSignFire", TearX, south, 0f, Vector3.zero, 2.3f);
            Fit("Pallet_0", Pickup.x, Pickup.y, 0f, new Vector3(1.2f, PalletTop, 1.0f));
            Fit("SeamPickupSign", Pickup.x, north, 180f, Vector3.zero, 1.55f);
            Fit("SeamBay", Bay.center.x, Bay.center.y, 180f, Vector3.zero);                     // (reads from the way in)
            Fit("SeamShop" + kind, 4.2f, north, 180f, Vector3.zero, kind == 0 ? 2.8f : 2.15f);
            // The client's own way in and out (2026-11-09, the owner: "how do people get in and out to take and put boxes
            // there"): a staff door beside the bay, always shut and locked to you.
            Fit("StaffDoor", east, DoorZ, -90f, new Vector3(1.06f, 2.13f, 0.16f));

            // Lights: LED high-bays in the warehouse, battens elsewhere, on a 4.2 x 2.4 m grid.
            for (int ix = 0; ix < 2; ix++)
            for (int iz = 0; iz < 3; iz++)
            {
                float x = 2.1f + ix * 4.2f, z = 1.8f + iz * 3.0f;
                if (kind == 0) { if (iz == 1) continue; Fit("RealHighBay", x, z + (iz == 0 ? 0.6f : -0.6f), 0f, Vector3.zero, ceil); }
                else Fit("RealBatten", x, z, 0f, Vector3.zero, ceil);
            }

            switch (kind)
            {
                case 0:     // a wholesale warehouse: racking down the west wall, a loading door, a pallet jack, safety signs
                    Fit("RealWindow", west, 4.2f, 90f, Vector3.zero, 3.2f);
                    Prop(PropKind.StorageRack, 0.65f, 2.9f, 90f, new Vector3(2.3f, 3.0f, 0.95f), 11);
                    Prop(PropKind.StorageRack, 0.65f, 5.3f, 90f, new Vector3(2.3f, 3.0f, 0.95f), 12);
                    Fit("RollerDoor", east, 3.6f, -90f, Vector3.zero);
                    Fit("RealSignForklift", east, 6.0f, -90f, Vector3.zero, 1.6f);
                    Fit("PalletJack", 5.6f, 6.0f, 200f, new Vector3(0.55f, 0.3f, 1.4f));
                    Prop(PropKind.Pallet, 3.4f, 4.8f, 90f, new Vector3(1.2f, 1.4f, 1.0f), 21);
                    Fit("RealSignHiVis", 1.6f, south, 0f, Vector3.zero, 1.6f);
                    Fit("HiVisHooks", 6.6f, south, 0f, Vector3.zero);
                    Fit("RealWallPhone", 2.6f, south, 0f, Vector3.zero);
                    Fit("RealClock", 6.6f, north, 180f, Vector3.zero, 2.3f);
                    break;
                case 1:     // a print shop's back room: the press, the guillotine, paper on pallets, shelves of stock
                    Fit("RealWindow", east, 4.2f, -90f, Vector3.zero, 1.5f);
                    Fit("PrintPress", 3.4f, 4.4f, 90f, new Vector3(2.6f, 1.6f, 1.4f));
                    Fit("Guillotine", 6.4f, 5.4f, -90f, new Vector3(0.8f, 1.3f, 0.6f));
                    Fit("PaperStack", 7.4f, 1.6f, 0f, new Vector3(1.2f, 1.0f, 1.0f));
                    Fit("PaperStack", 7.4f, 3.1f, 10f, new Vector3(1.2f, 1.0f, 1.0f));
                    Fit("RealGondolaB", west, 2.4f, 90f, new Vector3(1.3f, 1.9f, 1.0f));
                    Fit("RealGondolaA", west, 5.0f, 90f, new Vector3(1.3f, 1.9f, 1.0f));
                    Fit("RealWallPhone", 2.4f, south, 0f, Vector3.zero);
                    Fit("RealClock", 6.6f, north, 180f, Vector3.zero, 2.2f);
                    break;
                case 2:     // an office supplier: shelving bays full of stock down both walls, the counter by the way in
                    Fit("RealWindow", 1.9f, south, 0f, Vector3.zero, 2.0f);
                    for (int i = 0; i < 4; i++)
                        Fit(i % 2 == 0 ? "RealGondolaA" : "RealGondolaB", west, 1.3f + i * 1.32f, 90f, new Vector3(1.3f, 1.9f, 1.0f));
                    Fit("RealGondolaB", east, 4.6f, -90f, new Vector3(1.3f, 1.9f, 1.0f));
                    Fit("RealGondolaA", east, 5.92f, -90f, new Vector3(1.3f, 1.9f, 1.0f));
                    Fit("RealGondolaA", 4.2f, 4.2f, 0f, new Vector3(1.3f, 1.9f, 1.0f));
                    Fit("RealGondolaB", 4.2f, 4.2f, 180f, new Vector3(1.3f, 1.9f, 1.0f));
                    Fit("ShopCounter", 6.85f, 1.9f, -90f, new Vector3(1.8f, 1.0f, 0.62f));
                    Fit("RealClock", 6.6f, north, 180f, Vector3.zero, 2.0f);
                    break;
                default:    // a furniture store's showroom: sofas, a club chair and a lamp set out as a room, a sale on
                    Fit("RealWindow", west, 4.6f, 90f, Vector3.zero, 1.0f);
                    Fit("ShowroomRug", 2.6f, 3.3f, 0f, Vector3.zero);
                    Fit("ShowroomSofa", 2.6f, 2.1f, 0f, new Vector3(2.1f, 0.8f, 0.9f));
                    Fit("ShowroomTable", 2.6f, 3.4f, 0f, new Vector3(1.1f, 0.42f, 0.6f));
                    Fit("ShowroomChair", 4.2f, 3.6f, -100f, new Vector3(0.85f, 0.8f, 0.85f));
                    Fit("ShowroomLamp", 0.7f, 1.4f, 30f, Vector3.zero);
                    Fit("ShowroomSofaB", 7.6f, 4.4f, -90f, new Vector3(2.1f, 0.8f, 0.9f));
                    Fit("ShowroomTable", 6.2f, 4.4f, 90f, new Vector3(1.1f, 0.42f, 0.6f));
                    Fit("SaleBanner", 4.2f, 6.2f, 0f, Vector3.zero, ceil);
                    Fit("RealClock", 6.6f, north, 180f, Vector3.zero, 2.1f);
                    break;
            }
            // Its sound: a radio somewhere, the road outside, the air handling.
            v.sounds.Add(new SoundDef { x = kind == 2 ? 6.9f : 1.2f, z = kind == 2 ? 1.9f : 1.0f, y = 1.2f, kind = SoundKind.Radio, volume = 0.22f, range = 9f });
            v.sounds.Add(new SoundDef { x = kind == 0 ? 7.8f : 4.2f, z = kind == 0 ? 3.6f : 9.0f, y = 1.5f, kind = SoundKind.Traffic, volume = 0.35f, range = 12f });
            v.sounds.Add(new SoundDef { x = 4.2f, z = 4.8f, y = ceil - 0.3f, kind = SoundKind.Air, volume = 0.15f, range = 9f });

            var bp = new RoomBlueprint { id = Id(kind), name = LeaseSeams.Kinds[kind], enabled = false, allowRotate = false, variants = new List<RoomVariant> { v } };
            bp.Validate();
            return bp;
        }

        // (Last: static fields start in the order they are written.)
        static readonly RoomBlueprint[] s_rooms = { Make(0), Make(1), Make(2), Make(3) };
    }
}
