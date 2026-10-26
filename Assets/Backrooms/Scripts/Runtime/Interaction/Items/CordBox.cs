using System.Collections.Generic;
using Backrooms.Audio;
using UnityEngine;

namespace Backrooms.Interaction
{
    /// <summary>
    /// An orange extension cord, <see cref="Length"/> long, with a plug at one end and a box of three sockets at the other.
    ///  - Coiled (as found), it is one bundle. Holding it, click a wall outlet (or another cord's box): the plug goes in and
    ///    the cord pays out behind you as you carry the box.
    ///  - The box's sockets have power when its plug does, so a lamp (or another cord) plugged into it works anywhere the
    ///    cord reaches. At most <see cref="MaxChain"/> cords in a row.
    ///  - Walk further than the cord and the plug pops out. Right mouse while holding it coils it back up (anything
    ///    plugged into it comes out).
    /// The cord is a physics rope (<see cref="Cord"/>): it lies on the floor, drapes over things and catches on corners.
    /// Models: Resources/Items/CordCoil.fbx, CordSocket.fbx, Plug.fbx.
    /// </summary>
    public class CordBox : PowerUser
    {
        public const float Length = 8f;
        public const int MaxChain = 3;
        public static readonly Color Orange = new Color(0.95f, 0.38f, 0.05f);
        static readonly Vector3[] SocketFaces = { new Vector3(0f, 0.0362f, 0.0259f), new Vector3(0f, 0.0362f, 0.0034f), new Vector3(0f, 0.0362f, -0.0191f) };
        static readonly Vector3 CordEntry = new Vector3(0f, 0.0137f, -0.06f);
        static readonly Vector3 CoilSize = new Vector3(0.28f, 0.06f, 0.28f), BoxSize = new Vector3(0.045f, 0.036f, 0.075f);
        [Tooltip("Furthest outlet you can plug into from your eye (m).")] public float reach = 2.2f;

        public bool coiled = true;
        Plug _plug;
        Cord _cord;
        GameObject _coilModel, _boxModel, _bundlePlug;
        readonly List<Socket> _sockets = new List<Socket>();
        BoxCollider _col;
        AudioSource _fx;

        public override Plug Plug => _plug;
        public IReadOnlyList<Socket> BoxSockets => _sockets;
        public bool Powered => !coiled && PluggedLive;
        public int Depth => 1 + (_plug && _plug.Socket != null ? _plug.Socket.Depth : 0);
        public override bool PlaceOnDrop => !coiled;
        public override Vector3 HoldOffset => coiled ? new Vector3(0.2f, -0.36f, 0.42f) : new Vector3(0.15f, -0.22f, 0.35f);
        public override Vector3 GripLocal => coiled ? new Vector3(0.12f, 0.03f, 0f) : new Vector3(0.02f, 0.02f, 0f);
        public override Quaternion GripRotationLocal => Quaternion.LookRotation(new Vector3(-0.4f, -0.3f, 0.86f), Vector3.up);
        public override float GripCurl => 0.9f;
        public override float Amount { get => coiled ? 0f : 1f; set { } }
        public override string Status => coiled ? $"extension cord ({Length:0} m): click an outlet"
                                                : (Powered ? "extension cord: live" : "extension cord: no power") + "  (right mouse: coil)";

        public static CordBox Create(Vector3 position, Quaternion rotation)
        {
            var go = ItemModels.Body("ExtensionCord", position, rotation, 1.4f, "CordCoil", CoilSize, "CordOrange", false, new Vector3(0f, 0.03f, 0f));
            go.transform.Find("Model").name = "Coil";
            var box = go.AddComponent<CordBox>();
            box.itemId = "extensioncord"; box.displayName = "Extension cord";
            Generation.PoolLitSwitch.Attach(go);
            return box;
        }

        protected override void Awake()
        {
            base.Awake();
            _fx = ItemModels.Source(gameObject, 10f);
            _col = GetComponent<BoxCollider>();
            var c = transform.Find("Coil");
            _coilModel = c ? c.gameObject : null;
            var boxRoot = new GameObject("Box").transform;
            boxRoot.SetParent(transform, false);
            _boxModel = ItemModels.Attach(boxRoot, "CordSocket", BoxSize, "CordOrange");
            _boxModel = boxRoot.gameObject;
            var plugRoot = new GameObject("BundlePlug").transform;
            plugRoot.SetParent(transform, false);
            ItemModels.Attach(plugRoot, "Plug", new Vector3(0.035f, 0.03f, 0.045f), "PlugBody");
            foreach (var r in plugRoot.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].name == "Item_PlugBody") mats[i] = ItemModels.Mat("PlugBody_" + ColorUtility.ToHtmlStringRGB(Orange), Orange);
                r.sharedMaterials = mats;
            }
            _bundlePlug = plugRoot.gameObject;
            foreach (var f in SocketFaces) _sockets.Add(new Socket { anchor = _boxModel.transform, local = f, localNormal = Vector3.up, box = this });
            Shape();
        }

        /// <summary>Coiled: the coil, with the box and the plug tucked beside it. Out: just the box.</summary>
        void Shape()
        {
            if (_coilModel) _coilModel.SetActive(coiled);
            _bundlePlug.SetActive(coiled);
            if (coiled)
            {
                _boxModel.transform.localPosition = new Vector3(0.175f, 0f, 0.07f);
                _boxModel.transform.localRotation = Quaternion.Euler(0f, 70f, 0f);
                _bundlePlug.transform.localPosition = new Vector3(0.17f, 0.016f, -0.06f);
                _bundlePlug.transform.localRotation = Quaternion.Euler(0f, 120f, 0f);
                _col.size = CoilSize; _col.center = new Vector3(0f, 0.03f, 0f);
                Body.mass = 1.4f;
            }
            else
            {
                _boxModel.transform.localPosition = Vector3.zero;
                _boxModel.transform.localRotation = Quaternion.identity;
                _col.size = BoxSize; _col.center = new Vector3(0f, 0.018f, -0.012f);
                Body.mass = 0.35f;
            }
            foreach (var s in _sockets) { if (coiled) Sockets.RemoveBox(s); else Sockets.AddBox(s); }
        }

        public override void WakeCord() { if (_cord) _cord.Wake(); }

        /// <summary>Out of its coil with the plug in a socket (from a save: plugged = null, the plug lies at plugPosition).</summary>
        public void Deploy(Socket socket, Vector3? plugPosition = null)
        {
            if (!coiled) return;
            coiled = false;
            Shape();
            Vector3 at = socket != null ? socket.Position : plugPosition ?? transform.position + Vector3.up * 0.05f;
            _plug = Plug.Create("cordplug", this, at, socket != null ? socket.PlugRotation : Quaternion.identity, Orange);
            if (socket != null) _plug.PlugInto(socket);
            _cord = Cord.Create("ExtensionCordRope", _plug, Plug.CordPoint, this, CordEntry, Length, 0.0045f, ItemModels.Mat("CordOrange", Orange));
            _cord.onTaut = c => { if (_plug && _plug.Plugged) _plug.Unplug(true); };
        }

        /// <summary>Back into its coil: its plug comes out, and so does anything plugged into it.</summary>
        public void Coil()
        {
            if (coiled) return;
            foreach (var s in _sockets) if (s.occupant) s.occupant.Unplug(true);
            if (_plug) { _plug.Unplug(false); var hand = FindAnyObjectByType<PropGrabber>(); if (hand && hand.HeldItem == _plug) hand.LetGo(_plug); Destroy(_plug.gameObject); }
            if (_cord) Destroy(_cord.gameObject);
            _plug = null; _cord = null;
            coiled = true;
            Shape();
            _fx.PlayOneShot(SoundBank.CordWhip, 0.6f);
        }

        public override void HoldUpdate(in HoldInput i)
        {
            if (i.secondary) { Coil(); return; }
            if (!i.pressed || !i.hit || i.hitInfo.distance > reach) return;
            var s = Sockets.Nearest(i.hitInfo.point, 0.1f, this);
            if (s == null) return;
            if (coiled)
            {
                if (s.Depth + 1 > MaxChain) return;
                Deploy(s);
            }
            else if (_plug && !_plug.Plugged && _plug.CanUse(s) && Vector3.Distance(s.Position, transform.position) < Length * 0.95f) _plug.PlugInto(s);
        }

        void OnDestroy()
        {
            foreach (var s in _sockets) { Sockets.RemoveBox(s); if (s.occupant) s.occupant.Unplug(true); }
            if (_plug) Destroy(_plug.gameObject);
            if (_cord) Destroy(_cord.gameObject);
        }
    }
}
