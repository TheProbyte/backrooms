using Backrooms.Audio;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The main switch on a janitor's closet's breaker panel (the panel itself is part of the chunk: OfficeChunkBuilder.Special).
    /// Click it and the hand pushes the handle up (on) or down (off): every outlet in the district on that storey gets power
    /// (<see cref="PowerGrid"/>), except the few rooms that have their own. A small lamp on the panel shows it is on.
    /// Frame: origin at the middle of the panel's front, +Z out of the wall, +Y up.
    /// </summary>
    public class BreakerPanel : MonoBehaviour, IPokeTarget
    {
        static readonly Vector3 Pivot = new Vector3(-0.115f, -0.07f, 0.012f);   // on the right as you face it
        const float Arm = 0.1f, OnAngle = 28f, OffAngle = 152f;

        static Mesh s_cube;
        static Material s_pilot;

        Vector3Int _circuit;
        Transform _lever;
        Renderer _lamp;
        MaterialPropertyBlock _mpb;
        float _angle, _want;
        AudioSource _fx;

        public Vector3 PokeNormal => transform.forward;
        bool On => PowerGrid.On(_circuit);

        public void Init(Vector3Int circuit, uint renderMask)
        {
            _circuit = circuit;
            if (!s_cube) s_cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var dark = FurnitureMaterials.Dark;

            _lever = new GameObject("Handle").transform;
            _lever.SetParent(transform, false);
            _lever.localPosition = Pivot;
            Part(_lever, "Bar", new Vector3(0f, Arm * 0.5f, 0f), new Vector3(0.022f, Arm, 0.022f), dark, renderMask);
            Part(_lever, "Grip", new Vector3(0f, Arm, 0f), new Vector3(0.05f, 0.034f, 0.034f), dark, renderMask);
            Part(transform, "Mount", Pivot + new Vector3(0f, 0f, -0.006f), new Vector3(0.05f, 0.05f, 0.016f), FurnitureMaterials.Steel, renderMask);

            // The pilot lamp: lit green while the district has power.
            if (!s_pilot)
            {
                var sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh) s_pilot = new Material(sh) { name = "BreakerPilot", hideFlags = HideFlags.DontSave };
            }
            _lamp = Part(transform, "Pilot", new Vector3(0.11f, -0.17f, 0.006f), new Vector3(0.022f, 0.022f, 0.008f), s_pilot ? s_pilot : dark, renderMask);
            _mpb = new MaterialPropertyBlock();

            var bc = gameObject.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = Pivot + new Vector3(0f, 0f, 0.05f); bc.size = new Vector3(0.14f, 0.26f, 0.1f);
            _want = _angle = On ? OnAngle : OffAngle;
            Pose();
        }

        Renderer Part(Transform parent, string name, Vector3 at, Vector3 size, Material m, uint mask)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at; go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = s_cube;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.renderingLayerMask = mask;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return r;
        }

        void Pose()
        {
            if (_lever) _lever.localRotation = Quaternion.Euler(_angle, 0f, 0f);
            if (_lamp && _mpb != null)
            {
                bool on = Mathf.Abs(_angle - OnAngle) < 1f;
                _mpb.SetColor("_BaseColor", on ? new Color(0.35f, 1f, 0.4f) : new Color(0.05f, 0.08f, 0.05f));
                _lamp.SetPropertyBlock(_mpb);
            }
        }

        void Update()
        {
            _want = On ? OnAngle : OffAngle;               // another panel of the district, or a loaded save
            if (Mathf.Approximately(_angle, _want)) return;
            _angle = Mathf.MoveTowards(_angle, _want, Time.deltaTime * 900f);
            Pose();
        }

        Vector3 Tip(float angle) => Pivot + Quaternion.Euler(angle, 0f, 0f) * Vector3.up * Arm;

        /// <summary>The fingers come under the grip and push it up, or over it and push it down.</summary>
        public void PokeStroke(out Vector3 from, out Vector3 to)
        {
            bool on = On;
            var tip = Tip(on ? OnAngle : OffAngle) + Vector3.forward * 0.02f;
            from = transform.TransformPoint(tip + (on ? Vector3.up : Vector3.down) * 0.03f);
            to = transform.TransformPoint(Tip(on ? 90f : 90f) + Vector3.forward * 0.02f);
        }

        public void Press()
        {
            bool on = !On;
            PowerGrid.Set(_circuit, on);
            if (!_fx) _fx = ItemModels.Source(gameObject, 8f);
            _fx.pitch = on ? 0.55f : 0.5f;
            _fx.PlayOneShot(SoundBank.SwitchClick, 1.4f * SoundCatalog.Volume(SoundIds.Switches));
        }

        public Vector3 FocusPoint => transform.TransformPoint(Pivot + Vector3.forward * 0.03f);
    }
}
