using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Backrooms.UI
{
    /// <summary>
    /// The game's words in one place (Docs/UiPlan.md U18, 2026-11-10): the notices, titles, hints and notes the player
    /// reads, by key, in <c>Resources/Text/Words.csv</c>. Edit the wording there, not in the code. The file is in the
    /// layout Unity's Localization package imports as a string table (Key, Id, English(en)), so another language is a
    /// column away if one is ever wanted (the owner, 2026-11-10: "we'll worry about languages later").
    /// <para>Values are .NET format strings: {0}, {1} take the values given in order.</para>
    /// </summary>
    public static class Words
    {
        static Dictionary<string, string> _table;

        /// <summary>The words for a key, its values put in; a missing key shows as [key] so it is seen, never blank.</summary>
        public static string Get(string key, params object[] values)
        {
            if (_table == null) Load();
            if (!_table.TryGetValue(key, out var text)) return "[" + key + "]";
            if (values == null || values.Length == 0) return text;
            try { return string.Format(System.Globalization.CultureInfo.InvariantCulture, text, values); }
            catch (System.FormatException) { return text; }
        }

        public static bool Has(string key) { if (_table == null) Load(); return _table.ContainsKey(key); }
        public static IEnumerable<string> Keys { get { if (_table == null) Load(); return _table.Keys; } }

        static void Load()
        {
            _table = new Dictionary<string, string>();
            var asset = Resources.Load<TextAsset>("Text/Words");
            if (asset) Parse(asset.text, _table);
        }

        /// <summary>Reads the CSV (Key, Id, English(en); quoted fields may hold commas and doubled quotes).</summary>
        public static void Parse(string csv, Dictionary<string, string> into)
        {
            int keyCol = 0, textCol = 2;
            bool header = true;
            foreach (var row in Rows(csv))
            {
                if (row.Count == 0 || (row.Count == 1 && row[0].Length == 0)) continue;
                if (header)
                {
                    header = false;
                    for (int i = 0; i < row.Count; i++)
                    {
                        if (row[i].Trim() == "Key") keyCol = i;
                        if (row[i].Trim().StartsWith("English")) textCol = i;
                    }
                    continue;
                }
                if (row.Count <= Mathf.Max(keyCol, textCol)) continue;
                string key = row[keyCol].Trim();
                if (key.Length == 0 || key.StartsWith("#")) continue;
                into[key] = row[textCol];
            }
        }

        static IEnumerable<List<string>> Rows(string csv)
        {
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < csv.Length; i++)
            {
                char c = csv[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else field.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    row.Add(field.ToString()); field.Clear();
                    yield return row;
                    row = new List<string>();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); yield return row; }
        }
    }
}
