using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// The Reference Match scene's controller: a named camera pose the player starts at (F8 returns to it), the
    /// Reference Match camcorder preset applied in memory (the player's own saved settings are untouched), a freeze
    /// for repeatable stills (F9: no handheld motion, exposure/focus locked, time-varying tape effects stopped),
    /// and a fixed-resolution capture of the real game camera for side-by-side comparison with the reference photo.
    /// </summary>
    public class ReferenceMatch : MonoBehaviour
    {
        public static ReferenceMatch Instance { get; private set; }

        [Header("Reference Camera Pose (restores exactly)")]
        public Vector3 position = new Vector3(0.4f, 0f, 0f);
        public float yaw = -5f;
        public float pitch = 25f;

        [Header("Comparison capture (the reference photo is 1514 x 1240)")]
        public int captureWidth = 1514, captureHeight = 1240;
        public bool applyPresetOnStart = true;
        [Tooltip("Index in CamcorderSettings.PresetNames of the Reference Match preset.")]
        public int presetIndex = 7;

        public bool Frozen { get; private set; }

        CamcorderPlayerController _player;
        CamcorderRig _rig;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _player = FindAnyObjectByType<CamcorderPlayerController>();
            _rig = FindAnyObjectByType<CamcorderRig>();
            if (applyPresetOnStart && _rig)
            {
                var p = CamcorderSettings.Preset(presetIndex);
                p.CopyAudioFrom(_rig.Settings);
                _rig.SetSettings(p);
            }
            ReturnToPose();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f8Key.wasPressedThisFrame) ReturnToPose();
            if (kb.f9Key.wasPressedThisFrame) SetFrozen(!Frozen);
        }

        /// <summary>Puts the player at the reference pose (exactly, with zero velocity).</summary>
        public void ReturnToPose()
        {
            if (!_player) _player = FindAnyObjectByType<CamcorderPlayerController>();
            if (_player) _player.Teleport(position, yaw, pitch);
        }

        public void SetFrozen(bool on)
        {
            Frozen = on;
            if (!_rig) _rig = FindAnyObjectByType<CamcorderRig>();
            if (_rig) _rig.freezeForStills = on;
        }

        /// <summary>Restores the pose, freezes the camera, lets exposure and history settle, renders the game camera to a
        /// fixed-size texture and saves it as PNG. Normal behaviour is restored afterwards.</summary>
        public void Capture(string path, int settleFrames = 12)
        {
            StartCoroutine(CaptureRoutine(path, settleFrames));
        }

        public bool Capturing { get; private set; }

        /// <summary>Saves every recorded frame (at the preset's recording frame rate) for <paramref name="seconds"/> as
        /// numbered PNGs, without freezing anything, to compare camera motion with footage frame by frame.</summary>
        public void CaptureSequence(string folder, float seconds) => StartCoroutine(SequenceRoutine(folder, seconds));

        IEnumerator SequenceRoutine(string folder, float seconds)
        {
            // Frames are copied on the GPU, scaled to 960 x 540 and read back asynchronously so recording does not
            // slow the game down; the PNGs are written at the end.
            Capturing = true;
            Directory.CreateDirectory(folder);
            const int w = 960, h = 540;
            var full = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            var small = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            var shots = new System.Collections.Generic.List<byte[]>();
            int pending = 0;
            int wanted = Mathf.CeilToInt(seconds * Mathf.Max(CamcorderFrameHold.Fps, 24f));
            float worst = 0f;
            while (shots.Count < wanted)
            {
                yield return new WaitForEndOfFrame();
                worst = Mathf.Max(worst, Time.unscaledDeltaTime);
                if (!CamcorderFrameHold.RecordThisFrame) continue;
                ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
                if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(full, small, new Vector2(1f, -1f), new Vector2(0f, 1f));   // upright PNGs
                else Graphics.Blit(full, small);
                int index = shots.Count; shots.Add(null); pending++;
                UnityEngine.Rendering.AsyncGPUReadback.Request(small, 0, TextureFormat.RGBA32, r => { if (!r.hasError) shots[index] = r.GetData<byte>().ToArray(); pending--; });
            }
            while (pending > 0) yield return null;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int i = 0; i < shots.Count; i++)
            {
                if (shots[i] == null) continue;
                tex.LoadRawTextureData(shots[i]); tex.Apply();
                File.WriteAllBytes(Path.Combine(folder, $"f{i:0000}.png"), tex.EncodeToPNG());
            }
            Destroy(tex); full.Release(); small.Release(); Destroy(full); Destroy(small);
            Debug.Log($"[Backrooms] Saved {shots.Count} recorded frames to {folder} (longest frame {worst * 1000f:0} ms).");
            Capturing = false;
        }

        IEnumerator CaptureRoutine(string path, int settleFrames)
        {
            Capturing = true;
            bool wasFrozen = Frozen;
            SetFrozen(true);
            ReturnToPose();
            for (int i = 0; i < settleFrames; i++) yield return null;
            var cam = Camera.main;
            var rt = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var oldRect = cam.rect;
            cam.targetTexture = rt;
            cam.rect = new Rect(0, 0, 1, 1);
            yield return null;
            yield return new WaitForEndOfFrame();
            RenderTexture.active = rt;
            var tex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            cam.rect = oldRect;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex); rt.Release(); Destroy(rt);
            SetFrozen(wasFrozen);
            Capturing = false;
        }
    }
}
