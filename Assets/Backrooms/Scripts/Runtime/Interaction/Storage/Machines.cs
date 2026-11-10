using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The floor's machines (2026-11-08, Docs/GameDesign.md section 8: power): generators, conveyor sections and floodlights,
    /// bought as kits at the supply counter and put up with the outline (KitPlacer). Each is a pinned moved prop.
    /// <list type="bullet">
    /// <item>Big machines take a chunky yellow industrial plug that only fits a generator: look at a conveyor or a
    ///   floodlight and click to run its cable to the nearest generator in reach (12 m), click again to unplug it.</item>
    /// <item>A generator runs a set load (<see cref="Capacity"/>: four conveyor sections, a floodlight counts two); a dial
    ///   on its front shows it. Plug in more and it stalls with a clunk, and everything on it stops, until something is
    ///   unplugged. That is the only cause of a power cut. It thumps and rattles while it runs.</item>
    /// <item>The power bill: each generator set up takes <see cref="BillPerGenerator"/> off the day's pay.</item>
    /// <item>A powered conveyor carries the boxes on its belt along its length (it wakes them and sets their speed along it).</item>
    /// <item>A powered floodlight lights a whole row of shelves (Shelves.Measure counts it as one of your own lights).</item>
    /// </list>
    /// Until their own models are made the machines are built of chunky blocks in the house colours.
    /// </summary>
    public class Machines : MonoBehaviour
    {
        public static Machines Instance { get; private set; }
        static WorldSaveData D => GameSession.Data;

        public const int Capacity = 4, BillPerGenerator = 12;
        public const float CableReach = 12f, BeltTop = 0.52f, BeltSpeed = 0.9f;

        /// <summary>The lights of the powered floodlights (Shelves counts them).</summary>
        public static readonly List<Light> Lights = new List<Light>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Lights.Clear(); }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static ulong Id(Kits.Kind k, int n) => 0xC4EA000000000000ul | ((ulong)(int)k << 32) | (uint)n;
        public static bool Is(ulong id) => (id >> 48) == 0xC4EAul;
        public static Kits.Kind KindOf(ulong id) => (Kits.Kind)(int)((id >> 32) & 0xFF);

        public static Vector3 Size(Kits.Kind k) =>
            k == Kits.Kind.Generator ? new Vector3(0.95f, 0.75f, 0.6f) : k == Kits.Kind.Conveyor ? new Vector3(0.62f, BeltTop, 1.2f) : new Vector3(0.7f, 2.0f, 0.7f);

        public static string Model(Kits.Kind k) => k == Kits.Kind.Generator ? "Generator" : k == Kits.Kind.Conveyor ? "ConveyorSection" : "Floodlight";

        static MovableKind PropKind(Kits.Kind k) => k == Kits.Kind.Generator ? MovableKind.Generator : k == Kits.Kind.Conveyor ? MovableKind.Conveyor : MovableKind.Floodlight;

        public static int LoadOf(Kits.Kind k) => k == Kits.Kind.Floodlight ? 2 : k == Kits.Kind.Conveyor ? 1 : 0;

        static List<ulong> ListOf(Kits.Kind k) => k == Kits.Kind.Generator ? D.generators : k == Kits.Kind.Conveyor ? D.conveyors : D.floodlights;

        // ---------------- Setting them up, taking them down ----------------

        public static ulong Add(Kits.Kind kind, Vector3 at, Quaternion rot)
        {
            var mgr = MovablePropManager.Instance;
            if (D == null || !mgr) return 0;
            ulong id = Net.WorkNet.Client ? Id(kind, ((Net.NetSession.LocalId + 1) << 24) | Random.Range(1, 1 << 24)) : Id(kind, D.nextKit++);
            ListOf(kind).Add(id);
            Net.WorkNet.Ask("machine+ " + id);
            Net.WorkNet.Changed();
            var a = WorldStreamer.Current ? WorldStreamer.Current.assets : null;
            string model = Model(kind);
            var spec = new MovableSpec
            {
                id = id, kind = PropKind(kind), renderMask = uint.MaxValue, proxyMaterial = a ? a.baseboard : null, position = at,
                yaw = rot.eulerAngles.y, size = Size(kind), pinned = true,
                fitting = model,                              // (blocks stand in if it is not loaded: MachineUnit)
            };
            mgr.Place(spec, at, rot);
            CamcorderOSD.Notice(kind == Kits.Kind.Generator ? "GENERATOR SET UP - ON THE POWER BILL" : Kits.Name(kind) + " SET UP - CLICK IT TO PLUG IN");
            return id;
        }

        /// <summary>Co-op, the host: a joined player set a machine up, or took one down.</summary>
        public static void Register(ulong id) { if (D != null && !ListOf(KindOf(id)).Contains(id)) ListOf(KindOf(id)).Add(id); }
        public static void Unregister(ulong id)
        {
            if (D == null) return;
            var kind = KindOf(id);
            ListOf(kind).Remove(id);
            if (kind == Kits.Kind.Generator) { for (int i = D.plugGenerator.Count - 1; i >= 0; i--) if (D.plugGenerator[i] == id) Unplug(D.plugMachine[i]); }
            else Unplug(id);
        }

        public static void Remove(ulong id)
        {
            if (D == null) return;
            var kind = KindOf(id);
            Net.WorkNet.Ask("machine- " + id);
            Net.WorkNet.Changed();
            ListOf(kind).Remove(id);
            if (kind == Kits.Kind.Generator) { for (int i = D.plugGenerator.Count - 1; i >= 0; i--) if (D.plugGenerator[i] == id) Unplug(D.plugMachine[i]); }
            else Unplug(id);
            MovablePropManager.Instance?.Forget(id);
        }

        /// <summary>The power bill for a day.</summary>
        public static int Bill() => D == null ? 0 : D.generators.Count * BillPerGenerator;

        // ---------------- Plugs ----------------

        public static ulong PluggedInto(ulong machine)
        {
            if (D == null) return 0;
            int i = D.plugMachine.IndexOf(machine);
            return i >= 0 && i < D.plugGenerator.Count ? D.plugGenerator[i] : 0;
        }

        public static int Load(ulong generator)
        {
            int n = 0;
            if (D == null) return 0;
            for (int i = 0; i < D.plugGenerator.Count && i < D.plugMachine.Count; i++)
                if (D.plugGenerator[i] == generator) n += LoadOf(KindOf(D.plugMachine[i]));
            return n;
        }

        public static bool Stalled(ulong generator) => Load(generator) > Capacity;

        /// <summary>Running: plugged into a generator that is set up and not stalled.</summary>
        public static bool Powered(ulong machine)
        {
            ulong g = PluggedInto(machine);
            return g != 0 && D.generators.Contains(g) && !Stalled(g);
        }

        static void Unplug(ulong machine)
        {
            int i = D.plugMachine.IndexOf(machine);
            if (i < 0) return;
            D.plugMachine.RemoveAt(i);
            if (i < D.plugGenerator.Count) D.plugGenerator.RemoveAt(i);
        }

        /// <summary>A click with empty hands (PropGrabber): on a conveyor or a floodlight, plug it into the nearest generator
        /// in reach, or unplug it. True if it was one.</summary>
        public static bool TryClick(Vector3 eye, Vector3 dir)
        {
            if (D == null || !Physics.Raycast(eye, dir, out var hit, 3.2f, ~0, QueryTriggerInteraction.Ignore)) return false;
            var mp = hit.collider.GetComponentInParent<MovableProp>();
            if (!mp || !Is(mp.Spec.id)) return false;
            ulong id = mp.Spec.id;
            var kind = KindOf(id);
            if (kind == Kits.Kind.Generator)
            {
                CamcorderOSD.Notice($"GENERATOR - LOAD {Load(id)} OF {Capacity}" + (Stalled(id) ? " - STALLED" : ""));
                return true;
            }
            if (PluggedInto(id) != 0)
            {
                ulong was = PluggedInto(id);
                Unplug(id);
                CamcorderOSD.Notice("UNPLUGGED");
                Audio.WorldSound.PlayAt(Audio.SoundBank.Tick(0), mp.transform.position, 0.5f, 8f, 0.8f);
                if (was != 0 && !Stalled(was)) MachineUnit.Find(was)?.Restart();
                return true;
            }
            ulong best = 0; float bd = CableReach;
            var mgr = MovablePropManager.Instance;
            foreach (var g in D.generators)
                if (mgr && mgr.Where(g, out var gp, out _))
                {
                    float d = Vector3.Distance(gp, mp.transform.position);
                    if (d < bd) { bd = d; best = g; }
                }
            if (best == 0) { CamcorderOSD.Notice("NO GENERATOR IN REACH - ITS CABLE IS 12 M"); return true; }
            bool wasStalled = Stalled(best);
            D.plugMachine.Add(id); D.plugGenerator.Add(best);
            if (Stalled(best) && !wasStalled)
            {
                CamcorderOSD.Notice("GENERATOR STALLED - TOO MUCH PLUGGED IN");
                MachineUnit.Find(best)?.Clunk();
            }
            else CamcorderOSD.Notice($"PLUGGED IN - LOAD {Load(best)} OF {Capacity}");
            return true;
        }

        /// <summary>A machine came into the world (MovableProp.Spawn): what it does, and its blocks if it has no model.</summary>
        public static void Attach(MovableProp p)
        {
            if (!p || !Is(p.Spec.id) || p.GetComponent<MachineUnit>()) return;
            p.gameObject.AddComponent<MachineUnit>().Init(p);
        }
    }

    /// <summary>One machine in the world: its look (its model, Tools/Blender/build_machines.py, or blocks if that is not
    /// loaded), its cable to the generator, the generator's sound and dial, the conveyor's belt, the floodlight's light.
    /// The moving and glowing parts are the game's on either: the generator's needle on its dial, the floodlight's lens.</summary>
    public class MachineUnit : MonoBehaviour
    {
        static readonly Dictionary<ulong, MachineUnit> s_live = new Dictionary<ulong, MachineUnit>();
        public static MachineUnit Find(ulong id) => s_live.TryGetValue(id, out var m) && m ? m : null;

        MovableProp _prop;
        Kits.Kind _kind;
        LineRenderer _cable;
        Transform _needle;
        Light _light;
        AudioSource _hum;
        Renderer _lens;
        static readonly Collider[] s_over = new Collider[24];
        static Material s_body, s_frame, s_dark, s_yellow, s_cream, s_lensOn, s_lensOff, s_cableMat;
        static AudioClip s_engine;

        public void Init(MovableProp p)
        {
            _prop = p;
            _kind = Machines.KindOf(p.Spec.id);
            s_live[p.Spec.id] = this;
            Materials();
            bool modelled = !string.IsNullOrEmpty(p.Spec.fitting) && FittingModels.Ready && FittingModels.Get(p.Spec.fitting) != null;
            if (modelled) Moving(); else Blocks();
            if (_kind == Kits.Kind.Generator)
            {
                _hum = ItemModels.Source(gameObject, 22f);
                _hum.clip = Engine(); _hum.loop = true; _hum.volume = 0.5f;
                Audio.SoundRoute.Add(_hum);
                _hum.Play();
            }
            if (_kind == Kits.Kind.Floodlight)
            {
                var lg = new GameObject("Light");
                lg.transform.SetParent(transform, false);
                lg.transform.localPosition = new Vector3(0f, p.Spec.size.y - 0.16f, 0.16f);
                lg.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);
                _light = lg.AddComponent<Light>();
                _light.type = LightType.Spot; _light.spotAngle = 110f; _light.innerSpotAngle = 60f;
                _light.range = 16f; _light.intensity = 0f; _light.color = new Color(1f, 0.95f, 0.86f);
                _light.shadows = LightShadows.None; _light.renderingLayerMask = -1;      // (FloodShadows decides)
            }
            if (_kind != Kits.Kind.Generator)
            {
                var cg = new GameObject("Cable");
                cg.transform.SetParent(transform, false);
                _cable = cg.AddComponent<LineRenderer>();
                _cable.widthMultiplier = 0.025f; _cable.positionCount = 0; _cable.useWorldSpace = true;
                _cable.sharedMaterial = s_cableMat; _cable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        void OnDestroy()
        {
            if (_prop && s_live.TryGetValue(_prop.Spec.id, out var me) && me == this) s_live.Remove(_prop.Spec.id);
            if (_light) Machines.Lights.Remove(_light);
        }

        public void Clunk() { Audio.WorldSound.PlayAt(Audio.SoundBank.Tick(0), transform.position, 1f, 20f, 0.35f); }
        public void Restart() { Audio.WorldSound.PlayAt(Audio.SoundBank.Tick(0), transform.position, 0.8f, 20f, 0.5f); }

        void Update()
        {
            if (!_prop || GameSession.Data == null) return;
            ulong id = _prop.Spec.id;
            if (_kind == Kits.Kind.Generator)
            {
                bool stalled = Machines.Stalled(id);
                if (_hum) { _hum.pitch = stalled ? 0.4f : 1f; _hum.volume = stalled ? 0f : 0.5f; }
                if (_needle) _needle.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(70f, -70f, Mathf.Clamp01(Machines.Load(id) / (float)Machines.Capacity)) + (stalled ? 0f : Mathf.Sin(Time.time * 31f) * 2f));
                return;
            }
            bool on = Machines.Powered(id);
            if (_light) FloodShadows();
            if (_light)
            {
                _light.intensity = Mathf.MoveTowards(_light.intensity, on ? 18f : 0f, Time.deltaTime * 40f);
                bool lit = _light.intensity > 0.01f;
                if (lit && !Machines.Lights.Contains(_light)) Machines.Lights.Add(_light);
                if (!lit) Machines.Lights.Remove(_light);
                if (_lens) _lens.sharedMaterial = lit ? s_lensOn : s_lensOff;
            }
            Cable();
        }

        // ---------------- Floodlight shadows (2026-11-08 profile: about 0.35 ms of GPU each, at any distance) ----------------

        static readonly List<MachineUnit> s_floods = new List<MachineUnit>();
        static int s_floodFrame = -1;
        const int ShadowedFloods = 2;
        const float FloodShadowReach = 14f;

        /// <summary>Once a frame for them all: only the two lit floodlights nearest the camera within 14 m cast shadows
        /// (soft up close, hard further off), as the lamps keep theirs to within 14-20 m.</summary>
        void FloodShadows()
        {
            if (s_floodFrame == Time.frameCount) return;
            s_floodFrame = Time.frameCount;
            var cam = Camera.main;
            s_floods.Clear();
            foreach (var m in s_live.Values) if (m && m._light && m._light.intensity > 0.01f) s_floods.Add(m);
            if (!cam) { foreach (var m in s_floods) m._light.shadows = LightShadows.None; return; }
            var eye = cam.transform.position;
            s_floods.Sort((a, b) => (a.transform.position - eye).sqrMagnitude.CompareTo((b.transform.position - eye).sqrMagnitude));
            for (int i = 0; i < s_floods.Count; i++)
            {
                float d2 = (s_floods[i].transform.position - eye).sqrMagnitude;
                var want = i >= ShadowedFloods || d2 > FloodShadowReach * FloodShadowReach ? LightShadows.None : d2 < 36f ? LightShadows.Soft : LightShadows.Hard;
                if (s_floods[i]._light.shadows != want) s_floods[i]._light.shadows = want;
            }
        }

        void Cable()
        {
            if (!_cable) return;
            ulong g = Machines.PluggedInto(_prop.Spec.id);
            var mgr = MovablePropManager.Instance;
            if (g == 0 || !mgr || !mgr.Pose(g, out var gp, out var gr)) { _cable.positionCount = 0; return; }
            // (Where it plugs in on the model: the floodlight's switch box on its back leg, the conveyor's at its back end.)
            var a = transform.TransformPoint(_kind == Kits.Kind.Floodlight ? new Vector3(0f, 0.26f, -0.23f) : new Vector3(0f, 0.25f, -_prop.Spec.size.z * 0.5f + 0.05f));
            var b = gp + gr * new Vector3(0.3f, 0.45f, 0.31f);
            const int n = 14;
            _cable.positionCount = n;
            float sag = Mathf.Min(0.6f, Vector3.Distance(a, b) * 0.08f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                var p = Vector3.Lerp(a, b, t);
                // Sagging, and lying along the floor where it would reach it.
                p.y = Mathf.Max(transform.position.y + 0.02f, p.y - sag * 4f * t * (1f - t) - 0.3f * Mathf.Sin(Mathf.PI * t));
                _cable.SetPosition(i, p);
            }
        }

        void FixedUpdate()
        {
            if (_kind != Kits.Kind.Conveyor || !_prop || GameSession.Data == null || !Machines.Powered(_prop.Spec.id)) return;
            // The belt: what rests on it is carried along it (+Z).
            var size = _prop.Spec.size;
            var centre = transform.TransformPoint(new Vector3(0f, Machines.BeltTop + 0.2f, 0f));
            int n = Physics.OverlapBoxNonAlloc(centre, new Vector3(size.x * 0.5f - 0.02f, 0.2f, size.z * 0.5f), s_over, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            var along = transform.forward;
            for (int i = 0; i < n; i++)
            {
                var mp = s_over[i] ? s_over[i].GetComponentInParent<MovableProp>() : null;
                if (!mp || mp == _prop || mp.held || mp.Pinned || mp.Spec.kind != MovableKind.Box) continue;
                if (mp.Body.isKinematic) mp.Wake();
                if (mp.Body.isKinematic) continue;
                var v = mp.Body.linearVelocity;
                var flat = along * Machines.BeltSpeed;
                // Toward the belt's middle line too, so boxes stay on.
                var side = Vector3.Dot(mp.transform.position - transform.position, transform.right);
                flat -= transform.right * side * 1.5f;
                mp.Body.linearVelocity = new Vector3(flat.x, v.y, flat.z);
            }
        }

        // ---------------- Their look until they have models ----------------

        static void Materials()
        {
            if (s_body) return;
            var sh = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            Material M(string n, Color c, float smooth = 0.35f)
            {
                var m = new Material(sh) { name = n, hideFlags = HideFlags.DontSave };
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth);
                return m;
            }
            s_body = M("Machine_Amber", new Color(0.94f, 0.64f, 0.12f));
            s_frame = M("Machine_Avocado", new Color(0.49f, 0.54f, 0.18f));
            s_dark = M("Machine_Dark", new Color(0.17f, 0.14f, 0.12f), 0.2f);
            s_yellow = M("Machine_PlugYellow", new Color(0.98f, 0.8f, 0.1f), 0.5f);
            s_cream = M("Machine_Cream", new Color(0.95f, 0.9f, 0.78f), 0.4f);
            s_lensOff = M("Machine_LensOff", new Color(0.55f, 0.55f, 0.5f), 0.8f);
            s_lensOn = M("Machine_LensOn", new Color(1f, 0.97f, 0.9f), 0.8f);
            s_lensOn.EnableKeyword("_EMISSION"); s_lensOn.SetColor("_EmissionColor", new Color(6f, 5.6f, 5f));
            s_cableMat = M("Machine_Cable", new Color(0.98f, 0.78f, 0.08f), 0.3f);
        }

        Transform Block(string name, Vector3 at, Vector3 size, Material m, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent ? parent : transform, false);
            go.transform.localPosition = at; go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.renderingLayerMask = uint.MaxValue;
            return go.transform;
        }

        /// <summary>On the model: the generator's needle on its dial, the floodlight's lens in its bezel (lit when it is on).</summary>
        void Moving()
        {
            var s = _prop.Spec.size;
            if (_kind == Kits.Kind.Generator)
            {
                _needle = new GameObject("Needle").transform;
                _needle.SetParent(transform, false);
                _needle.localPosition = new Vector3(0.08f, 0.41f, s.z * 0.5f - 0.004f);
                Block("NeedleBar", new Vector3(0f, 0.05f, 0f), new Vector3(0.01f, 0.1f, 0.005f), s_dark, _needle);
            }
            else if (_kind == Kits.Kind.Floodlight)
                _lens = Block("Lens", new Vector3(0f, s.y - 0.16f, 0.135f), new Vector3(0.42f, 0.24f, 0.01f), s_lensOff).GetComponent<Renderer>();
        }

        void Blocks()
        {
            var s = _prop.Spec.size;
            switch (_kind)
            {
                case Kits.Kind.Generator:
                    Block("Skid", new Vector3(0f, 0.05f, 0f), new Vector3(s.x, 0.1f, s.z), s_dark);
                    Block("Body", new Vector3(0f, 0.38f, 0f), new Vector3(s.x - 0.1f, 0.56f, s.z - 0.08f), s_body);
                    Block("Tank", new Vector3(-0.12f, 0.7f, 0f), new Vector3(0.5f, 0.1f, 0.36f), s_frame);
                    Block("Panel", new Vector3(0.18f, 0.42f, s.z * 0.5f - 0.03f), new Vector3(0.42f, 0.3f, 0.03f), s_dark);
                    Block("Dial", new Vector3(0.08f, 0.45f, s.z * 0.5f - 0.012f), new Vector3(0.14f, 0.14f, 0.01f), s_cream);
                    _needle = new GameObject("Needle").transform;
                    _needle.SetParent(transform, false);
                    _needle.localPosition = new Vector3(0.08f, 0.41f, s.z * 0.5f - 0.004f);
                    Block("NeedleBar", new Vector3(0f, 0.05f, 0f), new Vector3(0.012f, 0.1f, 0.006f), s_dark, _needle);
                    Block("Socket", new Vector3(0.3f, 0.45f, s.z * 0.5f - 0.01f), new Vector3(0.1f, 0.1f, 0.03f), s_yellow);
                    for (int k = -1; k <= 1; k += 2) Block("Wheel", new Vector3(k * (s.x * 0.5f - 0.08f), 0.08f, -s.z * 0.5f + 0.02f), new Vector3(0.06f, 0.16f, 0.16f), s_dark);
                    break;
                case Kits.Kind.Conveyor:
                    Block("Belt", new Vector3(0f, Machines.BeltTop - 0.02f, 0f), new Vector3(s.x - 0.1f, 0.04f, s.z), s_dark);
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Block("Rail", new Vector3(k * (s.x * 0.5f - 0.03f), Machines.BeltTop - 0.04f, 0f), new Vector3(0.06f, 0.14f, s.z), s_frame);
                        for (int e = -1; e <= 1; e += 2)
                            Block("Leg", new Vector3(k * (s.x * 0.5f - 0.05f), (Machines.BeltTop - 0.1f) * 0.5f, e * (s.z * 0.5f - 0.08f)), new Vector3(0.05f, Machines.BeltTop - 0.1f, 0.05f), s_dark);
                    }
                    Block("Plug", new Vector3(0f, 0.25f, -s.z * 0.5f + 0.02f), new Vector3(0.08f, 0.08f, 0.04f), s_yellow);
                    break;
                default:
                    Block("Base", new Vector3(0f, 0.04f, 0f), new Vector3(s.x, 0.08f, 0.08f), s_dark);
                    Block("Base2", new Vector3(0f, 0.04f, 0f), new Vector3(0.08f, 0.08f, s.z), s_dark);
                    Block("Pole", new Vector3(0f, s.y * 0.5f, 0f), new Vector3(0.07f, s.y - 0.2f, 0.07f), s_frame);
                    Block("Head", new Vector3(0f, s.y - 0.16f, 0f), new Vector3(0.5f, 0.32f, 0.26f), s_body);
                    _lens = Block("Lens", new Vector3(0f, s.y - 0.16f, 0.135f), new Vector3(0.42f, 0.24f, 0.01f), s_lensOff).GetComponent<Renderer>();
                    Block("Plug", new Vector3(0f, 0.25f, -0.05f), new Vector3(0.08f, 0.08f, 0.04f), s_yellow);
                    break;
            }
        }

        /// <summary>A small engine: a 25 Hz thump with a rattle on top, looped.</summary>
        static AudioClip Engine()
        {
            if (s_engine) return s_engine;
            int sr = 44100, n = sr;
            var d = new float[n];
            var rng = new System.Random(7);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float thump = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * 25f * t)), 6f);
                lp += ((float)(rng.NextDouble() * 2.0 - 1.0) - lp) * 0.25f;
                float rattle = lp * (0.4f + 0.6f * thump);
                d[i] = (Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.35f * thump + rattle * 0.25f) * 0.6f;
            }
            s_engine = AudioClip.Create("GeneratorEngine", n, 1, sr, false);
            s_engine.SetData(d, 0);
            return s_engine;
        }
    }

    /// <summary>The work lamp (2026-11-08): a stand lamp about three times as bright as a living room's, whiter, reaching
    /// further, that lights two shelves. Its own model (Tools/Blender/build_machines.py: a heavy foot, a pole held like the
    /// floor lamp's, an amber work-light head) and a wide spotlight out of the head's front; the floor lamp's ways
    /// otherwise (its cord, its plug, its switch). The floor lamp's model if its own is not loaded.</summary>
    public static class WorkLamp
    {
        public static FloorLamp Create(Vector3 at, Quaternion rot, bool plugged)
        {
            if (FittingModels.Ready && FittingModels.MakeMesh("WorkLamp", out var mesh, out var mats))
            {
                var go = new GameObject("WorkLamp");
                go.transform.SetPositionAndRotation(at, rot);
                var model = new GameObject("Model");
                model.transform.SetParent(go.transform, false);
                model.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = model.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats; mr.renderingLayerMask = uint.MaxValue;
                var bc = go.AddComponent<BoxCollider>();
                bc.size = new Vector3(0.38f, 1.56f, 0.38f); bc.center = new Vector3(0f, 0.78f, 0f);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 6f; rb.linearDamping = 0.1f; rb.angularDamping = 3f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.centerOfMass = new Vector3(0f, 0.05f, 0f);         // the heavy foot keeps it standing
                FloorLamp.s_buildWork = true;
                var w = go.AddComponent<FloorLamp>();
                w.itemId = "worklamp"; w.displayName = "Work lamp";
                w.SetPower(3.2f, 9f, new Color(1f, 0.92f, 0.8f));
                w.startPlugged = plugged;
                return w;
            }
            var l = FloorLamp.Create(at, rot);
            l.itemId = "worklamp"; l.displayName = "Work lamp";
            l.SetPower(3.2f, 9f, new Color(1f, 0.92f, 0.8f));
            l.startPlugged = plugged;
            return l;
        }
    }

    /// <summary>Spare tubes and the repair kit (agreed, Docs/GameDesign.md section 8): not made yet, so the counter does not
    /// sell them (<see cref="Ready"/>).</summary>
    public static class Fixes
    {
        public static bool Ready => false;
        public static void CreateRepairKit(Vector3 at, Quaternion rot) { }
        public static void CreateTubes(Vector3 at, Quaternion rot) { }
    }
}
