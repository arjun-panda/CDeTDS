using TDSPro.DAL;
using TDSPro.DAL.Models;

namespace TDSPro.BLL
{
    /// <summary>
    /// FY rollover — seeds the new FY for all active salary employees under a deductor:
    ///   1. Copies tax_declarations from current FY → new FY (employee edits after)
    ///   2. Seeds April (month 4) monthly_salary_entry as Draft from salary_structure
    /// salary_structures are FY-agnostic — nothing to copy there.
    /// Non-salary deductees have no structure to roll over.
    /// </summary>
    public class RolloverService
    {
        public class RolloverResult
        {
            public int EmpTotal       { get; set; }
            public int DeclCopied     { get; set; }
            public int DeclSkipped    { get; set; }   // already existed
            public int AprilSeeded    { get; set; }
            public int AprilSkipped   { get; set; }   // already existed
            public List<string> Errors { get; set; } = new();
            public bool HasErrors => Errors.Count > 0;
        }

        public RolloverResult Execute(int deductorId, string fromFy, string toFy)
        {
            var result  = new RolloverResult();
            var payRepo = new PayrollRepository();
            var salRepo = new SalaryRepository();

            var employees = payRepo.GetAllEmployees(deductorId);
            result.EmpTotal = employees.Count;

            int toFyStartYear = int.Parse(toFy[..4]);   // e.g. 2026 from "2026-27"
            int aprilYear     = toFyStartYear;           // April is always in the start year

            foreach (var emp in employees)
            {
                try
                {
                    // ── 1. Copy tax declaration ──────────────────────────────
                    var existingDecl = payRepo.GetDeclaration(emp.Id, toFy);
                    if (existingDecl.Id > 0)
                    {
                        result.DeclSkipped++;
                    }
                    else
                    {
                        var srcDecl = payRepo.GetDeclaration(emp.Id, fromFy);
                        // Copy into new FY — zero out investment values (employee re-declares)
                        // but carry forward HRA city type and rent as a starting point
                        var newDecl = new TaxDeclaration
                        {
                            EmployeeId          = emp.Id,
                            FinancialYear       = toFy,
                            RentPaid            = srcDecl.RentPaid,
                            HraCityType         = srcDecl.HraCityType,
                            LandlordPan         = srcDecl.LandlordPan,
                            IsParentSeniorCitizen = srcDecl.IsParentSeniorCitizen,
                            // Investment declarations reset to zero — employee re-submits Form 12BB
                            Sec80C              = 0,
                            Sec80D_Self         = 0,
                            Sec80D_Parents      = 0,
                            Sec80G              = 0,
                            Sec80CCD_Employee   = 0,
                            Sec80CCD_Employer   = 0,
                            OtherDeductions     = 0,
                            IncomeOtherSources  = 0,
                            Sec80E              = 0,
                            Sec80EEA            = 0,
                            Sec80TTA            = 0,
                            Sec80TTB            = 0,
                            Sec80DD             = 0,
                            Sec80U              = 0,
                            LtaExemption        = 0,
                        };
                        payRepo.SaveDeclaration(newDecl);
                        result.DeclCopied++;
                    }

                    // ── 2. Seed April entry from salary structure ────────────
                    var existing = salRepo.Get(emp.Id, toFy, 4);
                    if (existing != null)
                    {
                        result.AprilSkipped++;
                        continue;
                    }

                    var ss = emp.Salary;
                    if (ss == null)
                    {
                        result.Errors.Add($"{emp.Name}: no salary structure found — April not seeded.");
                        continue;
                    }

                    double pf = 0;
                    if (ss.PfApplicable)
                        pf = ss.PfFixedAmount > 0
                            ? ss.PfFixedAmount
                            : Math.Min(ss.Basic * 0.12, 1800);

                    double esi = ss.EsiApplicable
                        ? Math.Round((ss.Basic + ss.Hra + ss.SpecialAllowance) * 0.0075, 2)
                        : 0;

                    var april = new MonthlySalaryEntry
                    {
                        EmployeeId       = emp.Id,
                        DeductorId       = deductorId,
                        FinancialYear    = toFy,
                        Month            = 4,
                        Year             = aprilYear,
                        Basic            = ss.Basic,
                        HRA              = ss.Hra,
                        DaPercent        = ss.Da,
                        DaAmount         = Math.Round(ss.Basic * ss.Da / 100, 2),
                        SpecialAllowance = ss.SpecialAllowance,
                        MedicalAllowance = ss.MedicalAllowance,
                        Lta              = ss.Lta,
                        OtherAllowances  = ss.OtherAllowance,
                        NpsEmployer      = ss.EmployerNps,
                        PfEmployee       = pf,
                        EsiEmployee      = esi,
                        Status           = "Draft",
                        IsLocked         = false,
                        WorkingDays      = 30,
                    };
                    april.RecalcGross();
                    salRepo.Save(april);
                    result.AprilSeeded++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{emp.Name}: {ex.Message}");
                }
            }

            return result;
        }
    }
}
