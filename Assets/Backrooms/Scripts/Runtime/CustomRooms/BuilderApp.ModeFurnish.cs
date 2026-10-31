using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    // Build mode, FURNISH: props (furniture, racks, lamps, clocks) and loot spots, placed on the floor (racks and clocks
    // against a wall), then moved, lifted, turned and sized with SELECT's handles.
    public partial class BuilderApp
    {
        sealed class FurnishMode : Mode
        {
            public FurnishMode(BuilderApp app) : base(app) { }
            public override string Name => "FURNISH";
            public override int Icon => 8;
            public override string Tip => "Furniture, racks, lamps, clocks and loot.";
            public override BuilderTool[] Tools => new[] { BuilderTool.Prop, BuilderTool.Loot };
            public override string ToolName(BuilderTool t) => t == BuilderTool.Prop ? "PROPS" : "LOOT SPOT";
            public override string ToolTip(BuilderTool t) => t == BuilderTool.Prop ? "Furniture and things." : "A spot where an item can be found.";
            static readonly PropKind[] Kinds = { PropKind.Box, PropKind.Pallet, PropKind.Sofa, PropKind.Armchair, PropKind.OfficeChair, PropKind.ConferenceTable, PropKind.FloorLamp, PropKind.StorageRack, PropKind.WallClock };
            static readonly string[] Names = { "BOX", "PALLET", "SOFA", "ARMCHAIR", "OFFICE CHAIR", "TABLE", "FLOOR LAMP", "STORAGE RACK", "WALL CLOCK" };
            public override void Options(ref ImUi.Column col)
            {
                if (app._tool == BuilderTool.Loot)
                {
                    ImUi.Header(col.Row(24), "Item name");
                    app._lootItem = ImUi.TextField(col.Row(30), app._lootItem, "lootitem", 40);
                    ImUi.Dimmed(col.Row(40), "Spawns as a loose box until the game has loot of its own.");
                    return;
                }
                ImUi.Header(col.Row(24), "Prop");
                for (int i = 0; i < Kinds.Length; i++)
                    if (ImUi.Button(col.Row(30), Names[i], app._propKind == Kinds[i], WallProp(Kinds[i]) ? "Click beside a solid wall." : "Click the floor to place it."))
                        app._propKind = Kinds[i];
            }
            public override string Hint()
            {
                if (app._tool == BuilderTool.Loot) return $"Click the floor to mark where '{app._lootItem}' can be found.";
                int i = System.Array.IndexOf(Kinds, app._propKind);
                string what = i >= 0 ? Names[i].ToLowerInvariant() : "prop";
                return WallProp(app._propKind) ? $"Click the floor beside a solid wall to put a {what} against it." : $"Click the floor to put a {what} there, then drag it or its handles.";
            }
        }

        static Vector3 DefaultSize(PropKind k)
        {
            switch (k)
            {
                case PropKind.Sofa: return new Vector3(1.9f, 0.8f, 0.9f);
                case PropKind.ConferenceTable: return new Vector3(2.4f, 0.74f, 1.1f);
                case PropKind.Pallet: return new Vector3(1.2f, 1.6f, 1.0f);
                case PropKind.FloorLamp: return new Vector3(0.4f, 1.6f, 0.4f);
                case PropKind.WallClock: return new Vector3(0.36f, 2.1f, 0.06f);
                case PropKind.StorageRack: return new Vector3(2.3f, 2.4f, 0.95f);
                case PropKind.Armchair: return new Vector3(0.9f, 0.85f, 0.9f);
                case PropKind.OfficeChair: return new Vector3(0.6f, 1.0f, 0.6f);
                default: return new Vector3(0.6f, 0.5f, 0.5f);
            }
        }

        Vector2 Snap(Vector2 m) => new Vector2(Mathf.Round(m.x / 0.15f) * 0.15f, Mathf.Round(m.y / 0.15f) * 0.15f);

        bool InsideRoom(Vector2 m) { var s = V.SizeMetres; return m.x > 0.05f && m.y > 0.05f && m.x < s.x - 0.05f && m.y < s.y - 0.05f; }

        void PlaceProp()
        {
            if (!_hoverGround) return;
            var m = Snap(_hoverMetres);
            if (!InsideRoom(m)) { SetStatus("Place props inside the room."); return; }
            var size = DefaultSize(_propKind);
            float yaw = 0f;
            if (WallProp(_propKind) && !V.TryWallPlacement(_hoverMetres, size, out m, out yaw))
            { SetStatus("Click near a solid wall with enough space. Openings cannot support this prop."); return; }
            PushUndo();
            var v = V;
            v.props.Add(new PropDef { kind = _propKind, x = m.x, z = m.y, yaw = yaw, size = size, lit = true, variant = Random.Range(0, 1 << 16) });
            _selected = new Selection { kind = SelKind.Prop, index = v.props.Count - 1 };
            _tool = BuilderTool.Select;
            BuilderSounds.Play(BuilderSounds.Sfx.Place, 0.85f + Random.value * 0.2f);
            Changed();
        }

        void PlaceLoot()
        {
            if (!_hoverGround) return;
            var m = Snap(_hoverMetres);
            if (!InsideRoom(m)) { SetStatus("Place loot inside the room."); return; }
            PushUndo();
            var v = V;
            v.loot.Add(new LootDef { item = string.IsNullOrWhiteSpace(_lootItem) ? "Loot" : _lootItem, x = m.x, z = m.y });
            _selected = new Selection { kind = SelKind.Loot, index = v.loot.Count - 1 };
            _tool = BuilderTool.Select;
            BuilderSounds.Play(BuilderSounds.Sfx.Place, 1.2f);
            Changed();
        }

        static bool WallProp(PropKind kind) => kind == PropKind.WallClock || kind == PropKind.StorageRack;
    }
}
