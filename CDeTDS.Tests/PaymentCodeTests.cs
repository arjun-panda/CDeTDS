using CDeTDS.DAL;

namespace CDeTDS.Tests;

/// <summary>
/// Locks the IT Act 2025 payment-code mappings in BuiltInTdsRules.PaymentCodeFor.
/// Sources: Protean RPU 1.0 "Select Section for PAYMENT" picker (2026-06-18) and a
/// real TY 2026-27 challan receipt (1002 = salary, non-Govt). These mappings are
/// inert until FVU_USE_PAYMENT_CODES is enabled, but a wrong code would mis-report
/// TDS once activated — so the confirmed ones are pinned here.
/// </summary>
public class PaymentCodeTests
{
    [Theory]
    // Salary — confirmed by the real challan receipt (Section 392, Code 1002).
    [InlineData("192",  "Salary",                         "Individual", "1002")]
    // Interest / dividends
    [InlineData("193",  "Interest on Securities",         "Company",    "1019")]
    [InlineData("194",  "Dividends",                      "All",        "1029")]
    // Commission / brokerage
    [InlineData("194D", "Insurance Commission",           "Individual", "1005")]
    [InlineData("194H", "Commission / Brokerage",         "All",        "1006")]
    // Contractor — Individual/HUF vs others
    [InlineData("194C", "Payment to Contractor",          "Individual", "1023")]
    [InlineData("194C", "Payment to Contractor",          "Company",    "1024")]
    // Rent — machinery vs land/building
    [InlineData("194I", "Rent of Plant & Machinery",      "All",        "1008")]
    [InlineData("194I", "Rent of Land or Building",       "All",        "1009")]
    // 194J split by nature (the key correction: professional fees = 1027, not 1025)
    [InlineData("194J", "Fees for Professional Services", "All",        "1027")]
    [InlineData("194J", "Technical Services",             "All",        "1026")]
    [InlineData("194J", "Director Payment",               "All",        "1028")]
    // 194S VDA — Individual/HUF vs others
    [InlineData("194S", "Virtual Digital Assets",         "Individual", "1038")]
    [InlineData("194S", "Virtual Digital Assets",         "Company",    "1037")]
    // Other common confirmed codes
    [InlineData("194O", "E-commerce",                     "All",        "1035")]
    [InlineData("194Q", "Purchase of Goods",              "All",        "1031")]
    [InlineData("194R", "Perquisite",                     "All",        "1033")]
    [InlineData("194T", "Partners Payment",               "All",        "1067")]
    // 194K mutual-fund units = 1013 per the official Form 140 file-format spec
    // (Annexure 2 Sl.4(i)) — NOT the RPU dropdown's "94K".
    [InlineData("194K", "Income from Mutual Fund Units",   "All",        "1013")]
    public void PaymentCodeFor_ReturnsConfirmedCode(string section, string nature, string deducteeType, string expected)
    {
        Assert.Equal(expected, BuiltInTdsRules.PaymentCodeFor(section, nature, deducteeType));
    }

    [Fact]
    public void PaymentCode_194K_IsNot_Legacy94K()
    {
        // Regression guard: an earlier seed used the RPU literal "94K" here, which the
        // official data-structure document corrected to 1013.
        Assert.NotEqual("94K", BuiltInTdsRules.PaymentCodeFor("194K", "Mutual Fund", "All"));
        Assert.Equal("1013",  BuiltInTdsRules.PaymentCodeFor("194K", "Mutual Fund", "All"));
    }

    [Theory]
    // Authoritative Annexure 2 Section / Table Sl. references from the Form 138/140 spec.
    [InlineData("192A", "Section 392(7)")]
    [InlineData("193",  "Section 393(1) Sl.5(i)")]
    [InlineData("194",  "Section 393(1) Sl.7")]
    [InlineData("194C", "Section 393(1) Sl.6(i)")]
    [InlineData("194D", "Section 393(1) Sl.1(i)")]
    [InlineData("194H", "Section 393(1) Sl.1(ii)")]
    [InlineData("194J", "Section 393(1) Sl.6(iii)")]
    [InlineData("194K", "Section 393(1) Sl.4(i)")]
    [InlineData("194B", "Section 393(3) Sl.1")]
    [InlineData("194T", "Section 393(3) Sl.7")]
    public void ReferenceAct_MatchesSpecAnnexure2(string section, string expectedRefPrefix)
    {
        var rule = new BuiltInRule(section, "x", "All", true, 0, 0, 0, 0, "2026-04-01", "", "v");
        Assert.StartsWith(expectedRefPrefix, rule.ReferenceAct);
    }

    [Fact]
    public void PaymentCodeFor_UnknownSection_IsBlank()
    {
        // 195 (non-resident) and TCS codes are not yet confirmed from the file-format
        // .xls — they must stay blank, never a guessed value.
        Assert.Equal("", BuiltInTdsRules.PaymentCodeFor("195", "Other", "All"));
        Assert.Equal("", BuiltInTdsRules.PaymentCodeFor("206C", "Scrap", "All"));
    }
}
