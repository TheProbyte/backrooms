using Backrooms.Camcorder;
using Backrooms.CustomRooms;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.Interaction
{
    /// <summary>
    /// The clients' staff doors (2026-11-09, the owner: "there should be a closed locked door in all realworld rooms because
    /// how do people get in and out to take and put boxes there for overflow crew"): the StaffDoor model on each room's east
    /// wall beside the DELIVERIES bay (SeamRooms.DoorZ). It is always locked to you: clicked, the lever goes down a little
    /// way and stops, the latch rattles in its keeper, and a line says whose people use it. Their people come through it
    /// while nobody is in the room, to bring the next lot and take returned boxes away (Contracts.Batches).
    /// </summary>
    public class StaffDoors : MonoBehaviour
    {
        const float Reach = 2.4f, Radius = 0.75f;
        static AudioClip s_rattle;
        float _next;

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || CamcorderPlayerController.InputBlocked || Time.time < _next) return;
            var cam = Camera.main;
            if (!cam || !SeamPlan.InZone(cam.transform.position)) return;
            int kind = SeamPlan.KindAt(cam.transform.position);
            if (kind < 0 || kind > 3) return;
            var door = SeamPlan.World(kind, SeamRooms.DoorX, SeamRooms.DoorZ, 1.05f);
            if (!Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, Reach, ~0, QueryTriggerInteraction.Ignore)) return;
            var d = hit.point - door; d.y *= 0.5f;                           // (a tall target: the whole leaf counts)
            if (d.magnitude > Radius) return;
            _next = Time.time + 0.8f;
            Audio.WorldSound.PlayAt(Rattle(), door + Vector3.left * 0.1f, 0.35f, 10f, Random.Range(0.94f, 1.06f));
            CamcorderOSD.Notice(Backrooms.UI.Words.Get("line.staff_door", Contracts.Clients[kind]));
        }

        /// <summary>A lever pressed against a locked latch: a clack, the latch rattling in its keeper twice, the lever back.</summary>
        static AudioClip Rattle()
        {
            if (s_rattle) return s_rattle;
            int sr = 44100, n = sr * 7 / 10;
            var data = new float[n];
            var rng = new System.Random(7);
            void Knock(float at, float amp, float freq, float decay)
            {
                int s0 = (int)(at * sr);
                for (int i = 0; i < sr / 8 && s0 + i < n; i++)
                {
                    float t = i / (float)sr;
                    float noise = (float)(rng.NextDouble() * 2 - 1);
                    data[s0 + i] += amp * Mathf.Exp(-t * decay) * (0.6f * Mathf.Sin(2f * Mathf.PI * freq * t) + 0.4f * noise);
                }
            }
            Knock(0.00f, 0.55f, 1900f, 70f);           // the lever stops
            Knock(0.09f, 0.40f, 2600f, 90f);           // the latch in its keeper
            Knock(0.15f, 0.30f, 2400f, 95f);
            Knock(0.26f, 0.35f, 2700f, 90f);
            Knock(0.33f, 0.22f, 2300f, 100f);
            Knock(0.52f, 0.30f, 1500f, 60f);           // the lever springs back
            s_rattle = AudioClip.Create("StaffDoorRattle", n, 1, sr, false);
            s_rattle.SetData(data, 0);
            return s_rattle;
        }
    }
}
