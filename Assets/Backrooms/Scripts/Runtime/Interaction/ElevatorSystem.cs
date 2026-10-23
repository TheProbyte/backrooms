using System.Collections.Generic;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Backrooms.Interaction
{
    /// <summary>A push button on a wall (or inside the cab). Clicked by the player's hand (<see cref="PropGrabber"/>).</summary>
    public class ElevatorButton : MonoBehaviour
    {
        public System.Action onPress;
        public Vector3 Normal => transform.forward;          // out of the wall, toward whoever presses it
        Renderer _lamp;
        Material _on, _off;
        bool _lit;
        float _flash;                                         // a quick light-up when pressed, even if nothing else lights it

        public void Init(Renderer lamp, Material on, Material off) { _lamp = lamp; _on = on; _off = off; SetLit(false); }
        public void SetLit(bool lit)
        {
            _lit = lit;
            Refresh();
        }
        void Refresh() { if (_lamp) _lamp.sharedMaterial = _lit || _flash > 0f ? _on : _off; }
        void Update() { if (_flash > 0f) { _flash -= Time.deltaTime; if (_flash <= 0f) Refresh(); } }
        public bool Lit => _lit;
        public void Press() { _flash = 0.35f; Refresh(); onPress?.Invoke(); }
    }

    /// <summary>Marks the cab's colliders so the player controller can ride along.</summary>
    public class ElevatorCab : MonoBehaviour { }

    /// <summary>
    /// One elevator: a cab that runs between the ground and upper storey of an atrium's wing, hoistway doors on
    /// both floors, a call button beside each, and a button in the cab. Built from boxes at runtime in the
    /// owning chunk (<see cref="OfficeChunkBuilder"/> supplies <see cref="ElevatorSpec"/>); which floor the cab
    /// stands at is remembered while the chunk is unloaded.
    ///
    /// Local frame: origin at the doorway's centre in the wall plane at floor 0; +z points out into the lobby,
    /// +x to the right, the shaft lies at -z. Behaviour: pressing a call button lights it and brings the cab;
    /// the doors open; stepping in and staying a moment closes the doors and takes the cab to the other floor
    /// (or press the cab's button); it moves on a smooth curve, then opens. A body in the doorway stops the
    /// doors from closing.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ElevatorSystem : MonoBehaviour
    {
        public float doorTime = 1.4f, travelTime = 9f, autoCloseDelay = 1.8f, idleCloseDelay = 8f;

        enum Phase { Closed, Opening, Open, Closing, Moving }

        // Cab and door dimensions (metres)
        const float DoorW = 1.05f, DoorH = 2.1f, CabW = 2.0f, CabD = 2.0f, CabH = 2.3f, CabFront = 0.14f;

        static readonly Dictionary<ulong, int> SavedFloor = new Dictionary<ulong, int>();
        static Material _steel, _steelDark, _floorMat, _lightPanel, _btnOn, _btnOff, _plate;

        ulong _id;
        float _pitch;
        Transform _cab;
        readonly Transform[] _hoistL = new Transform[2], _hoistR = new Transform[2];
        Transform _cabL, _cabR;
        readonly ElevatorButton[] _call = new ElevatorButton[2];
        ElevatorButton _cabButton;
        CamcorderPlayerController _player;

        int _floor, _target, _pending = -1;
        Phase _phase;
        float _door, _moveT, _cabY, _fromY, _toY, _openTimer, _insideTimer;
        bool _armed = true, _insideLast;

        public static ElevatorSystem Spawn(Transform chunk, ElevatorSpec spec)
        {
            var go = new GameObject("Elevator");
            go.transform.SetParent(chunk, false);
            go.transform.localPosition = spec.door;
            go.transform.localRotation = Quaternion.LookRotation(spec.outward, Vector3.up);
            var e = go.AddComponent<ElevatorSystem>();
            e.Build(spec);
            return e;
        }

        // ---------------- Build ----------------

        static Material Mat(string name, Color c, float metallic, float smooth, Color? emission = null)
        {
            var m = new Material(Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            return m;
        }

        static void EnsureMaterials()
        {
            if (_steel) return;
            _steel = Mat("Lift_Steel", new Color(0.74f, 0.75f, 0.77f), 0.4f, 0.5f);
            _steelDark = Mat("Lift_SteelDark", new Color(0.32f, 0.33f, 0.35f), 0.5f, 0.4f);
            _floorMat = Mat("Lift_Floor", new Color(0.2f, 0.19f, 0.17f), 0f, 0.3f);
            _lightPanel = Mat("Lift_Light", new Color(0.9f, 0.88f, 0.8f), 0f, 0.2f, new Color(2.6f, 2.45f, 2.0f));
            _btnOn = Mat("Lift_ButtonOn", new Color(1f, 0.7f, 0.25f), 0f, 0.5f, new Color(3.2f, 1.7f, 0.35f));
            _btnOff = Mat("Lift_ButtonOff", new Color(0.55f, 0.5f, 0.42f), 0f, 0.5f, new Color(0.08f, 0.06f, 0.03f));
            _plate = Mat("Lift_Plate", new Color(0.13f, 0.13f, 0.14f), 0.6f, 0.4f);
        }

        Transform Part(Transform parent, string name, Vector3 pos, Vector3 size, Material m, bool collide, bool cast = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var col = go.GetComponent<Collider>();
            if (!collide) Destroy(col);
            return go.transform;
        }

        ElevatorButton MakeButton(Transform parent, string name, Vector3 pos, Quaternion rot, System.Action onPress)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos; root.transform.localRotation = rot;     // +z of the button points out of its wall
            Part(root.transform, "Plate", new Vector3(0f, 0f, 0.008f), new Vector3(0.1f, 0.22f, 0.016f), _plate, false);
            var lamp = Part(root.transform, "Lamp", new Vector3(0f, 0.02f, 0.02f), new Vector3(0.05f, 0.05f, 0.012f), _btnOff, false);
            var trig = root.AddComponent<BoxCollider>();
            trig.isTrigger = true; trig.center = new Vector3(0f, 0f, 0.04f); trig.size = new Vector3(0.2f, 0.28f, 0.12f);
            var b = root.AddComponent<ElevatorButton>();
            b.Init(lamp.GetComponent<Renderer>(), _btnOn, _btnOff);
            b.onPress = onPress;
            return b;
        }

        void Build(ElevatorSpec spec)
        {
            EnsureMaterials();
            _id = spec.id; _pitch = spec.pitch;
            int side = spec.buttonSide;
            _floor = SavedFloor.TryGetValue(_id, out var f) ? f : 0;
            _cabY = _floor * _pitch;

            float halfDoor = DoorW * 0.5f;
            for (int fl = 0; fl < 2; fl++)
            {
                float y0 = fl * _pitch;
                var frame = new GameObject("Hoistway" + fl).transform;
                frame.SetParent(transform, false);
                frame.localPosition = new Vector3(0f, y0, 0f);
                // Trim around the doorway on the lobby side, and the two sliding leaves inside the wall.
                Part(frame, "JambL", new Vector3(-(halfDoor + 0.03f), DoorH * 0.5f, 0.095f), new Vector3(0.06f, DoorH + 0.06f, 0.04f), _steel, false);
                Part(frame, "JambR", new Vector3(halfDoor + 0.03f, DoorH * 0.5f, 0.095f), new Vector3(0.06f, DoorH + 0.06f, 0.04f), _steel, false);
                Part(frame, "Header", new Vector3(0f, DoorH + 0.03f, 0.095f), new Vector3(DoorW + 0.18f, 0.06f, 0.04f), _steel, false);
                _hoistL[fl] = Part(frame, "LeafL", new Vector3(-DoorW * 0.25f, DoorH * 0.5f - 0.01f, 0f), new Vector3(DoorW * 0.5f + 0.02f, DoorH - 0.02f, 0.04f), _steel, true, true);
                _hoistR[fl] = Part(frame, "LeafR", new Vector3(DoorW * 0.25f, DoorH * 0.5f - 0.01f, 0f), new Vector3(DoorW * 0.5f + 0.02f, DoorH - 0.02f, 0.04f), _steel, true, true);
                // Call button on the wall beside the door.
                int flc = fl;
                _call[fl] = MakeButton(frame, "CallButton", new Vector3(side * 1.2f, 1.1f, 0.075f), Quaternion.identity, () => Call(flc));
            }

            // The cab.
            _cab = new GameObject("Cab").transform;
            _cab.SetParent(transform, false);
            _cab.localPosition = new Vector3(0f, _cabY, 0f);
            _cab.gameObject.AddComponent<ElevatorCab>();
            float zc = -(CabFront + CabD * 0.5f);
            Part(_cab, "Floor", new Vector3(0f, -0.05f + 0.012f, zc), new Vector3(CabW, 0.1f, CabD), _floorMat, true);
            Part(_cab, "Back", new Vector3(0f, CabH * 0.5f, -(CabFront + CabD) - 0.025f), new Vector3(CabW + 0.1f, CabH, 0.05f), _steel, true);
            Part(_cab, "WallL", new Vector3(-(CabW * 0.5f + 0.025f), CabH * 0.5f, zc), new Vector3(0.05f, CabH, CabD), _steel, true);
            Part(_cab, "WallR", new Vector3(CabW * 0.5f + 0.025f, CabH * 0.5f, zc), new Vector3(0.05f, CabH, CabD), _steel, true);
            Part(_cab, "Ceiling", new Vector3(0f, CabH + 0.03f, zc), new Vector3(CabW + 0.1f, 0.06f, CabD), _steelDark, true);
            Part(_cab, "LightPanel", new Vector3(0f, CabH - 0.005f, zc), new Vector3(1.2f, 0.012f, 0.7f), _lightPanel, false);
            float panelW = (CabW - DoorW) * 0.5f;
            float pz = -(CabFront + 0.05f);
            Part(_cab, "FrontL", new Vector3(-(halfDoor + panelW * 0.5f), CabH * 0.5f, pz), new Vector3(panelW, CabH, 0.1f), _steel, true);
            Part(_cab, "FrontR", new Vector3(halfDoor + panelW * 0.5f, CabH * 0.5f, pz), new Vector3(panelW, CabH, 0.1f), _steel, true);
            Part(_cab, "FrontTop", new Vector3(0f, DoorH + (CabH - DoorH) * 0.5f, pz), new Vector3(DoorW, CabH - DoorH, 0.1f), _steel, true);
            _cabL = Part(_cab, "CabLeafL", new Vector3(-DoorW * 0.25f, DoorH * 0.5f, pz), new Vector3(DoorW * 0.5f + 0.02f, DoorH - 0.02f, 0.03f), _steel, true, true);
            _cabR = Part(_cab, "CabLeafR", new Vector3(DoorW * 0.25f, DoorH * 0.5f, pz), new Vector3(DoorW * 0.5f + 0.02f, DoorH - 0.02f, 0.03f), _steel, true, true);
            Part(_cab, "Rail", new Vector3(0f, 0.9f, -(CabFront + CabD) + 0.04f), new Vector3(1.6f, 0.04f, 0.04f), _steel, false);
            // The cab's own button, on the right-hand wall near the door, pointing into the cab (-x).
            _cabButton = MakeButton(_cab, "CabButton", new Vector3(CabW * 0.5f, 1.1f, -(CabFront + 0.45f)), Quaternion.LookRotation(Vector3.left, Vector3.up), CabPressed);

            var lightGo = new GameObject("CabLight");
            lightGo.transform.SetParent(_cab, false);
            lightGo.transform.localPosition = new Vector3(0f, CabH - 0.25f, zc);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point; l.range = 4f; l.intensity = 3.2f; l.color = new Color(1f, 0.93f, 0.8f); l.shadows = LightShadows.None;
            lightGo.AddComponent<UniversalAdditionalLightData>().renderingLayers = 0xFFFFFFFFu;

            ApplyPose();
        }

        // ---------------- Control ----------------

        void Call(int fl)
        {
            if (_phase == Phase.Moving) { if (_target != fl) _pending = fl; return; }
            if (fl == _floor)
            {
                if (_phase == Phase.Closed || _phase == Phase.Closing) _phase = Phase.Opening;
                else if (_phase == Phase.Open) _openTimer = 0f;
                return;
            }
            _pending = fl;
            if (_phase == Phase.Open || _phase == Phase.Opening) _phase = Phase.Closing;
        }

        void CabPressed()
        {
            if (_phase == Phase.Moving) return;
            _pending = 1 - _floor;
            if (_phase == Phase.Open || _phase == Phase.Opening) _phase = Phase.Closing;
        }

        void StartMove(int to)
        {
            _target = to; _fromY = _cabY; _toY = to * _pitch; _moveT = 0f; _phase = Phase.Moving; _pending = -1;
        }

        bool InsideCab(Vector3 world)
        {
            var p = transform.InverseTransformPoint(world);
            return Mathf.Abs(p.x) < CabW * 0.5f - 0.05f && p.z < -CabFront - 0.05f && p.z > -(CabFront + CabD) && p.y > _cabY - 0.3f && p.y < _cabY + CabH;
        }

        bool InDoorway(Vector3 world)
        {
            var p = transform.InverseTransformPoint(world);
            float y = _floor * _pitch;
            return Mathf.Abs(p.x) < DoorW * 0.5f + 0.2f && p.z > -CabFront - 0.1f && p.z < 0.55f && p.y > y - 0.3f && p.y < y + DoorH;
        }

        void Update()
        {
            if (!_player) _player = FindAnyObjectByType<CamcorderPlayerController>();
            float dt = Time.deltaTime;
            bool inside = _player && InsideCab(_player.transform.position);
            bool inDoorway = _player && InDoorway(_player.transform.position);

            switch (_phase)
            {
                case Phase.Closed:
                    if (_pending == _floor) { _pending = -1; _phase = Phase.Opening; }
                    else if (_pending != -1) StartMove(_pending);
                    break;
                case Phase.Opening:
                    _door = Mathf.MoveTowards(_door, 1f, dt / doorTime);
                    if (_door >= 1f) { _phase = Phase.Open; _openTimer = 0f; }
                    break;
                case Phase.Open:
                    _openTimer += dt;
                    if (inside) _insideTimer += dt; else { _insideTimer = 0f; _armed = true; }
                    if (_armed && inside && _insideTimer > autoCloseDelay) { _pending = 1 - _floor; _armed = false; }
                    if (_pending == _floor) _pending = -1;
                    if ((_pending != -1 && !inDoorway) || (_pending == -1 && !inside && !inDoorway && _openTimer > idleCloseDelay)) _phase = Phase.Closing;
                    break;
                case Phase.Closing:
                    if (inDoorway) { _phase = Phase.Opening; break; }              // somebody is in the way
                    _door = Mathf.MoveTowards(_door, 0f, dt / doorTime);
                    if (_door <= 0f) _phase = Phase.Closed;
                    break;
                case Phase.Moving:
                    _moveT = Mathf.Min(1f, _moveT + dt / travelTime);
                    float e = _moveT * _moveT * _moveT * (_moveT * (_moveT * 6f - 15f) + 10f);        // smootherstep: no jerk at either end
                    float y = Mathf.Lerp(_fromY, _toY, e);
                    if (_insideLast && _player) _player.platformMotion += Vector3.up * (y - _cabY);
                    _cabY = y;
                    if (_moveT >= 1f)
                    {
                        _floor = _target; SavedFloor[_id] = _floor; _cabY = _toY;
                        _phase = Phase.Opening; _armed = !inside; _insideTimer = 0f;
                    }
                    break;
            }
            _insideLast = inside;
            ApplyPose();
        }

        void ApplyPose()
        {
            _cab.localPosition = new Vector3(0f, _cabY, 0f);
            float k = _door * _door * (3f - 2f * _door);
            float slide = k * (DoorW * 0.5f + 0.04f);
            SetLeaves(_cabL, _cabR, DoorW * 0.25f, slide);
            for (int fl = 0; fl < 2; fl++)
            {
                float open = fl == _floor && _phase != Phase.Moving ? slide : 0f;
                SetLeaves(_hoistL[fl], _hoistR[fl], DoorW * 0.25f, open);
            }
            for (int fl = 0; fl < 2; fl++) _call[fl].SetLit(_pending == fl || (_phase == Phase.Moving && _target == fl));
            _cabButton.SetLit(_pending != -1 || _phase == Phase.Moving);
            Physics.SyncTransforms();
        }

        static void SetLeaves(Transform l, Transform r, float closedX, float slide)
        {
            var p = l.localPosition; p.x = -closedX - slide; l.localPosition = p;
            p = r.localPosition; p.x = closedX + slide; r.localPosition = p;
        }
    }
}
