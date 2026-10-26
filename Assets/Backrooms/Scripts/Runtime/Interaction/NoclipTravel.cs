using System.Collections;
using Backrooms.Audio;
using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// Walking through noclip doors (<see cref="NoclipPassage"/>).
    ///  - Near a door, the place it leads to is kept loaded (a streaming site), so it is all there before you arrive.
    ///  - Walking up to the wall, the camcorder's picture starts to break up (tracking errors, tearing); as the camera
    ///    goes into the wall it is at its worst: the picture rolls, frames drop (the last good one is held) and the
    ///    microphone cuts out and crackles. The player is moved to the other door's face during the held frames, and the
    ///    picture recovers in the new zone. Everything left behind unloads as the streamer follows the player, unseen.
    ///  - A Level 0 door leads to its Poolrooms door; a Poolrooms door leads back to the door you came in by
    ///    (<see cref="Origin"/>, saved with the world), or to the spawn if there is none.
    /// The glitch is the camcorder's own (CamcorderRig.Glitch drives CamcorderVideo.shader, CamcorderFrameHold.Freeze
    /// holds the frame, CamcorderAudioChain.dropout breaks the sound).
    /// </summary>
    public class NoclipTravel : MonoBehaviour
    {
        public static NoclipTravel Instance { get; private set; }
        /// <summary>Where a Poolrooms door leads back to: the face of the door you came in by (position on the wall line, out of that face).</summary>
        public static bool HasOrigin;
        public static Vector3 OriginPosition, OriginNormal;

        [Tooltip("Distance (m) at which a door's destination starts loading.")] public float preloadDistance = 16f;
        [Tooltip("Distance (m) from the wall at which the picture starts to break up.")] public float glitchDistance = 0.8f;

        public WorldStreamer streamer;
        CamcorderPlayerController _player;
        CamcorderRig _rig;
        AudioSource _crackle;
        NoclipPassage _watch;
        float _prevDist;
        bool _busy;
        float _glitch, _approach, _roll, _rollSpeed, _drop;
        object _site = new object();

        void Awake() { Instance = this; }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CamcorderRig.Glitch = 0f; CamcorderRig.GlitchRoll = 0f;
            CamcorderFrameHold.Freeze = false;
        }

        void Start()
        {
            if (!streamer) streamer = GetComponent<WorldStreamer>();
            _player = streamer && streamer.player ? streamer.player.GetComponent<CamcorderPlayerController>() : null;
            _crackle = gameObject.AddComponent<AudioSource>();
            _crackle.playOnAwake = false; _crackle.spatialBlend = 0f; _crackle.volume = 0.7f;
        }

        /// <summary>Where walking through a door from one of its sides leads: the face you come out of (wall line point, out of the face).</summary>
        public bool Destination(NoclipPassage door, out Vector3 pos, out Vector3 normal)
        {
            pos = normal = default;
            if (!door.arrival)
            {
                if (!door.hasPartner) return false;
                pos = door.partnerCentre; normal = door.partnerNormal;
                return true;
            }
            if (HasOrigin) { pos = OriginPosition; normal = OriginNormal; return true; }
            if (!streamer) return false;
            streamer.SpawnPose(out var sp, out float yaw);
            normal = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            pos = sp - normal * 0.45f;
            return true;
        }

        void Update()
        {
            var cam = Camera.main;
            if (!cam || !_player || !streamer) return;
            Vector3 eye = cam.transform.position;

            // The nearest door (by its wall line) within reach of the preload.
            NoclipPassage near = null; float best = preloadDistance;
            foreach (var d in NoclipPassage.All)
            {
                float dist = Vector3.Distance(new Vector3(eye.x, d.centre.y, eye.z), d.centre);
                if (dist < best && Mathf.Abs(eye.y - d.centre.y) < 4f) { best = dist; near = d; }
            }
            if (near && Destination(near, out var dp, out var dn)) streamer.SetSite(_site, dp + dn * 3f, 10f);
            else streamer.ClearSite(_site);

            float approach = 0f;
            if (near && !_busy)
            {
                float s = near.Distance(eye, out bool inOpening);
                if (inOpening)
                {
                    approach = Mathf.Clamp01(1f - Mathf.Abs(s) / glitchDistance);
                    approach *= approach;
                    // Go before the camera can see into the wall: as soon as its near plane would reach the wall's face
                    // (the frame is held from then on, so nothing behind the wall is ever seen).
                    float face = OfficeBiome.WallThickness * 0.5f + cam.nearClipPlane + 0.08f;
                    bool closing = _watch == near && Mathf.Abs(s) < Mathf.Abs(_prevDist) - 1e-5f;
                    bool crossed = _watch == near && Mathf.Sign(s) != Mathf.Sign(_prevDist) && Mathf.Abs(_prevDist) < 0.6f;
                    if ((closing && Mathf.Abs(s) < face) || crossed)
                        StartCoroutine(Go(near, crossed ? (_prevDist > 0f ? 1f : -1f) : Mathf.Sign(s)));
                }
                _watch = near; _prevDist = s;
            }
            else if (!_busy) _watch = null;
            _approach = Mathf.MoveTowards(_approach, approach, Time.unscaledDeltaTime * 4f);
            Apply();
        }

        void Apply()
        {
            float dt = Time.unscaledDeltaTime;
            float g = Mathf.Max(_glitch, _approach * 0.65f);
            // The picture rolls while it is broken, and settles back to frame as it recovers.
            if (g > 0.5f) _rollSpeed = Mathf.MoveTowards(_rollSpeed, 2.4f * g, dt * 6f);
            else _rollSpeed = Mathf.MoveTowards(_rollSpeed, 0f, dt * 3f);
            _roll = Mathf.Repeat(_roll + _rollSpeed * dt, 1f);
            if (_rollSpeed < 0.05f) _roll = Mathf.MoveTowards(_roll > 0.5f ? _roll - 1f : _roll, 0f, dt * 1.5f);
            CamcorderRig.Glitch = g;
            CamcorderRig.GlitchRoll = Mathf.Repeat(_roll, 1f);
            if (!_rig && Camera.main) _rig = Camera.main.GetComponent<CamcorderRig>();
            var chain = _rig ? _rig.Audio : null;
            if (chain) chain.dropout = Mathf.Max(_drop, _approach * 0.35f);
        }

        IEnumerator Go(NoclipPassage door, float fromSide)
        {
            if (!Destination(door, out var to, out var toNormal)) yield break;
            _busy = true;
            // Into the wall: the tape breaks up, frames drop, the mic cuts out.
            _glitch = 1f; _drop = 1f;
            _crackle.PlayOneShot(SoundBank.NoclipCrackle, 0.8f);
            CamcorderFrameHold.Freeze = true;
            yield return null;

            // Across: the same place relative to the other door's face, turned to come out of it.
            Vector3 travel = -door.normal * fromSide;
            Vector3 p = _player.transform.position;
            Vector3 r1 = Vector3.Cross(Vector3.up, travel), r2 = Vector3.Cross(Vector3.up, toNormal);
            float lateral = Mathf.Clamp(Vector3.Dot(p - door.centre, r1), -0.3f, 0.3f);
            Vector3 arrive = to + toNormal * 0.5f + r2 * lateral + Vector3.up * Mathf.Max(0.02f, p.y - door.centre.y);
            float yawDelta = Vector3.SignedAngle(travel, toNormal, Vector3.up);
            if (!door.arrival) { HasOrigin = true; OriginPosition = door.centre; OriginNormal = door.normal * fromSide; }
            _player.Warp(arrive, yawDelta);
            _watch = null;

            // Hold the last frame until the floor there is loaded (it normally already is), then a moment more.
            float wait = 0f;
            while (wait < 3f && streamer.LayoutAt(WorldStreamer.ChunkOf(arrive)) == null) { wait += Time.unscaledDeltaTime; yield return null; }
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.18f) yield return null;
            CamcorderFrameHold.Freeze = false;

            // The picture comes back: rolling and tearing settle over a second; the sound returns first.
            float t = 0f;
            while (t < 1.1f)
            {
                t += Time.unscaledDeltaTime;
                _glitch = Mathf.Lerp(1f, 0f, Mathf.SmoothStep(0f, 1f, t / 1.1f));
                _drop = Mathf.Lerp(1f, 0f, Mathf.Clamp01(t / 0.4f));
                yield return null;
            }
            _glitch = 0f; _drop = 0f;
            _busy = false;
        }

        /// <summary>
        /// A thrown (or falling) item goes through a noclip door like the player does: as it reaches the wall inside the
        /// opening, it is moved to the same place at the other door's face, its motion turned with it (with a faint
        /// crackle where it went in). Called from <see cref="WorldItem"/>'s FixedUpdate. Items with a cord out (a plug,
        /// a lamp or extension cord in use) stay on this side: the cord would stretch between zones.
        /// </summary>
        public static void ItemStep(WorldItem item)
        {
            var inst = Instance;
            if (!inst || !item || item.Held) return;
            var body = item.Body;
            Vector3 v = body.linearVelocity;
            if (v.sqrMagnitude < 0.2f) return;
            if (item is Plug || (item is PowerUser pu && pu.Plug)) return;
            Vector3 c = body.worldCenterOfMass;
            float T = OfficeBiome.WallThickness;
            foreach (var d in NoclipPassage.All)
            {
                if (!d || (c - d.centre).sqrMagnitude > 16f) continue;
                float s = d.Distance(c, out bool inOpening, -0.04f);
                if (!inOpening) continue;
                float vn = Vector3.Dot(v, d.normal);
                if (s * vn >= 0f) continue;                                     // moving away from the wall
                float extent = 0.05f;
                foreach (var col in item.GetComponentsInChildren<Collider>()) if (col.enabled && !col.isTrigger) extent = Mathf.Max(extent, Vector3.Dot(col.bounds.extents, new Vector3(Mathf.Abs(d.normal.x), Mathf.Abs(d.normal.y), Mathf.Abs(d.normal.z))));
                float reach = T * 0.5f + extent + Mathf.Abs(vn) * Time.fixedDeltaTime * 1.5f + 0.02f;
                if (Mathf.Abs(s) > reach) continue;
                if (!inst.Destination(d, out var to, out var toNormal)) return;
                Vector3 travel = -d.normal * Mathf.Sign(s);
                Quaternion turn = Quaternion.AngleAxis(Vector3.SignedAngle(travel, toNormal, Vector3.up), Vector3.up);
                Vector3 r1 = Vector3.Cross(Vector3.up, travel), r2 = Vector3.Cross(Vector3.up, toNormal);
                float lateral = Mathf.Clamp(Vector3.Dot(c - d.centre, r1), -d.Width * 0.5f + extent, d.Width * 0.5f - extent);
                Vector3 cOut = to + toNormal * (T * 0.5f + extent + 0.03f) + r2 * lateral + Vector3.up * (c.y - d.centre.y);
                AudioSource.PlayClipAtPoint(SoundBank.NoclipCrackle, c, 0.35f);
                // Turn it about its centre and move it across.
                Vector3 pivotOffset = body.position - c;
                Quaternion rot = turn * body.rotation;
                Vector3 pos = cOut + turn * pivotOffset;
                body.position = pos; body.rotation = rot;
                item.transform.SetPositionAndRotation(pos, rot);
                body.linearVelocity = turn * v;
                body.angularVelocity = turn * body.angularVelocity;
                return;
            }
        }

        /// <summary>For the F5 debug places: a noclip door's face to stand in front of.</summary>
        public static void FaceOf(NoclipPassage d, out Vector3 standAt, out Vector3 lookAt)
        {
            standAt = d.centre + d.normal * 1.4f + Vector3.up * 0.05f;
            lookAt = d.centre + Vector3.up * 1.2f;
        }
    }
}
