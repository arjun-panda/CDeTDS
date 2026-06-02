using CDeTDS.DAL;

namespace CDeTDS.App
{
    /// <summary>
    /// Singleton that replaces Program.cs static state. Injected everywhere via DI.
    /// Raises OnChange so Blazor components can re-render when FY or Deductor switches.
    /// </summary>
    public class AppStateService
    {
        /// <summary>True when --login flag is passed on the command line (dev convenience).</summary>
        public bool AutoLogin { get; } =
            Environment.GetCommandLineArgs().Contains("--login", StringComparer.OrdinalIgnoreCase);

        public string AppDataPath     { get; set; } = "";
        public string CurrentUser     { get; private set; } = "";
        public string CurrentRole     { get; private set; } = "";
        // FY locked at login — the only FY where create / edit / delete is allowed.
        public string SessionFY       { get; private set; } = "";
        // FY currently being viewed in the UI — may differ from SessionFY when browsing past years.
        public string CurrentFY       { get; set; } = "";
        public int    CurrentDeductorId   { get; private set; } = 0;
        public string CurrentDeductorName { get; private set; } = "";
        public string CurrentDeductorTan  { get; private set; } = "";
        public LicenseInfo CurrentLicense { get; set; } = new();
        public bool IsLoggedIn => !string.IsNullOrEmpty(CurrentUser);

        public string CurrentAY =>
            CDeTDS.Common.TaxRules.AssessmentYearLabel(CurrentFY);

        /// <summary>True when the user is viewing a year other than the one they logged in to — UI must block create/edit/delete.</summary>
        public bool IsReadOnlyFY =>
            !string.IsNullOrEmpty(CurrentFY)
            && !string.IsNullOrEmpty(SessionFY)
            && CurrentFY != SessionFY;

        public event Action? OnChange;

        public void Login(string user, string role)
        {
            CurrentUser = user;
            CurrentRole = role;
            NotifyChanged();
        }

        public void Logout()
        {
            CurrentUser = "";
            CurrentRole = "";
            SessionFY = "";
            CurrentDeductorId = 0;
            CurrentDeductorName = "";
            CurrentDeductorTan = "";
            NotifyChanged();
        }

        /// <summary>Called once at login. Locks the editable FY for the rest of the session.</summary>
        public void SetSessionFY(string fy)
        {
            SessionFY = fy;
            CurrentFY = fy;
            NotifyChanged();
        }

        /// <summary>Switch the viewed FY for reference (read-only when ≠ SessionFY).</summary>
        public void SetFY(string fy)
        {
            CurrentFY = fy;
            NotifyChanged();
        }

        public void SetDeductor(int id, string name, string tan)
        {
            CurrentDeductorId   = id;
            CurrentDeductorName = name;
            CurrentDeductorTan  = tan;
            try { FolderManager.SetCompany(tan, name); } catch { }
            try { Database.SetSetting("LAST_DEDUCTOR_ID", id.ToString()); } catch { }
            MigrateGlobalCredentials(id);
            NotifyChanged();
        }

        // One-time migration: move old global credential keys → per-deductor keys.
        // Runs each time a deductor is selected; no-ops once scoped keys are populated.
        private static void MigrateGlobalCredentials(int id)
        {
            try
            {
                var pairs = new[]
                {
                    ("ItPortalUserId",  $"ItPortalUserId_{id}"),
                    ("ItPortalPan",     $"ItPortalPan_{id}"),
                    ("TracesUserId",    $"TracesUserId_{id}"),
                    ("TracesTan",       $"TracesTan_{id}"),
                    ("TinNsdlTan",      $"TinNsdlTan_{id}"),
                };
                foreach (var (oldKey, newKey) in pairs)
                {
                    if (!string.IsNullOrEmpty(Database.GetSetting(newKey))) continue; // already migrated
                    var val = Database.GetSetting(oldKey);
                    if (!string.IsNullOrEmpty(val))
                        Database.SetSetting(newKey, val);
                }

                // Encrypted credentials
                var credPairs = new[]
                {
                    ("ItPortalPassword",  $"ItPortalPassword_{id}"),
                    ("TracesPassword",    $"TracesPassword_{id}"),
                    ("TinNsdlPassword",   $"TinNsdlPassword_{id}"),
                };
                foreach (var (oldKey, newKey) in credPairs)
                {
                    if (!string.IsNullOrEmpty(AesEncryption.LoadCredential(newKey))) continue;
                    var val = AesEncryption.LoadCredential(oldKey);
                    if (!string.IsNullOrEmpty(val))
                        AesEncryption.SaveCredential(newKey, val);
                }
            }
            catch { }
        }

        public void NotifyStateChanged() => OnChange?.Invoke();

        private void NotifyChanged() => NotifyStateChanged();
    }
}
