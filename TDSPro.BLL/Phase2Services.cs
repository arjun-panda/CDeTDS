using TDSPro.DAL;
using TDSPro.DAL.Models;
using TDSPro.DAL.Repositories;

namespace TDSPro.BLL
{
    // ── Reports Service ───────────────────────────────────────────────────────
    public class ReportsService
    {
        private readonly ReportsRepository _repo = new();

        public List<QuarterSummary>    GetQuarterSummary(string fy, int? deductorId = null)
            => _repo.GetQuarterSummary(fy, deductorId);

        public List<DeducteeReport>    GetDeducteeReport(string fy, string? quarter = null, int? deductorId = null)
            => _repo.GetDeducteeReport(fy, quarter, deductorId);

        public List<SectionReport>     GetSectionReport(string fy, string? quarter = null, int? deductorId = null)
            => _repo.GetSectionReport(fy, quarter, deductorId);

        public ChallanReconciliation   GetChallanRecon(string fy, string? quarter = null, int? deductorId = null)
            => _repo.GetChallanReconciliation(fy, quarter, deductorId);
    }

    // ── Return / FVU Service ──────────────────────────────────────────────────
    public class ReturnService
    {
        private readonly ReportsRepository _repo    = new();
        private readonly FvuUtilityRunner  _runner  = new();

        public ReturnData BuildReturn(int deductorId, string fy, string quarter, string formType)
            => _repo.BuildReturnData(deductorId, fy, quarter, formType);

        /// <summary>
        /// Validate return data — returns list of FvuValidationError.
        /// Blocking errors must be fixed before generating FVU.
        /// </summary>
        public List<FvuValidationError> ValidateFull(ReturnData data)
            => FvuGenerator.Validate(data);

        /// <summary>
        /// Legacy string-list validation (used by ReturnForm Step 3).
        /// </summary>
        public List<string> Validate(ReturnData data)
            => FvuGenerator.Validate(data).Select(e => e.ToString()).ToList();

        /// <summary>
        /// Generate the .txt input file for NSDL FVU utility.
        /// Returns (ok, path, error).
        /// </summary>
        public (bool Ok, string Path, string Error) GenerateTxtFile(
            ReturnData data, string outputDir, int deductorId = 0)
        {
            try
            {
                var errors = FvuGenerator.Validate(data);
                var blocking = errors.Where(e => e.IsBlocking).ToList();
                if (blocking.Count > 0)
                    return (false, "", string.Join("\n", blocking.Select(e => e.Message)));

                Directory.CreateDirectory(outputDir);
                var content  = FvuGenerator.Generate(data);
                var fileName = FvuGenerator.GetFileName(data);
                var fullPath = Path.Combine(outputDir, fileName);
                File.WriteAllText(fullPath, content, System.Text.Encoding.ASCII);

                Database.LogAction("system", "FVU_TXT_GENERATE", "Return",
                    $"{data.Header.FormType}/{data.Header.TanOfDeductor}/{data.Header.Quarter}");

                // Save to filing history (PRN is empty until uploaded to portal — user updates later)
                if (deductorId > 0)
                    Database.SaveFilingHistory(
                        deductorId, data.Header.FormType, data.Header.FinancialYear, data.Header.Quarter,
                        data.Header.IsCorrection, data.Header.IsCorrection ? data.Header.CorrectionType : "",
                        prn: "", txtPath: fullPath);

                return (true, fullPath, "");
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        /// <summary>
        /// Legacy alias kept for ReturnForm.
        /// </summary>
        public (bool Ok, string Path, string Error) GenerateFvu(
            ReturnData data, string outputDir, int deductorId = 0)
            => GenerateTxtFile(data, outputDir, deductorId);

        /// <summary>
        /// Run NSDL FVU Java utility on the generated .txt file.
        /// Async — use await.
        /// </summary>
        public Task<FvuRunResult> RunFvuUtilityAsync(
            string txtFilePath, string outputDir,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
            => _runner.RunFvuAsync(txtFilePath, outputDir, progress, ct);

        public FvuPreflightResult CheckFvuPreflight() => _runner.CheckPreflight();
        public FvuConfig GetFvuConfig()  => _runner.LoadConfig();
        public void SaveFvuConfig(FvuConfig cfg) => _runner.SaveConfig(cfg);

        public string GetSampleFvuStructure(string formType = "26Q")
            => FvuGenerator.GetSampleStructure(formType);

        public string BuildTextReport(ReturnData data)
            => ExportHelper.BuildTextReport(data);

        public (bool Ok, string Path, string Error) ExportCsv(
            List<TdsEntry> entries, string outputDir, string fileName)
        {
            try
            {
                var csv  = ExportHelper.EntresToCsv(entries);
                var path = Path.Combine(outputDir, fileName);
                File.WriteAllText(path, csv, System.Text.Encoding.UTF8);
                return (true, path, "");
            }
            catch (Exception ex) { return (false, "", ex.Message); }
        }

        public (bool Ok, string Path, string Error) ExportChallanCsv(
            List<Challan> challans, string outputDir, string fileName)
        {
            try
            {
                var csv  = ExportHelper.ChallansToCsv(challans);
                var path = Path.Combine(outputDir, fileName);
                File.WriteAllText(path, csv, System.Text.Encoding.UTF8);
                return (true, path, "");
            }
            catch (Exception ex) { return (false, "", ex.Message); }
        }
    }
}
