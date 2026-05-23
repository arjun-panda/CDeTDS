using TDSPro.BLL;

namespace TDSPro.Tests;

/// <summary>SalaryOptimizer: split must sum to CTC exactly; LTA + reimbursements only in Old regime.</summary>
public class SalaryOptimizerTests
{
    private static SalaryOptimizerInput Std(double ctc, string regime = "Old",
        string pfMode = "Capped", bool gratuity = true, bool nps = false)
        => new()
        {
            MonthlyCtc          = ctc,
            Regime              = regime,
            HraCityType         = "Metro",
            MonthlyRentDeclared = 0,
            PfMode              = pfMode,
            IncludeGratuity     = gratuity,
            EmployerOffersNps   = nps,
            FinancialYear       = "2026-27",
            DateOfBirth         = "1990-01-01",
        };

    private static double SumSplit(SalaryOptimizerResult r, SalaryOptimizerInput input)
    {
        double basic = r.MonthlyBasic;
        double empPf = input.PfMode switch
        {
            "None"   => 0,
            "Full"   => Math.Round(basic * 0.12),
            _        => basic > 15000 ? 1800 : Math.Round(basic * 0.12)
        };
        double grat  = input.IncludeGratuity ? Math.Round(basic * 0.0481) : 0;
        return r.MonthlyBasic + r.MonthlyHra + r.MonthlySpecial + r.MonthlyLta
             + r.MonthlyTelephone + r.MonthlyMeal + empPf + grat;
    }

    // ── CTC reconciliation: split MUST equal input CTC for every config ─────
    [Theory]
    [InlineData( 30000, "Old", "Capped",  true)]
    [InlineData( 50000, "Old", "Capped",  true)]
    [InlineData( 55000, "Old", "Full",    true)]
    [InlineData( 80000, "Old", "Capped",  false)]
    [InlineData(100000, "Old", "None",    true)]
    [InlineData(150000, "Old", "Capped",  true)]
    [InlineData( 50000, "New", "Capped",  true)]
    [InlineData(100000, "New", "Capped",  true)]
    public void Optimize_SplitSumsToCtc(double ctc, string regime, string pfMode, bool gratuity)
    {
        var input = Std(ctc, regime, pfMode, gratuity);
        var result = SalaryOptimizer.Optimize(input);
        var sum = SumSplit(result, input);
        // Allow ±₹2 for rounding (Basic/HRA/PF/Gratuity all round independently)
        Assert.InRange(sum, ctc - 2, ctc + 2);
    }

    // ── Basic % must be one of the candidate values ─────────────────────────
    [Fact]
    public void Optimize_BasicPctIsCandidateValue()
    {
        var result = SalaryOptimizer.Optimize(Std(50000));
        Assert.Contains(result.BasicPct, new[] { 0.40, 0.45, 0.50 });
    }

    // ── HRA = 50% of Basic for Metro, 40% for Non-Metro ─────────────────────
    [Fact]
    public void Optimize_Hra_Metro_Is50PctBasic()
    {
        var r = SalaryOptimizer.Optimize(Std(50000));   // Metro by default
        Assert.Equal(Math.Round(r.MonthlyBasic * 0.50), r.MonthlyHra);
    }

    [Fact]
    public void Optimize_Hra_NonMetro_Is40PctBasic()
    {
        var input = Std(50000);
        input.HraCityType = "Non-Metro";
        var r = SalaryOptimizer.Optimize(input);
        Assert.Equal(Math.Round(r.MonthlyBasic * 0.40), r.MonthlyHra);
    }

    // ── LTA only in Old regime ──────────────────────────────────────────────
    [Fact]
    public void Optimize_LtaZero_InNewRegime()
    {
        var r = SalaryOptimizer.Optimize(Std(50000, regime: "New"));
        Assert.Equal(0, r.MonthlyLta);
    }

    [Fact]
    public void Optimize_LtaPositive_InOldRegime()
    {
        var r = SalaryOptimizer.Optimize(Std(50000, regime: "Old"));
        Assert.True(r.MonthlyLta > 0);
        // capped at min(₹2500, 5% Basic)
        Assert.True(r.MonthlyLta <= 2500);
        Assert.True(r.MonthlyLta <= Math.Round(r.MonthlyBasic * 0.05) + 1);
    }

    // ── Telephone + Meal reimbursements only in Old regime ──────────────────
    [Fact]
    public void Optimize_Reimbursements_OnlyInOldRegime()
    {
        var newReg = SalaryOptimizer.Optimize(Std(50000, regime: "New"));
        Assert.Equal(0, newReg.MonthlyTelephone);
        Assert.Equal(0, newReg.MonthlyMeal);

        var oldReg = SalaryOptimizer.Optimize(Std(50000, regime: "Old"));
        Assert.True(oldReg.MonthlyTelephone > 0);
        Assert.True(oldReg.MonthlyMeal > 0);
    }

    // ── Gratuity toggle: when off, Special inflates to fill ─────────────────
    [Fact]
    public void Optimize_GratuityOff_SpecialIsHigher()
    {
        var withGrat    = SalaryOptimizer.Optimize(Std(50000, gratuity: true));
        var withoutGrat = SalaryOptimizer.Optimize(Std(50000, gratuity: false));
        // Without gratuity, the freed ~₹1000 should appear in Special
        Assert.True(withoutGrat.MonthlySpecial > withGrat.MonthlySpecial);
        // But CTC still reconciles for both
    }

    // ── PF None vs Capped: both reconcile to CTC ────────────────────────────
    // (We don't assert which one's Special is higher — optimizer may pick
    // different Basic% in each case. Just verify CTC reconciles in both.)
    [Theory]
    [InlineData("None")]
    [InlineData("Capped")]
    [InlineData("Full")]
    public void Optimize_AllPfModes_Reconcile(string pfMode)
    {
        var input = Std(50000, pfMode: pfMode);
        var result = SalaryOptimizer.Optimize(input);
        Assert.InRange(SumSplit(result, input), 49998, 50002);
    }

    // ── Savings vs naive baseline ───────────────────────────────────────────
    [Fact]
    public void Optimize_AnnualSavings_NonNegative()
    {
        // Optimizer should never yield WORSE tax than naive 50% Basic baseline
        var r = SalaryOptimizer.Optimize(Std(100000));
        Assert.True(r.AnnualSavings >= 0);
    }
}
