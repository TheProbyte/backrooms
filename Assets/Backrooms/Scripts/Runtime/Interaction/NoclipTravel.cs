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
        float _glitch, _approach, _roll, _rollSpeed, _drop, _wash, _snow;
        /// <summary>When the last trip through began (unscaled time): one soon after is quicker still.</summary>
        float _lastTrip = -100f;
        object _site = new object();
        readonly object _home = new object();
        /// <summary>The backrooms kept round a seam while you are in its room: the player's own square and one ring more
        /// (you come out a step from the door, often in the next chunk), never past what is kept round the player anyway.</summary>
        int HomeRings => Mathf.Min(streamer.loadRadius + 1, streamer.unloadRadius);

        void Awake() { Instance = this; }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CamcorderRig.Glitch = 0f; CamcorderRig.GlitchRoll = 0f;
            CamcorderRig.Wash = 0f; CamcorderRig.Snow = 0f;
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
                if (d.seam && NoclipPassage.SeamsShut) continue;      // (closing time: the tears are shut)
                float dist = Vector3.Distance(new Vector3(eye.x, d.centre.y, eye.z), d.centre);
                if (dist < best && Mathf.Abs(eye.y - d.centre.y) < 4f) { best = dist; near = d; }
            }
            // (A seam's rooms: the whole block of them, as standing in one loads its neighbours too; else they were built
            // again on every trip. 2026-11-09.)
            if (near && Destination(near, out var dp, out var dn)) streamer.SetSite(_site, dp + dn * 3f, SeamPlan.InZone(dp) ? 2f * OfficeBiome.ChunkSize : 10f);
            else streamer.ClearSite(_site);
            // In a seam's room, the backrooms round the door you came through stay loaded, as a second streaming source
            // (as Unreal's World Partition does round a teleport's other end): going back is instant, nothing is rebuilt.
            // (2026-11-09, the owner: "laggy ... looks like it's reloading the whole backrooms every time".)
            // (And still while you are back on the floor near that seam, so going to and fro builds nothing at all.)
            if (HasOrigin && (SeamPlan.InZone(_player.transform.position)
                              || (_player.transform.position - OriginPosition).sqrMagnitude < preloadDistance * preloadDistance))
                streamer.SetArea(_home, OriginPosition, HomeRings);
            else streamer.ClearSite(_home);

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
                        Begin(near, crossed ? (_prevDist > 0f ? 1f : -1f) : Mathf.Sign(s));
                }
                _watch = near; _prevDist = s;
                // (2026-11-09, the owner: going in "super slow ... if you stand in between, you end up falling out of the map".)
                // The body, not only the eye: in the opening and into the wall's thickness, or past it, it goes through,
                // whichever way it is facing or moving.
                float b = near.Distance(_player.transform.position + Vector3.up * 0.9f, out bool bodyIn, 0f);
                if (!_busy && bodyIn)
                {
                    bool inWall = Mathf.Abs(b) < OfficeBiome.WallThickness * 0.5f + 0.05f;
                    bool bodyCrossed = _bodyWatch == near && Mathf.Sign(b) != Mathf.Sign(_prevBody) && Mathf.Abs(_prevBody) < 0.6f;
                    if (inWall || bodyCrossed)
                    {
                        float side = _bodyWatch == near && Mathf.Abs(_prevBody) > 1e-4f ? Mathf.Sign(_prevBody) : Mathf.Sign(s);
                        Begin(near, side);
                    }
                }
                _bodyWatch = near; _prevBody = b;
                if (bodyIn) { _lastDoor = near; _lastSide = Mathf.Sign(b); }
            }
            else if (!_busy) { _watch = null; _bodyWatch = null; }
            Rescue();
            _approach = Mathf.MoveTowards(_approach, approach, Time.unscaledDeltaTime * 4f);
            Apply();
        }

        NoclipPassage _bodyWatch, _lastDoor;
        float _prevBody, _lastSide = 1f;
        Coroutine _go;
        Vector3 _arrivedAt; float _arrivedYaw, _arrivedTime = -100f;

        /// <summary>Through this door from this side (a trip already easing out is cut short: its picture carries on from
        /// the new one's white).</summary>
        void Begin(NoclipPassage door, float side)
        {
            if (_busy) return;
            if (_go != null) StopCoroutine(_go);
            _go = StartCoroutine(Go(door, side));
        }

        /// <summary>
        /// The last safety net: fallen below the floor near a noclip door or seam (somehow through the gap behind one), the
        /// player is put back in front of the door on the side they were last on, as if they had stepped back out.
        /// </summary>
        void Rescue()
        {
            var p = _player.transform.position;
            // Just through (the last ten seconds) and fallen below where you came out: back there.
            if (Time.time - _arrivedTime < 10f && p.y < _arrivedAt.y - 1.5f)
            {
                _player.TeleportSafe(_arrivedAt + Vector3.up * 0.05f, _arrivedYaw);
                _bodyWatch = null; _watch = null;
                return;
            }
            if (_busy || !_lastDoor) return;
            if (p.y > _lastDoor.centre.y - 1.5f) return;
            var flat = p - _lastDoor.centre; flat.y = 0f;
            if (flat.sqrMagnitude > 64f) return;
            var n = _lastDoor.normal * _lastSide;
            _player.TeleportSafe(_lastDoor.centre + n * 0.9f + Vector3.up * 0.05f, Quaternion.LookRotation(n).eulerAngles.y);
            _bodyWatch = null; _watch = null;
        }

        void Apply()
        {
            float dt = Time.unscaledDeltaTime;
            // (2026-11-09: the way through is white-out and soft snow, CamcorderRig.Wash and Snow; the rolling, tearing
            // glitch is left for the old interference only.)
            CamcorderRig.Wash = Mathf.Max(_wash, _approach * 0.55f);
            CamcorderRig.Snow = _snow;
            float g = Mathf.Max(_glitch, Mathf.Clamp01(CamcorderRig.Interference));
            // The picture rolls while it is broken, and settles back to frame as it recovers.
            if (g > 0.5f) _rollSpeed = Mathf.MoveTowards(_rollSpeed, 2.4f * g, dt * 6f);
            else _rollSpeed = Mathf.MoveTowards(_rollSpeed, 0f, dt * 3f);
            _roll = Mathf.Repeat(_roll + _rollSpeed * dt, 1f);
            if (_rollSpeed < 0.05f) _roll = Mathf.MoveTowards(_roll > 0.5f ? _roll - 1f : _roll, 0f, dt * 1.5f);
            CamcorderRig.Glitch = g;
            CamcorderRig.GlitchRoll = Mathf.Repeat(_roll, 1f);
            if (!_rig && Camera.main) _rig = Camera.main.GetComponent<CamcorderRig>();
            var chain = _rig ? _rig.Audio : null;
            if (chain) chain.dropout = Mathf.Max(_drop, Mathf.Clamp01(CamcorderRig.Interference) * 0.5f);
        }

        IEnumerator Go(NoclipPassage door, float fromSide)
        {
            if (!Destination(door, out var to, out var toNormal)) yield break;
            _busy = true;
            // (2026-11-09, the owner, with a reference clip: "less loud and annoying if you're doing it a lot". Into the wall
            // the frame goes white; a moment of soft pale snow; a flash; and the room comes up out of white as the exposure
            // settles. Silent. About a second, the controls never taken,
            // and quicker again for a trip soon after the last.)
            bool again = Time.unscaledTime - _lastTrip < 30f;
            _lastTrip = Time.unscaledTime;
            // (No sound at all, 2026-11-09, the owner: "less hiss noise when going through. should be none really".)
            _wash = 1f; _drop = 0f;
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
            // (Before the jump, so the streamer never sees a moment with the player gone and the backrooms unwanted.)
            if (!door.arrival && SeamPlan.InZone(to)) streamer.SetArea(_home, door.centre, HomeRings);
            // And the place being left stays loaded across the jump: on the far side the nearest door is the one you came
            // out of, which keeps it (Update), but the streamer could look first and drop it for a frame (a seam's room
            // was built again on every trip back, 2026-11-09).
            streamer.SetSite(_site, door.centre, SeamPlan.InZone(door.centre) ? 2f * OfficeBiome.ChunkSize : 10f);
            // What the player carries comes along, where it was in their hands (a box out of a seam's place, 2026-11-07):
            // left to follow the hand on its own, it would be dragged from the other side of the jump.
            // (Every box in the arms or on the hand truck, 2026-11-09: PropGrabber.CarryAcross. The hand truck itself rides
            // on the player.)
            var grabber = _player.GetComponentInChildren<PropGrabber>();
            _player.Warp(arrive, yawDelta);
            if (grabber) grabber.CarryAcross(p, Quaternion.Euler(0f, yawDelta, 0f));
            _watch = null;

            // Hold the last frame until the floor there is loaded (it normally already is), then a moment more.
            float wait = 0f;
            while (wait < 3f && streamer.LayoutAt(WorldStreamer.ChunkOf(arrive)) == null) { wait += Time.unscaledDeltaTime; yield return null; }
            // (And until the floor there is solid: its colliders come a little after the layout. 2026-11-09.)
            while (wait < 4f && !Physics.Raycast(arrive + Vector3.up * 0.5f, Vector3.down, 2f, ~0, QueryTriggerInteraction.Ignore)) { wait += Time.unscaledDeltaTime; yield return null; }
            _arrivedAt = arrive; _arrivedYaw = _player.transform.eulerAngles.y; _arrivedTime = Time.time;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.05f) yield return null;
            CamcorderFrameHold.Freeze = false;
            // (The door watches you again from here, while the picture is still coming back: stepping straight back in goes
            // straight back through, never into the gap behind the wall. 2026-11-09.)
            _busy = false;
            _watch = null; _bodyWatch = null;

            // Soft snow over the washed-out picture, shorter for a trip soon after the last.
            float snowFor = again ? 0.12f : 0.3f, t = 0f;
            while (t < snowFor)
            {
                t += Time.unscaledDeltaTime;
                float u = t / snowFor;
                _snow = 0.85f * (1f - u * u);
                _wash = 0.35f;
                yield return null;
            }
            // A flash of white, then the room comes up out of it as the exposure settles.
            _snow = 0f; _drop = 0f; _wash = 1f;
            t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.05f) yield return null;
            float settle = again ? 0.6f : 0.9f;
            t = 0f;
            while (t < settle)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / settle);
                _wash = (1f - u) * (1f - u);                        // (quick at first, the last of it slow)
                yield return null;
            }
            _wash = 0f; _snow = 0f; _glitch = 0f; _drop = 0f;
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
            if (!item || item.Held) return;
            if (item is Plug || (item is PowerUser pu && pu.Plug)) return;
            BodyStep(item.Body, item.transform, item.gameObject);
        }

        /// <summary>A thrown box goes through a noclip door or a seam the same way (2026-11-09, the owner: "you can't throw
        /// boxes through seams"). Called from <see cref="MovableProp"/>'s FixedUpdate for boxes in flight.</summary>
        public static void PropStep(MovableProp prop)
        {
            if (!prop || prop.held || !prop.Body || prop.Body.isKinematic) return;
            if (BodyStep(prop.Body, prop.transform, prop.gameObject)) MovablePropManager.Instance?.Store(prop);
        }

        static bool BodyStep(Rigidbody body, Transform root, GameObject go)
        {
            var inst = Instance;
            if (!inst || !body) return false;
            Vector3 v = body.linearVelocity;
            if (v.sqrMagnitude < 0.2f) return false;
            Vector3 c = body.worldCenterOfMass;
            float T = OfficeBiome.WallThickness;
            foreach (var d in NoclipPassage.All)
            {
                if (!d || (c - d.centre).sqrMagnitude > 16f || (d.seam && NoclipPassage.SeamsShut)) continue;
                float s = d.Distance(c, out bool inOpening, -0.04f);
                if (!inOpening) continue;
                float vn = Vector3.Dot(v, d.normal);
                if (s * vn >= 0f) continue;                                     // moving away from the wall
                float extent = 0.05f;
                foreach (var col in go.GetComponentsInChildren<Collider>()) if (col.enabled && !col.isTrigger) extent = Mathf.Max(extent, Vector3.Dot(col.bounds.extents, new Vector3(Mathf.Abs(d.normal.x), Mathf.Abs(d.normal.y), Mathf.Abs(d.normal.z))));
                float reach = T * 0.5f + extent + Mathf.Abs(vn) * Time.fixedDeltaTime * 1.5f + 0.02f;
                if (Mathf.Abs(s) > reach) continue;
                if (!inst.Destination(d, out var to, out var toNormal)) return false;
                Vector3 travel = -d.normal * Mathf.Sign(s);
                Quaternion turn = Quaternion.AngleAxis(Vector3.SignedAngle(travel, toNormal, Vector3.up), Vector3.up);
                Vector3 r1 = Vector3.Cross(Vector3.up, travel), r2 = Vector3.Cross(Vector3.up, toNormal);
                float lateral = Mathf.Clamp(Vector3.Dot(c - d.centre, r1), -d.Width * 0.5f + extent, d.Width * 0.5f - extent);
                Vector3 cOut = to + toNormal * (T * 0.5f + extent + 0.03f) + r2 * lateral + Vector3.up * (c.y - d.centre.y);
                // Turn it about its centre and move it across.
                Vector3 pivotOffset = body.position - c;
                Quaternion rot = turn * body.rotation;
                Vector3 pos = cOut + turn * pivotOffset;
                body.position = pos; body.rotation = rot;
                root.SetPositionAndRotation(pos, rot);
                body.linearVelocity = turn * v;
                body.angularVelocity = turn * body.angularVelocity;
                return true;
            }
            return false;
        }

        /// <summary>For the F5 debug places: a noclip door's face to stand in front of.</summary>
        public static void FaceOf(NoclipPassage d, out Vector3 standAt, out Vector3 lookAt)
        {
            standAt = d.centre + d.normal * 1.4f + Vector3.up * 0.05f;
            lookAt = d.centre + Vector3.up * 1.2f;
        }
    }
}
