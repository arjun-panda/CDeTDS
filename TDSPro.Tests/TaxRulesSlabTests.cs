using TDSPro.Common;

namespace TDSPro.Tests;

/// <summary>Pure slab tax calculations across regimes / FYs / age categories.</summary>
public class TaxRulesSlabTests
{
    // ── OLD REGIME (Below 60) — slabs unchanged since FY 2017-18 ─────────────
    // 0-2.5L: 0% | 2.5L-5L: 5% | 5L-10L: 20% | >10L: 30%
    [Theory]
    [InlineData(200000,       0)]    // below ₹2.5L nil slab
    [InlineData(250000,       0)]    // exactly at nil boundary
    [InlineData(500000,    12500)]   // 250000 × 5%
    [InlineData(700000,    52500)]   // 12500 + 200000 × 20%
    [InlineData(1000000,  112500)]   // 12500 + 500000 × 20%
    [InlineData(1500000,  262500)]   // 112500 + 500000 × 30%
    [InlineData(2000000,  412500)]   // 112500 + 1000000 × 30%
    public void OldRegime_Below60_Slabs(double taxable, double expectedTax)
    {
        var rules = TaxRules.GetRules("2025-26", isNew: false, AgeCategory.Below60);
        Assert.Equal(expectedTax, TaxRules.ComputeSlabTax(taxable, rules));
    }

    // ── OLD REGIME (Senior 60-79) — ₹3L nil, then same as Below60 ────────────
    [Theory]
    [InlineData(300000,       0)]
    [InlineData(500000,    10000)]   // 200000 × 5%
    [InlineData(1000000,  110000)]   // 10000 + 500000 × 20%
    public void OldRegime_Senior_Slabs(double taxable, double expectedTax)
    {
        var rules = TaxRules.GetRules("2025-26", isNew: false, AgeCategory.Senior60to79);
        Assert.Equal(expectedTax, TaxRules.ComputeSlabTax(taxable, rules));
    }

    // ── OLD REGIME (Super Senior 80+) — ₹5L nil ──────────────────────────────
    [Theory]
    [InlineData(500000,       0)]
    [InlineData(700000,    40000)]   // 200000 × 20%
    [InlineData(1000000,  100000)]   // 500000 × 20%
    public void OldRegime_SuperSenior_Slabs(double taxable, double expectedTax)
    {
        var rules = TaxRules.GetRules("2025-26", isNew: false, AgeCategory.SuperSenior80Plus);
        Assert.Equal(expectedTax, TaxRules.ComputeSlabTax(taxable, rules));
    }

    // ── NEW REGIME FY 2025-26 / 2026-27 — Budget 2025 slabs ──────────────────
    // 0-4L: 0% | 4L-8L: 5% | 8L-12L: 10% | 12L-16L: 15% | 16L-20L: 20% | 20L-24L: 25% | >24L: 30%
    [Theory]
    [InlineData(400000,        0)]   // at nil boundary
    [InlineData(800000,    20000)]   // 400000 × 5%
    [InlineData(1200000,   60000)]   // 20000 + 400000 × 10%
    [InlineData(1600000,  120000)]   // 60000 + 400000 × 15%
    [InlineData(2000000,  200000)]   // 120000 + 400000 × 20%
    [InlineData(2400000,  300000)]   // 200000 + 400000 × 25%
    [InlineData(3000000,  480000)]   // 300000 + 600000 × 30%
    public void NewRegime_2026_27_Slabs(double taxable, double expectedTax)
    {
        var rules = TaxRules.GetRules("2026-27", isNew: true);
        Assert.Equal(expectedTax, TaxRules.ComputeSlabTax(taxable, rules));
    }

    // ── NEW REGIME FY 2024-25 — Budget 2024 slabs ────────────────────────────
    // 0-3L: 0% | 3L-7L: 5% | 7L-10L: 10% | 10L-12L: 15% | 12L-15L: 20% | >15L: 30%
    [Theory]
    [InlineData(700000,    20000)]   // 400000 × 5%
    [InlineData(1000000,   50000)]   // 20000 + 300000 × 10%
    [InlineData(1200000,   80000)]   // 50000 + 200000 × 15%
    [InlineData(1500000,  140000)]   // 80000 + 300000 × 20%
    [InlineData(2000000,  290000)]   // 140000 + 500000 × 30%
    public void NewRegime_2024_25_Slabs(double taxable, double expectedTax)
    {
        var rules = TaxRules.GetRules("2024-25", isNew: true);
        Assert.Equal(expectedTax, TaxRules.ComputeSlabTax(taxable, rules));
    }

    // ── NEW REGIME FY 2023-24 — Budget 2023 slabs ────────────────────────────
    // 0-3L: 0% | 3L-6L: 5% | 6L-9L: 10% | 9L-12L: 15% | 12L-15L: 20% | >15L: 30%
    [Theory]
    [InlineData(600000,    15000)]   // 300000 × 5%
    [InlineData(900000,    45000)]   // 15000 + 300000 × 10%
    [InlineData(1500000,  150000)]   // 45000 + 300000 × 15% + 300000 × 20% = 45000+45000+60000
    public void NewRegime_2023_24_Slabs(double taxable, double expectedTax)
    {
        var rules = TaxRules.GetRules("2023-24", isNew: true);
        Assert.Equal(expectedTax, TaxRules.ComputeSlabTax(taxable, rules));
    }

    // ── Standard deduction round-trip ─────────────────────────────────────────
    [Fact]
    public void StandardDeduction_OldRegime_IsAlways50000()
    {
        foreach (var fy in new[] { "2023-24", "2024-25", "2025-26", "2026-27" })
            Assert.Equal(50000, TaxRules.GetStandardDeduction(fy, isNew: false));
    }

    [Fact]
    public void StandardDeduction_NewRegime_Was50K_Then75K_FromFY2024_25()
    {
        Assert.Equal(50000, TaxRules.GetStandardDeduction("2023-24", isNew: true));
        Assert.Equal(75000, TaxRules.GetStandardDeduction("2024-25", isNew: true));
        Assert.Equal(75000, TaxRules.GetStandardDeduction("2025-26", isNew: true));
        Assert.Equal(75000, TaxRules.GetStandardDeduction("2026-27", isNew: true));
    }

    [Fact]
    public void Nps80CCD2_NewRegime_14PercentFromFY2024_25()
    {
        Assert.Equal(0.10, TaxRules.Get80CCD2Rate("2023-24", isNew: true));
        Assert.Equal(0.14, TaxRules.Get80CCD2Rate("2024-25", isNew: true));
        Assert.Equal(0.14, TaxRules.Get80CCD2Rate("2025-26", isNew: true));
        // Old regime stays 10% throughout
        Assert.Equal(0.10, TaxRules.Get80CCD2Rate("2025-26", isNew: false));
    }
}
