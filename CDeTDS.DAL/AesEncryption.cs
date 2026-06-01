using System.Security.Cryptography;
using System.Text;

namespace CDeTDS.DAL
{
    /// <summary>
    /// AES-256 encryption for storing portal credentials.
    /// Key is derived from machine SID + app name — unique per installation.
    /// Never stores plain-text passwords anywhere.
    /// </summary>
    public static class AesEncryption
    {
        // 256-bit key derived via PBKDF2 (100,000 iterations) from machine + app seed.
        // Changing this breaks existing encrypted values — users must re-enter credentials.
        private static readonly byte[] _key  = DeriveKey();
        private static readonly byte[] _salt = Encoding.UTF8.GetBytes("TDSPro_v3_AES_Salt_2026!");

        private static byte[] DeriveKey()
        {
            var seed = $"TDSPro|{Environment.MachineName}|{Environment.UserName}|IT_Act_2026";
            var saltBytes = Encoding.UTF8.GetBytes("TDSPro_PBKDF2_Salt_v2!");
            using var pbkdf2 = new Rfc2898DeriveBytes(seed, saltBytes, 100_000, HashAlgorithmName.SHA256);
            return pbkdf2.GetBytes(32); // 256-bit key
        }

        /// <summary>Encrypt a plain-text string → Base64 ciphertext.</summary>
        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            using var aes = Aes.Create();
            aes.Key     = _key;
            aes.Mode    = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();

            using var enc    = aes.CreateEncryptor();
            var plain        = Encoding.UTF8.GetBytes(plainText);
            var cipherBytes  = enc.TransformFinalBlock(plain, 0, plain.Length);

            // Prepend IV to ciphertext
            var result = new byte[aes.IV.Length + cipherBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
            Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
            return Convert.ToBase64String(result);
        }

        /// <summary>Decrypt a Base64 ciphertext → plain text.</summary>
        public static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";
            try
            {
                var data = Convert.FromBase64String(cipherText);
                using var aes  = Aes.Create();
                aes.Key        = _key;
                aes.Mode       = CipherMode.CBC;
                aes.Padding    = PaddingMode.PKCS7;

                // Extract IV (first 16 bytes)
                var iv          = new byte[16];
                var cipherBytes = new byte[data.Length - 16];
                Buffer.BlockCopy(data, 0, iv, 0, 16);
                Buffer.BlockCopy(data, 16, cipherBytes, 0, cipherBytes.Length);
                aes.IV = iv;

                using var dec = aes.CreateDecryptor();
                var plain     = dec.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
                return Encoding.UTF8.GetString(plain);
            }
            catch { return ""; }
        }

        /// <summary>Save an encrypted credential to the fvu_format_config table.</summary>
        public static void SaveCredential(string key, string value)
        {
            var encrypted = Encrypt(value);
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO fvu_format_config (config_key,form_type,version,effective_from,notes)
                VALUES(@k,'CREDENTIAL','1.0','2026-04-01',@v)
                ON CONFLICT(config_key) DO UPDATE SET notes=excluded.notes";
            cmd.Parameters.AddWithValue("@k", "CRED_" + key);
            cmd.Parameters.AddWithValue("@v", encrypted);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Load and decrypt a credential.</summary>
        public static string LoadCredential(string key, string defaultVal = "")
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = "SELECT notes FROM fvu_format_config WHERE config_key=@k";
                cmd.Parameters.AddWithValue("@k", "CRED_" + key);
                var val = cmd.ExecuteScalar() as string ?? "";
                return string.IsNullOrEmpty(val) ? defaultVal : Decrypt(val);
            }
            catch { return defaultVal; }
        }

        /// <summary>Delete a saved credential.</summary>
        public static void ClearCredential(string key)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM fvu_format_config WHERE config_key=@k";
            cmd.Parameters.AddWithValue("@k", "CRED_" + key);
            cmd.ExecuteNonQuery();
        }
    }
}
