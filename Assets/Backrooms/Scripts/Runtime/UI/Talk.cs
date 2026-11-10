using UnityEngine;

namespace Backrooms.UI
{
    /// <summary>
    /// The talk mark (Docs/UiPlan.md U7, 2026-11-10): three small bars, like a level meter, beside a player's name while
    /// they speak, moving with their voice. Drawn in IMGUI (the name tags and the Tab list).
    /// </summary>
    public static class Talk
    {
        /// <summary>Three bars in <paramref name="r"/> for a voice level (0..1), in a player's colour.</summary>
        public static void Bars(Rect r, float level, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            float t = Time.unscaledTime;
            float bw = r.width / 5f;
            var old = GUI.color;
            for (int i = 0; i < 3; i++)
            {
                // Each bar its own wobble, all scaled by the level: the middle one tallest.
                float k = Mathf.Clamp01(level * 1.6f) * (0.55f + 0.45f * Mathf.Abs(Mathf.Sin(t * (9f + i * 3.7f) + i * 1.3f)));
                if (i == 1) k = Mathf.Min(1f, k * 1.25f);
                float h = Mathf.Max(r.height * 0.2f, r.height * k);
                var bar = new Rect(r.x + i * bw * 2f, r.yMax - h, bw, h);
                GUI.color = new Color(0f, 0f, 0f, c.a * 0.6f);
                GUI.DrawTexture(new Rect(bar.x + 1f, bar.y + 1f, bar.width, bar.height), UiKit.White);
                GUI.color = c;
                GUI.DrawTexture(bar, UiKit.White);
            }
            GUI.color = old;
        }
    }
}
