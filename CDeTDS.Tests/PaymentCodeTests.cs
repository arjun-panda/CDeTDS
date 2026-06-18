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
    public void PaymentCodeFor_ReturnsConfirmedCode(string section, string nature, string deducteeType, string expected)
    {
        Assert.Equal(expected, BuiltInTdsRules.PaymentCodeFor(section, nature, deducteeType));
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
