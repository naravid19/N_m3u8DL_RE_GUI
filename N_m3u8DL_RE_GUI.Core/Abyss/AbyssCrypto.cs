using System;
using System.Security.Cryptography;
using System.Text;

namespace N_m3u8DL_RE_GUI.Core.Abyss
{
    /// <summary>
    /// Cryptographic helper methods for Abyss/Hydrax video stream token generation and payload decryption.
    /// Uses standard AES-CTR and MD5 algorithms with zero external dependencies.
    /// </summary>
    public static class AbyssCrypto
    {
        private static readonly Encoding Iso88591 = Encoding.GetEncoding("ISO-8859-1");

        /// <summary>
        /// Derives a 32-character hexadecimal MD5 hash from a UTF-8 string (e.g. "{user_id}:{slug}:{md5_id}").
        /// </summary>
        public static string DeriveKey(string input)
        {
            if (input == null) return string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            return ComputeMd5Hex(bytes);
        }

        /// <summary>
        /// Derives an MD5 hash from a numeric value according to Abyss protocol
        /// (each decimal digit character is mapped to its raw byte value 0..9).
        /// </summary>
        public static string DeriveKey(long number)
        {
            string str = number.ToString();
            byte[] bytes = new byte[str.Length];
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                bytes[i] = char.IsDigit(c) ? (byte)(c - '0') : (byte)c;
            }
            return ComputeMd5Hex(bytes);
        }

        /// <summary>
        /// Computes lowercase hexadecimal MD5 digest.
        /// </summary>
        private static string ComputeMd5Hex(byte[] input)
        {
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(input);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Performs AES-CTR (Counter Mode) encryption or decryption.
        /// CTR mode is symmetric (Encrypt(data) == Decrypt(data)).
        /// </summary>
        public static byte[] AesCtrTransform(byte[] data, byte[] key, byte[] iv)
        {
            if (data == null || data.Length == 0) return Array.Empty<byte>();
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null || iv.Length != 16) throw new ArgumentException("IV must be 16 bytes", nameof(iv));

            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            aes.Key = key;

            using var encryptor = aes.CreateEncryptor();
            byte[] counter = (byte[])iv.Clone();
            byte[] result = new byte[data.Length];
            byte[] keyStream = new byte[16];

            int blockCount = (data.Length + 15) / 16;
            for (int b = 0; b < blockCount; b++)
            {
                encryptor.TransformBlock(counter, 0, 16, keyStream, 0);

                int offset = b * 16;
                int count = Math.Min(16, data.Length - offset);
                for (int i = 0; i < count; i++)
                {
                    result[offset + i] = (byte)(data[offset + i] ^ keyStream[i]);
                }

                // Increment 128-bit big-endian counter
                for (int i = 15; i >= 0; i--)
                {
                    if (++counter[i] != 0)
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Encrypts an input string with AES-CTR using the specified MD5 hex key string.
        /// </summary>
        public static byte[] EncryptAesCtr(string data, string keyHex)
        {
            byte[] dataBytes = Encoding.UTF8.GetBytes(data ?? string.Empty);
            byte[] keyBytes = Encoding.UTF8.GetBytes(keyHex);
            byte[] iv = new byte[16];
            Array.Copy(keyBytes, 0, iv, 0, 16);
            return AesCtrTransform(dataBytes, keyBytes, iv);
        }

        /// <summary>
        /// Decrypts an ISO-8859-1 encoded ciphertext string into a UTF-8 string.
        /// </summary>
        public static string DecryptString(string cipherTextIso8859, string keyHex)
        {
            if (string.IsNullOrEmpty(cipherTextIso8859)) return string.Empty;

            byte[] cipherBytes = new byte[cipherTextIso8859.Length];
            for (int i = 0; i < cipherTextIso8859.Length; i++)
            {
                cipherBytes[i] = (byte)cipherTextIso8859[i];
            }

            byte[] keyBytes = Encoding.UTF8.GetBytes(keyHex);
            byte[] iv = new byte[16];
            Array.Copy(keyBytes, 0, iv, 0, 16);

            byte[] decrypted = AesCtrTransform(cipherBytes, keyBytes, iv);
            return Encoding.UTF8.GetString(decrypted);
        }

        /// <summary>
        /// Double-Base64 encodes encrypted chunk path bytes into the Abysscdn segment request token.
        /// </summary>
        public static string DoubleBase64(byte[] cipherBytes)
        {
            if (cipherBytes == null || cipherBytes.Length == 0) return string.Empty;

            // First pass: encode raw cipher bytes (interpreted as ISO-8859-1 chars)
            string first = Convert.ToBase64String(cipherBytes).Replace("=", "");
            byte[] firstBytes = Encoding.UTF8.GetBytes(first);
            // Second pass: encode first base64 ASCII bytes
            return Convert.ToBase64String(firstBytes).Replace("=", "");
        }
    }
}
