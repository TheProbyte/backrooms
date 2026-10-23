using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// Shows the camcorder picture at the recording's frame rate (for example 24 frames per second) while the game
    /// keeps running and taking input at the display rate. On a recorded frame the finished picture (after the
    /// camcorder pass) is kept; on the frames in between the kept picture is shown again. Off when
    /// <see cref="Fps"/> is 0, for cameras rendering into a texture (stills), and in the Scene view.
    /// </summary>
    public class CamcorderFrameHold : ScriptableRendererFeature
    {
        /// <summary>Recording frame rate; 0 shows every rendered frame.</summary>
        public static float Fps;
        /// <summary>True on the frames that record a new picture (set by <see cref="Tick"/>).</summary>
        public static bool RecordThisFrame = true;
        /// <summary>Counts recorded pictures (the camcorder noise re-seeds on each).</summary>
        public static int RecordedFrames;

        static double _next;
        static int _tickedFrame = -1;

        /// <summary>Called once per frame (by the camcorder rig) before rendering.</summary>
        public static void Tick()
        {
            if (_tickedFrame == Time.frameCount) return;
            _tickedFrame = Time.frameCount;
            if (Fps <= 0.01f) { RecordThisFrame = true; RecordedFrames++; return; }
            double now = Time.unscaledTimeAsDouble, step = 1.0 / Fps;
            if (now >= _next)
            {
                RecordThisFrame = true; RecordedFrames++;
                // Keep a steady cadence; if the game fell more than a frame behind, start counting again from now.
                _next = now - _next > step ? now + step : _next + step;
            }
            else RecordThisFrame = false;
        }

        class HoldPass : ScriptableRenderPass
        {
            RTHandle _held;
            bool _valid;

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cam = frameData.Get<UniversalCameraData>();
                if (Fps <= 0.01f || cam.cameraType != CameraType.Game || cam.camera.targetTexture != null) { _valid = false; return; }
                var res = frameData.Get<UniversalResourceData>();
                var desc = cam.cameraTargetDescriptor;
                desc.depthStencilFormat = GraphicsFormat.None;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                if (RenderingUtils.ReAllocateHandleIfNeeded(ref _held, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_CamcorderHeldFrame")) _valid = false;
                var held = renderGraph.ImportTexture(_held);
                var color = res.activeColorTexture;
                if (RecordThisFrame || !_valid)
                {
                    renderGraph.AddBlitPass(color, held, Vector2.one, Vector2.zero, filterMode: RenderGraphUtils.BlitFilterMode.ClampNearest, passName: "Camcorder Keep Frame");
                    _valid = true;
                }
                else
                    renderGraph.AddBlitPass(held, color, Vector2.one, Vector2.zero, filterMode: RenderGraphUtils.BlitFilterMode.ClampNearest, passName: "Camcorder Show Kept Frame");
            }

            public void Dispose() { _held?.Release(); _held = null; }
        }

        HoldPass _pass;

        public override void Create()
        {
            // Directly after the camcorder's own full-screen pass (which runs at AfterRenderingPostProcessing).
            _pass = new HoldPass { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing + 1 };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (Fps > 0.01f) renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing) { _pass?.Dispose(); }
    }
}
