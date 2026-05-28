using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TDSPro.DAL
{
    /// <summary>
    /// Downloads the NSDL CSI file from the IT Portal.
    /// Two-step TAN login:
    ///   Step 1 — POST /iec/loginapi/login  {entity, serviceName:"wLoginService"}  → returns reqId
    ///   Step 2 — POST /iec/loginapi/login  {entity, reqId, pass:base64(pwd), serviceName:"loginService", role:"TDS", ...}
    /// </summary>
    public static class CsiDownloader
    {
        private const string BaseUrl  = "https://eportal.incometax.gov.in";
        private const string LoginUrl = "/iec/loginapi/login";

        // CSI download — discovered from browser network tab after login
        // POST with {tan, quarter(1-4), financialYear(start year)} → returns file bytes
        private const string CsiUrl   = "/iec/foservices/api/tdsServices/generateCsiFileForTan";

        public class CsiResult
        {
            public bool   Success  { get; init; }
            public string FilePath { get; init; } = "";
            public string Message  { get; init; } = "";
        }

        public static async Task<CsiResult> DownloadAsync(string quarter, string fy,
            IProgress<string>? progress = null, int deductorId = 0)
        {
            var key      = deductorId > 0 ? $"ItPortalUserId_{deductorId}" : "ItPortalUserId";
            var passKey  = deductorId > 0 ? $"ItPortalPassword_{deductorId}" : "ItPortalPassword";
            var tan      = Database.GetSetting(key);
            var password = AesEncryption.LoadCredential(passKey);

            if (string.IsNullOrWhiteSpace(tan))
                return Fail("IT Portal TAN not configured. Set it in Portals page.");
            if (string.IsNullOrWhiteSpace(password))
                return Fail("IT Portal password not saved. Set it in Portals page.");

            tan = tan.Trim().ToUpper();

            try
            {
                var cookies = new CookieContainer();
                using var handler = new HttpClientHandler
                {
                    CookieContainer   = cookies,
                    UseCookies        = true,
                    AllowAutoRedirect = true,
                };
                using var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
                http.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124 Safari/537.36");
                http.DefaultRequestHeaders.Add("Origin",  "https://eportal.incometax.gov.in");
                http.DefaultRequestHeaders.Add("Referer", "https://eportal.incometax.gov.in/iec/foservices/");
                // ── Step 0: load portal home to get session cookies ───────────
                progress?.Report("Step 0: Loading portal session…");
                await http.GetAsync("/iec/foservices/");

                // ── Step 1: pre-login (get reqId) ─────────────────────────────
                progress?.Report("Step 1: Connecting to IT Portal…");

                var step1 = JsonSerializer.Serialize(new
                {
                    entity      = tan,
                    serviceName = "wLoginService",
                });
                var r1 = await PostJsonWithHeaders(http, LoginUrl, step1,
                    new[] { ("caller", "eF") });
                if (!r1.ok)
                    return Fail($"Pre-login failed (HTTP {r1.status}): {r1.body[..Math.Min(200, r1.body.Length)]}");

                var doc1  = JsonNode.Parse(r1.body);
                var reqId = doc1?["reqId"]?.GetValue<string>() ?? "";
                if (string.IsNullOrEmpty(reqId))
                    return Fail($"Pre-login: reqId not found. Response: {r1.body[..Math.Min(300, r1.body.Length)]}");

                // Extract XSRF / CSRF token from cookies — portal requires it as a header in Step 2
                var allCookies  = cookies.GetAllCookies().Cast<Cookie>().ToList();
                var xsrfToken   = allCookies.FirstOrDefault(c =>
                    c.Name.Equals("XSRF-TOKEN",  StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Equals("_csrf",        StringComparison.OrdinalIgnoreCase) ||
                    c.Name.Equals("csrf-token",   StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                var cookieDebug = string.Join(", ", allCookies.Select(c => c.Name));
                progress?.Report($"Step 1 OK. reqId={reqId}. Cookies: {cookieDebug}. XSRF={xsrfToken[..Math.Min(8, xsrfToken.Length)]}..");

                // ── Step 2: password submit ────────────────────────────────────
                progress?.Report("Step 2: Submitting credentials…");

                var passB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
                var step2 = JsonSerializer.Serialize(new
                {
                    errors                = Array.Empty<object>(),
                    reqId                 = reqId,
                    entity                = tan,
                    entityType            = "User ID",
                    role                  = "TDS",
                    uidValdtnFlg          = "true",
                    aadhaarMobileValidated = "false",
                    secAccssMsg           = "",
                    secLoginOptions       = "",
                    dtoService            = "LOGIN",
                    exemptedPan           = "false",
                    userConsent           = "",
                    imgByte               = (string?)null,
                    pass                  = passB64,
                    passValdtnFlg         = (string?)null,
                    otpGenerationFlag     = (string?)null,
                    otp                   = (string?)null,
                    otpValdtnFlg          = (string?)null,
                    otpSourceFlag         = (string?)null,
                    contactPan            = (string?)null,
                    contactMobile         = (string?)null,
                    contactEmail          = (string?)null,
                    email                 = (string?)null,
                    mobileNo              = (string?)null,
                    forgnDirEmailId       = (string?)null,
                    imagePath             = (string?)null,
                    serviceName           = "loginService",
                });

                // Build Step 2 headers — include XSRF token if present
                var step2Headers = new List<(string, string)> { ("sn", "loginService") };
                if (!string.IsNullOrEmpty(xsrfToken))
                {
                    step2Headers.Add(("X-XSRF-TOKEN",  xsrfToken));
                    step2Headers.Add(("X-CSRF-TOKEN",  xsrfToken));
                }
                progress?.Report($"Step 2 sending… passB64={passB64[..Math.Min(10,passB64.Length)]}..");
                var r2 = await PostJsonWithHeaders(http, LoginUrl, step2, step2Headers.ToArray());
                if (!r2.ok)
                    return Fail($"Login failed (HTTP {r2.status}): {r2.body[..Math.Min(300, r2.body.Length)]}");

                var doc2 = JsonNode.Parse(r2.body);
                var code = doc2?["messages"]?[0]?["code"]?.GetValue<string>() ?? "";
                if (code != "EF00000")
                {
                    var desc = doc2?["messages"]?[0]?["desc"]?.GetValue<string>() ?? "";
                    return Fail($"Login error [{code}]: {desc}\nFull: {r2.body[..Math.Min(500, r2.body.Length)]}");
                }

                progress?.Report("Login successful. Downloading CSI file…");

                // ── Step 3: Download CSI ──────────────────────────────────────
                int qNum    = quarter switch { "Q1" => 1, "Q2" => 2, "Q3" => 3, _ => 4 };
                int fyStart = int.Parse(fy.Split('-')[0]);

                var csiPayload = JsonSerializer.Serialize(new
                {
                    tan           = tan,
                    quarter       = qNum,
                    financialYear = fyStart,
                });
                var r3 = await PostJson(http, CsiUrl, csiPayload);

                if (!r3.ok)
                    return Fail($"CSI download failed (HTTP {r3.status}): {r3.body[..Math.Min(300, r3.body.Length)]}");

                // If JSON error came back
                if (r3.contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                    return Fail($"CSI endpoint returned: {r3.body[..Math.Min(400, r3.body.Length)]}");

                var bytes = r3.bytes;
                if (bytes.Length < 10)
                    return Fail("Downloaded file is empty.");

                // ── Step 4: Save ──────────────────────────────────────────────
                Directory.CreateDirectory(FolderManager.CsiFolder);
                var fyShort  = fy.Replace("-", "");
                var fileName = $"{tan}_Q{qNum}_{fyShort}.csi";
                var filePath = Path.Combine(FolderManager.CsiFolder, fileName);
                await File.WriteAllBytesAsync(filePath, bytes);

                Database.LogAction("user", "CSI_DOWNLOAD", "CsiDownloader",
                    $"Saved {fileName} ({bytes.Length} bytes)");

                progress?.Report($"Saved: {filePath}");
                return new CsiResult { Success = true, FilePath = filePath,
                    Message = $"CSI saved: {fileName} ({bytes.Length} bytes)" };
            }
            catch (HttpRequestException hEx) { return Fail($"Network error: {hEx.Message}"); }
            catch (Exception ex)             { return Fail($"Error: {ex.Message}"); }
        }

        private static Task<(bool ok, int status, string body, string contentType, byte[] bytes)>
            PostJson(HttpClient http, string url, string json)
            => PostJsonWithHeaders(http, url, json, Array.Empty<(string, string)>());

        private static async Task<(bool ok, int status, string body, string contentType, byte[] bytes)>
            PostJsonWithHeaders(HttpClient http, string url, string json, (string name, string value)[] headers)
        {
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            foreach (var (name, value) in headers)
                req.Headers.TryAddWithoutValidation(name, value);
            var resp        = await http.SendAsync(req);
            var bytes       = await resp.Content.ReadAsByteArrayAsync();
            var ct          = resp.Content.Headers.ContentType?.MediaType ?? "";
            var body        = ct.Contains("json") || ct.Contains("text")
                              ? Encoding.UTF8.GetString(bytes)
                              : $"[binary {bytes.Length} bytes]";
            return ((int)resp.StatusCode is >= 200 and < 300, (int)resp.StatusCode, body, ct, bytes);
        }

        private static CsiResult Fail(string msg) => new() { Success = false, Message = msg };
    }
}
