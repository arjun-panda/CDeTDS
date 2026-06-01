using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CDeTDS.DAL
{
    // ─────────────────────────────────────────────────────────────────────────
    // PAN VERIFICATION — 3-TIER SYSTEM
    //
    // Tier 1 (Instant, offline):  Format check + entity decode  → <1ms
    // Tier 2 (Browser):           Opens official IT Portal "Verify PAN Status"
    //                             Pre-login service — no account needed
    //                             URL: https://eportal.incometax.gov.in/iec/
    //                                  foservices/#/pre-login/verifyYourPAN/
    // Tier 3 (API, optional):     Paid API key via Settings (Surepass/Karza)
    //                             Programmatic verification if key is configured
    // ─────────────────────────────────────────────────────────────────────────

    public class PanVerifyResult
    {
        public bool   IsValid        { get; set; }
        public string Status         { get; set; } = "";
        public string Message        { get; set; } = "";
        public string Name           { get; set; } = "";
        public string EntityType     { get; set; } = "";
        public string EntityLabel    { get; set; } = "";
        public bool   IsNetworkError { get; set; }
        public bool   NeedsLogin     { get; set; }
        public string Provider       { get; set; } = "";
        public bool   IsFormatOnly   { get; set; }  // true = offline check only

        public static PanVerifyResult FormatError(string r)
            => new() { IsValid=false, Status="FormatError", Message=r, IsFormatOnly=true };
        public static PanVerifyResult FormatOk(string entity, string label)
            => new() { IsValid=true,  Status="FormatOk",   Message=$"Valid format — {label}",
                       EntityType=entity, EntityLabel=label, IsFormatOnly=true };
        public static PanVerifyResult NetworkError(string d)
            => new() { IsValid=false, Status="NetworkError",  Message=d, IsNetworkError=true };
        public static PanVerifyResult Valid(string n, string p="IT Portal")
            => new() { IsValid=true,  Status="Verified", Name=n, Message=$"Verified ✓  {n}", Provider=p };
        public static PanVerifyResult Invalid(string m)
            => new() { IsValid=false, Status="Invalid", Message=m };
        public static PanVerifyResult Deactivated(string n)
            => new() { IsValid=false, Status="Deactivated", Name=n, Message=$"PAN deactivated — {n}" };
    }

    public class PanVerificationService
    {
        // Official IT Portal pre-login PAN verification (no login required)
        public const string IT_PORTAL_VERIFY_URL =
            "https://eportal.incometax.gov.in/iec/foservices/#/pre-login/verifyYourPAN/";

        private static readonly Regex _panRx =
            new(@"^[A-Z]{3}[PCBHFATLJG][A-Z]\d{4}[A-Z]$", RegexOptions.Compiled);

        // Entity type map — 4th character of PAN
        private static readonly Dictionary<char, (string Code, string Label)> EntityMap = new()
        {
            ['P'] = ("Individual",     "Individual (Person)"),
            ['C'] = ("Company",        "Company"),
            ['H'] = ("HUF",            "Hindu Undivided Family (HUF)"),
            ['F'] = ("Firm",           "Firm / LLP"),
            ['A'] = ("AOP",            "Association of Persons (AOP)"),
            ['T'] = ("Trust",          "Trust / AOP (Trusts)"),
            ['B'] = ("BOI",            "Body of Individuals (BOI)"),
            ['L'] = ("LocalAuthority", "Local Authority"),
            ['G'] = ("Government",     "Government"),
            ['J'] = ("AJP",            "Artificial Juridical Person"),
        };

        // ── Tier 1: Instant offline format + structure check ─────────────────
        public PanVerifyResult CheckFormat(string pan)
        {
            if (string.IsNullOrWhiteSpace(pan))
                return PanVerifyResult.FormatError("PAN is required.");
            pan = pan.Trim().ToUpper();
            if (pan.Length != 10)
                return PanVerifyResult.FormatError(
                    $"PAN must be exactly 10 characters ({pan.Length} entered).");
            if (!_panRx.IsMatch(pan))
            {
                char c4 = pan.Length >= 4 ? pan[3] : ' ';
                if (!"PCBHFATLJG".Contains(c4))
                    return PanVerifyResult.FormatError(
                        $"4th character '{c4}' is invalid.\n" +
                        "P=Individual  C=Company  H=HUF  F=Firm/LLP  A=AOP\n" +
                        "T=Trust  B=BOI  L=Local Authority  G=Govt  J=AJP");
                return PanVerifyResult.FormatError(
                    "Invalid PAN format. Required: AAA_A_####_A  (e.g. AHLPP4511D)");
            }
            var (code, label) = EntityMap[pan[3]];
            return PanVerifyResult.FormatOk(code, label);
        }

        // ── Tier 2: Opens official IT Portal in browser (pre-login, no account needed)
        public static void OpenVerifyOnPortal(string? pan = null)
        {
            // Note: IT Portal verify URL does not support PAN pre-fill via URL params
            // (it's a JS SPA). User enters PAN + Name + DOB + Mobile + OTP on portal.
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(IT_PORTAL_VERIFY_URL)
                { UseShellExecute = true });
        }

        // ── Tier 3: Optional 3rd-party API (if key configured in Settings) ───
        public async Task<PanVerifyResult> VerifyViaApiAsync(string pan)
        {
            pan = pan.Trim().ToUpper();
            var fmt = CheckFormat(pan);
            if (!fmt.IsValid) return fmt;

            // Check for configured API key
            var apiKey  = AesEncryption.LoadCredential("PAN_API_KEY",  "");
            var apiProv = AesEncryption.LoadCredential("PAN_API_PROV", "");

            if (string.IsNullOrEmpty(apiKey))
                return new PanVerifyResult
                {
                    IsValid       = false,
                    Status        = "NoApiKey",
                    Message       = "No API key configured. Set up in Settings → PAN Verification.",
                    IsFormatOnly  = true,
                };

            // 90-day cache
            var cached = GetCached(pan);
            if (cached != null) return cached;

            try
            {
                PanVerifyResult result;
                if (apiProv == "Surepass")
                    result = await VerifyViaSurepass(pan, apiKey);
                else if (apiProv == "Karza")
                    result = await VerifyViaKarza(pan, apiKey);
                else
                    result = PanVerifyResult.Invalid($"Unknown API provider: {apiProv}");

                if (result.IsValid)
                    SaveCache(pan, result);

                return result;
            }
            catch (TaskCanceledException)
            {
                return PanVerifyResult.NetworkError("API timed out. Check internet connection.");
            }
            catch (Exception ex)
            {
                return PanVerifyResult.NetworkError($"API error: {ex.Message}");
            }
        }

        // ── Surepass PAN verification API ────────────────────────────────────
        private static async Task<PanVerifyResult> VerifyViaSurepass(
            string pan, string apiKey)
        {
            using var client = new System.Net.Http.HttpClient
                { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add(
                "Authorization", $"Bearer {apiKey}");

            var body = new System.Net.Http.StringContent(
                $"{{\"id_number\":\"{pan}\"}}",
                System.Text.Encoding.UTF8, "application/json");

            var resp = await client.PostAsync(
                "https://kyc-api.surepass.io/api/v1/pan/pan", body);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return PanVerifyResult.Invalid($"Surepass error {resp.StatusCode}: {json}");

            // Parse response
            if (json.Contains("\"success\":true") || json.Contains("\"status\":\"active\""))
            {
                var nameM = System.Text.RegularExpressions.Regex
                    .Match(json, "\"full_name\"\\s*:\\s*\"([^\"]+)\"");
                string name = nameM.Success ? nameM.Groups[1].Value : "";
                return PanVerifyResult.Valid(name, "Surepass");
            }
            if (json.Contains("INVALID") || json.Contains("invalid"))
                return PanVerifyResult.Invalid("PAN is invalid per Surepass.");
            if (json.Contains("DEACTIVATED") || json.Contains("deactivated"))
                return PanVerifyResult.Deactivated("PAN deactivated per Surepass.");

            return PanVerifyResult.NetworkError("Surepass returned unexpected response.");
        }

        // ── Karza PAN verification API ────────────────────────────────────────
        private static async Task<PanVerifyResult> VerifyViaKarza(
            string pan, string apiKey)
        {
            using var client = new System.Net.Http.HttpClient
                { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("x-karza-key", apiKey);

            var body = new System.Net.Http.StringContent(
                $"{{\"pan\":\"{pan}\",\"consent\":\"y\"}}",
                System.Text.Encoding.UTF8, "application/json");

            var resp = await client.PostAsync(
                "https://api.karza.in/v2/pan-status", body);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return PanVerifyResult.Invalid($"Karza error {resp.StatusCode}");

            if (json.Contains("\"VALID\"") || json.Contains("\"ACTIVE\""))
            {
                var nameM = System.Text.RegularExpressions.Regex
                    .Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                string name = nameM.Success ? nameM.Groups[1].Value : "";
                return PanVerifyResult.Valid(name, "Karza");
            }
            return PanVerifyResult.Invalid("PAN not verified by Karza.");
        }

        // ── 90-day cache ──────────────────────────────────────────────────────
        private static PanVerifyResult? GetCached(string pan)
        {
            try
            {
                var raw = AesEncryption.LoadCredential($"PAN_CACHE_{pan}", "");
                if (string.IsNullOrEmpty(raw)) return null;
                var parts = raw.Split('|');
                if (parts.Length < 3) return null;
                if (!DateTime.TryParse(parts[0], out var dt)) return null;
                if ((DateTime.UtcNow - dt).TotalDays > 90) return null;
                bool isValid = parts[1] == "1";
                string name  = parts[2];
                return isValid
                    ? PanVerifyResult.Valid(name, "Cache")
                    : PanVerifyResult.Invalid("PAN invalid (cached)");
            }
            catch { return null; }
        }

        private static void SaveCache(string pan, PanVerifyResult r)
        {
            try
            {
                var data = $"{DateTime.UtcNow:O}|{(r.IsValid?1:0)}|{r.Name}";
                AesEncryption.SaveCredential($"PAN_CACHE_{pan}", data);
            }
            catch { }
        }

        // ── Audit log ─────────────────────────────────────────────────────────
        public static void LogAudit(string pan, PanVerifyResult r)
        {
            try
            {
                CDeTDS.DAL.Database.LogAction("System", "PAN_VERIFY",
                    "PanVerification",
                    $"PAN={pan} Status={r.Status} Provider={r.Provider} Name={r.Name}");
            }
            catch { }
        }

        // ── Helper: get stored preferences ────────────────────────────────────
        public static string GetPref(string key, string def = "")
        {
            try { return AesEncryption.LoadCredential(key, def); }
            catch { return def; }
        }
    }
}
