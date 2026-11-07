using UnityEngine;

namespace Backrooms.StyleLab
{
    /// <summary>
    /// The player's look in the enclosed hazmat suit (2026-11-06), all of it their choice: the painted eyes and mouth,
    /// the nose, brows, hair, facial hair, glasses, a mask over the face, the skin and hair colours, the name on the chest
    /// tape and the crew number on the back. The painted eyes blink in Play mode.
    /// The suit model (HazmatSuit4, Tools/Blender/style_lab_characters6.py) carries every option under its Neck joint,
    /// each in a group named Prefix_Choice (Nose_Round, Brows_Flat, Hair_Short, FacialHair_Stubble, Mask_Full...), a
    /// Glasses group, the painted layers (Paint_Features, Paint_Shade) and markers for the text (NameTapeAnchor/Right/Up,
    /// NumberAnchor/Right/Up). This shows the chosen groups, swaps the skin and hair materials, picks the painted set
    /// (T_FacePaint_Atlas: three sets, open and blinking), keeps the face out of the shadows (the hood would leave it
    /// black) and builds the name and number as small meshes from the letter atlas (T_LabelFont), bent to the body.
    /// Works in the editor too; Apply() after changing fields from code.
    /// </summary>
    [ExecuteAlways]
    public class HazmatAppearance : MonoBehaviour
    {
        public enum EyeStyle { Tired, Wide, Calm }
        public enum NoseStyle { Round, Long, Button }
        public enum BrowStyle { Flat, Worried, Thick }
        public enum HairStyle { Short, Fringe, Receding, Buzz }
        public enum FacialHairStyle { None, Moustache, Stubble, Beard }
        public enum MaskStyle { None, Half, Full }

        [Header("Face")]
        public EyeStyle eyes = EyeStyle.Tired;
        public NoseStyle nose = NoseStyle.Round;
        public BrowStyle brows = BrowStyle.Flat;
        public HairStyle hair = HairStyle.Short;
        public FacialHairStyle facialHair = FacialHairStyle.Moustache;
        public bool glasses;
        public MaskStyle mask = MaskStyle.None;
        [Tooltip("An index into Skin Tones.")] public int skinTone;
        [Tooltip("An index into Hair Tones.")] public int hairTone;

        [Header("Suit")]
        public string playerName = "WORKER";
        public string crewNumber = "07";

        [Header("Expression")]
        [Tooltip("How loud they are talking (0..1, from voice chat when there is one): the mouth opens and closes with it.")]
        [Range(0f, 1f)] public float talk;
        [Tooltip("Frightened: wide, white-ringed eyes and an open mouth.")]
        public bool scared;
        [Tooltip("Frightened whenever the building's tension (TensionDirector) is past this (1 or more: never by itself).")]
        [Range(0f, 1.01f)] public float scaredFromTension = 0.75f;

        [Header("Blinking (Play mode)")]
        public bool blink = true;
        [Tooltip("Seconds between blinks, picked at random in this range.")] public Vector2 blinkInterval = new Vector2(2.5f, 6f);
        public float blinkLength = 0.12f;

        [Header("First person")]
        [Tooltip("Your own body seen from inside the hood: the face and its choices are drawn into the shadows only, and the " +
                 "name and number are not drawn (the trunk is hidden in first person).")]
        public bool firstPerson;

        [Header("Materials")]
        public Material[] skinTones;
        public Material[] hairTones;
        [Tooltip("Material for the name and number letters (the letter atlas, alpha clipped).")] public Material labelMaterial;

        const string LabelChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-.#' ";
        const int LabelCols = 8, LabelRows = 6;
        static readonly int IdBaseST = Shader.PropertyToID("_BaseMap_ST");

        MaterialPropertyBlock _mpb;
        float _nextBlink, _openAt;
        bool _closed;

        void OnEnable()
        {
            _seed = Random.value * 100f;
            Apply();
            ScheduleBlink(Time.time);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // building the labels creates objects, which Unity does not allow during OnValidate
            // (Not on the prefab file itself: Unity refuses to parent objects inside a prefab asset.)
            UnityEditor.EditorApplication.delayCall += () => { if (this && !UnityEditor.EditorUtility.IsPersistent(gameObject)) Apply(); };
        }
#endif

        public void Apply()
        {
            Show("Nose_", nose.ToString());
            Show("Brows_", brows.ToString());
            Show("Hair_", hair.ToString());
            Show("FacialHair_", facialHair.ToString());
            Show("Mask_", mask.ToString());
            var g = Find("Glasses");
            if (g) g.gameObject.SetActive(glasses);
            Material skin = Pick(skinTones, skinTone), hairMat = Pick(hairTones, hairTone);
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!InHead(r.transform)) continue;
                r.shadowCastingMode = firstPerson ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (skin && Contains(skinTones, r.sharedMaterial)) r.sharedMaterial = skin;
                else if (hairMat && Contains(hairTones, r.sharedMaterial)) r.sharedMaterial = hairMat;
            }
            SetFeatures();
            BuildLabel("NameTape", (playerName ?? "").ToUpperInvariant(), 0.018f, 0.1f, 0.4f, "NameText");
            BuildLabel("Number", crewNumber ?? "", 0.075f, 0.22f, 0.19f, "NumberText");
        }

        void Update()
        {
            if (!Application.isPlaying) return;
            float t = Time.time;
            bool changed = false;
            if (blink)
            {
                if (!_closed && t >= _nextBlink) { _closed = true; _openAt = t + blinkLength; changed = true; }
                else if (_closed && t >= _openAt) { _closed = false; ScheduleBlink(t); changed = true; }
            }
            // scared while the building is tense
            var td = Atmosphere.TensionDirector.Instance;
            bool fright = scared || (td && td.Tension >= scaredFromTension);
            if (fright != _fright) { _fright = fright; changed = true; }
            // the mouth flaps with the voice: open on the louder moments, a little noise so it is not a metronome
            bool mouth = talk > 0.12f && Mathf.PerlinNoise(t * 7f, _seed) < 0.35f + talk * 0.5f;
            if (mouth != _mouth) { _mouth = mouth; changed = true; }
            if (changed) SetFeatures();
        }

        bool _fright, _mouth;
        float _seed;

        void ScheduleBlink(float now)
        {
            // now and then a quick second blink
            _nextBlink = now + (Random.value < 0.15f ? 0.25f : Random.Range(blinkInterval.x, blinkInterval.y));
        }

        /// <summary>
        /// The painted eyes and mouth: the chosen set's frame (open, blinking, talking or scared), picked out of the atlas
        /// (three sets across, four frames down: open, blink, talk, scared).
        /// </summary>
        void SetFeatures()
        {
            var t = Find("Paint_Features");
            if (!t) return;
            var r = t.GetComponent<Renderer>();
            if (!r) return;
            int row = _closed ? 1 : (_fright || scared) ? 3 : _mouth ? 2 : 0;
            _mpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_mpb);
            _mpb.SetVector(IdBaseST, new Vector4(1f / 3f, 0.25f, (int)eyes / 3f, 1f - (row + 1) * 0.25f));
            r.SetPropertyBlock(_mpb);
        }

        /// <summary>A piece's name without the part suffix the rigged model gives it ("Nose_Round.Head" is "Nose_Round").</summary>
        static string Base(string name)
        {
            int dot = name.IndexOf('.');
            return dot < 0 ? name : name.Substring(0, dot);
        }

        void Show(string prefix, string choice)
        {
            string want = prefix + choice;
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                string n = Base(t.name);
                if (n.StartsWith(prefix)) t.gameObject.SetActive(n == want);
            }
        }

        Transform Find(string name)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == name || Base(t.name) == name) return t;
            return null;
        }

        static bool InHead(Transform t)
        {
            for (; t; t = t.parent)
            {
                var n = Base(t.name);
                if (n == "HeadParts" || n == "Glasses" || n == "Face" || n.StartsWith("Paint_") || n.StartsWith("Nose_") || n.StartsWith("Brows_") ||
                    n.StartsWith("Hair_") || n.StartsWith("FacialHair_") || n.StartsWith("Mask_")) return true;
                if (t.GetComponent<Renderer>() && t.name.EndsWith(".Head") && !t.name.StartsWith("Suit")) return true;
            }
            return false;
        }

        static Material Pick(Material[] list, int i) => list != null && list.Length > 0 ? list[Mathf.Clamp(i, 0, list.Length - 1)] : null;

        static bool Contains(Material[] list, Material m)
        {
            if (!m || list == null) return false;
            foreach (var x in list) if (x == m) return true;
            return false;
        }

        /// <summary>
        /// Letters as quads on a plane through the marker, facing out of the body, each pushed back by the body's curve
        /// (radius bendRadius) so the line follows it. Text reads left to right for someone facing the surface.
        /// </summary>
        void BuildLabel(string key, string text, float capHeight, float maxWidth, float bendRadius, string childName)
        {
            Transform a = Find(key + "Anchor"), u = Find(key + "Up");
            if (!a || !u) return;
            Transform parent = a.parent;
            Transform child = parent.Find(childName);
            if (!child)
            {
                var go = new GameObject(childName) { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>();
                child = go.transform;
            }
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;
            var mf = child.GetComponent<MeshFilter>();
            var mr = child.GetComponent<MeshRenderer>();
            mr.sharedMaterial = labelMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.enabled = !firstPerson;
            var mesh = mf.sharedMesh;
            if (!mesh) { mesh = new Mesh { name = childName, hideFlags = HideFlags.DontSave }; mf.sharedMesh = mesh; }
            mesh.Clear();
            if (string.IsNullOrEmpty(text) || !labelMaterial) return;

            // the frame in the parent's space: out of the body, up, and the reading direction
            Vector3 c = parent.InverseTransformPoint(a.position);
            Vector3 up = parent.InverseTransformDirection(u.position - a.position).normalized;
            Vector3 n = Vector3.Cross(parent.InverseTransformDirection(Find(key + "Right").position - a.position).normalized, up).normalized;
            if (Vector3.Dot(n, c) < 0f) n = -n;     // the markers sit outside the body, so out is away from the joint
            Vector3 right = Vector3.Cross(n, up).normalized;   // Unity is left-handed: this reads left to right from outside

            // DejaVu Sans Mono in 128 px cells drawn at 104 px: capitals ~76 px tall, an advance of ~62 px
            float cell = capHeight / (76f / 128f);
            float adv = cell * (62f / 128f);
            float width = adv * text.Length;
            if (width > maxWidth) { float k = maxWidth / width; cell *= k; adv *= k; width = maxWidth; }
            var verts = new System.Collections.Generic.List<Vector3>();
            var uvs = new System.Collections.Generic.List<Vector2>();
            var norms = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < text.Length; i++)
            {
                int idx = LabelChars.IndexOf(text[i]);
                if (idx < 0 || text[i] == ' ') continue;
                float x = -width / 2f + adv * (i + 0.5f);
                float sag = x * x / (2f * Mathf.Max(0.05f, bendRadius));
                Vector3 mid = c + right * x - n * sag;
                float hw = cell / 2f;
                int b = verts.Count;
                verts.Add(mid - right * hw - up * hw);   // bottom left
                verts.Add(mid + right * hw - up * hw);   // bottom right
                verts.Add(mid + right * hw + up * hw);   // top right
                verts.Add(mid - right * hw + up * hw);   // top left
                int col = idx % LabelCols, row = idx / LabelCols;
                float u0 = col / (float)LabelCols, u1 = (col + 1) / (float)LabelCols;
                float v1 = 1f - row / (float)LabelRows, v0 = 1f - (row + 1) / (float)LabelRows;
                uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u1, v0)); uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u0, v1));
                for (int k = 0; k < 4; k++) norms.Add(n);
                tris.Add(b + 3); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b + 3); tris.Add(b + 1); tris.Add(b);
            }
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
        }
    }
}
