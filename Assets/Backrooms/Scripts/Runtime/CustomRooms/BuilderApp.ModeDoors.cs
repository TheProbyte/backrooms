using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    // Build mode, DOORS & WINDOWS: a door, double door, arch, window or tall opening put where you point on a wall, then
    // sized by its handles (BuilderApp.Handles).
    public partial class BuilderApp
    {
        sealed class DoorsMode : Mode
        {
            public DoorsMode(BuilderApp app) : base(app) { }
            public override string Name => "DOORS";
            public override int Icon => 2;
            public override string Tip => "Doors, arches and windows of any size, on any wall.";
            public override BuilderTool[] Tools => new[] { BuilderTool.Opening };
            static readonly OpeningKind[] Kinds = { OpeningKind.Doorway, OpeningKind.DoubleDoor, OpeningKind.Arch, OpeningKind.Window, OpeningKind.Tall };
            static readonly string[] Names = { "DOOR", "DOUBLE DOOR", "ARCH", "WINDOW", "TALL OPENING" };
            static readonly string[] Tips =
            {
                "0.9 × 2.1 m to start; drag its handles to any size.", "1.7 m wide.", "A round-topped opening.",
                "A hole with a sill 0.9 m up. Drag its bottom handle to the floor to make it a door.", "Up to the ceiling.",
            };
            public override void Options(ref ImUi.Column col)
            {
                ImUi.Header(col.Row(24), "Kind");
                if (app._opening == OpeningKind.Gap) app._opening = OpeningKind.Doorway;
                for (int i = 0; i < Kinds.Length; i++) if (ImUi.Button(col.Row(30), Names[i], app._opening == Kinds[i], Tips[i])) app._opening = Kinds[i];
                col.Space(4);
                ImUi.Dimmed(col.Row(76), "Point at a wall and click. Then drag it: sides wider, top taller, bottom for a sill, middle along the wall. A door in the outer wall is a way in.");
            }
            public override string Hint()
            {
                int i = System.Array.IndexOf(Kinds, app._opening);
                string what = i >= 0 ? Names[i].ToLowerInvariant() : "door";
                return $"Point at a wall and click to put a {what} there (white: it fits, red: it does not). Then drag its handles.";
            }
        }

        /// <summary>The Opening tool's kinds as openings of their own size (OPEN GAP stays an edge tool).</summary>
        OpeningDef Preset(OpeningKind k, float ceiling)
        {
            switch (k)
            {
                case OpeningKind.DoubleDoor: return new OpeningDef { from = 0f, to = 1.7f, top = 2.1f };
                case OpeningKind.Arch: return new OpeningDef { from = 0f, to = 1.0f, top = 2.35f, arch = true };
                case OpeningKind.Window: return new OpeningDef { from = 0f, to = 1.2f, sill = 0.9f, top = 2.0f };
                case OpeningKind.Tall: return new OpeningDef { from = 0f, to = 1.0f, top = ceiling };
                default: return new OpeningDef { from = 0f, to = 0.9f, top = 2.1f };
            }
        }

        void PlaceOpening()
        {
            if (_ghost == null) { SetStatus("Point at a wall to put it there."); BuilderSounds.Play(BuilderSounds.Sfx.Error); return; }
            if (!_ghostFits) { SetStatus("It does not fit there: it must sit on solid wall, clear of other openings."); BuilderSounds.Play(BuilderSounds.Sfx.Error); return; }
            PushUndo();
            V.openings.Add(_ghost);
            _selected = new Selection { kind = SelKind.Opening, index = V.openings.Count - 1 };
            _dispFor = null;
            _tool = BuilderTool.Select;
            _ghost = null;
            BuilderSounds.Play(BuilderSounds.Sfx.Place, 0.9f + Random.value * 0.15f);
            SetStatus("Drag its sides to widen it, its top to make it taller, its bottom up for a window, its middle along the wall. Alt: no snapping.", 8f);
            Changed();
        }

        /// <summary>Selects the opening the pointer is on (through its hole, or the wall round it). True if one was.</summary>
        bool SelectOpeningAt(Vector2 screen)
        {
            if (!RayWall(PointerRay(screen), false, out bool vert, out int line, out float along, out float height)) return false;
            var v = V;
            for (int i = 0; i < v.openings.Count; i++)
            {
                var o = v.openings[i];
                if (o.vertical != vert || o.line != line) continue;
                if (along < o.from - 0.08f || along > o.to + 0.08f || height > o.top + 0.15f) continue;
                _selected = new Selection { kind = SelKind.Opening, index = i };
                _dispFor = null;
                BuilderSounds.Play(BuilderSounds.Sfx.Pick);
                return true;
            }
            return false;
        }
    }
}
