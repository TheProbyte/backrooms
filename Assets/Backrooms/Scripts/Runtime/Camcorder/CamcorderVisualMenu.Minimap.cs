using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// F6: a small biome map in the top-left corner (under the REC readout) that stays up while you play.
    /// One pixel per chunk, centred on you, north up, with an arrow showing which way you face.
    /// </summary>
    public partial class CamcorderVisualMenu
    {
        const int MiniChunks = 24;                  // about 345 m across
        VisualElement _mini, _miniMap, _miniMarker;
        Label _miniLabel;
        Texture2D _miniTex;
        Vector2Int _miniCenter = new Vector2Int(int.MinValue, 0);
        float _miniNextDraw, _miniNextLabel;

        public bool MinimapOn { get; private set; }

        void BuildMinimap()
        {
            _mini = new VisualElement { pickingMode = PickingMode.Ignore };
            _mini.AddToClassList("cc-mini");
            _miniLabel = Text("", "cc-mini-label");
            _miniLabel.pickingMode = PickingMode.Ignore;
            _miniMap = new VisualElement { pickingMode = PickingMode.Ignore };
            _miniMap.AddToClassList("cc-mini-map");
            _miniTex = new Texture2D(MiniChunks, MiniChunks, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _miniMap.style.backgroundImage = new StyleBackground(_miniTex);
            _miniMarker = new Label("▲") { pickingMode = PickingMode.Ignore };
            _miniMarker.AddToClassList("cc-mini-marker");
            _miniMap.Add(_miniMarker);
            _mini.Add(_miniMap);
            _mini.Add(_miniLabel);
            _root.Add(_mini);
            Show(_mini, false);
        }

        public void SetMinimap(bool on)
        {
            MinimapOn = on;
            if (_mini != null) Show(_mini, on);
            _miniCenter.x = int.MinValue;
        }

        void UpdateMinimap(Keyboard kb)
        {
            if (kb.f6Key.wasPressedThisFrame) SetMinimap(!MinimapOn);
            if (!MinimapOn || _mini == null || !_ws) return;

            // Beside the settings panel while it is open, otherwise in the corner.
            // Inside the picture under REC/SP (the 4:3 frame when pillarboxed).
            float W = _root.layout.width, H = _root.layout.height;
            float fw = rig && rig.Settings.aspect43 ? Mathf.Min(W, H * 4f / 3f) : W;
            float frameLeft = (W - fw) * 0.5f;
            float left = frameLeft + fw * 0.06f;
            if (IsOpen) left = Mathf.Max(left, _panel.layout.xMax + 16f);
            _mini.style.left = left;
            _mini.style.top = H * 0.07f + 110f;

            var cam = Camera.main ? Camera.main.transform : null;
            Vector3 pos = cam ? cam.position : Vector3.zero;
            var chunk = WorldStreamer.ChunkOf(pos);
            if (chunk != _miniCenter || Time.unscaledTime >= _miniNextDraw)
            {
                _miniCenter = chunk;
                FillBiomeMap(_miniTex, MiniChunks, chunk);
                _miniMap.MarkDirtyRepaint();
                _miniNextDraw = Time.unscaledTime + 5f;           // loaded chunks brighten as they stream in
            }
            float fx = pos.x / OfficeBiome.ChunkSize - (_miniCenter.x - MiniChunks / 2);
            float fz = pos.z / OfficeBiome.ChunkSize - (_miniCenter.y - MiniChunks / 2);
            _miniMarker.style.left = Length.Percent(Mathf.Clamp01(fx / MiniChunks) * 100f);
            _miniMarker.style.bottom = Length.Percent(Mathf.Clamp01(fz / MiniChunks) * 100f);
            float yaw = cam ? cam.eulerAngles.y : 0f;
            _miniMarker.style.rotate = new Rotate(new Angle(yaw, AngleUnit.Degree));

            if (Time.unscaledTime >= _miniNextLabel)
            {
                _miniNextLabel = Time.unscaledTime + 0.5f;
                var layout = _ws.LayoutAt(chunk);
                string biome = layout != null ? layout.biomeName : _ws.Settings.Evaluate(_ws.Seed, chunk).Dominant;
                _miniLabel.text = $"{biome.ToUpperInvariant()}   {chunk.x},{chunk.y}";
            }
        }
    }
}
