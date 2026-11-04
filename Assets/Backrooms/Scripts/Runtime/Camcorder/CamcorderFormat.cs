using UnityEngine;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// What each recording format does to the picture (NTSC, late-1990s consumer gear), in recording-raster pixels
    /// (640 x 480 for a 4:3 picture). The F5 sliders for these effects are multipliers on the format's own values, so a
    /// format never shows artefacts it could not have (no tape noise on DV, no compression blocks on VHS).
    /// </summary>
    public struct CamcorderFormat
    {
        public string name;
        public bool analog;             // tape played back through a VCR or the camera: timebase errors, tape noise, dropouts
        public float lumaSigma;         // luma bandwidth as a Gaussian sigma (VHS ~240 TV lines, Hi8 ~400, DV ~500)
        public float chromaSigma;       // colour-under bandwidth (analog), ~30-40 TV lines
        public float chromaTrail;       // analog colour filters are causal: colour trails to the right
        public float chromaDelay;       // luma/chroma timing error
        public bool comb;               // NTSC comb filter on playback: colour averaged with the line above
        public bool dv411;              // DV: one colour sample per four pixels
        public float detailCamera;      // the camera's aperture correction (edge enhancement)
        public float detailPlayback;    // the VCR's own peaking
        public float setup;             // black level: 7.5 IRE on analog NTSC, 0 on DV
        public float jitter, flagging;  // timebase: per-line wander, top-of-frame bend after the head switch
        public float dropouts;          // chance per line per field
        public float lumaNoise, chromaNoise;
        public float phaseNoiseDeg;     // colour-under hue wander per line
        public float crossColour;       // rainbow on fine detail (composite capture)
        public float headSwitchLines, headSwitchSkew;
        public float compression;       // DV DCT

        public static readonly string[] Names = { "VHS-C SP", "VHS-C EP", "Hi8", "MiniDV" };

        public static CamcorderFormat Get(int index)
        {
            switch (Mathf.Clamp(index, 0, Names.Length - 1))
            {
                default:
                case 0:
                    return new CamcorderFormat
                    {
                        name = Names[0], analog = true, lumaSigma = 0.75f, chromaSigma = 3.4f, chromaTrail = 0.6f, chromaDelay = 1.0f, comb = true,
                        detailCamera = 0.6f, detailPlayback = 0.3f, setup = 0.075f, jitter = 0.5f, flagging = 3f, dropouts = 0.0005f,
                        lumaNoise = 0.007f, chromaNoise = 0.02f, phaseNoiseDeg = 4f, crossColour = 0.3f, headSwitchLines = 7f, headSwitchSkew = 6f,
                    };
                case 1:
                    return new CamcorderFormat
                    {
                        name = Names[1], analog = true, lumaSigma = 1.0f, chromaSigma = 4.0f, chromaTrail = 0.6f, chromaDelay = 1.3f, comb = true,
                        detailCamera = 0.5f, detailPlayback = 0.35f, setup = 0.075f, jitter = 0.8f, flagging = 5f, dropouts = 0.002f,
                        lumaNoise = 0.012f, chromaNoise = 0.035f, phaseNoiseDeg = 8f, crossColour = 0.3f, headSwitchLines = 8f, headSwitchSkew = 8f,
                    };
                case 2:
                    return new CamcorderFormat
                    {
                        name = Names[2], analog = true, lumaSigma = 0.45f, chromaSigma = 3.2f, chromaTrail = 0.5f, chromaDelay = 0.6f, comb = true,
                        detailCamera = 0.7f, detailPlayback = 0.2f, setup = 0.075f, jitter = 0.4f, flagging = 2f, dropouts = 0.0015f,
                        lumaNoise = 0.005f, chromaNoise = 0.015f, phaseNoiseDeg = 3f, crossColour = 0.2f, headSwitchLines = 6f, headSwitchSkew = 5f,
                    };
                case 3:
                    return new CamcorderFormat
                    {
                        name = Names[3], analog = false, lumaSigma = 0.35f, chromaSigma = 0f, dv411 = true,
                        detailCamera = 1.0f, setup = 0f, compression = 0.3f,
                    };
            }
        }

        /// <summary>Normalised 5-tap Gaussian weights (centre, +-1, +-2) for the luma bandwidth.</summary>
        public static Vector3 LumaWeights(float sigma)
        {
            sigma = Mathf.Max(sigma, 0.05f);
            float w1 = Mathf.Exp(-1f / (2f * sigma * sigma)), w2 = Mathf.Exp(-4f / (2f * sigma * sigma));
            float sum = 1f + 2f * w1 + 2f * w2;
            return new Vector3(1f / sum, w1 / sum, w2 / sum);
        }
    }
}
