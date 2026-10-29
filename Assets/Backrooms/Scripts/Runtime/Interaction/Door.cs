using System.Collections.Generic;
using Backrooms.Audio;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A hinged wooden door in a doorway (placed by the chunk builder; model Resources/Items/Door.fbx, see
    /// Tools/Blender/build_door.py). Shut, it is latched: only its lever opens it. The hand (PropGrabber.Door.cs) takes the
    /// lever on your side, pushes it down, and then the door follows the hand as you walk: a step forward or back opens it
    /// a crack, walking on swings it wide, and let go it keeps swinging with what it had (wide open after a quick shove,
    /// or shut with a bang). An unlatched door can be nudged by walking into it. It never swings through the player. Its
    /// angle is remembered (<see cref="DoorStates"/>).
    /// This object's frame is the model's as imported: x along the wall, y up, z toward the side the door opens to. (The
    /// FBX import mirrors the Blender x, so the hinge is on +x, the door opens by turning +y-wise, and the levers point
    /// from their spindles toward +x and go down by turning the other way about z.)
    /// </summary>
    public class Door : MonoBehaviour
    {
        public const float MaxOpen = 88f;
        static readonly Vector3 Hinge = new Vector3(0.48f, 0f, 0.065f);
        static readonly Vector3 Spindle = new Vector3(-0.892f, 0.97f, 0f);        // from the hinge, in the door's frame
        const float Lever = 0.062f, LeverOut = 0.053f, SlabT = 0.04f, LeverDown = 36f;

        public string Key { get; private set; }
        /// <summary>Degrees open (0 shut, toward +z).</summary>
        public float Angle { get; private set; }
        public bool Latched { get; private set; } = true;
        public bool Held { get; private set; }
        public float Speed => _vel;

        float _vel;                      // degrees a second
        float _turnFront, _turnBack;     // levers 0..1 (pushed down)
        int _side;                       // the lever held: +1 the +z face, -1 the other
        Transform _leaf, _front, _back;
        Quaternion _leafRest, _frontRest, _backRest;
        Vector3 _leafAxis, _frontAxis, _backAxis;
        BoxCollider _slab;
        AudioSource _fx, _creak;
        float _settled = -1f;
        bool _moved;                     // moved since it was built (only then is its angle remembered)
        static CharacterController s_cc;

        DoorEdges.Edge _edge;
        int _roomFront, _roomBack;
        bool _shutRegistered;
        Renderer _spillFront, _spillBack;
        MaterialPropertyBlock _mpb;
        float _nextSpill, _spillF, _spillB, _driftSeed;
        static Material s_spill;
        const float ShutBelow = 2.5f;           // degrees: still shut as far as light and sound are concerned

        public void Init(Transform model, float angle, string key, DoorEdges.Edge edge, int roomFront, int roomBack)
        {
            Key = key;
            _edge = edge; _roomFront = roomFront; _roomBack = roomBack;
            _driftSeed = Mathf.Abs(key.GetHashCode() % 1000) * 0.137f;
            _leaf = Find(model, "Leaf"); _front = Find(model, "HandleFront"); _back = Find(model, "HandleBack");
            var slab = Find(model, "Slab");
            if (!_leaf || !slab) { enabled = false; return; }
            _leafRest = _leaf.localRotation;
            _leafAxis = Quaternion.Inverse(_leaf.rotation) * transform.up;
            if (_front) { _frontRest = _front.localRotation; _frontAxis = Quaternion.Inverse(_front.rotation) * transform.forward; }
            if (_back) { _backRest = _back.localRotation; _backAxis = Quaternion.Inverse(_back.rotation) * transform.forward; }
            _slab = slab.gameObject.AddComponent<BoxCollider>();                      // fits the slab's mesh
            var rb = _leaf.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
            if (DoorStates.TryGet(key, out float saved)) angle = saved;
            Angle = Mathf.Clamp(angle, 0f, MaxOpen);
            Latched = Angle < 0.5f;
            Pose();
            MakeSpill();
            UpdateShut();
        }

        void OnDestroy()
        {
            if (_shutRegistered) DoorEdges.Set(_edge, false);
            foreach (var r in new[] { _spillFront, _spillBack })
                if (r) { var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) Destroy(mf.sharedMesh); }
        }

        /// <summary>Shut (or as good as), the doorway is a wall to light and sound.</summary>
        void UpdateShut()
        {
            bool shut = Angle < ShutBelow;
            if (shut == _shutRegistered) return;
            _shutRegistered = shut;
            DoorEdges.Set(_edge, shut);
        }

        // ---------------- Light under the door ----------------

        /// <summary>A thin glow on the floor each side of the door, for light from the room behind it to show under it.</summary>
        void MakeSpill()
        {
            if (!s_spill)
            {
                var sh = Shader.Find("Backrooms/LightSpill");
                if (!sh) return;
                s_spill = new Material(sh) { name = "DoorLightSpill", hideFlags = HideFlags.DontSave };
            }
            _mpb = new MaterialPropertyBlock();
            _spillFront = SpillQuad("SpillFront", 1f);
            _spillBack = SpillQuad("SpillBack", -1f);
        }

        Renderer SpillQuad(string name, float side)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            const float half = 0.47f, from = 0.0f, to = 0.42f, y = 0.004f;
            var mesh = new Mesh { name = name };
            mesh.vertices = new[] { new Vector3(-half, y, from * side), new Vector3(half, y, from * side), new Vector3(half, y, to * side), new Vector3(-half, y, to * side) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = side > 0 ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = s_spill;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.enabled = false;
            return r;
        }

        /// <summary>Each side shows the light of the room on the other side, while the door is shut.</summary>
        void UpdateSpill(float now)
        {
            if (!_spillFront || now < _nextSpill) return;
            _nextSpill = now + 0.12f + (_driftSeed % 0.08f);
            var lights = FindLights();
            float shut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ShutBelow * 0.5f, 8f, Angle));
            float front = lights ? lights.RoomLight(_edge.chunk, _roomBack, now) : 0f;       // the front floor shows the back room
            float back = lights ? lights.RoomLight(_edge.chunk, _roomFront, now) : 0f;
            _spillF = front * shut; _spillB = back * shut;
            Show(_spillFront, _spillF);
            Show(_spillBack, _spillB);
        }

        void Show(Renderer r, float k)
        {
            bool on = k > 0.02f;
            if (r.enabled != on) r.enabled = on;
            if (!on) return;
            _mpb.SetColor("_Color", new Color(1f, 0.95f, 0.82f) * (0.55f * k));
            r.SetPropertyBlock(_mpb);
        }

        static Generation.FixtureLightManager s_lights;
        static Generation.FixtureLightManager FindLights()
        {
            if (!s_lights) s_lights = FindAnyObjectByType<Generation.FixtureLightManager>();
            return s_lights;
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        // ---------------- Geometry ----------------

        Vector3 HingeWorld => transform.TransformPoint(Hinge);
        Quaternion Swing(float angle) => Quaternion.AngleAxis(angle, transform.up);

        /// <summary>A point of the door (from the hinge, in the door's frame, shut) where it is now.</summary>
        Vector3 OnLeaf(Vector3 local, float angle) => HingeWorld + Swing(angle) * transform.TransformVector(local);

        /// <summary>Which face of the door a point is on: +1 the +z face (as the door stands now), -1 the other.</summary>
        public int SideOf(Vector3 p)
        {
            Vector3 local = transform.InverseTransformVector(Quaternion.Inverse(Swing(Angle)) * (p - HingeWorld));
            return local.z > -SlabT * 0.5f ? 1 : -1;
        }

        /// <summary>Where the hand holds the lever on that face (the lever pushed down by turn 0..1).</summary>
        public Vector3 GripPoint(int side, float turn) => OnLeaf(GripLocal(side, turn), Angle);

        Vector3 GripLocal(int side, float turn)
        {
            float a = LeverDown * turn * Mathf.Deg2Rad;
            float z = side > 0 ? LeverOut : -SlabT - LeverOut;
            return Spindle + new Vector3(Lever * Mathf.Cos(a), -Lever * Mathf.Sin(a), z);
        }

        /// <summary>Distance from a point to the lever on that face (a rounded bar, and its rose): what the fingers close on.</summary>
        public float LeverDistance(int side, float turn, Vector3 p)
        {
            float a = LeverDown * turn * Mathf.Deg2Rad;
            float z = side > 0 ? LeverOut : -SlabT - LeverOut;
            Vector3 s = OnLeaf(Spindle + new Vector3(0f, 0f, z), Angle);
            Vector3 tip = OnLeaf(Spindle + new Vector3(0.1f * Mathf.Cos(a), -0.1f * Mathf.Sin(a), z), Angle);
            float bar = WorldItem.SegmentDistance(p, s, tip) - 0.0095f;
            // The door's face behind it.
            Vector3 n = Swing(Angle) * transform.forward * side;
            Vector3 face = OnLeaf(Spindle + new Vector3(0f, 0f, side > 0 ? 0f : -SlabT), Angle);
            float slab = Vector3.Dot(p - face, n);
            return Mathf.Min(bar, slab);
        }

        /// <summary>The hand's frame on the lever: palm down on it, fingers over it toward the door, turned with it.</summary>
        public Quaternion GripRotation(int side, float turn)
        {
            Vector3 n = Swing(Angle) * transform.forward * side;                       // out of that face
            Quaternion lever = Quaternion.AngleAxis(-LeverDown * turn, Swing(Angle) * transform.forward);
            Vector3 fingers = (-n * 0.8f + Vector3.down * 0.45f).normalized;
            Vector3 back = Vector3.ProjectOnPlane(Vector3.up + n * 0.3f, fingers).normalized;
            return lever * Quaternion.LookRotation(fingers, back);
        }

        // ---------------- The hand ----------------

        /// <summary>The hand takes the lever on this side.</summary>
        public void Grab(int side) { Held = true; _side = side; }

        /// <summary>How far down the held lever is (0..1); pushing it down unlatches the door.</summary>
        public void Turn(float turn)
        {
            float was = _side > 0 ? _turnFront : _turnBack;
            if (_side > 0) _turnFront = turn; else _turnBack = turn;
            if (was < 0.6f && turn >= 0.6f) { if (Latched) Fx(SoundBank.DoorLatch, 0.7f); Latched = false; }
            Pose();
        }

        /// <summary>The held door goes where the hand takes it (as far as its hinge allows, never through the player).</summary>
        public void Drive(Vector3 hand, float dt)
        {
            Vector3 up = transform.up;
            Vector3 r0 = Vector3.ProjectOnPlane(transform.TransformVector(GripLocal(_side, 1f)), up);
            Vector3 rt = Vector3.ProjectOnPlane(hand - HingeWorld, up);
            if (rt.sqrMagnitude < 0.01f) return;
            float want = Vector3.SignedAngle(r0, rt, up);
            if (want < -60f) want += 360f;                                             // well round on the open side
            want = Mathf.Clamp(want, 0f, MaxOpen);
            // A door has weight: it follows the hand quickly, not instantly.
            float next = Mathf.MoveTowards(Angle, Mathf.Lerp(Angle, want, 1f - Mathf.Exp(-dt / 0.06f)), 260f * dt);
            float before = Angle;
            if (!TryMove(next)) { _vel = 0f; return; }
            _vel = Mathf.Lerp(_vel, (Angle - before) / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt / 0.05f));
        }

        /// <summary>Out of the face on that side (as the door stands now), toward whoever is there.</summary>
        public Vector3 FaceNormal(int side) => Swing(Angle) * transform.forward * side;

        /// <summary>The way a point on the leaf moves as the door opens (level, unit; zero at the hinge).</summary>
        public Vector3 OpeningAt(Vector3 p)
        {
            Vector3 r = Vector3.ProjectOnPlane(p - HingeWorld, transform.up);
            return r.sqrMagnitude < 1e-6f ? Vector3.zero : Vector3.Cross(transform.up, r).normalized;
        }

        /// <summary>From the hinge toward the lever's edge, along the leaf as it stands now (level, unit).</summary>
        public Vector3 TowardLatch => (Swing(Angle) * transform.TransformDirection(Spindle.x < 0f ? Vector3.left : Vector3.right)).normalized;

        /// <summary>
        /// A quick shove or tug from the hand (right click). Shut, it opens (pushed from the far side it swings wide,
        /// tugged from its own side part way); a latched door is unlatched on the way (the hand hits the lever). Already
        /// open, it is shoved the way the player faces: wider, or toward shut.
        /// </summary>
        public void Shove(int side, Vector3 at, Vector3 facing, bool pull)
        {
            if (Held) return;
            if (Latched)
            {
                Fx(SoundBank.DoorLatch, 0.6f);
                Latched = false;
                if (side > 0) _turnFront = 1f; else _turnBack = 1f;               // springs back up in Update
                Pose();
            }
            float speed;
            if (Angle < 3f) speed = pull ? 115f : 210f;
            else
            {
                Vector3 r = Vector3.ProjectOnPlane(at - HingeWorld, transform.up);
                Vector3 opening = Vector3.Cross(transform.up, r).normalized;
                speed = Vector3.Dot(Vector3.ProjectOnPlane(facing, transform.up), opening) >= 0f ? 190f : -190f;
            }
            _vel = speed;
            _moved = true;
            _shovedAt = Time.time;
        }
        float _shovedAt = -1f, _tuggedAt = -1f;

        /// <summary>A click on a door well open (round 11): the palm swings it shut; it latches as it gets there, clicking
        /// home, or banging if it came hard.</summary>
        public void ShoveShut(float speed = 150f)
        {
            if (Held || Angle < 0.5f) return;
            _vel = -Mathf.Abs(speed);
            _moved = true;
            _shovedAt = Time.time;
        }

        /// <summary>
        /// The hand on the lever tugging the door on open toward you (a quick pull, round 9): it swings at least this fast
        /// (degrees a second) while the hand pulls, resting against the body if the body is still in its way, and keeps
        /// pressing on gently for a moment after so it opens as the body steps back. (Left to its own swing it stopped at
        /// the body at 24 degrees, a gap of 0.38 m.)
        /// </summary>
        public void Tug(float speed)
        {
            if (Held) return;
            if (Latched) { Latched = false; }
            if (_vel < speed) _vel = speed;
            _moved = true;
            _shovedAt = _tuggedAt = Time.time;
        }

        /// <summary>Distance from a point to the slab (the palm flat on it).</summary>
        public float SlabDistance(Vector3 p)
        {
            if (!_slab) return float.MaxValue;
            Vector3 q = Physics.ClosestPoint(p, _slab, _slab.transform.position, _slab.transform.rotation);
            return (q - p).sqrMagnitude < 1e-12f ? -0.001f : Vector3.Distance(q, p);
        }

        /// <summary>The hand lets go: the lever springs up, the door keeps swinging with what it had.</summary>
        public void Release() { Held = false; }

        /// <summary>Walked into: an unlatched door is pushed along a little.</summary>
        public void Nudge(Vector3 at, Vector3 push)
        {
            if (Latched || Held) return;
            // Just tugged open, the body still coming in does not push it back shut (a quick pull at the lever: the door
            // unlatched, the body's last step in shut it again the next frame and it latched).
            if (Time.time - _shovedAt < 0.5f && Vector3.Dot(push, OpeningAt(at)) < 0f) return;
            Vector3 r = Vector3.ProjectOnPlane(at - HingeWorld, transform.up);
            if (r.sqrMagnitude < 0.01f) return;
            Vector3 opening = Vector3.Cross(transform.up, r).normalized;
            float a = Vector3.Dot(push, opening);
            // Moving at the speed of what pushes it (a shove does not add up frame after frame).
            float want = Mathf.Clamp(a / r.magnitude * Mathf.Rad2Deg * 1.1f, -160f, 160f);
            if (want > 0f && _vel < want) _vel = want;
            else if (want < 0f && _vel > want) _vel = want;
            _moved = true;
        }

        // ---------------- Motion ----------------

        void Update()
        {
            float dt = Time.deltaTime;
            UpdateSpill(Time.time);
            Drift(dt);
            // Levers spring back up when not held.
            bool posed = false;
            if (!Held || _side < 0) { if (_turnFront > 0f) { _turnFront = Mathf.MoveTowards(_turnFront, 0f, dt * 6f); posed = true; } }
            if (!Held || _side > 0) { if (_turnBack > 0f) { _turnBack = Mathf.MoveTowards(_turnBack, 0f, dt * 6f); posed = true; } }
            if (!Held && _vel != 0f)
            {
                _vel *= Mathf.Exp(-dt * 1.4f);                                         // hinges and air slow it
                float next = Angle + _vel * dt;
                if (next >= MaxOpen)
                {
                    next = MaxOpen;
                    if (_vel > 35f) Fx(SoundBank.DoorBump, Mathf.Clamp01(_vel / 200f) * 0.8f);
                    _vel = -_vel * 0.18f;
                }
                if (next <= 0f)
                {
                    next = 0f;
                    // Shut: with a bang if it came hard, else it just clicks home.
                    Fx(SoundBank.DoorShut, Mathf.Clamp01(-_vel / 160f) * 0.9f + 0.15f);
                    _vel = 0f; Latched = true;
                }
                // Swinging open into the player (pulled toward you), it rests against you and goes on as you step back; it
                // used to bounce back and latch (a quick pull opened it 6 degrees and it shut again). Swinging shut into you,
                // it bounces off.
                // (Just tugged, it keeps pressing on at 30 a second: it opens as the body steps out of its way.)
                if (!TryMove(next)) _vel = _vel > 0f ? Mathf.Max(Mathf.Min(_vel * 0.9f, 40f), Time.time - _tuggedAt < 0.9f ? 30f : 0f) : -_vel * 0.1f;
                if (Mathf.Abs(_vel) < 1.5f) _vel = 0f;
            }
            else if (posed) Pose();
            // The hinges creak while it moves.
            float creak = Mathf.Clamp01((Mathf.Max(Mathf.Abs(_vel), Mathf.Abs(_driftVel) * 1.6f) - 6f) / 90f);
            if (creak > 0.001f || (_creak && _creak.isPlaying))
            {
                if (!_creak)
                {
                    var at = new GameObject("CreakSound");
                    at.transform.SetParent(transform, false);
                    at.transform.localPosition = Hinge + Vector3.up * 1.0f;
                    _creak = ItemModels.Source(at, 14f);
                    _creak.clip = SoundBank.DoorCreak; _creak.loop = true; _creak.volume = 0f;
                }
                if (!_creak.isPlaying) { _creak.time = Random.Range(0f, _creak.clip.length); _creak.Play(); }
                _creak.volume = Mathf.MoveTowards(_creak.volume, creak * 0.35f * SoundCatalog.Volume(SoundIds.Doors), dt * 3f);
                _creak.pitch = 0.85f + 0.3f * Mathf.Clamp01(Mathf.Abs(_vel) / 160f);
                if (_creak.volume <= 0.001f && creak <= 0.001f) _creak.Stop();
            }
            // Remember where it was left.
            if (!Held && _vel == 0f)
            {
                if (_settled < 0f) { _settled = Time.time; if (_moved) DoorStates.Set(Key, Angle); }
            }
            else _settled = -1f;
        }

        /// <summary>Turns the door to an angle unless that would put it into the player. False if blocked.</summary>
        bool TryMove(float angle)
        {
            if (Mathf.Abs(angle - Angle) < 1e-4f) return true;
            if (!s_cc) { var p = FindAnyObjectByType<CamcorderPlayerController>(); s_cc = p ? p.GetComponent<CharacterController>() : null; }
            if (s_cc && s_cc.enabled && _slab)
            {
                Quaternion d = Swing(angle) * Quaternion.Inverse(Swing(Angle));
                Vector3 h = HingeWorld;
                var t = _slab.transform;
                float into = Overlap(h + d * (t.position - h), d * t.rotation);
                // Only if it is going further into the player (it may always move out).
                if (into > 0.002f && into > Overlap(t.position, t.rotation)) return false;
            }
            Angle = angle;
            if (!_drifting) _moved = true;
            if (Angle > 0.5f) Latched = false;
            Pose();
            UpdateShut();
            return true;
        }

        // ---------------- Air ----------------

        bool _drifting;
        float _driftVel;

        /// <summary>
        /// An unlatched door left alone is never quite still: air moving through the building pushes it a degree or two
        /// now and then (and once in a while a gust swings it a little more, the hinges creaking). It never latches itself.
        /// </summary>
        void Drift(float dt)
        {
            if (Held || Latched || _vel != 0f || Angle < 1.5f || Angle > MaxOpen - 1f) { _driftVel = 0f; return; }
            float t = Time.time * 0.06f + _driftSeed;
            float push = (Mathf.PerlinNoise(t, _driftSeed) - 0.5f) * 5f;                      // deg/s^2
            float gust = Mathf.PerlinNoise(Time.time * 0.03f, _driftSeed * 3.1f);
            if (gust > 0.78f) push += (gust - 0.78f) * 140f * Mathf.Sign(Mathf.PerlinNoise(_driftSeed, Time.time * 0.02f) - 0.5f);
            _driftVel = Mathf.Clamp(_driftVel + push * dt, -14f, 14f) * Mathf.Exp(-dt * 0.8f);
            float next = Angle + _driftVel * dt;
            if (next < 1.5f || next > MaxOpen - 1f) { _driftVel = -_driftVel * 0.3f; return; }
            _drifting = true;
            if (!TryMove(next)) _driftVel = 0f;
            _drifting = false;
        }

        /// <summary>How far the slab, posed so, is into the player's capsule (0 if clear).</summary>
        float Overlap(Vector3 pos, Quaternion rot)
        {
            var cc = s_cc;
            Vector3 s = _slab.transform.lossyScale;
            Vector3 half = Vector3.Scale(_slab.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z))) * 0.5f;
            Vector3 centre = pos + rot * Vector3.Scale(_slab.center, s);
            Vector3 c = cc.transform.TransformPoint(cc.center);
            float h = Mathf.Max(0f, cc.height * 0.5f - cc.radius);
            float worst = 0f;
            for (int k = 0; k <= 4; k++)
            {
                Vector3 p = c + Vector3.up * Mathf.Lerp(-h, h, k / 4f);
                Vector3 l = Quaternion.Inverse(rot) * (p - centre);
                Vector3 q = new Vector3(Mathf.Clamp(l.x, -half.x, half.x), Mathf.Clamp(l.y, -half.y, half.y), Mathf.Clamp(l.z, -half.z, half.z));
                worst = Mathf.Max(worst, cc.radius - (l - q).magnitude);
            }
            return worst;
        }

        void Pose()
        {
            if (_leaf) _leaf.localRotation = _leafRest * Quaternion.AngleAxis(Angle, _leafAxis);
            if (_front) _front.localRotation = _frontRest * Quaternion.AngleAxis(-LeverDown * _turnFront, _frontAxis);
            if (_back) _back.localRotation = _backRest * Quaternion.AngleAxis(-LeverDown * _turnBack, _backAxis);
        }

        void Fx(AudioClip clip, float volume)
        {
            if (!_fx)
            {
                var at = new GameObject("DoorSound");
                at.transform.SetParent(transform, false);
                at.transform.localPosition = new Vector3(0.3f, 1.0f, 0f);
                _fx = ItemModels.Source(at, 16f);
            }
            _fx.PlayOneShot(clip, volume * SoundCatalog.Volume(SoundIds.Doors));
        }
    }

    /// <summary>Where doors were left (by the door's key: chunk and edge). Saved with the world.</summary>
    public static class DoorStates
    {
        static readonly Dictionary<string, float> s_angles = new Dictionary<string, float>();
        public static bool TryGet(string key, out float angle) => s_angles.TryGetValue(key ?? "", out angle);
        public static void Set(string key, float angle) { if (!string.IsNullOrEmpty(key)) s_angles[key] = angle; }
        public static void Clear() => s_angles.Clear();

        public static void Save(List<string> keys, List<float> angles)
        {
            keys.Clear(); angles.Clear();
            foreach (var kv in s_angles) { keys.Add(kv.Key); angles.Add(Mathf.Round(kv.Value * 10f) / 10f); }
        }

        public static void Load(List<string> keys, List<float> angles)
        {
            Clear();
            if (keys == null || angles == null) return;
            for (int i = 0; i < keys.Count && i < angles.Count; i++) s_angles[keys[i]] = angles[i];
        }
    }
}
