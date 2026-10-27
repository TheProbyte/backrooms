using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Backrooms.Interaction
{
    /// <summary>Leaves an item's field out of the item tuning menu (F2): live state, such as the paint or tape left, not a setting.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NotTunedAttribute : Attribute { }

    /// <summary>
    /// Item tuning (the F2 menu, <see cref="ItemTuningMenu"/>): each item type's settings, changed in game for every item of
    /// that type. The settings are the item class's own public fields (float, int and bool, not [NotTuned]) and its weight,
    /// found by reflection, so a new item shows up with no extra code; [Range] gives a slider's range and [Tooltip] its
    /// explanation. Values are kept per item type (its class name), applied as each item wakes (WorldItem.Awake) and to the
    /// items already out when changed. Saved as the items' defaults everywhere: in the editor into the project
    /// (Resources/ItemTuning.json, which builds ship with); in a build over that, on this machine.
    /// </summary>
    public static class ItemTuning
    {
        public enum Kind { Float, Int, Bool }

        public sealed class Setting
        {
            public string name, label, tip;
            public Kind kind;
            public bool ranged;                  // min and max came from [Range]
            public float min, max;
            public FieldInfo field;              // null: the weight (the Rigidbody's mass)
            public bool IsWeight => field == null;
        }

        [Serializable] class Value { public string name; public string value; }
        [Serializable] class Entry { public string type; public List<Value> values = new List<Value>(); }
        [Serializable] class Sheet { public List<Entry> items = new List<Entry>(); }

        const string WeightName = "mass";
        public const string ProjectAsset = "Assets/Backrooms/Resources/ItemTuning.json";
        static Dictionary<string, Dictionary<string, string>> s_values;                     // type -> setting -> value
        static readonly Dictionary<Type, List<Setting>> s_settings = new Dictionary<Type, List<Setting>>();
        static readonly Dictionary<string, Dictionary<string, float>> s_defaults = new Dictionary<string, Dictionary<string, float>>();
        static bool s_dirty;

        /// <summary>Something changed and is not saved yet.</summary>
        public static bool Dirty => s_dirty;
        /// <summary>For tests: read and write here instead (null: the usual places).</summary>
        public static string PathOverride;

        static string ProjectPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), ProjectAsset);
        static string LocalPath => Path.Combine(Application.persistentDataPath, "ItemTuning.json");
        static string WritePath => PathOverride ?? (Application.isEditor ? ProjectPath : LocalPath);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_values = null; s_dirty = false; }

        // ---------------- Settings of an item type ----------------

        /// <summary>The settings of an item type: its own public fields (the classes between it and WorldItem), then its weight.</summary>
        public static List<Setting> SettingsOf(Type t)
        {
            if (s_settings.TryGetValue(t, out var list)) return list;
            list = new List<Setting>();
            var chain = new List<Type>();
            for (var c = t; c != null && c != typeof(WorldItem) && c != typeof(MonoBehaviour); c = c.BaseType) chain.Insert(0, c);
            foreach (var c in chain)
                foreach (var f in c.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (f.IsInitOnly || f.IsLiteral || f.IsDefined(typeof(NotTunedAttribute), true) || f.IsDefined(typeof(NonSerializedAttribute), true)
                        || f.IsDefined(typeof(HideInInspector), true)) continue;
                    Kind k;
                    if (f.FieldType == typeof(float)) k = Kind.Float;
                    else if (f.FieldType == typeof(int)) k = Kind.Int;
                    else if (f.FieldType == typeof(bool)) k = Kind.Bool;
                    else continue;
                    var s = new Setting { name = f.Name, label = Nice(f.Name), field = f, kind = k };
                    var tip = f.GetCustomAttribute<TooltipAttribute>(true);
                    s.tip = tip != null ? tip.tooltip : "";
                    var range = f.GetCustomAttribute<RangeAttribute>(true);
                    if (range != null) { s.ranged = true; s.min = range.min; s.max = range.max; }
                    list.Add(s);
                }
            list.Add(new Setting
            {
                name = WeightName, label = "Weight", kind = Kind.Float, ranged = true, min = 0.02f, max = 10f,
                tip = "How heavy it is (kg): how it falls, tumbles and flies when thrown, and how much it slows you while you carry it.",
            });
            s_settings[t] = list;
            return list;
        }

        /// <summary>"breakRadius" -> "Break radius".</summary>
        public static string Nice(string field)
        {
            var sb = new System.Text.StringBuilder(field.Length + 4);
            for (int i = 0; i < field.Length; i++)
            {
                char ch = field[i];
                if (i == 0) { sb.Append(char.ToUpperInvariant(ch)); continue; }
                if (char.IsUpper(ch) && !char.IsUpper(field[i - 1])) { sb.Append(' '); sb.Append(char.ToLowerInvariant(ch)); }
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        // ---------------- Values ----------------

        public static float Get(WorldItem item, Setting s)
        {
            if (s.IsWeight) { var rb = item ? item.GetComponent<Rigidbody>() : null; return rb ? rb.mass : 0f; }
            object v = s.field.GetValue(item);
            switch (s.kind)
            {
                case Kind.Int: return (int)v;
                case Kind.Bool: return (bool)v ? 1f : 0f;
                default: return (float)v;
            }
        }

        static void SetOn(WorldItem item, Setting s, float v)
        {
            if (!item) return;
            if (s.IsWeight) { var rb = item.GetComponent<Rigidbody>(); if (rb) rb.mass = Mathf.Max(0.001f, v); return; }
            switch (s.kind)
            {
                case Kind.Int: s.field.SetValue(item, Mathf.RoundToInt(v)); break;
                case Kind.Bool: s.field.SetValue(item, v > 0.5f); break;
                default: s.field.SetValue(item, v); break;
            }
        }

        /// <summary>The value an item type's setting is tuned to; false when it is not tuned (it has its code default).</summary>
        public static bool TryTuned(Type t, string setting, out float value)
        {
            Ensure();
            value = 0f;
            return s_values.TryGetValue(t.Name, out var vals) && vals.TryGetValue(setting, out var text)
                   && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>The code default of a setting (from the first item of the type seen this session), or NaN if none has been.</summary>
        public static float Default(Type t, string setting)
            => s_defaults.TryGetValue(t.Name, out var d) && d.TryGetValue(setting, out var v) ? v : float.NaN;

        /// <summary>Applies the saved tuning to an item as it wakes (and notes the type's code defaults, from the first one).</summary>
        public static void Apply(WorldItem item)
        {
            if (!item) return;
            Ensure();
            var t = item.GetType();
            var settings = SettingsOf(t);
            if (!s_defaults.ContainsKey(t.Name))
            {
                var d = new Dictionary<string, float>();
                foreach (var s in settings) d[s.name] = Get(item, s);
                s_defaults[t.Name] = d;
            }
            if (!s_values.TryGetValue(t.Name, out var vals)) return;
            foreach (var s in settings)
                if (vals.TryGetValue(s.name, out var text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    SetOn(item, s, v);
        }

        /// <summary>Tunes a setting of an item type: kept (saved soon) and set on every item of the type already out.</summary>
        public static void Change(Type t, Setting s, float v)
        {
            Ensure();
            if (s.kind == Kind.Int) v = Mathf.Round(v);
            if (s.kind == Kind.Bool) v = v > 0.5f ? 1f : 0f;
            if (!s_values.TryGetValue(t.Name, out var vals)) s_values[t.Name] = vals = new Dictionary<string, string>();
            vals[s.name] = v.ToString("R", CultureInfo.InvariantCulture);
            foreach (var item in Object.FindObjectsByType<WorldItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (item.GetType() == t) SetOn(item, s, v);
            s_dirty = true;
        }

        /// <summary>Back to the code defaults for an item type (every setting), on the items out too.</summary>
        public static void Reset(Type t)
        {
            Ensure();
            s_values.Remove(t.Name);
            if (s_defaults.TryGetValue(t.Name, out var d))
                foreach (var item in Object.FindObjectsByType<WorldItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (item.GetType() == t)
                        foreach (var s in SettingsOf(t))
                            if (d.TryGetValue(s.name, out float v)) SetOn(item, s, v);
            s_dirty = true;
        }

        // ---------------- Saving ----------------

        static void Ensure() { if (s_values == null) Load(); }

        /// <summary>Reads the tuning: the project's (shipped in builds), then in a build this machine's over it.</summary>
        public static void Load()
        {
            s_values = new Dictionary<string, Dictionary<string, string>>();
            s_dirty = false;
            try
            {
                if (PathOverride != null) { if (File.Exists(PathOverride)) Merge(File.ReadAllText(PathOverride)); return; }
                if (Application.isEditor) { if (File.Exists(ProjectPath)) Merge(File.ReadAllText(ProjectPath)); return; }
                var shipped = Resources.Load<TextAsset>("ItemTuning");
                if (shipped) Merge(shipped.text);
                if (File.Exists(LocalPath)) Merge(File.ReadAllText(LocalPath));
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not read the item tuning: " + e.Message); }
        }

        static void Merge(string json)
        {
            var sheet = JsonUtility.FromJson<Sheet>(json);
            if (sheet?.items == null) return;
            foreach (var e in sheet.items)
            {
                if (string.IsNullOrEmpty(e.type) || e.values == null) continue;
                if (!s_values.TryGetValue(e.type, out var vals)) s_values[e.type] = vals = new Dictionary<string, string>();
                foreach (var v in e.values) if (!string.IsNullOrEmpty(v.name)) vals[v.name] = v.value;
            }
        }

        /// <summary>Writes the tuning (into the project in the editor, on this machine in a build).</summary>
        public static void Save()
        {
            Ensure();
            var sheet = new Sheet();
            var types = new List<string>(s_values.Keys);
            types.Sort(StringComparer.Ordinal);
            foreach (var type in types)
            {
                var vals = s_values[type];
                if (vals.Count == 0) continue;
                var e = new Entry { type = type };
                var names = new List<string>(vals.Keys);
                names.Sort(StringComparer.Ordinal);
                foreach (var n in names) e.values.Add(new Value { name = n, value = vals[n] });
                sheet.items.Add(e);
            }
            try
            {
                string path = WritePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(sheet, true));
                s_dirty = false;
#if UNITY_EDITOR
                if (PathOverride == null) UnityEditor.AssetDatabase.ImportAsset(ProjectAsset);
#endif
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not save the item tuning: " + e.Message); }
        }
    }
}
