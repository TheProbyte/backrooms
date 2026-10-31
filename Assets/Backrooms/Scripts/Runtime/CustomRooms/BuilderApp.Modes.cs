using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Build mode's five modes (round 17: the builder made simpler): SELECT, BUILD, DOORS & WINDOWS, FURNISH and PAINT, each
    /// a small class in its own file (BuilderApp.Mode*.cs) with its tools, its options under the mode buttons and the hint
    /// line it shows. The tools themselves (<see cref="BuilderTool"/>) are what the input code acts on; a mode is the set of
    /// them it offers, and remembers the one last used.
    /// </summary>
    public partial class BuilderApp
    {
        abstract class Mode
        {
            protected readonly BuilderApp app;
            protected Mode(BuilderApp app) { this.app = app; last = Tools[0]; }
            public abstract string Name { get; }
            /// <summary>Icon index (ImUi.ToolButton).</summary>
            public abstract int Icon { get; }
            public abstract string Tip { get; }
            /// <summary>The tools it offers; the first is where it starts.</summary>
            public abstract BuilderTool[] Tools { get; }
            public virtual string ToolName(BuilderTool t) => t.ToString().ToUpperInvariant();
            public virtual string ToolTip(BuilderTool t) => "";
            /// <summary>The tool last used in this mode (it comes back to it).</summary>
            public BuilderTool last;
            /// <summary>Its options, under the mode buttons on the left.</summary>
            public virtual void Options(ref ImUi.Column col) { }
            /// <summary>What a click does now (the hint line at the bottom).</summary>
            public abstract string Hint();
        }

        Mode[] _modes;
        Mode[] Modes => _modes ?? (_modes = new Mode[] { new SelectMode(this), new BuildMode(this), new DoorsMode(this), new FurnishMode(this), new PaintMode(this), new WeirdMode(this) });

        Mode ModeOf(BuilderTool t)
        {
            foreach (var m in Modes) if (System.Array.IndexOf(m.Tools, t) >= 0) return m;
            return Modes[0];
        }

        Mode CurrentMode => ModeOf(_tool);

        void SetMode(Mode m) { if (m != CurrentMode) { SetTool(m.last); _linkFirst = null; BuilderSounds.Play(BuilderSounds.Sfx.Tick, 1.1f); } }

        /// <summary>A room (an area of cells) chosen: the inspector shows it, PAINT paints into it.</summary>
        void SelectRoom(int i)
        {
            _activeRoom = Mathf.Clamp(i, 0, V.rooms.Count - 1);
            _selected = new Selection { kind = SelKind.Room, index = _activeRoom };
        }
    }
}
