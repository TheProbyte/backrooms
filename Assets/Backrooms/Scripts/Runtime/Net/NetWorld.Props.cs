using System.Collections.Generic;
using Backrooms.Interaction;
using Backrooms.Saves;
using UnityEngine;

namespace Backrooms.Net
{
    public partial class NetWorld
    {
        /// <summary>What this game knows of a piece of furniture (by its id, the same in every game: it comes from the seed).</summary>
        sealed class PropNet
        {
            public int owner = -1;
            public double restT = double.MinValue;
            public int restFrom = -1;
            public readonly List<Sample> buf = new List<Sample>();
        }

        readonly Dictionary<ulong, PropNet> _props = new Dictionary<ulong, PropNet>();
        MovableProp _sitting;

        PropNet PropOf(ulong id) { if (!_props.TryGetValue(id, out var p)) _props[id] = p = new PropNet(); return p; }

        // ---------------- This game moved one ----------------

        /// <summary>Something here started it moving (the hand, a shove, a thing thrown into it): this game moves it now.</summary>
        void LocalWoke(MovableProp p)
        {
            if (!p || p.Spec == null || p.NetRemote) return;
            var pn = PropOf(p.Spec.id);
            if (pn.owner == Me) return;
            pn.owner = Me; pn.buf.Clear();
            var w = W(Msg.PropOwner); w.ULong(p.Spec.id); w.Short((short)Me);
            NetSession.SendWorld(true);
        }

        /// <summary>It came to rest here (or was nailed down): where it is now, and what it is, for every game's record.</summary>
        void LocalRested(MovableProp p)
        {
            if (!p || p.Spec == null || p.NetRemote) return;
            var pn = PropOf(p.Spec.id);
            if (pn.owner >= 0 && pn.owner != Me) return;
            pn.owner = -1;
            SendPropRest(p);
        }

        /// <summary>The game here put a prop into the world itself (a lot stacked, a kit delivered, a shelf put up): what it
        /// is and where, for every game's record (as a prop come to rest, which is what it is).</summary>
        void LocalPlaced(MovableSpec spec, Vector3 pos, Quaternion rot)
        {
            if (spec == null) return;
            var pn = PropOf(spec.id);
            pn.owner = -1; pn.buf.Clear();
            var w = W(Msg.PropRest);
            w.ULong(spec.id);
            w.Vec(pos); w.Rot(rot);
            w.Bool(spec.pinned);
            w.Str(JsonUtility.ToJson(WorldSaver.FromSpec(spec, pos, rot)));
            w.Byte((byte)Me); w.Double(Now);
            NetSession.SendWorld(true);
        }

        /// <summary>The game here took a prop out of the world (a kit's crate once it is put up, a batch collected).</summary>
        void LocalForgot(ulong id)
        {
            _props.Remove(id);
            var w = W(Msg.PropGone);
            w.ULong(id);
            NetSession.SendWorld(true);
        }

        void OnPropGone(NetReader r)
        {
            ulong id = r.ULong();
            var p = MovableProp.Find(id);
            if (p && _hand && _hand.Held == p) _hand.Release("it is gone");
            MovablePropManager.Instance?.Forget(id);
            _props.Remove(id);
        }

        void SendPropRest(MovableProp p)
        {
            var w = W(Msg.PropRest);
            w.ULong(p.Spec.id);
            w.Vec(p.transform.position); w.Rot(p.transform.rotation);
            w.Bool(p.Pinned);
            w.Str(JsonUtility.ToJson(WorldSaver.FromSpec(p.Spec, p.transform.position, p.transform.rotation)));
            w.Byte((byte)Me); w.Double(Now);
            NetSession.SendWorld(true);
        }

        void TickProps()
        {
            // The office chair you sit on rolls and turns with you (it is moved by hand there, not by physics).
            var seat = _player ? _player.GetComponent<Seating>() : null;
            var chair = seat && seat.Seated && seat.Chair && seat.Chair.Spec.kind == MovableKind.OfficeChair ? seat.Chair : null;
            if (chair != _sitting)
            {
                if (_sitting && !_sitting.NetRemote) { PropOf(_sitting.Spec.id).owner = -1; MovablePropManager.Instance?.Store(_sitting); SendPropRest(_sitting); }
                _sitting = chair;
                if (chair && !chair.NetRemote) { MovablePropManager.Instance?.Detach(chair); LocalWoke(chair); }
            }
            foreach (var kv in _props)
            {
                if (kv.Value.owner != Me) continue;
                var p = MovableProp.Find(kv.Key);
                if (!p || (p.Body.isKinematic && !p.held && p != _sitting)) continue;
                var w = W(Msg.PropMove);
                w.ULong(kv.Key); w.Byte((byte)Me); w.Double(Now);
                w.Vec(p.transform.position); w.Rot(p.transform.rotation);
                w.Vec(p.Body.isKinematic ? Vector3.zero : p.Body.linearVelocity);
                NetSession.SendWorld(false);
            }
        }

        // ---------------- Another game moves one ----------------

        bool OnPropOwner(int from, NetReader r)
        {
            ulong id = r.ULong(); int owner = r.Short();
            var pn = PropOf(id);
            if (Host && from != 0 && pn.owner >= 0 && pn.owner != owner)
            {
                // Someone else has it moving (holding it, pushing it): they keep it; the claimant follows them.
                var w = W(Msg.PropOwner); w.ULong(id); w.Short((short)pn.owner);
                NetSession.SendTo(from, true);
                return false;
            }
            pn.owner = owner;
            if (owner != Me) MakeRemote(id);
            else ReleaseRemote(id);
            return true;
        }

        void MakeRemote(ulong id)
        {
            var p = MovableProp.Find(id);
            if (!p) return;
            if (_hand && _hand.Held == p) _hand.Release("someone else has it");
            var seat = _player ? _player.GetComponent<Seating>() : null;
            if (seat && seat.Chair == p && p.Spec.kind == MovableKind.OfficeChair) seat.StandNow();
            MovablePropManager.Instance?.Detach(p);
            p.NetRemote = true;
            if (!p.Body.isKinematic) { p.Body.linearVelocity = Vector3.zero; p.Body.angularVelocity = Vector3.zero; p.Body.isKinematic = true; }
            p.Body.interpolation = RigidbodyInterpolation.None;
        }

        void ReleaseRemote(ulong id)
        {
            var p = MovableProp.Find(id);
            if (p) p.NetRemote = false;
            if (_props.TryGetValue(id, out var pn)) pn.buf.Clear();
        }

        void OnPropMove(NetReader r)
        {
            ulong id = r.ULong(); int from = r.Byte(); double t = r.Double();
            var s = new Sample { t = t, p = r.Vec(), r = r.Rot(), v = r.Vec() };
            Clock(from, t);
            var pn = PropOf(id);
            if (pn.owner == Me) return;
            if (from == pn.restFrom && t <= pn.restT) return;                 // (late, from before it came to rest)
            if (pn.owner != from) { pn.owner = from; MakeRemote(id); }
            var p = MovableProp.Find(id);
            if (p && !p.NetRemote) MakeRemote(id);
            if (pn.buf.Count > 0 && pn.buf[pn.buf.Count - 1].t >= t) return;
            pn.buf.Add(s);
            while (pn.buf.Count > 20) pn.buf.RemoveAt(0);
        }

        void UpdateRemoteProps()
        {
            foreach (var kv in _props)
            {
                var pn = kv.Value;
                if (pn.owner < 0 || pn.owner == Me || pn.buf.Count == 0) continue;
                var p = MovableProp.Find(kv.Key);
                if (!p || !p.NetRemote) continue;
                if (!SampleAt(pn.buf, pn.owner, out var pos, out var rot)) continue;
                p.transform.SetPositionAndRotation(pos, rot);
                p.Body.position = pos; p.Body.rotation = rot;
            }
        }

        void OnPropRest(int from, NetReader r)
        {
            ulong id = r.ULong();
            Vector3 pos = r.Vec(); Quaternion rot = r.Rot();
            bool pinned = r.Bool();
            string json = r.Str();
            int sender = r.Byte(); double t = r.Double();
            var pn = PropOf(id);
            pn.owner = -1; pn.buf.Clear();
            pn.restFrom = sender; pn.restT = t;
            var mgr = MovablePropManager.Instance;
            var p = MovableProp.Find(id);
            if (p)
            {
                if (_hand && _hand.Held == p) _hand.Release("someone else moved it");
                mgr?.Detach(p);
                p.NetRemote = false;
                if (!p.Body.isKinematic) { p.Body.linearVelocity = Vector3.zero; p.Body.angularVelocity = Vector3.zero; p.Body.isKinematic = true; }
                p.Body.interpolation = RigidbodyInterpolation.None;
                p.transform.SetPositionAndRotation(pos, rot);
                p.Body.position = pos; p.Body.rotation = rot;
                if (pinned && !p.Pinned) p.NailDown();
                else if (!pinned && p.Pinned) p.Unnail();
                mgr?.Store(p);
                return;
            }
            // Not in this game's world now (far off): its record, so it is there when its chunk loads.
            if (!mgr || string.IsNullOrEmpty(json)) return;
            var data = JsonUtility.FromJson<PropData>(json);
            if (data == null) return;
            data.position = pos; data.rotation = rot; data.pinned = pinned;
            var spec = WorldSaver.ToSpec(data);
            if (spec != null) mgr.NetPlace(spec, pos, rot);
        }
    }
}
