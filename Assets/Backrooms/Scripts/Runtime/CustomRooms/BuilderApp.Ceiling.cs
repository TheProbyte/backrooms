using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Build mode: the ceiling hidden fully (owner: "with the variated ceilings sometimes the 'above' drop ceiling area covers
    /// the room when building it"). Hiding went by the ceiling's own materials, but once the world has run (Test in world) the
    /// chunk builder uses its normal-mapped and stained copies of them and builds the space above the tiles (joists, deck,
    /// ducts, insulation) in materials of their own, so some of it stayed. Now everything above a cut height is gone: renderers
    /// lying wholly above it are switched off, and the world's own wall and ceiling shader clips the rest at it (the portal clip
    /// plane, set only while the builder's camera draws). The cut is just under the room's lowest ceiling; CEILING [H] shows it
    /// again (SOLID view and walking always show it).
    /// </summary>
    public partial class BuilderApp
    {
        bool _ceilingShown;
        readonly List<Renderer> _aboveCut = new List<Renderer>();
        static readonly int ClipPlane = Shader.PropertyToID("_PortalClipPlane");
        const string ClipKeyword = "BACKROOMS_PORTAL_CLIP";

        /// <summary>The ceiling and everything above it are hidden (the views that look in from above, unless shown).</summary>
        bool HidesCeiling => !_walking && !_ceilingShown && _view != BuilderView.Solid;

        /// <summary>Height (above the floor) everything is cut at: a little under the lowest ceiling of the room.</summary>
        float CutHeight
        {
            get
            {
                float low = float.MaxValue;
                var v = V;
                for (int z = 0; z < v.depth; z++)
                for (int x = 0; x < v.width; x++)
                    if (!v.Outside(x, z)) low = Mathf.Min(low, v.RoomAt(x, z).ceiling);
                if (low == float.MaxValue) low = Generation.OfficeBiome.CeilingHeight;
                return Mathf.Min(low, Generation.OfficeBiome.CeilingHeight) - 0.06f;
            }
        }

        /// <summary>After a rebuild: the renderers that lie wholly above the cut (the ceiling, the space above it).</summary>
        void IndexAboveCut()
        {
            _aboveCut.Clear();
            if (!_previewObject) return;
            float cut = _previewRoot.position.y + CutHeight;
            foreach (var r in _previewObject.GetComponentsInChildren<Renderer>(true))
                if (r.bounds.min.y >= cut - 0.05f || IsCeilingMaterial(r.sharedMaterial) || Generation.MaterialMaps.IsCeilingVariant(r.sharedMaterial) && r.bounds.min.y > cut - 0.5f)
                    _aboveCut.Add(r);
            ApplyCeiling();
        }

        void ApplyCeiling()
        {
            bool hide = HidesCeiling;
            foreach (var r in _aboveCut) if (r) r.enabled = !hide;
        }

        void ToggleCeiling()
        {
            _ceilingShown = !_ceilingShown;
            if (_ceilingShown && _view != BuilderView.Solid) SetStatus("Ceiling shown. [H] hides it again.");
            else if (!_ceilingShown) SetStatus(_view == BuilderView.Solid ? "SOLID always shows the ceiling: the other views hide it." : "Ceiling hidden: everything above the lowest ceiling is cut away.");
            ApplyCeiling();
            BuilderSounds.Play(BuilderSounds.Sfx.Tick, _ceilingShown ? 1.1f : 0.9f);
        }

        void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (cam != _cam || _screen != ScreenKind.Editor || _bp == null || !HidesCeiling) return;
            // Keep what lies below the plane y = cut: dot((x, y, z, 1), (0, -1, 0, cut)) >= 0.
            Shader.SetGlobalVector(ClipPlane, new Vector4(0f, -1f, 0f, _previewRoot.position.y + CutHeight));
            Shader.EnableKeyword(ClipKeyword);
        }

        /// <summary>The clip is only ever on while the builder's own camera draws (never for the catalog studio, never in the world).</summary>
        static void ClearClip()
        {
            if (!Shader.IsKeywordEnabled(ClipKeyword)) return;
            Shader.DisableKeyword(ClipKeyword);
            Shader.SetGlobalVector(ClipPlane, Vector4.zero);
        }
    }
}
