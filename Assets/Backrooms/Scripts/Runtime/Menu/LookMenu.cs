using System;
using System.Collections.Generic;
using Backrooms.Audio;
using Backrooms.StyleLab;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Backrooms.Menu
{
    /// <summary>
    /// YOUR LOOK (2026-11-06, Phase 5 of Docs/StyleRollout.md): the face in your suit, from the main menu and the Escape
    /// menu. Laid out as the company's ID card: the choices on the left (eyes, nose, brows, hair, facial hair, glasses,
    /// mask, skin, hair colour, crew number, the name on the chest tape), and on the right your suited head and shoulders,
    /// turning slowly, drawn live by a camera of its own on a little stage far below the world. RANDOM picks a look.
    /// Saved as it changes (StyleLab.PlayerLook); your own suit and, in co-op, the other players see it at once.
    /// Styled as the other menus (Resources/UI/MainMenu.uss), in the company's colours in the Overflow look.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public class LookMenu : MonoBehaviour
    {
        static LookMenu s_instance;
        public static bool IsOpen => s_instance && s_instance._open;
        public static int ClosedFrame { get; private set; } = -10;

        bool _open;
        Action _onClose;
        UIDocument _doc;
        VisualElement _root, _panel, _list, _preview;
        Label _card;
        readonly List<(VisualElement el, Action act)> _items = new List<(VisualElement, Action)>();
        readonly Dictionary<int, Action<int>> _steps = new Dictionary<int, Action<int>>();
        int _sel, _openedFrame;
        AudioSource _beep;

        // The stage: the suit, a camera and two lamps, 2 km below the world.
        static readonly Vector3 StageAt = new Vector3(0f, -2000f, 0f);
        GameObject _stage;
        Transform _turn, _head;
        Camera _cam;
        RenderTexture _rt;
        HazmatAppearance _ap;
        float _turnT;

        static readonly string[] EyeNames = { "TIRED", "WIDE", "CALM" };
        static readonly string[] NoseNames = { "ROUND", "LONG", "BUTTON" };
        static readonly string[] BrowNames = { "FLAT", "WORRIED", "THICK" };
        static readonly string[] HairNames = { "SHORT", "FRINGE", "RECEDING", "BUZZ" };
        static readonly string[] FacialNames = { "NONE", "MOUSTACHE", "STUBBLE", "BEARD" };
        static readonly string[] MaskNames = { "NONE", "HALF", "FULL" };

        public static void Open(Action onClose)
        {
            if (!s_instance)
            {
                var go = new GameObject("LookMenu");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<LookMenu>();
            }
            s_instance.Show(onClose);
        }

        void Awake()
        {
            _beep = gameObject.AddComponent<AudioSource>();
            _beep.playOnAwake = false; _beep.spatialBlend = 0f; _beep.volume = 0.45f; _beep.ignoreListenerPause = true;
        }

        void Show(Action onClose)
        {
            _onClose = onClose;
            if (_doc == null) Build();
            MainMenu.ApplyLook(_root);
            if (!_stage) MakeStage();
            _stage.SetActive(true);
            _open = true;
            _openedFrame = Time.frameCount;
            _panel.style.display = DisplayStyle.Flex;
            Fill();
            ShowLook();
        }

        void Close()
        {
            if (!_open) return;
            _open = false;
            ClosedFrame = Time.frameCount;
            _panel.style.display = DisplayStyle.None;
            if (_stage) _stage.SetActive(false);
            Beep(2);
            var cb = _onClose; _onClose = null;
            cb?.Invoke();
        }

        void OnDestroy()
        {
            if (_rt) { _rt.Release(); Destroy(_rt); }
            if (_stage) Destroy(_stage);
        }

        void Build()
        {
            _doc = gameObject.AddComponent<UIDocument>();
            _doc.panelSettings = Resources.Load<PanelSettings>("UI/CamcorderPanel");
            _doc.sortingOrder = 21;
            _root = _doc.rootVisualElement;
            var sheet = Resources.Load<StyleSheet>("UI/MainMenu");
            if (sheet) _root.styleSheets.Add(sheet);
            _root.AddToClassList("mm-root");
            _root.pickingMode = PickingMode.Ignore;
            MainMenu.ApplyMenuFont(_root);
            _panel = new VisualElement();
            _panel.AddToClassList("mm-panel");
            _panel.AddToClassList("lk-panel");
            _root.Add(_panel);
            var header = new VisualElement(); header.AddToClassList("mm-header");
            header.Add(Text("YOUR LOOK", "mm-title"));
            header.Add(Text("EMPLOYEE ID", "mm-page"));
            _panel.Add(header);
            var rule = new VisualElement(); rule.AddToClassList("mm-rule"); _panel.Add(rule);
            MainMenu.AddStripe(_panel);
            var body = new VisualElement(); body.AddToClassList("lk-body");
            _list = new VisualElement(); _list.AddToClassList("mm-list"); _list.AddToClassList("lk-list");
            body.Add(_list);
            var card = new VisualElement(); card.AddToClassList("lk-card");
            _preview = new VisualElement(); _preview.AddToClassList("lk-preview"); _preview.pickingMode = PickingMode.Ignore;
            card.Add(_preview);
            _card = Text("", "lk-cardtext"); card.Add(_card);
            body.Add(card);
            _panel.Add(body);
            _panel.Add(Text("▲▼ SELECT    ◄ ► CHANGE    ENTER OK    ESC BACK", "mm-hint"));
        }

        // ---------------- The stage ----------------

        void MakeStage()
        {
            _stage = new GameObject("LookStage");
            _stage.transform.SetParent(transform, false);
            _stage.transform.position = StageAt;
            _turn = new GameObject("Turn").transform;
            _turn.SetParent(_stage.transform, false);
            var prefab = Resources.Load<GameObject>("Player/Hazmat/PlayerHazmatSuit");
            if (prefab)
            {
                var suit = Instantiate(prefab, _turn);
                suit.transform.localPosition = Vector3.zero;
                suit.transform.localRotation = Quaternion.identity;
                foreach (var a in suit.GetComponentsInChildren<Animator>(true)) a.enabled = false;      // the rest pose
                foreach (var r in suit.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
                _ap = suit.GetComponentInChildren<HazmatAppearance>(true);
                if (_ap) _ap.firstPerson = false;
                // The head's bone (not a mesh: the meshes sit at the suit's origin); the neck if there is no head bone.
                foreach (var name in new[] { "head", "Head", "neck_01", "Neck" })
                {
                    foreach (var t in suit.GetComponentsInChildren<Transform>(true))
                        if (t.name == name && !t.GetComponent<Renderer>()) { _head = t; break; }
                    if (_head) break;
                }
            }
            // Head and shoulders: framed on the head (or where a head would be, 1.6 m up), from a little above.
            Vector3 head = _head ? _head.position : StageAt + Vector3.up * 1.62f;
            var camGo = new GameObject("LookCamera");
            camGo.transform.SetParent(_stage.transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.fieldOfView = 21f;
            _cam.nearClipPlane = 0.05f; _cam.farClipPlane = 8f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.86f, 0.80f, 0.66f);          // the ID card's photo backing (cream)
            // (The head bone is at the head's base: the face about 5 cm above it, the hood's top 28 cm. From 1.75 m at 24°
            // the picture holds 1.15-1.8 m at 21°: the whole hood and the shoulders.)
            camGo.transform.position = head + new Vector3(0f, 0.08f, 1.75f);
            camGo.transform.LookAt(head + new Vector3(0f, 0.02f, 0f));
            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            _rt = new RenderTexture(480, 600, 24, RenderTextureFormat.ARGB32) { name = "LookPreview", antiAliasing = 4 };
            _cam.targetTexture = _rt;
            _preview.style.backgroundImage = Background.FromRenderTexture(_rt);
            // A warm key light from the front left and a cool fill from the right, both short-ranged (nothing else down here).
            Lamp("Key", head + new Vector3(-0.8f, 0.5f, 1.1f), new Color(1f, 0.92f, 0.8f), 2.2f);
            Lamp("Fill", head + new Vector3(0.9f, 0.1f, 0.8f), new Color(0.75f, 0.82f, 1f), 0.9f);
        }

        void Lamp(string name, Vector3 at, Color c, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_stage.transform, false);
            go.transform.position = at;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = 4f; l.color = c; l.intensity = intensity; l.shadows = LightShadows.None;
        }

        void ShowLook()
        {
            var look = PlayerLook.Saved;
            if (_ap)
            {
                look.ApplyTo(_ap);
                _ap.playerName = Net.NetSession.PlayerName;
                _ap.Apply();
            }
            if (_card != null)
                _card.text = $"{Net.NetSession.PlayerName}\nCREW {Mathf.Clamp(look.crew, 0, PlayerLook.MaxCrew):00}   ·   OVERFLOW";
        }

        // ---------------- The choices ----------------

        static string Of(string[] names, int i) => names[Mathf.Clamp(i, 0, names.Length - 1)];
        static int Wrap(int v, int d, int n) => n <= 0 ? 0 : ((v + d) % n + n) % n;
        int SkinTones => _ap && _ap.skinTones != null && _ap.skinTones.Length > 0 ? Mathf.Min(_ap.skinTones.Length, PlayerLook.MaxTones) : 3;
        int HairTones => _ap && _ap.hairTones != null && _ap.hairTones.Length > 0 ? Mathf.Min(_ap.hairTones.Length, PlayerLook.MaxTones) : 4;

        delegate void Edit(ref PlayerLook l, int d);

        void Choice(string label, Func<PlayerLook, string> value, Edit edit)
        {
            Option(label, () => value(PlayerLook.Saved), d => { var l = PlayerLook.Saved; edit(ref l, d); PlayerLook.Saved = l; ShowLook(); });
        }

        void Fill()
        {
            _list.Clear(); _items.Clear(); _steps.Clear();
            Field("NAME", Net.NetSession.PlayerName, 16, v => { Net.NetSession.PlayerName = v; ShowLook(); });
            Choice("EYES", l => Of(EyeNames, l.eyes), (ref PlayerLook l, int d) => l.eyes = Wrap(l.eyes, d, PlayerLook.Eyes));
            Choice("NOSE", l => Of(NoseNames, l.nose), (ref PlayerLook l, int d) => l.nose = Wrap(l.nose, d, PlayerLook.Noses));
            Choice("BROWS", l => Of(BrowNames, l.brows), (ref PlayerLook l, int d) => l.brows = Wrap(l.brows, d, PlayerLook.Brows));
            Choice("HAIR", l => Of(HairNames, l.hair), (ref PlayerLook l, int d) => l.hair = Wrap(l.hair, d, PlayerLook.Hairs));
            Choice("FACIAL HAIR", l => Of(FacialNames, l.facialHair), (ref PlayerLook l, int d) => l.facialHair = Wrap(l.facialHair, d, PlayerLook.FacialHairs));
            Choice("GLASSES", l => l.glasses ? "ON" : "OFF", (ref PlayerLook l, int d) => l.glasses = !l.glasses);
            Choice("MASK", l => Of(MaskNames, l.mask), (ref PlayerLook l, int d) => l.mask = Wrap(l.mask, d, PlayerLook.Masks));
            int skins = SkinTones, hairs = HairTones;
            Choice("SKIN", l => $"{Mathf.Clamp(l.skinTone, 0, skins - 1) + 1} OF {skins}", (ref PlayerLook l, int d) => l.skinTone = Wrap(Mathf.Clamp(l.skinTone, 0, skins - 1), d, skins));
            Choice("HAIR COLOUR", l => $"{Mathf.Clamp(l.hairTone, 0, hairs - 1) + 1} OF {hairs}", (ref PlayerLook l, int d) => l.hairTone = Wrap(Mathf.Clamp(l.hairTone, 0, hairs - 1), d, hairs));
            Choice("CREW NUMBER", l => Mathf.Clamp(l.crew, 0, PlayerLook.MaxCrew).ToString("00"), (ref PlayerLook l, int d) => l.crew = Wrap(l.crew - 1, d, PlayerLook.MaxCrew) + 1);
            if (Backrooms.Generation.ArtStyle.Current != Backrooms.Generation.ArtStyle.Look.Overflow)
                _list.Add(Text("THE SUIT IS WORN IN THE OVERFLOW ART STYLE (SETTINGS > GRAPHICS).", "mm-desc"));
            Item("RANDOM", () => { PlayerLook.Saved = PlayerLook.Random(skins, hairs); Beep(1); ShowLook(); Fill(); Select(_items.Count - 2, false); }, true);
            Item("BACK", Close, true);
            Select(0, false);
        }

        // ---------------- Input ----------------

        void Update()
        {
            if (!_open) return;
            CamcorderPlayerController.InputBlocked = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // The head turns slowly from side to side (unscaled: the game may be paused behind).
            if (_turn)
            {
                _turnT += Time.unscaledDeltaTime;
                _turn.localRotation = Quaternion.Euler(0f, Mathf.Sin(_turnT * 0.5f) * 28f, 0f);
            }
            var kb = Keyboard.current;
            if (kb == null || _items.Count == 0 || Time.frameCount == _openedFrame) return;
            var focused = _root != null && _root.focusController != null ? _root.focusController.focusedElement as VisualElement : null;
            if (focused != null && (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null))
            {
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame) focused.Blur();
                return;
            }
            bool left = kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame, right = kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame;
            if ((left || right) && _steps.TryGetValue(_sel, out var step)) { step(right ? 1 : -1); Beep(0); return; }
            if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) Select((_sel + _items.Count - 1) % _items.Count, true);
            else if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) Select((_sel + 1) % _items.Count, true);
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Activate(_sel);
            else if (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) Close();
        }

        // ---------------- Rows (as SettingsMenu's) ----------------

        void Item(string text, Action act, bool small = false)
        {
            var row = new VisualElement(); row.AddToClassList("mm-item");
            if (small) row.AddToClassList("mm-small");
            row.Add(Text("►", "mm-cursor"));
            row.Add(Text(text, "mm-text"));
            Add(row, act);
        }

        void Option(string label, Func<string> value, Action<int> step)
        {
            var row = new VisualElement(); row.AddToClassList("mm-item"); row.AddToClassList("lk-option");
            row.Add(Text("►", "mm-cursor"));
            row.Add(Text(label, "lk-label"));
            var text = Text("", "lk-value");
            row.Add(text);
            void Refresh() => text.text = "◄ " + value() + " ►";
            Refresh();
            _steps[_items.Count] = d => { step(d); Refresh(); };
            Add(row, () => { step(1); Refresh(); Beep(0); });
        }

        void Field(string label, string value, int max, Action<string> changed)
        {
            var row = new VisualElement(); row.AddToClassList("mm-item"); row.AddToClassList("mm-fieldrow"); row.AddToClassList("lk-option");
            row.Add(Text("►", "mm-cursor"));
            row.Add(Text(label, "lk-label"));
            var tf = new TextField { value = value ?? "", maxLength = max };
            tf.AddToClassList("mm-field");
            tf.RegisterValueChangedCallback(e => changed(e.newValue));
            row.Add(tf);
            Add(row, () => tf.Focus());
        }

        void Add(VisualElement row, Action act)
        {
            int index = _items.Count;
            _items.Add((row, act));
            row.RegisterCallback<PointerEnterEvent>(_ => { if (_sel != index) Select(index, true); });
            row.RegisterCallback<ClickEvent>(_ => Activate(index));
            _list.Add(row);
        }

        void Select(int i, bool sound)
        {
            if (_items.Count == 0) return;
            _sel = Mathf.Clamp(i, 0, _items.Count - 1);
            for (int k = 0; k < _items.Count; k++) _items[k].el.EnableInClassList("mm-sel", k == _sel);
            if (sound) Beep(0);
        }

        void Activate(int i)
        {
            if (i < 0 || i >= _items.Count) return;
            Select(i, false);
            _items[i].act?.Invoke();
        }

        void Beep(int kind)
        {
            if (_beep) _beep.PlayOneShot(SoundBank.MenuBeep(kind), SoundCatalog.Volume(SoundIds.MenuBeep));
        }

        static Label Text(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }
    }
}
