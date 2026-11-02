using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Backrooms.Tests
{
    /// <summary>
    /// The screen-space bounce (Resources/ScreenLighting.compute) on a made-up view of a floor meeting a wall, where every
    /// surface gives the same light (the last frame's picture all 1). Light is then the same from every direction, so a
    /// surface must come out equally lit wherever it is: in the open the visibility is 1 and nothing bounces; in the corner
    /// the wall hides part of the floor's sky and gives back exactly what it hides (visibility + bounce = 1). Plain AO
    /// would leave the corner dark; a bounce with the wrong weights would make it brighter or darker than the open floor.
    /// </summary>
    public class ScreenLightingTests
    {
        const int W = 320, H = 240;

        [Test]
        public void BounceReturnsWhatTheOcclusionTakesInAnEvenlyLitCorner()
        {
            if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Compute shader support required");
            var cs = Object.Instantiate(Resources.Load<ComputeShader>("ScreenLighting"));
            var objects = new List<Object> { cs };
            RenderTexture RT(int w, int h, RenderTextureFormat f) { var t = new RenderTexture(w, h, 0, f, RenderTextureReadWrite.Linear) { enableRandomWrite = true }; t.Create(); objects.Add(t); return t; }
            Color[] Read(RenderTexture t)
            {
                var previous = RenderTexture.active; RenderTexture.active = t;
                var cpu = new Texture2D(t.width, t.height, TextureFormat.RGBAFloat, false, true);
                try { cpu.ReadPixels(new Rect(0, 0, t.width, t.height), 0, 0); cpu.Apply(); return cpu.GetPixels(); }
                finally { RenderTexture.active = previous; Object.DestroyImmediate(cpu); }
            }
            try
            {
                // The camera: 1.6 m up, looking 25 degrees down toward a wall 3 m away.
                var go = new GameObject("ScreenLightingTestCamera") { hideFlags = HideFlags.HideAndDontSave };
                objects.Add(go);
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.fieldOfView = 60f; cam.aspect = W / (float)H; cam.nearClipPlane = 0.05f; cam.farClipPlane = 100f;
                go.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, 0f), Quaternion.Euler(25f, 0f, 0f));
                var proj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, true);
                var vp = proj * cam.worldToCameraMatrix;
                var inv = vp.inverse;
                float flip = SystemInfo.graphicsUVStartsAtTop ? -1f : 1f, nearDevice = SystemInfo.usesReversedZBuffer ? 1f : 0f;
                Vector3 camPos = go.transform.position, fwd = go.transform.forward;

                // Depth and normals of the floor (y 0) and the wall (z 3).
                var depth = new Color[W * H];
                var normals = new Color[W * H];
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    var c = new Vector4((x + 0.5f) / W * 2f - 1f, ((y + 0.5f) / H * 2f - 1f) * flip, nearDevice, 1f);
                    var w = inv * c; var p = (Vector3)w / w.w;
                    var d = (p - camPos).normalized;
                    float tFloor = d.y < -1e-4f ? -camPos.y / d.y : float.MaxValue, tWall = d.z > 1e-4f ? (3f - camPos.z) / d.z : float.MaxValue;
                    float t = Mathf.Min(tFloor, tWall);
                    var hit = camPos + d * t;
                    var clip = vp * new Vector4(hit.x, hit.y, hit.z, 1f);
                    depth[y * W + x] = new Color(clip.z / clip.w, 0, 0, 0);
                    normals[y * W + x] = tFloor < tWall ? new Color(0, 1, 0, 0) : new Color(0, 0, -1, 0);
                }
                var depthTex = new Texture2D(W, H, TextureFormat.RFloat, false, true); depthTex.SetPixels(depth); depthTex.Apply(); objects.Add(depthTex);
                var normalTex = new Texture2D(W, H, TextureFormat.RGBAFloat, false, true); normalTex.SetPixels(normals); normalTex.Apply(); objects.Add(normalTex);
                int hw = W / 2, hh = H / 2;
                var white = new Texture2D(hw, hh, TextureFormat.RGBAHalf, true, true);
                var ones = new Color[hw * hh]; for (int i = 0; i < ones.Length; i++) ones[i] = Color.white;
                white.SetPixels(ones); white.Apply(true); objects.Add(white);

                var pos = RT(hw, hh, RenderTextureFormat.ARGBFloat);
                var nrm = RT(hw, hh, RenderTextureFormat.ARGBHalf);
                var trace = RT(hw, hh, RenderTextureFormat.ARGBHalf);
                var geo = RT(hw, hh, RenderTextureFormat.ARGBHalf);
                var hidden = RT(hw, hh, RenderTextureFormat.ARGBHalf);
                int kPrep = cs.FindKernel("Prepare"), kGI = cs.FindKernel("TraceGI");
                // No light grid here (ProbeShare is not run): the occlusion takes an even share.
                float pxPerM = hh / (2f * Mathf.Tan(30f * Mathf.Deg2Rad));
                cs.SetMatrix("_InvVP", inv); cs.SetMatrix("_VP", vp); cs.SetMatrix("_PrevVP", vp);
                cs.SetVector("_CamFwd", new Vector4(fwd.x, fwd.y, fwd.z, nearDevice));
                cs.SetVector("_PrevCam", new Vector4(camPos.x, camPos.y, camPos.z, 1f));
                cs.SetVector("_FullSize", new Vector4(W, H, 1f / W, 1f / H));
                cs.SetVector("_HalfSize", new Vector4(hw, hh, 1f / hw, 1f / hh));
                cs.SetVector("_GIParams", new Vector4(1.5f, 0.25f, pxPerM, 16f));
                cs.SetVector("_GIParams2", new Vector4(4f, 6f, 1f, 1f));
                cs.SetFloat("_FlipY", flip);
                cs.SetFloat("_ReversedZ", SystemInfo.usesReversedZBuffer ? 1f : 0f);
                cs.SetTexture(kPrep, "_CameraDepthTexture", depthTex);
                cs.SetTexture(kPrep, "_CameraNormalsTexture", normalTex);
                cs.SetTexture(kPrep, "_HalfPos", pos);
                cs.SetTexture(kPrep, "_HalfNormal", nrm);
                cs.SetTexture(kPrep, "_HalfGeo", geo);
                cs.SetTexture(kGI, "_HalfGeoR", geo);
                cs.SetTexture(kGI, "_GIHidden", hidden);
                cs.SetTexture(kGI, "_HalfPosR", pos);
                cs.SetTexture(kGI, "_HalfNormalR", nrm);
                cs.SetTexture(kGI, "_PrevPos", pos);
                cs.SetTexture(kGI, "_ColorHist", white);
                cs.SetTexture(kGI, "_GITrace", trace);
                cs.SetVector("_CamPos", new Vector4(camPos.x, camPos.y, camPos.z, 0f));
                cs.Dispatch(kPrep, (hw + 7) / 8, (hh + 7) / 8, 1);
                var where = Read(pos);

                // Averaged over a few frames' jitter.
                var sum = new Vector4[hw * hh];
                const int frames = 8;
                for (int f = 0; f < frames; f++)
                {
                    cs.SetVector("_CamPos", new Vector4(camPos.x, camPos.y, camPos.z, f));
                    cs.Dispatch(kGI, (hw + 7) / 8, (hh + 7) / 8, 1);
                    var g = Read(trace);
                    for (int i = 0; i < g.Length; i++) sum[i] += (Vector4)g[i] / frames;
                }

                (float vis, float gi, int n) Region(System.Func<Vector3, bool> pick)
                {
                    float v = 0, b = 0; int n = 0;
                    for (int i = 0; i < where.Length; i++)
                    {
                        var c = where[i];
                        if (c.a <= 0f || !pick(new Vector3(c.r, c.g, c.b))) continue;
                        v += sum[i].w; b += sum[i].x; n++;
                    }
                    return n > 0 ? (v / n, b / n, n) : (0, 0, 0);
                }
                var open = Region(p => p.y < 0.01f && p.z > 0.9f && p.z < 1.3f && Mathf.Abs(p.x) < 0.6f);
                var corner = Region(p => p.y < 0.01f && p.z > 2.7f && p.z < 2.9f && Mathf.Abs(p.x) < 0.6f);
                var wallFoot = Region(p => p.z > 2.99f && p.y > 0.08f && p.y < 0.25f && Mathf.Abs(p.x) < 0.6f);
                string report = $"open floor vis {open.vis:0.000} bounce {open.gi:0.000} ({open.n} px); corner vis {corner.vis:0.000} bounce {corner.gi:0.000} ({corner.n}); wall foot vis {wallFoot.vis:0.000} bounce {wallFoot.gi:0.000} ({wallFoot.n})";
                Debug.Log("[ScreenLightingTests] " + report);
                Assert.Greater(open.n, 20, report); Assert.Greater(corner.n, 5, report); Assert.Greater(wallFoot.n, 5, report);
                Assert.Greater(open.vis, 0.95f, "nothing hides open floor: " + report);
                Assert.Less(open.gi, 0.05f, report);
                Assert.Less(corner.vis, 0.9f, "the wall hides part of the floor's sky in the corner: " + report);
                Assert.Less(wallFoot.vis, 0.9f, "the floor hides part of the wall's: " + report);
                Assert.That(corner.vis + corner.gi, Is.InRange(0.85f, 1.08f), "the corner is as lit as the open floor: " + report);
                Assert.That(wallFoot.vis + wallFoot.gi, Is.InRange(0.85f, 1.08f), "the wall's foot too: " + report);
            }
            finally
            {
                foreach (var o in objects) if (o) Object.DestroyImmediate(o);
            }
        }
    }
}
