using System;
using System.Text;
using UnityEngine;

namespace Backrooms.Net
{
    /// <summary>
    /// Writes a message (co-op, 2026-11-04): the first byte is its kind (<see cref="Msg"/>), then its fields, little-endian.
    /// One writer is reused for every message sent (<see cref="NetSession.Begin"/>); <see cref="ToArray"/> copies it out.
    /// </summary>
    public sealed class NetWriter
    {
        byte[] _b = new byte[256];
        public int Length { get; private set; }

        public void Reset() { Length = 0; }

        void Room(int n)
        {
            if (Length + n <= _b.Length) return;
            int size = _b.Length * 2;
            while (size < Length + n) size *= 2;
            Array.Resize(ref _b, size);
        }

        public byte[] ToArray() { var a = new byte[Length]; Buffer.BlockCopy(_b, 0, a, 0, Length); return a; }

        public void Byte(byte v) { Room(1); _b[Length++] = v; }
        public void Bool(bool v) => Byte(v ? (byte)1 : (byte)0);
        public void Short(short v) { Room(2); _b[Length++] = (byte)v; _b[Length++] = (byte)(v >> 8); }
        public void Int(int v) { Room(4); for (int i = 0; i < 4; i++) _b[Length++] = (byte)(v >> (i * 8)); }
        public void UInt(uint v) => Int((int)v);
        public void Long(long v) { Room(8); for (int i = 0; i < 8; i++) _b[Length++] = (byte)(v >> (i * 8)); }
        public void ULong(ulong v) => Long((long)v);
        public void Float(float v) => Int(BitConverter.SingleToInt32Bits(v));
        public void Double(double v) => Long(BitConverter.DoubleToInt64Bits(v));
        public void Vec(Vector3 v) { Float(v.x); Float(v.y); Float(v.z); }
        public void Rot(Quaternion q) { Float(q.x); Float(q.y); Float(q.z); Float(q.w); }

        /// <summary>A unit quaternion in 7 bytes is overkill here; 16 is fine at these rates. Angles in degrees as shorts (0.01 deg).</summary>
        public void Angle(float deg) => Short((short)Mathf.Clamp(Mathf.RoundToInt(Mathf.DeltaAngle(0f, deg) * 100f), short.MinValue, short.MaxValue));

        public void Str(string s)
        {
            if (string.IsNullOrEmpty(s)) { Int(0); return; }
            int n = Encoding.UTF8.GetByteCount(s);
            Int(n); Room(n);
            Encoding.UTF8.GetBytes(s, 0, s.Length, _b, Length);
            Length += n;
        }

        public void Bytes(byte[] a) { if (a == null) { Int(-1); return; } Int(a.Length); Raw(a, 0, a.Length); }
        public void Raw(byte[] a, int offset, int count) { Room(count); Buffer.BlockCopy(a, offset, _b, Length, count); Length += count; }
    }

    /// <summary>Reads a message written by <see cref="NetWriter"/>. A short or broken message throws (the session drops it).</summary>
    public sealed class NetReader
    {
        byte[] _b;
        int _p, _end;

        public NetReader() { }
        public NetReader(byte[] data) { Set(data, 0, data.Length); }
        public void Set(byte[] data, int offset, int count) { _b = data; _p = offset; _end = offset + count; }

        public int Left => _end - _p;
        public int Position => _p;

        void Need(int n) { if (_p + n > _end) throw new FormatException("message too short"); }

        public byte Byte() { Need(1); return _b[_p++]; }
        public bool Bool() => Byte() != 0;
        public short Short() { Need(2); short v = (short)(_b[_p] | (_b[_p + 1] << 8)); _p += 2; return v; }
        public int Int() { Need(4); int v = _b[_p] | (_b[_p + 1] << 8) | (_b[_p + 2] << 16) | (_b[_p + 3] << 24); _p += 4; return v; }
        public uint UInt() => (uint)Int();
        /// <summary>The next uint, without moving on.</summary>
        public uint PeekUInt() { Need(4); return (uint)(_b[_p] | (_b[_p + 1] << 8) | (_b[_p + 2] << 16) | (_b[_p + 3] << 24)); }
        public long Long() { Need(8); long v = 0; for (int i = 0; i < 8; i++) v |= (long)_b[_p + i] << (i * 8); _p += 8; return v; }
        public ulong ULong() => (ulong)Long();
        public float Float() => BitConverter.Int32BitsToSingle(Int());
        public double Double() => BitConverter.Int64BitsToDouble(Long());
        public Vector3 Vec() => new Vector3(Float(), Float(), Float());
        public Quaternion Rot()
        {
            var q = new Quaternion(Float(), Float(), Float(), Float());
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 1e-4f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }
        public float Angle() => Short() / 100f;

        public string Str()
        {
            int n = Int();
            if (n <= 0) return "";
            if (n > 1 << 24) throw new FormatException("string too long");
            Need(n);
            var s = Encoding.UTF8.GetString(_b, _p, n);
            _p += n;
            return s;
        }

        public byte[] Bytes()
        {
            int n = Int();
            if (n < 0) return null;
            if (n > 1 << 26) throw new FormatException("block too long");
            Need(n);
            var a = new byte[n];
            Buffer.BlockCopy(_b, _p, a, 0, n);
            _p += n;
            return a;
        }
    }

    /// <summary>Deflate for the big blocks (the world sent on joining, paint).</summary>
    public static class NetPack
    {
        public static byte[] Pack(byte[] data)
        {
            if (data == null) return null;
            using (var ms = new System.IO.MemoryStream())
            {
                using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) ds.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        public static byte[] Unpack(byte[] packed)
        {
            if (packed == null) return null;
            using (var ms = new System.IO.MemoryStream(packed))
            using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
            using (var o = new System.IO.MemoryStream())
            {
                ds.CopyTo(o);
                return o.ToArray();
            }
        }

        public static byte[] PackString(string s) => Pack(Encoding.UTF8.GetBytes(s ?? ""));
        public static string UnpackString(byte[] b) => b == null ? "" : Encoding.UTF8.GetString(Unpack(b));
    }
}
