using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>
    /// Wooden doors in some of the Backrooms' doorways (not the Poolrooms): a headed, square-topped doorway inside the
    /// chunk between two ordinary rooms gets one by the biome's <see cref="BiomeRules.doorChance"/>, decided per edge from
    /// its world position (the same every time). Which way it swings, and whether it is shut, ajar or wide open, come from
    /// the same hash. The door itself is Interaction.Door on the model from <see cref="DoorModels"/>.
    /// </summary>
    public partial class OfficeChunkBuilder
    {
        public struct DoorSpec
        {
            public Vector3 position;      // chunk-local: the opening's bottom centre on the wall's centre line
            public float yaw;             // turns the model's +z (the side it swings to) to face its room
            public float angle;           // degrees open as built
            public uint mask;             // both rooms' light layers
            public uint maskFront, maskBack;  // the +z (swing) side's room's layer, the other side's
            public string key;
            public DoorEdges.Edge edge;   // the doorway in world cells (shut, it is a wall to light and sound)
            public int roomFront, roomBack; // rooms on the +z (swing) side and the other side (this chunk's indices)
            public int boltSide;          // a good room's door: the face its slide bolt is on (+1 the +z face, -1 the other), 0 none
            public bool closer, kick, wedge;  // its hardware (DoorHardware, 2026-11-03): a closer, a kick plate, a wedge holding it open
        }

        readonly List<DoorSpec> _doors = new List<DoorSpec>();

        static uint DoorHash(int wx, int wz, bool vertical, int storey)
        {
            uint h = (uint)(wx * 0x27D4EB2D) ^ (uint)(wz * 0x165667B1) ^ (vertical ? 0x85EBCA77u : 0xC2B2AE3Du) ^ (uint)(storey * 0x9E3779B1);
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h;
        }

        bool DoorRoom(int x, int z)
        {
            var r = _L.RoomAt(x, z);
            return !r.noFloor && (!r.custom || r.landmark == HubPlan.ZoneName) && r.role == AnnexRole.None && r.kind != RoomKind.Crawl && !r.spiral && !r.openAbove
                   && r.ceiling >= OfficeBiome.DoorHeaderHeight + 0.2f && !_L.Has(x, z, CellFlags.NoFloor)
                   && _L.Plain(x, z);                // (no door at steps down into a sunken room, nor onto a pit room's landing)
        }

        void BuildDoors()
        {
            _doors.Clear();
            if (Pool || !DoorModels.HasModel || Mathf.Abs(_L.headerHeight - OfficeBiome.DoorHeaderHeight) > 0.01f) return;
            int n = _L.N;
            // Rooms with a ceiling light on (in this chunk), for the shut doors that show a line of light under them.
            var lit = new HashSet<int>();
            foreach (var f in _L.fixtures) if (f.on) lit.Add(_L.roomOf[f.cell.x, f.cell.y]);
            // (The hub's doorways on a chunk's edge too: the chunk past the edge's line 0 makes those; elsewhere the lines
            // inside the chunk only, as the world always had it.)
            bool hubDoors = _L.zoneName == HubPlan.ZoneName;
            for (int pass = 0; pass < 2; pass++)
            {
                bool vertical = pass == 0;
                for (int line = hubDoors ? 0 : 1; line < n; line++)
                for (int along = 0; along < n; along++)
                {
                    var e = vertical ? _L.vEdges[line, along] : _L.hEdges[along, line];
                    if (e != EdgeKind.DoorHeader || IsArched(vertical, line, along)) continue;
                    bool border = line == 0;
                    int bx = vertical ? line : along, bz = vertical ? along : line;
                    // On the edge, room A is the next chunk's: this side's room stands in for it.
                    int ax = border ? bx : vertical ? line - 1 : along, az = border ? bz : vertical ? along : line - 1;
                    if (border ? !DoorRoom(bx, bz) : (!DoorRoom(ax, az) || !DoorRoom(bx, bz) || _L.roomOf[ax, az] == _L.roomOf[bx, bz])) continue;
                    float chance = Mathf.Min(_L.RulesAt(ax, az).doorChance, _L.RulesAt(bx, bz).doorChance) * DetailRates.Doors;
                    // A good room's one way in always has a door, shut when you find it (SpecialRooms).
                    bool good = _L.RoomAt(ax, az).special != GoodRoom.None || _L.RoomAt(bx, bz).special != GoodRoom.None;
                    if (good) chance = 1f;
                    bool hub = _L.zoneName == HubPlan.ZoneName;                  // the hub: a door in each of its doorways
                    if (hub) chance = 1f;
                    int wx = _L.coord.x * n + (vertical ? line : along), wz = _L.coord.y * n + (vertical ? along : line);
                    uint h = DoorHash(wx, wz, vertical, _L.storey);
                    if ((h & 0xFFFF) / 65535f >= chance) continue;
                    // Swing into one room or the other; mostly shut, some ajar, a few wide open.
                    bool toB = (h & 0x10000u) != 0;
                    float pick = ((h >> 17) & 0xFF) / 255f, amount = ((h >> 25) & 0x7F) / 127f;
                    float angle = good || pick < 0.62f ? 0f : pick < 0.88f ? Mathf.Lerp(8f, 35f, amount) : Mathf.Lerp(70f, 88f, amount);
                    Vector3 pos = vertical ? new Vector3(line * C, 0f, (along + 0.5f) * C) : new Vector3((along + 0.5f) * C, 0f, line * C);
                    // Model +z toward room B (+x or +z of the edge) or room A.
                    float yaw = vertical ? (toB ? 90f : -90f) : (toB ? 0f : 180f);
                    var ra = _L.RoomAt(ax, az); var rb = _L.RoomAt(bx, bz);
                    // Its hardware (2026-11-03): a closer on some, a kick plate on some, a wedge under some wide-open ones.
                    uint h2 = DoorHash(wx + 7, wz - 3, !vertical, _L.storey + 11);
                    bool closer = (h2 & 0xFF) / 255f < 0.15f * DetailRates.WallWear && !good;
                    bool kick = ((h2 >> 8) & 0xFF) / 255f < 0.14f * DetailRates.WallWear;
                    bool wedge = angle > 60f && ((h2 >> 16) & 0xFF) / 255f < 0.45f * DetailRates.FloorMarks;
                    {
                        var face = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;   // the swing side
                        if (closer) Saw(pos + Vector3.up * 2.0f + face * 0.08f, face, "door_closer");
                        if (kick) Saw(pos + Vector3.up * 0.15f - face * 0.05f, -face, "kick_plate");
                        if (wedge) Saw(pos + face * 0.8f, Vector3.up, "door_wedge");
                        // Shut, a lit room one side and none the other: the light shows under it on the dark side.
                        bool la = lit.Contains(_L.roomOf[ax, az]), lb = lit.Contains(_L.roomOf[bx, bz]);
                        if (angle == 0f && la != lb)
                        {
                            var darkSide = (la ? 1f : -1f) * (vertical ? Vector3.right : Vector3.forward);
                            Saw(pos + Vector3.up * 0.05f + darkSide * 0.05f, darkSide, "door_light");
                        }
                    }
                    int gLine = vertical ? _L.coord.x * n + line : _L.coord.y * n + line;
                    int gAlong = vertical ? _L.coord.y * n + along : _L.coord.x * n + along;
                    _doors.Add(new DoorSpec
                    {
                        position = pos, yaw = yaw, angle = angle,
                        mask = LayerBit(ra.color) | LayerBit(rb.color),
                        maskFront = toB ? LayerBit(rb.color) : LayerBit(ra.color),
                        maskBack = toB ? LayerBit(ra.color) : LayerBit(rb.color),
                        key = $"{_L.coord.x},{_L.storey},{_L.coord.y},{(vertical ? 1 : 0)},{line},{along}",
                        edge = new DoorEdges.Edge
                        {
                            vertical = vertical, line = gLine, along = gAlong, storey = _L.storey,
                            chunk = new Vector3Int(_L.coord.x, _L.storey, _L.coord.y), centre = _origin + pos + Vector3.up * 1.05f,
                        },
                        roomFront = toB ? _L.roomOf[bx, bz] : _L.roomOf[ax, az],
                        roomBack = toB ? _L.roomOf[ax, az] : _L.roomOf[bx, bz],
                        // The bolt on the good room's side (the inside).
                        boltSide = !good ? 0 : ((toB ? rb : ra).special != GoodRoom.None ? 1 : -1),
                        closer = closer, kick = kick, wedge = wedge,
                    });
                }
            }
        }

        void InstantiateDoors(Transform root)
        {
            foreach (var d in _doors)
            {
                var go = new GameObject("Door");
                go.transform.SetParent(root, false);
                go.transform.localPosition = d.position;
                go.transform.localRotation = Quaternion.Euler(0f, d.yaw, 0f);
                var model = DoorModels.Make(go.transform, d.mask);
                if (!model) { Object.Destroy(go); continue; }
                DoorHardware.Fit(go.transform, model, d.mask, d.closer, d.kick);      // (on the shut model, before it opens)
                var door = go.AddComponent<Interaction.Door>();
                door.Init(model, d.angle, d.key, d.edge, d.roomFront, d.roomBack);
                if (d.boltSide != 0) door.AddBolt(d.boltSide);
                door.SetLightMasks(d.maskFront, d.maskBack);
                if (d.wedge) DoorHardware.Wedge(root, model, d.mask);
                DoorMarks(model, d, root.GetComponent<GeneratedChunkResources>());
            }
        }
    }

    /// <summary>The door model (Resources/Items/Door.fbx) and its materials: stained wood, satin metal.</summary>
    public static class DoorModels
    {
        static GameObject s_prefab;
        static Material s_wood, s_frame, s_metal;
        /// <summary>The model is loaded (a plain flag: chunk data is built on worker threads).</summary>
        public static bool HasModel { get; private set; }
        /// <summary>The handles' satin metal (the closers too, DoorHardware).</summary>
        public static Material Metal => s_metal;

        static ArtStyle.Look s_look;

        public static void Ensure(OfficeBiomeAssets assets)
        {
            if (s_prefab && s_look == ArtStyle.Current) return;     // (again for the other look's door)
            s_look = ArtStyle.Current;
            s_prefab = Resources.Load<GameObject>(ArtStyle.Path("Items/Door"));
            HasModel = s_prefab;
            if (!HasModel) return;
            // Honey-stained oak: the furniture's wood texture, lighter and warmer.
            var src = assets ? assets.wood : null;
            Material Wood(string name, Color tint)
            {
                Material m = src ? new Material(src) : Interaction.ItemModels.Mat(name, tint);
                m.name = name; m.hideFlags = HideFlags.DontSave;
                if (src)
                {
                    m.SetColor("_BaseColor", tint);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
                    m.SetTextureScale("_BaseMap", Vector2.one);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.42f);
                }
                return MaterialMaps.For(m);
            }
            s_wood = Wood("DoorWood", new Color(1.0f, 0.84f, 0.66f));
            s_frame = Wood("DoorFrame", new Color(0.93f, 0.78f, 0.62f));
            // The photographed door's oak veneer (Resources/Textures/Photo/DoorVeneer: its own honey colour, 1.5 m along the
            // grain by 0.5 m across; the model's UVs are 0.5 m to the unit with the grain along U), and its relief.
            var veneer = Destruction.DemolitionMaterials.Photo("DoorVeneer");
            if (veneer && src)
            {
                var vn = Destruction.DemolitionMaterials.Photo("DoorVeneer", true);
                void Veneer(Material m, float shade)
                {
                    m.SetTexture("_BaseMap", veneer);
                    m.SetTextureScale("_BaseMap", new Vector2(1f / 3f, 1f));
                    m.SetColor("_BaseColor", new Color(shade, shade, shade));
                    if (m.HasProperty("_Color")) m.SetColor("_Color", new Color(shade, shade, shade));
                    if (vn && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", vn); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
                }
                Veneer(s_wood, 1f);
                Veneer(s_frame, 0.9f);
            }
            s_metal = Interaction.ItemModels.Mat("DoorMetal", new Color(0.72f, 0.71f, 0.68f));
        }

        /// <summary>A door under parent (whose frame is the model's), with its materials; null if there is no model.</summary>
        public static Transform Make(Transform parent, uint mask)
        {
            if (!s_prefab) return null;
            var go = Object.Instantiate(s_prefab, parent, false);
            go.name = "DoorModel";
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] ? mats[i].name.Replace(" (Instance)", "") : "";
                    mats[i] = n.Contains("Metal") ? s_metal : n.Contains("Frame") ? s_frame : s_wood;
                }
                r.sharedMaterials = mats;
                r.renderingLayerMask = mask;
                bool handle = r.name.StartsWith("Handle");
                r.shadowCastingMode = handle ? ShadowCastingMode.Off : ShadowCastingMode.On;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            return go.transform;
        }
    }
}
