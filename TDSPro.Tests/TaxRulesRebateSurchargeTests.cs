using TDSPro.Common;

namespace TDSPro.Tests;

/// <summary>87A rebate, surcharge, and full-tax ComputeFull tests.</summary>
public class TaxRulesRebateSurchargeTests
{
    // ── 87A — Old regime: flat ₹12,500 rebate for taxable ≤ ₹5L ─────────────
    [Theory]
    [InlineData(400000,    7500,  7500, 0)]     // tax 7500 < 12500 → fully rebated
    [InlineData(500000,   12500, 12500, 0)]     // boundary: tax = rebate
    [InlineData(500001,   12500,     0, 12500)] // ₹1 over → no rebate at all (no marginal relief)
    [InlineData(700000,   52500,     0, 52500)] // well above threshold → 0 rebate
    public void Apply87A_OldRegime_FlatRebateNoMarginalRelief(
        double taxable, double rawTax, double expectedRebate, double expectedAfter)
    {
        var rules = TaxRules.GetRules("2025-26", isNew: false, AgeCategory.Below60);
        var (after, rebate) = TaxRules.Apply87A(rawTax, taxable, rules);
        Assert.Equal(expectedRebate, rebate);
        Assert.Equal(expectedAfter, after);
    }

    // ── 87A — New regime FY 2025-26: ₹60K rebate for taxable ≤ ₹12L + marginal relief ─
    [Theory]
    [InlineData(1100000,   40000,  40000, 0)]      // tax fully rebated
    [InlineData(1200000,   60000,  60000, 0)]      // boundary
    [InlineData(1250000,   72500,  22500, 50000)]  // marginal relief: capped at (income-12L)=50000
    [InlineData(1600000,  120000,      0, 120000)] // well above → no rebate
    public void Apply87A_NewRegime_2025_26_WithMarginalRelief(
        double taxable, double rawTax, double expectedRebate, double expectedAfter)
    {
        var rules = TaxRules.GetRules("2025-26", isNew: true);
        var (after, rebate) = TaxRules.Apply87A(rawTax, taxable, rules);
        Assert.Equal(expectedRebate, rebate);
        Assert.Equal(expectedAfter, after);
    }

    // ── 87A — New regime FY 2024-25: ₹25K rebate for taxable ≤ ₹7L + marginal relief ─
    [Theory]
    [InlineData(700000,    25000, 25000, 0)]
    [InlineData(710000,    35500, 25500, 10000)] // 35500 > 10000 cap → capped at 10000
    public void Apply87A_NewRegime_2024_25_MarginalRelief(
        double taxable, double rawTax, double expectedRebate, double expectedAfter)
    {
        var rules = TaxRules.GetRules("2024-25", isNew: true);
        var (after, rebate) = TaxRules.Apply87A(rawTax, taxable, rules);
        Assert.Equal(expectedRebate, rebate);
        Assert.Equal(expectedAfter, after);
    }

    // ── Surcharge — old regime: 10/15/25/37% based on income thresholds ─────
    [Theory]
    [InlineData( 4_000_000, 100000,      0)]   // below ₹50L → 0
    [InlineData( 5_000_000, 100000,      0)]   // exactly ₹50L → 0
    [InlineData( 7_000_000, 100000,  10000)]   // ₹50L-1Cr → 10%
    [InlineData(15_000_000, 100000,  15000)]   // ₹1Cr-2Cr → 15%
    [InlineData(30_000_000, 100000,  25000)]   // ₹2Cr-5Cr → 25%
    [InlineData(60_000_000, 100000,  37000)]   // >₹5Cr old regime → 37%
    public void Surcharge_OldRegime_Slabs(double income, double tax, double expectedSur)
    {
        var rules = TaxRules.GetRules("2025-26", isNew: false, AgeCategory.Below60);
        Assert.Equal(expectedSur, TaxRules.CalcSurcharge(tax, income, rules));
    }

    // ── Surcharge — new regime: capped at 25% (no 37% slab) ─────────────────
    [Theory]
    [InlineData(60_000_000, 100000, 25000)]   // >₹5Cr new regime stays at 25%
    [InlineData(30_000_000, 100000, 25000)]   // >₹2Cr-5Cr: 25%
    public void Surcharge_NewRegime_CappedAt25Percent(double income, double tax, double expectedSur)
    {
        var rules = TaxRules.GetRules("2025-26", isNew: true);
        Assert.Equal(expectedSur, TaxRules.CalcSurcharge(tax, income, rules));
    }

    // ── ComputeFull — end-to-end test, verifies tax+cess composition ────────
    // Salaried ₹10L taxable, new regime FY 2025-26: tax = ₹40K → 87A rebates to ₹0 → cess 0
    [Fact]
    public void ComputeFull_NewRegime_10L_Salaried_FullyRebated()
    {
        var r = TaxRules.ComputeFull(1000000, "2025-26", isNew: true);
        Assert.Equal(40000, r.rawTax);    // ₹4L×0% + ₹4L×5% + ₹2L×10% = ₹40K
        Assert.Equal(40000, r.rebate);    // ≤ ₹12L threshold, full rebate
        Assert.Equal(0,     r.surcharge);
        Assert.Equal(0,     r.cess);
        Assert.Equal(0,     r.total);
    }

    // ₹15L taxable, new FY 2025-26 → tax 105000, no rebate, 4% cess
    [Fact]
    public void ComputeFull_NewRegime_15L_Salaried()
    {
        var r = TaxRules.ComputeFull(1500000, "2025-26", isNew: true);
        // Slabs: 0×4L + 5%×4L (20K) + 10%×4L (40K) + 15%×3L (45K) = 105K
        Assert.Equal(105000, r.rawTax);
        Assert.Equal(0,      r.rebate);
        Assert.Equal(0,      r.surcharge);
        Assert.Equal(Math.Round(105000 * 0.04), r.cess);          // 4200
        Assert.Equal(Math.Round(105000 + r.cess), r.total);       // 109200
    }

    // ── Age category ─────────────────────────────────────────────────────────
    [Theory]
    [InlineData("1990-01-01", "2025-26", AgeCategory.Below60)]
    [InlineData("1960-04-01", "2025-26", AgeCategory.Senior60to79)] // age 65 in FY end
    [InlineData("1940-04-01", "2025-26", AgeCategory.SuperSenior80Plus)] // 85+
    public void GetAgeCategory_PartitionsCorrectly(string dobStr, string fy, AgeCategory expected)
    {
        var dob = DateTime.Parse(dobStr);
        Assert.Equal(expected, TaxRules.GetAgeCategory(dob, fy));
    }

    [Fact]
    public void GetAgeCategory_NullDob_DefaultsBelow60()
    {
        Assert.Equal(AgeCategory.Below60, TaxRules.GetAgeCategory(null, "2025-26"));
    }
}
