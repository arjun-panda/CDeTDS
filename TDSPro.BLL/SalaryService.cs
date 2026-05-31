using TDSPro.DAL;
using TDSPro.DAL.Models;
using TDSPro.Common;

namespace TDSPro.BLL
{
    public class SalaryService
    {
        private readonly SalaryRepository _repo = new();

        /// <summary>Save without validation (legacy callers). Prefer SaveValidated.</summary>
        public void Save(MonthlySalaryEntry e) => _repo.Save(e);

        /// <summary>Validate then save. Returns errors if validation fails; nothing written.</summary>
        public (bool ok, string msg) SaveValidated(MonthlySalaryEntry e)
        {
            var v = PayrollValidators.ValidateMonthlyEntry(e);
            if (!v.Ok) return (false, v.ErrorSummary);
            _repo.Save(e);
            return (true, v.Warnings.Count > 0 ? $"Saved (warnings: {v.WarningSummary})" : "Saved.");
        }

        public ValidationResult ValidateMonthly(MonthlySalaryEntry e) => PayrollValidators.ValidateMonthlyEntry(e);

        /// <summary>
        /// Auto-create or update a Section 192 TDS Entry for this monthly salary row.
        /// Links to the deductee table via employee PAN (creates deductee if missing).
        /// Idempotent — keyed by remark tag [AUTO:salary:{empId}:{fy}:{month}].
        /// </summary>
        public (bool ok, string msg) UpsertSec192Entry(MonthlySalaryEntry entry, Employee emp, int deductorId)
        {
            // Do NOT skip nil-TDS employees — 24Q returns require every salary payment as a deductee row.
            if (string.IsNullOrWhiteSpace(emp.Pan))
                return (false, $"Employee {emp.Name} has no PAN — cannot create Sec 192 entry.");

            var payRepo = new PayrollRepository();
            int deducteeId = payRepo.EnsureSalaryDeductee(emp);
            if (deducteeId == 0)
                return (false, "Failed to resolve deductee for employee.");

            string tag = $"[AUTO:salary:{emp.Id}:{entry.FinancialYear}:{entry.Month}]";

            // Look up existing auto-created entry for this (employee, FY, month)
            var tdsRepo = new TDSPro.DAL.Repositories.TdsEntryRepository();
            var existing = tdsRepo.GetByRemarksTag(deductorId, tag);

            int dim = DateTime.DaysInMonth(entry.Year, entry.Month);
            var entryDate = new DateTime(entry.Year, entry.Month, dim);   // last day of pay month
            // Sec 192 deposit due date — 7th of next month (April–February); 30 Apr for March
            DateTime due = entry.Month == 3
                ? new DateTime(entry.Year, 4, 30)
                : new DateTime(entry.Year + (entry.Month == 12 ? 1 : 0),
                               entry.Month == 12 ? 1 : entry.Month + 1, 7);
            string quarter = entry.Month switch
            {
                >= 4 and <= 6  => "Q1",
                >= 7 and <= 9  => "Q2",
                >= 10 and <= 12=> "Q3",
                _              => "Q4"
            };

            var te = existing ?? new TdsEntry();
            te.DeductorId    = deductorId;
            te.DeducteeId    = deducteeId;
            te.Section       = "192";
            te.PaymentNature = "Salary";
            te.Amount        = entry.GrossPayment;
            te.Rate          = 0;
            te.TdsAmount     = entry.TdsDeducted;
            te.Surcharge     = 0;
            te.Cess          = 0;
            te.EntryDate     = entryDate;
            te.DueDate       = due;
            te.FinancialYear = entry.FinancialYear;
            te.Quarter       = quarter;
            te.Remarks       = $"Salary TDS — {new DateTime(1,entry.Month,1):MMMM} {entry.Year} {tag}";
            if (existing == null)
            {
                // New entry: set defaults for payment fields
                te.Status    = "Pending";
                te.ChallanNo = "";
                te.Interest  = 0;
                te.LateFee   = 0;
                te.TotalTds  = entry.TdsDeducted;
            }
            else
            {
                // Existing entry: preserve user-entered payment fields (challan, interest, late fee, status)
                te.TotalTds = entry.TdsDeducted + te.Interest + te.LateFee;
            }

            var result = tdsRepo.Save(te);
            return (result.Ok, result.Ok ? $"Sec 192 entry {(existing == null ? "created" : "updated")} (₹{entry.TdsDeducted:N0})" : result.Msg);
        }

        /// <summary>
        /// Reverse of UpsertSec192Entry — finds the auto-created Sec 192 entry for this
        /// (employee, FY, month) by remarks tag and deletes it. Called from Unlock so
        /// stale TDS rows don't pollute quarterly returns after a correction.
        /// </summary>
        public (bool ok, string msg) RemoveSec192Entry(int employeeId, string fy, int month, int deductorId)
        {
            string tag = $"[AUTO:salary:{employeeId}:{fy}:{month}]";
            var tdsRepo = new TDSPro.DAL.Repositories.TdsEntryRepository();
            var match = tdsRepo.GetByRemarksTag(deductorId, tag);
            if (match == null) return (true, "No Sec 192 entry to remove.");
            if (!string.IsNullOrEmpty(match.ChallanNo))
                return (false, $"Sec 192 entry already linked to challan {match.ChallanNo}. Unlink first.");
            var r = tdsRepo.Delete(match.Id);
            return (r.Ok, r.Ok ? $"Sec 192 entry removed (₹{match.TotalTds:N0})." : r.Msg);
        }

        public MonthlySalaryEntry? Get(int empId, string fy, int month) => _repo.Get(empId, fy, month);
        public List<MonthlySalaryEntry> GetAllForFY(int empId, string fy) => _repo.GetAllForFY(empId, fy);

        // ── FY helpers ───────────────────────────────────────────────────────
        public static int[] FyMonths  => new[]{4,5,6,7,8,9,10,11,12,1,2,3};
        public static string[] FyMonthNames => new[]{"Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec","Jan","Feb","Mar"};

        public static int FyStartYear(string fy)
            => fy.Length >= 4 && int.TryParse(fy[..4], out int y) ? y : DateTime.Today.Year;

        public static int CalendarYear(string fy, int month)
        {
            int s = FyStartYear(fy);
            return month >= 4 ? s : s + 1;
        }

        public static int MonthsRemaining(string fy, int fromMonth)
        {
            int s = FyStartYear(fy);
            int fromY = CalendarYear(fy, fromMonth);
            var start = new DateTime(fromY, fromMonth, 1);
            var end   = new DateTime(s + 1, 3, 31);
            int months = (end.Year - start.Year) * 12 + end.Month - start.Month + 1;
            return Math.Max(1, months);
        }

        // ── Compute monthly TDS ───────────────────────────────────────────────
        /// <summary>
        /// Fills TaxComputed, SurchargeAmt, CessAmt on the entry.
        /// Does NOT override TdsDeducted — caller decides.
        /// </summary>
        public AnnualComputation ComputeMonth(
            MonthlySalaryEntry entry,
            Employee emp,
            string fy,
            TaxDeclaration decl)
        {
            entry.RecalcGross();

            // Pull all saved months for this employee
            var saved = _repo.GetAllForFY(emp.Id, fy)
                             .Where(e => !(e.Month == entry.Month && e.Year == entry.Year))
                             .ToList();
            saved.Add(entry);  // include current month

            var result = ComputeAnnual(saved, emp, fy, decl, entry.Month);

            // Fill computed TDS breakdown on entry
            double tdsPerMonth = result.ThisMonthTds;
            entry.TaxComputed = tdsPerMonth;
            // Proportional surcharge / cess allocation
            double chosen = result.ChosenRegime == "New" ? result.NewRegime.TotalTax : result.OldRegime.TotalTax;
            if (chosen > 0)
            {
                double sc   = result.ChosenRegime == "New" ? result.NewRegime.Surcharge : result.OldRegime.Surcharge;
                double cess = result.ChosenRegime == "New" ? result.NewRegime.Cess      : result.OldRegime.Cess;
                entry.SurchargeAmt = Math.Round(tdsPerMonth * sc   / chosen);
                entry.CessAmt      = Math.Round(tdsPerMonth * cess / chosen);
            }
            return result;
        }

        // ── Annual dual-regime computation ────────────────────────────────────
        public AnnualComputation ComputeAnnual(
            List<MonthlySalaryEntry> entries,
            Employee emp,
            string fy,
            TaxDeclaration decl,
            int forMonth,
            double? reimbShortfallOverride = null,
            double? ytdTdsOverride = null)
        {
            int fyStart = FyStartYear(fy);
            var fyMonths = FyMonths;

            // Separate actual (saved) months vs remaining (projected)
            var actualByMonth = entries.ToDictionary(e => e.Month);
            int currentFyIndex = Array.IndexOf(fyMonths, forMonth);
            if (currentFyIndex < 0) currentFyIndex = 0;   // fallback: treat as April
            int actualCount = 0; double projectionBasis = 0;

            // Sum actual gross (Apr to current month)
            // annualGrossActual = TRUE gross payment (GrossPayment, before exemptions)
            // annualSec10Actual = total sec10 exemptions from line items (for display and tax formula)
            double annualGrossActual = 0;
            double annualSec10ExemptActual = 0;   // "other" + "perq" exempts from LineItems
            double annualPerqOnlyExemptedActual = 0;  // only perq-category (taxable in new regime)
            var sec10Actual = new Dictionary<(string name, string rule, string cat), double>();
            for (int i = 0; i <= currentFyIndex; i++)
            {
                int m = fyMonths[i];
                if (actualByMonth.TryGetValue(m, out var e))
                {
                    e.PerqExempted = e.LineItems.Sum(li => li.Exempt);
                    e.RecalcGross();
                    annualGrossActual += e.GrossPayment;   // TRUE gross (Conveyance included as received)
                    annualSec10ExemptActual += e.PerqExempted;  // all line item exempts
                    actualCount++;
                    foreach (var li in e.LineItems)
                    {
                        if (li.Exempt <= 0) continue;
                        var key = (li.Name, li.RuleRef, li.Category);
                        sec10Actual[key] = sec10Actual.GetValueOrDefault(key) + li.Exempt;
                        if (li.Category == "perq") annualPerqOnlyExemptedActual += li.Exempt;
                    }
                    if (e.LeaveEncExempted > 0)
                    {
                        var key = ("Leave Encashment", "Sec 10(10AA)", "other");
                        sec10Actual[key] = sec10Actual.GetValueOrDefault(key) + e.LeaveEncExempted;
                    }
                }
            }
            // Projection basis = current month's TRUE gross payment.
            // For sec10 exempts: if current month has no line items yet (new/unsaved entry),
            // fall back to last saved month with exempts so recurring items (Conveyance etc.) project correctly.
            // In that case also exclude current month from actual sec10 count and project it instead.
            projectionBasis = actualByMonth.TryGetValue(forMonth, out var cur) ? cur.GrossPayment : 0;

            // Project remaining months (after current)
            int projectedMonths = 12 - currentFyIndex - 1;

            // For sec10 exempts: if current month has no line items yet (new/unsaved entry),
            // fall back to last saved month with exempts and project it too.
            bool curHasLineExempts = cur?.LineItems.Any(l => l.Exempt > 0) == true;
            var lastSaved = fyMonths.Take(currentFyIndex + 1)
                                    .Select(m => actualByMonth.TryGetValue(m, out var em) ? em : null)
                                    .LastOrDefault(e => e != null && e.LineItems.Any(l => l.Exempt > 0));
            var projectionLineBasis = curHasLineExempts ? cur : lastSaved;
            int projectedMonthsForSec10 = curHasLineExempts ? projectedMonths : projectedMonths + 1;
            double annualSec10ExemptActualAdj = curHasLineExempts
                ? annualSec10ExemptActual
                : annualSec10ExemptActual - (cur?.PerqExempted ?? 0);
            double projectionSec10Basis = projectionLineBasis?.PerqExempted ?? 0;
            double annualGrossProjected       = projectionBasis * projectedMonths;
            double annualSec10ExemptProjected = projectionSec10Basis * projectedMonthsForSec10;

            double annualGross            = annualGrossActual + annualGrossProjected;
            double annualSec10LineExempts = annualSec10ExemptActualAdj + annualSec10ExemptProjected;


            // Variable pay (annual bonus + incentive) — added to projection so TDS spreads
            // across all months. If the bonus has already been entered as a Salary Data row,
            // it's already in annualGrossActual; we add it only if NOT yet present.
            // Use average actual monthly gross (not current-month structure) as the fixed baseline
            // so mid-year salary revisions don't cause false double-count.
            double annualVariable = (emp.Salary?.AnnualBonus ?? 0) + (emp.Salary?.AnnualIncentive ?? 0);
            double avgActualMonthly = actualCount > 0 ? annualGrossActual / actualCount : 0;
            double expectedFixedYtd = avgActualMonthly * (currentFyIndex + 1);
            if (annualGrossActual < expectedFixedYtd + annualVariable * 0.5)
                annualGross += annualVariable;

            // Reimbursement shortfall — amounts eligible but not claimed with bills become taxable
            double reimbShortfall = reimbShortfallOverride ?? new TDSPro.DAL.ReimbursementRepository()
                .GetForFy(emp.Id, fy)
                .Sum(c => c.Shortfall);
            annualGross += reimbShortfall;

            // Bill-substantiated reimbursements (conveyance, telephone, etc.) flow through
            // PerqExempted in each monthly entry, so they are ALREADY excluded from annualGross
            // (both actual months via GrossTaxableSalary and projected months via projectionBasis).
            // No further deduction needed — removing separate annualComponentsPaid.

            // Sum actual deductions
            double annualPf  = 0, annualPt  = 0, annualNps = 0;
            for (int i = 0; i <= currentFyIndex; i++)
            {
                int m = fyMonths[i];
                if (actualByMonth.TryGetValue(m, out var e))
                {
                    annualPf  += e.PfEmployee + e.VPF;
                    annualPt  += e.ProfessionalTax;
                    annualNps += e.NpsEmployer;
                }
            }
            // Project remaining months using current
            if (cur != null)
            {
                annualPf  += (cur.PfEmployee + cur.VPF) * projectedMonths;
                annualPt  += cur.ProfessionalTax        * projectedMonths;
                annualNps += cur.NpsEmployer             * projectedMonths;
            }

            // HRA exemption — use saved monthly entry if available, else salary structure
            double hraBasic = cur?.Basic > 0 ? cur.Basic : (emp.Salary?.Basic ?? 0);
            double hraHra   = cur?.HRA   > 0 ? cur.HRA   : (emp.Salary?.Hra   ?? 0);
            var hraCityType = !string.IsNullOrEmpty(emp.HraCityType) ? emp.HraCityType : decl.HraCityType;
            // RentPaid in DB is annual; CalcHraExemptionPublic takes monthly → divide by 12
            double hraExemption = PayrollService.CalcHraExemptionPublic(hraBasic, hraHra, decl.RentPaid / 12, hraCityType) * 12;

            // Age category — needed for 80D self-limit (senior citizen gets ₹50K, others ₹25K)
            DateTime? dob = DateTime.TryParseExact(emp.DateOfBirth, "dd-MMM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d1) ? d1 :
                (DateTime.TryParse(emp.DateOfBirth, out var d2) ? d2 : (DateTime?)null);
            var ageCategory  = TDSPro.Common.TaxRules.GetAgeCategory(dob, fy);
            bool isSelfSenior = ageCategory != TDSPro.Common.AgeCategory.Below60;

            // 80D limits: ₹50K if employee is senior citizen, ₹25K otherwise (Section 80D)
            double limit80DSelf    = isSelfSenior ? 50000 : 25000;
            // 80D parents: ₹50K if parent is senior citizen, ₹25K otherwise
            double limit80DParents = decl.IsParentSeniorCitizen ? 50000 : 25000;

            // ── OLD REGIME ────────────────────────────────────────────────────
            // Old regime std deduction: ₹50,000 for all FYs (old regime never raised to ₹75K)
            var oldRules = TDSPro.Common.TaxRules.GetRules(fy, false, ageCategory);
            double stdDedOld = oldRules.StandardDeduction;

            // Chapter VI-A deductions (old regime only — all applicable sections)
            // 80TTA (non-senior, ₹10K) and 80TTB (senior, ₹50K) are mutually exclusive
            double tta_ttb = isSelfSenior
                           ? Math.Min(decl.Sec80TTB, 50000)   // senior: 80TTB only
                           : Math.Min(decl.Sec80TTA, 10000);  // non-senior: 80TTA only
            double chap6a = Math.Min(decl.Sec80C + annualPf, 150000)          // 80C: LIC/PF/ELSS — PF counts, cap ₹1.5L
                          + Math.Min(decl.Sec80D_Self,    limit80DSelf)         // 80D self/family health insurance
                          + Math.Min(decl.Sec80D_Parents, limit80DParents)      // 80D parents health insurance
                          + decl.Sec80G                                          // 80G donations (no cap for 100% eligible)
                          + Math.Min(decl.Sec80CCD_Employee, 50000)             // 80CCD(1B) additional NPS — ₹50K cap
                          + decl.Sec80E                                          // 80E education loan interest — no cap
                          + Math.Min(decl.Sec80EEA, 150000)                     // 80EEA housing loan first home — ₹1.5L cap
                          + tta_ttb                                              // 80TTA or 80TTB (mutually exclusive)
                          + Math.Min(decl.Sec80DD,  125000)                     // 80DD differently abled dependent — ₹1.25L
                          + Math.Min(decl.Sec80U,   125000)                     // 80U self differently abled — ₹1.25L
                          + decl.OtherDeductions;
            // LTA u/s 10(5) is a Sec 10 exemption — deducted from gross separately, NOT inside chap6a
            double ltaExemption = decl.LtaExemption;
            // 80CCD(2) cap is based on salary structure Basic (not monthly entry which may be 0 on LOP)
            double annualBasic  = (emp.Salary?.Basic ?? 0) * 12;
            double nps80CCD2    = Math.Min(annualNps, annualBasic * TDSPro.Common.TaxRules.Get80CCD2Rate(fy, false));
            double nps80CCD2New = Math.Min(annualNps, annualBasic * TDSPro.Common.TaxRules.Get80CCD2Rate(fy, true));

            // annualGross = TRUE gross (GrossPayment) already includes all components as received.
            // annualSec10LineExempts = bills reimbursements + perq exempts from line items.
            // "other" category exempts are exempt BOTH regimes u/s 10(14)(i).
            // "perq" category exempts are exempt old regime; taxable new regime → add back for new regime.
            var newRules     = TDSPro.Common.TaxRules.GetRules(fy, true);
            double stdDedNew = newRules.StandardDeduction;
            double curPerqOnlyExempted = cur?.LineItems.Where(l => l.Category == "perq").Sum(l => l.Exempt) ?? 0;
            double annualPerqOnlyExemptedProjected = curPerqOnlyExempted * projectedMonths;
            // newRegimeAddBack: perq-only exempts must be added back for new regime (taxable there)
            double newRegimeAddBack = annualPerqOnlyExemptedActual + annualPerqOnlyExemptedProjected;
            // "other" (bills reimbursement) exempts stay exempt in new regime — no add-back
            double annualOtherExempts = Math.Max(0, annualSec10LineExempts - newRegimeAddBack);
            // trueAnnualGross = gross for display; same for both regimes (true GrossPayment)
            double trueAnnualGross = annualGross;

            // Build itemised Sec 10 list — scale projected months proportionally from current
            var sec10Items = new List<TDSPro.DAL.Models.Sec10Item>();
            // HRA u/s 10(13A)
            if (hraExemption > 0)
                sec10Items.Add(new TDSPro.DAL.Models.Sec10Item { Name = "HRA", RuleRef = "Sec 10(13A)", OldRegime = hraExemption, NewRegime = 0 });
            // LTA u/s 10(5) — exempt in BOTH regimes (Sec 10(5) applies regardless of regime choice)
            if (ltaExemption > 0)
                sec10Items.Add(new TDSPro.DAL.Models.Sec10Item { Name = "LTA", RuleRef = "Sec 10(5)", OldRegime = ltaExemption, NewRegime = ltaExemption });
            // Named line items from actual months + project remaining.
            // Use projectionLineBasis (current month if it has line items, else last saved month with exempts)
            // so recurring exemptions like Conveyance project correctly even when current month not yet entered.
            foreach (var kvp in sec10Actual)
            {
                double projectedAmt = 0;
                if (projectedMonthsForSec10 > 0 && projectionLineBasis != null)
                {
                    var curLi = projectionLineBasis.LineItems.FirstOrDefault(l => l.Name == kvp.Key.name && l.RuleRef == kvp.Key.rule && l.Category == kvp.Key.cat);
                    projectedAmt = (curLi?.Exempt ?? 0) * projectedMonthsForSec10;
                }
                double total = kvp.Value + projectedAmt;
                if (total <= 0) continue;
                // Bills-based reimbursements u/s 10(14)(i) are exempt in both regimes per CBDT.
                // "other" category = bills-substantiated reimbursements (always both-regime exempt).
                // "perq" category without explicit Sec 10 rule = taxable in new regime.
                bool newRegimeExempt = kvp.Key.cat == "other"                                               // all bills reimbursements
                                    || kvp.Key.rule.Contains("10(14)(i)", StringComparison.OrdinalIgnoreCase)
                                    || kvp.Key.rule.Contains("10(10AA)", StringComparison.OrdinalIgnoreCase);
                sec10Items.Add(new TDSPro.DAL.Models.Sec10Item
                {
                    Name      = kvp.Key.name,
                    RuleRef   = kvp.Key.rule,
                    OldRegime = total,
                    NewRegime = newRegimeExempt ? total : 0,
                });
            }

            // ── OLD REGIME ────────────────────────────────────────────────────
            // annualSec10LineExempts = bills reimbursements + perq exempts (all line item exempts)
            // hraExemption and ltaExemption are separate Sec 10 exemptions not in lineItems
            double taxableOld = annualGross
                - annualSec10LineExempts                    // Sec 10 line item exempts (bills reimb + perq)
                - stdDedOld - hraExemption - ltaExemption - annualPt - chap6a - nps80CCD2
                + decl.IncomeOtherSources;
            taxableOld = Math.Max(0, taxableOld);

            var oldR = BuildRegime("Old Regime", taxableOld, trueAnnualGross, stdDedOld, hraExemption, annualPt, chap6a, nps80CCD2, decl.IncomeOtherSources, false, fy, ageCategory, newRegimeAddBack, sec10Items);

            // ── NEW REGIME — FY-aware std deduction and slabs ─────────────────
            // annualOtherExempts = bills-reimbursement exempts ("other" cat) — exempt in BOTH regimes u/s 10(14)(i)
            // newRegimeAddBack = perq exempts — taxable in new regime, so do NOT deduct them here
            // ltaExemption u/s 10(5) applies in BOTH regimes
            double taxableNew = Math.Max(0, trueAnnualGross - annualOtherExempts - ltaExemption - stdDedNew - nps80CCD2New + decl.IncomeOtherSources);

            var newR = BuildRegime("New Regime", taxableNew, trueAnnualGross, stdDedNew, 0, 0, 0, nps80CCD2New, decl.IncomeOtherSources, true, fy, TDSPro.Common.AgeCategory.Below60, 0, sec10Items);

            // YTD TDS
            int fromYear = CalendarYear(fy, forMonth);
            double ytd = ytdTdsOverride ?? _repo.GetYtdTds(emp.Id, fy, forMonth, fromYear);
            int remaining = MonthsRemaining(fy, forMonth);

            // If the employee has an explicit regime preference, honour it.
            // Otherwise pick whichever regime has the lower total tax (auto-optimal).
            string chosen;
            if (string.Equals(emp.TaxRegime, "Old", StringComparison.OrdinalIgnoreCase))
                chosen = "Old";
            else if (string.Equals(emp.TaxRegime, "New", StringComparison.OrdinalIgnoreCase))
                chosen = "New";
            else
                chosen = newR.TotalTax <= oldR.TotalTax ? "New" : "Old";

            return new AnnualComputation
            {
                OldRegime        = oldR,
                NewRegime        = newR,
                ChosenRegime     = chosen,
                YtdTdsDeducted   = ytd,
                MonthsRemaining  = remaining,
                ComputedForMonth = forMonth,
                MonthsActual     = actualCount,
                MonthsProjected  = projectedMonths,
            };
        }

        private static RegimeResult BuildRegime(
            string name, double taxableIncome, double grossSalary,
            double stdDed, double hraEx, double pt, double chap6a, double nps2, double otherSrc,
            bool isNew, string fy,
            TDSPro.Common.AgeCategory age = TDSPro.Common.AgeCategory.Below60,
            double reimbExemption = 0,
            List<TDSPro.DAL.Models.Sec10Item>? sec10Items = null)
        {
            var rules                = TDSPro.Common.TaxRules.GetRules(fy, isNew, age);
            double rawTax            = TDSPro.Common.TaxRules.ComputeSlabTax(taxableIncome, rules);
            var (taxAfter, rebate)   = TDSPro.Common.TaxRules.Apply87A(rawTax, taxableIncome, rules);
            double sc                = TDSPro.Common.TaxRules.CalcSurcharge(taxAfter, taxableIncome, rules);
            double cess              = Math.Round((taxAfter + sc) * 0.04);
            double total             = Math.Round(taxAfter + sc + cess);

            return new RegimeResult
            {
                RegimeName        = name,
                GrossSalary       = grossSalary,
                ReimbExemption    = reimbExemption,
                StandardDeduction = stdDed,
                HraExemption      = hraEx,
                ProfTaxDeduction  = pt,
                Chapter6A         = chap6a,
                NpsEmployer80CCD2 = nps2,
                IncomeOtherSources= otherSrc,
                TotalIncome       = taxableIncome,
                TaxOnIncome       = rawTax,
                Rebate87A         = rebate,
                TaxAfterRebate    = taxAfter,
                Surcharge         = sc,
                Cess              = cess,
                TotalTax          = total,
                Sec10Items        = sec10Items ?? new(),
            };
        }
    }
}
