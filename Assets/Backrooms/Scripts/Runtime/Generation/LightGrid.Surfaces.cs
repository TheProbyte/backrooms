using System.Collections.Generic;
using UnityEngine;

namespace Backrooms.Generation
{
    public partial class LightGrid
    {
        // Main-thread material sampling only. Workers still publish pure room/finish identifiers.
        struct SurfaceRecord { public Vector4 floor, wall, ceiling; }
        readonly SurfaceRecord[] _surfaceData = new SurfaceRecord[OccN * OccN * 2];
        readonly Dictionary<Material, Vector4> _reflectance = new Dictionary<Material, Vector4>();
        readonly Dictionary<Room, SurfaceRecord> _roomSurfaces = new Dictionary<Room, SurfaceRecord>();
        ComputeBuffer _surfaces;
        RenderTexture _wallSrc, _wall0, _wall1, _directionSrc, _direction0, _direction1;
        RenderTexture _wallCur, _directionCur;

        void InitSurfaces()
        {
            _surfaces = new ComputeBuffer(_surfaceData.Length, 48);
            _wallSrc = Make2D("BounceWallSource"); _wall0 = Make2D("BounceWall0"); _wall1 = Make2D("BounceWall1");
            _directionSrc = Make2D("BounceDirectionSource"); _direction0 = Make2D("BounceDirection0"); _direction1 = Make2D("BounceDirection1");
        }

        void ReleaseSurfaces()
        {
            _surfaces?.Release(); _surfaces = null;
            foreach (var r in new[] { _wallSrc, _wall0, _wall1, _directionSrc, _direction0, _direction1 })
                if (r) { r.Release(); if (Application.isPlaying) Destroy(r); else DestroyImmediate(r); }
            _reflectance.Clear(); _roomSurfaces.Clear(); _wallCur = _directionCur = null;
        }

        SurfaceRecord SurfacesFor(Room room)
        {
            if (_roomSurfaces.TryGetValue(room, out var record)) return record;
            var a = streamer ? streamer.assets : null;
            record = new SurfaceRecord
            {
                floor = Reflectance(a ? a.FloorMaterial(room) : null, 0.25f),
                wall = Reflectance(a ? a.WallMaterial(room) : null, 0.5f),
                ceiling = Reflectance(a ? (room.PlasterCeiling && a.ceilingPlaster ? a.ceilingPlaster : a.ceilingTile) : null, 0.7f),
            };
            _roomSurfaces[room] = record; return record;
        }

        Vector4 Reflectance(Material material, float fallback)
        {
            if (!material) return new Vector4(fallback, fallback, fallback, 0);
            if (_reflectance.TryGetValue(material, out var value)) return value;
            var color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor").linear : Color.white;
            var texture = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
            if (texture)
            {
                // A bounded downsample works with non-readable imported textures; never change import flags.
                var previous = RenderTexture.active;
                var rt = RenderTexture.GetTemporary(16, 16, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                var sample = new Texture2D(16, 16, TextureFormat.RGBAHalf, false, true);
                try
                {
                    Graphics.Blit(texture, rt); RenderTexture.active = rt;
                    sample.ReadPixels(new Rect(0, 0, 16, 16), 0, 0); sample.Apply(false, false);
                    var pixels = sample.GetPixels(); Color mean = Color.clear;
                    foreach (var pixel in pixels) mean += pixel;
                    color *= mean / pixels.Length;
                }
                finally
                {
                    RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt);
                    if (Application.isPlaying) Destroy(sample); else DestroyImmediate(sample);
                }
            }
            value = new Vector4(Mathf.Clamp(color.r, 0.015f, 0.9f), Mathf.Clamp(color.g, 0.015f, 0.9f), Mathf.Clamp(color.b, 0.015f, 0.9f), 0);
            _reflectance[material] = value; return value;
        }

        void BindBounceSurfaces()
        {
            compute.SetBuffer(_kBounceSrc, "_Surfaces", _surfaces);
            compute.SetTexture(_kBounceSrc, "_WallSrc", _wallSrc);
            compute.SetTexture(_kBounceSrc, "_DirectionSrc", _directionSrc);
        }

        void BindBounceDiffusion(int step)
        {
            compute.SetBuffer(_kBounceDiff, "_Surfaces", _surfaces);
            compute.SetTexture(_kBounceDiff, "_WallSrcIn", _wallSrc);
            compute.SetTexture(_kBounceDiff, "_DirectionSrcIn", _directionSrc);
            compute.SetTexture(_kBounceDiff, "_WallIn", step == 0 ? _wallSrc : _wallCur);
            compute.SetTexture(_kBounceDiff, "_DirectionIn", step == 0 ? _directionSrc : _directionCur);
            _wallCur = (step & 1) == 0 ? _wall0 : _wall1;
            _directionCur = (step & 1) == 0 ? _direction0 : _direction1;
            compute.SetTexture(_kBounceDiff, "_WallOut", _wallCur);
            compute.SetTexture(_kBounceDiff, "_DirectionOut", _directionCur);
        }

        void BindBounceOutputs()
        {
            Shader.SetGlobalTexture("_LGridWallBounce", _wallCur ? _wallCur : _wall0);
            Shader.SetGlobalTexture("_LGridBounceDirection", _directionCur ? _directionCur : _direction0);
        }
    }
}
