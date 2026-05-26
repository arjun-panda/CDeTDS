using TDSPro.DAL;
using TDSPro.DAL.Models;
using TDSPro.Common;

namespace TDSPro.BLL
{
    /// <summary>
    /// Salary computation engine.
    /// Supports both Old and New tax regimes for FY 2025-26 (AY 2026-27).
    /// Monthly TDS = (Total annual tax - YTD TDS) / remaining months.
    /// </summary>
    public class PayrollService
    {
        private readonly PayrollRepository _repo = new();

        // ── Professional Tax by state ─────────────────────────────────────────
        // States with simple flat monthly rates
        private static readonly Dictionary<string, double> PtFlat = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Maharashtra"]    = 200,
            ["Karnataka"]      = 200,
            ["West Bengal"]    = 200,
            ["Andhra Pradesh"] = 200,
            ["Telangana"]      = 200,
            ["Madhya Pradesh"] = 202,
            ["Odisha"]         = 200,
        };

        /// <summary>
        /// Returns monthly Professional Tax for the given state and monthly gross salary.
        /// Tamil Nadu and Gujarat use slab-based PT; all others use flat monthly rates.
        /// </summary>
        public  static double GetPtPublic(string state, double monthlyGross)
            => GetProfessionalTax(state, monthlyGross);
        private static double GetProfessionalTax(string state, double monthlyGross)
        {
            if (string.IsNullOrEmpty(state)) return 0;

            // Tamil Nadu — annual PT billed half-yearly; slabs based on annual gross
            // Annual gross:  ≤21,000 → nil | ≤30,000 → ₹270 | ≤45,000 → ₹630
            //                ≤60,000 → ₹1,380 | ≤75,000 → ₹2,050 | >75,000 → ₹2,500
            if (state.Equals("Tamil Nadu", StringComparison.OrdinalIgnoreCase))
            {
                double annualGross = monthlyGross * 12;
                double annualPt =
                    annualGross <= 21000 ? 0 :
                    annualGross <= 30000 ? 270 :
                    annualGross <= 45000 ? 630 :
                    annualGross <= 60000 ? 1380 :
                    annualGross <= 75000 ? 2050 :
                    2500;
                return Math.Round(annualPt / 12, 0);
            }

            // Gujarat — monthly slab on monthly gross
            // <6,000 → nil | 6,000–8,999 → ₹80 | 9,000–11,999 → ₹150 | ≥12,000 → ₹200
            if (state.Equals("Gujarat", StringComparison.OrdinalIgnoreCase))
            {
                return monthlyGross < 6000  ? 0 :
                       monthlyGross < 9000  ? 80 :
                       monthlyGross < 12000 ? 150 : 200;
            }

            return PtFlat.TryGetValue(state, out var flat) ? flat : 0;
        }

        // ══════════════════════════════════════════════════════════════════════
        // COMPUTE MONTHLY PAYROLL
        // ══════════════════════════════════════════════════════════════════════
        public SalaryComputeResult Compute(Employee emp, int month, int year, string fy,
            int daysWorked = 0, int totalDays = 0, int deductorId = 0)
        {
            var ss   = emp.Salary ?? new SalaryStructure();
            var decl = _repo.GetDeclaration(emp.Id, fy);
            var ytd  = _repo.GetYtdTds(emp.Id, fy, month, year);

            // ── Pro-rata scaling ──────────────────────────────────────────────
            // Indian payroll standard (30-day method): per-day = monthly ÷ 30.
            // daysWorked=30 in ANY month (28/30/31 calendar days) = full salary.
            // daysWorked=15 = 50% salary. daysWorked=0 = zero salary.
            // Factor capped at 1.0 — never overpay for months with 31 days.
            const int StdDays    = 30;
            double proRataFactor = 1.0;
            bool   isProRata     = daysWorked >= 0 && totalDays > 0 && daysWorked < StdDays;
            if (isProRata)
                proRataFactor = (double)daysWorked / StdDays;

            // Work on a scaled copy — never mutate the master salary structure
            var scaled = new SalaryStructure
            {
                EmployeeId       = ss.EmployeeId,
                Basic            = Math.Round(ss.Basic            * proRataFactor),
                Hra              = Math.Round(ss.Hra              * proRataFactor),
                Da               = Math.Round(ss.Da               * proRataFactor),
                SpecialAllowance = Math.Round(ss.SpecialAllowance * proRataFactor),
                MedicalAllowance = Math.Round(ss.MedicalAllowance * proRataFactor),
                Lta              = Math.Round(ss.Lta              * proRataFactor),
                OtherAllowance   = 0,                              // mirror of Components
                PfApplicable     = ss.PfApplicable,
                PfFixedAmount    = ss.PfFixedAmount > 0 ? Math.Round(ss.PfFixedAmount * proRataFactor) : 0,
                EsiApplicable    = ss.EsiApplicable,
                PtState          = ss.PtState,
                EffectiveFrom    = ss.EffectiveFrom,
                AnnualBonus      = ss.AnnualBonus,                 // annual figures — not pro-rated
                AnnualIncentive  = ss.AnnualIncentive,
                Components       = ss.Components.Select(c => new SalaryComponent {
                    Category = c.Category, Name = c.Name, RuleRef = c.RuleRef, Ordinal = c.Ordinal,
                    Received = Math.Round(c.Received * proRataFactor),
                    Paid     = Math.Round(c.Paid     * proRataFactor),
                    Taxable  = Math.Round(c.Taxable  * proRataFactor),
                }).ToList(),
            };
            ss = scaled;   // use scaled for all calculations below

            // Months remaining in FY (including current)
            int fyStart     = fy.StartsWith("20") ? int.Parse(fy[..4]) : DateTime.Today.Year;
            int fyEndMonth  = 3;   // March
            int fyEndYear   = fyStart + 1;
            int remainMonths = MonthsRemaining(month, year, fyEndMonth, fyEndYear);

            // ── Annual projection ─────────────────────────────────────────────
            // Variable pay (bonus + incentive) — annual lump-sum, added to projection so TDS spreads
            double annualVariable = ss.AnnualBonus + ss.AnnualIncentive;
            // Reimbursement shortfall — amounts eligible but not claimed with bills become taxable
            double reimbShortfall = new TDSPro.DAL.ReimbursementRepository()
                .GetForFy(emp.Id, fy)
                .Sum(c => c.Shortfall);
            // Custom components — Received already in ss.GrossSalary; Paid (bills) is exempt
            double annualComponentsPaid = ss.ComponentsPaid() * 12;
            double annualGross   = ss.GrossSalary * 12 + annualVariable + reimbShortfall;
            double annualBasic   = ss.Basic        * 12;
            double annualHra     = ss.Hra          * 12;
            double annualRent    = decl.RentPaid   * 12;

            // PF (employee: 12% of basic, capped at ₹1800/month if ESI applicable)
            // PF: use employee's fixed amount if set, otherwise auto 12% of Basic
            double pfMonthly = 0;
            if (ss.PfFixedAmount > 0 || ss.PfApplicable)
                pfMonthly = ss.PfFixedAmount > 0
                    ? ss.PfFixedAmount                  // fixed (e.g. capped at ₹1,800)
                    : Math.Round(ss.Basic * 0.12);      // auto 12% of basic
            double annualPf  = pfMonthly * 12;

            // ESI (employee: 0.75% of gross, applicable if gross ≤ ₹21,000/month)
            double esiMonthly = ss.EsiApplicable && ss.GrossSalary <= 21000
                ? Math.Round(ss.GrossSalary * 0.0075)
                : 0;

            // Professional tax
            double ptMonthly = GetProfessionalTax(ss.PtState, ss.GrossSalary);
            double annualPt  = ptMonthly * 12;

            // ── Load FY-specific rules ────────────────────────────────────────
            // Age-based slab selection (Section 192 — use correct old-regime slabs)
            DateTime? dob = DateTime.TryParseExact(emp.DateOfBirth, "dd-MMM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d) ? d :
                (DateTime.TryParse(emp.DateOfBirth, out var d2) ? d2 : (DateTime?)null);
            var ageCategory = TDSPro.Common.TaxRules.GetAgeCategory(dob, fy);

            var oldRules    = TDSPro.Common.TaxRules.GetRules(fy, false, ageCategory);
            var newRules    = TDSPro.Common.TaxRules.GetRules(fy, true);
            double stdDedOld = oldRules.StandardDeduction;   // ₹50,000 — old regime, fixed for all FYs
            double stdDedNew = newRules.StandardDeduction;   // ₹75,000 from FY 2024-25 (new regime only)

            // ── OLD REGIME ────────────────────────────────────────────────────
            // Prefer employee-level HRA city type; fall back to declaration value
            var hraCityType = !string.IsNullOrEmpty(emp.HraCityType) ? emp.HraCityType : decl.HraCityType;
            // RentPaid in DB is annual ("Annual Rent Paid"); CalcHraExemption takes monthly → divide by 12
            double hraExemption = CalcHraExemption(ss.Basic, ss.Hra, decl.RentPaid / 12, hraCityType);
            double hraExAnnual  = hraExemption * 12;

            // 80D self-limit: ₹50K for senior citizen employee, ₹25K for general
            // 80D parent-limit: ₹50K if parent is senior citizen, ₹25K otherwise
            double limit80DSelf    = (ageCategory != TDSPro.Common.AgeCategory.Below60) ? 50000 : 25000;
            double limit80DParents = decl.IsParentSeniorCitizen ? 50000 : 25000;
            if (dob.HasValue)
            {
                int fyEnd = int.Parse(fy.Split('-')[0]) + 1;
                int ageAtFYEnd = fyEnd - dob.Value.Year
                    - (new DateTime(fyEnd,3,31) < dob.Value.AddYears(fyEnd - dob.Value.Year) ? 1 : 0);
                if (ageAtFYEnd >= 60) limit80DSelf = 50000;
            }

            // 80TTA (non-senior ₹10K) and 80TTB (senior ₹50K) are mutually exclusive
            bool isSeniorPs = ageCategory != TDSPro.Common.AgeCategory.Below60;
            double tta_ttbPs = isSeniorPs
                             ? Math.Min(decl.Sec80TTB, 50000)
                             : Math.Min(decl.Sec80TTA, 10000);
            double chap6a = Math.Min(decl.Sec80C + annualPf, 150000)         // PF counts in 80C, cap ₹1.5L
                          + Math.Min(decl.Sec80D_Self,    limit80DSelf)        // 80D self
                          + Math.Min(decl.Sec80D_Parents, limit80DParents)     // 80D parents
                          + decl.Sec80G                                         // 80G donations
                          + Math.Min(decl.Sec80CCD_Employee, 50000)            // 80CCD(1B) ₹50K
                          + decl.Sec80E                                         // education loan — no limit
                          + Math.Min(decl.Sec80EEA, 150000)                    // housing loan first home ₹1.5L
                          + tta_ttbPs                                           // 80TTA or 80TTB (mutually exclusive)
                          + Math.Min(decl.Sec80DD, 125000)                     // differently abled dependent ₹1.25L max
                          + Math.Min(decl.Sec80U,  125000)                     // self differently abled ₹1.25L max
                          + decl.OtherDeductions;                               // any other declared

            // 80CCD(2) — employer NPS: FY-aware rate (10% up to FY2023-24, 14% from FY2024-25 new regime)
            double nps80CCD2Rate = TDSPro.Common.TaxRules.Get80CCD2Rate(fy, false); // old regime rate
            double nps80CCD2RateNew = TDSPro.Common.TaxRules.Get80CCD2Rate(fy, true); // new regime rate
            double npsEmployer  = Math.Min(decl.Sec80CCD_Employer, annualBasic * nps80CCD2Rate);

            double taxableOld = annualGross
                              - hraExAnnual
                              - stdDedOld
                              - annualPt
                              - chap6a                             // includes PF inside 80C — do NOT subtract annualPf separately
                              - npsEmployer
                              - decl.LtaExemption                  // LTA u/s 10(5)
                              - annualComponentsPaid               // bills-substantiated portion of components
                              + decl.IncomeOtherSources;
            taxableOld = Math.Max(0, Math.Round(taxableOld));

            double rawTaxOld               = TDSPro.Common.TaxRules.ComputeSlabTax(taxableOld, oldRules);
            var   (taxAfterOld, r87AO)      = TDSPro.Common.TaxRules.Apply87A(rawTaxOld, taxableOld, oldRules);
            double surchargeOld             = TDSPro.Common.TaxRules.CalcSurcharge(taxAfterOld, taxableOld, oldRules);
            double cessOld                  = Math.Round((taxAfterOld + surchargeOld) * 0.04);
            double totalTaxOld              = taxAfterOld + surchargeOld + cessOld;
            double taxOld = taxAfterOld;

            // ── NEW REGIME — use FY-aware NPS rate (14% from FY 2024-25) ─────
            double npsEmployerNew = Math.Min(decl.Sec80CCD_Employer, annualBasic * nps80CCD2RateNew);
            double taxableNew = annualGross
                              - stdDedNew
                              - npsEmployerNew
                              - decl.LtaExemption                  // LTA u/s 10(5) exempt in both regimes
                              - annualComponentsPaid               // bills reimbursements exempt both regimes u/s 10(14)(i)
                              + decl.IncomeOtherSources;
            taxableNew = Math.Max(0, Math.Round(taxableNew));

            double rawTaxNew               = TDSPro.Common.TaxRules.ComputeSlabTax(taxableNew, newRules);
            var   (taxAfterNew, r87AN)      = TDSPro.Common.TaxRules.Apply87A(rawTaxNew, taxableNew, newRules);
            double surchargeNew             = TDSPro.Common.TaxRules.CalcSurcharge(taxAfterNew, taxableNew, newRules);
            double cessNew                  = Math.Round((taxAfterNew + surchargeNew) * 0.04);
            double totalTaxNew              = taxAfterNew + surchargeNew + cessNew;
            double taxNew = taxAfterNew;

            // ── Choose regime ─────────────────────────────────────────────────
            bool useOld;
            if (string.Equals(emp.TaxRegime, "Old", StringComparison.OrdinalIgnoreCase))
                useOld = true;
            else if (string.Equals(emp.TaxRegime, "New", StringComparison.OrdinalIgnoreCase))
                useOld = false;
            else
                useOld = totalTaxOld <= totalTaxNew;   // auto-optimal: pick lower tax
            double stdDed        = useOld ? stdDedOld : stdDedNew;
            double chosenTax     = useOld ? totalTaxOld : totalTaxNew;
            double taxableChosen = useOld ? taxableOld  : taxableNew;

            // Monthly TDS = (remaining annual tax - ytd) / remaining months
            double remainingTax = Math.Max(0, chosenTax - ytd);
            double monthlyTds   = remainMonths > 0
                ? Math.Round(remainingTax / remainMonths)
                : 0;

            var run = new PayrollRun
            {
                EmployeeId        = emp.Id,
                DeductorId        = deductorId,
                EmployeeName      = emp.Name,
                EmployeeCode      = emp.EmployeeCode,
                Pan               = emp.Pan,
                Month             = month,
                Year              = year,
                FinancialYear     = fy,
                Basic             = ss.Basic,
                Hra               = ss.Hra,
                Da                = ss.Da,
                Special           = ss.SpecialAllowance,
                Medical           = ss.MedicalAllowance,
                Lta               = ss.Lta,
                Other             = ss.ComponentsReceived(),   // mirrors Other Allowance UI field
                GrossSalary       = ss.GrossSalary,
                PfEmployee        = pfMonthly,
                EsiEmployee       = esiMonthly,
                ProfessionalTax   = ptMonthly,
                TdsDeducted       = monthlyTds,
                TaxRegimeUsed     = emp.TaxRegime,
                HraExemption      = hraExemption,
                StandardDeduction = stdDed,
                Chapter6ADeduction= chap6a,
                TaxableIncome     = taxableChosen,
                AnnualTax         = useOld ? taxOld     : taxNew,
                Surcharge         = useOld ? surchargeOld : surchargeNew,
                Cess              = useOld ? cessOld    : cessNew,
                TotalAnnualTax    = chosenTax,
                YtdTds            = ytd,
                Status            = "Draft",
                ProRataDays       = isProRata ? daysWorked  : 0,
                ProRataTotal      = isProRata ? totalDays   : 0,
            };

            return new SalaryComputeResult
            {
                Run               = run,
                TaxOldRegime      = totalTaxOld,
                TaxNewRegime      = totalTaxNew,
                TaxableOld        = taxableOld,
                TaxableNew        = taxableNew,
                RecommendedRegime = totalTaxNew <= totalTaxOld ? "New" : "Old",
            };
        }

        /// <summary>Returns PayrollRun-shaped rows for a given month, sourced from monthly_salary_entries (single source of truth after Pass 2).</summary>
        public List<PayrollRun> GetRuns(int month, int year, int? deductorId = null)
        {
            var salaryRepo = new SalaryRepository();
            var employees  = _repo.GetAllEmployees(deductorId).Where(e => e.IsActive).ToList();
            // Derive FY from year+month: month >= 4 → FY starts that year; else previous year
            int fyStart = month >= 4 ? year : year - 1;
            string fy = $"{fyStart}-{(fyStart + 1) % 100:D2}";
            var list = new List<PayrollRun>();
            foreach (var emp in employees)
            {
                var e = salaryRepo.Get(emp.Id, fy, month);
                if (e == null || e.Status == "Skip") continue;
                list.Add(AdaptEntryToRun(e, emp, deductorId));
            }
            return list;
        }

        /// <summary>Returns all PayrollRun-shaped rows for an FY, sourced from monthly_salary_entries.</summary>
        public List<PayrollRun> GetRunsForFY(string fy, int? deductorId = null)
        {
            var salaryRepo = new SalaryRepository();
            var employees  = _repo.GetAllEmployees(deductorId).Where(e => e.IsActive).ToList();
            var list = new List<PayrollRun>();
            foreach (var emp in employees)
                foreach (var e in salaryRepo.GetAllForFY(emp.Id, fy).Where(x => x.Status != "Skip"))
                    list.Add(AdaptEntryToRun(e, emp, deductorId));
            return list;
        }

        /// <summary>
        /// Returns a year summary: one row per employee, one column per month (Apr–Mar).
        /// Each cell is the PayrollRun for that employee+month, or null if not yet run.
        /// </summary>
        public List<EmployeeYearSummary> GetYearSummary(string fy, int? deductorId = null)
        {
            // Source of truth = monthly_salary_entries (after Pass 2). Legacy payroll_runs no longer populated.
            var salaryRepo = new SalaryRepository();
            var employees = _repo.GetAllEmployees(deductorId).Where(e => e.IsActive).ToList();
            var result = new List<EmployeeYearSummary>();
            foreach (var emp in employees)
            {
                var entries = salaryRepo.GetAllForFY(emp.Id, fy);
                if (entries.Count == 0) continue;
                var summary = new EmployeeYearSummary
                {
                    EmployeeId   = emp.Id,
                    EmployeeName = emp.Name,
                    EmployeeCode = emp.EmployeeCode,
                    Pan          = emp.Pan,
                    MonthlyRuns  = entries
                        .Where(e => e.Status != "Skip")           // exclude LOP-skip months from yearly totals
                        .GroupBy(e => e.Month)
                        .ToDictionary(g => g.Key, g => AdaptEntryToRun(g.OrderByDescending(x => x.Id).First(), emp, deductorId)),
                };
                result.Add(summary);
            }
            return result.OrderBy(s => s.EmployeeName).ToList();
        }

        /// <summary>Project a saved MonthlySalaryEntry into a PayrollRun shape so EmployeeYearSummary keeps working.</summary>
        private static PayrollRun AdaptEntryToRun(MonthlySalaryEntry e, Employee emp, int? deductorId)
        {
            return new PayrollRun
            {
                Id              = e.Id,
                EmployeeId      = e.EmployeeId,
                DeductorId      = deductorId ?? e.DeductorId,
                Month           = e.Month,
                Year            = e.Year,
                FinancialYear   = e.FinancialYear,
                Basic           = e.Basic,
                Hra             = e.HRA,
                Da              = e.DaAmount,
                Special         = e.SpecialAllowance,
                Medical         = e.MedicalAllowance,
                Lta             = e.Lta,
                Other           = e.OtherAllowances,
                GrossSalary     = e.GrossPayment > 0 ? e.GrossPayment : e.Basic + e.HRA + e.DaAmount + e.SpecialAllowance + e.MedicalAllowance + e.Lta + e.OtherAllowances + e.Bonus + e.Commission + e.AdvanceSalary + e.Arrears + e.NpsEmployer + e.PerqTaxable + e.LeaveEncTaxable,
                PfEmployee      = e.PfEmployee,
                EsiEmployee     = e.EsiEmployee,
                ProfessionalTax = e.ProfessionalTax,
                TdsDeducted     = e.TdsDeducted,
                Status          = e.IsLocked ? "Processed" : (string.IsNullOrEmpty(e.Status) ? "Draft" : e.Status),
                TaxRegimeUsed   = emp.TaxRegime,
                EmployeeName    = emp.Name,
                EmployeeCode    = emp.EmployeeCode,
                Pan             = emp.Pan,
            };
        }

        public List<Employee> GetEmployees(int? deductorId = null)
            => _repo.GetAllEmployees(deductorId);

        public (bool ok, string msg) ClearAllEmployees(int deductorId)
            => _repo.ClearAllEmployees(deductorId);

        public (bool ok, string msg) SaveEmployee(Employee e)
        {
            // Validate first; refuse to persist invalid data.
            var v = PayrollValidators.ValidateEmployee(e);
            if (!v.Ok) return (false, v.ErrorSummary);
            var (ok, msg) = _repo.SaveEmployee(e);
            // Surface warnings (e.g. malformed IFSC) to caller even when save succeeds
            if (ok && v.Warnings.Count > 0)
                return (true, $"{msg} (warnings: {v.WarningSummary})");
            return (ok, msg);
        }

        /// <summary>Validate an employee without saving — used by UI to pre-flight before opening dialog actions.</summary>
        public ValidationResult ValidateEmployeeOnly(Employee e)
            => PayrollValidators.ValidateEmployee(e);

        public (bool ok, string msg) DeleteEmployee(int id)
            => _repo.DeleteEmployee(id);

        public TaxDeclaration GetDeclaration(int empId, string fy)
            => _repo.GetDeclaration(empId, fy);

        public void SaveDeclaration(TaxDeclaration d)
            => _repo.SaveDeclaration(d);

        public List<LandlordRecord> GetLandlords(int empId, string fy)
            => _repo.GetLandlords(empId, fy);

        public void SaveLandlord(LandlordRecord lr)
            => _repo.SaveLandlord(lr);

        public void DeleteLandlord(int id)
            => _repo.DeleteLandlord(id);

        /// <summary>HRA exemption = min(actual HRA, 50%/40% basic, rent - 10% basic). Monthly.</summary>
        public  static double CalcHraExemptionPublic(double basic, double hra, double rent, string cityType)
            => CalcHraExemption(basic, hra, rent, cityType);
        private static double CalcHraExemption(double basic, double hra, double rent, string cityType)
        {
            if (rent <= 0 || hra <= 0) return 0;
            double pct    = cityType == "Metro" ? 0.50 : 0.40;
            double limit1 = hra;
            double limit2 = basic * pct;
            double limit3 = Math.Max(0, rent - basic * 0.10);
            return Math.Round(Math.Min(limit1, Math.Min(limit2, limit3)));
        }

        private static int MonthsRemaining(int month, int year, int endMonth, int endYear)
        {
            var start = new DateTime(year, month, 1);
            var end   = new DateTime(endYear, endMonth, 1);
            int diff  = (end.Year - start.Year) * 12 + (end.Month - start.Month) + 1;
            return Math.Max(1, diff);
        }

    }
}
