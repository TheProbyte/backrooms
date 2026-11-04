using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Generation
{
    /// <summary>
    /// The modelled fittings and leftovers of Level 0 (Tools/Blender/build_fittings.py, Resources/Fittings/*.fbx, 2026-11-03):
    /// fire gear, thermostats, grilles, fountains, filled windows, corner guards, door hardware, floor boxes, the bucket and
    /// the cubicle panels, at real sizes, bevelled, with weighted normals and printed labels. Their mesh data is read once on
    /// the main thread (WorldStreamer.Awake, like WallPlateModels) and copied into each chunk's meshes by the chunk builder
    /// on its worker thread; each material slot is given its finish by name (FittingMaterials.ForSlot). Door hardware, which
    /// moves with its door, is made as meshes of its own (MakeMesh).
    /// </summary>
    public static class FittingModels
    {
        public class Part
        {
            public string slot;
            public Material material;
            public Vector3[] vertices, normals;
            public Vector4[] tangents;
            public Vector2[] uv;
            public int[] triangles;
        }

        static readonly string[] Names =
        {
            "Extinguisher", "ExtinguisherCabinet", "ExtinguisherBracket", "PullStation", "HornStrobe", "ThermostatBox",
            "ThermostatRound", "ReturnGrille", "DrinkingFountain", "FilledWindow", "FilledWindowMullion", "FilledWindowCross",
            "CornerGuardL", "CornerGuardR", "CloserBody", "CloserArm", "CloserForearm", "CloserShoe", "KickPlate", "DoorWedge",
            "FloorBoxRound", "FloorBoxSquare", "FloorBoxOpen", "Bucket", "Partition_120_135", "Partition_120_165",
            "Partition_90_135", "Partition_90_165",
            // The good rooms' furniture (Tools/Blender/build_rooms.py, 2026-11-04: baked).
            "Fridge", "Microwave", "DiningTable", "Desk", "CrtMonitor", "Keyboard", "MopSink", "ServiceFaucet", "Mop",
            "BreakerPanel", "Shelving_100", "Shelving_110", "CounterCabinet", "CounterSink", "PaintCan", "CleanerJug",
            // The ceiling lights' parts (Tools/Blender/build_lights.py, 2026-11-04).
            "TrofferFrame", "TrofferHousing", "StripChannel", "StripTube", "ChainLinks", "CableGripper", "CableAnchor",
            // The warehouse's pallets and rack hardware (Tools/Blender/build_warehouse.py, 2026-11-04).
            "Pallet_0", "Pallet_1", "Pallet_2", "RackBasePlate", "RackConnector", "RackLabel_0", "RackLabel_1", "RackLabel_2",
            "RackLabel_3", "ColumnGuard", "LoadSign",
            // The Poolrooms' fittings (Tools/Blender/build_pool.py, 2026-11-04; drawn there with PoolLit copies).
            "LifeguardChair", "PoolShower", "LadderTread", "StallBracket", "CoatHook", "BleacherCap", "BleacherBracket",
            // The drinks vending machine (Tools/Blender/build_vending.py, 2026-11-04).
            "VendingMachine", "VendingGlow", "VendingPlug",
            // Signs (Tools/Blender/build_signs.py, 2026-11-04).
            "ExitSign", "ExitCombo", "ExitFace", "StopSign",
            // Chairs (Tools/Blender/build_chairs.py, 2026-11-04): movable, built from these at spawn.
            "LawnChair_White", "LawnChair_Green", "SchoolChair_Blue", "SchoolChair_Orange",
            // Carts (Tools/Blender/build_carts.py, 2026-11-04): movable; their casters' forks and wheels move (CartWheels).
            "UtilityCart", "JanitorCart", "PlatformTruck", "Caster4Swivel", "Caster4Rigid", "Caster5Swivel", "Caster5Rigid",
            "Wheel4", "Wheel5", "Wheel8",
        };

        static readonly Dictionary<string, Part[]> s_models = new Dictionary<string, Part[]>();
        static readonly Dictionary<string, (Mesh mesh, Material[] mats)> s_meshes = new Dictionary<string, (Mesh, Material[])>();

        /// <summary>The models are loaded (a plain flag: chunk data is built on worker threads).</summary>
        public static bool Ready { get; private set; }

        public static void Ensure(OfficeBiomeAssets a)
        {
            if (Ready) return;
            FittingMaterials.Ensure(a);
            if (!FittingMaterials.Ready) return;
            foreach (var n in Names)
            {
                var parts = Load(n);
                if (parts != null) s_models[n] = parts;
            }
            Ready = s_models.Count > 0;
        }

        /// <summary>A model's parts, or null if it is not there.</summary>
        public static Part[] Get(string name) => s_models.TryGetValue(name, out var p) ? p : null;

        static Part[] Load(string name)
        {
            var prefab = Resources.Load<GameObject>("Fittings/" + name);
            if (!prefab) return null;
            var root = prefab.transform;
            var toModel = Matrix4x4.TRS(Vector3.zero, root.localRotation, root.localScale) * root.worldToLocalMatrix;
            var parts = new List<Part>();
            // A baked model (Tools/Blender/bake_kit.py): its own colour texture through uv0, a material a slot on it.
            var albedo = Resources.Load<Texture2D>("Fittings/" + name + "_Albedo");
            float density = 0f;
            if (albedo)
            {
                var allV = new List<Vector3>(); var allUv = new List<Vector2>(); var allT = new List<int>();
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = mf.sharedMesh;
                    if (!mesh || !mesh.isReadable) continue;
                    var mm = toModel * mf.transform.localToWorldMatrix;
                    int b0 = allV.Count;
                    foreach (var p in mesh.vertices) allV.Add(mm.MultiplyPoint3x4(p));
                    var u = mesh.uv;
                    for (int i = 0; i < mesh.vertexCount; i++) allUv.Add(i < u.Length ? u[i] : Vector2.zero);
                    foreach (int i in mesh.triangles) allT.Add(b0 + i);
                }
                density = BakedMaterials.UvPerMetre(allV, allUv, allT);
            }
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                var mr = mf.GetComponent<MeshRenderer>();
                if (!mesh || !mr || !mesh.isReadable) continue;
                var m = toModel * mf.transform.localToWorldMatrix;
                var v = mesh.vertices; var n = mesh.normals; var t = mesh.tangents; var uv = mesh.uv;
                var outV = new Vector3[v.Length]; var outN = new Vector3[v.Length]; var outT = new Vector4[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    outV[i] = m.MultiplyPoint3x4(v[i]);
                    outN[i] = i < n.Length ? m.MultiplyVector(n[i]).normalized : Vector3.forward;
                    var tt = i < t.Length ? t[i] : new Vector4(1f, 0f, 0f, 1f);
                    var td = m.MultiplyVector(new Vector3(tt.x, tt.y, tt.z)).normalized;
                    outT[i] = new Vector4(td.x, td.y, td.z, tt.w);
                }
                if (uv.Length != v.Length) uv = new Vector2[v.Length];
                var mats = mr.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    string slot = s < mats.Length && mats[s] ? mats[s].name.Replace(" (Instance)", "") : "Default";
                    parts.Add(new Part
                    {
                        slot = slot,
                        material = albedo ? BakedMaterials.For(slot, albedo, density, FittingMaterials.Source) ?? FittingMaterials.ForSlot(slot) : FittingMaterials.ForSlot(slot),
                        vertices = outV, normals = outN, tangents = outT, uv = uv, triangles = mesh.GetTriangles(s),
                    });
                }
            }
            return parts.Count > 0 ? parts.ToArray() : null;
        }

        /// <summary>A model as one mesh of its own (a submesh a slot) and its materials, for things that move (door hardware).</summary>
        public static bool MakeMesh(string name, out Mesh mesh, out Material[] materials)
        {
            mesh = null; materials = null;
            if (s_meshes.TryGetValue(name, out var c)) { mesh = c.mesh; materials = c.mats; return mesh; }
            var parts = Get(name);
            if (parts == null) return false;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<Vector4>(); var uv = new List<Vector2>();
            var subs = new List<List<int>>();
            var mats = new List<Material>();
            foreach (var p in parts)
            {
                var map = new Dictionary<int, int>();
                var tris = new List<int>(p.triangles.Length);
                foreach (int i in p.triangles)
                {
                    if (!map.TryGetValue(i, out int j))
                    {
                        j = v.Count; map[i] = j;
                        v.Add(p.vertices[i]); n.Add(p.normals[i]); t.Add(p.tangents[i]); uv.Add(p.uv[i]);
                    }
                    tris.Add(j);
                }
                subs.Add(tris); mats.Add(p.material);
            }
            mesh = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTangents(t); mesh.SetUVs(0, uv);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i, false);
            mesh.RecalculateBounds();
            materials = mats.ToArray();
            s_meshes[name] = (mesh, materials);
            return true;
        }

        /// <summary>A moving model's object under parent (no colliders), on the given light layers.</summary>
        public static Transform Spawn(string name, Transform parent, uint mask, Vector3 localPos, Quaternion localRot, Vector3? scale = null)
        {
            if (!MakeMesh(name, out var mesh, out var mats)) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = scale ?? Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.renderingLayerMask = mask;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }
    }
}
