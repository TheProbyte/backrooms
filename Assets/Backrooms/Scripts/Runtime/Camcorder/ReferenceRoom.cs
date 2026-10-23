using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// The hand-built room of the Reference Match scene, reconstructed from one reference photograph: a broad foreground
    /// room with a large opening (thick wall, wall returns, header) into a second room with a recessed back wall and a
    /// suspended ceiling with fluorescent panels. Built from real boxes at start (and by <see cref="Rebuild"/>), with real
    /// colliders and lights; every dimension is a field so the match can be tuned in play mode. Nothing here is a
    /// projection or an overlay of the photograph.
    /// </summary>
    [ExecuteAlways]
    public class ReferenceRoom : MonoBehaviour
    {
        [Header("Materials")]
        public Material wall, olive, carpet, trim, ceilingTile, recess, panel;

        [Header("Opening wall (front face at wallZ)")]
        public float wallZ = 5.2f, wallThickness = 0.5f;
        public float openLeft = -2.1f, openRight = 1.2f, openHeight = 2.35f;
        public float ceilingHeight = 2.7f;
        [Header("Left partition, right return, right header")]
        public float partitionZ = 2.95f, partitionThickness = 0.2f;
        [Tooltip("The pale wall at the left is oblique (about 30 degrees to the view): from A (near, left) to B (where it meets the thick return), world x,z.")]
        public Vector2 obliqueA = new Vector2(-4.17f, 1.08f), obliqueB = new Vector2(-2.3f, 4.6f);
        public float returnX0 = 1.45f, returnX1 = 1.95f, returnDepth = 0.5f;
        [Tooltip("Above this height the walls on the left carry the olive wallpaper band seen at the top left of the reference.")]
        public float oliveBandBottom = 2.7f;
        public float headerRecessDepth = 0.35f, headerRecessBottom = 2.35f;
        [Header("Back room")]
        public float backZ = 12.9f, backLeftX = -5.5f, backRightX = 6.0f;
        public float recessHalfWidth = 1.6f, recessDepth = 0.3f, supportWidth = 0.45f, supportDepth = 0.18f;
        public float darkBandBottom = 2.05f, darkBandTop = 2.3f;
        [Header("Trim")]
        [Tooltip("A narrow light trim along the foot of the walls (off: the reference room has none).")]
        public bool baseboard = false;
        public float trimHeight = 0.075f, trimDepth = 0.012f;
        [Header("Ceiling")]
        [Tooltip("Ceiling tile size: the panels are square, one tile each, centred on a tile.")]
        public float tileSize = 0.6f;
        [Header("Lights")]
        public float panelIntensity = 5.2f, panelRange = 9f, panelSpotAngle = 160f;
        [Tooltip("Inner cone as a fraction of the outer: near 1 spreads a panel's light evenly (no pools on nearby walls).")]
        public float panelInnerFraction = 0.9f;
        [Tooltip("Stand-ins for light bounced off the floor and walls: unshadowed wide spots at floor level pointing up, yellow like the wallpaper they stand for.")]
        public float fillIntensity = 9f, fillRange = 7f, fillAngle = 150f;
        [Tooltip("The back room's bounce stand-ins: brighter and whiter, so the room beyond the opening reads evenly lit and near white.")]
        public float backFillIntensity = 12f, backFillRange = 9f;
        [Tooltip("Back-room panel brightness (the back room is lit evenly, without the front room's left-to-right falloff).")]
        public float backPanelIntensity = 4.2f;
        public Color panelColor = new Color(1f, 0.95f, 0.8f);
        public Color fillColor = new Color(0.8f, 0.7f, 0.05f);
        [Tooltip("Panel brightness at the left and right of the room: the reference is shaded on the left and bright on the right.")]
        public float panelLeftFactor = 0.05f, panelRightFactor = 0.95f;
        public Color fillColorRight = new Color(1f, 0.97f, 0.82f);
        public bool panelShadows = true;
        public Color ambientColor = new Color(0.28f, 0.27f, 0.10f);

        public static readonly Vector2[] ForegroundPanels =
        {
            new Vector2(-2.4f, 1.4f), new Vector2(0f, 1.4f), new Vector2(2.4f, 1.4f), new Vector2(4.8f, 1.4f),
            new Vector2(-2.4f, 3.0f), new Vector2(0f, 3.0f), new Vector2(2.4f, 3.0f), new Vector2(4.8f, 3.0f),
        };
        // Back room: one row down the middle (the two panels seen through the opening) and side panels kept clear of the walls.
        public static readonly Vector2[] BackPanels =
        {
            new Vector2(-0.3f, 7.4f), new Vector2(-0.3f, 9.8f), new Vector2(-0.3f, 11.6f),
            new Vector2(-3.3f, 8.6f), new Vector2(3.3f, 8.6f), new Vector2(-3.3f, 11.0f), new Vector2(3.3f, 11.0f),
        };

        const string GeneratedName = "Generated";
        const float CarpetMeters = 3f;   // one repeat of RefCarpet_Albedo (see BackroomsTextureGenerator.RefCarpetMeters)
        const int NX = 1, PX = 2, NY = 4, PY = 8, NZ = 16, PZ = 32;

        void OnEnable() { Rebuild(); }

        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            var old = transform.Find(GeneratedName);
            if (old) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
            var root = new GameObject(GeneratedName);
            root.transform.SetParent(transform, false);
            root.hideFlags = HideFlags.DontSave;
            BuildGeometry(root.transform);
            BuildLights(root.transform);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor;
        }

        // Tuning shortcut for play mode: apply light parameters without rebuilding the geometry.
        public void ApplyLights()
        {
            var g = transform.Find(GeneratedName);
            if (!g) return;
            foreach (var l in g.GetComponentsInChildren<Light>(true))
            {
                if (l.name.StartsWith("Panel_")) { bool back = l.transform.position.z > 5.5f; l.intensity = back ? backPanelIntensity : panelIntensity * PanelFactor(l.transform.position.x); l.range = panelRange; l.spotAngle = panelSpotAngle; l.innerSpotAngle = panelSpotAngle * (back ? 0.95f : panelInnerFraction); l.color = panelColor; l.shadows = panelShadows ? LightShadows.Soft : LightShadows.None; }
                else if (l.name.StartsWith("Fill_")) { bool back = l.transform.position.z > 6f; l.intensity = back ? backFillIntensity : fillIntensity; l.range = back ? backFillRange : fillRange; l.color = l.transform.position.z > 6f ? fillColorRight : Color.Lerp(fillColor, fillColorRight, Mathf.InverseLerp(-3f, 3f, l.transform.position.x)); l.spotAngle = fillAngle; l.innerSpotAngle = fillAngle * 0.5f; }
            }
            RenderSettings.ambientLight = ambientColor;
        }

        // ---------------- Geometry ----------------

        void BuildGeometry(Transform root)
        {
            var writers = new Dictionary<Material, MeshWriter>();
            var colliders = new List<(Vector3 c, Vector3 s)>();
            var rotated = new List<(Vector3 c, Vector3 s, Quaternion r)>();
            MeshWriter W(Material m) { if (!writers.TryGetValue(m, out var w)) writers[m] = w = new MeshWriter(); return w; }

            // World-space projections so textures run continuously across surfaces.
            MeshWriter.UVProjector wallUV = MeshWriter.WallUV;
            MeshWriter.UVProjector floorUV = (p, n) => new Vector2(p.x / CarpetMeters, p.z / CarpetMeters);
            MeshWriter.UVProjector ceilUV = (p, n) => new Vector2(p.x / (tileSize * 4f), p.z / (tileSize * 4f));
            MeshWriter.UVProjector flat = (p, n) => Vector2.zero;

            void Box(Material m, float x0, float y0, float z0, float x1, float y1, float z1, int mask, MeshWriter.UVProjector uv, bool collide = true)
            {
                if (!m) return;
                var c = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
                var s = new Vector3(x1 - x0, y1 - y0, z1 - z0);
                W(m).Box(c, s, mask, uv);
                if (collide) colliders.Add((c, s));
            }

            void RBox(Material m, Vector2 a, Vector2 b, float height, float thick, int mask, bool collide, float y0 = 0f)
            {
                if (!m) return;
                var d = b - a; float len = d.magnitude; if (len < 0.01f) return;
                float yaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                var c = new Vector3((a.x + b.x) * 0.5f, y0 + (height - y0) * 0.5f, (a.y + b.y) * 0.5f);
                var s3 = new Vector3(thick, height - y0, len);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var dir = d / len;
                W(m).Box(c, s3, rot, mask, (p, n) => new Vector2((p.x * dir.x + p.z * dir.y) / OfficeBiome.CellSize, p.y / OfficeBiome.CeilingHeight));
                if (collide) rotated.Add((c, s3, rot));
                if (y0 > 0f || !baseboard || !trim) return;
                // narrow light trim along the wall's foot, on its visible side
                var nrm = new Vector3(dir.y, 0f, -dir.x);
                var tc = new Vector3(c.x, trimHeight * 0.5f, c.z) + nrm * (thick * 0.5f + trimDepth * 0.5f);
                W(trim).Box(tc, new Vector3(trimDepth, trimHeight, len), rot, 2 | 8, (p, n) => Vector2.zero);
            }
            float H = ceilingHeight;
            Box(carpet, -9f, -0.3f, -5f, 9f, 0f, 16f, PY, floorUV);                                  // one carpet slab through both rooms
            Box(ceilingTile, -9f, H, -5f, 9f, H + 0.2f, 16f, NY, ceilUV);

            float z0 = wallZ, z1 = wallZ + wallThickness;
            Box(wall, -9f, 0f, z0, openLeft, H, z1, NZ | PX | PZ, wallUV);                           // left of the opening (jamb faces +X)
            Box(wall, openRight, 0f, z0, 9f, H, z1, NZ | NX | PZ, wallUV);                           // right of the opening (jamb faces -X)
            Box(olive, openLeft, openHeight, z0, openRight, H, z1, NZ | NY | PZ, wallUV);            // header: olive wallpaper, soffit visible
            // The oblique pale wall on the left and the thick olive return that joins it to the opening.
            var bExt = obliqueB + (obliqueB - obliqueA).normalized * partitionThickness;   // overlap at the corner (no seam)
            RBox(wall, obliqueA, bExt, oliveBandBottom, partitionThickness, PX, true);
            RBox(wall, obliqueB, new Vector2(openLeft, z0), oliveBandBottom, partitionThickness, PX, true);
            RBox(olive, obliqueA, bExt, H, partitionThickness, PX, false, oliveBandBottom);
            RBox(olive, obliqueB, new Vector2(openLeft, z0), H, partitionThickness, PX, false, oliveBandBottom);
            // (a short square-on stub closes the room's left side behind them)
            Box(wall, -9f, 0f, partitionZ - 6f, -8.8f, H, partitionZ, PX, wallUV);
            Box(wall, returnX0, 0f, z0 - returnDepth, returnX1, H, z0, NZ | NX | PX, wallUV);        // projecting vertical return
            Box(wall, openRight, headerRecessBottom, z0 - headerRecessDepth, 9f, H, z0, NZ | NY, wallUV);   // shallow header along the right wall

            // Back room.
            Box(wall, backLeftX, 0f, z1, backLeftX + 0.2f, H, backZ + 0.4f, PX, wallUV);
            Box(wall, backRightX, 0f, z1, backRightX + 0.2f, H, backZ + 0.4f, NX, wallUV);
            Box(wall, backLeftX, 0f, backZ, -recessHalfWidth, H, backZ + 0.4f, NZ, wallUV);
            Box(wall, recessHalfWidth, 0f, backZ, backRightX + 0.2f, H, backZ + 0.4f, NZ, wallUV);
            float rz = backZ + recessDepth;
            Box(wall, -recessHalfWidth, 0f, rz, recessHalfWidth, darkBandBottom, rz + 0.1f, NZ, wallUV);
            Box(wall, -recessHalfWidth, darkBandTop, rz, recessHalfWidth, H, rz + 0.1f, NZ, wallUV);
            Box(recess, -recessHalfWidth, darkBandBottom, rz, recessHalfWidth, darkBandTop, rz + 0.1f, NZ, flat);
            Box(wall, -recessHalfWidth - supportWidth, 0f, backZ - supportDepth, -recessHalfWidth, H, backZ, NZ | PX | NX, wallUV);
            Box(wall, recessHalfWidth, 0f, backZ - supportDepth, recessHalfWidth + supportWidth, H, backZ, NZ | PX | NX, wallUV);

            // Trim: narrow, light, close to the wall colour.
            float th = trimHeight, td = trimDepth;
            if (!baseboard) th = 0f;
            void TrimZ(float xa, float xb, float z, int sign) { if (th > 0f) Box(trim, xa, 0f, sign < 0 ? z - td : z, xb, th, sign < 0 ? z : z + td, sign < 0 ? NZ | PY | NX | PX : PZ | PY | NX | PX, flat, false); }
            void TrimX(float x, float za, float zb, int sign) { if (th > 0f) Box(trim, sign < 0 ? x - td : x, 0f, za, sign < 0 ? x : x + td, th, zb, sign < 0 ? NX | PY | NZ | PZ : PX | PY | NZ | PZ, flat, false); }
            TrimZ(-9f, openLeft, z0, -1);
            TrimZ(openRight, returnX0, z0, -1); TrimZ(returnX1, 9f, z0, -1);
            TrimX(returnX0, z0 - returnDepth, z0, -1); TrimX(returnX1, z0 - returnDepth, z0, 1);
            TrimZ(backLeftX, -recessHalfWidth - supportWidth, backZ, -1); TrimZ(recessHalfWidth + supportWidth, backRightX + 0.2f, backZ, -1);
            TrimZ(-recessHalfWidth, recessHalfWidth, rz, -1);

            int i = 0;
            foreach (var kv in writers)
            {
                if (kv.Value.VertexCount == 0) continue;
                var go = new GameObject(kv.Key.name + "_" + i++);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = kv.Value.ToMesh(go.name);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            var col = new GameObject("Colliders");
            col.transform.SetParent(root, false);
            foreach (var (c, s) in colliders) { var b = col.AddComponent<BoxCollider>(); b.center = c; b.size = s; }
            foreach (var (c, s, r) in rotated)
            {
                var go = new GameObject("ObliqueCollider");
                go.transform.SetParent(col.transform, false);
                go.transform.localPosition = c; go.transform.localRotation = r;
                go.AddComponent<BoxCollider>().size = s;
            }
        }

        // ---------------- Lights ----------------

        void BuildLights(Transform root)
        {
            var lens = new MeshWriter();
            int n = 0;
            foreach (var p in ForegroundPanels) AddPanel(root, p, lens, n++);
            foreach (var p in BackPanels) AddPanel(root, p, lens, n++, true);
            if (panel)
            {
                var go = new GameObject("PanelLenses");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = lens.ToMesh("Panels");
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = panel; r.shadowCastingMode = ShadowCastingMode.Off;
            }
            // Bounce stand-ins (see Docs/ReferenceMatch.md): the real thing would be baked or probe-based indirect light.
            int f = 0;
            foreach (var p in new[] { new Vector3(-3f, 0.15f, 1.6f), new Vector3(0f, 0.15f, 1.6f), new Vector3(3f, 0.15f, 1.6f), new Vector3(-3f, 0.15f, 3.2f), new Vector3(0f, 0.15f, 3.2f), new Vector3(3f, 0.15f, 3.2f),
                                      new Vector3(-2f, 0.15f, 8f), new Vector3(2f, 0.15f, 8f), new Vector3(-2f, 0.15f, 10f), new Vector3(2f, 0.15f, 10f) })
            {
                var go = new GameObject("Fill_" + f++);
                go.transform.SetParent(root, false);
                go.transform.position = p;
                go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = fillAngle; l.innerSpotAngle = fillAngle * 0.5f;
                bool back = p.z > 6f;
                l.range = back ? backFillRange : fillRange; l.intensity = back ? backFillIntensity : fillIntensity; l.color = p.z > 6f ? fillColorRight : Color.Lerp(fillColor, fillColorRight, Mathf.InverseLerp(-3f, 3f, p.x)); l.shadows = LightShadows.None;
                go.AddComponent<UniversalAdditionalLightData>().renderingLayers = 0xFFFFFFFFu;
            }
        }

        float PanelFactor(float x) => Mathf.Lerp(panelLeftFactor, panelRightFactor, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-4.5f, 4.5f, x)));

        float SnapToTile(float v) => (Mathf.Floor(v / tileSize) + 0.5f) * tileSize;

        void AddPanel(Transform root, Vector2 p, MeshWriter lens, int i, bool back = false)
        {
            p = new Vector2(SnapToTile(p.x), SnapToTile(p.y));
            float side = tileSize - 0.02f;
            lens.Box(new Vector3(p.x, ceilingHeight - 0.004f, p.y), new Vector3(side, 0.006f, side), NY | NX | PX | NZ | PZ, MeshWriter.MetreUV);
            var go = new GameObject("Panel_" + i);
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(p.x, ceilingHeight - 0.06f, p.y);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot; l.spotAngle = panelSpotAngle; l.innerSpotAngle = panelSpotAngle * (back ? 0.95f : panelInnerFraction);
            l.range = panelRange; l.intensity = back ? backPanelIntensity : panelIntensity * PanelFactor(p.x); l.color = panelColor;
            l.shadows = panelShadows ? LightShadows.Soft : LightShadows.None; l.shadowStrength = 0.9f;
            go.AddComponent<UniversalAdditionalLightData>().renderingLayers = 0xFFFFFFFFu;
        }
    }
}
