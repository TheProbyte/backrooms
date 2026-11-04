using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The wall outlet and light switch models (Tools/Blender/build_items2.py, Resources/Items/WallOutlet.fbx and
    /// LightSwitch.fbx: 70 x 115 mm plates, origin at the middle of the plate's back, facing +Z). Their mesh data is read
    /// once on the main thread (WorldStreamer.Awake) and copied into each chunk's meshes by the chunk builder on its worker
    /// thread, so the hundreds of plates in view cost no extra objects. Materials are the items' (ItemModels: Backrooms/Lit
    /// with a fine plastic or metal normal map), one per material slot.
    /// </summary>
    public static class WallPlateModels
    {
        public class Part
        {
            public Material material;
            public Vector3[] vertices, normals;
            public Vector4[] tangents;
            public Vector2[] uv;
            public int[] triangles;
            public bool toggle;          // the switch's toggle: not baked, it is its own object (Interaction.LightSwitch)
        }

        /// <summary>The switch's toggle as a mesh of its own (model frame), and its material.</summary>
        public static Mesh ToggleMesh { get; private set; }
        public static Material ToggleMaterial { get; private set; }
        /// <summary>The toggle mesh exists (a plain flag: chunk data is built on worker threads, away from the engine).</summary>
        public static bool HasToggle { get; private set; }

        static readonly HashSet<Material> s_materials = new HashSet<Material>();

        /// <summary>A plate material: too small to cast shadows worth drawing (and its toggle would shadow itself).</summary>
        public static bool IsPlate(Material m) => m && s_materials.Contains(m);

        public static Part[] Outlet { get; private set; }
        public static Part[] Switch { get; private set; }

        public static void Ensure()
        {
            if (Outlet == null) Outlet = Load("WallOutlet");
            if (Switch == null) Switch = Load("LightSwitch");
            if (!ToggleMesh && Switch != null)
                foreach (var p in Switch)
                {
                    if (!p.toggle) continue;
                    // Only the toggle's own vertices.
                    var map = new Dictionary<int, int>();
                    var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<Vector4>(); var uv = new List<Vector2>();
                    var tris = new int[p.triangles.Length];
                    for (int i = 0; i < tris.Length; i++)
                    {
                        int k = p.triangles[i];
                        if (!map.TryGetValue(k, out int m))
                        {
                            m = v.Count; map[k] = m;
                            v.Add(p.vertices[k]); n.Add(p.normals[k]); t.Add(p.tangents[k]); uv.Add(p.uv[k]);
                        }
                        tris[i] = m;
                    }
                    var mesh = new Mesh { name = "SwitchToggle" };
                    mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTangents(t); mesh.SetUVs(0, uv); mesh.SetTriangles(tris, 0);
                    mesh.RecalculateBounds();
                    ToggleMesh = mesh;
                    ToggleMaterial = p.material;
                    HasToggle = true;
                    break;
                }
        }

        static Part[] Load(string name)
        {
            var prefab = Resources.Load<GameObject>("Items/" + name);
            if (!prefab) return null;
            var root = prefab.transform;
            // Into the root's parent space, keeping the import's axis fix (the same frame ItemModels.Attach gives).
            var toModel = Matrix4x4.TRS(Vector3.zero, root.localRotation, root.localScale) * root.worldToLocalMatrix;
            var parts = new List<Part>();
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
                    var src = s < mats.Length ? mats[s] : null;
                    Color? col = null;
                    if (src && src.HasProperty("_BaseColor")) col = src.GetColor("_BaseColor");
                    else if (src && src.HasProperty("_Color")) col = src.GetColor("_Color");
                    // The screws and the toggle are the plate's own ivory plastic: one material (one draw per room)
                    // for all of it but the outlet's dark slots.
                    string slot = src ? src.name.Replace(" (Instance)", "") : "PlatePlastic";
                    bool toggle = slot == "SwitchToggle";
                    if (slot == "Screw" || toggle) { slot = "PlatePlastic"; col = null; }
                    var mat = Interaction.ItemModels.Mat(slot, col);
                    s_materials.Add(mat);
                    parts.Add(new Part
                    {
                        material = mat,
                        vertices = outV, normals = outN, tangents = outT, uv = uv,
                        triangles = mesh.GetTriangles(s),
                        toggle = toggle,
                    });
                }
            }
            return parts.Count > 0 ? parts.ToArray() : null;
        }
    }
}
