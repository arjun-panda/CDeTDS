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
        {
            var data = _repo.BuildReturnData(deductorId, fy, quarter, formType);

            // For 24Q Q4: replace the stub SalaryDetails with correctly computed values.
            // ReportsRepository.BuildSalaryDetails (DAL) cannot call SalaryService (BLL),
            // so the real computation is done here using ComputeAnnual — the same engine
            // that drives monthly TDS, payslips, and Form 16.
            if (formType == "24Q" && quarter == "Q4")
                data.SalaryDetails = BuildSalaryDetailsFromComputation(deductorId, fy, data.Challans);

            return data;
        }

        private List<ReturnSalaryDetail> BuildSalaryDetailsFromComputation(
            int deductorId, string fy, List<ReturnChallanDetail> challans)
        {
            var salRepo  = new SalaryRepository();
            var payRepo  = new PayrollRepository();
            var salSvc   = new SalaryService();
            var result   = new List<ReturnSalaryDetail>();
            string lastChallanNo = challans.LastOrDefault()?.ChallanNo ?? "";

            // Get all employees who have salary entries this FY via PayrollRepository
            var allEmps = payRepo.GetAllEmployees(deductorId);

            int[] fyOrder = { 4, 5, 6, 7, 8, 9, 10, 11, 12, 1, 2, 3 };
            int fyYear = int.Parse(fy.Split('-')[0]);

            foreach (var emp in allEmps)
            {
                var entries = salRepo.GetAllForFY(emp.Id, fy);
                if (entries.Count == 0) continue;

                string pan      = emp.Pan;
                string name     = emp.Name;
                string gender   = emp.Sex ?? "Male";
                string joinStr  = emp.JoinDate ?? "";
                string leaveStr = emp.LeavingDate ?? "";
                string challanNo= lastChallanNo;

                var decl = payRepo.GetDeclaration(emp.Id, fy)
                           ?? new TaxDeclaration { EmployeeId = emp.Id, FinancialYear = fy };

                // Use the last saved month as "current" — gives full-year actual view,
                // no future projection, so all 12 months are treated as actuals.
                int lastMonth = entries
                    .OrderByDescending(e => { int i = System.Array.IndexOf(fyOrder, e.Month); return i >= 0 ? i : -1; })
                    .First().Month;

                AnnualComputation ann;
                try { ann = salSvc.ComputeAnnual(entries, emp, fy, decl, lastMonth); }
                catch { continue; }

                bool isNew  = ann.ChosenRegime.Equals("New", StringComparison.OrdinalIgnoreCase);
                var  chosen = isNew ? ann.NewRegime : ann.OldRegime;

                // Annual TDS = sum of all months actually deducted (not just Q4)
                double annualTds = entries.Sum(e => e.TdsDeducted);

                // Employment period
                var from = new DateTime(fyYear, 4, 1);
                var to   = new DateTime(fyYear + 1, 3, 31);
                if (!string.IsNullOrEmpty(joinStr)  && DateTime.TryParse(joinStr,  out var j) && j > from) from = j;
                if (!string.IsNullOrEmpty(leaveStr) && DateTime.TryParse(leaveStr, out var l) && l < to)   to   = l;

                string nsdlGender = gender?.ToUpper() switch { "F" => "W", "FEMALE" => "W", "W" => "W", "S" => "S", _ => "G" };
                string nsdlRegime = isNew ? "O" : "N";

                // Exempt u/s 10 = HRA + bills reimbursements (from line items)
                double exemptU10 = chosen.HraExemption + chosen.ReimbExemption;

                // GTI per NSDL SD[18]: Gross − Exempt u/s 10 − Std Deduction
                double gti = Math.Max(0, chosen.GrossSalary - exemptU10 - chosen.StandardDeduction);

                result.Add(new ReturnSalaryDetail
                {
                    Pan              = pan,
                    Name             = name,
                    Gender           = nsdlGender,
                    EmployeeCategory = "A",
                    TaxRegime        = nsdlRegime,
                    EmploymentFrom   = from,
                    EmploymentTo     = to,
                    Salary17_1       = chosen.GrossSalary,
                    ExemptU10        = exemptU10,
                    ExemptU10Count   = exemptU10 > 0 ? 1 : 0,
                    StandardDeduction= chosen.StandardDeduction,
                    GrossTotalIncome = gti,
                    TaxableIncome    = chosen.TotalIncome,
                    TaxPayable       = chosen.TaxAfterRebate,
                    Rebate87A        = chosen.Rebate87A,
                    Surcharge        = chosen.Surcharge,
                    Cess             = chosen.Cess,
                    TotalTaxPayable  = Math.Max(0, chosen.TotalTax),
                    TdsDeducted      = annualTds,
                    // 80CCD(2) employer NPS is a Ch6A deduction under new regime — must appear in
                    // SD[20]/[21] so SD[22] (GTI after Ch6A) matches the income tax was computed on.
                    Chapter6ATotal   = chosen.Chapter6A + (isNew ? chosen.NpsEmployer80CCD2 : 0),
                    Chapter6ACount   = (chosen.Chapter6A + (isNew ? chosen.NpsEmployer80CCD2 : 0)) > 0 ? 1 : 0,
                    ChallanNo        = challanNo,
                });
            }

            return result;
        }

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
