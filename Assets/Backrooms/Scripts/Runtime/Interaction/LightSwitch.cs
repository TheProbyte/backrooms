using System.Collections.Generic;
using Backrooms.Audio;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// A wall light switch (its plate is baked into the chunk; this is the toggle and a trigger to click). Click it within
    /// reach and the hand reaches out and flicks the toggle with a finger: down switches off the ceiling lights of the room
    /// it is in (in this chunk, <see cref="LightZones"/>), up switches them on again. Every switch of the same room shows
    /// the same position. Frame: origin at the middle of the plate's back, +Z out of the wall, +Y up.
    /// </summary>
    public class LightSwitch : MonoBehaviour, IPokeTarget
    {
        // The toggle as modelled is up; it turns about the plate's horizontal axis to the mirrored down position.
        static readonly Vector3 Pivot = new Vector3(0f, 0.0005f, 0f);
        static readonly Vector3 TipUp = new Vector3(0f, 0.0114f, 0.0166f);
        const float Throw = 66.6f;

        static readonly List<LightSwitch> s_all = new List<LightSwitch>();

        Vector3Int _chunk;
        int _room;
        Transform _toggle;
        float _angle, _want;
        AudioSource _fx;

        public Vector3 PokeNormal => transform.forward;
        bool Off => LightZones.IsOff(_chunk, _room);

        public void Init(Vector3Int chunk, int room, uint renderMask)
        {
            _chunk = chunk; _room = room;
            var mesh = WallPlateModels.ToggleMesh;
            if (mesh)
            {
                var pivot = new GameObject("Toggle").transform;
                pivot.SetParent(transform, false);
                pivot.localPosition = Pivot;
                var model = new GameObject("Model");
                model.transform.SetParent(pivot, false);
                model.transform.localPosition = -Pivot;
                model.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = model.AddComponent<MeshRenderer>();
                r.sharedMaterial = WallPlateModels.ToggleMaterial;
                r.renderingLayerMask = renderMask;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                _toggle = pivot;
            }
            _want = _angle = Off ? Throw : 0f;
            Pose();
        }

        void OnEnable() => s_all.Add(this);
        void OnDisable() => s_all.Remove(this);

        void Pose() { if (_toggle) _toggle.localRotation = Quaternion.Euler(_angle, 0f, 0f); }

        void Update()
        {
            if (Mathf.Approximately(_angle, _want)) return;
            _angle = Mathf.MoveTowards(_angle, _want, Time.deltaTime * Throw / 0.05f);   // a snap, not a slide
            Pose();
        }

        /// <summary>The fingertip goes over the top of the toggle and pushes it down (or under it and up).</summary>
        public void PokeStroke(out Vector3 from, out Vector3 to)
        {
            Vector3 up = TipUp + Vector3.forward * 0.006f + Vector3.up * 0.012f;
            Vector3 down = new Vector3(0f, -TipUp.y, TipUp.z) + Vector3.forward * 0.006f - Vector3.up * 0.012f;
            bool off = Off;
            from = transform.TransformPoint(off ? down : up);
            to = transform.TransformPoint(off ? up - Vector3.up * 0.016f : down + Vector3.up * 0.016f);
        }

        public void Press()
        {
            bool off = !Off;
            LightZones.Set(_chunk, _room, off);
            foreach (var s in s_all) if (s._chunk == _chunk && s._room == _room) s._want = off ? Throw : 0f;
            if (!_fx) _fx = ItemModels.Source(gameObject, 6f);
            _fx.pitch = off ? 0.92f : 1.05f;
            _fx.PlayOneShot(SoundBank.SwitchClick, 0.9f * SoundCatalog.Volume(SoundIds.Switches));
        }

        /// <summary>The middle of the plate's face (for the focus marks).</summary>
        public Vector3 FocusPoint => transform.TransformPoint(new Vector3(0f, 0f, 0.01f));
    }
}
