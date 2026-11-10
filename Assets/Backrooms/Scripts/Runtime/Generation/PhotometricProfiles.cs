using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Backrooms.Generation
{
    /// <summary>
    /// The ceiling fixtures' light shapes from photometric data (2026-10-28): IES LM-63 files, the format lighting makers
    /// publish their measurements in (candela at each angle from straight down). Each fixture type's file is read from
    /// Resources/Lighting/IES (Troffer, Dome, Strip; .txt so Unity keeps the text), its candela averaged over the
    /// horizontal planes and fitted with intensity ~ cos(angle)^k, the exponent the shaders and the light grid use
    /// (GridLight.padding.y; LGrid_Profile). Replace a file with a real fixture's .ies (renamed .txt) to light the
    /// building with that fixture's measured shape. Lambertian (a perfect diffuser) is k = 1; a prismatic lens is a little
    /// narrower, an opal dome and bare tubes throw more sideways (k below 1).
    /// </summary>
    public static class PhotometricProfiles
    {
        static float s_troffer = -1f, s_dome = -1f, s_strip = -1f;

        /// <summary>The fitted exponent for a fixture type (troffer when neither dome nor strip).</summary>
        public static float Exponent(bool dome, bool strip)
        {
            if (s_troffer < 0f)
            {
                s_troffer = Load("Troffer", 1.1f);
                s_dome = Load("Dome", 0.65f);
                s_strip = Load("Strip", 0.45f);
            }
            return dome ? s_dome : strip ? s_strip : s_troffer;
        }

        static float Load(string name, float fallback)
        {
            var ta = Resources.Load<TextAsset>("Lighting/IES/" + name);
            if (!ta) return fallback;
            try
            {
                if (TryParse(ta.text, out var angles, out var candela)) return Fit(angles, candela);
                Debug.LogWarning("[Backrooms] Could not read the photometric file " + name + "; using a typical shape.");
            }
            catch (System.Exception e) { Debug.LogWarning("[Backrooms] Photometric file " + name + ": " + e.Message); }
            return fallback;
        }

        /// <summary>
        /// Reads an IES LM-63 file (any version): the vertical angles and the candela at each, averaged over the horizontal
        /// planes. Type C photometry (0 = straight down), as ceiling fixtures are measured.
        /// </summary>
        public static bool TryParse(string text, out float[] angles, out float[] candela)
        {
            angles = null; candela = null;
            int tilt = text.IndexOf("TILT=", System.StringComparison.OrdinalIgnoreCase);
            if (tilt < 0) return false;
            int eol = text.IndexOf('\n', tilt);
            if (eol < 0) return false;
            string tiltValue = text.Substring(tilt + 5, eol - tilt - 5).Trim();
            var nums = new List<float>();
            foreach (var tok in text.Substring(eol + 1).Split(new[] { ' ', '\t', '\r', '\n', ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                if (float.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) nums.Add(v);
            int i = 0;
            if (tiltValue.Equals("INCLUDE", System.StringComparison.OrdinalIgnoreCase))
            {
                // Lamp-to-luminaire geometry, then pairs of tilt angles and factors: skipped.
                if (nums.Count < 2) return false;
                int pairs = (int)nums[1];
                i = 2 + pairs * 2;
            }
            if (nums.Count < i + 13) return false;
            int nV = (int)nums[i + 3], nH = (int)nums[i + 4];
            float multiplier = nums[i + 2];
            i += 10 + 3;                                                      // the luminaire line, then ballast, future, watts
            if (nV < 2 || nH < 1 || nums.Count < i + nV + nH + nV * nH) return false;
            angles = new float[nV];
            for (int v = 0; v < nV; v++) angles[v] = nums[i + v];
            i += nV + nH;
            candela = new float[nV];
            for (int h = 0; h < nH; h++)
                for (int v = 0; v < nV; v++) candela[v] += nums[i + h * nV + v] * multiplier / nH;
            return true;
        }

        /// <summary>Best k for candela(angle) / candela(0) ~ cos(angle)^k, weighted by the solid angle each band covers.</summary>
        public static float Fit(float[] angles, float[] candela)
        {
            float i0 = 0f;
            for (int v = 0; v < angles.Length; v++) if (angles[v] <= 0.5f) i0 = Mathf.Max(i0, candela[v]);
            if (i0 <= 0f) i0 = Mathf.Max(1e-3f, candela[0]);
            float best = 1f, bestErr = float.MaxValue;
            for (float k = 0.2f; k <= 4f; k += 0.01f)
            {
                float err = 0f;
                for (int v = 0; v < angles.Length; v++)
                {
                    float a = angles[v];
                    if (a > 89.9f) continue;                                  // the horizon: nothing below the ceiling sees it
                    float rad = a * Mathf.Deg2Rad, w = Mathf.Max(0.05f, Mathf.Sin(rad));
                    float d = Mathf.Pow(Mathf.Cos(rad), k) - candela[v] / i0;
                    err += w * d * d;
                }
                if (err < bestErr) { bestErr = err; best = k; }
            }
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_troffer = s_dome = s_strip = -1f; }
    }
}
