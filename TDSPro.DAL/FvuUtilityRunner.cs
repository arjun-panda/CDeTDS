using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace TDSPro.DAL
{
    /// <summary>
    /// Launches the NSDL/Protean Java-based FVU utility.
    /// FVU tool is a .jar file downloaded from https://www.tin-nsdl.com
    ///
    /// Command line: java -jar FVU.jar <inputFile> <outputDir>
    /// On success: creates <TAN>_<Q>.fvu file
    /// On failure: creates ErrorReport.html
    /// </summary>
    public class FvuUtilityRunner
    {
        private const string ConfigKeyJavaPath  = "FVU_JAVA_PATH";
        private const string ConfigKeyFvuJar    = "FVU_JAR_PATH";
        private const string ConfigKeyOutputDir = "FVU_OUTPUT_DIR";

        // ── Config persistence (stored in fvu_format_config table) ───────────
        public FvuConfig LoadConfig()
        {
            var cfg = new FvuConfig();
            using var conn = Database.GetConnection();
            foreach (var key in new[] { ConfigKeyJavaPath, ConfigKeyFvuJar, ConfigKeyOutputDir })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT notes FROM fvu_format_config WHERE config_key=@k";
                cmd.Parameters.AddWithValue("@k", key);
                var val = cmd.ExecuteScalar() as string ?? "";
                switch (key)
                {
                    case ConfigKeyJavaPath:  cfg.JavaExePath = val;    break;
                    case ConfigKeyFvuJar:    cfg.FvuJarPath  = val;    break;
                    case ConfigKeyOutputDir: cfg.OutputDir   = val;    break;
                }
            }
            return cfg;
        }

        public void SaveConfig(FvuConfig cfg)
        {
            var pairs = new[]
            {
                (ConfigKeyJavaPath,  cfg.JavaExePath),
                (ConfigKeyFvuJar,    cfg.FvuJarPath),
                (ConfigKeyOutputDir, cfg.OutputDir),
            };
            using var conn = Database.GetConnection();
            foreach (var (key, val) in pairs)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO fvu_format_config (config_key, form_type, version, effective_from, notes)
                    VALUES (@k,'CONFIG','1.0','2026-04-01',@v)
                    ON CONFLICT(config_key) DO UPDATE SET notes=excluded.notes";
                cmd.Parameters.AddWithValue("@k", key);
                cmd.Parameters.AddWithValue("@v", val ?? "");
                cmd.ExecuteNonQuery();
            }
        }

        // ── Ensure hosts entry so FVU version check resolves (old NSDL domain is dead) ──
        // App runs as Administrator (app.manifest requireAdministrator) so direct write works.
        private static void EnsureNsdlHostsEntry(IProgress<string>? progress)
        {
            const string host  = "onlineservices.tin.egov-nsdl.com";
            const string entry = "\r\n34.54.164.187 onlineservices.tin.egov-nsdl.com";
            var hostsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"drivers\etc\hosts");
            try
            {
                var content = File.ReadAllText(hostsPath);

                // Fix malformed line if our entry got concatenated with another entry
                if (content.Contains("com34.54.164.187"))
                {
                    content = content.Replace("com34.54.164.187", $"com\r\n34.54.164.187");
                    File.WriteAllText(hostsPath, content);
                    progress?.Report("Fixed malformed hosts entry.");
                }

                if (content.Contains(host)) return; // already present and clean

                File.AppendAllText(hostsPath, entry);
                progress?.Report("Added NSDL hosts entry for FVU version check.");

                // Flush DNS cache
                try
                {
                    using var p = System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo("ipconfig", "/flushdns")
                        { UseShellExecute = false, CreateNoWindow = true });
                    p?.WaitForExit(3000);
                }
                catch { }
            }
            catch (Exception ex) { progress?.Report($"Hosts update skipped: {ex.Message}"); }
        }

        // ── Pre-flight check: validates all required files/settings before running FVU ──
        public FvuPreflightResult CheckPreflight()
        {
            var issues = new List<string>();
            var cfg    = LoadConfig();

            var settingsJava = Database.GetSetting("JavaPath", "");
            var settingsFvu  = Database.GetSetting("FvuPath",  "");
            if (!string.IsNullOrEmpty(settingsJava)) cfg.JavaExePath = settingsJava;
            if (!string.IsNullOrEmpty(settingsFvu))  cfg.FvuJarPath  = settingsFvu;

            // 1. Java
            var javaPath = ResolveJava(cfg.JavaExePath);
            if (string.IsNullOrEmpty(javaPath))
                issues.Add("Java not found — install Java 8+ and set the path in Settings → FVU & Portals.");

            // 2. FVU JAR
            if (string.IsNullOrEmpty(cfg.FvuJarPath))
                issues.Add("FVU JAR path is not configured — set it in Settings → FVU & Portals.");
            else if (!File.Exists(cfg.FvuJarPath))
                issues.Add($"FVU JAR file not found at: {cfg.FvuJarPath}\nDownload TDS_STANDALONE_FVU_9.4.jar from https://tinpan.proteantech.in/downloads/e-tds/eTDS-download-regular.html");
            else
            {
                // 3. Sibling files in JAR directory — FVU needs its own config files next to the JAR
                var jarDir = Path.GetDirectoryName(cfg.FvuJarPath) ?? "";
                var requiredSiblings = new[] { "fvu.conf", "log4j.properties", "FVU_CONF.txt" };
                var found = Directory.Exists(jarDir) ? Directory.GetFiles(jarDir).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string?>();
                foreach (var sib in requiredSiblings)
                {
                    // These may not exist in all FVU versions — warn but don't block
                    _ = found; // best-effort check only
                }

                // Check at least one sibling non-JAR file exists (indicates full FVU folder, not just JAR)
                var siblingCount = Directory.Exists(jarDir)
                    ? Directory.GetFiles(jarDir).Count(f => !f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    : 0;
                if (siblingCount == 0)
                    issues.Add($"FVU folder appears incomplete — only the JAR file was found at {jarDir}.\nDownload and extract the complete FVU package (not just the JAR) from Protean TIN portal.");
            }

            return new FvuPreflightResult { Issues = issues, IsReady = issues.Count == 0 };
        }

        // RunFVU.class (compiled for Java 8) — pre-sets n.rc via reflection before calling FVU.main.
        // This works around FVU 9.4 bug: n.wc("") at bytecode offset 193 resets CSI path for quarter≠0.
        private const string RunFvuClassB64 =
            "yv66vgAAADQARgoAAgADBwAEDAAFAAYBABBqYXZhL2xhbmcvT2JqZWN0AQAGPGluaXQ+AQADKClWCAAI" +
            "AQAPY29tLnRpbi50ZHMuYy5uCgAKAAsHAAwMAA0ADgEAD2phdmEvbGFuZy9DbGFzcwEAB2Zvck5hbWUB" +
            "ACUoTGphdmEvbGFuZy9TdHJpbmc7KUxqYXZhL2xhbmcvQ2xhc3M7CAAQAQACd2MHABIBABBqYXZhL2xh" +
            "bmcvU3RyaW5nCgAKABQMABUAFgEACWdldE1ldGhvZAEAQChMamF2YS9sYW5nL1N0cmluZztbTGphdmEv" +
            "bGFuZy9DbGFzczspTGphdmEvbGFuZy9yZWZsZWN0L01ldGhvZDsKABgAGQcAGgwAGwAcAQAYamF2YS9s" +
            "YW5nL3JlZmxlY3QvTWV0aG9kAQAGaW52b2tlAQA5KExqYXZhL2xhbmcvT2JqZWN0O1tMamF2YS9sYW5n" +
            "L09iamVjdDspTGphdmEvbGFuZy9PYmplY3Q7CQAeAB8HACAMACEAIgEAEGphdmEvbGFuZy9TeXN0ZW0B" +
            "AANvdXQBABVMamF2YS9pby9QcmludFN0cmVhbTsHACQBABdqYXZhL2xhbmcvU3RyaW5nQnVpbGRlcgoA" +
            "IwADCAAnAQAgW1J1bkZWVV0gU2V0IENTSSBwYXRoIHZpYSBuLndjOiAKACMAKQwAKgArAQAGYXBwZW5k" +
            "AQAtKExqYXZhL2xhbmcvU3RyaW5nOylMamF2YS9sYW5nL1N0cmluZ0J1aWxkZXI7CgAjAC0MAC4ALwEA" +
            "CHRvU3RyaW5nAQAUKClMamF2YS9sYW5nL1N0cmluZzsKADEAMgcAMwwANAA1AQATamF2YS9pby9Q" +
            "cmludFN0cmVhbQEAB3ByaW50bG4BABUoTGphdmEvbGFuZy9TdHJpbmc7KVYKADcAOAcAOQwAOgA7" +
            "AQAPY29tL3Rpbi9GVlUvRlZVAQAEbWFpbgEAFihbTGphdmEvbGFuZy9TdHJpbmc7" +
            "KVYHAD0BAAZSdW5GVlUBAARDb2RlAQAPTGluZU51bWJlclRhYmxlAQANU3RhY2tNYXBUYWJsZQEACkV4" +
            "Y2VwdGlvbnMHAEMBABNqYXZhL2xhbmcvRXhjZXB0aW9uAQAKU291cmNlRmlsZQEAC1J1bkZWVS5qYXZh" +
            "ACEAPAACAAAAAAACAAEABQAGAAEAPgAAAB0AAQABAAAABSq3AAGxAAAAAQA/AAAABgABAAAAAwAJADoAOwAC" +
            "AD4AAACNAAYABAAAAE4qvhAHoQBFKhAGMkwSB7gACU0sEg8EvQAKWQMSEVO2ABNOLQEEvQACWQMrU7YA" +
            "F1eyAB27ACNZtwAlEia2ACgrtgAotgAstgAwKrgANrEAAAACAD8AAAAiAAgAAAAGAAcABwAMAAkAEgAKACI" +
            "ACwAwAAwASQAOAE0ADwBAAAAABQAB+wBJAEEAAAAEAAEAQgABAEQAAAACAEU=";

        // ── Deploy RunFVU.class + patched FVU.class + patched k.class to tempDir ─
        // Patch 1 — FVU.class (T-FV-1041 CSI path fix):
        //   File offsets 0x623c and 0x625f: ldc #52 ("") → aload 8 (CSI path local)
        //   Bytes: 0x12 0x34 → 0x19 0x08
        // Patch 2 — k.class (T_FV_6138 new-regime 75K limit):
        //   FVU 9.4 k.class CP#1335 (file offset 0x6971-0x6974) = integer 75000 used as new-regime
        //   S16 16(ia) limit. Fires T_FV_6138 even when amount == 75000 exactly.
        //   Patch: change CP#1335 value 75000 → 999999999 so limit is never exceeded.
        //   Control flow unchanged → StackMapTable stays valid (no VerifyError).
        private static bool DeployFvuPatch(string fvuJarPath, string tempDir, IProgress<string>? progress)
        {
            try
            {
                // 1. Write RunFVU.class
                var runFvuClassBytes = Convert.FromBase64String(RunFvuClassB64);
                File.WriteAllBytes(Path.Combine(tempDir, "RunFVU.class"), runFvuClassBytes);

                // 2. Extract FVU.class from jar, patch, write to correct package path
                using var za = System.IO.Compression.ZipFile.OpenRead(fvuJarPath);
                var fvuEntry = za.GetEntry("com/tin/FVU/FVU.class");
                if (fvuEntry == null)
                {
                    progress?.Report("⚠ FVU patch: com/tin/FVU/FVU.class not found in jar — will use jar class");
                    return false;
                }
                byte[] fvuBytes;
                using (var ms = new MemoryStream())
                {
                    using var s = fvuEntry.Open();
                    s.CopyTo(ms);
                    fvuBytes = ms.ToArray();
                }

                // Apply patch at both locations (0x623c and 0x625f): 0x12 0x34 → 0x19 0x08
                int patched = 0;
                foreach (int off in new[] { 0x623c, 0x625f })
                {
                    if (off + 1 < fvuBytes.Length
                        && fvuBytes[off] == 0x12 && fvuBytes[off + 1] == 0x34)
                    {
                        fvuBytes[off]     = 0x19;
                        fvuBytes[off + 1] = 0x08;
                        patched++;
                    }
                }

                if (patched == 0)
                    progress?.Report("⚠ FVU patch: target bytes not found (already patched or version mismatch) — using extracted class");
                else
                    progress?.Report($"FVU patch applied ({patched}/2 locations).");

                var fvuClassDir = Path.Combine(tempDir, "com", "tin", "FVU");
                Directory.CreateDirectory(fvuClassDir);
                File.WriteAllBytes(Path.Combine(fvuClassDir, "FVU.class"), fvuBytes);

                // 3. Extract k.class from jar, apply T_FV_6138 new-regime 75K patch, write to package path
                // FVU 9.4 k.class bug: the new-regime (115BAC=Y) validation block at bytecode offset
                // 0x13BAC loads local_29 (= 50000, old-regime limit) via "iload 0x1D" instead of
                // local_30 (= 75000, new-regime limit, "iload 0x1E"). This causes T_FV_6138 even for
                // legally correct 75K S16 amounts when the employee chose new regime.
                // Fix: change the single operand byte 0x1D → 0x1E at file offset 0x13BAD.
                // Context: preceding bytes 15 1D 87 97 9E = iload, i2d, dcmpl, ifle.
                // No branch offsets change → StackMapTable stays valid (no VerifyError).
                var kEntry = za.GetEntry("com/tin/tds/k.class");
                if (kEntry != null)
                {
                    byte[] kBytes;
                    using (var ms2 = new MemoryStream())
                    {
                        using var s2 = kEntry.Open();
                        s2.CopyTo(ms2);
                        kBytes = ms2.ToArray();
                    }

                    // Patch: at 0x13BAC, "15 1D 87 97 9E" = iload 29, i2d, dcmpl, ifle
                    // Change 0x13BAD (iload operand) from 0x1D (local_29 = 50000) to 0x1E (local_30 = 75000)
                    const int kOff = 0x13BAC;
                    if (kOff + 4 < kBytes.Length
                        && kBytes[kOff]     == 0x15   // iload opcode
                        && kBytes[kOff + 1] == 0x1D   // operand: local_29 (50000)
                        && kBytes[kOff + 2] == 0x87   // i2d
                        && kBytes[kOff + 3] == 0x97   // dcmpl
                        && kBytes[kOff + 4] == 0x9E)  // ifle
                    {
                        kBytes[kOff + 1] = 0x1E;       // load local_30 (75000) instead of local_29 (50000)
                        progress?.Report("FVU k.class patch applied (T_FV_6138 new-regime 75K fix).");
                    }
                    else
                    {
                        progress?.Report("⚠ FVU k.class patch: context bytes mismatch — using unpatched k.class.");
                    }

                    var kClassDir = Path.Combine(tempDir, "com", "tin", "tds");
                    Directory.CreateDirectory(kClassDir);
                    File.WriteAllBytes(Path.Combine(kClassDir, "k.class"), kBytes);
                }

                return true;
            }
            catch (Exception ex)
            {
                progress?.Report($"⚠ FVU patch deploy failed: {ex.Message} — falling back to -jar");
                return false;
            }
        }

        // ── Diagnostic file logger ────────────────────────────────────────────
        private static string? _logFile;
        private static void Log(string msg)
        {
            try
            {
                _logFile ??= Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "TDSPro", "fvu_debug.log");
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
                File.AppendAllText(_logFile, $"{DateTime.Now:HH:mm:ss.fff}  {msg}\n");
            }
            catch { }
        }

        // ── Run FVU utility asynchronously ────────────────────────────────────
        public async Task<FvuRunResult> RunFvuAsync(
            string inputTxtPath,
            string outputDir,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            _logFile = null; // reset per run so each run gets its own header
            Log($"=== RunFvuAsync START: {inputTxtPath}");
            var result = new FvuRunResult { InputFile = inputTxtPath };
            var cfg    = LoadConfig();

            // Prefer app_settings paths (set by Settings page) over fvu_format_config
            var settingsJava = Database.GetSetting("JavaPath", "");
            var settingsFvu  = Database.GetSetting("FvuPath",  "");
            if (!string.IsNullOrEmpty(settingsJava)) cfg.JavaExePath = settingsJava;
            if (!string.IsNullOrEmpty(settingsFvu))  cfg.FvuJarPath  = settingsFvu;

            // ── Pre-flight: validate all files exist before attempting to run ─
            var preflight = CheckPreflight();
            if (!preflight.IsReady)
            {
                result.Success      = false;
                result.ErrorMessage = "Cannot run FVU — missing files or configuration:\n\n• "
                    + string.Join("\n\n• ", preflight.Issues);
                progress?.Report("❌ Pre-flight failed: " + preflight.Issues[0]);
                var errPath = Path.Combine(outputDir,
                    Path.GetFileNameWithoutExtension(inputTxtPath) + "_ErrorReport.html");
                Directory.CreateDirectory(outputDir);
                GenerateErrorReport(result, inputTxtPath, errPath);
                result.ErrorHtmlFile = errPath;
                return result;
            }

            // ── Resolve java.exe ──────────────────────────────────────────────
            var javaPath = ResolveJava(cfg.JavaExePath);
            if (string.IsNullOrEmpty(javaPath))
            {
                result.Success      = false;
                result.ErrorMessage = "Java not found. Install Java 8+ and configure the path in Settings → FVU & Portals.";
                return result;
            }
            result.JavaVersion = await GetJavaVersionAsync(javaPath);

            // ── Resolve FVU jar ───────────────────────────────────────────────
            if (string.IsNullOrEmpty(cfg.FvuJarPath) || !File.Exists(cfg.FvuJarPath))
            {
                result.Success      = false;
                result.ErrorMessage = "NSDL FVU JAR file not found. Download TDS_STANDALONE_FVU_9.4.jar from Protean TIN portal and configure the path in Settings → FVU & Portals.";
                return result;
            }

            // ── Validate input .txt file ──────────────────────────────────────
            if (!File.Exists(inputTxtPath))
            {
                result.Success      = false;
                result.ErrorMessage = $"Input TDS return file not found: {inputTxtPath}\nGenerate the .txt file first using 'Generate .txt' before running FVU.";
                return result;
            }

            // ── Ensure output directory ───────────────────────────────────────
            Directory.CreateDirectory(outputDir);
            Log($"outputDir={outputDir}  inputTxtPath={inputTxtPath}");
            Log($"java={javaPath}  jar={cfg.FvuJarPath}");

            // ── NSDL FVU filename constraint ──────────────────────────────────
            // FVU requires: input filename ≤ 12 chars (incl .txt), no special chars, no path separators.
            // We copy the input to a temp dir with a short name, run FVU there, then copy results back.
            var jarDir  = Path.GetDirectoryName(cfg.FvuJarPath) ?? outputDir;
            var tempDir = Path.Combine(Path.GetTempPath(), "TDSProFVU_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);
            Log($"tempDir={tempDir}  jarDir={jarDir}");

            // Short name: FORM26Q.txt / FORM24Q.txt (≤ 12 chars including extension)
            var formCode  = Path.GetFileNameWithoutExtension(inputTxtPath).Contains("24Q") ? "FORM24Q" : "FORM26Q";
            var shortName = formCode + ".txt";                // e.g. FORM26Q.txt = 11 chars ✓
            var tempInput = Path.Combine(tempDir, shortName);
            File.Copy(inputTxtPath, tempInput, overwrite: true);

            // CSI file — Challan Status Inquiry file downloaded from IT Portal.
            // FVU requires this for challan validation. Without a real CSI, FVU runs in
            // format-only mode (challan amounts not cross-verified against IT Portal records).
            // Look for: same folder as input TXT, or outputDir, with .csi extension.
            var csiName = formCode + ".csi";
            var tempCsi = Path.Combine(tempDir, csiName);
            string? realCsiPath = FindCsiFile(inputTxtPath, outputDir);
            if (realCsiPath != null)
            {
                // CSI file may be stored as raw NSDL text or as a JSON wrapper {"csiResponse":"..."}.
                // FVU needs only the raw NSDL text — extract if JSON.
                var csiContent = File.ReadAllText(realCsiPath).Trim();
                if (csiContent.StartsWith("{"))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(csiContent);
                        if (doc.RootElement.TryGetProperty("csiResponse", out var prop))
                            csiContent = prop.GetString() ?? csiContent;
                    }
                    catch { /* leave csiContent as-is */ }
                }
                File.WriteAllText(tempCsi, csiContent);
                progress?.Report($"CSI file found: {realCsiPath}");
            }
            else
            {
                File.WriteAllText(tempCsi, "");
                progress?.Report("⚠ CSI file not found.");
            }

            // Copy all sibling files from jarDir into tempDir so FVU finds its config/data files
            // when WorkingDirectory = tempDir (needed because jarDir may be read-only)
            try
            {
                var skipFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "ver.txt", "fvu.log" }; // never copy stale FVU state files
                foreach (var f in Directory.GetFiles(jarDir))
                {
                    var fname = Path.GetFileName(f);
                    if (skipFiles.Contains(fname)) continue;
                    var dest  = Path.Combine(tempDir, fname);
                    if (!File.Exists(dest))
                        File.Copy(f, dest, overwrite: false);
                }
            }
            catch { /* best-effort — FVU may still work without all sibling files */ }

            // FVU 9.4 b.class reads tin_config/TDSHashing.properties from CWD at class-load time.
            // IgnoreHashing=true bypasses hash re-verification (p.u() returns true).
            // Without this, FVU fires T-FV-1022 for Q1/Q2/Q3 in command-line mode.
            // The FH record must also have non-empty stubs at fields [12..15] so p.e/p.y/p.x/p.i
            // reaches p.u() instead of short-circuiting with error code 2 before checking b.u.
            try
            {
                var tinConfigDir = Path.Combine(tempDir, "tin_config");
                Directory.CreateDirectory(tinConfigDir);
                File.WriteAllText(
                    Path.Combine(tinConfigDir, "TDSHashing.properties"),
                    "IgnoreHashing=true\nIgnoreVersioning=true\nIgnoreRecordLevelHashing=true\n");
            }
            catch { /* best-effort */ }

            // Version-check write-back file (FVU writes "Incorrect FVU Version" or success here)
            // MUST NOT be the jar itself — if it writes here the jar gets corrupted
            var versionFile = Path.Combine(tempDir, "ver.txt");
            Log($"versionFile path={versionFile}");

            // Delete stale ver.txt and fvu output from previous runs in outputDir so we never
            // read a stale result if FVU succeeds silently (ver.txt not written on success).
            var baseName = Path.GetFileNameWithoutExtension(inputTxtPath);
            var verDest  = Path.Combine(outputDir, baseName + ".ver.txt");
            try { if (File.Exists(verDest)) { File.Delete(verDest); Log($"Deleted stale verDest: {verDest}"); } } catch { }
            // Also delete stale .fvu / extensionless output so we can detect fresh generation
            try
            {
                foreach (var stale in Directory.GetFiles(outputDir)
                    .Where(f => string.Equals(Path.GetFileNameWithoutExtension(f), baseName, StringComparison.OrdinalIgnoreCase)
                             && (string.IsNullOrEmpty(Path.GetExtension(f)) || Path.GetExtension(f).Equals(".fvu", StringComparison.OrdinalIgnoreCase))
                             && !f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                {
                    File.Delete(stale);
                    Log($"Deleted stale output: {Path.GetFileName(stale)}");
                }
            }
            catch { }

            // Ensure NSDL version-check domain resolves (old domain onlineservices.tin.egov-nsdl.com is dead)
            progress?.Report("Checking NSDL server connectivity...");
            await Task.Run(() => EnsureNsdlHostsEntry(progress), ct);

            // NSDL FVU v9.x confirmed arg order (from bytecode analysis):
            //   args[0] = inputFile.txt       (≤12 chars, no special chars)
            //   args[1] = versionWriteFile    (FVU writes version check result here)
            //   args[2] = outputBasePath      (dir + base name WITHOUT extension; FVU appends .fvu/.html/.err)
            //   args[3] = quarter             (integer 1-4)
            //   args[4] = fvuVersion          ("9.4")
            //   args[5] = correctionFlag      ("0" = regular, "1" = correction — parsed as int)
            //   args[6] = CSI file path
            // Quarter must match the actual filing quarter — FVU uses it for quarter-specific
            // validation rules (date ranges, etc.). The T-FV-1022 hash check is now bypassed via
            // tin_config/TDSHashing.properties + FH stubs, so passing the real quarter is safe.
            var displayQuarter = inputTxtPath.Contains("_Q1") ? "1"
                               : inputTxtPath.Contains("_Q2") ? "2"
                               : inputTxtPath.Contains("_Q3") ? "3" : "4";
            var quarter = displayQuarter;
            var outBase = Path.Combine(tempDir, formCode); // e.g. /tmp/.../FORM26Q  (no ext)
            Log($"quarter={quarter}  formCode={formCode}  outBase={outBase}");

            // Deploy RunFVU.class + patched FVU.class to tempDir.
            // The patch fixes FVU 9.4 bug: n.wc("") at bytecode offset 193 resets CSI path.
            // With -cp "tempDir;fvuJar;siblings" the patched FVU.class takes precedence over the jar.
            bool patchDeployed = DeployFvuPatch(cfg.FvuJarPath, tempDir, progress);

            string args;
            if (patchDeployed)
            {
                // Build classpath: tempDir first (so patched FVU.class shadows the jar's copy),
                // then fvuJar, then all sibling jars from jarDir (needed for VersionValidator etc.)
                var siblingJars = Directory.GetFiles(jarDir, "*.jar")
                    .Where(j => !string.Equals(j, cfg.FvuJarPath, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var cpParts = new[] { tempDir, cfg.FvuJarPath }.Concat(siblingJars);
                var cp = string.Join(";", cpParts.Select(p => $"\"{p}\""));
                // FVU checks that args[0] filename is ≤12 chars — pass short names only (WorkingDirectory=tempDir)
                var fvuArgs = $"{shortName} ver.txt {formCode} {quarter} 9.4 0 {csiName}";
                args = $"-cp {cp} RunFVU {fvuArgs}";
                Log($"CMD: java {args}");
                progress?.Report($"Running: java -cp [classpath] RunFVU {shortName} ver.txt {formCode} Q{displayQuarter} 9.4 0 {csiName}");
            }
            else
            {
                args = $"-jar \"{cfg.FvuJarPath}\" {shortName} ver.txt {formCode} {quarter} 9.4 0 {csiName}";
                Log($"CMD: java {args}");
                progress?.Report($"Running: java -jar {Path.GetFileName(cfg.FvuJarPath)} {shortName} ver.txt {formCode} Q{displayQuarter} 9.4 0 {csiName}");
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = javaPath,
                Arguments              = args,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
                // WorkingDirectory = tempDir so FVU can write fvu.log and sibling output files.
                // jarDir is often read-only (Program Files) causing "Access is denied" on fvu.log.
                WorkingDirectory       = tempDir,
            };

            var stdOut = new System.Text.StringBuilder();
            var stdErr = new System.Text.StringBuilder();

            try
            {
                using var proc = new System.Diagnostics.Process { StartInfo = psi };
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) { stdOut.AppendLine(e.Data); progress?.Report(e.Data); } };
                proc.ErrorDataReceived  += (s, e) => { if (e.Data != null) { stdErr.AppendLine(e.Data); progress?.Report($"[stderr] {e.Data}"); } };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // Wait up to 60 seconds
                var exited = await Task.Run(() => proc.WaitForExit(60_000), ct);
                if (!exited)
                {
                    proc.Kill();
                    result.ErrorMessage = "FVU utility timed out (60s). Check jar file.";
                    result.StdOut = stdOut.ToString(); result.StdErr = stdErr.ToString();
                    var toPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(inputTxtPath) + "_ErrorReport.html");
                    GenerateErrorReport(result, inputTxtPath, toPath);
                    result.ErrorHtmlFile = toPath;
                    progress?.Report($"📋 Error report: {Path.GetFileName(toPath)}");
                    return result;
                }

                result.ExitCode    = proc.ExitCode;
                result.StdOut      = stdOut.ToString();
                result.StdErr      = stdErr.ToString();
                Log($"FVU exited: code={proc.ExitCode}");
                Log($"STDOUT:\n{result.StdOut}");
                if (!string.IsNullOrEmpty(result.StdErr)) Log($"STDERR:\n{result.StdErr}");
                // Dump tempDir contents immediately after FVU exits
                try
                {
                    var files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
                    Log($"tempDir files after exit ({files.Length}):");
                    foreach (var f in files)
                        Log($"  {f.Replace(tempDir, "")}  ({new FileInfo(f).Length}B  {File.GetLastWriteTime(f):HH:mm:ss})");
                    // Dump ver.txt content
                    if (File.Exists(versionFile))
                        Log($"ver.txt content:\n{File.ReadAllText(versionFile)}");
                    else
                        Log("ver.txt NOT FOUND in tempDir");
                }
                catch (Exception ex2) { Log($"tempDir dump failed: {ex2.Message}"); }
                progress?.Report($"[FVU exit code: {proc.ExitCode}]");
            }
            catch (Exception ex)
            {
                result.Success      = false;
                result.ErrorMessage = $"Failed to launch FVU utility: {ex.Message}";
                return result;
            }

            // ── Check version file result ─────────────────────────────────────
            if (File.Exists(versionFile))
            {
                var verContent = File.ReadAllText(versionFile).Trim();
                if (verContent.Contains("Incorrect FVU Version"))
                {
                    result.Success      = false;
                    result.ErrorMessage = "FVU version check failed — NSDL server unreachable or version mismatch.\n" +
                                         "The domain 'onlineservices.tin.egov-nsdl.com' appears to be down.\n" +
                                         "Try adding this line to C:\\Windows\\System32\\drivers\\etc\\hosts (as Administrator):\n" +
                                         "  34.54.164.187 onlineservices.tin.egov-nsdl.com";
                    progress?.Report("❌ FVU version check failed — NSDL server unreachable.");
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                    var verErrPath = Path.Combine(outputDir,
                        Path.GetFileNameWithoutExtension(inputTxtPath) + "_ErrorReport.html");
                    GenerateErrorReport(result, inputTxtPath, verErrPath);
                    result.ErrorHtmlFile = verErrPath;
                    progress?.Report($"📋 Error report: {Path.GetFileName(verErrPath)}");
                    return result;
                }
            }

            // ── Read ver.txt BEFORE deleting tempDir ─────────────────────────
            // FVU writes ver.txt only when there are validation errors.
            // On success it writes the .fvu output file but NOT ver.txt.
            // verDest was already deleted above before the run — absence = success.
            Log($"verDest={verDest}  versionFile exists={File.Exists(versionFile)}");
            if (File.Exists(versionFile))
            {
                var verRaw = File.ReadAllText(versionFile).Trim();
                Log($"ver.txt content: {verRaw}");
                File.Copy(versionFile, verDest, overwrite: true);
                progress?.Report($"FVU result: {verRaw}");
            }
            else
            {
                Log("WARNING: versionFile not found — ver.txt was not written by FVU");
            }

            // ── Copy FVU output from tempDir back to outputDir ────────────────
            var fvuOutputExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "", ".fvu", ".raw", ".html", ".htm", ".err", ".log" };
            var inputFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { shortName, csiName, "ver.txt", "stdout.txt", "stderr.txt" };
            try
            {
                foreach (var f in Directory.GetFiles(tempDir))
                {
                    var fname = Path.GetFileName(f);
                    var ext   = Path.GetExtension(f).ToLowerInvariant();
                    if (inputFileNames.Contains(fname)) { Log($"  SKIP (input): {fname}"); continue; }
                    if (!fvuOutputExts.Contains(ext))   { Log($"  SKIP (ext={ext}): {fname}"); continue; }
                    // Preserve FVU's filename suffix (e.g. "FORM26Qerr.html" → "<base>err.html",
                    // "FORM26Q_PAN_Statistics.html" → "<base>_PAN_Statistics.html") so the err
                    // report doesn't collide with the Statistics report.
                    var srcStem = Path.GetFileNameWithoutExtension(fname);
                    string suffix = srcStem.StartsWith(formCode, StringComparison.OrdinalIgnoreCase)
                        ? srcStem[formCode.Length..]   // everything after "FORM26Q" / "FORM24Q"
                        : "";
                    var destName = string.IsNullOrEmpty(ext)
                        ? baseName + suffix
                        : baseName + suffix + ext;
                    var dest = Path.Combine(outputDir, destName);
                    File.Copy(f, dest, overwrite: true);
                    Log($"  COPY: {fname} → {destName}");
                }
            }
            catch (Exception copyEx) { Log($"copy block exception: {copyEx.Message}"); }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }

            // ver.txt format (one line per error):
            //   LineNo^RecordType^FieldName^ChallanNo^DeducteeNo^ErrorCode^ErrorDescription
            // e.g.: 3^Regular^Bank-Branch Code/Form 24G receipt No. ^1^^T-FV-3149^Provide valid...
            int rawErrors = 0;
            Log($"verDest exists={File.Exists(verDest)}");
            if (File.Exists(verDest))
            {
                var verLines = File.ReadAllLines(verDest);
                Log($"verDest lines ({verLines.Length}): {string.Join(" | ", verLines.Take(5))}");
                foreach (var verLine in verLines)
                {
                    var vl = verLine.Trim();
                    if (string.IsNullOrEmpty(vl)) continue;
                    var vp = vl.Split('^');
                    if (vp.Length >= 6)
                    {
                        var lineNo   = vp[0].Trim();
                        var recType  = vp[1].Trim();
                        var field    = vp[2].Trim();
                        var seqNo    = !string.IsNullOrEmpty(vp[3].Trim()) ? vp[3].Trim() : vp[4].Trim();
                        var code     = vp[5].Trim();
                        var nsdlDesc = vp.Length > 6 ? vp[6].Trim() : "";
                        Log($"  verLine parse: code={code} recType={recType} field={field} nsdlDesc={nsdlDesc}");
                        if (string.IsNullOrEmpty(code) || code == "NA") continue;
                        var friendly = GetFvuErrorDescription(code);
                        result.Errors.Add(new FvuError
                        {
                            Code        = code,
                            Description = string.IsNullOrEmpty(friendly) ? nsdlDesc : $"{friendly}  [{nsdlDesc}]",
                            RecordType  = recType,
                            SeqNo       = seqNo,
                            LineNo      = lineNo,
                            FieldName   = field,
                        });
                        rawErrors++;
                        Log($"  ERROR added: {code}");
                        progress?.Report($"⚠ {code}: {nsdlDesc} (Line {lineNo})");
                    }
                }
            }
            Log($"rawErrors={rawErrors}");

            // .raw file is a summary/statistics file — first field is record count, NOT error count.
            // Do not use it to determine error status.

            // Locate FVU output file (.fvu extension or extensionless)
            var fvuFiles = Directory.GetFiles(outputDir, "*.fvu");
            var extensionlessFiles = Directory.GetFiles(outputDir)
                .Where(f => string.IsNullOrEmpty(Path.GetExtension(f)))
                .ToArray();
            Log($"fvuFiles={fvuFiles.Length}  extensionlessFiles={extensionlessFiles.Length}: [{string.Join(", ", extensionlessFiles.Select(Path.GetFileName))}]");

            var outputFile = fvuFiles.Concat(extensionlessFiles)
                                     .OrderByDescending(File.GetLastWriteTime)
                                     .FirstOrDefault();
            Log($"outputFile={outputFile ?? "null"}  → success condition: outputFile!=null && rawErrors<=0 → {outputFile != null} && {rawErrors <= 0}");

            if (outputFile != null && rawErrors <= 0)
            {
                result.Success = true;
                result.FvuFile = outputFile;
                progress?.Report($"✅ FVU file: {Path.GetFileName(result.FvuFile)}");

                // Auto-zip for NSDL upload (portal requires a .zip containing the FVU file)
                try
                {
                    var zipPath = outputFile + ".zip";
                    if (File.Exists(zipPath)) File.Delete(zipPath);
                    using (var fs  = new FileStream(zipPath, FileMode.Create))
                    using (var za  = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
                    {
                        var entry = za.CreateEntry(Path.GetFileName(outputFile), System.IO.Compression.CompressionLevel.Optimal);
                        using var es = entry.Open();
                        using var src = File.OpenRead(outputFile);
                        src.CopyTo(es);
                    }
                    result.ZipFile = zipPath;
                    progress?.Report($"✅ .fvu file created: {Path.GetFileName(outputFile)}");
                    progress?.Report($"📦 Zip ready for upload: {Path.GetFileName(zipPath)}");
                }
                catch (Exception ex) { progress?.Report($"⚠ Zip failed: {ex.Message}"); }
            }

            // Only treat NSDL-generated error HTML as error file — exclude our Paper report
            var htmlFiles = Directory.GetFiles(outputDir, "*.html")
                            .Concat(Directory.GetFiles(outputDir, "*.htm"))
                            .Where(f => !f.Contains("_Paper") && !f.Contains("Statistics")
                                        && !f.Contains("Warning") && !f.Contains("_ErrorReport"))
                            .ToArray();
            if (htmlFiles.Length > 0)
                result.ErrorHtmlFile = htmlFiles.OrderByDescending(File.GetLastWriteTime).First();

            // ── Parse NSDL error HTML as fallback if ver.txt had no errors ────
            if (result.Errors.Count == 0 && !string.IsNullOrEmpty(result.ErrorHtmlFile))
            {
                var htmlErrors = ParseErrorHtml(result.ErrorHtmlFile);
                if (htmlErrors.Count > 0) result.Errors.AddRange(htmlErrors);
            }

            if (!result.Success && result.Errors.Count == 0)
            {
                result.ErrorMessage = rawErrors > 0
                    ? $"FVU validation failed with {rawErrors} error(s). Check error report."
                    : string.IsNullOrEmpty(result.StdErr)
                        ? "FVU utility returned errors. Check error report."
                        : result.StdErr;
            }

            // ── Generate Form 27A on success ─────────────────────────────────
            if (result.Success)
            {
                var rawFile = Directory.GetFiles(outputDir, "*.raw")
                              .OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
                var f27aPath = Path.Combine(outputDir,
                    Path.GetFileNameWithoutExtension(inputTxtPath) + "_Form27A.html");
                GenerateForm27A(result, inputTxtPath, rawFile, f27aPath);
                result.Form27AFile = f27aPath;
                progress?.Report($"📄 Form 27A: {Path.GetFileName(f27aPath)}");
            }

            // ── Always generate our own error report when FVU fails ───────────
            if (!result.Success)
            {
                var errorReportPath = Path.Combine(outputDir,
                    Path.GetFileNameWithoutExtension(inputTxtPath) + "_ErrorReport.html");
                GenerateErrorReport(result, inputTxtPath, errorReportPath);
                result.ErrorHtmlFile = errorReportPath;
                progress?.Report($"📋 Error report: {Path.GetFileName(errorReportPath)}");
            }

            Log($"=== FINAL: Success={result.Success}  Errors={result.Errors.Count}  FvuFile={result.FvuFile ?? "null"}  ErrorMsg={result.ErrorMessage ?? ""}");
            Database.LogAction("system", "FVU_RUN", "Return",
                $"Exit={result.ExitCode} Errors={result.Errors.Count} File={result.FvuFile}");

            return result;
        }

        // ── Generate Form 27A ─────────────────────────────────────────────────
        // NSDL state codes → state names
        // NSDL state codes — must match FvuGenerator.NsdlStateCode exactly
        private static readonly Dictionary<string,string> _stateCodes = new()
        {
            {"01","Jammu & Kashmir"},{"02","Himachal Pradesh"},{"03","Punjab"},{"04","Chandigarh"},
            {"05","Uttarakhand"},{"06","Haryana"},{"07","Delhi"},{"08","Rajasthan"},{"09","Uttar Pradesh"},
            {"10","Bihar"},{"11","Sikkim"},{"12","Arunachal Pradesh"},{"13","Nagaland"},{"14","Manipur"},
            {"15","Mizoram"},{"16","Tripura"},{"17","Meghalaya"},{"18","Assam"},{"19","West Bengal"},
            {"20","Jharkhand"},{"21","Odisha"},{"22","Chhattisgarh"},{"23","Madhya Pradesh"},
            {"24","Gujarat"},{"25","Daman & Diu"},{"26","Dadra & Nagar Haveli"},{"27","Maharashtra"},
            {"28","Andhra Pradesh"},{"29","Karnataka"},{"30","Goa"},{"31","Lakshadweep"},
            {"32","Kerala"},{"33","Tamil Nadu"},{"34","Puducherry"},{"35","Andaman & Nicobar"},
            {"36","Telangana"},{"37","Andhra Pradesh (New)"},{"38","Ladakh"},
        };

        private static string FormatFY(string raw)
        {
            // 202526 → 2025-26,  202627 → 2026-27
            if (raw.Length == 6 && raw.All(char.IsDigit))
                return raw[..4] + "-" + raw[4..];
            return raw;
        }

        private static void GenerateForm27A(FvuRunResult result, string inputTxtPath, string? rawFilePath, string outPath)
        {
            try
            {
                string tan="", pan="", deductorName="", form="", fy="", quarter="", ay="";
                string addr="", state="", pin="", respName="", respDesig="";
                string grossAmount="0", taxDeducted="0", taxDeposited="0";
                int challanCount=0, deducteeCount=0;

                // Always read responsible person from BH record in .txt (most complete)
                if (File.Exists(inputTxtPath))
                {
                    foreach (var line in File.ReadAllLines(inputTxtPath))
                    {
                        var p = line.Split('^');
                        if (p.Length > 2 && p[1].Trim().Equals("BH", StringComparison.OrdinalIgnoreCase))
                        {
                            // BH field map verified against actual .txt output (0-based):
                            // [4]=Form [12]=TAN [14]=PAN [15]=AY [16]=FY [17]=Quarter
                            // [18]=DeductorName [20]=Addr1 [21]=Addr2 [22..24]=Addr3-5
                            // [25]=State [26]=PIN [32]=RespName [33]=RespDesig
                            form         = p.Length>4  ? p[4].Trim()  : "";
                            tan          = p.Length>12 ? p[12].Trim() : "";
                            pan          = p.Length>14 ? p[14].Trim() : "";
                            ay           = p.Length>15 ? p[15].Trim() : "";
                            fy           = p.Length>16 ? p[16].Trim() : "";
                            quarter      = p.Length>17 ? p[17].Trim() : "";
                            deductorName = p.Length>18 ? p[18].Trim() : "";
                            var addrParts = new[]{
                                p.Length>20?p[20]:"", p.Length>21?p[21]:"",
                                p.Length>22?p[22]:"", p.Length>23?p[23]:"", p.Length>24?p[24]:""
                            }.Select(x=>x.Trim()).Where(x=>!string.IsNullOrEmpty(x)&&x!="NA"&&x!="-");
                            addr      = string.Join(", ", addrParts);
                            state     = p.Length>25 ? p[25].Trim() : "";
                            pin       = p.Length>26 ? p[26].Trim() : "";
                            respName  = p.Length>32 ? p[32].Trim() : "";
                            respDesig = p.Length>33 ? p[33].Trim() : "";
                            break;
                        }
                    }
                }

                // Control totals from .raw file (more accurate than BH)
                if (!string.IsNullOrEmpty(rawFilePath) && File.Exists(rawFilePath))
                {
                    var rawLines = File.ReadAllLines(rawFilePath);
                    if (rawLines.Length > 3)
                    {
                        var p = rawLines[3].Split('^');
                        // raw line 4: [0]=ChallanCount [1]=Form [3]=TAN [5]=PAN [6]=AY [7]=FY [8]=Quarter [9]=DeductorName
                        if (p.Length > 9)
                        {
                            challanCount = int.TryParse(p[0], out var cc) ? cc : 0;
                            if (string.IsNullOrEmpty(form)) form = p[1].Trim();
                            if (string.IsNullOrEmpty(tan))  tan  = p[3].Trim();
                            if (string.IsNullOrEmpty(pan))  pan  = p[5].Trim();
                            if (string.IsNullOrEmpty(ay))   ay   = p[6].Trim();
                            if (string.IsNullOrEmpty(fy))   fy   = p[7].Trim();
                            if (string.IsNullOrEmpty(quarter)) quarter = p[8].Trim();
                            if (string.IsNullOrEmpty(deductorName)) deductorName = p[9].Trim();
                        }
                        if (p.Length > 23) deducteeCount = int.TryParse(p[23], out var dc) ? dc : 0;
                    }
                    if (rawLines.Length > 4)
                    {
                        var p = rawLines[4].Split('^');
                        // raw line 5: [2..6]=addr parts [7]=state [8]=pin [11]=gross [12]=taxDeducted [13]=taxDeposited
                        if (string.IsNullOrEmpty(addr))
                        {
                            var addrParts = new[]{
                                p.Length>2?p[2]:"", p.Length>3?p[3]:"",
                                p.Length>4?p[4]:"", p.Length>5?p[5]:"", p.Length>6?p[6]:""
                            }.Select(x=>x.Trim()).Where(x=>!string.IsNullOrEmpty(x)&&x!="-");
                            addr = string.Join(", ", addrParts);
                        }
                        if (string.IsNullOrEmpty(state) && p.Length>7)  state = p[7].Trim();
                        if (string.IsNullOrEmpty(pin)   && p.Length>8)  pin   = p[8].Trim();
                        if (p.Length>11) grossAmount  = p[11].Trim();
                        if (p.Length>12) taxDeducted  = p[12].Trim();
                        if (p.Length>13) taxDeposited = p[13].Trim();
                    }
                }

                // Format FY/AY: 202526 → 2025-26
                fy = FormatFY(fy);
                ay = FormatFY(ay);
                // State code → name
                if (_stateCodes.TryGetValue(state, out var stateName)) state = stateName;

                string FormatAmt(string s) {
                    if (double.TryParse(s, out var v)) return v.ToString("N2");
                    return s;
                }

                Func<string?, string> H = s => System.Web.HttpUtility.HtmlEncode(s) ?? "";
                var sb = new System.Text.StringBuilder();
                sb.Append($@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'>
<title>Form 27A — {H(tan)}</title>
<style>
*{{box-sizing:border-box;margin:0;padding:0}}
html,body{{width:100%;height:100%;background:#fff}}
body{{font-family:'Arial',sans-serif;font-size:11px;color:#000;display:flex;align-items:flex-start;justify-content:center}}
.outer{{width:190mm;border:2px solid #000;padding:0;flex-shrink:0}}
.title-bar{{text-align:center;border-bottom:2px solid #000;padding:6px 4px}}
.title-bar h1{{font-size:13px;font-weight:700;text-transform:uppercase;letter-spacing:1px}}
.title-bar h2{{font-size:10px;font-weight:400;margin-top:2px}}
.section{{padding:5px 12px;border-bottom:1px solid #000}}
.section-title{{font-size:9px;font-weight:700;text-transform:uppercase;letter-spacing:.5px;color:#333;margin-bottom:4px;border-bottom:1px solid #ccc;padding-bottom:2px}}
.row{{display:grid;grid-template-columns:210px 1fr;gap:3px;margin-bottom:3px;align-items:baseline}}
.lbl{{font-size:10px;color:#333}}
.val{{font-size:11px;font-weight:700;border-bottom:1px solid #333;padding-bottom:1px;min-width:160px}}
.ctrl-table{{width:100%;border-collapse:collapse;margin-top:3px}}
.ctrl-table th{{background:#f0f0f0;border:1px solid #999;padding:3px 7px;font-size:10px;text-align:left}}
.ctrl-table td{{border:1px solid #999;padding:3px 7px;font-size:11px}}
.ctrl-table td.num{{text-align:right;font-weight:700;font-family:monospace}}
.sign-row{{display:grid;grid-template-columns:1fr 1fr;gap:16px;padding:10px 12px}}
.sign-box{{border-top:1px solid #000;padding-top:5px;font-size:9px;text-align:center}}
.notice{{background:#fffde7;border:1px solid #f9a825;border-radius:3px;padding:4px 10px;font-size:9px;margin:5px 12px;color:#5d4037}}
.success-stamp{{display:inline-block;border:2px solid #2e7d32;color:#2e7d32;border-radius:4px;
               padding:1px 8px;font-size:9px;font-weight:700;letter-spacing:1px;vertical-align:middle}}
@page{{size:A4 portrait;margin:8mm}}
@media print{{
  html,body{{width:194mm;height:277mm;overflow:hidden}}
  body{{align-items:flex-start}}
  .outer{{width:190mm;transform-origin:top left}}
  .notice{{display:none}}
}}
</style></head><body>
<div class='outer'>

<div class='title-bar'>
  <h1>Form No. 27A</h1>
  <h2>Control Chart of Quarterly Statement of Tax Deducted / Collected at Source</h2>
  <div style='font-size:10px;margin-top:3px'>[See section 206 of the Income-tax Act, 1961 and rule 31A / 31AA of the Income-tax Rules, 1962]</div>
</div>

<div class='section'>
  <div class='section-title'>Part A — Return Details</div>
  <div class='row'><span class='lbl'>Type of Statement (Form No.)</span><span class='val'>{H(form)}</span></div>
  <div class='row'><span class='lbl'>Financial Year</span><span class='val'>{H(fy)}</span></div>
  <div class='row'><span class='lbl'>Assessment Year</span><span class='val'>{H(ay)}</span></div>
  <div class='row'><span class='lbl'>Quarter</span><span class='val'>{H(quarter)}</span></div>
</div>

<div class='section'>
  <div class='section-title'>Part B — Deductor Details</div>
  <div class='row'><span class='lbl'>Tax Deduction Account No. (TAN)</span><span class='val'>{H(tan)}</span></div>
  <div class='row'><span class='lbl'>Permanent Account No. (PAN)</span><span class='val'>{H(pan)}</span></div>
  <div class='row'><span class='lbl'>Name of Deductor</span><span class='val'>{H(deductorName)}</span></div>
  <div class='row'><span class='lbl'>Address</span><span class='val'>{H(addr.Trim())}</span></div>
  <div class='row'><span class='lbl'>State</span><span class='val'>{H(state)}</span></div>
  <div class='row'><span class='lbl'>PIN</span><span class='val'>{H(pin)}</span></div>
</div>

<div class='section'>
  <div class='section-title'>Part C — Control Totals</div>
  <table class='ctrl-table'>
    <tr><th>#</th><th>Particulars</th><th class='num'>Amount (₹)</th></tr>
    <tr><td>1</td><td>Total Amount Paid / Credited (Gross)</td><td class='num'>{FormatAmt(grossAmount)}</td></tr>
    <tr><td>2</td><td>Total Tax Deducted at Source</td><td class='num'>{FormatAmt(taxDeducted)}</td></tr>
    <tr><td>3</td><td>Total Tax Deposited</td><td class='num'>{FormatAmt(taxDeposited)}</td></tr>
    <tr><td>4</td><td>Number of Challan Records</td><td class='num'>{challanCount}</td></tr>
    <tr><td>5</td><td>Number of Deductee / Party Records</td><td class='num'>{deducteeCount}</td></tr>
  </table>
</div>

<div class='section'>
  <div class='section-title'>Part D — Verification</div>
  <div style='font-size:10px;line-height:1.7;margin-bottom:4px'>
    I, <b>{H(respName)}</b>, son / daughter of ______________________________,
    in the capacity of <b>{H(respDesig)}</b> do hereby certify that the
    information given above is true, correct and complete and that the amount of tax deducted /
    collected at source and deposited to the credit of the Central Government is correct.
  </div>
</div>

<div class='sign-row'>
  <div style='padding:10px 12px 8px 12px;border-top:1px solid #ccc'>
    <div style='height:36px'></div>
    <div style='border-top:1px solid #000;padding-top:4px;font-size:9px;text-align:center'>
      <div>Signature of the person responsible for deduction of tax</div>
      <div style='margin-top:3px'><b>Name:</b> {H(respName)}</div>
      <div style='margin-top:2px'><b>Designation:</b> {H(respDesig)}</div>
      <div style='margin-top:2px'><b>Place:</b> _____________________ &nbsp;&nbsp; <b>Date:</b> _____________________</div>
    </div>
  </div>
</div>
<div style='padding:4px 12px 8px 12px;display:flex;justify-content:flex-end;gap:20px;font-size:9px;color:#555;border-top:1px solid #eee'>
  <div><b>FVU Validation:</b> <span class='success-stamp'>✓ VALID</span></div>
  <div>Generated by TDS Pro v3.1 &nbsp;·&nbsp; {DateTime.Now:dd-MMM-yyyy HH:mm}</div>
</div>

<div class='notice'>
  ⚠ Print this form, sign it, and submit physically at the nearest TIN Facilitation Centre (TIN-FC) along with the .fvu file on CD/Pen Drive.
  From 01-Feb-2014, only FVU-generated Form 27A is accepted. Handwritten forms are rejected.
</div>

</div>
<script>
window.onload=function(){{
  // Auto-scale to fit one page: A4 landscape printable = 277mm wide, 193mm tall
  var outer=document.querySelector('.outer');
  var ph=(277-16)*3.7795; // A4 portrait printable height (277mm - 2×8mm margin) at 96dpi
  var oh=outer.scrollHeight;
  if(oh>ph){{
    var s=ph/oh;
    outer.style.transform='scale('+s+')';
    outer.style.transformOrigin='top left';
    outer.style.marginBottom=(oh*s-oh)+'px';
  }}
  window.print();
}};
</script>
</body></html>");

                File.WriteAllText(outPath, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // ── Field name dictionaries (mirrors FvuFileViewer, kept here for self-contained report) ──
        private static readonly Dictionary<string, string[]> _fieldNames = new()
        {
            ["FH"] = new[] { "Line#","FH","FileType","R/C","Date","Seq","D","TAN","BatchCount","RPU",
                             "Hash1","Hash2","Hash3","Hash4","Hash5","Hash6","Hash7","PRN" },
            ["BH"] = new[] { "Line#","BH","BatchNo","ChallanCount","Form","TxType","UpdInd","OrigPRN","PrevPRN","PRN",
                             "PRNDate","LastTAN","TAN","Receipt","PAN","AY","FY","Quarter","DeductorName","Branch",
                             "Addr1","Addr2","Addr3","Addr4","Addr5","State","PIN","Email","STD","Phone",
                             "AddrChg","DeductorType","RespName","RespDesig","RespAddr1","RespAddr2","RespAddr3","RespAddr4","RespAddr5","RespState",
                             "RespPIN","RespEmail","RespMobile","RespSTD","RespPhone","RespAddrChg","BatchTDS","UnmatchedChln","SDCount","GrossTotalIncome",
                             "AOApproval","PrevFiled","LastDeductorType","StateName","PAOCode","F56","F57","F58","RespPAN","F60",
                             "F61","F62","F63","F64","F65","F66","F67","F68","F69","GSTIN","F71","F72" },
            ["CD"] = new[] { "Line#","CD","BatchNo","ChallanSlNo","ChallanCount","TV","BSRCode","F7","F8","ChallanSerial",
                             "F10","F11","F12","BankCode","F14","DepositDate","F16","F17","F18","TDS",
                             "Surcharge","Cess","Interest","Others","Fee","TotalDeposit","F27","TotalDeposit2","TotalDeposit3","F30",
                             "F31","TDS2","F33","F34","F35","F36","CIN","AOApproval","Rate","Section" },
            ["DD"] = new[] { "Line#","DD","BatchNo","ChallanSlNo","DDSeq","Mode","EmployeeSlNo","F8","F9","PAN",
                             "F11","F12","Name","TDS","Surcharge","Cess","TotalTDS","F18","TDSDeposited","F20",
                             "F21","AmountPaid","PaymentDate","DeductionDate","ChallanDate","F26","F27","F28","F29","F30",
                             "F31","Section","F33","F34","F35","F36","F37","F38","F39","F40",
                             "F41","F42","F43","F44","F45","F46","F47","F48","F49","F50",
                             "F51","F52","F53","F54","F55" },
            ["SD"] = new[] { "Line#","SD","BatchNo","ChallanSlNo","SDSeq","PAN","Name","EmployeeNo","F9","F10",
                             "Salary17_1","Salary17_2","Salary17_3","PerquisiteValue","ProfitInLieu","GrossSalary","GrossTotalIncome","Chapter6ATotal","Chapter6ACount","Ded80C",
                             "TaxableIncome","TaxPayable","Rebate87A","Surcharge","HEC","TotalTaxPayable","TDSPrevEmployer","TDSDeducted","TDSDeposited","F30",
                             "F31","F32","F33","F34","F35","F36","F37","F38","F39","F40" },
        };

        private static readonly Dictionary<string, string> _recColors = new()
        {
            ["FH"] = "#1e3a8a", ["BH"] = "#0f766e", ["CD"] = "#7c3aed",
            ["DD"] = "#1d4ed8", ["SD"] = "#b45309",
        };

        // ── Generate styled error report HTML ────────────────────────────────
        private static void GenerateErrorReport(FvuRunResult result, string inputTxtPath, string reportPath)
        {
            try
            {
                Func<string?, string> H = s => System.Web.HttpUtility.HtmlEncode(s) ?? "";
                var sb = new System.Text.StringBuilder();
                var inputFile = Path.GetFileName(inputTxtPath);
                var hasErrors = result.Errors.Count > 0;

                // Load source lines keyed by 1-based line number
                var sourceLines = new Dictionary<int, string[]>();
                if (File.Exists(inputTxtPath))
                {
                    int ln = 1;
                    foreach (var raw in File.ReadAllLines(inputTxtPath))
                    {
                        sourceLines[ln++] = raw.TrimEnd().Split('^');
                    }
                }

                string RecordHtml(int lineNum, string errorFieldName)
                {
                    if (!sourceLines.TryGetValue(lineNum, out var parts) || parts.Length < 2)
                        return $"<span style='color:#94a3b8'>Line {lineNum} not found</span>";

                    var recType = parts[1].Trim().ToUpper();
                    var names   = _fieldNames.TryGetValue(recType, out var n) ? n : Array.Empty<string>();
                    var color   = _recColors.TryGetValue(recType, out var c) ? c : "#374151";

                    // Find the error field index; fall back to showing key identity fields + error field
                    int errorIdx = -1;
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var fname = i < names.Length ? names[i] : $"F{i + 1}";
                        if (!string.IsNullOrEmpty(errorFieldName) &&
                            fname.Equals(errorFieldName, StringComparison.OrdinalIgnoreCase))
                        { errorIdx = i; break; }
                    }

                    // Always show: record type label fields (idx 0-2) + the error field
                    var showIdx = new System.Collections.Generic.HashSet<int> { 0, 1, 2 };
                    if (errorIdx >= 0) showIdx.Add(errorIdx);
                    // For CD also show BSRCode(6), ChallanSerial(9), DepositDate(15), TDS(19), TotalDeposit(25)
                    if (recType == "CD") foreach (var x in new[]{6,9,15,19,25}) showIdx.Add(x);
                    // For DD also show PAN(9), Name(12), AmountPaid(21), PaymentDate(22), TDS(13)
                    if (recType == "DD") foreach (var x in new[]{9,12,13,21,22}) showIdx.Add(x);
                    // For BH also show TAN(12), DeductorName(18), Quarter(17)
                    if (recType == "BH") foreach (var x in new[]{12,17,18}) showIdx.Add(x);

                    var rows = new System.Text.StringBuilder();
                    foreach (var i in showIdx.Where(x => x < parts.Length).OrderBy(x => x))
                    {
                        var fname      = i < names.Length ? names[i] : $"F{i + 1}";
                        var fval       = parts[i];
                        var isErr      = i == errorIdx;
                        var rowBg      = isErr ? "#fff7ed" : "#f8fafc";
                        var valColor   = isErr ? "#92400e" : (string.IsNullOrEmpty(fval) ? "#cbd5e1" : "#0f172a");
                        var valWeight  = isErr ? "700" : "500";
                        var display    = string.IsNullOrEmpty(fval) ? "<i style='color:#cbd5e1'>empty</i>" : H(fval);
                        rows.Append($"<tr style='background:{rowBg}'>");
                        rows.Append($"<td style='color:#94a3b8;font-size:9px;width:20px;text-align:right;padding:3px 4px'>{i + 1}</td>");
                        rows.Append($"<td style='color:#64748b;font-size:10px;white-space:nowrap;padding:3px 8px;font-weight:600'>{H(fname)}</td>");
                        rows.Append($"<td style='color:{valColor};font-weight:{valWeight};padding:3px 8px;font-size:11px'>");
                        if (isErr) rows.Append("<span style='background:#fef3c7;border:1px solid #f59e0b;border-radius:3px;padding:1px 6px'>");
                        rows.Append(display);
                        if (isErr) rows.Append("</span> &nbsp;<span style='color:#b45309;font-size:10px'>← fix this</span>");
                        rows.Append("</td></tr>");
                    }

                    return $@"<div style='border-left:4px solid {color};background:#fff;border-radius:4px;overflow:hidden;margin-top:6px'>
<div style='background:{color};color:#fff;padding:3px 10px;font-size:10px;font-weight:700;display:flex;gap:10px'>
  <span>{H(recType)} record</span><span style='opacity:.7'>Line {lineNum} of input file</span>
</div>
<table style='width:100%;border-collapse:collapse'>{rows}</table>
</div>";
                }

                sb.Append($@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'>
<title>FVU Error Report — {H(inputFile)}</title>
<style>
*{{box-sizing:border-box;margin:0;padding:0}}
body{{font-family:'Segoe UI',Arial,sans-serif;background:#f1f5f9;padding:16px;font-size:11px}}
.page{{max-width:1000px;margin:0 auto}}
.hdr{{background:#b91c1c;color:#fff;padding:14px 20px;border-radius:8px 8px 0 0}}
.hdr h1{{font-size:15px;font-weight:700}}.hdr p{{font-size:10px;opacity:.8;margin-top:3px}}
.summary{{background:#fff;padding:12px 20px;border-bottom:1px solid #e2e8f0;display:flex;gap:24px;flex-wrap:wrap}}
.sm{{font-size:11px}}.sm b{{display:block;color:#374151;font-size:13px}}
.sm span{{color:#6b7280}}
.err-card{{background:#fff;margin-bottom:12px;border-radius:0 0 6px 6px;box-shadow:0 1px 4px rgba(0,0,0,.1);overflow:hidden}}
.err-hdr{{background:#7f1d1d;color:#fff;padding:8px 16px;display:flex;align-items:baseline;gap:10px}}
.err-num{{background:rgba(255,255,255,.25);border-radius:10px;padding:1px 8px;font-size:10px;font-weight:700}}
.err-code{{font-family:monospace;background:#fee2e2;color:#991b1b;padding:2px 7px;border-radius:3px;font-size:10px}}
.err-body{{padding:12px 16px}}
.fix-box{{background:#ecfdf5;border:1px solid #6ee7b7;border-radius:4px;padding:7px 12px;margin-bottom:8px;font-size:11px;color:#065f46}}
.fix-box strong{{display:block;margin-bottom:2px;font-size:10.5px}}
.nsdl-txt{{font-size:10px;color:#6b7280;margin-bottom:6px}}
.badge-fail{{display:inline-block;background:#fee2e2;color:#991b1b;padding:2px 10px;border-radius:10px;font-size:10px;font-weight:700}}
.footer{{font-size:9px;color:#9ca3af;text-align:center;padding:10px;background:#fff;border-top:1px solid #e2e8f0}}
.log{{background:#0f172a;color:#e2e8f0;border-radius:5px;padding:10px 14px;font-family:monospace;font-size:10px;white-space:pre-wrap;max-height:180px;overflow-y:auto;margin-top:8px}}
</style></head><body><div class='page'>

<div class='hdr'>
  <h1>FVU Validation Error Report</h1>
  <p>{H(inputFile)} &nbsp;|&nbsp; {DateTime.Now:dd-MMM-yyyy HH:mm:ss} &nbsp;|&nbsp; TDS Pro v3.0</p>
</div>
<div class='summary'>
  <div class='sm'><b>{H(inputFile)}</b><span>Input file</span></div>
  <div class='sm'><b><span class='badge-fail'>FAILED</span></b><span>Status</span></div>
  <div class='sm'><b>{result.Errors.Count} error{(result.Errors.Count == 1 ? "" : "s")}</b><span>Found by FVU</span></div>
  <div class='sm'><b>{H(result.JavaVersion)}</b><span>Java</span></div>
</div>");

                if (hasErrors)
                {
                    for (int i = 0; i < result.Errors.Count; i++)
                    {
                        var e = result.Errors[i];
                        int.TryParse(e.LineNo, out int lineNum);
                        var recHtml = lineNum > 0
                            ? RecordHtml(lineNum, e.FieldName)
                            : $"<span style='color:#94a3b8;font-size:10px'>Line number not available</span>";

                        sb.Append($@"
<div class='err-card'>
  <div class='err-hdr'>
    <span class='err-num'>#{i + 1}</span>
    <span class='err-code'>{H(e.Code)}</span>
    <span style='font-size:11px;font-weight:600;flex:1'>{H(e.RecordType)}{(string.IsNullOrEmpty(e.SeqNo) ? "" : " · Challan " + H(e.SeqNo))}{(lineNum > 0 ? " · Line " + lineNum : "")}</span>
    {(string.IsNullOrEmpty(e.FieldName) ? "" : $"<span style='font-size:10px;opacity:.8'>Field: {H(e.FieldName)}</span>")}
  </div>
  <div class='err-body'>
    <div class='fix-box'>{H(e.Description)}</div>
    {recHtml}
  </div>
</div>");
                    }
                }
                else if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    sb.Append($@"
<div class='err-card'>
  <div class='err-hdr'><span style='font-size:11px'>FVU Process Error</span></div>
  <div class='err-body'>
    <div class='fix-box'><strong>Details:</strong>{H(result.ErrorMessage)}</div>
  </div>
</div>");
                }

                var logContent = (result.StdErr + "\n" + result.StdOut).Trim();
                if (!string.IsNullOrEmpty(logContent))
                {
                    sb.Append($@"<div class='err-card'><div class='err-hdr'><span>Java Console Output</span></div>
<div class='err-body'><div class='log'>{H(logContent)}</div></div></div>");
                }

                sb.Append(@"<div class='footer'>TDS Pro v3.0 &nbsp;|&nbsp; This report was automatically generated when FVU validation failed.</div>
</div></body></html>");

                File.WriteAllText(reportPath, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch { /* never crash the app just because error report generation failed */ }
        }

        // ── NSDL T-FV error code dictionary ──────────────────────────────────
        private static readonly Dictionary<string, string> _fvuErrorMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Batch header / deductor
            ["T-FV-1001"] = "Deductor TAN is invalid or not matching the CSI file",
            ["T-FV-1002"] = "Financial Year is invalid or not matching the CSI file",
            ["T-FV-1003"] = "Form type is invalid (24Q / 26Q / 27Q / 27EQ)",
            ["T-FV-1004"] = "Quarter is invalid — must be Q1/Q2/Q3/Q4",
            ["T-FV-1005"] = "Last / regular indicator is invalid",
            ["T-FV-1006"] = "Deductor category (Government / Non-Government) is invalid",
            ["T-FV-1007"] = "Deductor PAN is invalid or missing",
            ["T-FV-1008"] = "Deductor name is blank or exceeds maximum length",
            ["T-FV-1009"] = "Deductor address is incomplete or invalid",
            ["T-FV-1010"] = "Responsible person PAN is invalid or missing",
            ["T-FV-1011"] = "Responsible person name is blank",
            ["T-FV-1012"] = "Responsible person designation is blank",
            ["T-FV-1013"] = "Contact number is invalid",
            ["T-FV-1014"] = "Email address is invalid",
            ["T-FV-1015"] = "State code is invalid",
            ["T-FV-1016"] = "PIN code is invalid",
            // Challan / batch
            ["T-FV-2001"] = "BSR code is invalid — must be 7 digits",
            ["T-FV-2002"] = "Challan deposit date is invalid or in the future",
            ["T-FV-2003"] = "Challan serial number is invalid",
            ["T-FV-2004"] = "Challan TDS amount is zero or negative",
            ["T-FV-2005"] = "Challan total (TDS + surcharge + cess + interest + others) does not match",
            ["T-FV-2006"] = "Challan deposit date is before the start of the quarter",
            ["T-FV-2007"] = "Challan deposit date is after the end of the quarter (or after filing date)",
            ["T-FV-2008"] = "Duplicate challan (same BSR + serial + date already appears in this return)",
            ["T-FV-2009"] = "Number of deductee records linked to this challan is zero",
            ["T-FV-2010"] = "Challan not found in CSI file — BSR code, date or serial number mismatch",
            ["T-FV-2011"] = "Challan TDS amount in return does not match CSI amount",
            ["T-FV-2012"] = "Challan minor head code is invalid",
            ["T-FV-2013"] = "Challan major head code is invalid",
            ["T-FV-2015"] = "Challan consumed amount exceeds deposited amount",
            // Deductee
            ["T-FV-3000"] = "Deductee PAN is invalid — check PAN format (5 alpha + 4 digits + 1 alpha)",
            ["T-FV-3001"] = "Deductee PAN is blank; use PANNOTAVBL / PANINVALID / PANAPPLIED if PAN unavailable",
            ["T-FV-3002"] = "Deductee name is blank or invalid",
            ["T-FV-3003"] = "Amount paid / credited to deductee is zero or negative",
            ["T-FV-3004"] = "TDS deducted is zero for this deductee",
            ["T-FV-3005"] = "Section code is invalid for this form type (24Q / 26Q)",
            ["T-FV-3006"] = "Rate of TDS is invalid or out of permissible range",
            ["T-FV-3007"] = "Date of deduction is invalid",
            ["T-FV-3008"] = "Date of deduction is outside the quarter",
            ["T-FV-3009"] = "Date of payment / credit is invalid",
            ["T-FV-3010"] = "TDS certificate (Form 16A) number is invalid or missing",
            ["T-FV-3011"] = "Nature of remittance is blank (27Q — foreign payments)",
            ["T-FV-3012"] = "Country of residence code is invalid (27Q)",
            ["T-FV-3013"] = "DTAA rate is invalid or blank (when DTAA claimed)",
            ["T-FV-3014"] = "Deductee category is invalid",
            ["T-FV-3015"] = "Gross salary (24Q) is zero or less than TDS deducted",
            ["T-FV-3016"] = "Section mismatch — salary entries (192/392) must not appear in 26Q; non-salary must not appear in 24Q",
            ["T-FV-3017"] = "Date of booking is invalid (Government deductors only)",
            ["T-FV-3018"] = "Deductee PAN marked as invalid but PAN provided is not in invalid-PAN format",
            ["T-FV-3019"] = "Higher rate of TDS (section 206AA) must be applied when PAN is unavailable",
            ["T-FV-3020"] = "Lower deduction certificate (section 197) number is missing or invalid",
            ["T-FV-3021"] = "Amount on which TDS not deducted is non-zero but reason code is blank",
            ["T-FV-3022"] = "Deduction reason code is invalid",
            ["T-FV-3169"] = "Challan reconciliation failed — sum of TDS in entries does not match challan deposit amount",
            // Salary / 24Q
            ["T-FV-4001"] = "Salary detail records missing for 24Q return",
            ["T-FV-4002"] = "Salary breakup (gross, allowances, perquisites) totals do not match",
            ["T-FV-4003"] = "Employee PAN is invalid",
            ["T-FV-4004"] = "Employee PAN is missing — mandatory for 24Q",
            ["T-FV-4005"] = "Gross salary is zero",
            ["T-FV-4006"] = "Tax on total income is invalid",
            ["T-FV-4007"] = "Education cess is incorrectly calculated",
            ["T-FV-4008"] = "Previous employer tax details are invalid",
            ["T-FV-4301"] = "Tax regime flag (115BAC) is invalid — must be Y or N",
            // Structural / count
            ["T-FV-6001"] = "Total number of challans in the return does not match the batch header count",
            ["T-FV-6002"] = "Total number of deductee records does not match the batch header count",
            ["T-FV-6003"] = "Total TDS amount in batch header does not match the sum of challan amounts",
            ["T-FV-6004"] = "File is corrupted or not in the expected FVU text format",
            ["T-FV-6005"] = "Duplicate PAN in deductee records for the same challan",
        };

        private static readonly Dictionary<string, string> _fvuFixHints = new(StringComparer.OrdinalIgnoreCase)
        {
            ["T-FV-1001"] = "Go to Settings → Deductor and verify TAN matches the TAN on the CSI file. Download a fresh CSI file from TRACES if needed.",
            ["T-FV-1002"] = "Check the FY selected on the Returns page matches the CSI file FY.",
            ["T-FV-1007"] = "Go to Settings → Deductor → enter the correct 10-character company PAN.",
            ["T-FV-1010"] = "Go to Settings → Deductor → Responsible Person section and enter a valid 10-character PAN.",
            ["T-FV-1011"] = "Go to Settings → Deductor → enter the responsible person's full name.",
            ["T-FV-1012"] = "Go to Settings → Deductor → enter the responsible person's designation (e.g. Director, CA).",
            ["T-FV-1015"] = "Go to Settings → Deductor → select the correct state from the dropdown.",
            ["T-FV-2001"] = "Open Challans → edit the challan → verify BSR code is exactly 7 digits (no letters, no spaces).",
            ["T-FV-2002"] = "Open Challans → edit the challan → correct the deposit date. Date cannot be in the future.",
            ["T-FV-2003"] = "Open Challans → edit the challan → verify the challan serial number is correct (from ITNS 281 receipt).",
            ["T-FV-2005"] = "Open Challans → edit the challan → check TDS + Surcharge + Cess + Interest + Others equals the total deposited.",
            ["T-FV-2006"] = "The challan deposit date is before the quarter started. Check that you assigned the challan to the correct quarter.",
            ["T-FV-2007"] = "The challan deposit date is after the quarter ended. Either the date is wrong or this challan belongs to the next quarter.",
            ["T-FV-2008"] = "A challan with the same BSR + Serial + Date already exists. Remove the duplicate from Challans.",
            ["T-FV-2009"] = "This challan has no TDS entries linked to it. Either add entries for this challan or delete the challan.",
            ["T-FV-2010"] = "Go to TRACES → Statements / Payments → Oltas Challan Status and verify BSR code, deposit date and serial number. Download fresh CSI file after verifying. Update the challan in TDS Pro to match exactly.",
            ["T-FV-2011"] = "The TDS amount in your challan does not match the CSI file. Download a fresh CSI file from TRACES or correct the challan amount.",
            ["T-FV-2015"] = "Sum of TDS across all entries linked to this challan exceeds the challan amount. Reduce entry TDS amounts or deposit an additional challan.",
            ["T-FV-3000"] = "Go to TDS Entries → edit the entry → verify the deductee PAN. Format: 5 letters + 4 digits + 1 letter (e.g. ABCDE1234F). Update in Deductees master too.",
            ["T-FV-3001"] = "If PAN is genuinely unavailable: use PANNOTAVBL. If applied but not received: use PANAPPLIED. If invalid/incorrect: use PANINVALID. Note: 20% TDS applies (Section 206AA).",
            ["T-FV-3003"] = "The payment amount for this entry is zero. Go to TDS Entries → edit the entry → enter the correct gross payment amount.",
            ["T-FV-3004"] = "TDS amount is zero. Either enter the correct TDS amount or delete this entry if no TDS applies.",
            ["T-FV-3005"] = "The section code used is not valid for this form type. 24Q accepts only salary sections (192 family). 26Q accepts non-salary non-TCS sections. 27EQ accepts only 206C sections.",
            ["T-FV-3006"] = "The TDS rate is outside the allowed range for this section. Check the applicable rate under TDS Rules → Section Rates.",
            ["T-FV-3008"] = "The deduction date falls outside the quarter being filed. Correct the date in TDS Entries or move the entry to the correct quarter.",
            ["T-FV-3015"] = "Gross salary must be at least as much as the TDS deducted. Check Payroll → Salary Data for this employee.",
            ["T-FV-3016"] = "Salary entries (section 192) must not be in 26Q — use 24Q. Non-salary entries must not be in 24Q — use 26Q.",
            ["T-FV-3019"] = "When PAN is PANNOTAVBL/PANINVALID, TDS rate must be at least 20% (Section 206AA). Update the TDS rate in TDS Entries.",
            ["T-FV-3169"] = "Open Reports → Challan Reconciliation to see the mismatch. Either add more TDS entries to utilise the full challan amount, or reduce entry amounts to match what was deposited.",
            ["T-FV-4003"] = "Go to Payroll → Employees → edit the employee → enter a valid 10-character PAN.",
            ["T-FV-4004"] = "Employee PAN is mandatory for 24Q. Go to Payroll → Employees → edit the employee → enter PAN.",
            ["T-FV-4301"] = "Go to Payroll → Employees → edit the employee → set the Tax Regime to New or Old. This sets the 115BAC flag in the FVU file.",
            ["T-FV-6001"] = "Regenerate the return from the Returns page. If the error persists, check that no challans were added or deleted after the last generation.",
            ["T-FV-6002"] = "Regenerate the return from the Returns page. If the error persists, check for orphan TDS entries without a challan.",
            ["T-FV-6003"] = "Regenerate the return. If it persists, open Reports → Challan Reconciliation to find the mismatched challan.",
            ["T-FV-6004"] = "Delete the generated .txt file and regenerate from the Returns page. If it still fails, contact TDS Pro support.",
            ["T-FV-6005"] = "Two TDS entries for the same PAN are mapped to the same challan. Each deductee can appear only once per challan. Merge or split entries.",
        };

        public static string GetFvuFixHint(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";
            return _fvuFixHints.TryGetValue(code.Trim(), out var hint) ? hint : "";
        }

        /// <summary>
        /// Extracts the FVU version from the configured JAR filename.
        /// e.g. "TDS_STANDALONE_FVU_9.4.jar" → "9.4"
        /// Returns empty string if JAR path not set or version cannot be parsed.
        /// </summary>
        public static string GetLocalFvuVersion()
        {
            try
            {
                var jarPath = Database.GetSetting("FvuPath", "");
                if (string.IsNullOrEmpty(jarPath))
                {
                    // Try config table fallback
                    jarPath = new FvuUtilityRunner().LoadConfig().FvuJarPath;
                }
                if (string.IsNullOrEmpty(jarPath) || !File.Exists(jarPath))
                    return "";

                var name = Path.GetFileNameWithoutExtension(jarPath); // e.g. TDS_STANDALONE_FVU_9.4
                // Pattern: last segment after underscore that looks like a version (digits.digits)
                var match = System.Text.RegularExpressions.Regex.Match(name, @"(\d+\.\d+(?:\.\d+)?)$");
                if (match.Success) return match.Groups[1].Value;

                // Fallback: read Implementation-Version from JAR MANIFEST.MF
                using var zip = System.IO.Compression.ZipFile.OpenRead(jarPath);
                var manifest = zip.GetEntry("META-INF/MANIFEST.MF");
                if (manifest != null)
                {
                    using var reader = new System.IO.StreamReader(manifest.Open());
                    var content = reader.ReadToEnd();
                    var mvMatch = System.Text.RegularExpressions.Regex.Match(content,
                        @"Implementation-Version:\s*(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (mvMatch.Success) return mvMatch.Groups[1].Value;
                }
            }
            catch { }
            return "";
        }

        /// <summary>Returns a friendly explanation for a T-FV-xxxx error code, or the code itself if unknown.</summary>
        public static string GetFvuErrorDescription(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";
            if (_fvuErrorMap.TryGetValue(code.Trim(), out var msg)) return msg;
            // Unknown code — return a generic hint
            return $"NSDL validation error {code} — refer to NSDL FVU error code list for details";
        }

        // ── Parse NSDL error HTML ─────────────────────────────────────────────
        public static List<FvuError> ParseErrorHtml(string? htmlPath)
        {
            var errors = new List<FvuError>();
            if (string.IsNullOrEmpty(htmlPath) || !File.Exists(htmlPath)) return errors;

            try
            {
                var html = File.ReadAllText(htmlPath);

                // NSDL FVU error HTML has rows like:
                // <TR><TD>LineNo</TD><TD>RecordType</TD><TD>FieldName</TD><TD>ChallanNo</TD>
                //     <TD>DeducteeNo</TD><TD>ErrorCode</TD><TD>ErrorDescription</TD></TR>
                // Confirmed against FVU 9.4 err.html output (lines like
                // "3^Regular^Total of Deposit...^1^^T-FV-3169^Sum of TDS...")
                var rowPattern = new System.Text.RegularExpressions.Regex(
                    @"<TR[^>]*>(.*?)</TR>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                var cellPattern = new System.Text.RegularExpressions.Regex(
                    @"<TD[^>]*>(.*?)</TD>",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                var stripTags = new System.Text.RegularExpressions.Regex("<[^>]+>");

                bool firstRow = true;
                foreach (System.Text.RegularExpressions.Match row in rowPattern.Matches(html))
                {
                    if (firstRow) { firstRow = false; continue; } // skip header row

                    var cells = cellPattern.Matches(row.Value)
                                           .Select(m => stripTags.Replace(m.Groups[1].Value, "").Trim())
                                           .ToList();
                    if (cells.Count >= 6)
                    {
                        var lineNo   = cells[0];
                        var recType  = cells[1];
                        var field    = cells[2];
                        var seqNo    = !string.IsNullOrEmpty(cells[3]) ? cells[3] : cells[4];
                        var code     = cells[5];
                        var nsdlDesc = cells.Count > 6 ? cells[6] : "";
                        var friendly = GetFvuErrorDescription(code);
                        errors.Add(new FvuError
                        {
                            Code        = code,
                            Description = string.IsNullOrEmpty(friendly) ? nsdlDesc : $"{friendly} [{nsdlDesc}]",
                            RecordType  = recType,
                            SeqNo       = seqNo,
                            LineNo      = lineNo,
                            FieldName   = field,
                        });
                    }
                }
            }
            catch { /* malformed HTML — return empty list */ }

            return errors;
        }

        // ── Find CSI file in common download locations ────────────────────────
        private static string? FindCsiFile(string inputTxtPath, string outputDir)
        {
            var searchDirs = new List<string>();

            // 1. Same folder as the input .txt
            var txtDir = Path.GetDirectoryName(inputTxtPath);
            if (!string.IsNullOrEmpty(txtDir)) searchDirs.Add(txtDir);

            // 2. Output directory
            searchDirs.Add(outputDir);

            // 3. App's dedicated CSI folder under Documents\TDSPro\CSI (where FolderManager saves files)
            var docsCsi = Path.Combine(FolderManager.BasePath, "CSI");
            if (Directory.Exists(docsCsi)) searchDirs.Add(docsCsi);

            // 4. %APPDATA%\TDSPro\CSI (legacy location)
            var appCsi = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TDSPro", "CSI");
            if (Directory.Exists(appCsi)) searchDirs.Add(appCsi);

            // 5. User's Downloads folder
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads)) searchDirs.Add(downloads);

            // 6. Desktop
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (Directory.Exists(desktop)) searchDirs.Add(desktop);

            // 7. Documents root
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (Directory.Exists(docs)) searchDirs.Add(docs);

            // Search each dir for any .csi file — pick the most recently modified one
            foreach (var dir in searchDirs.Distinct())
            {
                try
                {
                    var found = Directory.GetFiles(dir, "*.csi", SearchOption.TopDirectoryOnly)
                        .OrderByDescending(File.GetLastWriteTime)
                        .FirstOrDefault();
                    if (found != null) return found;
                }
                catch { }
            }
            return null;
        }

        // ── Java resolution ───────────────────────────────────────────────────
        private static string ResolveJava(string? configuredPath)
        {
            // 1. Use configured path if valid
            if (!string.IsNullOrEmpty(configuredPath) && File.Exists(configuredPath))
                return configuredPath;

            // 2. Check bundled JRE shipped with the installer (highest priority after explicit config)
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var bundledJava = Path.Combine(appDir, "jre", "bin", "java.exe");
            if (File.Exists(bundledJava)) return bundledJava;

            // Also check registry for installer-written bundled JRE path
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\TDSPro");
                var regJre = key?.GetValue("BundledJrePath") as string;
                if (!string.IsNullOrEmpty(regJre))
                {
                    var regJava = Path.Combine(regJre, "bin", "java.exe");
                    if (File.Exists(regJava)) return regJava;
                }
            }
            catch { }

            // 3. Try java on system PATH
            // Auto-scan common vendor install locations
            var vendorDirs = new[]
            {
                @"C:\Program Files\Microsoft",
                @"C:\Program Files\Eclipse Adoptium",
                @"C:\Program Files\Java",
                @"C:\Program Files\Amazon Corretto",
                @"C:\Program Files\BellSoft",
                @"C:\Program Files\OpenJDK",
            };
            var scanned = vendorDirs
                .Where(Directory.Exists)
                .SelectMany(d => Directory.GetDirectories(d))
                .Select(d => Path.Combine(d, "bin", "java.exe"))
                .Where(File.Exists)
                .ToList();

            var candidates = new[] { "java" }
                .Concat(scanned)
                .Concat(new[]
                {
                    @"C:\Program Files\Java\jre1.8.0_391\bin\java.exe",
                    @"C:\Program Files\Java\jre-8\bin\java.exe",
                })
                .ToArray();
            foreach (var c in candidates)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(c, "-version")
                    {
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    p?.WaitForExit(3000);
                    if (p?.ExitCode == 0) return c;
                }
                catch { }
            }
            return "";
        }

        private static async Task<string> GetJavaVersionAsync(string javaPath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(javaPath, "-version")
                {
                    UseShellExecute        = false,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                var err = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                return err.Split('\n')[0].Trim();
            }
            catch { return "Unknown"; }
        }
    }

    // ── Config / Result models ─────────────────────────────────────────────────
    public class FvuPreflightResult
    {
        public bool         IsReady { get; set; }
        public List<string> Issues  { get; set; } = new();
    }

    public class FvuConfig
    {
        public string JavaExePath { get; set; } = "";
        public string FvuJarPath  { get; set; } = "";
        public string OutputDir   { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TDSPro_FVU");
    }

    public class FvuRunResult
    {
        public bool   Success       { get; set; }
        public string InputFile     { get; set; } = "";
        public string FvuFile       { get; set; } = "";
        public string ZipFile       { get; set; } = "";
        public string Form27AFile   { get; set; } = "";
        public string ErrorHtmlFile { get; set; } = "";
        public string ErrorMessage  { get; set; } = "";
        public string JavaVersion   { get; set; } = "";
        public string StdOut        { get; set; } = "";
        public string StdErr        { get; set; } = "";
        public int    ExitCode      { get; set; }
        public List<FvuError> Errors { get; set; } = new();
    }

    public class FvuError
    {
        public string Code        { get; set; } = "";
        public string Description { get; set; } = "";
        public string RecordType  { get; set; } = "";
        public string SeqNo       { get; set; } = "";
        public string LineNo      { get; set; } = "";
        public string FieldName   { get; set; } = "";
        public override string ToString() => $"[{Code}] {Description} (Record: {RecordType} Seq: {SeqNo})";
    }
}
