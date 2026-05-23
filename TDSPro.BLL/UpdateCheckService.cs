using System.Net.Http;
using System.Text.Json;
using TDSPro.Common;
using TDSPro.DAL;

namespace TDSPro.BLL
{
    public class UpdateInfo
    {
        public string Version     { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string Notes       { get; set; } = "";
        public bool   IsNewer     { get; set; }
    }

    public class UpdateCheckService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

        /// <summary>
        /// Checks tdspro.in/version.json. Returns null if offline or already up-to-date.
        /// Only checks once per day (cached in app_settings).
        /// </summary>
        public async Task<UpdateInfo?> CheckAsync(bool forceCheck = false)
        {
            try
            {
                // Rate-limit: once per day (bypass when user manually triggers)
                if (!forceCheck)
                {
                    var lastCheck = Database.GetSetting("UPDATE_LAST_CHECK", "");
                    if (lastCheck == DateTime.Today.ToString("yyyy-MM-dd"))
                        return null;
                }

                var json = await _http.GetStringAsync(AppConstants.VersionCheckUrl);
                Database.SetSetting("UPDATE_LAST_CHECK", DateTime.Today.ToString("yyyy-MM-dd"));

                using var doc  = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var latest = root.GetProperty("version").GetString() ?? "";
                var info   = new UpdateInfo
                {
                    Version     = latest,
                    ReleaseDate = root.TryGetProperty("releaseDate", out var rd) ? rd.GetString() ?? "" : "",
                    DownloadUrl = root.TryGetProperty("downloadUrl", out var du) ? du.GetString() ?? "" : "",
                    Notes       = root.TryGetProperty("notes",       out var n)  ? n.GetString()  ?? "" : "",
                    IsNewer     = IsNewerVersion(latest, AppConstants.AppVersion)
                };

                return info.IsNewer ? info : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsNewerVersion(string remote, string local)
        {
            try
            {
                var r = Version.Parse(remote);
                var l = Version.Parse(local);
                return r > l;
            }
            catch { return false; }
        }
    }
}
