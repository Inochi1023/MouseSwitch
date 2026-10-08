using System;
using System.IO;

namespace MouseSwitch
{
    /// <summary>
    /// Several input records are packed into one encrypted frame, so the
    /// per frame crypto overhead is paid once per flush instead of once
    /// per mouse movement.
    /// </summary>
    internal sealed class PacketWriter
    {
        private readonly MemoryStream _ms = new MemoryStream(256);

        public int Length { get { return (int)_ms.Length; } }

        public void Move(int dx, int dy)
        {
            if (dx > short.MaxValue) dx = short.MaxValue;
            if (dx < short.MinValue) dx = short.MinValue;
            if (dy > short.MaxValue) dy = short.MaxValue;
            if (dy < short.MinValue) dy = short.MinValue;
            _ms.WriteByte(Op.Move);
            PutI16((short)dx);
            PutI16((short)dy);
        }

        public void Button(byte button, bool down)
        {
            _ms.WriteByte(Op.Button);
            _ms.WriteByte(button);
            _ms.WriteByte((byte)(down ? 1 : 0));
        }

        public void Wheel(int delta, bool horizontal)
        {
            if (delta > short.MaxValue) delta = short.MaxValue;
            if (delta < short.MinValue) delta = short.MinValue;
            _ms.WriteByte(Op.Wheel);
            PutI16((short)delta);
            _ms.WriteByte((byte)(horizontal ? 1 : 0));
        }

        public void Key(ushort vk, ushort scan, bool down, bool extended)
        {
            _ms.WriteByte(Op.Key);
            PutU16(vk);
            PutU16(scan);
            _ms.WriteByte((byte)(down ? 1 : 0));
            _ms.WriteByte((byte)(extended ? 1 : 0));
        }

        public void Simple(byte op)
        {
            _ms.WriteByte(op);
        }

        public void Stamp(byte op, long value)
        {
            _ms.WriteByte(op);
            byte[] b = BitConverter.GetBytes(value);
            _ms.Write(b, 0, 8);
        }

        public void Config(bool shareKeyboard)
        {
            _ms.WriteByte(Op.Config);
            _ms.WriteByte((byte)(shareKeyboard ? 1 : 0));
        }

        private void PutI16(short v)
        {
            byte[] b = BitConverter.GetBytes(v);
            _ms.Write(b, 0, 2);
        }

        private void PutU16(ushort v)
        {
            byte[] b = BitConverter.GetBytes(v);
            _ms.Write(b, 0, 2);
        }

        public byte[] ToArray()
        {
            return _ms.ToArray();
        }

        public void Reset()
        {
            _ms.SetLength(0);
        }
    }

    internal sealed class PacketReader
    {
        private readonly byte[] _buf;
        private int _pos;

        public PacketReader(byte[] payload)
        {
            _buf = payload;
            _pos = 0;
        }

        public bool HasMore { get { return _pos < _buf.Length; } }

        public byte NextOp()
        {
            return _buf[_pos++];
        }

        public short I16()
        {
            short v = BitConverter.ToInt16(_buf, _pos);
            _pos += 2;
            return v;
        }

        public ushort U16()
        {
            ushort v = BitConverter.ToUInt16(_buf, _pos);
            _pos += 2;
            return v;
        }

        public byte U8()
        {
            return _buf[_pos++];
        }

        public long I64()
        {
            long v = BitConverter.ToInt64(_buf, _pos);
            _pos += 8;
            return v;
        }
    }
}
