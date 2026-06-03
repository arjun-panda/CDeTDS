using CDeTDS.Common;

namespace CDeTDS.DAL
{
    /// <summary>
    /// Manages the CDeTDS file/folder structure.
    ///
    /// Structure:
    ///   BasePath\
    ///     {FY}\         e.g. 2025-26\
    ///       Q1\         Returns\ FVU\ Reports\ Challans\ Justification\ Conso\
    ///       Q2\         ...
    ///       Q3\
    ///       Q4\
    ///     Backup\
    ///     Temp\
    ///
    /// Also handles:
    ///   - Auto FY detection from a date
    ///   - Auto quarter detection from a date
    ///   - Auto-naming files as FormType_Quarter_FY_Date.ext
    ///   - Daily auto-backup (keeps last 30)
    /// </summary>
    public static class FolderManager
    {
        private static string _basePath = "";
        private static string _companyFolder = "";  // set by SetCompany() when deductor is selected

        public static string BasePath
        {
            get
            {
                if (!string.IsNullOrEmpty(_basePath)) return _basePath;
                var defaultPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CDeTDS");
                var stored = LoadSetting("BASE_FOLDER", defaultPath);
                // If the stored path is on a drive that doesn't exist or isn't ready,
                // silently fall back to Documents\CDeTDS and persist the reset.
                try
                {
                    var root = Path.GetPathRoot(stored);
                    if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
                    {
                        stored = defaultPath;
                        SaveSetting("BASE_FOLDER", stored);
                    }
                }
                catch { stored = defaultPath; }
                _basePath = stored;
                return _basePath;
            }
        }

        /// <summary>
        /// Root folder for the currently selected company.
        /// Falls back to BasePath if no company is set.
        /// Structure: BasePath\Companies\{TAN}_{SafeName}\
        /// </summary>
        public static string CompanyFolder
        {
            get
            {
                if (!string.IsNullOrEmpty(_companyFolder)) return _companyFolder;
                return BasePath;
            }
        }

        /// <summary>
        /// Call this whenever the selected deductor changes.
        /// Creates the company folder structure immediately.
        /// </summary>
        public static void SetCompany(string tan, string companyName)
        {
            if (string.IsNullOrEmpty(tan)) { _companyFolder = ""; return; }
            var safeName = string.Concat(companyName.ToUpper()
                .Where(c => char.IsLetterOrDigit(c) || c == ' ')
                .Take(20))
                .Trim().Replace(' ', '_');
            _companyFolder = Path.Combine(BasePath, "Companies", $"{tan}_{safeName}");
            Directory.CreateDirectory(_companyFolder);
            Directory.CreateDirectory(SalarySlipsFolder);
            Directory.CreateDirectory(PayrollRunsFolder);
            Directory.CreateDirectory(Form16Folder);
            Directory.CreateDirectory(ExportsFolder);
            Directory.CreateDirectory(TemplatesFolder);
        }

        // ── Company sub-folders ───────────────────────────────────────────────
        public static string SalarySlipsFolder              => Path.Combine(CompanyFolder, "SalarySlips");
        public static string SalarySlipsForFyFolder(string fy) => Path.Combine(SalarySlipsFolder, fy);
        public static string PayrollRunsFolder              => Path.Combine(CompanyFolder, "PayrollRuns");
        public static string Form16Folder                   => Path.Combine(CompanyFolder, "Form16");
        public static string Form16ForFyFolder(string fy)  => Path.Combine(Form16Folder, fy);
        public static string Form16AForFyFolder(string fy) => Path.Combine(Form16Folder, "16A", fy);
        public static string Form27DForFyFolder(string fy) => Path.Combine(Form16Folder, "27D", fy);
        public static string ExportsFolder                  => Path.Combine(CompanyFolder, "Exports");
        public static string TemplatesFolder                => Path.Combine(CompanyFolder, "Templates");

        // ── Folder paths ──────────────────────────────────────────────────────
        public static string FyFolder(string fy)
            => Path.Combine(CompanyFolder, fy);

        /// <summary>
        /// Return output folder: {Company}\{FY}\Returns\{formType}\{quarter}\
        /// e.g. Companies\DELA...\2025-26\Returns\26Q\Q1\
        /// </summary>
        public static string ReturnFolder(string fy, string formType, string quarter)
            => Path.Combine(FyFolder(fy), "Returns", formType.ToUpper(), quarter.ToUpper());

        // Legacy — kept for Reports and non-return sub-folders (Reports, Challans, etc.)
        public static string QuarterFolder(string fy, string quarter)
            => Path.Combine(FyFolder(fy), quarter);

        public static string SubFolder(string fy, string quarter, string sub)
            => Path.Combine(QuarterFolder(fy, quarter), sub);

        public static string BackupFolder
            => Path.Combine(BasePath, "Backup");

        public static string TempFolder
            => Path.Combine(BasePath, "Temp");

        /// <summary>
        /// Folder where CSI files should be saved. Place .csi files downloaded from
        /// IT Portal / OLTAS here — FVU will pick them up automatically.
        /// </summary>
        public static string CsiFolder
            => Path.Combine(BasePath, "CSI");

        // Sub-folder names
        public static readonly string[] SubFolders =
            { "Returns", "FVU", "Reports", "Challans", "Justification", "Conso" };

        // ── Create full structure ─────────────────────────────────────────────
        public static void EnsureStructure(string fy, string? quarter = null)
        {
            Directory.CreateDirectory(BasePath);
            Directory.CreateDirectory(BackupFolder);
            Directory.CreateDirectory(TempFolder);
            Directory.CreateDirectory(CsiFolder);
            Directory.CreateDirectory(FyFolder(fy));

            var quarters = quarter != null
                ? new[] { quarter }
                : AppConstants.QuarterCodes;

            // New: Returns\{formType}\{quarter}\ structure
            foreach (var form in new[] { "26Q", "24Q" })
                foreach (var q in quarters)
                    Directory.CreateDirectory(ReturnFolder(fy, form, q));

            // Legacy quarter sub-folders (Reports, Challans, etc.) — keep for non-return files
            foreach (var q in quarters)
            {
                Directory.CreateDirectory(QuarterFolder(fy, q));
                foreach (var sub in SubFolders)
                    Directory.CreateDirectory(SubFolder(fy, q, sub));
            }
        }

        /// <summary>Create full structure for all FYs in the database.</summary>
        public static void EnsureAllStructures()
        {
            foreach (var fy in AppConstants.FinancialYears)
                EnsureStructure(fy);
        }

        // ── Auto file naming ──────────────────────────────────────────────────
        /// <summary>
        /// Generate standardised file name:
        /// FormType_Quarter_FY_Date.ext
        /// e.g. 26Q_Q1_2025-26_20260412.txt
        /// </summary>
        public static string AutoFileName(string formType, string quarter, string fy,
            string extension, DateTime? date = null)
        {
            var d   = (date ?? DateTime.Today).ToString("yyyyMMdd");
            var fyS = fy.Replace("-", "");
            return $"{formType}_{quarter}_{fyS}_{d}.{extension.TrimStart('.')}";
        }

        /// <summary>Get the FVU output path for a return.</summary>
        public static string FvuFilePath(string formType, string quarter, string fy, string tan)
        {
            var folder = ReturnFolder(fy, formType, quarter);
            Directory.CreateDirectory(folder);
            var fileName = $"{formType}_{tan}_{quarter}_{fy.Replace("-","")}.txt";
            return Path.Combine(folder, fileName);
        }

        /// <summary>Get the Reports path.</summary>
        public static string ReportFilePath(string reportName, string fy, string quarter, string ext)
        {
            EnsureStructure(fy, quarter);
            var d = DateTime.Today.ToString("yyyyMMdd");
            return Path.Combine(SubFolder(fy, quarter, "Reports"), $"{reportName}_{d}.{ext}");
        }

        // ── Auto FY & Quarter detection ───────────────────────────────────────
        /// <summary>
        /// Detect Financial Year from a date (April–March cycle).
        /// Apr 2026 → "2026-27"
        /// Jan 2026 → "2025-26"
        /// </summary>
        public static string DetectFY(DateTime date)
        {
            int startYear = date.Month >= 4 ? date.Year : date.Year - 1;
            int endYear   = startYear + 1;
            return $"{startYear}-{endYear.ToString()[^2..]}";
        }

        /// <summary>
        /// Detect Quarter from a date.
        /// Q1: Apr-Jun, Q2: Jul-Sep, Q3: Oct-Dec, Q4: Jan-Mar
        /// </summary>
        public static string DetectQuarter(DateTime date)
            => date.Month switch
            {
                4 or 5 or 6  => "Q1",
                7 or 8 or 9  => "Q2",
                10 or 11 or 12 => "Q3",
                _            => "Q4",
            };

        /// <summary>Quarter display label (e.g. "Q1 (Apr-Jun)").</summary>
        public static string QuarterLabel(string qCode) => qCode switch
        {
            "Q1" => "Q1 (Apr-Jun)",
            "Q2" => "Q2 (Jul-Sep)",
            "Q3" => "Q3 (Oct-Dec)",
            "Q4" => "Q4 (Jan-Mar)",
            _    => qCode,
        };

        /// <summary>TDS return due date for a quarter.</summary>
        public static DateTime ReturnDueDate(string fy, string quarter)
        {
            int startYear = int.Parse(fy.Split('-')[0]);
            return quarter switch
            {
                "Q1" => new DateTime(startYear,     7,  31),
                "Q2" => new DateTime(startYear,    10,  31),
                "Q3" => new DateTime(startYear + 1,  1,  31),
                "Q4" => new DateTime(startYear + 1,  5,  31),
                _    => DateTime.Today,
            };
        }

        /// <summary>Challan deposit due date: 7th of following month (30th for March).</summary>
        public static DateTime ChallanDueDate(DateTime txDate)
        {
            if (txDate.Month == 3) return new DateTime(txDate.Year, 3, 30);
            var next = txDate.AddMonths(1);
            return new DateTime(next.Year, next.Month, 7);
        }

        // ── Daily auto-backup ─────────────────────────────────────────────────
        public static void RunDailyAutoBackup(string dbPath, int keepDays = 30)
        {
            try
            {
                Directory.CreateDirectory(BackupFolder);
                var today      = DateTime.Today.ToString("yyyyMMdd");
                var backupFile = Path.Combine(BackupFolder, $"cdetds_auto_{today}.db");

                // Only backup once per day
                if (File.Exists(backupFile)) return;

                File.Copy(dbPath, backupFile, overwrite: true);

                // Prune old backups — keep last `keepDays`
                var backups = Directory.GetFiles(BackupFolder, "cdetds_auto_*.db")
                                       .OrderByDescending(f => f)
                                       .Skip(keepDays)
                                       .ToArray();
                foreach (var old in backups)
                    try { File.Delete(old); } catch { }

                Database.LogAction("system", "AUTO_BACKUP", "Backup",
                    $"Saved: {backupFile}. Pruned: {backups.Length} old backups.");
            }
            catch { /* Silent — never crash app due to backup failure */ }
        }

        /// <summary>Manual on-demand backup with timestamp — always creates a new file (no once-per-day gate).</summary>
        public static string? BackupNow(string dbPath)
        {
            try
            {
                Directory.CreateDirectory(BackupFolder);
                var stamp      = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var backupFile = Path.Combine(BackupFolder, $"cdetds_manual_{stamp}.db");
                File.Copy(dbPath, backupFile, overwrite: false);
                Database.LogAction("user", "MANUAL_BACKUP", "Backup", $"Saved: {backupFile}");
                return backupFile;
            }
            catch { return null; }
        }

        /// <summary>List all backup files (auto + manual) sorted newest-first.</summary>
        public static List<BackupInfo> ListBackups()
        {
            try
            {
                Directory.CreateDirectory(BackupFolder);
                return Directory.GetFiles(BackupFolder, "cdetds_*.db")
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Select(f => new BackupInfo
                    {
                        Path       = f.FullName,
                        FileName   = f.Name,
                        CreatedAt  = f.LastWriteTime,
                        SizeBytes  = f.Length,
                        IsAuto     = f.Name.StartsWith("cdetds_auto_", StringComparison.OrdinalIgnoreCase),
                    })
                    .ToList();
            }
            catch { return new(); }
        }

        // ── Open folder in Explorer ───────────────────────────────────────────
        public static void OpenFolder(string path)
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start("explorer.exe", path);
        }

        public static void OpenCurrentQuarterFolder()
        {
            var fy = DetectFY(DateTime.Today);
            var q  = DetectQuarter(DateTime.Today);
            OpenFolder(QuarterFolder(fy, q));
        }

        public static void OpenReturnFolder(string fy, string quarter)
            => OpenFolder(SubFolder(fy, quarter, "Returns"));

        public static void OpenFvuFolder(string fy, string formType, string quarter)
            => OpenFolder(ReturnFolder(fy, formType, quarter));

        // Legacy overload — opens 26Q folder by default
        public static void OpenFvuFolder(string fy, string quarter)
            => OpenFolder(ReturnFolder(fy, "26Q", quarter));

        // ── Base path config ──────────────────────────────────────────────────
        public static void SetBasePath(string newPath)
        {
            _basePath = newPath;
            SaveSetting("BASE_FOLDER", newPath);
        }

        private static string LoadSetting(string key, string def)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = "SELECT notes FROM fvu_format_config WHERE config_key=@k";
                cmd.Parameters.AddWithValue("@k", "SETTING_" + key);
                return cmd.ExecuteScalar() as string ?? def;
            }
            catch { return def; }
        }

        private static void SaveSetting(string key, string value)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO fvu_format_config (config_key,form_type,version,effective_from,notes)
                    VALUES(@k,'SETTING','1.0','2026-04-01',@v)
                    ON CONFLICT(config_key) DO UPDATE SET notes=excluded.notes";
                cmd.Parameters.AddWithValue("@k", "SETTING_" + key);
                cmd.Parameters.AddWithValue("@v", value);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }

    /// <summary>Metadata for one backup file shown in the Settings → Backup &amp; Restore page.</summary>
    public class BackupInfo
    {
        public string   Path      { get; set; } = "";
        public string   FileName  { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public long     SizeBytes { get; set; }
        public bool     IsAuto    { get; set; }
        public string   SizeLabel => SizeBytes switch
        {
            < 1024            => $"{SizeBytes} B",
            < 1024 * 1024     => $"{SizeBytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{SizeBytes / 1048576.0:F1} MB",
            _                 => $"{SizeBytes / 1073741824.0:F2} GB",
        };
    }
}
