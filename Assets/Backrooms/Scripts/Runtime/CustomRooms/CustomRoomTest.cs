using System.Collections;
using Backrooms.Camcorder;
using Backrooms.Generation;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Backrooms.CustomRooms
{
    /// <summary>
    /// "Test in world": the room being edited is forced into the real procedural world, just outside the spawn hall, and the player is
    /// put inside it with the camcorder filters on. It is generated and built exactly like a placed room, so its doors link
    /// to the procedural rooms around it. Escape > Back to the builder returns to the editor with all edits intact.
    /// </summary>
    public static class CustomRoomTest
    {
        public static RoomBlueprint Blueprint;
        public static int Variant;
        public static bool Active => BuildSession.Testing && Blueprint != null;

        /// <summary>Cell of the district (0, 0) where the room is placed: just beyond the spawn hall.</summary>
        public const int CellX = 15, CellZ = 15;

        public static void Begin(RoomBlueprint bp, int variant)
        {
            Blueprint = bp.Clone();
            Blueprint.Validate();
            if (string.IsNullOrEmpty(Blueprint.id)) Blueprint.id = "unsaved";
            CustomRoomStamp.Override = Blueprint;
            Variant = Mathf.Clamp(variant, 0, Blueprint.variants.Count - 1);
            BuildSession.Testing = true;
        }

        public static void End()
        {
            CustomRoomStamp.Override = null;
            Blueprint = null;
            BuildSession.Testing = false;
        }

        public static LandmarkPlacement Placement()
        {
            var v = Blueprint.variants[Variant];
            var rect = new RectInt(CellX, CellZ, v.width, v.depth);
            return new LandmarkPlacement
            {
                name = Blueprint.name, kind = LandmarkKind.Custom, district = Vector2Int.zero, rect = rect, core = rect, storeys = 1,
                seed = 0x7E57, blueprint = Blueprint.id, variant = Variant, rot = 0,
            };
        }

        /// <summary>World position of the middle of the room (metres).</summary>
        public static Vector3 Centre()
        {
            var v = Blueprint.variants[Variant];
            const float c = OfficeBiome.CellSize;
            return new Vector3((CellX + v.width * 0.5f) * c, 0.05f, (CellZ + v.depth * 0.5f) * c);
        }

        /// <summary>Called by the pause menu object (which lives across scene loads) whenever a scene has loaded.</summary>
        public static void OnLoad()
        {
            if (!Active || SceneManager.GetActiveScene().name != SceneIds.World) return;
            var go = new GameObject("TestRoomLauncher");
            go.AddComponent<Launcher>();
        }

        sealed class Launcher : MonoBehaviour
        {
            IEnumerator Start()
            {
                CamcorderPlayerController player = null;
                for (int i = 0; i < 300 && player == null; i++) { player = FindFirstObjectByType<CamcorderPlayerController>(); yield return null; }
                if (!player || !Active) { Destroy(gameObject); yield break; }
                for (int i = 0; i < 20; i++) yield return null;
                var c = Centre();
                player.TeleportSafe(c, 180f, 6f);          // you wait in place until the floor has loaded
                Debug.Log($"[Backrooms] Testing custom room '{Blueprint.name}' at {c}. Escape > Back to the builder.");
                Destroy(gameObject);
            }
        }
    }
}
