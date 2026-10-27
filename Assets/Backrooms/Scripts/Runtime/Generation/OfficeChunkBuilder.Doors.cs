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
            public string key;
            public DoorEdges.Edge edge;   // the doorway in world cells (shut, it is a wall to light and sound)
            public int roomFront, roomBack; // rooms on the +z (swing) side and the other side (this chunk's indices)
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
            return !r.noFloor && !r.custom && r.role == AnnexRole.None && r.kind != RoomKind.Crawl && !r.spiral && !r.openAbove
                   && r.ceiling >= OfficeBiome.DoorHeaderHeight + 0.2f && !_L.Has(x, z, CellFlags.NoFloor);
        }

        void BuildDoors()
        {
            _doors.Clear();
            if (Pool || !DoorModels.HasModel || Mathf.Abs(_L.headerHeight - OfficeBiome.DoorHeaderHeight) > 0.01f) return;
            int n = _L.N;
            for (int pass = 0; pass < 2; pass++)
            {
                bool vertical = pass == 0;
                for (int line = 1; line < n; line++)
                for (int along = 0; along < n; along++)
                {
                    var e = vertical ? _L.vEdges[line, along] : _L.hEdges[along, line];
                    if (e != EdgeKind.DoorHeader || IsArched(vertical, line, along)) continue;
                    int ax = vertical ? line - 1 : along, az = vertical ? along : line - 1;
                    int bx = vertical ? line : along, bz = vertical ? along : line;
                    if (!DoorRoom(ax, az) || !DoorRoom(bx, bz) || _L.roomOf[ax, az] == _L.roomOf[bx, bz]) continue;
                    float chance = Mathf.Min(_L.RulesAt(ax, az).doorChance, _L.RulesAt(bx, bz).doorChance);
                    int wx = _L.coord.x * n + (vertical ? line : along), wz = _L.coord.y * n + (vertical ? along : line);
                    uint h = DoorHash(wx, wz, vertical, _L.storey);
                    if ((h & 0xFFFF) / 65535f >= chance) continue;
                    // Swing into one room or the other; mostly shut, some ajar, a few wide open.
                    bool toB = (h & 0x10000u) != 0;
                    float pick = ((h >> 17) & 0xFF) / 255f, amount = ((h >> 25) & 0x7F) / 127f;
                    float angle = pick < 0.62f ? 0f : pick < 0.88f ? Mathf.Lerp(8f, 35f, amount) : Mathf.Lerp(70f, 88f, amount);
                    Vector3 pos = vertical ? new Vector3(line * C, 0f, (along + 0.5f) * C) : new Vector3((along + 0.5f) * C, 0f, line * C);
                    // Model +z toward room B (+x or +z of the edge) or room A.
                    float yaw = vertical ? (toB ? 90f : -90f) : (toB ? 0f : 180f);
                    var ra = _L.RoomAt(ax, az); var rb = _L.RoomAt(bx, bz);
                    int gLine = vertical ? _L.coord.x * n + line : _L.coord.y * n + line;
                    int gAlong = vertical ? _L.coord.y * n + along : _L.coord.x * n + along;
                    _doors.Add(new DoorSpec
                    {
                        position = pos, yaw = yaw, angle = angle,
                        mask = LayerBit(ra.color) | LayerBit(rb.color),
                        key = $"{_L.coord.x},{_L.storey},{_L.coord.y},{(vertical ? 1 : 0)},{line},{along}",
                        edge = new DoorEdges.Edge
                        {
                            vertical = vertical, line = gLine, along = gAlong, storey = _L.storey,
                            chunk = new Vector3Int(_L.coord.x, _L.storey, _L.coord.y), centre = _origin + pos + Vector3.up * 1.05f,
                        },
                        roomFront = toB ? _L.roomOf[bx, bz] : _L.roomOf[ax, az],
                        roomBack = toB ? _L.roomOf[ax, az] : _L.roomOf[bx, bz],
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
                go.AddComponent<Interaction.Door>().Init(model, d.angle, d.key, d.edge, d.roomFront, d.roomBack);
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

        public static void Ensure(OfficeBiomeAssets assets)
        {
            if (s_prefab) return;
            s_prefab = Resources.Load<GameObject>("Items/Door");
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
