using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Models the player brings in (round 18): .glb (binary glTF 2.0) and .obj (with its .mtl) files in
    /// <c>persistentDataPath/CustomModels</c>, read with a small reader of our own (no package): meshes, node transforms,
    /// base colours and colour textures. Each becomes a hidden template (its foot at its pivot, a box collider round it,
    /// the world's own Backrooms/Lit material with its colours) that build mode's catalog places as a static prop
    /// (<see cref="PropKind.Custom"/>, <see cref="Prop.model"/>). Loaded on the main thread before the world is built;
    /// the chunk builder's worker threads only look templates up.
    /// </summary>
    public static class CustomModels
    {
        static Dictionary<string, GameObject> s_models = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, string> s_errors = new Dictionary<string, string>();
        static Material s_template;
        static string s_dir;
        static bool s_loaded;

        public static string Dir { get { if (s_dir == null) s_dir = Path.Combine(Application.persistentDataPath, "CustomModels"); return s_dir; } }

        /// <summary>The loaded models' names (file names), sorted.</summary>
        public static List<string> Names { get { var l = new List<string>(s_models.Keys); l.Sort(StringComparer.OrdinalIgnoreCase); return l; } }
        public static IReadOnlyDictionary<string, string> Errors => s_errors;

        public static GameObject Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var m = s_models;
            return m.TryGetValue(name, out var g) ? g : null;
        }

        /// <summary>Loads the folder once (main thread). a: the biome's assets, whose wood material the models' copy.</summary>
        public static void Ensure(OfficeBiomeAssets a)
        {
            if (s_loaded) return;
            Reload(a);
        }

        public static void Reload(OfficeBiomeAssets a)
        {
            s_loaded = true;
            if (a && (a.wood || a.laminate)) s_template = a.wood ? a.wood : a.laminate;
            var next = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
            s_errors.Clear();
            try
            {
                Directory.CreateDirectory(Dir);
                foreach (var f in Directory.GetFiles(Dir))
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".glb" && ext != ".obj") continue;
                    string name = Path.GetFileName(f);
                    try
                    {
                        if (new FileInfo(f).Length > 96L * 1024 * 1024) { s_errors[name] = "over 96 MB"; continue; }
                        var parts = ext == ".glb" ? ReadGlb(f) : ReadObj(f);
                        var go = Template(name, parts);
                        if (go) next[name] = go; else s_errors[name] = "no meshes in it";
                    }
                    catch (Exception e) { s_errors[name] = e.Message; Debug.LogWarning($"[Backrooms] Could not load model {name}: {e.Message}"); }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Backrooms] Could not read the models folder: " + e.Message); }
            var old = s_models;
            s_models = next;
            foreach (var g in old.Values) if (g) { if (Application.isPlaying) UnityEngine.Object.Destroy(g); else UnityEngine.Object.DestroyImmediate(g); }
        }

        // ---------------- Templates ----------------

        sealed class Part
        {
            public Mesh mesh;
            public Matrix4x4 matrix = Matrix4x4.identity;
            public Color color = Color.white;
            public Texture2D texture;
        }

        static GameObject Template(string name, List<Part> parts)
        {
            if (parts.Count == 0) return null;
            var root = new GameObject("Model " + name);
            root.SetActive(false);
            root.hideFlags = HideFlags.HideAndDontSave;
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(root);
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            var mats = new Dictionary<(Color, Texture2D), Material>();
            Bounds b = default; bool first = true;
            foreach (var p in parts)
            {
                var go = new GameObject(p.mesh.name);
                go.transform.SetParent(body, false);
                go.transform.localPosition = p.matrix.GetColumn(3);
                go.transform.localRotation = p.matrix.rotation;
                go.transform.localScale = p.matrix.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = p.mesh;
                if (!mats.TryGetValue((p.color, p.texture), out var m))
                {
                    m = s_template ? new Material(s_template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    m.name = name + " material"; m.hideFlags = HideFlags.DontSave;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", p.color);
                    if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", p.texture); m.SetTextureScale("_BaseMap", Vector2.one); }
                    if (m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
                    if (m.HasProperty("_DetailAlbedoMap")) { m.SetTexture("_DetailAlbedoMap", null); m.SetTexture("_DetailNormalMap", null); }
                    mats[(p.color, p.texture)] = m;
                }
                go.AddComponent<MeshRenderer>().sharedMaterial = m;
                var mb = TransformBounds(go.transform.localToWorldMatrix, p.mesh.bounds);
                if (first) { b = mb; first = false; } else b.Encapsulate(mb);
            }
            // The foot at the pivot: its lowest point on the floor, its middle over the spot.
            body.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, b.size.y * 0.5f, 0f);
            box.size = Vector3.Max(b.size, Vector3.one * 0.02f);
            return root;
        }

        static Bounds TransformBounds(Matrix4x4 m, Bounds b)
        {
            var r = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                r.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return r;
        }

        static Mesh MakeMesh(string name, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tri)
        {
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            if (uv != null && uv.Count == v.Count) mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0, true);
            if (n != null && n.Count == v.Count) mesh.SetNormals(n); else mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            return mesh;
        }

        // ---------------- glTF binary ----------------

        static List<Part> ReadGlb(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67) throw new Exception("not a .glb file");
            int at = 12;
            string json = null; int binStart = -1;
            while (at + 8 <= bytes.Length)
            {
                int len = (int)BitConverter.ToUInt32(bytes, at), type = (int)BitConverter.ToUInt32(bytes, at + 4);
                if (type == 0x4E4F534A) json = Encoding.UTF8.GetString(bytes, at + 8, len);
                else if (type == 0x004E4942) binStart = at + 8;
                at += 8 + len;
            }
            if (json == null) throw new Exception("no glTF JSON");
            var root = (Dictionary<string, object>)MiniJson.Parse(json);
            var accessors = Arr(root, "accessors"); var views = Arr(root, "bufferViews");
            var meshes = Arr(root, "meshes"); var nodes = Arr(root, "nodes");
            var materials = Arr(root, "materials"); var textures = Arr(root, "textures"); var images = Arr(root, "images");
            var parts = new List<Part>();
            var texCache = new Dictionary<int, Texture2D>();

            byte[] View(int vi, out int start, out int stride)
            {
                var bv = (Dictionary<string, object>)views[vi];
                start = binStart + Int(bv, "byteOffset", 0); stride = Int(bv, "byteStride", 0);
                if (binStart < 0) throw new Exception("external buffers are not supported (use .glb)");
                return bytes;
            }
            float[] Floats(int ai, int comps)
            {
                var acc = (Dictionary<string, object>)accessors[ai];
                int count = Int(acc, "count", 0), ct = Int(acc, "componentType", 5126);
                var data = View(Int(acc, "bufferView", 0), out int start, out int stride);
                start += Int(acc, "byteOffset", 0);
                int size = ct == 5126 ? 4 : ct == 5123 || ct == 5122 ? 2 : 1;
                if (stride == 0) stride = size * comps;
                bool norm = acc.TryGetValue("normalized", out var nz) && nz is bool nb && nb;
                var r = new float[count * comps];
                for (int i = 0; i < count; i++)
                for (int c = 0; c < comps; c++)
                {
                    int o = start + i * stride + c * size;
                    r[i * comps + c] = ct == 5126 ? BitConverter.ToSingle(data, o)
                        : ct == 5123 ? (norm ? BitConverter.ToUInt16(data, o) / 65535f : BitConverter.ToUInt16(data, o))
                        : ct == 5121 ? (norm ? data[o] / 255f : data[o]) : BitConverter.ToInt16(data, o);
                }
                return r;
            }
            int[] Indices(int ai)
            {
                var acc = (Dictionary<string, object>)accessors[ai];
                int count = Int(acc, "count", 0), ct = Int(acc, "componentType", 5125);
                var data = View(Int(acc, "bufferView", 0), out int start, out _);
                start += Int(acc, "byteOffset", 0);
                var r = new int[count];
                for (int i = 0; i < count; i++)
                    r[i] = ct == 5125 ? (int)BitConverter.ToUInt32(data, start + i * 4) : ct == 5123 ? BitConverter.ToUInt16(data, start + i * 2) : data[start + i];
                return r;
            }
            Texture2D Tex(int ti)
            {
                if (texCache.TryGetValue(ti, out var t)) return t;
                t = null;
                if (textures != null && ti < textures.Count)
                {
                    int src = Int((Dictionary<string, object>)textures[ti], "source", -1);
                    if (src >= 0 && images != null && src < images.Count)
                    {
                        var img = (Dictionary<string, object>)images[src];
                        int bv = Int(img, "bufferView", -1);
                        if (bv >= 0)
                        {
                            var v = (Dictionary<string, object>)views[bv];
                            int len = Int(v, "byteLength", 0);
                            var data = new byte[len];
                            Buffer.BlockCopy(bytes, binStart + Int(v, "byteOffset", 0), data, 0, len);
                            t = new Texture2D(2, 2, TextureFormat.RGBA32, true) { hideFlags = HideFlags.DontSave, name = Path.GetFileName(path) + " tex" };
                            if (!t.LoadImage(data)) t = null;
                        }
                    }
                }
                texCache[ti] = t;
                return t;
            }

            void Visit(int ni, Matrix4x4 parent)
            {
                var node = (Dictionary<string, object>)nodes[ni];
                var local = Matrix4x4.identity;
                if (node.TryGetValue("matrix", out var mo) && mo is List<object> ml && ml.Count == 16)
                {
                    var m = new Matrix4x4();
                    for (int i = 0; i < 16; i++) m[i % 4, i / 4] = Convert.ToSingle(ml[i], CultureInfo.InvariantCulture);
                    var s = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
                    local = s * m * s;
                }
                else
                {
                    var tr = Vec(node, "translation", Vector3.zero); tr.x = -tr.x;
                    var q = node.TryGetValue("rotation", out var ro) && ro is List<object> rl && rl.Count == 4
                        ? new Quaternion(F(rl[0]), -F(rl[1]), -F(rl[2]), F(rl[3])) : Quaternion.identity;
                    local = Matrix4x4.TRS(tr, q, Vec(node, "scale", Vector3.one));
                }
                var world = parent * local;
                if (node.TryGetValue("mesh", out var mi) && meshes != null)
                {
                    var mesh = (Dictionary<string, object>)meshes[Convert.ToInt32(mi)];
                    foreach (Dictionary<string, object> prim in Arr(mesh, "primitives"))
                    {
                        if (Int(prim, "mode", 4) != 4) continue;
                        var attr = (Dictionary<string, object>)prim["attributes"];
                        if (!attr.ContainsKey("POSITION")) continue;
                        var pos = Floats(Convert.ToInt32(attr["POSITION"]), 3);
                        int n = pos.Length / 3;
                        var v = new List<Vector3>(n); for (int i = 0; i < n; i++) v.Add(new Vector3(-pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]));
                        List<Vector3> nr = null; List<Vector2> uv = null;
                        if (attr.ContainsKey("NORMAL")) { var a = Floats(Convert.ToInt32(attr["NORMAL"]), 3); nr = new List<Vector3>(n); for (int i = 0; i < n; i++) nr.Add(new Vector3(-a[i * 3], a[i * 3 + 1], a[i * 3 + 2])); }
                        if (attr.ContainsKey("TEXCOORD_0")) { var a = Floats(Convert.ToInt32(attr["TEXCOORD_0"]), 2); uv = new List<Vector2>(n); for (int i = 0; i < n; i++) uv.Add(new Vector2(a[i * 2], 1f - a[i * 2 + 1])); }
                        int[] idx = prim.ContainsKey("indices") ? Indices(Convert.ToInt32(prim["indices"])) : null;
                        var tri = new List<int>(idx != null ? idx.Length : n);
                        int tc = idx != null ? idx.Length : n;
                        for (int i = 0; i + 2 < tc; i += 3)
                        {
                            int a0 = idx != null ? idx[i] : i, a1 = idx != null ? idx[i + 1] : i + 1, a2 = idx != null ? idx[i + 2] : i + 2;
                            tri.Add(a0); tri.Add(a2); tri.Add(a1);              // the X flip turns the winding over
                        }
                        var part = new Part { mesh = MakeMesh(Path.GetFileNameWithoutExtension(path), v, nr, uv, tri), matrix = world };
                        int mat = Int(prim, "material", -1);
                        if (mat >= 0 && materials != null && mat < materials.Count)
                        {
                            var md = (Dictionary<string, object>)materials[mat];
                            if (md.TryGetValue("pbrMetallicRoughness", out var pbo) && pbo is Dictionary<string, object> pbr)
                            {
                                if (pbr.TryGetValue("baseColorFactor", out var cf) && cf is List<object> cl && cl.Count >= 3)
                                    part.color = new Color(F(cl[0]), F(cl[1]), F(cl[2]), cl.Count > 3 ? F(cl[3]) : 1f).gamma;
                                if (pbr.TryGetValue("baseColorTexture", out var bt) && bt is Dictionary<string, object> btd) part.texture = Tex(Int(btd, "index", -1));
                            }
                        }
                        parts.Add(part);
                    }
                }
                if (node.TryGetValue("children", out var ch) && ch is List<object> cl2)
                    foreach (var c in cl2) Visit(Convert.ToInt32(c), world);
            }

            var sceneNodes = new List<int>();
            var scenes = Arr(root, "scenes");
            if (scenes != null && scenes.Count > 0)
            {
                int si = Int(root, "scene", 0);
                foreach (var nn in Arr((Dictionary<string, object>)scenes[Mathf.Clamp(si, 0, scenes.Count - 1)], "nodes")) sceneNodes.Add(Convert.ToInt32(nn));
            }
            else if (nodes != null) for (int i = 0; i < nodes.Count; i++) sceneNodes.Add(i);
            foreach (int ni in sceneNodes) Visit(ni, Matrix4x4.identity);
            return parts;
        }

        static List<object> Arr(Dictionary<string, object> d, string key) => d != null && d.TryGetValue(key, out var o) ? o as List<object> : null;
        static int Int(Dictionary<string, object> d, string key, int def) => d.TryGetValue(key, out var o) && o != null ? Convert.ToInt32(o, CultureInfo.InvariantCulture) : def;
        static float F(object o) => Convert.ToSingle(o, CultureInfo.InvariantCulture);
        static Vector3 Vec(Dictionary<string, object> d, string key, Vector3 def) =>
            d.TryGetValue(key, out var o) && o is List<object> l && l.Count >= 3 ? new Vector3(F(l[0]), F(l[1]), F(l[2])) : def;

        // ---------------- OBJ ----------------

        static List<Part> ReadObj(string path)
        {
            var pos = new List<Vector3>(); var uvs = new List<Vector2>(); var nrm = new List<Vector3>();
            var groups = new Dictionary<string, (List<Vector3> v, List<Vector2> uv, List<Vector3> n, List<int> tri)>();
            var mtl = new Dictionary<string, (Color c, Texture2D t)>();
            string current = "";
            var ci = CultureInfo.InvariantCulture;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var t = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                switch (t[0])
                {
                    case "v": pos.Add(new Vector3(-float.Parse(t[1], ci), float.Parse(t[2], ci), float.Parse(t[3], ci))); break;
                    case "vt": uvs.Add(new Vector2(float.Parse(t[1], ci), t.Length > 2 ? float.Parse(t[2], ci) : 0f)); break;
                    case "vn": nrm.Add(new Vector3(-float.Parse(t[1], ci), float.Parse(t[2], ci), float.Parse(t[3], ci))); break;
                    case "usemtl": current = t.Length > 1 ? t[1] : ""; break;
                    case "mtllib": if (t.Length > 1) ReadMtl(Path.Combine(Path.GetDirectoryName(path), line.Substring(7).Trim()), mtl); break;
                    case "f":
                    {
                        if (!groups.TryGetValue(current, out var g)) groups[current] = g = (new List<Vector3>(), new List<Vector2>(), new List<Vector3>(), new List<int>());
                        int first = g.v.Count;
                        for (int k = 1; k < t.Length; k++)
                        {
                            var ix = t[k].Split('/');
                            int Ref(string s, int count) { int i = int.Parse(s, ci); return i < 0 ? count + i : i - 1; }
                            g.v.Add(pos[Ref(ix[0], pos.Count)]);
                            g.uv.Add(ix.Length > 1 && ix[1].Length > 0 && uvs.Count > 0 ? uvs[Ref(ix[1], uvs.Count)] : Vector2.zero);
                            g.n.Add(ix.Length > 2 && ix[2].Length > 0 && nrm.Count > 0 ? nrm[Ref(ix[2], nrm.Count)] : Vector3.zero);
                        }
                        for (int k = 1; k + 1 < t.Length - 1; k++) { g.tri.Add(first); g.tri.Add(first + k + 1); g.tri.Add(first + k); }
                        break;
                    }
                }
            }
            var parts = new List<Part>();
            foreach (var kv in groups)
            {
                bool hasN = !kv.Value.n.Exists(x => x == Vector3.zero);
                var part = new Part { mesh = MakeMesh(Path.GetFileNameWithoutExtension(path) + " " + kv.Key, kv.Value.v, hasN ? kv.Value.n : null, kv.Value.uv, kv.Value.tri) };
                if (mtl.TryGetValue(kv.Key, out var m)) { part.color = m.c; part.texture = m.t; }
                parts.Add(part);
            }
            return parts;
        }

        static void ReadMtl(string path, Dictionary<string, (Color c, Texture2D t)> into)
        {
            if (!File.Exists(path)) return;
            string name = null; var ci = CultureInfo.InvariantCulture;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                var t = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length < 2) continue;
                if (t[0] == "newmtl") { name = t[1]; into[name] = (Color.white, null); }
                else if (name != null && t[0] == "Kd" && t.Length >= 4) into[name] = (new Color(float.Parse(t[1], ci), float.Parse(t[2], ci), float.Parse(t[3], ci)).gamma, into[name].t);
                else if (name != null && t[0] == "map_Kd")
                {
                    string tp = Path.Combine(Path.GetDirectoryName(path), line.Substring(6).Trim());
                    if (!File.Exists(tp)) continue;
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { hideFlags = HideFlags.DontSave, name = Path.GetFileName(tp) };
                    if (tex.LoadImage(File.ReadAllBytes(tp))) into[name] = (into[name].c == Color.white ? Color.white : into[name].c, tex);
                }
            }
        }

        // ---------------- A small JSON reader ----------------

        static class MiniJson
        {
            public static object Parse(string s) { int i = 0; var v = Value(s, ref i); return v; }

            static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            static object Value(string s, ref int i)
            {
                Ws(s, ref i);
                if (i >= s.Length) throw new Exception("bad JSON");
                char c = s[i];
                if (c == '{')
                {
                    var d = new Dictionary<string, object>(); i++;
                    Ws(s, ref i);
                    if (s[i] == '}') { i++; return d; }
                    while (true)
                    {
                        Ws(s, ref i); var k = Str(s, ref i); Ws(s, ref i);
                        if (s[i] != ':') throw new Exception("bad JSON"); i++;
                        d[k] = Value(s, ref i); Ws(s, ref i);
                        if (s[i] == ',') { i++; continue; }
                        if (s[i] == '}') { i++; return d; }
                        throw new Exception("bad JSON");
                    }
                }
                if (c == '[')
                {
                    var l = new List<object>(); i++;
                    Ws(s, ref i);
                    if (s[i] == ']') { i++; return l; }
                    while (true)
                    {
                        l.Add(Value(s, ref i)); Ws(s, ref i);
                        if (s[i] == ',') { i++; continue; }
                        if (s[i] == ']') { i++; return l; }
                        throw new Exception("bad JSON");
                    }
                }
                if (c == '"') return Str(s, ref i);
                if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
                if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
                if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
                int st = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
            }

            static string Str(string s, ref int i)
            {
                if (s[i] != '"') throw new Exception("bad JSON");
                i++;
                var sb = new StringBuilder();
                while (s[i] != '"')
                {
                    if (s[i] == '\\')
                    {
                        i++;
                        char e = s[i];
                        if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                        else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e == 'b' ? '\b' : e == 'f' ? '\f' : e);
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                return sb.ToString();
            }
        }
    }
}
