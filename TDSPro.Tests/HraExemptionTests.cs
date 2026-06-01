using CDeTDS.BLL;

namespace CDeTDS.Tests;

/// <summary>HRA exemption u/s 10(13A) — min(actual HRA, rent − 10% basic, 50%/40% basic).</summary>
public class HraExemptionTests
{
    // ── Standard worked examples ─────────────────────────────────────────────
    // Basic 30000, HRA 15000, Rent 12000, Metro
    // limits: 15000 (HRA) | 9000 (rent − 3000) | 15000 (50% Basic)
    // exemption = min = 9000
    [Theory]
    [InlineData(30000, 15000, 12000, "Metro",     9000)]
    [InlineData(30000, 15000, 12000, "Non-Metro", 9000)]   // 50% Basic = 15K, 40% = 12K — min is still 9000 (rent-10%basic)
    [InlineData(30000, 15000, 20000, "Metro",    15000)]   // capped by HRA (limit 1)
    [InlineData(30000, 15000, 20000, "Non-Metro",12000)]   // capped by 40% Basic (limit 2)
    [InlineData(30000, 18000, 25000, "Metro",    15000)]   // capped by 50% Basic
    [InlineData(50000, 25000, 30000, "Metro",    25000)]   // exact equality: HRA=50% Basic=25000
    public void HraExemption_Worked(double basic, double hra, double rent, string city, double expected)
    {
        Assert.Equal(expected, PayrollService.CalcHraExemptionPublic(basic, hra, rent, city));
    }

    // ── Zero-rent or zero-HRA → no exemption ─────────────────────────────────
    [Theory]
    [InlineData(30000, 15000,     0, "Metro")]
    [InlineData(30000,     0, 12000, "Metro")]
    [InlineData(    0, 15000, 12000, "Metro")]
    public void HraExemption_NoRentOrNoHra_Zero(double basic, double hra, double rent, string city)
    {
        Assert.Equal(0, PayrollService.CalcHraExemptionPublic(basic, hra, rent, city));
    }

    // ── Rent ≤ 10% Basic → exemption clamps to 0 ─────────────────────────────
    [Fact]
    public void HraExemption_RentBelow10PctBasic_Zero()
    {
        // Basic 50000 → 10% = 5000. Rent 4000 → rent − 10%basic = -1000 → clamped to 0
        var result = PayrollService.CalcHraExemptionPublic(50000, 25000, 4000, "Metro");
        Assert.Equal(0, result);
    }

    // ── Metro vs Non-Metro switch matters (50% vs 40%) ──────────────────────
    [Fact]
    public void HraExemption_MetroVsNonMetro_Differs()
    {
        // Basic 40000 (so 50% = 20K, 40% = 16K)
        // HRA 25000, Rent 100000 (rent − 10%basic = 96000, big number)
        // Metro → min(25000, 20000, 96000) = 20000
        // Non-Metro → min(25000, 16000, 96000) = 16000
        var metro    = PayrollService.CalcHraExemptionPublic(40000, 25000, 100000, "Metro");
        var nonMetro = PayrollService.CalcHraExemptionPublic(40000, 25000, 100000, "Non-Metro");
        Assert.Equal(20000, metro);
        Assert.Equal(16000, nonMetro);
        Assert.True(metro > nonMetro);
    }
}
