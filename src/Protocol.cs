using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace MouseSwitch
{
    internal static class Op
    {
        public const byte Move = 0x01;
        public const byte Button = 0x02;
        public const byte Wheel = 0x03;
        public const byte Key = 0x04;
        public const byte Enter = 0x05;
        public const byte Leave = 0x06;
        public const byte Ping = 0x07;
        public const byte Pong = 0x08;
        public const byte Config = 0x09;
        public const byte Bye = 0x0A;
        public const byte ReleaseAll = 0x0B;
    }

    internal static class Btn
    {
        public const byte Left = 1;
        public const byte Right = 2;
        public const byte Middle = 3;
        public const byte X1 = 4;
        public const byte X2 = 5;
    }

    internal class HandshakeRejected : Exception
    {
        public HandshakeRejected(string m) : base(m) { }
    }

    /// <summary>
    /// An authenticated, encrypted message link over one TCP connection.
    /// Not thread safe for concurrent writes: callers serialise on SendLock.
    /// </summary>
    internal sealed class Link : IDisposable
    {
        public const int DefaultPort = 24800;
        public const int DiscoveryPort = 24801;
        private const int MaxFrame = 65536;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MSW1");

        private readonly TcpClient _tcp;
        private readonly NetworkStream _stream;
        private readonly SessionKeys _keys;
        private long _sendSeq;
        private long _recvSeq = -1;

        public readonly object SendLock = new object();
        public byte[] NewLongTermKey;

        private Link(TcpClient tcp, NetworkStream stream, SessionKeys keys)
        {
            _tcp = tcp;
            _stream = stream;
            _keys = keys;
        }

        public TcpClient Tcp { get { return _tcp; } }

        // ------------------------------------------------------------------
        // handshake
        // ------------------------------------------------------------------

        /// <param name="initiator">true on the machine that dials out (the host)</param>
        /// <param name="pairing">true for a first time pairing using a code</param>
        /// <param name="secret">the pairing code (pairing) or the stored long term key (resume)</param>
        /// <param name="responderSecret">responder only: given the mode
        /// (true = pairing), returns the secret to bind with, or null to refuse</param>
        public static Link Handshake(TcpClient tcp, bool initiator, bool pairing, byte[] secret,
                                     Func<bool, byte[]> responderSecret)
        {
            tcp.NoDelay = true;
            NetworkStream s = tcp.GetStream();
            s.ReadTimeout = 15000;
            s.WriteTimeout = 15000;

            byte[] pubA, pubB;
            bool pairMode = pairing;

            using (Crypto.Ecdh ecdh = new Crypto.Ecdh())
            {
                byte[] myPub = ecdh.PublicBlob;

                if (initiator)
                {
                    MemoryStream m = new MemoryStream();
                    m.Write(Magic, 0, 4);
                    m.WriteByte(1);
                    m.WriteByte((byte)(pairing ? 0 : 1));
                    WriteBlob(m, myPub);
                    byte[] buf = m.ToArray();
                    s.Write(buf, 0, buf.Length);
                    s.Flush();

                    byte[] head = ReadExact(s, 6);
                    CheckMagic(head);
                    if (head[5] != 0) throw new HandshakeRejected("상대 PC가 연결을 거부했습니다 (페어링 모드가 아닐 수 있음)");
                    byte[] peerPub = ReadBlob(s);
                    pubA = myPub;
                    pubB = peerPub;
                }
                else
                {
                    byte[] head = ReadExact(s, 6);
                    CheckMagic(head);
                    pairMode = head[5] == 0;
                    byte[] peerPub = ReadBlob(s);

                    byte[] resolved = responderSecret == null ? null : responderSecret(pairMode);
                    bool ok = resolved != null;
                    if (ok) secret = resolved;
                    MemoryStream m = new MemoryStream();
                    m.Write(Magic, 0, 4);
                    m.WriteByte(1);
                    m.WriteByte((byte)(ok ? 0 : 1));
                    WriteBlob(m, myPub);
                    byte[] buf = m.ToArray();
                    s.Write(buf, 0, buf.Length);
                    s.Flush();
                    if (!ok) throw new HandshakeRejected(pairMode
                        ? "상대가 페어링 모드가 아닙니다"
                        : "저장된 페어링 정보가 없습니다");

                    pubA = peerPub;
                    pubB = myPub;
                }

                byte[] z = ecdh.DeriveSecret(initiator ? pubB : pubA);
                byte[] salt = Crypto.Concat(pubA, pubB);
                byte[] okm = Crypto.Hkdf(z, salt, "msw1-v1-keys", 128);
                byte[] auth = Crypto.Hkdf(z, salt, "msw1-v1-auth", 32);
                byte[] bind = Crypto.Hkdf(secret, salt, "msw1-v1-bind", 32);

                byte[] confA = Crypto.Hmac(bind, Crypto.Concat(auth, new byte[] { (byte)'A' }));
                byte[] confB = Crypto.Hmac(bind, Crypto.Concat(auth, new byte[] { (byte)'B' }));

                if (initiator)
                {
                    s.Write(confA, 0, 32);
                    s.Flush();
                    byte[] got = ReadExact(s, 32);
                    if (!Crypto.ConstantTimeEquals(got, confB))
                        throw new HandshakeRejected(pairMode ? "페어링 코드가 틀렸습니다" : "인증 실패 - 다시 페어링하세요");
                }
                else
                {
                    byte[] got = ReadExact(s, 32);
                    if (!Crypto.ConstantTimeEquals(got, confA))
                        throw new HandshakeRejected(pairMode ? "페어링 코드가 틀렸습니다" : "인증 실패 - 다시 페어링하세요");
                    s.Write(confB, 0, 32);
                    s.Flush();
                }

                SessionKeys keys = new SessionKeys();
                byte[] a2bEnc = Slice(okm, 0, 32);
                byte[] a2bMac = Slice(okm, 32, 32);
                byte[] b2aEnc = Slice(okm, 64, 32);
                byte[] b2aMac = Slice(okm, 96, 32);
                if (initiator)
                {
                    keys.SendEnc = a2bEnc; keys.SendMac = a2bMac;
                    keys.RecvEnc = b2aEnc; keys.RecvMac = b2aMac;
                }
                else
                {
                    keys.SendEnc = b2aEnc; keys.SendMac = b2aMac;
                    keys.RecvEnc = a2bEnc; keys.RecvMac = a2bMac;
                }

                Link link = new Link(tcp, s, keys);
                if (pairMode) link.NewLongTermKey = Crypto.Hkdf(z, salt, "msw1-v1-ltk", 32);
                // Both sides exchange a heartbeat every second, so silence for
                // ten means the peer is gone and the link should be rebuilt.
                s.ReadTimeout = 10000;
                s.WriteTimeout = 8000;
                return link;
            }
        }

        private static void CheckMagic(byte[] head)
        {
            for (int i = 0; i < 4; i++)
                if (head[i] != Magic[i]) throw new HandshakeRejected("프로토콜이 맞지 않습니다");
            if (head[4] != 1) throw new HandshakeRejected("버전이 다릅니다. 양쪽 모두 같은 버전을 쓰세요");
        }

        private static byte[] Slice(byte[] src, int offset, int len)
        {
            byte[] r = new byte[len];
            Buffer.BlockCopy(src, offset, r, 0, len);
            return r;
        }

        private static void WriteBlob(Stream s, byte[] blob)
        {
            s.WriteByte((byte)(blob.Length >> 8));
            s.WriteByte((byte)(blob.Length & 0xFF));
            s.Write(blob, 0, blob.Length);
        }

        private static byte[] ReadBlob(Stream s)
        {
            byte[] len = ReadExact(s, 2);
            int n = (len[0] << 8) | len[1];
            if (n <= 0 || n > 4096) throw new HandshakeRejected("잘못된 키 길이");
            return ReadExact(s, n);
        }

        public static byte[] ReadExact(Stream s, int count)
        {
            byte[] buf = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buf, got, count - got);
                if (n <= 0) throw new IOException("연결이 끊어졌습니다");
                got += n;
            }
            return buf;
        }

        // ------------------------------------------------------------------
        // framing
        // ------------------------------------------------------------------

        public void Send(byte[] payload)
        {
            byte[] plain = new byte[8 + payload.Length];
            long seq = ++_sendSeq;
            for (int i = 0; i < 8; i++) plain[i] = (byte)(seq >> (56 - 8 * i));
            Buffer.BlockCopy(payload, 0, plain, 8, payload.Length);

            byte[] frame = Crypto.Encrypt(_keys.SendEnc, _keys.SendMac, plain);
            byte[] outBuf = new byte[4 + frame.Length];
            int len = frame.Length;
            outBuf[0] = (byte)(len >> 24);
            outBuf[1] = (byte)(len >> 16);
            outBuf[2] = (byte)(len >> 8);
            outBuf[3] = (byte)len;
            Buffer.BlockCopy(frame, 0, outBuf, 4, len);

            lock (SendLock)
            {
                _stream.Write(outBuf, 0, outBuf.Length);
                _stream.Flush();
            }
        }

        public byte[] Receive()
        {
            byte[] lenBuf = ReadExact(_stream, 4);
            int len = (lenBuf[0] << 24) | (lenBuf[1] << 16) | (lenBuf[2] << 8) | lenBuf[3];
            if (len <= 0 || len > MaxFrame) throw new IOException("프레임 크기 오류");
            byte[] frame = ReadExact(_stream, len);
            byte[] plain = Crypto.Decrypt(_keys.RecvEnc, _keys.RecvMac, frame);
            if (plain.Length < 8) throw new CryptographicException("페이로드 오류");

            long seq = 0;
            for (int i = 0; i < 8; i++) seq = (seq << 8) | plain[i];
            if (seq <= _recvSeq) throw new CryptographicException("재전송 공격 의심");
            _recvSeq = seq;

            byte[] payload = new byte[plain.Length - 8];
            Buffer.BlockCopy(plain, 8, payload, 0, payload.Length);
            return payload;
        }

        public void Dispose()
        {
            try { _stream.Close(); }
            catch { }
            try { _tcp.Close(); }
            catch { }
        }
    }
}
