using System.Collections.Generic;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The seam's card (2026-11-09, decisions 7 and 8): an Overflow work form on a clipboard, hung on the wall beside a
    /// seam's tape, saying what the client behind it offers. Like a Satisfactory node you can read before you build on it:
    /// the client, the lot waiting (how many boxes, how long they stay, what it pays in and back), and where things stand
    /// (NEEDS A HAND TRUCK, ON HOLD, your batch of theirs DUE BACK). There is nothing to accept: take the boxes and the lot is
    /// yours. Read by looking at it; redrawn twice a second while you are near.
    /// </summary>
    public class SeamCard : MonoBehaviour
    {
        const int W = 132, H = 112;
        /// <summary>Its size on the wall and where it is read from (Generation.PixelScreen, 2026-11-10: at 0.44 m wide its
        /// 1-dot lines covered 5 of the camcorder's lines from 2 m; at 0.62 m, 7.7).</summary>
        public const float Width = 0.62f, ReadAt = 2f;
        public const int Texels = W, SmallestDot = 1;
        int _client;
        Texture2D _tex;
        Color32[] _px;
        float _next;
        string _last = "";
        static readonly List<(string text, Color32 ink)> s_lines = new List<(string, Color32)>();

        static readonly Color32 Paper = new Color32(236, 226, 200, 255), Ink = new Color32(52, 38, 28, 255), Band = new Color32(240, 162, 30, 255),
            Board = new Color32(120, 84, 48, 255);

        public static void Attach(Seam seam, uint mask)
        {
            if (!seam || seam.kind > 3 || seam.GetComponentInChildren<SeamCard>()) return;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "SeamCard";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(seam.transform, false);
            // On the wall beside the doorway, the side away from where a chute goes (LeaseSeams.ChuteOffset is +x).
            q.transform.localPosition = new Vector3(-(NoclipDoors.Width * 0.5f + 0.06f + Width * 0.5f), 1.3f, 0.004f);
            q.transform.localRotation = Quaternion.Euler(0f, 180f, -1.5f);
            q.transform.localScale = new Vector3(Width, Width * H / W, 1f);
            var card = q.AddComponent<SeamCard>();
            card._client = seam.kind;
            card._tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "SeamCard" };
            card._px = new Color32[W * H];
            var sh = Shader.Find("Backrooms/TvScreen") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(sh) { name = "SeamCard" };
            mat.SetTexture("_BaseMap", card._tex);
            mat.SetColor("_BaseColor", new Color(0.8f, 0.78f, 0.74f));
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.renderingLayerMask = mask;
            card.Draw(true);
        }

        void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            var cam = Camera.main;
            if (!cam || (cam.transform.position - transform.position).sqrMagnitude > 14f * 14f) return;
            Draw(false);
        }

        void Draw(bool force)
        {
            s_lines.Clear();
            var cs = Contracts.Instance;
            if (cs) cs.Card(_client, s_lines);
            else s_lines.Add(("OVERFLOW WORK", Ink));
            string key = "";
            foreach (var (t, _) in s_lines) key += t + "|";
            if (!force && key == _last) return;
            _last = key;
            for (int i = 0; i < _px.Length; i++) _px[i] = Paper;
            // The clipboard's edge and clip, the company's band, the client's tape colour.
            PixelFont.Fill(_px, W, H, 0, 0, W, 3, Board);
            PixelFont.Fill(_px, W, H, 0, H - 3, W, H, Board);
            PixelFont.Fill(_px, W, H, 0, 0, 3, H, Board);
            PixelFont.Fill(_px, W, H, W - 3, 0, W, H, Board);
            PixelFont.Fill(_px, W, H, W / 2 - 14, 0, W / 2 + 14, 6, new Color32(160, 160, 165, 255));
            PixelFont.Fill(_px, W, H, 3, 7, W - 3, 18, Band);
            PixelFont.DrawCentred(_px, W, H, "OVERFLOW WORK", W / 2, 9, 1, Ink);
            var tape = (Color32)Contracts.ClientColours[Mathf.Clamp(_client, 0, 3)];
            PixelFont.Fill(_px, W, H, 3, 20, W - 3, 31, tape);
            PixelFont.DrawCentred(_px, W, H, Contracts.Clients[Mathf.Clamp(_client, 0, 3)], W / 2, 22, 1, new Color32(250, 246, 236, 255));
            int y = 35;
            foreach (var (t, ink) in s_lines)
            {
                if (y > H - 12) break;
                string text = t;
                while (text.Length > 3 && PixelFont.Width(text, 1) > W - 10) text = text.Substring(0, text.Length - 1);
                PixelFont.Draw(_px, W, H, text, 6, y, 1, ink);
                y += 10;
            }
            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        void OnDestroy() { if (_tex) Destroy(_tex); }
    }
}
