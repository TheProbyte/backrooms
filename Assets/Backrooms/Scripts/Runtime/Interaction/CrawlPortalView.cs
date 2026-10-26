using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// One crawl hole: a square opening at the foot of a wall, a crawl duct behind it, and at the duct's far end a window
    /// (<see cref="quad"/>) that shows, and leads to, the partner hole somewhere else.
    ///
    /// The duct occupies the room behind the wall, so it is "bigger on the inside": from that room nothing is there
    /// (the duct only draws where the hole's stencil mask marks the screen, and its colliders only exist while the player
    /// is at or in the hole), and the back of the wall is an ordinary wall patch. While the player is inside, the duct
    /// draws normally, the patch hides (so the opening behind them shows the room they came from), and its walls hold
    /// them. Crossing the window at the far end moves the player out of the partner hole, facing out of it.
    ///
    /// Frame: <see cref="position"/> is the hole's centre on the wall face, <see cref="normal"/> points out of the wall into
    /// the room. The duct runs from z = 0 to z = -<see cref="CrawlPortals.DuctLength"/> in <see cref="Local"/>.
    /// </summary>
    public class CrawlPortalView : MonoBehaviour
    {
        public static readonly List<CrawlPortalView> All = new List<CrawlPortalView>();
        public static int Revision { get; private set; }
        public const int Layer = 30;                       // the exit windows; portal cameras never see them (no recursion)

        public Vector3 position, normal, partnerPosition, partnerNormal;
        public ulong id;
        /// <summary>The window at the duct's far end.</summary>
        [System.NonSerialized] public MeshRenderer quad;
        [System.NonSerialized] public Material material;
        /// <summary>Solid wall thickness over the opening: only an aligned crawling player passes it.</summary>
        [System.NonSerialized] public BoxCollider backstop;
        [System.NonSerialized] public MeshRenderer duct, mask, patch;
        [System.NonSerialized] public BoxCollider[] ductColliders;
        BoxCollider _deadEnd;
        Material[] _ductSet;
        CharacterController _ignored;

        /// <summary>The player is in the duct (entered through the opening, not yet out of either end).</summary>
        public bool Inside { get; private set; }
        bool _collision = true, _haveMode;

        public Vector3 ExitPosition => position - normal * CrawlPortals.DuctLength;

        static Mesh _quadMesh, _ductMesh;
        static Shader _viewShader, _ductShader, _maskShader;
        static Material _maskMaterial;
        static readonly Dictionary<(Material, Material), Material[]> _ductMaterials = new Dictionary<(Material, Material), Material[]>();

        /// <param name="wall">Material whose texture lines the duct (null: plain).</param>
        /// <param name="floor">Material whose texture is the duct's floor.</param>
        /// <param name="back">Material of the wall face behind the hole (the patch over the opening seen from the other room).</param>
        /// <param name="backMask">Light (rendering layer) mask of the room behind, for the patch.</param>
        /// <param name="mask">Light mask of the room the hole opens into: the duct's entrance is lit by that room's real lights.</param>
        public static CrawlPortalView Create(Transform parent, CrawlPortal p, Material wall = null, Material floor = null, Material back = null, uint backMask = 1, uint mask = 0xFFFFFFFFu)
        {
            var go = new GameObject("CrawlHole_" + p.id.ToString("X"));
            go.transform.SetParent(parent, true);
            // +Z of the object points into the wall, along the duct.
            go.transform.SetPositionAndRotation(p.position, Quaternion.LookRotation(-p.normal, Vector3.up));
            var v = go.AddComponent<CrawlPortalView>();
            v.position = p.position; v.normal = p.normal; v.partnerPosition = p.partnerPosition; v.partnerNormal = p.partnerNormal; v.id = p.id;
            float L = CrawlPortals.DuctLength, T = OfficeBiome.WallThickness, H = CrawlPortals.HoleHalf;
            if (!_quadMesh) _quadMesh = MakeQuad(CrawlPortals.HoleSize);
            if (!_ductMesh) _ductMesh = MakeDuct();
            if (!_viewShader) _viewShader = Shader.Find("Backrooms/PortalView");
            if (!_ductShader) _ductShader = Shader.Find("Backrooms/CrawlDuct");
            if (!_maskShader) _maskShader = Shader.Find("Backrooms/CrawlMask");

            // Exit window (faces back up the duct, toward the entrance).
            var exit = Child(go.transform, "Exit", new Vector3(0, 0, L), Layer);
            exit.AddComponent<MeshFilter>().sharedMesh = _quadMesh;
            v.quad = Renderer(exit);
            v.material = new Material(_viewShader ? _viewShader : Shader.Find("Universal Render Pipeline/Unlit")) { name = "PortalView" };
            v.material.SetFloat("_Fade", 0f);
            v.quad.sharedMaterial = v.material;

            // Stencil mask over the opening (just in front of the wall face).
            if (_maskShader)
            {
                if (!_maskMaterial) _maskMaterial = new Material(_maskShader) { name = "CrawlMask", hideFlags = HideFlags.DontSave };
                var m = Child(go.transform, "Mask", new Vector3(0, 0, -0.001f), 0);
                m.AddComponent<MeshFilter>().sharedMesh = _quadMesh;
                v.mask = Renderer(m); v.mask.sharedMaterial = _maskMaterial;
            }

            // The duct.
            if (_ductShader)
            {
                var d = Child(go.transform, "Duct", Vector3.zero, 0);
                d.AddComponent<MeshFilter>().sharedMesh = _ductMesh;
                v.duct = Renderer(d);
                v.duct.renderingLayerMask = mask;
                v._ductSet = DuctMaterials(wall, floor);
            }

            // The back of the wall over the opening, as seen from the room behind: an ordinary piece of that wall.
            if (back)
            {
                var b = Child(go.transform, "BackPatch", Vector3.zero, 0);
                b.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);   // world-space mesh, so its UVs match the wall it patches
                b.AddComponent<MeshFilter>().sharedMesh = MakePatch(p.position - p.normal * T, -p.normal);
                v.patch = Renderer(b);
                v.patch.sharedMaterial = back;
                v.patch.renderingLayerMask = backMask;
                v.patch.shadowCastingMode = ShadowCastingMode.Off;
            }

            // Colliders: the wall thickness over the opening (solid except to an aligned crawler) and the duct's sides.
            var seal = new GameObject("PortalBackstop");
            seal.transform.SetParent(go.transform, false);
            v.backstop = seal.AddComponent<BoxCollider>();
            v.backstop.center = Vector3.forward * (T * 0.5f);
            v.backstop.size = new Vector3(CrawlPortals.HoleSize, CrawlPortals.HoleSize, T);
            var cols = new GameObject("DuctColliders");
            cols.transform.SetParent(go.transform, false);
            const float W = 0.3f;
            float len = L + 0.6f, mid = len * 0.5f;
            BoxCollider Box(Vector3 c, Vector3 s) { var bc = cols.AddComponent<BoxCollider>(); bc.center = c; bc.size = s; return bc; }
            v._deadEnd = Box(new Vector3(0, 0, L + 0.45f + W * 0.5f), new Vector3(2 * H + 2 * W, 2 * H + 2 * W, W));
            v.ductColliders = new[]
            {
                Box(new Vector3(-H - W * 0.5f, 0, mid), new Vector3(W, 2 * H + 2 * W, len)),
                Box(new Vector3(H + W * 0.5f, 0, mid), new Vector3(W, 2 * H + 2 * W, len)),
                Box(new Vector3(0, H + W * 0.5f, mid), new Vector3(2 * H, W, len)),
                Box(new Vector3(0, -H - W * 0.5f, mid), new Vector3(2 * H, W, len)),
                v._deadEnd,
            };
            v.SetDuctCollision(false);
            v.ApplyMode();
            v.SetDuctLight(-1);
            return v;
        }

        static GameObject Child(Transform parent, string name, Vector3 local, int layer)
        {
            var g = new GameObject(name) { layer = layer };
            g.transform.SetParent(parent, false);
            g.transform.localPosition = local;
            return g;
        }

        static MeshRenderer Renderer(GameObject g)
        {
            var r = g.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        /// <summary>
        /// Duct materials for a wall/floor finish: [walls, floor] masked (seen through the hole), then [walls, floor] open
        /// (the player is inside). Shared by every duct with that finish.
        /// </summary>
        static Material[] DuctMaterials(Material wall, Material floor)
        {
            if (!_ductMaterials.TryGetValue((wall, floor), out var set))
            {
                Material Make(Material src, string name, bool inside)
                {
                    var m = new Material(_ductShader) { name = name + (inside ? " (inside)" : " (through hole)"), hideFlags = HideFlags.DontSave };
                    var tex = src && src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
                    if (tex) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", src.GetTextureScale("_BaseMap")); m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_BaseMap")); }
                    if (src && src.HasProperty("_BaseColor")) m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
                    if (src && src.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", src.GetFloat("_Smoothness"));   // the entrance is shaded like the room
                    m.SetFloat("_DuctLength", CrawlPortals.DuctLength);
                    // Through the hole: drawn after opaque geometry, only on stencil-marked pixels, not in depth prepasses
                    // (it overlaps the room behind). Inside: ordinary opaque geometry.
                    m.SetFloat("_StencilComp", inside ? (float)CompareFunction.Always : (float)CompareFunction.Equal);
                    m.renderQueue = inside ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent - 400;
                    m.SetShaderPassEnabled("DepthOnly", inside);
                    m.SetShaderPassEnabled("DepthNormals", inside);
                    return m;
                }
                set = new[] { Make(wall, "CrawlDuct", false), Make(floor, "CrawlDuctFloor", false), Make(wall, "CrawlDuct", true), Make(floor, "CrawlDuctFloor", true) };
                _ductMaterials[(wall, floor)] = set;
            }
            return set;
        }

        static Mesh MakeQuad(float size)
        {
            float h = size * 0.5f;
            var m = new Mesh { name = "CrawlPortalQuad" };
            // In the object's frame +Z points into the wall (the quad faces -Z, out toward the room).
            m.vertices = new[] { new Vector3(-h, -h, 0), new Vector3(h, -h, 0), new Vector3(h, h, 0), new Vector3(-h, h, 0) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Inward-facing duct: walls and ceiling (submesh 0, with an end cap behind the exit window) and floor (submesh 1). UVs in metres.</summary>
        static Mesh MakeDuct()
        {
            float h = CrawlPortals.HoleHalf, L = CrawlPortals.DuctLength, floorY = -h + 0.004f, cap = L + 0.03f;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var walls = new List<int>(); var floor = new List<int>();
            void Quad(List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, System.Func<Vector3, Vector2> map)
            {
                int s = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(normal);
                uv.Add(map(a)); uv.Add(map(b)); uv.Add(map(c)); uv.Add(map(d));
                // Front faces are clockwise seen from the side the normal points to.
                bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;
                if (flip) tris.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
                else tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            // Wallpaper UVs: one repeat per cell along, per ceiling height up (as on the walls of the rooms).
            Vector2 Side(Vector3 p) => new Vector2(p.z / OfficeBiome.CellSize, (p.y + h) / OfficeBiome.CeilingHeight);
            Vector2 Top(Vector3 p) => new Vector2(p.x / OfficeBiome.CellSize, p.z / OfficeBiome.CellSize);
            Vector2 Carpet(Vector3 p) => new Vector2(p.x / 3f, p.z / 3f);
            Quad(walls, new Vector3(-h, floorY, 0), new Vector3(-h, floorY, L), new Vector3(-h, h, L), new Vector3(-h, h, 0), Vector3.right, Side);
            Quad(walls, new Vector3(h, floorY, 0), new Vector3(h, floorY, L), new Vector3(h, h, L), new Vector3(h, h, 0), Vector3.left, Side);
            Quad(walls, new Vector3(-h, h, 0), new Vector3(h, h, 0), new Vector3(h, h, L), new Vector3(-h, h, L), Vector3.down, Top);
            Quad(walls, new Vector3(-h, floorY, cap), new Vector3(h, floorY, cap), new Vector3(h, h, cap), new Vector3(-h, h, cap), Vector3.back, Side);
            Quad(floor, new Vector3(-h, floorY, 0), new Vector3(h, floorY, 0), new Vector3(h, floorY, L), new Vector3(-h, floorY, L), Vector3.up, Carpet);
            var m = new Mesh { name = "CrawlDuct" };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.subMeshCount = 2;
            m.SetTriangles(walls, 0); m.SetTriangles(floor, 1);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>World-space patch over the opening on the back face of the wall, with the same UVs as the wall around it.</summary>
        static Mesh MakePatch(Vector3 centre, Vector3 faceNormal)
        {
            float h = CrawlPortals.HoleHalf;
            var right = Vector3.Cross(Vector3.up, faceNormal);
            var a = centre - right * h - Vector3.up * h; var b = centre + right * h - Vector3.up * h;
            var c = b + Vector3.up * 2 * h; var d = a + Vector3.up * 2 * h;
            var m = new Mesh { name = "CrawlHoleBackPatch" };
            m.vertices = new[] { a, b, c, d };
            m.normals = new[] { faceNormal, faceNormal, faceNormal, faceNormal };
            m.uv = new[] { MeshWriter.WallUV(a, faceNormal), MeshWriter.WallUV(b, faceNormal), MeshWriter.WallUV(c, faceNormal), MeshWriter.WallUV(d, faceNormal) };   // world-space, as the chunk's walls
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), faceNormal) < 0f;
            m.triangles = flip ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateTangents();
            m.RecalculateBounds();
            return m;
        }

        void OnEnable() { All.Add(this); Revision++; CrawlPortalSystem.Ensure(); }
        void OnDisable() { SetPassage(null, false); SetInside(false); All.Remove(this); Revision++; }
        void OnDestroy()
        {
            if (material) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            if (patch) { var mf = patch.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) { if (Application.isPlaying) Destroy(mf.sharedMesh); else DestroyImmediate(mf.sharedMesh); } }
            if (All.Count == 0)
            {
                foreach (var mesh in new[] { _quadMesh, _ductMesh }) if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
                _quadMesh = _ductMesh = null;
            }
        }

        public void SetPassage(CharacterController controller, bool open)
        {
            if (_ignored && (_ignored != controller || !open))
            {
                if (backstop) Physics.IgnoreCollision(_ignored, backstop, false);
                _ignored = null;
            }
            if (open && controller && backstop)
            {
                Physics.IgnoreCollision(controller, backstop, true);
                _ignored = controller;
            }
        }

        /// <summary>The duct's colliders exist only while the player is at or in the hole (the room behind can be walked freely).</summary>
        public void SetDuctCollision(bool on, bool deadEnd = true)
        {
            if (ductColliders == null) return;
            if (_deadEnd)
            {
                // With the far end not loaded the duct is a dead end just past the window; otherwise it stops nothing.
                var c = _deadEnd.center; c.z = deadEnd ? CrawlPortals.DuctLength + 0.15f : CrawlPortals.DuctLength + 0.6f;
                _deadEnd.center = c;
            }
            if (on == _collision) return;
            _collision = on;
            foreach (var c in ductColliders) if (c) c.enabled = on;
        }

        static MaterialPropertyBlock _block;
        static readonly int IdDuctMouth = Shader.PropertyToID("_DuctMouth"), IdDuctExitRot = Shader.PropertyToID("_DuctExitRot");
        int _ductLight = int.MinValue;

        /// <summary>
        /// Which pair of rows of the duct mouth light texture (<see cref="CrawlPortalSystem"/>) lights this duct, or -1 for the
        /// shader's default light. Also passes the turn from this hole to its partner, so the far end's walls look up the
        /// light at the partner in the matching directions.
        /// </summary>
        public void SetDuctLight(int index)
        {
            if (index == _ductLight) return;
            _ductLight = index;
            // The window's fallback (shown before its view has a picture) is that measured light too, not a flat dark colour.
            if (material) material.SetFloat(IdDuctMouth, index);
            if (!duct) return;
            _block ??= new MaterialPropertyBlock();
            _block.Clear();
            float yaw = YawDelta * Mathf.Deg2Rad;
            _block.SetFloat(IdDuctMouth, index);
            _block.SetVector(IdDuctExitRot, new Vector4(Mathf.Cos(yaw), Mathf.Sin(yaw), 0, 0));
            duct.SetPropertyBlock(_block);
        }

        /// <summary>Switches the duct between "seen through the hole" and "the player is in it".</summary>
        public void SetInside(bool inside)
        {
            if (inside == Inside && _haveMode) return;
            Inside = inside;
            ApplyMode();
        }

        void ApplyMode()
        {
            _haveMode = true;
            if (duct && _ductSet != null)
                duct.sharedMaterials = Inside ? new[] { _ductSet[2], _ductSet[3] } : new[] { _ductSet[0], _ductSet[1] };
            if (patch) patch.enabled = !Inside;
            if (material)
            {
                material.SetFloat("_StencilComp", Inside ? (float)CompareFunction.Always : (float)CompareFunction.Equal);
                material.renderQueue = Inside ? (int)RenderQueue.Geometry : (int)RenderQueue.Transparent - 390;
            }
        }

        public bool Fits(CharacterController controller)
        {
            if (!controller || controller.height > CrawlPortals.HoleSize - 0.02f) return false;
            var c = Local(controller.transform.TransformPoint(controller.center));
            return Mathf.Abs(c.x) + controller.radius < CrawlPortals.HoleHalf + 0.01f &&
                   Mathf.Abs(c.y) + controller.height * 0.5f < CrawlPortals.HoleHalf + 0.04f;
        }

        /// <summary>
        /// Maps a point or direction at this duct's far end to the matching place at the partner hole: crossing the window
        /// brings you out of the partner's opening, facing out of it.
        /// </summary>
        public Matrix4x4 ToPartner
        {
            get
            {
                var here = Matrix4x4.TRS(ExitPosition, Quaternion.LookRotation(normal, Vector3.up), Vector3.one);
                var there = Matrix4x4.TRS(partnerPosition, Quaternion.LookRotation(-partnerNormal, Vector3.up), Vector3.one);
                return there * here.inverse;
            }
        }

        /// <summary>Yaw (degrees) added to anything that passes through.</summary>
        public float YawDelta => Vector3.SignedAngle(normal, -partnerNormal, Vector3.up);

        /// <summary>Point in this hole's frame: x across, y up from the hole's centre, z out of the wall (the duct is z &lt; 0).</summary>
        public Vector3 Local(Vector3 world)
        {
            var d = world - position;
            var right = Vector3.Cross(Vector3.up, normal);
            return new Vector3(Vector3.Dot(d, right), d.y, Vector3.Dot(d, normal));
        }

        /// <summary>Is the point within the opening's square (with a margin), seen along the duct?</summary>
        public static bool InAperture(Vector3 local, float margin = 0f) =>
            Mathf.Abs(local.x) < CrawlPortals.HoleHalf + margin && Mathf.Abs(local.y) < CrawlPortals.HoleHalf + margin;
    }
}
