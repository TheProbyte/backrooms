using Backrooms.Net;
using UnityEngine;

namespace Backrooms.DebugTools
{
    /// <summary>
    /// Another player with nobody behind it (2026-11-09): a RemotePlayer fed made-up updates twenty times a second, so the
    /// way friends look (walking, running, crouching, crawling, turning on the spot, in the air, sliding, lying on a cot,
    /// sitting) can be looked at and photographed on one machine with no co-op session.
    /// <c>RemotePuppet.Make(position, yaw)</c>, then set <see cref="mode"/>.
    /// </summary>
    public class RemotePuppet : MonoBehaviour
    {
        public enum Mode { Idle, Walk, Run, CrouchWalk, Crawl, Lie, Turn, Air, Slide, Sit, CrouchStill }
        public Mode mode;
        public Vector3 origin;
        public float yaw;
        /// <summary>Freezes the walk where it is (for a picture mid-stride).</summary>
        public bool hold;
        RemotePlayer _rp;
        float _next, _along, _dir = 1f, _spin;

        public static RemotePuppet Make(Vector3 at, float yaw)
        {
            var go = new GameObject("RemotePuppet");
            var p = go.AddComponent<RemotePuppet>();
            p.origin = at; p.yaw = yaw;
            p._rp = RemotePlayer.Create(3, go.transform);
            return p;
        }

        public RemotePlayer Player => _rp;

        void Update()
        {
            if (!_rp) return;
            float dt = Time.deltaTime;
            float speed = mode == Mode.Walk ? 1.5f : mode == Mode.Run ? 4.2f : mode == Mode.CrouchWalk ? 0.8f : mode == Mode.Crawl ? 0.45f : mode == Mode.Slide ? 3f : 0f;
            if (!hold) _along += speed * _dir * dt;
            // Up and down a 3 m line, turning round at each end.
            if (_along > 3f) { _along = 3f; _dir = -1f; } else if (_along < 0f) { _along = 0f; _dir = 1f; }
            if (mode == Mode.Turn) _spin += 90f * dt;
            float y = yaw + (_dir < 0f ? 180f : 0f) + _spin;
            var rot = Quaternion.Euler(0f, y, 0f);
            Vector3 pos = origin + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * _along;
            if (Time.time < _next) return;
            _next = Time.time + 0.05f;
            byte flags = RemotePlayer.FGround;
            float eye = 1.58f;
            Vector3 seatHip = Vector3.zero;
            switch (mode)
            {
                case Mode.Run: flags |= RemotePlayer.FRun; break;
                case Mode.CrouchWalk: case Mode.CrouchStill: flags |= 1; eye = 1.02f; break;
                case Mode.Crawl: flags |= 2; eye = 0.42f; break;
                case Mode.Lie: flags |= RemotePlayer.FSeated; eye = 0.25f; seatHip = new Vector3(0f, -0.05f, 0.55f); pos += Vector3.up * 0.47f; break;
                case Mode.Sit: flags |= RemotePlayer.FSeated; eye = 1.2f; seatHip = new Vector3(0f, 0.46f, -0.05f); break;
                case Mode.Air: flags = 0; pos += Vector3.up * 0.4f; break;
                case Mode.Slide: flags |= 1; flags |= RemotePlayer.FSlide; eye = 0.92f; break;
            }
            _rp.Push(new RemotePlayer.State
            {
                t = Time.realtimeSinceStartupAsDouble, pos = pos, yaw = y, pitch = mode == Mode.Lie ? -78f : 4f, eye = eye, flags = flags,
                vel = hold ? Vector3.zero : rot * Vector3.forward * speed, hand = new Vector3(0.24f, 0.86f, 0.05f), handBlend = 0f,
                itemPos = Vector3.zero, itemRot = Quaternion.identity, seatHip = seatHip,
            });
        }
    }
}
