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
            public override string ToolName(BuilderTool t) => t == BuilderTool.Prop ? "PROPS" : "LOOT";
            public override string ToolTip(BuilderTool t) => t == BuilderTool.Prop ? "Furniture and things." : "A spot where an item can be found.";
            public override void Options(ref ImUi.Column col)
            {
                if (app._tool == BuilderTool.Loot)
                {
                    ImUi.Header(col.Row(24), "Item name");
                    app._lootItem = ImUi.TextField(col.Row(30), app._lootItem, "lootitem", 40);
                    ImUi.Dimmed(col.Row(40), "Spawns as a loose box until the game has loot of its own.");
                    return;
                }
                var items = BuilderCatalog.Items(app.assets);
                // Categories (three rows of two), search, then the pictures, two to a row.
                var cats = BuilderCatalog.Categories;
                for (int row = 0; row < 3; row++)
                {
                    var r = col.Row(26);
                    for (int k = 0; k < 2; k++)
                    {
                        int ci = row * 2 + k;
                        if (ImUi.Button(new Rect(r.x + k * (r.width / 2f), r.y, r.width / 2f - 3, r.height), cats[ci], app._catalogCategory == ci)) app._catalogCategory = ci;
                    }
                }
                app._catalogSearch = ImUi.TextField(col.Row(28), app._catalogSearch, "catalogsearch", 30);
                if (string.IsNullOrEmpty(app._catalogSearch)) GUI.Label(new Rect(col.x + 8, col.y - 34, col.w, 28), "search…", ImUi.Small);
                string cat = cats[app._catalogCategory], q = (app._catalogSearch ?? "").Trim().ToLowerInvariant();
                var shown = items.FindAll(it => (cat == "ALL" || it.category == cat) && (q.Length == 0 || it.name.ToLowerInvariant().Contains(q)));
                float cw = (col.w - 6f) * 0.5f, ch = 112f;
                for (int n = 0; n < shown.Count; n += 2)
                {
                    var r = col.Row(ch);
                    for (int k = 0; k < 2 && n + k < shown.Count; k++)
                    {
                        var it = shown[n + k];
                        var cell = new Rect(r.x + k * (cw + 6f), r.y, cw, ch);
                        bool on = app._catalogPick == it;
                        if (GUI.Button(cell, new GUIContent("", it.name), GUIStyle.none)) { app._catalogPick = it; app._propKind = it.kind; BuilderSounds.Play(BuilderSounds.Sfx.Tick, 1.1f); }
                        ImUi.Fill(cell, on ? ImUi.Accent : ImUi.Raised);
                        var pic = new Rect(cell.x + 3, cell.y + 3, cell.width - 6, cell.height - 34);
                        if (it.picture) GUI.DrawTexture(pic, it.picture, ScaleMode.ScaleAndCrop);
                        else { ImUi.Fill(pic, new Color(0.16f, 0.16f, 0.17f, 1f)); GUI.Label(pic, it.kind == PropKind.StorageRack ? "RACK" : "CLOCK", new GUIStyle(ImUi.Small) { alignment = TextAnchor.MiddleCenter }); }
                        GUI.Label(new Rect(cell.x + 4, cell.yMax - 31, cell.width - 8, 30), it.name, new GUIStyle(ImUi.Small) { fontSize = 11, wordWrap = true, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, normal = { textColor = on ? Color.black : ImUi.Text } });
                    }
                }
                if (shown.Count == 0) ImUi.Dimmed(col.Row(30), "Nothing matches.");
            }
            public override string Hint()
            {
                if (app._tool == BuilderTool.Loot) return $"Click the floor to mark where '{app._lootItem}' can be found.";
                var it = app._catalogPick;
                if (it == null) return "Pick something on the left, then click the floor to put it there.";
                return WallProp(it.kind) ? $"Click the floor beside a solid wall to put the {it.name.ToLowerInvariant()} against it." : $"Click the floor to put the {it.name.ToLowerInvariant()} there; then drag it or its handles.";
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
            if (_catalogPick == null) { SetStatus("Pick something from the catalog on the left first."); BuilderSounds.Play(BuilderSounds.Sfx.Error); return; }
            var size = _catalogPick.size;
            float yaw = 0f;
            if (WallProp(_propKind) && !V.TryWallPlacement(_hoverMetres, size, out m, out yaw))
            { SetStatus("Click near a solid wall with enough space. Openings cannot support this prop."); return; }
            PushUndo();
            var v = V;
            v.props.Add(new PropDef { kind = _catalogPick.kind, style = _catalogPick.style, x = m.x, z = m.y, yaw = yaw, size = size, lit = true, variant = Random.Range(0, 1 << 16) });
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

        BuilderCatalog.Item _catalogPick;
        int _catalogCategory;
        string _catalogSearch = "";

        /// <summary>The catalog's name for a placed prop (its kind and style), or its kind.</summary>
        string PropName(PropDef p)
        {
            var it = BuilderCatalog.Items(assets).Find(x => x.kind == p.kind && x.style == p.style && (p.kind != PropKind.Box || Mathf.Abs(x.size.x - p.size.x) < 0.01f));
            return it != null ? it.name : p.kind.ToString();
        }

        static bool WallProp(PropKind kind) => kind == PropKind.WallClock || kind == PropKind.StorageRack;
    }
}
