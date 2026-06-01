using CDeTDS.Common;

namespace CDeTDS.Tests;

/// <summary>
/// End-to-end FY 2026-27 verification (IT Act 2025 transitional year).
/// Confirms the Budget 2025 new-regime structure carries forward correctly:
///   ₹4L nil → 5/10/15/20/25/30% slabs | ₹75K std ded | 87A ₹60K @ ≤₹12L | 4% cess
/// Also locks in old-regime continuity and IT Act 2025 form mapping.
/// </summary>
public class TaxRules2026_27Tests
{
    private const string FY = "2026-27";

    // ── New-regime slabs (no rebate/cess) ─────────────────────────────────
    [Theory]
    [InlineData(400000,        0)]   // boundary nil
    [InlineData(500000,     5000)]   // 100000 × 5%
    [InlineData(800000,    20000)]   // 400000 × 5%
    [InlineData(1000000,   40000)]   // 20000 + 200000 × 10%
    [InlineData(1200000,   60000)]   // 20000 + 400000 × 10%
    [InlineData(1600000,  120000)]   // 60000 + 400000 × 15%
    [InlineData(2000000,  200000)]   // 120000 + 400000 × 20%
    [InlineData(2400000,  300000)]   // 200000 + 400000 × 25%
    [InlineData(3000000,  480000)]   // 300000 + 600000 × 30%
    public void NewRegime_Slabs_FY2026_27(double taxable, double expected)
    {
        var rules = TaxRules.GetRules(FY, isNew: true);
        Assert.Equal(expected, TaxRules.ComputeSlabTax(taxable, rules));
    }

    // ── 87A rebate: ₹60K cap, threshold ₹12L, with marginal relief ──────────
    [Theory]
    [InlineData(800000,    20000,  20000,     0)]  // small tax fully rebated
    [InlineData(1200000,   60000,  60000,     0)]  // boundary: exact ₹60K
    [InlineData(1210000,   61000,  51000, 10000)]  // marginal relief caps net to 10000
    [InlineData(1280000,   68000,      0, 68000)]  // 8000 over threshold → no rebate (full tax payable)
    [InlineData(1500000,   97500,      0, 97500)]  // well above → no rebate
    public void Rebate87A_NewRegime_FY2026_27(
        double taxable, double rawTax, double expectedRebate, double expectedAfter)
    {
        var rules = TaxRules.GetRules(FY, isNew: true);
        var (after, rebate) = TaxRules.Apply87A(rawTax, taxable, rules);
        Assert.Equal(expectedRebate, rebate);
        Assert.Equal(expectedAfter, after);
    }

    // ── Standard deduction = ₹75K (new regime) ─────────────────────────────
    [Fact]
    public void NewRegime_StandardDeduction_75K_FY2026_27()
    {
        var rules = TaxRules.GetRules(FY, isNew: true);
        Assert.Equal(75000, rules.StandardDeduction);
    }

    // ── Old regime unchanged for FY 2026-27 (₹2.5L exemption, ₹12,500 @ ≤₹5L) ─
    [Theory]
    [InlineData(250000,       0)]
    [InlineData(500000,   12500)]
    [InlineData(1000000, 112500)]
    public void OldRegime_Below60_Slabs_FY2026_27(double taxable, double expected)
    {
        var rules = TaxRules.GetRules(FY, isNew: false, AgeCategory.Below60);
        Assert.Equal(expected, TaxRules.ComputeSlabTax(taxable, rules));
    }

    [Fact]
    public void OldRegime_StandardDeduction_50K_FY2026_27()
    {
        var rules = TaxRules.GetRules(FY, isNew: false, AgeCategory.Below60);
        Assert.Equal(50000, rules.StandardDeduction);
    }

    // ── Surcharge tiers: ₹50L-1cr=10%, ₹1cr-2cr=15%, ₹2cr-5cr=25%, >₹5cr=37% (old) or 25% (new) ─
    [Theory]
    [InlineData(15000000,  true,  0.15)]   // ₹1.5cr → 15% (1cr–2cr tier)
    [InlineData(15000000,  false, 0.15)]   // ₹1.5cr old → 15%
    [InlineData(30000000,  true,  0.25)]   // ₹3cr → 25% (both regimes)
    [InlineData(55000000,  false, 0.37)]   // ₹5.5cr old → 37%
    [InlineData(55000000,  true,  0.25)]   // ₹5.5cr new → capped at 25%
    public void Surcharge_RegimeTier_FY2026_27(double taxable, bool isNew, double expectedRate)
    {
        var rules = TaxRules.GetRules(FY, isNew);
        double tax = TaxRules.ComputeSlabTax(taxable, rules);
        double surcharge = TaxRules.CalcSurcharge(tax, taxable, rules);
        Assert.InRange(surcharge / tax, expectedRate - 0.01, expectedRate + 0.01);
    }

    // ── 4% Health & Education cess on (tax + surcharge) ────────────────────
    [Fact]
    public void Cess_4Percent_FY2026_27()
    {
        var rules = TaxRules.GetRules(FY, isNew: true);
        double taxable = 1500000;
        // Tax: 0 + 20K (4L–8L@5%) + 40K (8L–12L@10%) + 45K (12L–15L@15%) = 105,000
        double tax  = TaxRules.ComputeSlabTax(taxable, rules);
        double cess = Math.Round(tax * 0.04);                       // 4200
        Assert.Equal(105000, tax);
        Assert.Equal(4200, cess);
    }

    // ── Full end-to-end: ₹15L gross salary, new regime, no Ch VI-A ─────────
    //   Gross 1500000 − Std 75000 = 1425000 taxable
    //   Tax: 0 + 20000 (4L–8L@5%) + 40000 (8L–12L@10%) + 33750 (12L–14.25L@15%) = 93,750
    //   87A: 1425000 > 1200000 → no rebate
    //   Surcharge: <50L → 0
    //   Cess: 4% × 93750 = 3750
    //   Total = ₹97,500
    [Fact]
    public void EndToEnd_15Lakh_NewRegime_FY2026_27()
    {
        var rules = TaxRules.GetRules(FY, isNew: true);
        double gross   = 1500000;
        double taxable = gross - rules.StandardDeduction;
        double slab    = TaxRules.ComputeSlabTax(taxable, rules);
        var (afterReb, rebate) = TaxRules.Apply87A(slab, taxable, rules);
        double sur     = TaxRules.CalcSurcharge(afterReb, taxable, rules);
        double cess    = Math.Round((afterReb + sur) * 0.04);
        double total   = afterReb + sur + cess;

        Assert.Equal(1425000, taxable);
        Assert.Equal(93750, slab);
        Assert.Equal(0, rebate);
        Assert.Equal(93750, afterReb);
        Assert.Equal(0, sur);
        Assert.Equal(3750, cess);
        Assert.Equal(97500, total);
    }

    // ── Full end-to-end: ₹12.75L salary → exactly zero tax for new-regime FY26 ─
    //   Gross 1275000 − Std 75000 = 1200000 taxable → tax 60000 → 87A=60000 → ₹0
    [Fact]
    public void EndToEnd_12_75Lakh_NewRegime_FullRebate_FY2026_27()
    {
        var rules = TaxRules.GetRules(FY, isNew: true);
        double gross   = 1275000;
        double taxable = gross - rules.StandardDeduction;
        double slab    = TaxRules.ComputeSlabTax(taxable, rules);
        var (afterReb, rebate) = TaxRules.Apply87A(slab, taxable, rules);

        Assert.Equal(1200000, taxable);
        Assert.Equal(60000, slab);
        Assert.Equal(60000, rebate);
        Assert.Equal(0, afterReb);
    }

    // ── IT Act 2025 form-code mapping kicks in for FY 2026-27 ──────────────
    [Fact]
    public void ITAct2025_Forms_FY2026_27()
    {
        Assert.True(TaxRules.IsNewAct(FY));
        Assert.Equal("Form 138",     TaxRules.SalaryReturnForm(FY));      // 24Q → 138
        Assert.Equal("Form 140",     TaxRules.NonSalaryReturnForm(FY));   // 26Q → 140
        Assert.Equal("Form 130",     TaxRules.SalaryTdsCertForm(FY));     // Form 16 → 130
        Assert.Equal("Form 131",     TaxRules.NonSalaryTdsCertForm(FY));  // Form 16A → 131
        Assert.Equal("Section 392(1)", TaxRules.SalaryTdsSection(FY));    // 192(1) → 392(1)
    }

    // ── Legacy act stays for FY 2025-26 ────────────────────────────────────
    [Fact]
    public void ITAct1961_Forms_FY2025_26()
    {
        Assert.False(TaxRules.IsNewAct("2025-26"));
        Assert.Equal("Form 24Q",     TaxRules.SalaryReturnForm("2025-26"));
        Assert.Equal("Section 192(1)", TaxRules.SalaryTdsSection("2025-26"));
    }
}
