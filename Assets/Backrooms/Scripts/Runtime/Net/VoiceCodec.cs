using UnityEngine;

namespace Backrooms.Net
{
    /// <summary>
    /// Co-op voice's codec (2026-11-07): IMA ADPCM, four bits a sample at 16 kHz (64 kbit/s while someone speaks). Built in,
    /// no package: the usual choice is Opus (through Concentus, as UniVoice and MetaVoiceChat do) at a third of the size,
    /// and it can replace this later behind the same two calls. Each packet starts from its own predictor and step, so a
    /// lost packet costs only its own 60 ms.
    /// </summary>
    public static class VoiceCodec
    {
        static readonly int[] Steps =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
            130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060,
            1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
            7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
        };
        static readonly int[] IndexShift = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

        /// <summary>Encodes samples (-1..1) into packed nibbles; the starting predictor and step index come back to send first.</summary>
        public static byte[] Encode(float[] pcm, int count, out short predictor, out byte index, ref int carryIndex)
        {
            var outB = new byte[(count + 1) / 2];
            int pred = Mathf.Clamp(Mathf.RoundToInt(pcm[0] * 32767f), -32768, 32767);
            int idx = Mathf.Clamp(carryIndex, 0, 88);
            predictor = (short)pred; index = (byte)idx;
            for (int i = 0; i < count; i++)
            {
                int s = Mathf.Clamp(Mathf.RoundToInt(pcm[i] * 32767f), -32768, 32767);
                int step = Steps[idx], diff = s - pred, code = 0;
                if (diff < 0) { code = 8; diff = -diff; }
                int delta = step >> 3;
                if (diff >= step) { code |= 4; diff -= step; delta += step; }
                if (diff >= step >> 1) { code |= 2; diff -= step >> 1; delta += step >> 1; }
                if (diff >= step >> 2) { code |= 1; delta += step >> 2; }
                pred = Mathf.Clamp(pred + ((code & 8) != 0 ? -delta : delta), -32768, 32767);
                idx = Mathf.Clamp(idx + IndexShift[code], 0, 88);
                if ((i & 1) == 0) outB[i >> 1] = (byte)code; else outB[i >> 1] |= (byte)(code << 4);
            }
            carryIndex = idx;
            return outB;
        }

        /// <summary>Decodes count samples (-1..1) into dst.</summary>
        public static void Decode(byte[] data, int count, short predictor, byte index, float[] dst)
        {
            int pred = predictor, idx = Mathf.Clamp(index, 0, 88);
            for (int i = 0; i < count && (i >> 1) < data.Length; i++)
            {
                int code = (i & 1) == 0 ? data[i >> 1] & 15 : data[i >> 1] >> 4;
                int step = Steps[idx], delta = step >> 3;
                if ((code & 4) != 0) delta += step;
                if ((code & 2) != 0) delta += step >> 1;
                if ((code & 1) != 0) delta += step >> 2;
                pred = Mathf.Clamp(pred + ((code & 8) != 0 ? -delta : delta), -32768, 32767);
                idx = Mathf.Clamp(idx + IndexShift[code], 0, 88);
                dst[i] = pred / 32768f;
            }
        }
    }
}
