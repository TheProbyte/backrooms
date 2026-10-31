using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Build mode's safety and sharing (round 18): an autosave a few seconds after every change (a crash loses nothing;
    /// the library offers it back), rooms started from templates, tags and a search in the library, and rooms exported as a
    /// file and a share code (and imported from either).
    /// </summary>
    public partial class BuilderApp
    {
        // ---------------- Autosave ----------------

        float _autosaveCheck, _autosavedAt;
        string _lastAutosave;

        static string AutosaveDir => Path.Combine(RoomLibrary.Dir, "Autosave");
        static string AutosavePath(string id) => Path.Combine(AutosaveDir, (string.IsNullOrEmpty(id) ? "unsaved" : id) + ".json");

        void UpdateAutosave()
        {
            if (_bp == null || !_dirty || Time.unscaledTime < _autosaveCheck) return;
            _autosaveCheck = Time.unscaledTime + 3f;
            try
            {
                string json = _bp.ToJson();
                if (json == _lastAutosave) return;
                Directory.CreateDirectory(AutosaveDir);
                string path = AutosavePath(_bp.id), tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                _lastAutosave = json; _autosavedAt = Time.unscaledTime;
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Autosave failed: " + e.Message); _autosaveCheck = Time.unscaledTime + 30f; }
        }

        static void DropAutosave(string id)
        {
            try { var p = AutosavePath(id); if (File.Exists(p)) File.Delete(p); } catch { }
        }

        /// <summary>Autosaves newer than their saved room (or of rooms never saved): work a crash or a quit left behind.</summary>
        static List<(RoomBlueprint bp, string path)> Recoverable()
        {
            var list = new List<(RoomBlueprint, string)>();
            try
            {
                if (!Directory.Exists(AutosaveDir)) return list;
                foreach (var f in Directory.GetFiles(AutosaveDir, "*.json"))
                {
                    var bp = RoomBlueprint.FromJson(File.ReadAllText(f));
                    if (bp == null) continue;
                    string saved = Path.Combine(RoomLibrary.Dir, bp.id + ".json");
                    if (File.Exists(saved) && File.GetLastWriteTimeUtc(saved) >= File.GetLastWriteTimeUtc(f)) continue;
                    list.Add((bp, f));
                }
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not read autosaves: " + e.Message); }
            return list;
        }

        List<(RoomBlueprint bp, string path)> _recoverable;

        // ---------------- Library: search, tags, sharing ----------------

        string _libSearch = "";
        string _libTag = "";

        List<RoomBlueprint> FilteredRooms()
        {
            var all = RoomLibrary.All;
            var list = new List<RoomBlueprint>();
            string q = (_libSearch ?? "").Trim().ToLowerInvariant();
            foreach (var b in all)
            {
                if (_libTag.Length > 0 && (b.tags == null || !b.tags.Contains(_libTag))) continue;
                if (q.Length > 0 && !b.name.ToLowerInvariant().Contains(q) && (b.tags == null || !b.tags.Exists(t => t.Contains(q)))) continue;
                list.Add(b);
            }
            return list;
        }

        List<string> AllTags()
        {
            var tags = new List<string>();
            foreach (var b in RoomLibrary.All) if (b.tags != null) foreach (var t in b.tags) if (!tags.Contains(t)) tags.Add(t);
            tags.Sort();
            return tags;
        }

        /// <summary>The library's bar under its title: search, tags, import, and unsaved work to get back.</summary>
        float DrawLibraryBar(float y)
        {
            if (_recoverable == null) _recoverable = Recoverable();
            foreach (var (bp, path) in new List<(RoomBlueprint, string)>(_recoverable))
            {
                var r = new Rect(40, y, Mathf.Min(1100f, ImUi.Width - 80f), 40);
                ImUi.Fill(r, new Color(0.3f, 0.22f, 0.05f, 1f));
                GUI.Label(new Rect(r.x + 12, r.y, r.width - 330, r.height), $"Unsaved work found: '{bp.name}' (autosaved {File.GetLastWriteTime(path):ddd HH:mm}).", ImUi.Label);
                if (ImUi.Button(new Rect(r.xMax - 314, r.y + 5, 150, 30), "RESTORE", true))
                {
                    _bp = bp; _vi = 0; _dirty = true; BuildSession.Dirty = true; BuildSession.Variant = 0;
                    _recoverable = null; OpenEditor();
                    SetStatus("Restored from the autosave. SAVE to keep it.", 8f);
                    return y;
                }
                if (ImUi.Button(new Rect(r.xMax - 158, r.y + 5, 150, 30), "THROW AWAY")) { try { File.Delete(path); } catch { } _recoverable = null; }
                y += 46;
            }
            GUI.Label(new Rect(40, y + 2, 70, 30), "Find", ImUi.Label);
            _libSearch = ImUi.TextField(new Rect(96, y, 260, 32), _libSearch, "libsearch", 40);
            float x = 372;
            foreach (var t in AllTags())
            {
                float w = 24f + t.Length * 9f;
                if (x + w > ImUi.Width - 360) break;
                if (ImUi.Button(new Rect(x, y, w, 32), t, _libTag == t, "Show only rooms tagged this. Click again for all.")) _libTag = _libTag == t ? "" : t;
                x += w + 4;
            }
            if (ImUi.Button(new Rect(ImUi.Width - 340, y, 140, 32), "IMPORT…", false, "A room someone shared: from a share code you copied, or a .room.json file.")) { _modal = Modal.Import; _importFiles = null; }
            if (ImUi.Button(new Rect(ImUi.Width - 194, y, 154, 32), "SHARED FOLDER", false, "Where exported rooms go, and where to put rooms to import.")) { Directory.CreateDirectory(ShareDir); Application.OpenURL("file:///" + ShareDir.Replace('\\', '/')); }
            return y + 44;
        }

        static string ShareDir => Path.Combine(Application.persistentDataPath, "SharedRooms");

        /// <summary>A share code: the room's JSON, compressed and in base 64, with a short prefix.</summary>
        public static string ShareCode(RoomBlueprint bp)
        {
            var raw = Encoding.UTF8.GetBytes(JsonUtility.ToJson(bp));
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) gz.Write(raw, 0, raw.Length);
                return "SRROOM1:" + Convert.ToBase64String(ms.ToArray());
            }
        }

        public static RoomBlueprint FromShareCode(string code)
        {
            code = (code ?? "").Trim();
            int i = code.IndexOf("SRROOM1:", StringComparison.Ordinal);
            if (i < 0) return null;
            var data = Convert.FromBase64String(code.Substring(i + 8).Trim());
            using (var ms = new MemoryStream(data))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var rd = new StreamReader(gz, Encoding.UTF8))
                return RoomBlueprint.FromJson(rd.ReadToEnd());
        }

        void ExportRoom(RoomBlueprint bp)
        {
            try
            {
                Directory.CreateDirectory(ShareDir);
                string safe = string.Join("_", bp.name.Split(Path.GetInvalidFileNameChars())).Trim();
                if (safe.Length == 0) safe = "room";
                string path = Path.Combine(ShareDir, safe + ".room.json");
                File.WriteAllText(path, bp.ToJson());
                GUIUtility.systemCopyBuffer = ShareCode(bp);
                SetStatus($"Exported to {path}. Its share code is on the clipboard too: paste it to a friend.", 10f);
                BuilderSounds.Play(BuilderSounds.Sfx.Place);
            }
            catch (Exception e) { SetStatus("Could not export: " + e.Message, 8f); }
        }

        bool ImportRoom(RoomBlueprint bp, string from)
        {
            if (bp == null || bp.variants.Count == 0) { SetStatus("That is not a room."); BuilderSounds.Play(BuilderSounds.Sfx.Error); return false; }
            bp.id = RoomLibrary.NewId(); bp.enabled = false;
            if (RoomLibrary.All is IReadOnlyList<RoomBlueprint> all && new List<RoomBlueprint>(all).Exists(b => b.name == bp.name)) bp.name += " (imported)";
            RoomLibrary.Save(bp);
            ClearThumbnails();
            SetStatus($"Imported '{bp.name}' from {from}. It is in your library (not in the world until you put it there).", 8f);
            BuilderSounds.Play(BuilderSounds.Sfx.Grow);
            return true;
        }

        string[] _importFiles;

        void DrawImportDialog()
        {
            float w = Mathf.Min(720f, ImUi.Width - 40f), h = 520f;
            var box = new Rect((ImUi.Width - w) * 0.5f, (ImUi.Height - h) * 0.5f, w, h);
            ImUi.PanelBox(box);
            GUI.Label(new Rect(box.x + 24, box.y + 18, box.width - 48, 40), "IMPORT A ROOM", ImUi.Title);
            var col = new ImUi.Column(box.x + 24, box.y + 70, box.width - 48, 34, 8);
            ImUi.Dimmed(col.Row(40), "Copy a share code (it starts SRROOM1:) and paste it here, or put .room.json files in the shared folder and pick one.");
            if (ImUi.Button(col.Row(40), "PASTE SHARE CODE FROM CLIPBOARD", true))
            {
                try { if (ImportRoom(FromShareCode(GUIUtility.systemCopyBuffer), "the share code")) _modal = Modal.None; }
                catch (Exception e) { SetStatus("That share code did not read: " + e.Message, 8f); BuilderSounds.Play(BuilderSounds.Sfx.Error); }
            }
            col.Space(6);
            ImUi.Header(col.Row(24), "Files in the shared folder");
            if (_importFiles == null) { try { Directory.CreateDirectory(ShareDir); _importFiles = Directory.GetFiles(ShareDir, "*.json"); } catch { _importFiles = new string[0]; } }
            if (_importFiles.Length == 0) ImUi.Dimmed(col.Row(30), "None yet.");
            int shown = 0;
            foreach (var f in _importFiles)
            {
                if (++shown > 7) break;
                if (ImUi.Button(col.Row(30), Path.GetFileName(f)))
                {
                    try { if (ImportRoom(RoomBlueprint.FromJson(File.ReadAllText(f)), Path.GetFileName(f))) _modal = Modal.None; }
                    catch (Exception e) { SetStatus("Could not read it: " + e.Message, 8f); }
                }
            }
            if (ImUi.Button(new Rect(box.xMax - 24 - 140, box.yMax - 62, 140, 40), "CLOSE")) _modal = Modal.None;
        }

        // ---------------- Templates ----------------

        void DrawTemplateDialog()
        {
            var list = RoomTemplates.All;
            float w = Mathf.Min(900f, ImUi.Width - 40f), h = 120f + 66f * list.Count;
            var box = new Rect((ImUi.Width - w) * 0.5f, Mathf.Max(20f, (ImUi.Height - h) * 0.5f), w, h);
            ImUi.PanelBox(box);
            GUI.Label(new Rect(box.x + 24, box.y + 18, box.width - 48, 40), "START FROM…", ImUi.Title);
            for (int i = 0; i < list.Count; i++)
            {
                var r = new Rect(box.x + 24, box.y + 70 + i * 66, box.width - 48, 58);
                ImUi.Fill(r, ImUi.Raised);
                GUI.Label(new Rect(r.x + 12, r.y + 4, r.width - 200, 26), list[i].name, ImUi.Label);
                ImUi.Dimmed(new Rect(r.x + 12, r.y + 28, r.width - 200, 28), list[i].about);
                if (ImUi.Button(new Rect(r.xMax - 170, r.y + 10, 158, 38), "START", i == 0))
                {
                    _modal = Modal.None;
                    _bp = list[i].make();
                    _bp.id = RoomLibrary.NewId();
                    _vi = 0; _dirty = true; BuildSession.Variant = 0; BuildSession.Dirty = true;
                    OpenEditor();
                    return;
                }
            }
            if (ImUi.Button(new Rect(box.xMax - 24 - 140, box.yMax - 50, 140, 36), "CANCEL")) _modal = Modal.None;
        }
    }
}
