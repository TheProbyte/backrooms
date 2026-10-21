using System;
using System.Reflection;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.UI;

namespace Backrooms.Camcorder
{
    /// <summary>
    /// F5 > WORLD GEN: live world-generation rules with a biome map and readouts. Changes rebuild every
    /// loaded chunk except the one you are standing in, so you never end up inside a wall.
    /// </summary>
    public partial class CamcorderVisualMenu
    {
        WorldStreamer _ws;
        Text _wgReadout;
        RawImage _wgMap;
        RectTransform _wgMarker;
        Texture2D _wgTex;
        InputField _wgSeedField;
        float _wgDirtyAt = -1f, _wgNextReadout, _wgNextMap;
        Vector2Int _wgMapCenter = new Vector2Int(int.MinValue, 0);
        const int MapChunks = 64;           // map covers 64 x 64 chunks (~920 m) around you

        void BuildWorldGen()
        {
            _ws = FindFirstObjectByType<WorldStreamer>();
            if (!_ws) { Section("WORLD GEN", "No world streamer in this scene."); return; }

            Section("LIVE DATA", "Updates while you walk. Settings changes rebuild the world around you (not the chunk you stand in).");
            // Biome map.
            var mapRow = Rect("BiomeMap", _list);
            mapRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 292;
            mapRow.gameObject.AddComponent<Canvas>();             // nested canvas: map updates don't rebatch the whole panel
            var mapGo = Rect("Map", mapRow);
            mapGo.anchorMin = mapGo.anchorMax = new Vector2(0, 1); mapGo.pivot = new Vector2(0, 1);
            mapGo.sizeDelta = new Vector2(280, 280); mapGo.anchoredPosition = new Vector2(0, -6);
            _wgMap = mapGo.gameObject.AddComponent<RawImage>();
            _wgTex = new Texture2D(MapChunks, MapChunks, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            _wgMap.texture = _wgTex;
            _wgMarker = Rect("You", mapGo);
            _wgMarker.sizeDelta = new Vector2(7, 7);
            _wgMarker.gameObject.AddComponent<Image>().color = Color.white;
            var legend = Label(mapRow, "", 13, TextCol, TextAnchor.UpperLeft);
            var lr = legend.rectTransform; lr.anchorMin = new Vector2(0, 1); lr.anchorMax = new Vector2(1, 1); lr.pivot = new Vector2(0, 1);
            lr.offsetMin = new Vector2(290, -280); lr.offsetMax = new Vector2(0, -6);
            legend.text = $"<color=#{ColorUtility.ToHtmlStringRGB(_ws.Settings.hallways.mapColor)}>■</color> Hallways\n" +
                          $"<color=#{ColorUtility.ToHtmlStringRGB(_ws.Settings.offices.mapColor)}>■</color> Offices\n" +
                          $"<color=#{ColorUtility.ToHtmlStringRGB(_ws.Settings.warehouse.mapColor)}>■</color> Warehouse\n\n" +
                          "Each pixel is one 14.4 m chunk.\nBrighter = deeper in that biome, darker = blending.\nWhite dot: you.\n\nThe map is the biome density field split by the delimiters below.";
            legend.supportRichText = true;

            var readRow = Rect("Readout", _list);
            readRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 250;
            readRow.gameObject.AddComponent<Canvas>();
            _wgReadout = Label(readRow, "", 14, TextCol, TextAnchor.UpperLeft);
            Stretch(_wgReadout.rectTransform);
            _wgReadout.supportRichText = true;

            // Seed.
            Section("SEED", "Same seed + same settings + same game version = the same world for anyone.");
            var seedRow = Rect("SeedRow", _list);
            seedRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            var hl = seedRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 6; hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = true;
            _wgSeedField = InputBox(seedRow, _ws.Seed.ToString());
            Button(seedRow, "Load seed", () => { if (int.TryParse(_wgSeedField.text, out int s)) { _ws.SetSeed(s); _wgMapCenter.x = int.MinValue; } });
            Button(seedRow, "Random", () => { int s = new System.Random().Next(1, int.MaxValue); _wgSeedField.text = s.ToString(); _ws.SetSeed(s); _wgMapCenter.x = int.MinValue; });
            Button(seedRow, "Copy code", () => GUIUtility.systemCopyBuffer = _ws.Settings.WorldCode(_ws.Seed));

            // Field + delimiters.
            var set = _ws.Settings;
            Section("BIOME FIELD", "A noise density map (Hytale World Gen V2 style) picks each chunk's biome.");
            WG(Slider("Region size", "Size of biome regions in chunks. Larger = bigger areas of the same biome.", 2f, 30f, "0.0", " ch", () => set.biomeScale, v => set.biomeScale = v));
            WG(Slider("Detail octaves", "Layers of noise. More octaves = more ragged, detailed region edges.", 1f, 6f, "0", "", () => set.octaves, v => set.octaves = Mathf.RoundToInt(v), true));
            WG(Slider("Domain warp", "Bends region boundaries into organic, swirling shapes instead of noise blobs.", 0f, 2f, "0.00", "", () => set.warp, v => set.warp = v));
            WG(Slider("Hallways below", "Density below this value is the Hallways biome.", 0f, 1f, "0.00", "", () => set.hallwaysBelow, v => set.hallwaysBelow = Mathf.Min(v, set.warehouseAbove - 0.01f)));
            WG(Slider("Warehouse above", "Density above this value is the Warehouse biome. Between the two is Offices.", 0f, 1f, "0.00", "", () => set.warehouseAbove, v => set.warehouseAbove = Mathf.Max(v, set.hallwaysBelow + 0.01f)));
            WG(Slider("Transition width", "How gradually one biome blends into the next. Rules are interpolated across this band.", 0f, 0.4f, "0.00", "", () => set.transition, v => set.transition = v));

            BiomeGroup("OFFICES rules", () => _ws.Settings.offices);
            BiomeGroup("HALLWAYS rules", () => _ws.Settings.hallways);
            BiomeGroup("WAREHOUSE rules", () => _ws.Settings.warehouse);

            Section("ACTIONS", "");
            var actRow = Rect("Actions", _list);
            actRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            var ah = actRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            ah.spacing = 6; ah.childControlWidth = true; ah.childControlHeight = true; ah.childForceExpandWidth = true;
            Button(actRow, "Rebuild now", () => _wgDirtyAt = 0f);
            Button(actRow, "Reset world gen", () =>
            {
                var d = new WorldGenSettings();
                CopyInto(d, _ws.Settings);
                RefreshAll(); _wgDirtyAt = 0f;
            });
        }

        /// <summary>Marks a control as world-gen: changing it schedules a rebuild.</summary>
        void WG(UnityEngine.UI.Slider s) { if (s) s.onValueChanged.AddListener(_ => { if (!_refreshing) _wgDirtyAt = Time.unscaledTime + 0.35f; }); }

        void BiomeGroup(string title, Func<BiomeRules> rules)
        {
            var container = Group(title, "Every number this biome's generator uses. Hover for details.");
            var outer = _list; _list = container;
            foreach (var f in typeof(BiomeRules).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType != typeof(float)) continue;
                var tip = f.GetCustomAttribute<TooltipAttribute>()?.tooltip ?? "";
                float min = 0f, max = 1f; string fmt = "0.00", unit = "";
                if (f.Name.StartsWith("tallM")) { min = 2.7f; max = OfficeBiome.MaxCeilingHeight; unit = " m"; fmt = "0.0"; }
                else if (f.Name == "pillarSpacing") { min = 2f; max = 6f; unit = " cells"; fmt = "0.0"; }
                else if (f.Name == "halfWalls") { min = 0f; max = 4f; fmt = "0"; }
                var field = f;
                WG(Slider(Nice(f.Name), tip, min, max, fmt, unit, () => (float)field.GetValue(rules()), v => field.SetValue(rules(), v)));
            }
            _list = outer;
        }

        static string Nice(string camel)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < camel.Length; i++)
            {
                char c = camel[i];
                if (i == 0) sb.Append(char.ToUpper(c));
                else if (char.IsUpper(c)) { sb.Append(' '); sb.Append(char.ToLower(c)); }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static void CopyInto(WorldGenSettings src, WorldGenSettings dst)
        {
            foreach (var f in typeof(WorldGenSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType == typeof(BiomeRules))
                {
                    var from = (BiomeRules)f.GetValue(src); var to = (BiomeRules)f.GetValue(dst);
                    foreach (var bf in typeof(BiomeRules).GetFields()) bf.SetValue(to, bf.GetValue(from));
                }
                else f.SetValue(dst, f.GetValue(src));
            }
        }

        /// <summary>Collapsible section; returns the container the rows go into.</summary>
        RectTransform Group(string title, string desc)
        {
            var header = Rect("Group_" + title, _list);
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            var container = Rect("Rows_" + title, _list);
            var vl = container.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = 4; vl.padding = new RectOffset(8, 0, 0, 6);
            vl.childControlHeight = true; vl.childControlWidth = true; vl.childForceExpandHeight = false;
            var d = Label(container, desc, 13, Dim, TextAnchor.UpperLeft);
            d.gameObject.AddComponent<LayoutElement>().preferredHeight = 18;
            Text label = null;
            var img = Button(header, "", () =>
            {
                bool open = !container.gameObject.activeSelf;
                container.gameObject.SetActive(open);
                label.text = (open ? "▼  " : "►  ") + title;
                HideTip();
            });
            img.color = new Color(1, 1, 1, 0.06f);
            Stretch(img.rectTransform);       // the header row has no layout group, so fill it explicitly
            label = header.GetComponentInChildren<Text>();
            label.alignment = TextAnchor.MiddleLeft; label.color = Accent; label.fontSize = 16;
            label.rectTransform.offsetMin = new Vector2(10, 0);
            label.text = "►  " + title;
            container.gameObject.SetActive(false);
            return container;
        }

        InputField InputBox(RectTransform parent, string value)
        {
            var box = Rect("Input", parent);
            var img = box.gameObject.AddComponent<Image>(); img.color = new Color(1, 1, 1, 0.12f);
            var text = Label(box, value, 15, TextCol, TextAnchor.MiddleLeft);
            Stretch(text.rectTransform); text.rectTransform.offsetMin = new Vector2(8, 0);
            text.supportRichText = false;
            var field = box.gameObject.AddComponent<InputField>();
            field.textComponent = text; field.targetGraphic = img;
            field.contentType = InputField.ContentType.IntegerNumber;
            field.text = value;
            return field;
        }

        void UpdateWorldGen()
        {
            if (!_ws) return;
            if (_wgDirtyAt >= 0f && Time.unscaledTime >= _wgDirtyAt)
            {
                _wgDirtyAt = -1f;
                _ws.Regenerate();
                _ws.Settings.Save();
                _wgMapCenter.x = int.MinValue;            // redraw map
            }
            if (!IsOpen || _tab != 2 || !_wgReadout) return;

            var cam = Camera.main ? Camera.main.transform : null;
            Vector3 pos = cam ? cam.position : Vector3.zero;
            var chunk = WorldStreamer.ChunkOf(pos);
            if (Time.unscaledTime >= _wgNextMap || chunk != _wgMapCenter) { DrawMap(chunk); _wgNextMap = Time.unscaledTime + 10f; }
            // Marker position inside the 64 x 64 map (sub-chunk precision).
            float fx = pos.x / OfficeBiome.ChunkSize - (_wgMapCenter.x - MapChunks / 2);
            float fz = pos.z / OfficeBiome.ChunkSize - (_wgMapCenter.y - MapChunks / 2);
            var anchor = new Vector2(fx / MapChunks, fz / MapChunks);
            if ((anchor - _wgMarker.anchorMin).sqrMagnitude > 1e-6f) _wgMarker.anchorMin = _wgMarker.anchorMax = anchor;


            if (Time.unscaledTime < _wgNextReadout) return;
            _wgNextReadout = Time.unscaledTime + 0.25f;
            var sample = _ws.Settings.Evaluate(_ws.Seed, chunk);
            var layout = _ws.LayoutAt(chunk);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>Biome here:</b> {layout?.biomeName ?? sample.Dominant}   (density {sample.density:0.000})");
            sb.AppendLine($"Blend   hallways {sample.wHallways * 100:0}%   offices {sample.wOffices * 100:0}%   warehouse {sample.wWarehouse * 100:0}%");
            sb.AppendLine($"Position {pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0}    chunk {chunk.x}, {chunk.y}" + (_ws.FrozenChunk == chunk ? "  <color=#E9C45C>(kept: you are in it)</color>" : ""));
            sb.AppendLine($"World code <b>{_ws.Settings.WorldCode(_ws.Seed)}</b>");
            if (layout != null)
            {
                int offices = 0, big = 0, halls = 0, crawls = 0, lounges = 0, conf = 0, tall = 0, lit = 0, dark = 0;
                foreach (var r in layout.rooms)
                {
                    switch (r.kind)
                    {
                        case RoomKind.Office: offices++; break; case RoomKind.BigOffice: big++; break;
                        case RoomKind.Hallway: halls++; break; case RoomKind.Crawl: crawls++; break;
                        case RoomKind.Lounge: lounges++; break; case RoomKind.Conference: conf++; break;
                    }
                    if (r.Tall) tall++;
                    if (r.lights == LightMode.AllOn) lit++; else if (r.lights == LightMode.Dark) dark++;
                }
                int on = 0; foreach (var f in layout.fixtures) if (f.on) on++;
                int racks = 0; foreach (var p in layout.props) if (p.kind == PropKind.StorageRack) racks++;
                sb.AppendLine($"<b>This chunk:</b> {layout.rooms.Count} rooms - {big} big, {offices} office, {halls} hall, {lounges} lounge, {conf} conference, {crawls} crawl");
                sb.AppendLine($"   {tall} tall ceilings, lights: {lit} lit / {layout.rooms.Count - lit - dark} mixed / {dark} dark rooms, {on}/{layout.fixtures.Count} fixtures on");
                sb.AppendLine($"   {layout.pillars.Count} pillars, {layout.halfWalls.Count} half walls, {layout.props.Count} props ({racks} racks), {layout.plates.Count} outlets/switches");
            }
            var lm = FindFirstObjectByType<FixtureLightManager>();
            sb.AppendLine($"<b>Streaming:</b> {_ws.LoadedChunks} chunks loaded, {_ws.QueuedChunks} queued, {_ws.ChunksBuilt} built this session");
            sb.AppendLine($"   chunk data {_ws.AverageBuildMs:0.0} ms avg (worker threads), {(lm ? lm.ActiveLights : 0)} pooled lights active");
            sb.AppendLine($"   {1f / Mathf.Max(Time.smoothDeltaTime, 1e-4f):0} fps");
            _wgReadout.text = sb.ToString();
        }

        void DrawMap(Vector2Int center)
        {
            _wgMapCenter = center;
            var s = _ws.Settings;
            var px = new Color32[MapChunks * MapChunks];
            for (int y = 0; y < MapChunks; y++)
            for (int x = 0; x < MapChunks; x++)
            {
                var c = new Vector2Int(center.x - MapChunks / 2 + x, center.y - MapChunks / 2 + y);
                var e = s.Evaluate(_ws.Seed, c);
                Color col = s.hallways.mapColor * e.wHallways + s.offices.mapColor * e.wOffices + s.warehouse.mapColor * e.wWarehouse;
                float purity = Mathf.Max(e.wHallways, Mathf.Max(e.wOffices, e.wWarehouse));
                col *= Mathf.Lerp(0.55f, 1f, purity);
                if (_ws.LayoutAt(c) == null) col *= 0.75f;                     // not loaded yet
                if ((c.x == 0 && c.y == 0)) col = Color.Lerp(col, Color.white, 0.5f);  // spawn
                col.a = 1f;
                px[y * MapChunks + x] = col;
            }
            _wgTex.SetPixels32(px);
            _wgTex.Apply(false);
        }
    }
}
