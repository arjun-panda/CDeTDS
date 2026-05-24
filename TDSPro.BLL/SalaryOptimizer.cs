using TDSPro.Common;
using TDSPro.DAL.Models;

namespace TDSPro.BLL
{
    public class SalaryOptimizerInput
    {
        public double MonthlyCtc          { get; set; }
        public string Regime              { get; set; } = "New";       // "Old" / "New"
        public string HraCityType         { get; set; } = "Non-Metro"; // "Metro" / "Non-Metro"
        public double MonthlyRentDeclared { get; set; } = 0;           // 0 = conservative (no HRA exemption)
        public bool   EmployerOffersNps   { get; set; } = false;       // 80CCD(2) up to 10% Basic
        public string PfMode              { get; set; } = "Capped";    // "Full" / "Capped" (₹1,800) / "None"
        public bool   IncludeGratuity     { get; set; } = true;        // 4.81% Basic provision
        public string FinancialYear       { get; set; } = "2026-27";
        public string DateOfBirth         { get; set; } = "";          // for age-based slab
        public double FixedBasicPct       { get; set; } = 0;           // 0 = auto-optimize; >0 = fixed (e.g. 0.40)
    }

    public class SalaryOptimizerResult
    {
        public double MonthlyBasic       { get; set; }
        public double MonthlyHra         { get; set; }
        public double MonthlySpecial     { get; set; }
        public double MonthlyLta         { get; set; } // Sec 10(5) — Old regime only
        public double MonthlyTelephone   { get; set; }
        public double MonthlyMeal        { get; set; }
        public double MonthlyNpsEmployer { get; set; } // employer-funded, not deducted from CTC pay
        public double BasicPct           { get; set; } // 0.40 / 0.45 / 0.50
        public double AnnualTaxChosen   { get; set; }
        public double AnnualTaxNaive    { get; set; }  // 50/50 baseline for comparison
        public double AnnualSavings     { get; set; }
        public string Notes             { get; set; } = "";
    }

    /// <summary>
    /// Closed-form CTC → salary-structure optimizer.
    /// Tries Basic at 40%/45%/50% of CTC and picks the split with the lowest annual tax.
    /// Uses the same tax math as PayrollService.Compute so projections match payroll.
    /// </summary>
    public static class SalaryOptimizer
    {
        public static SalaryOptimizerResult Optimize(SalaryOptimizerInput input)
        {
            // If user fixed a Basic %, use only that; otherwise try 40/45/50 and pick lowest tax
            var candidates = input.FixedBasicPct > 0
                ? new[] { input.FixedBasicPct }
                : new[] { 0.40, 0.45, 0.50 };
            SalaryOptimizerResult? best = null;
            double naiveTax = ComputeAnnualTax(input, basicPct: 0.50, includeNpsLever: false).Item1;

            foreach (var pct in candidates)
            {
                var c = ComputeAnnualTax(input, pct, input.EmployerOffersNps);
                if (best == null || c.tax < best.AnnualTaxChosen)
                {
                    best = new SalaryOptimizerResult
                    {
                        MonthlyBasic       = c.basic,
                        MonthlyHra         = c.hra,
                        MonthlySpecial     = c.special,
                        MonthlyLta         = c.lta,
                        MonthlyTelephone   = c.telephone,
                        MonthlyMeal        = c.meal,
                        MonthlyNpsEmployer = c.npsEmp,
                        BasicPct           = pct,
                        AnnualTaxChosen    = c.tax,
                    };
                }
            }

            best!.AnnualTaxNaive = naiveTax;
            best.AnnualSavings   = Math.Max(0, Math.Round(naiveTax - best.AnnualTaxChosen));

            var notes = new List<string>();
            notes.Add(input.FixedBasicPct > 0
                ? $"Basic fixed at {input.FixedBasicPct * 100:0}% of CTC as specified."
                : $"Basic set to {best.BasicPct * 100:0}% of CTC — optimal for this profile.");
            if (input.MonthlyRentDeclared <= 0 && input.Regime == "Old")
                notes.Add("HRA exemption not counted (no rent declared). Declare rent later to reduce tax further.");
            if (input.EmployerOffersNps)
                notes.Add($"Employer NPS adds ~₹{best.MonthlyNpsEmployer:N0}/month over CTC (80CCD(2) — fully exempt).");
            if (input.Regime == "New")
                notes.Add("New regime: only standard deduction + employer NPS matter; Basic split has minimal tax impact.");
            best.Notes = string.Join(" ", notes);

            return best;
        }

        // ── Core: build a candidate structure and compute annual tax exactly as PayrollService does ──
        private static (double tax, double basic, double hra, double special, double lta, double telephone, double meal, double npsEmp)
            ComputeAnnualTax(SalaryOptimizerInput input, double basicPct, bool includeNpsLever)
        {
            double ctc       = input.MonthlyCtc;
            double basic     = Math.Round(ctc * basicPct);
            double hraPct    = input.HraCityType == "Metro" ? 0.50 : 0.40;
            double hra       = Math.Round(basic * hraPct);
            // LTA → up to ₹2,500/mo or 5% of Basic (Old regime only — Sec 10(5) exemption)
            double lta       = input.Regime == "Old" ? Math.Min(2500, Math.Round(basic * 0.05)) : 0;
            // Employer PF — depends on PF mode
            double employerPf = input.PfMode switch
            {
                "None"   => 0,
                "Full"   => Math.Round(basic * 0.12),
                _        => basic > 15000 ? 1800 : Math.Round(basic * 0.12)   // "Capped" default
            };
            // Gratuity provision (4.81% Basic) — optional, sits inside CTC
            double gratuity = input.IncludeGratuity ? Math.Round(basic * 0.0481) : 0;
            // Employer NPS = 10% of Basic if offered (sits over CTC; not deducted from gross)
            double npsEmp     = includeNpsLever ? Math.Round(basic * 0.10) : 0;
            // Tax-exempt reimbursement defaults (Old regime only) — safe limits widely accepted
            // Telephone ₹1,500/mo (₹18K/yr — common cap), Meal ₹1,100/mo (₹50 × 22 working days)
            double telephone = input.Regime == "Old" ? Math.Min(1500, ctc * 0.05) : 0;
            double meal      = input.Regime == "Old" ? Math.Min(1100, ctc * 0.03) : 0;
            // Special = balance after Basic + HRA + LTA + employer PF + gratuity + reimbursements (so CTC adds up exactly)
            double special    = Math.Max(0, ctc - basic - hra - lta - employerPf - gratuity - telephone - meal);

            double employeePf = employerPf;

            double annualBasic   = basic   * 12;
            double annualHra     = hra     * 12;
            double annualSpecial = special * 12;
            double annualLta     = lta     * 12;
            double annualGross   = annualBasic + annualHra + annualSpecial + annualLta;
            double annualPf      = employeePf * 12;
            double annualNpsEmp  = npsEmp * 12;
            double annualRent    = input.MonthlyRentDeclared * 12;

            DateTime? dob = DateTime.TryParse(input.DateOfBirth, out var d) ? d : (DateTime?)null;
            var ageCat   = TaxRules.GetAgeCategory(dob, input.FinancialYear);
            var oldRules = TaxRules.GetRules(input.FinancialYear, false, ageCat);
            var newRules = TaxRules.GetRules(input.FinancialYear, true);

            double hraExAnnual = PayrollService.CalcHraExemptionPublic(basic, hra, input.MonthlyRentDeclared, input.HraCityType) * 12;
            double chap6a = Math.Min(annualPf, 150000); // PF only — conservative; user's actual 80C declarations are separate
            double npsRateOld = TaxRules.Get80CCD2Rate(input.FinancialYear, false);
            double npsRateNew = TaxRules.Get80CCD2Rate(input.FinancialYear, true);
            double npsExOld = Math.Min(annualNpsEmp, annualBasic * npsRateOld);
            double npsExNew = Math.Min(annualNpsEmp, annualBasic * npsRateNew);

            // Old regime
            double taxableOld = Math.Max(0, annualGross - hraExAnnual - oldRules.StandardDeduction - chap6a - npsExOld);
            double rawOld = TaxRules.ComputeSlabTax(taxableOld, oldRules);
            var (afterOld, _) = TaxRules.Apply87A(rawOld, taxableOld, oldRules);
            double surOld = TaxRules.CalcSurcharge(afterOld, taxableOld, oldRules);
            double totalOld = Math.Round((afterOld + surOld) * 1.04);

            // New regime
            double taxableNew = Math.Max(0, annualGross - newRules.StandardDeduction - npsExNew);
            double rawNew = TaxRules.ComputeSlabTax(taxableNew, newRules);
            var (afterNew, _) = TaxRules.Apply87A(rawNew, taxableNew, newRules);
            double surNew = TaxRules.CalcSurcharge(afterNew, taxableNew, newRules);
            double totalNew = Math.Round((afterNew + surNew) * 1.04);

            double chosen = input.Regime == "Old" ? totalOld : totalNew;
            return (chosen, basic, hra, special, lta, telephone, meal, npsEmp);
        }
    }
}
