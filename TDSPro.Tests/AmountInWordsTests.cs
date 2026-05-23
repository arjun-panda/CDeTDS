using TDSPro.Common;

namespace TDSPro.Tests;

public class AmountInWordsTests
{
    [Theory]
    [InlineData(0,         "Rupees Zero Only")]
    [InlineData(1,         "Rupees One Only")]
    [InlineData(19,        "Rupees Nineteen Only")]
    [InlineData(20,        "Rupees Twenty Only")]
    [InlineData(45,        "Rupees Forty-Five Only")]
    [InlineData(100,       "Rupees One Hundred Only")]
    [InlineData(105,       "Rupees One Hundred and Five Only")]
    [InlineData(999,       "Rupees Nine Hundred and Ninety-Nine Only")]
    [InlineData(1000,      "Rupees One Thousand Only")]
    [InlineData(1234,      "Rupees One Thousand Two Hundred and Thirty-Four Only")]
    [InlineData(100000,    "Rupees One Lakh Only")]
    [InlineData(234567,    "Rupees Two Lakh Thirty-Four Thousand Five Hundred and Sixty-Seven Only")]
    [InlineData(10000000,  "Rupees One Crore Only")]
    [InlineData(12345678,  "Rupees One Crore Twenty-Three Lakh Forty-Five Thousand Six Hundred and Seventy-Eight Only")]
    public void Rupees_FormatsIndianGrouping(double amt, string expected)
    {
        Assert.Equal(expected, AmountInWords.Rupees(amt));
    }

    [Fact]
    public void Rupees_WithPaise()
    {
        Assert.Equal("Rupees One Hundred and Fifty Paise Only", AmountInWords.Rupees(100.50));
    }

    [Fact]
    public void Rupees_Negative_HasMinusPrefix()
    {
        Assert.StartsWith("Minus Rupees", AmountInWords.Rupees(-500));
    }
}
