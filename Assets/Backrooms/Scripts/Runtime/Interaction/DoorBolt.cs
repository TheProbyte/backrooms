using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A slide bolt on the inside of a good room's door (<see cref="Door.AddBolt"/>), above the lever: click it and the hand
    /// slides the knob across (bolted: the lever turns but the door stays shut, it only rattles) or back. Only shut doors
    /// bolt. Frame: on the leaf, +Z out of the face it is on, +X toward the door's latch edge... or its hinge (the knob
    /// slides along X either way).
    /// </summary>
    public class DoorBolt : MonoBehaviour, IPokeTarget
    {
        static Mesh s_cube;
        const float Slide = 0.028f;

        Door _door;
        Transform _knob;
        float _at;

        public Vector3 PokeNormal => transform.forward;

        public void Init(Door door)
        {
            _door = door;
            if (!s_cube) s_cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            Part(transform, "Plate", new Vector3(0f, 0f, 0.004f), new Vector3(0.11f, 0.032f, 0.006f), FurnitureMaterials.Steel);
            Part(transform, "Keeper", new Vector3(-0.075f, 0f, 0.009f), new Vector3(0.03f, 0.03f, 0.016f), FurnitureMaterials.Steel);
            _knob = new GameObject("Knob").transform;
            _knob.SetParent(transform, false);
            Part(_knob, "Rod", new Vector3(0f, 0f, 0.011f), new Vector3(0.08f, 0.012f, 0.012f), FurnitureMaterials.Stainless);
            Part(_knob, "Grip", new Vector3(0.01f, 0f, 0.024f), new Vector3(0.012f, 0.012f, 0.026f), FurnitureMaterials.Stainless);
            var bc = gameObject.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = new Vector3(0f, 0f, 0.025f); bc.size = new Vector3(0.14f, 0.07f, 0.05f);
            _at = _door.Bolted ? 1f : 0f;
            Pose();
        }

        void Part(Transform parent, string name, Vector3 at, Vector3 size, Material m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at; go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = s_cube;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        void Pose() { if (_knob) _knob.localPosition = new Vector3(-Slide * _at, 0f, 0f); }

        void Update()
        {
            if (!_door) return;
            float want = _door.Bolted ? 1f : 0f;
            if (Mathf.Approximately(_at, want)) return;
            _at = Mathf.MoveTowards(_at, want, Time.deltaTime * 8f);
            Pose();
        }

        /// <summary>The fingers take the grip and slide it across (or back).</summary>
        public void PokeStroke(out Vector3 from, out Vector3 to)
        {
            bool bolted = _door && _door.Bolted;
            var a = new Vector3(0.01f - Slide * (bolted ? 1f : 0f), 0f, 0.03f);
            var b = new Vector3(0.01f - Slide * (bolted ? 0f : 1f), 0f, 0.03f);
            from = transform.TransformPoint(a + (b - a).normalized * -0.01f);
            to = transform.TransformPoint(b);
        }

        public void Press() { if (_door) _door.ToggleBolt(); }

        public Vector3 FocusPoint => transform.TransformPoint(new Vector3(0f, 0f, 0.02f));
    }
}
