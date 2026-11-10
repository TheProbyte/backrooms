using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The round ceiling fitting (a home's flush mount, a plaster room's dome; 2026-10-31): the classic early-2000s
    /// "boob light". A brushed metal band at the ceiling, a frosted glass bowl 34 cm across and 11 cm deep curving down
    /// to a small finial, smooth all round (48 sides with smooth normals: the old 16-sided drum showed its corners).
    /// The glass is textured like the troffers' lens (<see cref="MaterialMaps.TrofferOn"/>): lit, it glows brightest over
    /// its two bulbs, an etched ring of scallops round it, darker toward the band, and a few dead insects lie in the
    /// bottom of the bowl; unlit, the same frosted glass. Same shader keywords as the dome lens: no new variants.
    /// </summary>
    public static class DomeFitting
    {
        public const float Radius = 0.17f, Depth = 0.11f;
        const int Sides = 48;

        /// <summary>The lit and unlit glass (copies of the biome's dome lens with the textures), or null without them.</summary>
        public static Material On, Off;
        static Texture2D s_glow, s_glass;
        static float s_glowMean = 1f;
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap"), EmissionColor = Shader.PropertyToID("_EmissionColor"),
                            BaseMap = Shader.PropertyToID("_BaseMap");

        /// <summary>Is this one of the fitting's glass materials (lit or unlit)?</summary>
        public static bool IsGlass(Material m) => m && (m == On || m == Off);

        public static void Materials(OfficeBiomeAssets a)
        {
            On = Off = null;
            if (!a || !a.domeOn || !a.domeOff || !a.domeOn.HasProperty(EmissionMap)) return;
            if (!s_glow) s_glow = Glow(256, out s_glowMean);
            if (!s_glass) s_glass = Glass(256);
            On = new Material(a.domeOn) { name = a.domeOn.name + " (frosted bowl)", hideFlags = HideFlags.DontSave };
            On.SetTexture(EmissionMap, s_glow);
            On.SetTexture(BaseMap, s_glass);
            On.SetTextureScale(BaseMap, Vector2.one); On.SetTextureOffset(BaseMap, Vector2.zero);
            On.SetColor(EmissionColor, a.domeOn.GetColor(EmissionColor) * 1.2f);
            Off = new Material(a.domeOff) { name = a.domeOff.name + " (frosted bowl)", hideFlags = HideFlags.DontSave };
            Off.SetTexture(BaseMap, s_glass);
            Off.SetTextureScale(BaseMap, Vector2.one); Off.SetTextureOffset(BaseMap, Vector2.zero);
        }

        /// <summary>
        /// The fitting with its top at c (the ceiling): the glass bowl into glass, the band and the finial into metal (either
        /// may be null). Positions as c is given (chunk-local or world).
        /// </summary>
        public static void Build(MeshWriter glass, MeshWriter metal, Vector3 c)
        {
            const float band = 0.018f, bandR = Radius + 0.007f;
            if (metal != null)
            {
                // The band: its side, and the lip under it where the glass sits in.
                Revolve(metal, c, new[] { bandR, bandR }, new[] { 0f, -band }, new[] { new Vector2(1f, 0f), new Vector2(1f, 0f) });
                Revolve(metal, c, new[] { bandR, Radius - 0.004f }, new[] { -band, -band }, new[] { new Vector2(0f, -1f), new Vector2(0f, -1f) });
            }
            if (glass != null)
            {
                // A half-ellipse bowl: r = R sin(phi), y = -band - D cos(phi), from the rim (phi 90) to the bottom (0).
                const int rings = 12;
                float D = Depth - band;
                var r = new float[rings + 1]; var y = new float[rings + 1]; var n = new Vector2[rings + 1];
                for (int i = 0; i <= rings; i++)
                {
                    float phi = Mathf.PI * 0.5f * (1f - i / (float)rings);
                    float s = Mathf.Sin(phi), co = Mathf.Cos(phi);
                    r[i] = Radius * s; y[i] = -band - D * co;
                    n[i] = new Vector2(s / Radius, -co / D).normalized;
                }
                Revolve(glass, c, r, y, n);
            }
            if (metal != null)
            {
                // The finial: a small nickel cap holding the bowl up.
                float b = -Depth;
                Revolve(metal, c, new[] { 0.013f, 0.013f, 0.008f, 0f }, new[] { b + 0.002f, b - 0.01f, b - 0.016f, b - 0.018f },
                    new[] { new Vector2(1f, 0f), new Vector2(1f, -0.3f), new Vector2(0.6f, -1f), new Vector2(0f, -1f) });
            }
        }

        /// <summary>A surface of revolution about c's vertical: rings at (r[i], y[i]) with profile normals (radial, up).</summary>
        static void Revolve(MeshWriter w, Vector3 c, float[] r, float[] y, Vector2[] n)
        {
            float uvK = 0.5f / Radius;
            for (int i = 0; i + 1 < r.Length; i++)
                for (int s = 0; s < Sides; s++)
                {
                    float a0 = s * Mathf.PI * 2f / Sides, a1 = (s + 1) * Mathf.PI * 2f / Sides;
                    Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    Vector3 p00 = c + d0 * r[i] + Vector3.up * y[i], p01 = c + d1 * r[i] + Vector3.up * y[i];
                    Vector3 p11 = c + d1 * r[i + 1] + Vector3.up * y[i + 1], p10 = c + d0 * r[i + 1] + Vector3.up * y[i + 1];
                    Vector3 n00 = (d0 * n[i].x + Vector3.up * n[i].y).normalized, n01 = (d1 * n[i].x + Vector3.up * n[i].y).normalized;
                    Vector3 n11 = (d1 * n[i + 1].x + Vector3.up * n[i + 1].y).normalized, n10 = (d0 * n[i + 1].x + Vector3.up * n[i + 1].y).normalized;
                    Vector2 UV(Vector3 p) => new Vector2((p.x - c.x) * uvK + 0.5f, (p.z - c.z) * uvK + 0.5f);
                    w.SmoothQuad(p00, p01, p11, p10, n00, n01, n11, n10, UV(p00), UV(p01), UV(p11), UV(p10));
                }
        }

        // ---------------- Textures (seen from below: u, v across the bowl, 0.5 the middle) ----------------

        /// <summary>The etched ring (0 none, 1 an etched line), at radius rr (0 middle, 1 rim) and angle th.</summary>
        static float Etch(float rr, float th)
        {
            // Two thin rings with a band of scallops between them (rr 0.62 to 0.76), and a faint frosted star in the middle.
            float ring = Line(rr, 0.62f, 0.006f) + Line(rr, 0.76f, 0.006f);
            float band = Smooth(0.625f, 0.64f, rr) * (1f - Smooth(0.745f, 0.755f, rr));
            float sc = Mathf.Abs(Mathf.Sin(th * 9f));                       // 18 scallops
            float scallop = Line(rr, 0.645f + 0.09f * sc, 0.005f) * band;
            float star = (1f - Smooth(0.08f, 0.2f, rr)) * 0.35f * Mathf.Pow(Mathf.Abs(Mathf.Cos(th * 4f)), 6f);
            return Mathf.Clamp01(ring + scallop + star);
        }

        static float Line(float x, float at, float w) => Mathf.Exp(-(x - at) * (x - at) / (2f * w * w));
        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        /// <summary>Dead insects in the bowl's bottom: dark specks, 0 none .. 1 a body.</summary>
        static float Bugs(float u, float v)
        {
            if (s_bugs == null)
            {
                var rng = new System.Random(77);
                s_bugs = new Vector4[7];
                for (int i = 0; i < s_bugs.Length; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = 0.03f + (float)rng.NextDouble() * 0.12f;
                    s_bugs[i] = new Vector4(0.5f + Mathf.Cos(a) * d, 0.5f + Mathf.Sin(a) * d, (float)rng.NextDouble() * Mathf.PI, 0.01f + (float)rng.NextDouble() * 0.012f);
                }
            }
            float k = 0f;
            foreach (var b in s_bugs)
            {
                float cx = b.x, cy = b.y, ang = b.z, len = b.w;
                float du = u - cx, dv = v - cy;
                float along = du * Mathf.Cos(ang) + dv * Mathf.Sin(ang), across = -du * Mathf.Sin(ang) + dv * Mathf.Cos(ang);
                float e = (along * along) / (len * len) + (across * across) / (len * len * 0.16f);
                k = Mathf.Max(k, Mathf.Clamp01(1.4f - e));
            }
            return k;
        }

        static Vector4[] s_bugs;

        static float Grain(int x, int y)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        /// <summary>
        /// The light leaving the glass (linear, peak 1): two A19 bulbs side by side burn through the middle, and the frosted
        /// glass passes less and less of it out toward the rim (about 3 % at the band): a camera exposed for the room clips
        /// the middle and sees the glass, its etched ring and the bugs' shadows round it (an even glow was one white disc
        /// at any exposure, 2026-10-31). Mean returned (not used for scaling: the middle is as bright as the old lens).
        /// </summary>
        static Texture2D Glow(int size, out float mean)
        {
            var lin = new float[size * size];
            float max = 0f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float du = u - 0.5f, dv = v - 0.5f, rr = Mathf.Sqrt(du * du + dv * dv) * 2f, th = Mathf.Atan2(dv, du);
                float bulbs = 0f;
                for (int k = -1; k <= 1; k += 2) { float bx = u - (0.5f + k * 0.1f); bulbs += Mathf.Exp(-(bx * bx + dv * dv) / (2f * 0.1f * 0.1f)); }
                float g = (0.03f + 0.2f * Mathf.Exp(-rr * rr / (2f * 0.38f * 0.38f)) + bulbs) * Mathf.Lerp(0.45f, 1f, 1f - Smooth(0.86f, 1f, rr));
                g *= 1f - 0.45f * Etch(rr, th);
                g *= 1f - 0.85f * Bugs(u, v);
                g *= 0.97f + 0.06f * Grain(x, y);
                lin[y * size + x] = g;
                if (g > max) max = g;
            }
            float sum = 0f;
            var px = new Color[size * size];
            for (int i = 0; i < lin.Length; i++)
            {
                float g = lin[i] / max;
                sum += g;
                float s = Mathf.LinearToGammaSpace(g);
                px[i] = new Color(s, s, s, 1f);
            }
            mean = sum / lin.Length;
            return Tex("DomeGlow", size, px);
        }

        /// <summary>The frosted glass itself: off-white, the etched lines a touch greyer, the bugs brown-black, a fine frost.</summary>
        static Texture2D Glass(int size)
        {
            var px = new Color[size * size];
            var white = new Color(0.94f, 0.93f, 0.9f, 1f);
            var bug = new Color(0.12f, 0.09f, 0.06f, 1f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                float du = u - 0.5f, dv = v - 0.5f, rr = Mathf.Sqrt(du * du + dv * dv) * 2f, th = Mathf.Atan2(dv, du);
                var c = white * (1f - 0.12f * Etch(rr, th)) * (0.97f + 0.05f * Grain(x, y));
                c = Color.Lerp(c, bug, Bugs(u, v) * 0.9f);
                c.a = 1f;
                px[y * size + x] = c;
            }
            return Tex("DomeGlass", size, px);
        }

        static Texture2D Tex(string name, int size, Color[] px)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = name, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4, hideFlags = HideFlags.DontSave };
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }
    }
}
