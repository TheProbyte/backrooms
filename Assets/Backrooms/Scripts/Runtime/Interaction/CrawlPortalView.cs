using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// One end of a crawl hole: a quad over the opening that shows what lies beyond the other end, rendered by
    /// <see cref="CrawlPortalSystem"/> each frame it is in view. Crossing it (crawling into the hole) moves the player to the
    /// other end. Frames are world space: position on the wall face, normal out of the wall.
    /// </summary>
    public class CrawlPortalView : MonoBehaviour
    {
        public static readonly List<CrawlPortalView> All = new List<CrawlPortalView>();
        public const int Layer = 30;                       // portal cameras never see portal quads (no recursion)

        public Vector3 position, normal, partnerPosition, partnerNormal;
        public ulong id;
        [System.NonSerialized] public MeshRenderer quad;
        [System.NonSerialized] public Material material;

        static Mesh _quadMesh;
        static Shader _shader;

        public static CrawlPortalView Create(Transform parent, CrawlPortal p)
        {
            var go = new GameObject("CrawlPortal_" + p.id.ToString("X"));
            go.transform.SetParent(parent, true);
            go.layer = Layer;
            go.transform.SetPositionAndRotation(p.position + p.normal * 0.02f, Quaternion.LookRotation(-p.normal, Vector3.up));   // at the crossing plane, facing back along the tunnel
            var v = go.AddComponent<CrawlPortalView>();
            v.position = p.position; v.normal = p.normal; v.partnerPosition = p.partnerPosition; v.partnerNormal = p.partnerNormal; v.id = p.id;
            if (!_quadMesh) _quadMesh = MakeQuad(CrawlPortals.HoleSize);
            if (!_shader) _shader = Shader.Find("Backrooms/PortalView");
            go.AddComponent<MeshFilter>().sharedMesh = _quadMesh;
            v.quad = go.AddComponent<MeshRenderer>();
            v.quad.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            v.quad.receiveShadows = false;
            v.quad.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            v.quad.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            v.material = new Material(_shader ? _shader : Shader.Find("Universal Render Pipeline/Unlit")) { name = "PortalView" };
            v.material.SetFloat("_Fade", 0f);
            v.quad.sharedMaterial = v.material;
            return v;
        }

        static Mesh MakeQuad(float size)
        {
            float h = size * 0.5f;
            var m = new Mesh { name = "CrawlPortalQuad" };
            // In the object's frame +Z points into the wall (the quad faces -Z, out into the room).
            m.vertices = new[] { new Vector3(-h, -h, 0), new Vector3(h, -h, 0), new Vector3(h, h, 0), new Vector3(-h, h, 0) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            m.RecalculateBounds();
            return m;
        }

        void OnEnable() { All.Add(this); CrawlPortalSystem.Ensure(); }
        void OnDisable() { All.Remove(this); }
        void OnDestroy() { if (material) Destroy(material); }

        /// <summary>Maps a point or direction near this end to the matching place at the other end (turned half round, so going in comes out).</summary>
        public Matrix4x4 ToPartner
        {
            get
            {
                var here = Matrix4x4.TRS(position, Quaternion.LookRotation(normal, Vector3.up), Vector3.one);
                var there = Matrix4x4.TRS(partnerPosition, Quaternion.LookRotation(-partnerNormal, Vector3.up), Vector3.one);
                return there * here.inverse;
            }
        }

        /// <summary>Yaw (degrees) added to anything that passes through.</summary>
        public float YawDelta => Vector3.SignedAngle(normal, -partnerNormal, Vector3.up);

        /// <summary>Point in this hole's frame: x across, y up from the hole's centre, z out of the wall.</summary>
        public Vector3 Local(Vector3 world)
        {
            var d = world - position;
            var right = Vector3.Cross(Vector3.up, normal);
            return new Vector3(Vector3.Dot(d, right), d.y, Vector3.Dot(d, normal));
        }
    }
}
