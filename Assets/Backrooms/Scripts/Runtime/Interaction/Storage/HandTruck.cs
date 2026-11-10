using System.Collections.Generic;
using Backrooms.Camcorder;
using Backrooms.Generation;
using Backrooms.Saves;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The hand truck (2026-11-09, slice 1's carrying upgrade: Docs/GameDesign.md section 16). As in Big Ambitions, where a
    /// hand truck takes four things to the one you carry by hand: click its handles to take it, and the boxes you click
    /// go onto its nose plate instead of into your arms, a tall stack against its frame (up to <see cref="Load"/> kg and
    /// <see cref="Height"/> m: four warehouse boxes, five of QUILLBY's heavy paper, where the arms take two, or one). They
    /// put down the same way (a tap of the click onto the outline). Q with nothing on it parks it, standing, where you are.
    /// <para>Held, it rides in front of you (the body walks it, upright like a four-wheel convertible's cart mode), so a
    /// stack on it never slides off a turn. Bought at the supply counter once onboarding is done; saved where it was last
    /// parked (or where you were). Not shared with co-op friends yet.</para>
    /// </summary>
    public class HandTruck : MonoBehaviour, IPokeTarget
    {
        /// <summary>The one the local player is wheeling, or null.</summary>
        public static HandTruck Equipped { get; private set; }
        static readonly List<HandTruck> s_all = new List<HandTruck>();

        public const float Load = 70f, Height = 1.45f;
        /// <summary>The nose plate's top (m above the floor) and where it starts, ahead of the body (m).</summary>
        public const float PlateTop = 0.04f, PlateZ = 0.72f;
        const float At = 0.65f;                                // the frame's foot ahead of the body when held (m)

        /// <summary>A handle, in the body's frame (side +1 right, -1 left), while it is held.</summary>
        public static Vector3 Handle(float side) => new Vector3(0.19f * Mathf.Sign(side), 1.06f, At - 0.09f);
        // (The frame leans back toward you a little more held, its handles at the hands: see Press.)

        int _index;
        BoxCollider _solid;
        Transform _wheelL, _wheelR;
        float _spin, _nextSave;
        bool _wasHolding;
        Vector3 _last;
        static Material s_frame, s_rubber, s_steel;

        // ---------------- Making, saving ----------------

        WorldSaveData D => GameSession.Data;

        /// <summary>A new one (bought at the counter), standing at a spot.</summary>
        public static HandTruck Create(Vector3 at, Quaternion rot)
        {
            var d = GameSession.Data;
            if (d == null) return null;
            int i = d.handTrucks.Count / 4;
            d.handTrucks.Add(at.x); d.handTrucks.Add(at.y); d.handTrucks.Add(at.z); d.handTrucks.Add(rot.eulerAngles.y);
            return Make(i, at, rot);
        }

        /// <summary>Every hand truck in the save, where it was left (the world's first frames: Contracts.Start).</summary>
        public static void LoadAll()
        {
            foreach (var t in s_all) if (t) Destroy(t.gameObject);
            s_all.Clear();
            Equipped = null;
            var d = GameSession.Data;
            if (d == null) return;
            for (int i = 0; i + 3 < d.handTrucks.Count; i += 4)
                Make(i / 4, new Vector3(d.handTrucks[i], d.handTrucks[i + 1], d.handTrucks[i + 2]), Quaternion.Euler(0f, d.handTrucks[i + 3], 0f));
        }

        static HandTruck Make(int index, Vector3 at, Quaternion rot)
        {
            var go = new GameObject("HandTruck " + index);
            go.transform.SetPositionAndRotation(at, rot);
            var t = go.AddComponent<HandTruck>();
            t._index = index;
            t.Build();
            s_all.Add(t);
            return t;
        }

        void Save(Vector3 at, float yaw)
        {
            if (D == null || _index * 4 + 3 >= D.handTrucks.Count) return;
            D.handTrucks[_index * 4] = at.x; D.handTrucks[_index * 4 + 1] = at.y; D.handTrucks[_index * 4 + 2] = at.z; D.handTrucks[_index * 4 + 3] = yaw;
        }

        void OnDestroy() { s_all.Remove(this); if (Equipped == this) Equipped = null; }

        // ---------------- The model: the hub's chunky style (amber tube frame, dark tyres, a steel nose plate) ----------------

        void Build()
        {
            Materials();
            // The frame: two uprights leaning back a little, three cross bars, the handle loop at the top.
            for (int s = -1; s <= 1; s += 2)
            {
                Part("Upright", new Vector3(0.19f * s, 0.55f, -0.045f), new Vector3(0.035f, 1.08f, 0.035f), s_frame, -5f);
                Part("Grip", new Vector3(0.19f * s, 1.07f, -0.09f), new Vector3(0.05f, 0.12f, 0.05f), s_rubber, -5f);
            }
            for (int k = 0; k < 3; k++) Part("Bar", new Vector3(0f, 0.28f + k * 0.3f, -0.02f - k * 0.026f), new Vector3(0.38f, 0.03f, 0.03f), s_frame, 0f);
            Part("Top", new Vector3(0f, 1.1f, -0.095f), new Vector3(0.42f, 0.035f, 0.035f), s_frame, 0f);
            // The nose plate, out ahead along the floor.
            Part("Plate", new Vector3(0f, 0.02f, 0.2f), new Vector3(0.44f, 0.022f, 0.32f), s_steel, 0f);
            Part("PlateLip", new Vector3(0f, 0.035f, 0.355f), new Vector3(0.44f, 0.02f, 0.02f), s_steel, 0f);
            // The wheels behind the frame's foot, on an axle.
            Part("Axle", new Vector3(0f, 0.12f, -0.07f), new Vector3(0.5f, 0.025f, 0.025f), s_steel, 0f);
            _wheelL = Wheel(-0.27f);
            _wheelR = Wheel(0.27f);
            // Solid when parked (and what a click finds).
            _solid = gameObject.AddComponent<BoxCollider>();
            _solid.center = new Vector3(0f, 0.56f, 0.08f);
            _solid.size = new Vector3(0.58f, 1.12f, 0.5f);
        }

        Transform Wheel(float x)
        {
            var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            w.name = "Wheel";
            Destroy(w.GetComponent<Collider>());
            w.transform.SetParent(transform, false);
            w.transform.localPosition = new Vector3(x, 0.12f, -0.07f);
            w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            w.transform.localScale = new Vector3(0.24f, 0.035f, 0.24f);
            Paint(w, s_rubber);
            var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hub.name = "Hub";
            Destroy(hub.GetComponent<Collider>());
            hub.transform.SetParent(w.transform, false);
            hub.transform.localScale = new Vector3(0.45f, 1.1f, 0.45f);
            Paint(hub, s_frame);
            return w.transform;
        }

        void Part(string name, Vector3 at, Vector3 size, Material m, float lean)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.Euler(lean, 0f, 0f);
            go.transform.localScale = size;
            Paint(go, m);
        }

        static void Paint(GameObject go, Material m)
        {
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.On; r.renderingLayerMask = uint.MaxValue;
        }

        static void Materials()
        {
            if (s_frame) return;
            var sh = Shader.Find("Backrooms/Lit") ?? Shader.Find("Universal Render Pipeline/Lit");
            Material M(string n, Color c, float smooth)
            {
                var m = new Material(sh) { name = n, hideFlags = HideFlags.DontSave };
                m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                return m;
            }
            s_frame = M("HandTruck_Frame", new Color(0.93f, 0.6f, 0.13f), 0.45f);
            s_rubber = M("HandTruck_Rubber", new Color(0.09f, 0.08f, 0.08f), 0.15f);
            s_steel = M("HandTruck_Steel", new Color(0.55f, 0.56f, 0.57f), 0.55f);
        }

        // ---------------- Taking it, parking it ----------------

        public Vector3 PokeNormal => -transform.forward;
        public Vector3 FocusPoint => transform.TransformPoint(new Vector3(0f, 1.07f, -0.09f));
        public void PokeStroke(out Vector3 from, out Vector3 to) { from = FocusPoint - transform.forward * 0.05f; to = FocusPoint; }

        static PropGrabber Grabber()
        {
            var ws = WorldStreamer.Current;
            return ws && ws.player ? ws.player.GetComponentInChildren<PropGrabber>() : null;
        }

        /// <summary>Its handles clicked: you take it (with nothing in your hands).</summary>
        public void Press()
        {
            var g = Grabber();
            if (!g || Equipped) return;
            if (g.Held) { CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.hands_full")); return; }
            Equipped = this;
            _solid.enabled = false;
            transform.SetParent(g.transform, false);
            transform.localPosition = new Vector3(0f, 0f, At);
            transform.localRotation = Quaternion.identity;
            _last = transform.position;
            CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.hand_truck"));
        }

        /// <summary>Set down standing where it is, solid again.</summary>
        void Park(PropGrabber g)
        {
            var at = transform.position;
            float yaw = transform.eulerAngles.y;
            transform.SetParent(null, true);
            if (Physics.Raycast(at + Vector3.up * 0.6f, Vector3.down, out var hit, 1.6f, ~0, QueryTriggerInteraction.Ignore)) at.y = hit.point.y;
            transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            _solid.enabled = true;
            Equipped = null;
            Save(at, yaw);
            CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.hand_truck_parked"));
        }

        void Update()
        {
            if (Equipped != this) return;
            var g = Grabber();
            if (!g) return;
            // Q with nothing on it: parked here.
            var kb = Keyboard.current;
            // (Not the press that just set the last box down: that Q was the box's.)
            bool held = g.Held;
            if (kb != null && kb.qKey.wasPressedThisFrame && !held && !_wasHolding && !CamcorderPlayerController.InputBlocked) { Park(g); return; }
            _wasHolding = held;
            // The wheels turn as you go.
            var p = transform.position;
            float moved = Vector3.Dot(p - _last, transform.forward);
            _last = p;
            _spin += moved / 0.12f * Mathf.Rad2Deg;
            if (_wheelL) _wheelL.localRotation = Quaternion.Euler(_spin, 0f, 90f);
            if (_wheelR) _wheelR.localRotation = Quaternion.Euler(_spin, 0f, 90f);
            // Saved where you are (if you save with it in hand, it is there next time).
            if (Time.time >= _nextSave) { _nextSave = Time.time + 1f; Save(p, transform.eulerAngles.y); }
        }
    }
}
