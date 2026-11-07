using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A school / day backpack (2026-11-04; the owner: found fairly often, "set it down and open it", the things inside
    /// real things you see and take out by hand; models: Tools/Blender/build_backpack.py, an invented brand, NORREN).
    /// Carried in the hand by its grab loop. B puts the one in the hand on your back (one at a time), and B again with the
    /// hand empty takes it off into the hand. Set down (upright, or on its back), its zip's two pulls at the top are clicked
    /// to open it: they run down both sides and the front panel flops forward (onto the floor, or up and back if it lies on
    /// its back); a click on the panel zips it shut again. Open, a thing in the hand clicked on it goes in: it is put where
    /// it fits, lowest first, the way things settle in a bag (a height map over the bag's floor, every square way it can
    /// lie; too big: "WON'T FIT"); a thing inside is picked out with the empty hand like anything lying about, and what was
    /// on it settles. Nothing goes in or out while it is on your back or shut. Lamps, cords and the sledgehammer never go in.
    /// Shut, what is inside is switched off and carried with it (its weight with it); open, the bag sits where it is.
    /// Saved: its colour and whether it is open (Amount), worn or not, and everything in it where it lies in it
    /// (Saves.ItemData.bag). Found in offices and break rooms (loot from the chunk: DistrictGenerator.Finds), now and then
    /// with a thing or two in it.
    /// </summary>
    public class Backpack : WorldItem, IGripRound
    {
        // ---------------- The layout (Resources/Items/Backpack_Layout.json, Blender's frame) ----------------

        [System.Serializable]
        class LayoutJson { public float[] hinge, zip, inside_min, inside_max, grab; }

        static bool s_read;
        static Generation.ArtStyle.Look s_look;
        static Vector3 s_hinge = new Vector3(0f, 0f, 0.081f), s_grab = new Vector3(0f, 0.452f, -0.088f);
        static Vector3 s_inMin = new Vector3(-0.122f, 0.014f, -0.073f), s_inMax = new Vector3(0.122f, 0.36f, 0.066f);
        static Vector3[] s_zip;
        static float[] s_zipAt;      // distance along the zip to each point
        static float s_zipLen = 1f;
        public static readonly string[] Colours = { "Navy", "Red", "Forest" };

        static Vector3 U(float[] b, int i, Vector3 fallback) => b != null && b.Length >= i + 3 ? new Vector3(-b[i], b[i + 2], -b[i + 1]) : fallback;

        static void ReadLayout()
        {
            if (s_read && s_look == Generation.ArtStyle.Current) return;     // (read again for the other look's model)
            s_read = true;
            s_look = Generation.ArtStyle.Current;
            var t = Generation.ArtStyle.Load<TextAsset>("Items/Backpack_Layout");
            var j = t ? JsonUtility.FromJson<LayoutJson>(t.text) : null;
            if (j != null)
            {
                s_hinge = U(j.hinge, 0, s_hinge); s_grab = U(j.grab, 0, s_grab);
                var a = U(j.inside_min, 0, s_inMin); var b = U(j.inside_max, 0, s_inMax);
                s_inMin = Vector3.Min(a, b); s_inMax = Vector3.Max(a, b);
                if (j.zip != null && j.zip.Length >= 6)
                {
                    s_zip = new Vector3[j.zip.Length / 3];
                    for (int i = 0; i < s_zip.Length; i++) s_zip[i] = U(j.zip, i * 3, Vector3.zero);
                }
            }
            if (s_zip == null)
            {
                // (no layout: a plain arch over the front)
                s_zip = new Vector3[33];
                for (int i = 0; i < 33; i++)
                {
                    float a = Mathf.PI * i / 32f;
                    s_zip[i] = new Vector3(Mathf.Cos(a) * 0.14f, 0.2f + Mathf.Sin(a) * 0.22f, 0.06f);
                }
            }
            s_zipAt = new float[s_zip.Length];
            for (int i = 1; i < s_zip.Length; i++) s_zipAt[i] = s_zipAt[i - 1] + Vector3.Distance(s_zip[i - 1], s_zip[i]);
            s_zipLen = Mathf.Max(0.01f, s_zipAt[s_zip.Length - 1]);
        }

        /// <summary>A point on the zip, s from 0 (one foot) to 1 (the other), with its direction along it.</summary>
        static Vector3 ZipPoint(float s, out Vector3 along)
        {
            float d = Mathf.Clamp01(s) * s_zipLen;
            int i = 1;
            while (i < s_zip.Length - 1 && s_zipAt[i] < d) i++;
            float t = Mathf.InverseLerp(s_zipAt[i - 1], s_zipAt[i], d);
            along = (s_zip[i] - s_zip[i - 1]).normalized;
            return Vector3.Lerp(s_zip[i - 1], s_zip[i], t);
        }

        // ---------------- State ----------------

        int _colour;
        Transform _lid;
        readonly Transform[] _pulls = new Transform[2];
        GameObject _topZip;
        AudioSource _fx;
        bool _open;
        float _zipT;                 // 0: zipped shut (the pulls at the top), 1: open (the pulls at the feet)
        float _lidT;                 // 0: shut, 1: open to _lidOpen
        float _lidOpen = 100f;       // degrees about its hinge when open (it lies on the floor, or stands up and back)
        Coroutine _anim;

        /// <summary>Open (unzipped, the panel down): things go in and come out.</summary>
        public bool IsOpen => _open && _anim == null;
        /// <summary>Open, or opening (co-op: what another player's bag is set to).</summary>
        internal bool IsOpenOrOpening => _open;
        /// <summary>On someone's back (PropGrabber.WornBag).</summary>
        public bool Worn { get; private set; }
        public int Colour => _colour;
        protected override bool Pinned => _open || _anim != null || Worn;

        // ---------------- The item ----------------

        // Carried at your side by its grab loop, its side toward the way you walk.
        public override Vector3 HoldOffset => new Vector3(0.28f, -0.72f, 0.3f);
        public override Quaternion HoldTurn => Quaternion.Euler(0f, 90f, 0f);
        public override Vector3 GripLocal => s_grab + new Vector3(0f, -0.006f, 0f);
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(Vector3.forward, Vector3.up);
        public void GripAxis(out Vector3 point, out Vector3 axis) { point = s_grab; axis = Vector3.right; }
        public override float GripCurl => 0.95f;
        public override bool PlaceOnDrop => true;
        public override bool UsesAim => false;
        public override bool Pocketable => false;
        public override float Effort => Mathf.Clamp01(0.12f + (Body ? Body.mass : 1f) * 0.035f);
        public override Quaternion[] RestTurns => s_rests;
        static readonly Quaternion[] s_rests = { Quaternion.identity, Quaternion.Euler(-90f, 0f, 0f) };
        /// <summary>Its colour (0-2) and, in the half, whether it is open.</summary>
        public override float Amount
        {
            get => _colour + (_open ? 0.5f : 0f);
            set { Recolour(Mathf.Clamp(Mathf.FloorToInt(value + 0.01f), 0, Colours.Length - 1)); if (value - Mathf.Floor(value + 0.01f) > 0.25f) OpenNow(); }
        }
        public override string Status => $"backpack: {(_open ? "open" : "shut")}, {_in.Count} thing{(_in.Count == 1 ? "" : "s")} in it (B: put it on)";

        public override float ContactDistance(Vector3 p)
        {
            float body = BoxDistance(p, new Vector3(0f, 0.22f, -0.005f), new Vector3(0.15f, 0.22f, 0.085f));
            float grip = SegmentDistance(p, s_grab - Vector3.right * 0.028f, s_grab + Vector3.right * 0.028f) - 0.004f;
            return Mathf.Min(body, grip);
        }

        public static Backpack Create(Vector3 position, Quaternion rotation, int colour = 0)
        {
            colour = Mathf.Clamp(colour, 0, Colours.Length - 1);
            ReadLayout();
            var go = ItemModels.Body("Backpack", position, rotation, 0.7f, "Backpack_" + Colours[colour], new Vector3(0.3f, 0.02f, 0.15f), "Fabric_PackNavy", false,
                                     new Vector3(0f, 0.01f, -0.005f));
            var b = go.AddComponent<Backpack>();
            b.itemId = "backpack"; b.displayName = "Backpack";
            b._colour = colour;
            b.BuildParts();
            Generation.PoolLitSwitch.Attach(go);
            return b;
        }

        /// <summary>What is now and then left in a found one (weights): small things someone carried.</summary>
        static readonly (string id, int weight)[] Leftovers = { ("chalk", 30), ("tape", 25), ("spraycan", 15), ("nails", 12), ("cassette", 12) };

        /// <summary>
        /// A backpack found where the plan left one (OfficeChunkBuilder, PropKind.Backpack): its colour and whether it lies on
        /// its back from the variant, and from its dice now and then a thing or two in it (each loot of its own until the
        /// bag is taken or opened). It belongs to the chunk until then.
        /// </summary>
        public static Backpack MakeFound(Vector3 at, float yaw, int variant, ulong lootId, Transform parent)
        {
            bool lying = (variant & 4) != 0;
            var rot = Quaternion.Euler(0f, yaw, 0f) * (lying ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity);
            var pos = at + Vector3.up * (lying ? 0.095f : 0.004f) + (lying ? Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, -0.2f) : Vector3.zero);
            var bag = Create(pos, rot, (variant & 3) % Colours.Length);
            bag.lootId = lootId;
            var rng = new System.Random((variant >> 3) ^ 0x5EED);
            double roll = rng.NextDouble();
            int n = roll < 0.4 ? 1 : roll < 0.55 ? 2 : 0;
            int total = 0;
            foreach (var l in Leftovers) total += l.weight;
            for (int k = 0; k < n; k++)
            {
                int pick = rng.Next(total);
                string id = Leftovers[0].id;
                foreach (var l in Leftovers) { if (pick < l.weight) { id = l.id; break; } pick -= l.weight; }
                ulong inner = lootId ^ ((ulong)(k + 1) * 0xD1B54A32D192ED03ul);
                if (inner == 0) inner = 1;
                if (Saves.LootRegistry.IsTaken(inner)) continue;
                var it = ItemFactory.Spawn(id, pos + Vector3.up * 0.2f, rot);
                if (!it) continue;
                if (!bag.AdoptNew(it)) { Destroy(it.gameObject); continue; }
                it.lootId = inner;
            }
            if (parent) bag.transform.SetParent(parent, true);
            return bag;
        }

        protected override void Awake()
        {
            ReadLayout();
            base.Awake();
            Body.linearDamping = 0.15f; Body.angularDamping = 0.6f;
            // A bag's walls (hollow, so what is inside can be seen and taken when it is open): back, sides, top.
            Box(new Vector3(0f, 0.215f, -0.084f), new Vector3(0.3f, 0.41f, 0.018f));
            Box(new Vector3(-0.142f, 0.19f, -0.005f), new Vector3(0.02f, 0.36f, 0.15f));
            Box(new Vector3(0.142f, 0.19f, -0.005f), new Vector3(0.02f, 0.36f, 0.15f));
            Box(new Vector3(0f, 0.423f, -0.035f), new Vector3(0.22f, 0.025f, 0.1f));
            var src = new GameObject("Sound");
            src.transform.SetParent(transform, false);
            src.transform.localPosition = new Vector3(0f, 0.25f, 0.03f);
            _fx = ItemModels.Source(src, 9f);
            BackpackSounds.Make();
        }

        void Box(Vector3 c, Vector3 s) { var bc = gameObject.AddComponent<BoxCollider>(); bc.center = c; bc.size = s; }

        bool _built;
        void BuildParts()
        {
            if (_built) return;
            _built = true;
            string col = Colours[_colour];
            // The front panel on its hinge along the foot, with its own colliders (it is the bag's front when shut).
            var lid = new GameObject("Lid");
            lid.transform.SetParent(transform, false);
            lid.transform.localPosition = s_hinge;
            ItemModels.Attach(lid.transform, "BackpackLid_" + col, Vector3.one * 0.004f, "Fabric_Pack" + col);
            _lid = lid.transform;
            void LidBox(Vector3 c, Vector3 s) { var bc = lid.AddComponent<BoxCollider>(); bc.center = c; bc.size = s; }
            LidBox(new Vector3(0f, 0.11f, 0.004f), new Vector3(0.29f, 0.22f, 0.05f));
            LidBox(new Vector3(0f, 0.32f, -0.03f), new Vector3(0.25f, 0.2f, 0.045f));
            lid.AddComponent<BackpackZip>().bag = this;
            // The two sliders and their pulls, run along the zip.
            for (int i = 0; i < 2; i++)
            {
                var p = new GameObject("Pull" + i);
                p.transform.SetParent(transform, false);
                ItemModels.Attach(p.transform, "BackpackPull", Vector3.one * 0.003f, "Metal_PackSlider");
                _pulls[i] = p.transform;
            }
            // What a click opens it by: the pulls together at the top (a little bigger than them, standing proud).
            _topZip = new GameObject("ZipPulls");
            _topZip.transform.SetParent(transform, false);
            var top = ZipPoint(0.5f, out _);
            _topZip.transform.localPosition = top + new Vector3(0f, -0.012f, 0.022f);
            var tc = _topZip.AddComponent<BoxCollider>();
            tc.isTrigger = true; tc.size = new Vector3(0.1f, 0.07f, 0.07f);
            _topZip.AddComponent<BackpackZip>().bag = this;
            foreach (var r in GetComponentsInChildren<Renderer>()) if (!(r is ParticleSystemRenderer)) r.shadowCastingMode = ShadowCastingMode.Off;
            Pose();
        }

        void Start() { BuildParts(); }

        /// <summary>Its colour changed after it was made (a save being restored): its models again in that colour.</summary>
        void Recolour(int c)
        {
            if (c == _colour) return;
            _colour = c;
            if (!_built) return;
            string col = Colours[c];
            var old = transform.Find("Model");
            if (old) Destroy(old.gameObject);
            var m = ItemModels.Attach(transform, "Backpack_" + col, new Vector3(0.3f, 0.44f, 0.16f), "Fabric_Pack" + col);
            foreach (var r in m.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            if (_lid)
            {
                var lm = _lid.Find("Model");
                if (lm) Destroy(lm.gameObject);
                var l = ItemModels.Attach(_lid, "BackpackLid_" + col, Vector3.one * 0.004f, "Fabric_Pack" + col);
                foreach (var r in l.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        protected override void OnPickedUp()
        {
            OwnIt();
            if (_open || _anim != null) ShutNow();
        }

        void LateUpdate()
        {
            if (Worn) FollowBack();
        }

        // ---------------- Opening and shutting ----------------

        /// <summary>The empty hand clicks its pulls (open it) or its open panel (shut it).</summary>
        public bool ZipClicked(PropGrabber hand, bool onPanel)
        {
            if (Held || Worn || _anim != null) return false;
            if (!_open)
            {
                if (onPanel) return false;                       // (a click on the shut bag's front picks it up)
                if (transform.forward.y < -0.45f) { hand?.Say("IT'S LYING ON ITS FRONT"); return true; }
                if (Body && !Body.isKinematic && Body.linearVelocity.sqrMagnitude > 0.04f) return false;
                OwnIt();
                _anim = StartCoroutine(OpenRoutine());
                return true;
            }
            _anim = StartCoroutine(ShutRoutine());
            return true;
        }

        IEnumerator OpenRoutine()
        {
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = true;
            _open = true;
            _lidOpen = OpenAngle();
            Fx(BackpackSounds.Zip, 0.55f, 1f + Random.Range(-0.06f, 0.06f));
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.75f) { _zipT = Mathf.SmoothStep(0f, 1f, t); Pose(); yield return null; }
            _zipT = 1f;
            ShowContents(true);
            Settle(true);
            Fx(BackpackSounds.Flop, 0.5f, 1f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.32f) { _lidT = 1f - (1f - t) * (1f - t); Pose(); yield return null; }
            _lidT = 1f; Pose();
            _anim = null;
        }

        IEnumerator ShutRoutine()
        {
            Fx(BackpackSounds.Rustle, 0.35f, 1.1f);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.3f) { _lidT = 1f - Mathf.SmoothStep(0f, 1f, t); Pose(); yield return null; }
            _lidT = 0f;
            Fx(BackpackSounds.Zip, 0.5f, 1.08f + Random.Range(-0.05f, 0.05f));
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.65f) { _zipT = 1f - Mathf.SmoothStep(0f, 1f, t); Pose(); yield return null; }
            _zipT = 0f;
            _open = false;
            ShowContents(false);
            Pose();
            _anim = null;
            Body.isKinematic = false;
        }

        /// <summary>Open at once (a save being restored).</summary>
        internal void OpenNow()
        {
            BuildParts();
            _open = true; _zipT = 1f; _lidT = 1f;
            _lidOpen = OpenAngle();
            ShowContents(true);
            Pose();
        }

        /// <summary>Shut at once (picked up while open).</summary>
        internal void ShutNow()
        {
            if (_anim != null) { StopCoroutine(_anim); _anim = null; }
            if (_open) Fx(BackpackSounds.Zip, 0.4f, 1.25f);
            _open = false; _zipT = 0f; _lidT = 0f;
            ShowContents(false);
            Pose();
        }

        /// <summary>How far the panel opens: down until it meets the floor, or (lying on its back) up and over to 135.</summary>
        float OpenAngle()
        {
            float floor = transform.position.y;
            foreach (var c in GetComponents<Collider>()) floor = Mathf.Min(floor, c.bounds.min.y);
            for (float a = 60f; a <= 175f; a += 2.5f)
            {
                var r = transform.rotation * Quaternion.Euler(a, 0f, 0f);
                Vector3 hinge = transform.TransformPoint(s_hinge);
                Vector3 tip = hinge + r * new Vector3(0f, 0.42f, -0.02f);
                Vector3 mid = hinge + r * new Vector3(0f, 0.2f, 0.035f);
                if (Mathf.Min(tip.y, mid.y) <= floor + 0.02f) return a;
                if (a >= 135f && transform.forward.y > 0.5f) return 135f;
            }
            return 120f;
        }

        void Pose()
        {
            if (_lid) _lid.localRotation = Quaternion.Euler(_lidOpen * _lidT, 0f, 0f);
            // The pulls: together at the top, shut; at each foot of the zip, open (one each way).
            for (int i = 0; i < 2; i++)
            {
                if (!_pulls[i]) continue;
                float s = i == 0 ? Mathf.Lerp(0.497f, 0.02f, _zipT) : Mathf.Lerp(0.503f, 0.98f, _zipT);
                var p = ZipPoint(s, out var along);
                // Out of the zip a little (the front panel's way), hanging: its slider along the zip, its pull down.
                Vector3 outward = new Vector3(p.x, p.y - 0.2f, 0f).normalized * 0.004f + Vector3.forward * 0.003f;
                _pulls[i].localPosition = p + outward;
                Vector3 up = Vector3.Cross(along, Vector3.forward);
                if (up.y < 0f) up = -up;
                var world = transform.rotation * Quaternion.LookRotation(Vector3.forward, up);
                // the pull hangs: turned toward the world's down about the slider
                _pulls[i].rotation = Quaternion.Slerp(world, Quaternion.LookRotation(transform.forward, Vector3.up), 0.6f);
            }
            if (_topZip) _topZip.SetActive(!_open && _zipT < 0.01f);
        }

        void Fx(AudioClip c, float v, float pitch)
        {
            if (!c || !_fx) return;
            _fx.pitch = pitch;
            _fx.PlayOneShot(c, v);
        }

        /// <summary>Taken from its room (opened, picked up, filled): a free thing from now on, what is in it too.</summary>
        void OwnIt()
        {
            if (lootId != 0) { Saves.LootRegistry.Take(lootId); lootId = 0; transform.SetParent(null, true); }
            foreach (var p in _in)
                if (p.item && p.item.lootId != 0) { Saves.LootRegistry.Take(p.item.lootId); p.item.lootId = 0; }
        }

        // ---------------- What is inside ----------------

        class Packed
        {
            public WorldItem item;
            public Quaternion rot;
            public Vector3 ext;           // its size along the bag's axes, as it lies
            public Vector3 centre;        // its own bounds' middle (item frame)
            public int a, b, na, nb;      // its footprint on the bag's floor (cells)
            public float w;               // how high its underside is above the floor
        }

        readonly List<Packed> _in = new List<Packed>();
        /// <summary>The bag's frame axis that is "down" for the packing (0 x, 1 y, 2 z) and which way (-1 or +1).</summary>
        int _k = 1, _ks = -1;
        const float Cell = 0.01f;
        float[,] _h;
        int _ni, _nj;

        public int Count => _in.Count;
        public IEnumerable<WorldItem> Contents { get { foreach (var p in _in) if (p.item) yield return p.item; } }

        int AxisI => _k == 0 ? 1 : 0;
        int AxisJ => _k == 2 ? 1 : 2;

        void Grid()
        {
            Vector3 L = s_inMax - s_inMin;
            _ni = Mathf.Max(1, Mathf.FloorToInt(L[AxisI] / Cell + 0.01f));
            _nj = Mathf.Max(1, Mathf.FloorToInt(L[AxisJ] / Cell + 0.01f));
            _h = new float[_ni, _nj];
        }

        /// <summary>Which of its axes points most down now (not its front: it cannot be opened face down).</summary>
        void ChooseDown()
        {
            Vector3 down = transform.InverseTransformDirection(Vector3.down);
            int best = 1; float bestDot = -2f; int sign = -1;
            for (int k = 0; k < 3; k++)
                for (int s = -1; s <= 1; s += 2)
                {
                    if (k == 2 && s > 0) continue;
                    float d = down[k] * s;
                    if (d > bestDot) { bestDot = d; best = k; sign = s; }
                }
            _k = best; _ks = sign;
        }

        static readonly Dictionary<string, Bounds> s_bounds = new Dictionary<string, Bounds>();

        /// <summary>The thing's size in its own frame (its models, not its shadow quad or anything it trails).</summary>
        static Bounds LocalBounds(WorldItem it)
        {
            string key = it.itemId ?? it.name;
            if (s_bounds.TryGetValue(key, out var b)) return b;
            bool any = false;
            var inv = it.transform.worldToLocalMatrix;
            var meshes = new List<(Mesh mesh, Transform t)>();
            foreach (var mf in it.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh && !mf.name.Contains("Shadow")) meshes.Add((mf.sharedMesh, mf.transform));
            // (a skinned model, the rubber chicken: its mesh in its own frame)
            foreach (var sm in it.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (sm.sharedMesh) meshes.Add((sm.sharedMesh, sm.transform));
            foreach (var (mesh, tr) in meshes)
            {
                var mb = mesh.bounds;
                var m = inv * tr.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) != 0 ? 1 : -1, (c & 2) != 0 ? 1 : -1, (c & 4) != 0 ? 1 : -1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (!any) b = new Bounds(Vector3.zero, Vector3.one * 0.06f);
            s_bounds[key] = b;
            return b;
        }

        static List<Quaternion> s_turns;
        /// <summary>The 24 ways a thing can lie square in it (as it is first, then turned about the bag's up, then on its sides).</summary>
        static List<Quaternion> Turns()
        {
            if (s_turns != null) return s_turns;
            s_turns = new List<Quaternion>();
            var seen = new HashSet<string>();
            int[] order = { 0, 1, 3, 2 };
            foreach (int x in order) foreach (int z in order) foreach (int y in order)
            {
                var q = Quaternion.Euler(x * 90f, y * 90f, z * 90f);
                var m = Matrix4x4.Rotate(q);
                string key = "";
                for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) key += Mathf.RoundToInt(m[r, c]).ToString();
                if (seen.Add(key)) s_turns.Add(q);
            }
            return s_turns;
        }

        /// <summary>The empty hand or a bag of its own (lamps trail cords, a sledgehammer is too long): never goes in.</summary>
        public static bool Goes(WorldItem it) => it && !(it is Backpack) && !(it is PowerUser) && !(it is Plug) && !(it is Sledgehammer) && !(it is Boombox);

        /// <summary>Where a thing of this size would lie now (lowest first, toward the back), if anywhere.</summary>
        bool FindSpot(Bounds b, out Packed spot)
        {
            spot = null;
            if (_h == null) Grid();
            Vector3 L = s_inMax - s_inMin;
            int I = AxisI, J = AxisJ, K = _k;
            float best = float.MaxValue;
            var turns = Turns();
            var tried = new HashSet<Vector3Int>();
            for (int ti = 0; ti < turns.Count; ti++)
            {
                var q = turns[ti];
                var m = Matrix4x4.Rotate(q);
                Vector3 e = new Vector3(
                    Mathf.Abs(m[0, 0]) * b.size.x + Mathf.Abs(m[0, 1]) * b.size.y + Mathf.Abs(m[0, 2]) * b.size.z,
                    Mathf.Abs(m[1, 0]) * b.size.x + Mathf.Abs(m[1, 1]) * b.size.y + Mathf.Abs(m[1, 2]) * b.size.z,
                    Mathf.Abs(m[2, 0]) * b.size.x + Mathf.Abs(m[2, 1]) * b.size.y + Mathf.Abs(m[2, 2]) * b.size.z);
                var key = new Vector3Int(Mathf.RoundToInt(e.x * 1000f), Mathf.RoundToInt(e.y * 1000f), Mathf.RoundToInt(e.z * 1000f));
                if (!tried.Add(key)) continue;
                if (e[I] > L[I] + 0.002f || e[J] > L[J] + 0.002f || e[K] > L[K] + 0.002f) continue;
                int na = Mathf.Clamp(Mathf.CeilToInt(e[I] / Cell - 0.15f), 1, _ni), nb = Mathf.Clamp(Mathf.CeilToInt(e[J] / Cell - 0.15f), 1, _nj);
                for (int a = 0; a + na <= _ni; a++)
                    for (int bb = 0; bb + nb <= _nj; bb++)
                    {
                        float floor = 0f;
                        for (int x = a; x < a + na && floor + e[K] <= L[K] + 0.002f; x++)
                            for (int y = bb; y < bb + nb; y++) if (_h[x, y] > floor) floor = _h[x, y];
                        if (floor + e[K] > L[K] + 0.002f) continue;
                        // Lowest first, lying flat, toward the back and the middle; as it is turned now a little preferred.
                        float toBack = J == 2 ? bb / (float)_nj : I == 2 ? a / (float)_ni : 0f;
                        float mid = Mathf.Abs((a + na * 0.5f) / _ni - 0.5f);
                        float score = floor * 6f + e[K] * 1.2f + toBack * 0.03f + mid * 0.015f + ti * 0.0002f;
                        if (score < best)
                        {
                            best = score;
                            spot = new Packed { rot = q, ext = e, centre = b.center, a = a, b = bb, na = na, nb = nb, w = floor };
                        }
                    }
            }
            return spot != null;
        }

        /// <summary>The bag-frame pose of a packed thing.</summary>
        void PoseOf(Packed p, out Vector3 pos, out Quaternion rot)
        {
            int I = AxisI, J = AxisJ, K = _k;
            Vector3 c = Vector3.zero;
            c[I] = s_inMin[I] + p.a * Cell + p.ext[I] * 0.5f;
            c[J] = s_inMin[J] + p.b * Cell + p.ext[J] * 0.5f;
            c[K] = _ks < 0 ? s_inMin[K] + p.w + p.ext[K] * 0.5f : s_inMax[K] - p.w - p.ext[K] * 0.5f;
            rot = p.rot;
            pos = c - p.rot * p.centre;
        }

        void Mark(Packed p)
        {
            float top = p.w + p.ext[_k];
            for (int x = p.a; x < p.a + p.na; x++) for (int y = p.b; y < p.b + p.nb; y++) if (_h[x, y] < top) _h[x, y] = top;
        }

        /// <summary>A thing in the hand clicked on the open bag: into the hand's... into the bag where it fits.</summary>
        public bool TryPut(WorldItem item, PropGrabber hand)
        {
            if (!item || item == this) return false;
            if (!IsOpen) return false;
            if (!Goes(item)) { hand?.Say($"{item.displayName.ToUpperInvariant()} WON'T GO IN A BACKPACK"); return true; }
            if (!FindSpot(LocalBounds(item), out var spot)) { hand?.Say("IT WON'T FIT"); Fx(BackpackSounds.Rustle, 0.25f, 1.2f); return true; }
            OwnIt();
            hand?.LetGo(item);
            if (item.lootId != 0) { Saves.LootRegistry.Take(item.lootId); item.lootId = 0; }
            Insert(item, spot, true);
            return true;
        }

        void Insert(WorldItem item, Packed spot, bool animate)
        {
            spot.item = item;
            _in.Add(spot);
            Mark(spot);
            item.InBag = this;
            if (!item.Body.isKinematic) { item.Body.linearVelocity = Vector3.zero; item.Body.angularVelocity = Vector3.zero; }
            item.Body.isKinematic = true;
            item.Body.interpolation = RigidbodyInterpolation.None;
            PoseOf(spot, out var pos, out var rot);
            if (animate)
            {
                var fromPos = item.transform.position; var fromRot = item.transform.rotation;
                item.transform.SetParent(transform, true);
                StartCoroutine(Drop(item, fromPos, fromRot, pos, rot));
            }
            else
            {
                item.transform.SetParent(transform, false);
                item.transform.localPosition = pos; item.transform.localRotation = rot;
                item.Body.position = item.transform.position; item.Body.rotation = item.transform.rotation;
                item.gameObject.SetActive(_open);
            }
            UpdateMass();
        }

        IEnumerator Drop(WorldItem item, Vector3 fromPos, Quaternion fromRot, Vector3 pos, Quaternion rot)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.22f)
            {
                if (!item || item.InBag != this) yield break;
                float s = t * t;
                item.transform.position = Vector3.Lerp(fromPos, transform.TransformPoint(pos), s);
                item.transform.rotation = Quaternion.Slerp(fromRot, transform.rotation * rot, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            if (!item || item.InBag != this) yield break;
            item.transform.localPosition = pos; item.transform.localRotation = rot;
            item.Body.position = item.transform.position; item.Body.rotation = item.transform.rotation;
            Fx(BackpackSounds.Thud, Mathf.Clamp(0.25f + item.Body.mass * 0.4f, 0.25f, 0.8f), Random.Range(0.92f, 1.08f));
        }

        /// <summary>A thing in it was taken out by hand (WorldItem.SetHeld): what lay on it settles.</summary>
        public void Removed(WorldItem item)
        {
            int i = _in.FindIndex(p => p.item == item);
            if (i < 0) return;
            _in.RemoveAt(i);
            item.InBag = null;
            item.transform.SetParent(null, true);
            if (!item.gameObject.activeSelf) item.gameObject.SetActive(true);
            Fx(BackpackSounds.Rustle, 0.2f, 1.3f);
            Settle(false);
            UpdateMass();
        }

        /// <summary>
        /// Everything lies on what is under it again: lowest first, each down onto the height map. If the bag lies another
        /// way than when they were put in (it was shut, carried and set down on its back), they are put in again from the
        /// biggest; what no longer fits falls out in front of it.
        /// </summary>
        void Settle(bool reorient)
        {
            int k0 = _k, s0 = _ks;
            if (reorient) ChooseDown();
            Grid();
            if (reorient && (k0 != _k || s0 != _ks) && _in.Count > 0)
            {
                var all = new List<Packed>(_in);
                _in.Clear();
                all.Sort((x, y) => (y.ext.x * y.ext.y * y.ext.z).CompareTo(x.ext.x * x.ext.y * x.ext.z));
                foreach (var p in all)
                {
                    if (!p.item) continue;
                    if (FindSpot(LocalBounds(p.item), out var spot)) { spot.item = p.item; _in.Add(spot); Mark(spot); Place(spot); }
                    else Spill(p.item);
                }
                return;
            }
            _in.Sort((x, y) => x.w.CompareTo(y.w));
            foreach (var p in _in)
            {
                float floor = 0f;
                for (int x = p.a; x < p.a + p.na; x++) for (int y = p.b; y < p.b + p.nb; y++) if (_h[x, y] > floor) floor = _h[x, y];
                if (floor < p.w - 1e-4f) { p.w = floor; Place(p); }
                Mark(p);
            }
        }

        void Place(Packed p)
        {
            if (!p.item) return;
            PoseOf(p, out var pos, out var rot);
            p.item.transform.localPosition = pos; p.item.transform.localRotation = rot;
            p.item.Body.position = p.item.transform.position; p.item.Body.rotation = p.item.transform.rotation;
        }

        void Spill(WorldItem it)
        {
            it.InBag = null;
            it.transform.SetParent(null, true);
            it.gameObject.SetActive(true);
            it.transform.position = transform.TransformPoint(new Vector3(0f, 0.1f, 0.35f));
            it.Body.isKinematic = false;
        }

        void ShowContents(bool on)
        {
            foreach (var p in _in) if (p.item) p.item.gameObject.SetActive(on);
        }

        void UpdateMass()
        {
            float m = 0.7f;
            foreach (var p in _in) if (p.item && p.item.Body) m += p.item.Body.mass;
            Body.mass = m;
        }

        /// <summary>A thing back in it where it lay (a save being restored): local pose in the bag.</summary>
        public void AdoptSaved(WorldItem item, Vector3 localPos, Quaternion localRot)
        {
            if (!item) return;
            BuildParts();
            if (_h == null) { ChooseDown(); Grid(); }
            var b = LocalBounds(item);
            // Its footprint and height from where it lay (snapped to the cells).
            var m = Matrix4x4.Rotate(localRot);
            Vector3 e = new Vector3(
                Mathf.Abs(m[0, 0]) * b.size.x + Mathf.Abs(m[0, 1]) * b.size.y + Mathf.Abs(m[0, 2]) * b.size.z,
                Mathf.Abs(m[1, 0]) * b.size.x + Mathf.Abs(m[1, 1]) * b.size.y + Mathf.Abs(m[1, 2]) * b.size.z,
                Mathf.Abs(m[2, 0]) * b.size.x + Mathf.Abs(m[2, 1]) * b.size.y + Mathf.Abs(m[2, 2]) * b.size.z);
            Vector3 c = localPos + localRot * b.center;
            int I = AxisI, J = AxisJ, K = _k;
            var p = new Packed
            {
                rot = localRot, ext = e, centre = b.center,
                na = Mathf.Clamp(Mathf.CeilToInt(e[I] / Cell - 0.15f), 1, _ni), nb = Mathf.Clamp(Mathf.CeilToInt(e[J] / Cell - 0.15f), 1, _nj),
            };
            p.a = Mathf.Clamp(Mathf.RoundToInt((c[I] - e[I] * 0.5f - s_inMin[I]) / Cell), 0, _ni - p.na);
            p.b = Mathf.Clamp(Mathf.RoundToInt((c[J] - e[J] * 0.5f - s_inMin[J]) / Cell), 0, _nj - p.nb);
            p.w = Mathf.Max(0f, _ks < 0 ? c[K] - e[K] * 0.5f - s_inMin[K] : s_inMax[K] - c[K] - e[K] * 0.5f);
            if (item.lootId != 0 && lootId == 0) { Saves.LootRegistry.Take(item.lootId); item.lootId = 0; }
            p.item = item;
            _in.Add(p);
            Mark(p);
            item.InBag = this;
            item.Body.isKinematic = true;
            item.Body.interpolation = RigidbodyInterpolation.None;
            item.transform.SetParent(transform, false);
            item.transform.localPosition = localPos; item.transform.localRotation = localRot;
            item.gameObject.SetActive(_open);
            UpdateMass();
        }

        /// <summary>A new thing put in where it fits with no hand (loot made with it, the developer menu).</summary>
        public bool AdoptNew(WorldItem item)
        {
            if (!item || !Goes(item)) return false;
            BuildParts();
            if (_h == null) { ChooseDown(); Grid(); }
            if (!FindSpot(LocalBounds(item), out var spot)) return false;
            Insert(item, spot, false);
            return true;
        }

        // ---------------- On your back ----------------

        Transform _wearer;
        Camera _wearerEye;

        /// <summary>Onto someone's back (PropGrabber.WornBag): shut, switched to shadows only, carried behind them.</summary>
        public void PutOn(Transform wearer)
        {
            if (_open || _anim != null) ShutNow();
            OwnIt();
            Worn = true;
            _wearer = wearer;
            _wearerEye = wearer ? wearer.GetComponentInChildren<Camera>() : null;
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = true;
            Body.interpolation = RigidbodyInterpolation.None;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && r.GetComponent<ContactShadow>() == null && !r.name.Contains("Shadow"))
                    r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                else r.enabled = false;
            transform.SetParent(wearer, true);
            FollowBack();
            Fx(BackpackSounds.Rustle, 0.5f, 0.95f);
        }

        /// <summary>Off the back (into the hand, which takes it at once).</summary>
        public void TakeOff()
        {
            if (!Worn) return;
            Worn = false;
            _wearer = null;
            transform.SetParent(null, true);
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                r.enabled = true;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = true;
            foreach (var p in _in) if (p.item) p.item.gameObject.SetActive(false);
            Fx(BackpackSounds.Rustle, 0.5f, 1.05f);
        }

        /// <summary>Where it rides: its back against the wearer's, its top a hand under the shoulders (it goes down with a crouch).</summary>
        public static void BackPose(Transform wearer, Camera eye, out Vector3 pos, out Quaternion rot)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(wearer.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            float eyeY = eye ? eye.transform.position.y : wearer.position.y + 1.58f;
            pos = new Vector3(wearer.position.x, eyeY - 0.74f, wearer.position.z) - fwd * 0.24f;
            rot = Quaternion.LookRotation(-fwd, Vector3.up) * Quaternion.Euler(-7f, 0f, 0f);
        }

        void FollowBack()
        {
            if (!_wearer) return;
            BackPose(_wearer, _wearerEye, out var pos, out var rot);
            transform.SetPositionAndRotation(pos, rot);
        }
    }

    /// <summary>What a click on a backpack's pulls (shut) or its open front panel does: <see cref="Backpack.ZipClicked"/>.</summary>
    public class BackpackZip : MonoBehaviour, IHandUse
    {
        public Backpack bag;
        public bool OnPanel => transform != bag.transform && name == "Lid";
        public bool HandUse(PropGrabber hand) => bag && bag.ZipClicked(hand, OnPanel);
    }

    /// <summary>A backpack's sounds, made in code: the zip, the panel flopping open, the fabric, a thing landing in it.</summary>
    public static class BackpackSounds
    {
        public static AudioClip Zip, Flop, Rustle, Thud;
        const int Rate = 44100;

        static AudioClip Clip(string name, float[] d) { var c = AudioClip.Create(name, d.Length, 1, Rate, false); c.SetData(d, 0); return c; }

        public static void Make()
        {
            if (Zip) return;
            var r = new System.Random(9127);
            float N() => (float)(r.NextDouble() * 2.0 - 1.0);
            // A zip pulled: the slider over the coil's teeth (a fast, uneven run of ticks through the fabric), its pace
            // rising then easing, the tape's rasp under it.
            {
                int n = (int)(0.72f * Rate); var d = new float[n];
                float phase = 0f, bp1 = 0f, bp2 = 0f, lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)n;
                    float env = Mathf.SmoothStep(0f, 1f, t / 0.08f) * Mathf.SmoothStep(0f, 1f, (1f - t) / 0.12f);
                    float rate = 260f + 520f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.1f)) + 60f * N();
                    phase += rate / Rate;
                    float tick = 0f;
                    if (phase >= 1f) { phase -= 1f; tick = 0.9f + 0.3f * N(); }
                    lp += (tick - lp) * 0.55f;
                    float noise = N();
                    bp1 += (noise - bp1) * 0.35f; bp2 += (bp1 - bp2) * 0.35f;
                    float rasp = (bp1 - bp2) * 0.5f;
                    d[i] = (lp * 0.8f + rasp * 0.35f) * env * 0.8f;
                }
                Zip = Clip("PackZip", d);
            }
            // The front panel falling open: a soft fabric slap and the rustle of it settling.
            {
                int n = (int)(0.45f * Rate); var d = new float[n]; float lp = 0f, lp2 = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    lp += (N() - lp) * 0.08f; lp2 += (lp - lp2) * 0.2f;
                    float slap = lp2 * Mathf.Exp(-t * 28f) * 3.2f + Mathf.Sin(t * 2f * Mathf.PI * 95f) * Mathf.Exp(-t * 35f) * 0.25f;
                    float rus = (N() - lp) * 0.06f * Mathf.Exp(-t * 7f) * (0.5f + 0.5f * Mathf.Sin(t * 47f));
                    d[i] = slap + rus;
                }
                Flop = Clip("PackFlop", d);
            }
            // Fabric: a swish of nylon, rubbed (put on, taken off, shut).
            {
                int n = (int)(0.5f * Rate); var d = new float[n]; float hp = 0f, lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)n;
                    float x = N();
                    hp += (x - hp) * 0.25f;
                    lp += ((x - hp) - lp) * 0.5f;
                    float env = Mathf.Sin(Mathf.PI * t) * (0.6f + 0.4f * Mathf.Sin(t * 31f + Mathf.Sin(t * 9f) * 3f));
                    d[i] = lp * env * 0.35f;
                }
                Rustle = Clip("PackRustle", d);
            }
            // Something landing in it: a dull thump on fabric over the other things.
            {
                int n = (int)(0.2f * Rate); var d = new float[n]; float lp = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    lp += (N() - lp) * 0.06f;
                    d[i] = (Mathf.Sin(t * 2f * Mathf.PI * (140f - 260f * t)) * 0.6f + lp * 2.2f) * Mathf.Exp(-t * 32f);
                }
                Thud = Clip("PackThud", d);
            }
        }
    }
}
