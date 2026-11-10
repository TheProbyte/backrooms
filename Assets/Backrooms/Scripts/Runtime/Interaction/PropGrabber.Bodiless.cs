using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Body: None (Camera menu > HANDHELD > Body): no body drawn and no arm to reach with, as in Amnesia or Penumbra. Things
    /// are taken from where you look out to <see cref="bodilessReach"/> (the old no-body paths: Armed is false); no centre
    /// mark (the focus marks and the hint line say what a click takes). Holding something heavy, a dot sits where you hold it; when it lags behind (dragged, caught on something), a faint dotted line runs
    /// from the mark to it, fading as it catches up. The weight itself shows in how it moves (the hand's spring: it lags,
    /// scrapes and swings) and in the view.
    /// </summary>
    public partial class PropGrabber
    {
        void BodilessGUI()
        {
            if (!Bodiless || !_cam || !_player.HasFocus) return;
            float s = Screen.height / 1080f;
            float t = Mathf.Max(1.5f, 2f * s);
            var old = GUI.color;

            // (No centre mark, owner 2026-10-31: "maybe no crosshair". What a click takes is shown by the focus marks on it
            // and the hint line, as with a body.)

            // Holding a prop: where it is held, and a line to it while it lags.
            if (Current == State.Holding && Held)
            {
                Vector3 grip = Held.transform.TransformPoint(_gripLocal);
                Vector3 want = StandShoulder + transform.rotation * _offset;
                Vector3 g = _cam.WorldToScreenPoint(grip), w = _cam.WorldToScreenPoint(want);
                if (g.z > 0.05f)
                {
                    float gx = g.x, gy = Screen.height - g.y;
                    float lag = Vector3.Distance(grip, want);
                    float lineA = Mathf.Clamp01((lag - 0.07f) / 0.25f) * 0.55f;
                    if (lineA > 0.01f && w.z > 0.05f)
                    {
                        float wx = w.x, wy = Screen.height - w.y;
                        Vector2 a = new Vector2(wx, wy), b = new Vector2(gx, gy);
                        float len = Vector2.Distance(a, b), step = 7f * s;
                        GUI.color = new Color(1f, 1f, 1f, lineA);
                        for (float d = step; d < len - step * 0.5f; d += step)
                        {
                            Vector2 p = Vector2.Lerp(a, b, d / len);
                            GUI.DrawTexture(new Rect(p.x - t * 0.5f, p.y - t * 0.5f, t, t), Texture2D.whiteTexture);
                        }
                    }
                    float dot = 5f * s * Mathf.Lerp(1f, 1.4f, Strain);
                    GUI.color = new Color(0f, 0f, 0f, 0.45f);
                    GUI.DrawTexture(new Rect(gx - dot * 0.5f + 1f, gy - dot * 0.5f + 1f, dot, dot), Texture2D.whiteTexture);
                    GUI.color = new Color(1f, 1f, 1f, 0.8f);
                    GUI.DrawTexture(new Rect(gx - dot * 0.5f, gy - dot * 0.5f, dot, dot), Texture2D.whiteTexture);
                }
            }
            GUI.color = old;
        }

    }
}
