using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MudBlazor.Services;
using System.IO;
using System.Windows;
using CDeTDS.App.Services;
using CDeTDS.BLL;
using CDeTDS.DAL;
using Velopack;
using Velopack.Sources;

namespace CDeTDS.App
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;
        private static string _logPath = "";

        public App()
        {
            // Catch any unhandled exception and write to crash log before crashing
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var msg = e.ExceptionObject?.ToString() ?? "Unknown error";
                TryLog("UNHANDLED: " + msg);
                TryCrashLog(msg);
                if (e.IsTerminating)
                    MessageBox.Show(
                        "CDeTDS encountered a fatal error and must close.\n\n" +
                        "A crash log has been saved to:\n" + CrashLogPath() + "\n\n" +
                        "Please send this file to admin@capitaldesk.co.in for support.",
                        "CDeTDS — Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            DispatcherUnhandledException += (s, e) =>
            {
                TryLog("DISPATCHER: " + e.Exception?.ToString());
                TryCrashLog(e.Exception?.ToString() ?? "");
                MessageBox.Show(
                    "An error occurred:\n\n" + e.Exception?.Message + "\n\n" +
                    "The application will try to continue. If this persists, restart CDeTDS.\n" +
                    "Crash log: " + CrashLogPath(),
                    "CDeTDS — Error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };

            try
            {
                var services = new ServiceCollection();
                ConfigureServices(services);
                Services = services.BuildServiceProvider();
            }
            catch (Exception ex)
            {
                TryLog("DI BUILD FAILED: " + ex);
                MessageBox.Show("Failed to initialise services:\n\n" + ex.Message, "CDeTDS", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            services.AddWpfBlazorWebView();
            services.AddMudServices();

            services.AddSingleton<AppStateService>();
            services.AddSingleton<FilePickerService>();
            services.AddSingleton<CsiDownloadService>();
            services.AddSingleton<ItPortalUploadService>();

            // BLL
            services.AddTransient<DeductorService>();
            services.AddTransient<DeducteeService>();
            services.AddTransient<TdsEntryService>();
            services.AddTransient<ChallanService>();
            services.AddTransient<DashboardService>();
            services.AddTransient<PayrollService>();
            services.AddTransient<SalaryService>();
            services.AddTransient<MonthlyCloseService>();
            services.AddTransient<DeductionScheduleService>();
            services.AddTransient<TdsRulesService>();
            services.AddTransient<ReportsService>();
            services.AddTransient<ReturnService>();
            services.AddTransient<RulesUpdateService>();

            // DAL
            services.AddTransient<LicenseService>();
            services.AddSingleton<UpdateCheckService>();
            services.AddTransient<DueDateService>();
            services.AddTransient<PanVerificationService>();

#if DEBUG
            services.AddBlazorWebViewDeveloperTools();
#endif
        }

        // Cached once per process — registry reads are slow on some systems
        private static bool? _webView2Installed = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CDeTDS");
                Directory.CreateDirectory(appDataPath);
                _logPath = Path.Combine(appDataPath, "blazor_startup.log");
                TryLog("=== CDeTDS.App STARTUP ===");

                var state = Services.GetRequiredService<AppStateService>();
                state.AppDataPath = appDataPath;

                // DB init: CreateTables + seeds + migrations (synchronous — must finish before any page loads)
                TryLog("DB init...");
                Database.Initialize(appDataPath);
                Database.RunMigrationsAndBackup(appDataPath);
                TryLog("DB OK");

                state.CurrentFY = FolderManager.DetectFY(DateTime.Today);

                // Restore last-used deductor — pages need it before first render
                try
                {
                    var lastId = int.Parse(Database.GetSetting("LAST_DEDUCTOR_ID", "0"));
                    if (lastId > 0)
                    {
                        var ded = new CDeTDS.BLL.DeductorService().GetById(lastId);
                        if (ded != null) state.SetDeductor(ded.Id, ded.CompanyName, ded.Tan);
                    }
                }
                catch { }

                state.CurrentLicense = LicenseService.BuildTrial();

                // Seed FVU/Java paths from installer registry keys on first run
                SeedInstallerPaths();

                TryLog("Showing MainWindow...");
                var mainWindow = new MainWindow();
                mainWindow.Show();
                TryLog("MainWindow shown OK");

                // Everything below runs after the window is visible ─────────────
                Task.Run(() =>
                {
                    // 1. WebView2 check — only reads registry once per process
                    if (_webView2Installed == null)
                        _webView2Installed = IsWebView2Installed();
                    if (_webView2Installed == false)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            var result = MessageBox.Show(
                                "Microsoft Edge WebView2 Runtime is not installed.\n\n" +
                                "CDeTDS requires WebView2 to display its interface.\n\n" +
                                "Click OK to open the download page, then re-launch CDeTDS after installing.",
                                "CDeTDS — Missing Component",
                                MessageBoxButton.OKCancel,
                                MessageBoxImage.Warning);
                            if (result == MessageBoxResult.OK)
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                                    "https://go.microsoft.com/fwlink/p/?LinkId=2124703") { UseShellExecute = true });
                            Shutdown(1);
                        });
                        return;
                    }

                    // 3. QuestPDF licence (trivial but no need to block startup)
                    try { PdfReports.Initialize(); } catch { }

                    // 4. Folder structure
                    try { FolderManager.EnsureStructure(state.CurrentFY); } catch { }

                    // 5. Real license
                    try
                    {
                        var licSvc = Services.GetRequiredService<LicenseService>();
                        var lic = licSvc.LoadSaved();
                        if (lic != null) state.CurrentLicense = lic;
                    }
                    catch { }

                    // 6. TDS rules update (network — last)
                    try { new RulesUpdateService().AutoUpdateIfNeeded(); } catch (Exception ex) { TryLog("Rules: " + ex.Message); }

                    // 7. DB vacuum/analyse (only if needed)
                    try { Database.VacuumAndCheck(appDataPath); } catch { }

                    // 8. Silent auto-update via Velopack — download in background, apply on next restart
                    try { _ = CheckAndDownloadUpdateAsync(); } catch { }
                });
            }
            catch (Exception ex)
            {
                TryLog("STARTUP FAILED: " + ex);
                MessageBox.Show("Startup failed:\n\n" + ex.Message + "\n\n" + ex.InnerException?.Message,
                    "CDeTDS", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        // ── Velopack silent auto-updater ─────────────────────────────────────
        // Downloads update in background. On next app launch Velopack applies it silently.
        // Data is never touched — DB lives in AppData, install is in Program Files.
        private static async Task CheckAndDownloadUpdateAsync()
        {
            try
            {
                var source  = new GithubSource("https://github.com/arjun-panda/tdspro-releases", null, false);
                var manager = new UpdateManager(source);
                if (!manager.IsInstalled) return;   // dev/debug — not installed via Velopack
                var info = await manager.CheckForUpdatesAsync();
                if (info == null) return;            // already up to date
                TryLog($"Update available: {info.TargetFullRelease?.Version}. Downloading...");
                await manager.DownloadUpdatesAsync(info);
                TryLog("Update downloaded. Applying on next launch.");
                // Silent — no dialog. Update applies automatically on next app restart.
            }
            catch (Exception ex) { TryLog("AutoUpdate: " + ex.Message); }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CDeTDS");
                var dbPath     = Path.Combine(appDataPath, CDeTDS.Common.AppConstants.DbFileName);
                var backupDir  = Path.Combine(appDataPath, "Backup");

                if (File.Exists(dbPath))
                {
                    Directory.CreateDirectory(backupDir);
                    // Daily backup — one file per day, overwritten on subsequent closes the same day
                    var dest = Path.Combine(backupDir, $"CDeTDS_backup_{DateTime.Now:yyyyMMdd}.db");
                    File.Copy(dbPath, dest, overwrite: true);

                    // Keep only the 30 most recent daily backups (~1 month rolling window)
                    var old = Directory.GetFiles(backupDir, "CDeTDS_backup_*.db")
                                       .OrderByDescending(f => f)
                                       .Skip(30);
                    foreach (var f in old)
                        try { File.Delete(f); } catch { }
                }
            }
            catch { }

            base.OnExit(e);
        }

        private static void CopyDirectory(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.GetDirectories(src))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        private static void SeedInstallerPaths()
        {
            try
            {
                // 1. Registry (installed app) — highest priority
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\CDeTDS");
                if (key != null)
                {
                    var regFvu  = key.GetValue("FvuPath")  as string ?? "";
                    var regJava = key.GetValue("JavaPath") as string ?? "";
                    if (!string.IsNullOrEmpty(regFvu)  && File.Exists(regFvu))
                        CDeTDS.DAL.Database.SetSetting("FvuPath",  regFvu);
                    if (!string.IsNullOrEmpty(regJava) && File.Exists(regJava))
                        CDeTDS.DAL.Database.SetSetting("JavaPath", regJava);
                }
            }
            catch { }

            try
            {
                // 2. Auto-upgrade: if stored FvuPath points to an old JAR, replace it with 9.4
                var currentFvu = CDeTDS.DAL.Database.GetSetting("FvuPath", "");
                if (!string.IsNullOrEmpty(currentFvu) && !currentFvu.Contains("9.4"))
                {
                    // Search for 9.4 JAR next to the old one, in Program Files, or beside the exe
                    var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                    var candidates = new[]
                    {
                        Path.Combine(Path.GetDirectoryName(currentFvu) ?? "", "TDS_STANDALONE_FVU_9.4.jar"),
                        Path.Combine(exeDir, "FVU", "TDS_STANDALONE_FVU_9.4.jar"),
                        Path.Combine(exeDir, "TDS_STANDALONE_FVU_9.4", "TDS_STANDALONE_FVU_9.4.jar"),
                        @"C:\Program Files\CDeTDS\FVU\TDS_STANDALONE_FVU_9.4.jar",
                        @"C:\Program Files (x86)\CDeTDS\FVU\TDS_STANDALONE_FVU_9.4.jar",
                    };
                    var found = candidates.FirstOrDefault(File.Exists);
                    if (found != null)
                        CDeTDS.DAL.Database.SetSetting("FvuPath", found);
                }
            }
            catch { }
        }

        private static bool IsWebView2Installed()
        {
            // Machine-wide (most installs)
            using var k1 = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
            if (k1 != null)
            {
                var v = k1.GetValue("pv") as string;
                if (!string.IsNullOrEmpty(v) && v != "0.0.0.0") return true;
            }

            using var k2 = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
            if (k2 != null)
            {
                var v = k2.GetValue("pv") as string;
                if (!string.IsNullOrEmpty(v) && v != "0.0.0.0") return true;
            }

            // Per-user install
            using var k3 = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
            if (k3 != null)
            {
                var v = k3.GetValue("pv") as string;
                if (!string.IsNullOrEmpty(v) && v != "0.0.0.0") return true;
            }

            return false;
        }

        private static string CrashLogPath()
            => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CDeTDS", "Logs", $"crash_{DateTime.Now:yyyyMMdd}.log");

        private static void TryCrashLog(string msg)
        {
            try
            {
                var path = CrashLogPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] CDeTDS v{CDeTDS.Common.AppConstants.AppVersion}\n{msg}\n\n");
            }
            catch { }
        }

        private static void TryLog(string msg)
        {
            try
            {
                if (string.IsNullOrEmpty(_logPath))
                {
                    _logPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "CDeTDS", "Logs", "startup.log");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
            }
            catch { }
        }
    }
}
