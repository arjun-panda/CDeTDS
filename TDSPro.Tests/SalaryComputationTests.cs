using TDSPro.BLL;
using TDSPro.DAL.Models;
using TDSPro.Common;

namespace TDSPro.Tests;

/// <summary>
/// End-to-end salary tax computation tests.
///
/// Each test constructs MonthlySalaryEntry objects directly (no DB),
/// calls SalaryService.ComputeAnnual, and asserts exact tax figures.
///
/// All expected values are hand-computed from first principles and annotated
/// inline so any future regression is immediately diagnosable.
///
/// FY used throughout: 2025-26 (unless noted).
/// Old regime slabs (below-60): 0-2.5L=0% | 2.5-5L=5% | 5-10L=20% | 10L+=30%
/// New regime slabs (2025-26):  0-4L=0%   | 4-8L=5%   | 8-12L=10% | 12-16L=15% | 16-20L=20% | 20-24L=25% | 24L+=30%
/// Old std ded: 50,000 | New std ded: 75,000
/// Old 87A: taxable &lt;= 5L -&gt; max 12,500 rebate
/// New 87A: taxable &lt;= 12L -&gt; max 60,000 rebate (+ marginal relief)
/// </summary>
public class SalaryComputationTests
{
    const string FY = "2025-26";

    // ── Helpers ──────────────────────────────────────────────────────────────

    static Employee Emp(string regime = "New") => new()
    {
        Id = 1, Name = "Test", Pan = "ABCDE1234F",
        TaxRegime = regime,
        DateOfBirth = "01-Jan-1985",   // age ~40, Below60
    };

    static MonthlySalaryEntry BasicEntry(int month, double basic, double hra = 0) => new()
    {
        EmployeeId = 1, Month = month, Year = month >= 4 ? 2025 : 2026,
        FinancialYear = FY,
        Basic = basic, HRA = hra,
    };

    static MonthlySalaryEntry EntryWithOtherLine(int month, double basic, string name, double taxable, double exempt) =>
        new()
        {
            EmployeeId = 1, Month = month, Year = month >= 4 ? 2025 : 2026,
            FinancialYear = FY,
            Basic = basic,
            // OtherAllowances includes both taxable + exempt portions (mirrors BuildSdEntry)
            OtherAllowances = taxable + exempt,
            PerqExempted = exempt,
            LineItems = new() { new SalaryLineItem { Category = "other", Name = name, Taxable = taxable, Exempt = exempt } },
        };

    static MonthlySalaryEntry EntryWithPerqLine(int month, double basic, string name, double taxable, double exempt) =>
        new()
        {
            EmployeeId = 1, Month = month, Year = month >= 4 ? 2025 : 2026,
            FinancialYear = FY,
            Basic = basic,
            // PerqTotal = taxable + exempt (full perq amount); PerqExempted = exempt portion only
            PerqTotal = taxable + exempt,
            PerqExempted = exempt,
            LineItems = new() { new SalaryLineItem { Category = "perq", Name = name, Taxable = taxable, Exempt = exempt } },
        };

    static SalaryService Svc() => new();

    static TaxDeclaration NoDecl(int empId = 1) => new() { EmployeeId = empId, FinancialYear = FY };

    static TaxDeclaration DeclWith80C(double sec80C) => new()
    {
        EmployeeId = 1, FinancialYear = FY, Sec80C = sec80C,
    };

    static TaxDeclaration DeclWithLta(double lta) => new()
    {
        EmployeeId = 1, FinancialYear = FY, LtaExemption = lta,
    };

    static List<MonthlySalaryEntry> FullYear(double basic, double hra = 0)
    {
        var months = new[] { 4,5,6,7,8,9,10,11,12,1,2,3 };
        return months.Select(m => BasicEntry(m, basic, hra)).ToList();
    }

    // ── 1. BASIC SALARY ONLY — OLD REGIME ────────────────────────────────────

    [Fact]
    public void OldRegime_BasicOnly_FullYear_50K()
    {
        // Gross = 50,000 x 12 = 6,00,000
        // Taxable old = 6,00,000 - 50,000 (std) = 5,50,000
        // Tax = 12,500 + 20% x 50,000 = 22,500; cess 900; total 23,400
        var emp = Emp("Old");
        var entries = FullYear(50000);
        var r = Svc().ComputeAnnual(entries, emp, FY, NoDecl(), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(600000, r.OldRegime.GrossSalary);
        Assert.Equal(550000, r.OldRegime.TotalIncome);
        Assert.Equal(22500,  r.OldRegime.TaxOnIncome);
        Assert.Equal(0,      r.OldRegime.Rebate87A);
        Assert.Equal(900,    r.OldRegime.Cess);
        Assert.Equal(23400,  r.OldRegime.TotalTax);
    }

    // ── 2. 87A REBATE — OLD REGIME ───────────────────────────────────────────

    [Fact]
    public void OldRegime_87A_Rebate_TaxableAt5L()
    {
        // basic = 45,833 -> gross = 5,49,996; taxable = 4,99,996 -> rebate wipes
        var emp = Emp("Old");
        var entries = FullYear(45833);
        var r = Svc().ComputeAnnual(entries, emp, FY, NoDecl(), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(0, r.OldRegime.TotalTax, precision: 0);
        Assert.True(r.OldRegime.Rebate87A > 0);
    }

    [Fact]
    public void OldRegime_NoRebate_TaxableAbove5L()
    {
        // basic = 50,001 -> gross 6,00,012; taxable 5,50,012 -> no rebate
        var emp = Emp("Old");
        var entries = FullYear(50001);
        var r = Svc().ComputeAnnual(entries, emp, FY, NoDecl(), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(0, r.OldRegime.Rebate87A);
        Assert.True(r.OldRegime.TotalTax > 0);
    }

    // ── 3. CHAPTER VI-A — 80C ────────────────────────────────────────────────

    [Fact]
    public void OldRegime_80C_Reduces_Taxable()
    {
        // Gross 6,00,000; before 80C taxable = 5,50,000; 80C 1.5L -> 4,00,000 -> rebate
        var emp = Emp("Old");
        var entries = FullYear(50000);
        var r = Svc().ComputeAnnual(entries, emp, FY, DeclWith80C(150000), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(400000, r.OldRegime.TotalIncome);
        Assert.Equal(0, r.OldRegime.TotalTax);
    }

    [Fact]
    public void OldRegime_80C_Capped_At_1_5L()
    {
        // basic 70K -> gross 8,40,000; std 50K -> pre-80C 7,90,000
        // declare 2L but cap 1.5L -> taxable 6,40,000
        // Tax = 12,500 + 20% x 1,40,000 = 40,500; cess 1,620; total 42,120
        var emp = Emp("Old");
        var entries = FullYear(70000);
        var r = Svc().ComputeAnnual(entries, emp, FY, DeclWith80C(200000), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(640000, r.OldRegime.TotalIncome);
        Assert.Equal(40500,  r.OldRegime.TaxOnIncome);
        Assert.Equal(42120,  r.OldRegime.TotalTax);
    }

    // ── 4. BILLS REIMBURSEMENT — "other" category — BOTH REGIMES EXEMPT ──────

    [Fact]
    public void BothRegimes_ConveyanceExempt_FullYear()
    {
        // Basic 45K + Conveyance 10K/month (cat="other", taxable=0, exempt=10K)
        // Annual gross = 55K x 12 = 6,60,000 (both regimes)
        // annualSec10 = 10K x 12 = 1,20,000
        // Old taxable = 6,60,000 - 1,20,000 - 50,000 = 4,90,000 -> rebate wipes
        // New taxable = 6,60,000 - 1,20,000 - 75,000 = 4,65,000 -> rebate wipes
        var emp = Emp("Old");
        var months = new[] { 4,5,6,7,8,9,10,11,12,1,2,3 };
        var entries = months.Select(m => EntryWithOtherLine(m, 45000, "Conveyance", 0, 10000)).ToList();
        var r = Svc().ComputeAnnual(entries, emp, FY, NoDecl(), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(660000, r.OldRegime.GrossSalary);
        Assert.Equal(660000, r.NewRegime.GrossSalary);
        Assert.Equal(490000, r.OldRegime.TotalIncome);
        Assert.Equal(465000, r.NewRegime.TotalIncome);
        Assert.Equal(0,      r.OldRegime.TotalTax);
        Assert.Equal(0,      r.NewRegime.TotalTax);
        var convOld = r.OldRegime.Sec10Items.FirstOrDefault(x => x.Name == "Conveyance");
        Assert.NotNull(convOld);
        Assert.Equal(120000, convOld!.OldRegime);
        Assert.Equal(120000, convOld!.NewRegime);
    }

    [Fact]
    public void BothRegimes_ConveyanceExempt_Sec10DisplayMatchesTaxableDeduction()
    {
        // Invariant: Gross - sec10 - stdDed == TotalIncome (both regimes)
        var months = new[] { 4,5,6,7,8,9,10,11,12,1,2,3 };
        var entries = months.Select(m => EntryWithOtherLine(m, 50000, "Conveyance", 0, 5000)).ToList();
        var emp = Emp("Old");
        var r = Svc().ComputeAnnual(entries, emp, FY, NoDecl(), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        double sec10Old = r.OldRegime.Sec10Items.Sum(x => x.OldRegime);
        double sec10New = r.NewRegime.Sec10Items.Sum(x => x.NewRegime);

        Assert.Equal(r.OldRegime.TotalIncome,
            r.OldRegime.GrossSalary - sec10Old - r.OldRegime.StandardDeduction, precision: 0);
        Assert.Equal(r.NewRegime.TotalIncome,
            r.NewRegime.GrossSalary - sec10New - r.NewRegime.StandardDeduction, precision: 0);
    }

    // ── 5. LTA — Sec 10(5), old regime only, from declaration ────────────────

    [Fact]
    public void OldRegime_LTA_ReducesTaxable_NewRegime_DoesNot()
    {
        // Basic 60K -> gross 7,20,000; LTA decl 30K
        // Old = 7,20,000 - 30,000 - 50,000 = 6,40,000; Tax 40,500 + cess 1,620 = 42,120
        // New = 7,20,000 - 75,000 = 6,45,000 -> rebate wipes
        var emp = Emp("Old");
        var r = Svc().ComputeAnnual(FullYear(60000), emp, FY, DeclWithLta(30000), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(640000, r.OldRegime.TotalIncome);
        Assert.Equal(42120,  r.OldRegime.TotalTax);
        Assert.Equal(645000, r.NewRegime.TotalIncome);
        Assert.Equal(0,      r.NewRegime.TotalTax);
    }

    [Fact]
    public void LTA_NotDoubleDeducted_When_AlsoInPerqExempted()
    {
        // Gross 6,00,000; LTA 60K -> taxable = 4,90,000 -> rebate wipes
        var emp = Emp("Old");
        var r = Svc().ComputeAnnual(FullYear(50000), emp, FY, DeclWithLta(60000), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(490000, r.OldRegime.TotalIncome);
        Assert.Equal(0,      r.OldRegime.TotalTax);
    }

    // ── 6. PERQ CATEGORY — taxable new regime, exempt old regime ─────────────

    [Fact]
    public void PerqCategory_TaxableNewRegime_ExemptOldRegime()
    {
        // Basic 60K + Company car (total=5K, taxable=3K, exempt=2K)/month
        // Annual gross = 65K x 12 = 7,80,000
        // perq exempt 2K x 12 = 24K; annualOtherExempts = 0 (no "other" lines)
        // Old taxable = 7,80,000 - 24,000 - 50,000 = 7,06,000
        // New taxable = 7,80,000 - 0 - 75,000 = 7,05,000
        var emp = Emp("Old");
        var months = new[] { 4,5,6,7,8,9,10,11,12,1,2,3 };
        var entries = months.Select(m => EntryWithPerqLine(m, 60000, "Company Car", 3000, 2000)).ToList();
        var r = Svc().ComputeAnnual(entries, emp, FY, NoDecl(), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(780000, r.OldRegime.GrossSalary);
        Assert.Equal(706000, r.OldRegime.TotalIncome);
        Assert.Equal(705000, r.NewRegime.TotalIncome);
        var carSec10 = r.OldRegime.Sec10Items.FirstOrDefault(x => x.Name == "Company Car");
        Assert.NotNull(carSec10);
        Assert.Equal(24000, carSec10!.OldRegime);
        Assert.Equal(0,     carSec10!.NewRegime);
    }

    // ── 7. AJAY SHARMA — mixed months ─────────────────────────────────────────

    [Fact]
    public void AjaySharma_MixedMonths_Conveyance_OldRegime()
    {
        // Apr/May/Jun: Basic 55K + 10K conv -> GrossPayment 65K
        // Jul-Mar:     Basic 50K + 10K conv -> GrossPayment 60K
        // Annual gross = 65K x 3 + 60K x 9 = 7,35,000
        // sec10 = 10K x 12 = 1,20,000
        // Old taxable = 7,35,000 - 1,20,000 - 50,000 = 5,65,000
        //   Tax = 12,500 + 20% x 65,000 = 25,500; cess 1,020; total 26,520
        // New taxable = 7,35,000 - 1,20,000 - 75,000 = 5,40,000 -> rebate wipes
        var emp = Emp("Old");
        var hiMonths = new[] { 4, 5, 6 };
        var loMonths = new[] { 7, 8, 9, 10, 11, 12, 1, 2, 3 };
        var entries = hiMonths.Select(m => EntryWithOtherLine(m, 55000, "Conveyance", 0, 10000))
            .Concat(loMonths.Select(m => EntryWithOtherLine(m, 50000, "Conveyance", 0, 10000)))
            .ToList();
        var r = Svc().ComputeAnnual(entries, emp, FY, NoDecl(), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(735000, r.OldRegime.GrossSalary);
        Assert.Equal(565000, r.OldRegime.TotalIncome);
        Assert.Equal(25500,  r.OldRegime.TaxOnIncome);
        Assert.Equal(26520,  r.OldRegime.TotalTax);
        Assert.Equal(540000, r.NewRegime.TotalIncome);
        Assert.Equal(0,      r.NewRegime.TotalTax);
    }

    [Fact]
    public void AjaySharma_SelectJuly_3ActualMonths_Conveyance_ProjectsCorrectly()
    {
        // 3 actual months (Apr/May/Jun at 55K+10K conv), computing at July
        // July entry: Basic 50K, no conveyance entered yet
        // Fallback to June.PerqExempted=10K; projectedMonthsForSec10=9
        // annualSec10 = 30K actual + 10K x 9 projected = 1,20,000
        // annualGross = 65K x 3 + 50K + 50K x 8 = 1,95,000 + 50K + 4,00,000 = 6,45,000
        // Old taxable = 6,45,000 - 1,20,000 - 50,000 = 4,75,000 -> rebate wipes
        var emp = Emp("Old");
        var actualEntries = new[] { 4, 5, 6 }
            .Select(m => EntryWithOtherLine(m, 55000, "Conveyance", 0, 10000)).ToList();
        actualEntries.Add(BasicEntry(7, 50000)); // July: no conveyance
        var r = Svc().ComputeAnnual(actualEntries, emp, FY, NoDecl(), 7, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(645000, r.OldRegime.GrossSalary);
        Assert.Equal(475000, r.OldRegime.TotalIncome);
        Assert.Equal(0,      r.OldRegime.TotalTax);
        var conv = r.OldRegime.Sec10Items.FirstOrDefault(x => x.Name == "Conveyance");
        Assert.NotNull(conv);
        Assert.Equal(120000, conv!.OldRegime);
    }

    // ── 8. NEW REGIME — std ded 75K ──────────────────────────────────────────

    [Fact]
    public void NewRegime_StandardDeduction_75K()
    {
        // Basic 1,20,000 -> gross 14,40,000; taxable = 14,40,000 - 75,000 = 13,65,000
        // Tax new: 0+20K+40K+15%x1.65L = 84,750; cess 3,390; total 88,140
        // Key assertion: new std ded = 75K, old std ded = 50K
        var emp = Emp("New");
        var r = Svc().ComputeAnnual(FullYear(120000), emp, FY, NoDecl(), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(1440000, r.NewRegime.GrossSalary);
        Assert.Equal(1365000, r.NewRegime.TotalIncome);
        Assert.Equal(84750,   r.NewRegime.TaxOnIncome);
        Assert.Equal(88140,   r.NewRegime.TotalTax);
        Assert.Equal(50000,   r.OldRegime.StandardDeduction);
        Assert.Equal(75000,   r.NewRegime.StandardDeduction);
    }

    // ── 9. NEW REGIME 87A ────────────────────────────────────────────────────

    [Fact]
    public void NewRegime_87A_At_12L_Wipes_Tax()
    {
        // gross = 12,75,000; taxable = 12,00,000 -> rebate wipes
        var emp = Emp("New");
        var r = Svc().ComputeAnnual(FullYear(106250), emp, FY, NoDecl(), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(1200000, r.NewRegime.TotalIncome, precision: 0);
        Assert.Equal(0, r.NewRegime.TotalTax, precision: 0);
        Assert.True(r.NewRegime.Rebate87A > 0);
    }

    [Fact]
    public void NewRegime_87A_Above_12L_Tax_Applies()
    {
        // Basic 112,500 -> gross 13,50,000; taxable = 12,75,000 -> no rebate
        var emp = Emp("New");
        var r = Svc().ComputeAnnual(FullYear(112500), emp, FY, NoDecl(), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(1275000, r.NewRegime.TotalIncome);
        Assert.Equal(0,       r.NewRegime.Rebate87A);
        Assert.True(r.NewRegime.TotalTax > 0);
    }

    // ── 10. Invariant: Gross - Sec10 - StdDed = TotalIncome ──────────────────

    [Theory]
    [InlineData(40000, 5000)]
    [InlineData(60000, 10000)]
    [InlineData(80000, 0)]
    public void GrossSalary_MinusSec10_MinusStdDed_EqualsTotalIncome(double basic, double conv)
    {
        var months = new[] { 4,5,6,7,8,9,10,11,12,1,2,3 };
        var entries = months.Select(m => conv > 0
            ? EntryWithOtherLine(m, basic, "Conveyance", 0, conv)
            : BasicEntry(m, basic)).ToList();
        var emp = Emp("Old");
        var r = Svc().ComputeAnnual(entries, emp, FY, NoDecl(), 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        double sec10Old = r.OldRegime.Sec10Items.Sum(x => x.OldRegime);
        double sec10New = r.NewRegime.Sec10Items.Sum(x => x.NewRegime);

        Assert.Equal(r.OldRegime.TotalIncome,
            r.OldRegime.GrossSalary - sec10Old - r.OldRegime.StandardDeduction, precision: 0);
        Assert.Equal(r.NewRegime.TotalIncome,
            r.NewRegime.GrossSalary - sec10New - r.NewRegime.StandardDeduction, precision: 0);
    }

    // ── 11. HRA exemption — old regime only ──────────────────────────────────

    [Fact]
    public void OldRegime_HRA_Exemption_ReducesTaxable()
    {
        // Basic 30K/m, HRA 12K/m, Rent 15K/m (=1,80,000 annual stored in DB)
        // HRA exempt = min(12K, 15K-10%*30K=12K, 40%*30K=12K) = 12K/m x 12 = 1,44,000
        // Gross = 42K x 12 = 5,04,000
        // Old taxable = 5,04,000 - 1,44,000 - 50,000 = 3,10,000 -> rebate wipes
        // New taxable = 5,04,000 - 0 - 75,000 = 4,29,000 -> rebate wipes
        var emp = Emp("Old");
        emp.HraCityType = "non-metro";
        var months = new[] { 4,5,6,7,8,9,10,11,12,1,2,3 };
        var entries = months.Select(m => BasicEntry(m, 30000, 12000)).ToList();
        // RentPaid is annual in DB (UI label "Annual Rent Paid"); ComputeAnnual divides by 12 internally
        var decl = new TaxDeclaration { EmployeeId = 1, FinancialYear = FY, RentPaid = 180000, HraCityType = "non-metro" };
        var r = Svc().ComputeAnnual(entries, emp, FY, decl, 3, reimbShortfallOverride: 0, ytdTdsOverride: 0);

        Assert.Equal(310000, r.OldRegime.TotalIncome);
        Assert.Equal(0,      r.OldRegime.TotalTax);
        Assert.Equal(429000, r.NewRegime.TotalIncome);
        Assert.Equal(0,      r.NewRegime.TotalTax);
    }
}
