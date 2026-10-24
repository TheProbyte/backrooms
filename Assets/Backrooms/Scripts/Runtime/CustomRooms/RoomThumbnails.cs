using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>Small plan drawings of a room for the library cards: areas tinted, walls white, doorways orange, lights as dots.</summary>
    public static class RoomThumbnails
    {
        static readonly Color[] Areas =
        {
            new Color(0.25f, 0.42f, 0.62f), new Color(0.62f, 0.34f, 0.26f), new Color(0.38f, 0.56f, 0.28f),
            new Color(0.5f, 0.3f, 0.58f), new Color(0.62f, 0.52f, 0.2f), new Color(0.24f, 0.56f, 0.5f),
        };

        public static Texture2D Make(RoomBlueprint bp)
        {
            var v = bp.variants.Count > 0 ? bp.variants[0] : RoomVariant.CreateDefault();
            int cell = Mathf.Clamp(150 / Mathf.Max(v.width, v.depth), 4, 16), pad = 8;
            int w = v.width * cell + pad * 2 + 2, h = v.depth * cell + pad * 2 + 2;
            var px = new Color32[w * h];
            var bg = new Color32(18, 18, 20, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;

            void Rect(int x0, int y0, int x1, int y1, Color c)
            {
                var cc = (Color32)c;
                for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x1); x++) px[y * w + x] = cc;
            }

            for (int z = 0; z < v.depth; z++)
            for (int x = 0; x < v.width; x++)
                Rect(pad + x * cell, pad + z * cell, pad + (x + 1) * cell, pad + (z + 1) * cell, Areas[v.RoomOf(x, z) % Areas.Length] * 0.6f);

            Color EdgeColor(int code)
            {
                switch (EdgeCode.Kind(code))
                {
                    case EdgeCode.Wall: return new Color(0.93f, 0.93f, 0.9f);
                    case EdgeCode.Header: return new Color(1f, 0.6f, 0.2f);
                    case EdgeCode.Tall: return new Color(1f, 0.85f, 0.3f);
                    case EdgeCode.Half: return new Color(0.55f, 0.65f, 0.75f);
                    case EdgeCode.Railing: return new Color(0.55f, 0.65f, 0.75f);
                    default: return Color.clear;
                }
            }
            for (int x = 0; x <= v.width; x++)
            for (int z = 0; z < v.depth; z++)
            {
                var c = EdgeColor(v.V(x, z)); if (c.a <= 0) continue;
                Rect(pad + x * cell - 1, pad + z * cell, pad + x * cell + 1, pad + (z + 1) * cell + 1, c);
            }
            for (int x = 0; x < v.width; x++)
            for (int z = 0; z <= v.depth; z++)
            {
                var c = EdgeColor(v.H(x, z)); if (c.a <= 0) continue;
                Rect(pad + x * cell, pad + z * cell - 1, pad + (x + 1) * cell + 1, pad + z * cell + 1, c);
            }
            foreach (var f in v.fixtures)
            {
                int cx = pad + f.x * cell + cell / 2, cy = pad + f.z * cell + cell / 2;
                Rect(cx - 1, cy - 1, cx + 2, cy + 2, f.on ? new Color(1f, 0.95f, 0.5f) : new Color(0.5f, 0.5f, 0.5f));
            }
            foreach (var p in v.pillars) Rect(pad + p.x * cell - 2, pad + p.z * cell - 2, pad + p.x * cell + 3, pad + p.z * cell + 3, new Color(0.9f, 0.9f, 0.9f));
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
            t.SetPixels32(px); t.Apply();
            return t;
        }
    }
}
