using Microsoft.Win32;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CDeTDS.DAL
{
    /// <summary>
    /// License system — ECDSA P-256 asymmetric signing.
    ///
    /// SECURITY MODEL:
    ///   - Private key lives ONLY in TDSPro.KeyGen (never distributed)
    ///   - This file contains ONLY the public key — safe to ship
    ///   - Public key can verify signatures but cannot create them
    ///   - Even with full source + decompiled binary, no one can forge a key
    ///
    /// Key format: TDSPRO-[Base32( payload_bytes + sig_bytes )]
    /// Payload: JSON { tid, exp, ded, ent, usr, mid }
    /// </summary>
    public class LicenseService
    {
        // ── Public key — safe to embed, cannot forge signatures ───────────────
        private const string PublicKeyPem =
            "-----BEGIN PUBLIC KEY-----\n" +
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAESLlbrTuxUvQhVDZ8eN/oxMZte9E+\n" +
            "/TLuWxg7sLGKutgEVSd9v97Uc8W65wCm/wzNdAOo/xBtO4bTeBTjTXnf9w==\n" +
            "-----END PUBLIC KEY-----";

        private const string LicenseDbKey   = "LIC_KEY_V2";
        private const string InstallDateKey  = "LIC_INSTALL_V2";

        // ── Trial defaults ────────────────────────────────────────────────────
        public static LicenseInfo BuildTrial()
        {
            var install = GetInstallDate();
            var expiry  = install.AddDays(30);
            return new LicenseInfo
            {
                Tier         = LicenseTier.Trial,
                IsValid      = true,
                ExpiryDate   = expiry,
                MaxDeductors = 1,
                MaxEntries   = 100,
                MaxUsers     = 1,
                Message      = $"Trial — {Math.Max(0, (expiry - DateTime.Today).Days)} days remaining. Activate Pro to remove limits.",
            };
        }

        // ── Machine ID — stable hardware fingerprint ──────────────────────────
        public static string GetMachineId()
        {
            var parts = new List<string>();

            // 1. Windows MachineGuid (most stable — survives hardware changes)
            try
            {
                using var regKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
                var guid = regKey?.GetValue("MachineGuid")?.ToString() ?? "";
                if (!string.IsNullOrEmpty(guid)) parts.Add(guid);
            }
            catch { }

            // 2. Motherboard serial (physically tied to board)
            try
            {
                using var s = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject o in s.Get())
                {
                    var sn = o["SerialNumber"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(sn) && sn != "Default string" && sn != "To be filled by O.E.M.")
                        parts.Add(sn);
                }
            }
            catch { }

            // 3. Primary disk serial
            try
            {
                using var s = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_DiskDrive WHERE Index=0");
                foreach (ManagementObject o in s.Get())
                {
                    var sn = o["SerialNumber"]?.ToString()?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(sn)) parts.Add(sn);
                }
            }
            catch { }

            // 4. MachineName fallback
            parts.Add(Environment.MachineName.ToUpper());

            var combined = string.Join("|", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(combined));
            return Convert.ToHexString(hash)[..12].ToUpper();
        }

        // ── Validate a key entered by the user ────────────────────────────────
        public LicenseInfo ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return Invalid("No license key entered.");

            key = key.Trim().ToUpper().Replace(" ", "").Replace("-", "");
            if (key.StartsWith("TDSPRO")) key = key[6..];
            if (key.Length < 10) return Invalid("Key too short — check and re-enter.");

            try
            {
                // Base32-decode to get raw bytes
                var raw = Base32Decode(key);
                if (raw.Length < 8) return Invalid("Invalid key format.");

                // First 4 bytes = payload length (big-endian uint)
                var payloadLen = (int)((raw[0] << 24) | (raw[1] << 16) | (raw[2] << 8) | raw[3]);
                if (payloadLen <= 0 || payloadLen > raw.Length - 4)
                    return Invalid("Invalid key structure.");

                var payloadBytes = raw[4..(4 + payloadLen)];
                var sigBytes     = raw[(4 + payloadLen)..];

                // Verify ECDSA P-256 signature
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(PublicKeyPem);
                if (!ecdsa.VerifyData(payloadBytes, sigBytes, HashAlgorithmName.SHA256))
                    return Invalid("Invalid license key — signature verification failed.");

                // Parse payload JSON
                var json    = Encoding.UTF8.GetString(payloadBytes);
                var doc     = JsonDocument.Parse(json);
                var root    = doc.RootElement;

                var tid     = root.GetProperty("tid").GetString() ?? "TRIAL";
                var expStr  = root.GetProperty("exp").GetString() ?? "99991231";
                var maxDed  = root.GetProperty("ded").GetInt32();
                var maxEnt  = root.GetProperty("ent").GetInt32();
                var maxUsr  = root.GetProperty("usr").GetInt32();
                var mid     = root.GetProperty("mid").GetString() ?? "000000000000";

                var expiry = expStr == "99991231"
                    ? DateTime.Today.AddYears(100)
                    : DateTime.ParseExact(expStr, "yyyyMMdd", null);

                // Machine ID check (skip if floating key = all zeros)
                var thisMid = GetMachineId();
                var isFloating = string.IsNullOrEmpty(mid.Replace("0", ""));
                if (!isFloating && mid != thisMid)
                    return Invalid($"This key is locked to a different machine.\nYour Machine ID: {thisMid}\nPlease contact support to transfer.");

                // Expiry check (allow loading expired keys — app enforces read-only)
                var tier = tid == "PRO" ? LicenseTier.Pro : LicenseTier.Trial;
                var info = new LicenseInfo
                {
                    Key          = FormatKey(key),
                    Tier         = tier,
                    IsValid      = true,
                    ExpiryDate   = expiry,
                    MaxDeductors = maxEnt == 999999 ? int.MaxValue : maxDed,
                    MaxEntries   = maxEnt == 999999 ? int.MaxValue : maxEnt,
                    MaxUsers     = maxUsr,
                    MachineId    = thisMid,
                    Message      = BuildMessage(tier, expiry),
                };

                SaveLicense(info);
                return info;
            }
            catch (Exception ex)
            {
                return Invalid($"Key validation error: {ex.Message}");
            }
        }

        // ── Load saved license — always re-verifies signature ─────────────────
        public LicenseInfo LoadSaved()
        {
            var key = AesEncryption.LoadCredential(LicenseDbKey, "");
            if (string.IsNullOrEmpty(key)) return BuildTrial();

            // Re-validate from the stored key string — DB field edits are irrelevant
            var result = ValidateKey(key);

            // If signature no longer valid (key was tampered in DB), fall back to trial
            return result.IsValid ? result : BuildTrial();
        }

        // ── Save activated license (stores full key string, not fields) ────────
        private static void SaveLicense(LicenseInfo info)
        {
            // Store the full signed key string — on next load it's re-verified
            AesEncryption.SaveCredential(LicenseDbKey, info.Key);
        }

        // ── Remove saved license (reset to trial) ─────────────────────────────
        public static void ClearLicense()
        {
            AesEncryption.ClearCredential(LicenseDbKey);
        }

        // ── Install date for trial countdown ──────────────────────────────────
        private static DateTime GetInstallDate()
        {
            try
            {
                var stored = AesEncryption.LoadCredential(InstallDateKey, "");
                if (!string.IsNullOrEmpty(stored) && DateTime.TryParse(stored, out var d))
                    return d;
                var today = DateTime.Today;
                AesEncryption.SaveCredential(InstallDateKey, today.ToString("yyyy-MM-dd"));
                return today;
            }
            catch { return DateTime.Today; }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static string BuildMessage(LicenseTier tier, DateTime expiry)
        {
            if (expiry >= DateTime.Today.AddYears(99)) return $"{tier} — Lifetime";
            int days = (expiry - DateTime.Today).Days;
            return days > 0
                ? $"{tier} — {days} days remaining (expires {expiry:dd-MMM-yyyy})"
                : $"{tier} — EXPIRED on {expiry:dd-MMM-yyyy}";
        }

        private static string FormatKey(string raw)
        {
            raw = raw.Replace("-", "").ToUpper();
            if (raw.StartsWith("TDSPRO")) raw = raw[6..];
            var sb = new StringBuilder("TDSPRO");
            for (int i = 0; i < raw.Length; i++)
            {
                if (i % 6 == 0) sb.Append('-');
                sb.Append(raw[i]);
            }
            return sb.ToString();
        }

        private static LicenseInfo Invalid(string msg) =>
            new() { IsValid = false, Message = msg, Tier = LicenseTier.Trial };

        // ── Base32 (RFC 4648) — implementation shared with KeyGen via Common ──
        public static string Base32Encode(byte[] data) => CDeTDS.Common.Base32.Encode(data);
        public static byte[] Base32Decode(string s)    => CDeTDS.Common.Base32.Decode(s);
    }

    // ── Models ─────────────────────────────────────────────────────────────────
    public class LicenseInfo
    {
        public bool        IsValid      { get; set; }
        public string      Key          { get; set; } = "";
        public LicenseTier Tier         { get; set; } = LicenseTier.Trial;
        public DateTime    ExpiryDate   { get; set; } = DateTime.Today.AddDays(30);
        public int         MaxDeductors { get; set; } = 1;
        public int         MaxEntries   { get; set; } = 25;
        public int         MaxUsers     { get; set; } = 1;
        public string      MachineId    { get; set; } = "";
        public string      Message      { get; set; } = "";
        public bool        IsExpired    => ExpiryDate < DateTime.Today;
        public bool        IsReadOnly   => IsExpired;
        public int         DaysLeft     => Math.Max(0, (ExpiryDate - DateTime.Today).Days);
        public bool        IsTrial      => Tier == LicenseTier.Trial;
        public bool        IsWarningSoon => !IsExpired && DaysLeft <= 7;
        public string      TierLabel    => Tier == LicenseTier.Pro ? "Pro" : "Trial";
    }

    public enum LicenseTier { Trial = 0, Pro = 1 }
}
