using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Backrooms.Destruction
{
    /// <summary>
    /// A broken wall panel in the world: what is left of it (its chunks no longer draw it), its colliders, its shadow
    /// (so real lights shine through its holes exactly), and the rubble that has come to rest from it. Made by the
    /// <see cref="WallBreaker"/> while a chunk on either side of it is loaded; rebuilt whenever it is struck.
    /// </summary>
    public sealed class BrokenPanel : MonoBehaviour
    {
        public static readonly Dictionary<WallKey, BrokenPanel> Live = new Dictionary<WallKey, BrokenPanel>();

        public WallKey Key { get; private set; }
        public WallDamage.Panel Panel { get; private set; }
        MeshFilter _view, _shadow, _rubble;
        MeshRenderer _viewR, _shadowR, _rubbleR;
        Mesh _viewMesh, _shadowMesh, _rubbleMesh;
        GameObject _cols;
        readonly List<BoxCollider> _boxes = new List<BoxCollider>();
        int _built = -1, _rubbleBuilt = -1;
        float _rubbleDue;
        static PanelMesh s_mb, s_part;
        static readonly List<(Vector3 c, Vector3 s)> s_boxes = new List<(Vector3, Vector3)>();
        static readonly List<Vector3> s_cv = new List<Vector3>();
        static readonly List<int> s_ct = new List<int>();
        Mesh _boardMesh;
        MeshCollider _boardCollider;
        float _unseenSince, _nextTidy;
        /// <summary>Seconds the rubble must have been out of sight before its crumbs are tidied away (WallDamage.Tidy).</summary>
        public const float TidyAfter = 20f;
        Material[] _slots, _shadowSlots;

        public static BrokenPanel Spawn(WallKey key, WallDamage.Panel p, Transform parent)
        {
            if (Live.TryGetValue(key, out var have) && have) return have;
            var go = new GameObject($"BrokenWall_{(key.vertical ? "x" : "z")}{key.line}_{key.along}_s{key.storey}");
            go.transform.SetParent(parent, false);
            go.transform.position = key.Origin(WallDamage.Pitch);
            var b = go.AddComponent<BrokenPanel>();
            b.Key = key; b.Panel = p;
            b.Init();
            Live[key] = b;
            return b;
        }

        MeshRenderer Child(string name, out MeshFilter f, ShadowCastingMode shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            f = go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = shadows;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.renderingLayerMask = Panel.model.mask[0] | Panel.model.mask[1] | 1u;
            return r;
        }

        void Init()
        {
            _viewR = Child("Wall", out _view, ShadowCastingMode.Off);
            _shadowR = Child("Shadow", out _shadow, ShadowCastingMode.ShadowsOnly);
            _shadowR.renderingLayerMask = 0xFFFFFFFFu;                 // every light's shadows see it (as the walls' shadow boxes)
            _rubbleR = Child("Rubble", out _rubble, ShadowCastingMode.Off);
            _cols = new GameObject("Colliders");
            _cols.transform.SetParent(transform, false);
            _cols.isStatic = true;
            _slots = PanelGeometry.Materials(Panel.model);
            _shadowSlots = new Material[PanelGeometry.Slots];
            _shadowSlots[0] = DemolitionMaterials.GypsumEdge;
            Rebuild();
            RebuildRubble();
        }

        void OnDestroy()
        {
            if (Live.TryGetValue(Key, out var b) && b == this) Live.Remove(Key);
            if (_viewMesh) Destroy(_viewMesh);
            if (_shadowMesh) Destroy(_shadowMesh);
            if (_rubbleMesh) Destroy(_rubbleMesh);
            if (_boardMesh) Destroy(_boardMesh);
        }

        void LateUpdate()
        {
            // Scripts reloaded in Play Mode (the registry is gone): this copy is stale; the wall is made again as its chunk loads.
            if (Panel == null) { Destroy(gameObject); return; }
            if (Panel.state.version != _built) Rebuild();
            // Out of sight a while: the crumbs are swept away (never where you are looking).
            if (_rubbleR.isVisible || Panel.rubble.Count == 0) _unseenSince = Time.time;
            else if (Time.time - _unseenSince > TidyAfter && Time.time > _nextTidy) { _nextTidy = Time.time + 10f; WallDamage.Tidy(Panel); }
            if (Panel.rubbleVersion != _rubbleBuilt && Time.time >= _rubbleDue) { _rubbleDue = Time.time + 0.25f; RebuildRubble(); }
        }

        /// <summary>Meshes and colliders from what is left.</summary>
        public void Rebuild()
        {
            _built = Panel.state.version;
            float pitch = WallDamage.Pitch;
            var origin = Key.Origin(pitch);
            transform.position = origin;
            s_mb ??= new PanelMesh(PanelGeometry.Slots);
            s_mb.Clear(); s_mb.origin = origin;
            PanelGeometry.View(s_mb, Panel.model, Panel.state, pitch);
            _viewMesh = s_mb.Apply(_viewMesh, _slots, out var used, "BrokenWall");
            _view.sharedMesh = _viewMesh; _viewR.sharedMaterials = used;

            s_mb.Clear(); s_mb.origin = origin;
            PanelGeometry.Shadow(s_mb, Panel.model, Panel.state, pitch);
            _shadowMesh = s_mb.Apply(_shadowMesh, _shadowSlots, out var usedShadow, "BrokenWallShadow");
            _shadow.sharedMesh = _shadowMesh; _shadowR.sharedMaterials = usedShadow;

            PanelGeometry.Colliders(s_boxes, Panel.model, Panel.state, pitch);
            int i = 0;
            for (; i < s_boxes.Count; i++)
            {
                if (i >= _boxes.Count) _boxes.Add(_cols.AddComponent<BoxCollider>());
                var b = _boxes[i];
                b.enabled = true;
                b.center = s_boxes[i].c - origin; b.size = s_boxes[i].s;
            }
            for (; i < _boxes.Count; i++) if (_boxes[i]) _boxes[i].enabled = false;

            // The boards: one mesh collider the shape of the pieces left.
            PanelGeometry.BoardCollider(s_cv, s_ct, Panel.model, Panel.state, pitch, origin);
            if (!_boardMesh) _boardMesh = new Mesh { name = "BrokenWallCollider", hideFlags = HideFlags.DontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _boardMesh.Clear();
            _boardMesh.SetVertices(s_cv); _boardMesh.SetTriangles(s_ct, 0, true);
            if (!_boardCollider) _boardCollider = _cols.AddComponent<MeshCollider>();
            _boardCollider.sharedMesh = null;                                  // recooked with what is left
            _boardCollider.sharedMesh = s_ct.Count > 0 ? _boardMesh : null;
            _boardCollider.enabled = s_ct.Count > 0;
        }

        /// <summary>Everything that came to rest from this wall, as one mesh.</summary>
        void RebuildRubble()
        {
            _rubbleBuilt = Panel.rubbleVersion;
            float pitch = WallDamage.Pitch;
            var origin = Key.Origin(pitch);
            s_mb ??= new PanelMesh(PanelGeometry.Slots);
            s_part ??= new PanelMesh(PanelGeometry.Slots);
            s_mb.Clear(); s_mb.origin = origin;
            foreach (var r in Panel.rubble)
            {
                if (!WallDamage.Fits(Panel.model, r)) continue;
                s_part.Clear();
                s_part.origin = Part(s_part, Panel.model, r.part, r.side, r.index, r.span, pitch);
                s_part.origin = Vector3.zero;          // the part was built round its own centre: place it by its pose
                s_mb.Append(s_part, r.position, r.rotation);
            }
            if (s_mb.Empty) { _rubble.sharedMesh = null; return; }
            _rubbleMesh = s_mb.Apply(_rubbleMesh, _slots, out var used, "Rubble");
            _rubble.sharedMesh = _rubbleMesh; _rubbleR.sharedMaterials = used;
        }

        /// <summary>
        /// Builds one loose part round its own centre (its mesh's origin) and returns that centre in the world (where it
        /// was in the wall). Board pieces, baseboards, stud lengths, batts.
        /// </summary>
        public static Vector3 Part(PanelMesh mb, WallPanelModel m, Fallen.Part part, int side, int index, Vector2 span, float pitch)
        {
            Vector3 centre;
            switch (part)
            {
                case Fallen.Part.Board: centre = PanelGeometry.PieceOrigin(m, side, index, pitch); break;
                case Fallen.Part.Baseboard: centre = PanelGeometry.BaseboardOrigin(m, side, index, pitch); break;
                case Fallen.Part.Stud: centre = PanelGeometry.StudOrigin(m, index, span, pitch); break;
                default: centre = PanelGeometry.BattOrigin(m, index / m.rows, index % m.rows, pitch); break;
            }
            mb.origin = centre;
            switch (part)
            {
                case Fallen.Part.Board: PanelGeometry.BoardPiece(mb, m, null, side, index, pitch, true); break;
                case Fallen.Part.Baseboard: PanelGeometry.Baseboard(mb, m, side, index, pitch, true, true); break;
                case Fallen.Part.Stud: PanelGeometry.StudLength(mb, m, index, span, pitch, true, true); break;
                default: PanelGeometry.BattLength(mb, m, index / m.rows, index % m.rows, pitch, true, true); break;
            }
            return centre;
        }
    }
}
