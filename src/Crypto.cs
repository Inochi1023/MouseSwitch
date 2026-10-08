using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MouseSwitch
{
    /// <summary>
    /// Key agreement + authenticated encryption for the link.
    /// ECDH P-256 for the session key, AES-256-CBC encrypt-then-MAC with
    /// HMAC-SHA256 for the frames, and a pairing code that binds the
    /// exchange so a man in the middle without the code cannot complete it.
    /// </summary>
    internal static class Crypto
    {
        private static readonly RNGCryptoServiceProvider Rng = new RNGCryptoServiceProvider();

        public static byte[] Random(int n)
        {
            byte[] b = new byte[n];
            Rng.GetBytes(b);
            return b;
        }

        public static string NewPairingCode()
        {
            byte[] b = Random(4);
            uint v = BitConverter.ToUInt32(b, 0) % 1000000u;
            return v.ToString("D6");
        }

        // ---- HKDF (RFC 5869) over HMAC-SHA256 ----
        public static byte[] Hkdf(byte[] ikm, byte[] salt, string info, int length)
        {
            byte[] prk;
            using (HMACSHA256 h = new HMACSHA256(salt == null ? new byte[32] : salt))
            {
                prk = h.ComputeHash(ikm);
            }

            byte[] infoBytes = Encoding.UTF8.GetBytes(info);
            byte[] output = new byte[length];
            byte[] t = new byte[0];
            int pos = 0;
            byte counter = 1;

            using (HMACSHA256 h = new HMACSHA256(prk))
            {
                while (pos < length)
                {
                    byte[] input = new byte[t.Length + infoBytes.Length + 1];
                    Buffer.BlockCopy(t, 0, input, 0, t.Length);
                    Buffer.BlockCopy(infoBytes, 0, input, t.Length, infoBytes.Length);
                    input[input.Length - 1] = counter;
                    t = h.ComputeHash(input);
                    int take = Math.Min(t.Length, length - pos);
                    Buffer.BlockCopy(t, 0, output, pos, take);
                    pos += take;
                    counter++;
                }
            }
            return output;
        }

        public static byte[] Hmac(byte[] key, byte[] data)
        {
            using (HMACSHA256 h = new HMACSHA256(key))
            {
                return h.ComputeHash(data);
            }
        }

        public static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        public static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            for (int i = 0; i < parts.Length; i++) total += parts[i].Length;
            byte[] result = new byte[total];
            int pos = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                Buffer.BlockCopy(parts[i], 0, result, pos, parts[i].Length);
                pos += parts[i].Length;
            }
            return result;
        }

        // ---- frame encryption ----
        public static byte[] Encrypt(byte[] encKey, byte[] macKey, byte[] plaintext)
        {
            byte[] iv = Random(16);
            byte[] ct;
            using (Aes aes = new AesCryptoServiceProvider())
            {
                aes.KeySize = 256;
                aes.Key = encKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform enc = aes.CreateEncryptor())
                {
                    ct = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);
                }
            }
            byte[] body = Concat(iv, ct);
            byte[] mac = Hmac(macKey, body);
            return Concat(body, mac);
        }

        public static byte[] Decrypt(byte[] encKey, byte[] macKey, byte[] frame)
        {
            if (frame.Length < 16 + 16 + 32) throw new CryptographicException("frame too short");
            byte[] body = new byte[frame.Length - 32];
            byte[] mac = new byte[32];
            Buffer.BlockCopy(frame, 0, body, 0, body.Length);
            Buffer.BlockCopy(frame, body.Length, mac, 0, 32);
            if (!ConstantTimeEquals(mac, Hmac(macKey, body))) throw new CryptographicException("bad MAC");

            byte[] iv = new byte[16];
            Buffer.BlockCopy(body, 0, iv, 0, 16);
            using (Aes aes = new AesCryptoServiceProvider())
            {
                aes.KeySize = 256;
                aes.Key = encKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform dec = aes.CreateDecryptor())
                {
                    return dec.TransformFinalBlock(body, 16, body.Length - 16);
                }
            }
        }

        // ---- ECDH ----
        public sealed class Ecdh : IDisposable
        {
            private readonly ECDiffieHellmanCng _ecdh;

            public Ecdh()
            {
                _ecdh = new ECDiffieHellmanCng(256);
                _ecdh.KeyDerivationFunction = ECDiffieHellmanKeyDerivationFunction.Hash;
                _ecdh.HashAlgorithm = CngAlgorithm.Sha256;
            }

            public byte[] PublicBlob
            {
                get { return _ecdh.PublicKey.ToByteArray(); }
            }

            public byte[] DeriveSecret(byte[] peerPublicBlob)
            {
                using (ECDiffieHellmanCngPublicKey pk = (ECDiffieHellmanCngPublicKey)
                           ECDiffieHellmanCngPublicKey.FromByteArray(peerPublicBlob, CngKeyBlobFormat.EccPublicBlob))
                {
                    return _ecdh.DeriveKeyMaterial(pk);
                }
            }

            public void Dispose()
            {
                _ecdh.Clear();
            }
        }

        // ---- at-rest protection for the long term key ----
        public static byte[] ProtectLocal(byte[] data)
        {
            return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        }

        public static byte[] UnprotectLocal(byte[] data)
        {
            return ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
        }
    }

    /// <summary>Keys for one established session.</summary>
    internal sealed class SessionKeys
    {
        public byte[] SendEnc;
        public byte[] SendMac;
        public byte[] RecvEnc;
        public byte[] RecvMac;
    }
}
