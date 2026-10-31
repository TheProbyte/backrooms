using UnityEngine;
using UnityEngine.InputSystem;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// Build mode, step D (round 17): WALK puts you in the room you are building, in first person, at once (no world to
    /// load): mouse looks, WASD walks (Shift: faster), Space hops, Esc back to building. The preview has the world's own
    /// walls, doors and colliders, so you walk through what you made. TEST IN WORLD is still there for the real thing.
    /// </summary>
    public partial class BuilderApp
    {
        bool _walking;
        CharacterController _walker;
        float _walkYaw, _walkPitch, _walkVy, _walkStep;
        BuilderView _walkFromView;
        float _walkFromFov;

        public bool Walking => _walking;

        void BeginWalk()
        {
            if (_walking || _bp == null) return;
            var v = V;
            // Start on the floor cell nearest the middle that nothing stands on.
            Vector2 best = new Vector2(v.width, v.depth) * C * 0.5f; float bestD = float.MaxValue;
            for (int z = 0; z < v.depth; z++)
            for (int x = 0; x < v.width; x++)
            {
                var c = new Vector2((x + 0.5f) * C, (z + 0.5f) * C);
                bool blocked = v.props.Exists(p => Vector2.Distance(new Vector2(p.x, p.z), c) < Mathf.Max(p.size.x, p.size.z) * 0.6f + 0.3f)
                               || v.pillars.Exists(p => Vector2.Distance(new Vector2(p.x * C, p.z * C), c) < p.size + 0.3f);
                float d = (c - new Vector2(v.width, v.depth) * C * 0.5f).sqrMagnitude;
                if (!blocked && d < bestD) { bestD = d; best = c; }
            }
            _walkFromView = _view; _walkFromFov = _cam.fieldOfView;
            if (_view != BuilderView.Solid) SetView(BuilderView.Solid);
            if (_needRebuild) Rebuild();
            var go = new GameObject("BuilderWalker");
            go.transform.position = W3(best.x, best.y, 0.05f);
            _walker = go.AddComponent<CharacterController>();
            _walker.height = 1.75f; _walker.radius = 0.28f; _walker.center = new Vector3(0f, 0.875f, 0f); _walker.stepOffset = 0.3f;
            _walkYaw = _yaw; _walkPitch = 0f; _walkVy = 0f;
            _walking = true;
            _selected = Selection.None; _group.Clear();
            _cam.fieldOfView = 68f;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            BuilderSounds.Play(BuilderSounds.Sfx.Place, 0.8f);
        }

        void EndWalk()
        {
            if (!_walking) return;
            _walking = false;
            if (_walker) Destroy(_walker.gameObject);
            _walker = null;
            _cam.fieldOfView = _walkFromFov;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            SetView(_walkFromView);
            BuilderSounds.Play(BuilderSounds.Sfx.Drop, 0.8f);
        }

        /// <summary>Each frame while walking (instead of the editor's input).</summary>
        void UpdateWalk()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb == null || mouse == null || !_walker) { EndWalk(); return; }
            if (kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) { EndWalk(); return; }
            var d = mouse.delta.ReadValue() * 0.09f;
            _walkYaw += d.x; _walkPitch = Mathf.Clamp(_walkPitch - d.y, -85f, 85f);
            float dt = Time.unscaledDeltaTime;
            var input = new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
            float speed = kb.leftShiftKey.isPressed ? 3.2f : 1.5f;
            var yawRot = Quaternion.Euler(0f, _walkYaw, 0f);
            var move = yawRot * new Vector3(input.x, 0f, input.y);
            if (move.sqrMagnitude > 1f) move.Normalize();
            if (_walker.isGrounded) { _walkVy = -1f; if (kb.spaceKey.wasPressedThisFrame) _walkVy = 3.2f; }
            else _walkVy -= 9.81f * dt;
            _walker.Move((move * speed + Vector3.up * _walkVy) * dt);
            // Out of the room's floor (a gap in the preview): back to where it started.
            if (_walker.transform.position.y < -3f) { _walker.enabled = false; _walker.transform.position = W3(V.width * C * 0.5f, V.depth * C * 0.5f, 0.05f); _walker.enabled = true; }
            // A soft step sound as you go.
            if (_walker.isGrounded && input.sqrMagnitude > 0.01f)
            {
                _walkStep += dt * speed / 0.7f;
                if (_walkStep > 1f) { _walkStep -= 1f; BuilderSounds.Play(BuilderSounds.Sfx.Tick, 0.45f + Random.value * 0.1f, 0.5f); }
            }
            float bob = input.sqrMagnitude > 0.01f && _walker.isGrounded ? Mathf.Sin(Time.unscaledTime * speed * 5.5f) * 0.012f : 0f;
            _cam.transform.SetPositionAndRotation(_walker.transform.position + Vector3.up * (1.58f + bob), Quaternion.Euler(_walkPitch, _walkYaw, 0f));
        }

        /// <summary>While walking: only a small line saying how to get back.</summary>
        void DrawWalkGui()
        {
            var r = new Rect(ImUi.Width * 0.5f - 260f, 20f, 520f, 34f);
            ImUi.Fill(r, new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(r, "WALKING  ·  WASD walk, Shift faster, Space hop  ·  Esc back to building", new GUIStyle(ImUi.Small) { alignment = TextAnchor.MiddleCenter, fontSize = 15, normal = { textColor = Color.white } });
        }
    }
}
