using System.Text;

namespace CDeTDS.Common
{
    /// <summary>
    /// Base32 (RFC 4648) encoder/decoder shared by LicenseService (key validation,
    /// ships with the app) and CDeTDS.KeyGen (key signing, internal tool).
    /// The alphabet is part of the license key format — never change it.
    /// </summary>
    public static class Base32
    {
        private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static string Encode(byte[] data)
        {
            var sb = new StringBuilder();
            int bits = 0, acc = 0;
            foreach (var b in data)
            {
                acc = (acc << 8) | b;
                bits += 8;
                while (bits >= 5) { bits -= 5; sb.Append(Alphabet[(acc >> bits) & 31]); }
            }
            if (bits > 0) sb.Append(Alphabet[(acc << (5 - bits)) & 31]);
            return sb.ToString();
        }

        public static byte[] Decode(string s)
        {
            var bytes = new List<byte>();
            s = s.ToUpper().TrimEnd('=');
            int bits = 0, acc = 0;
            foreach (var c in s)
            {
                int v = Alphabet.IndexOf(c);
                if (v < 0) continue;
                acc = (acc << 5) | v;
                bits += 5;
                if (bits >= 8) { bits -= 8; bytes.Add((byte)((acc >> bits) & 0xFF)); }
            }
            return bytes.ToArray();
        }
    }
}
