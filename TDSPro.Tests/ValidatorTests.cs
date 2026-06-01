using CDeTDS.Common;
using CDeTDS.BLL;
using CDeTDS.DAL.Models;

namespace CDeTDS.Tests;

public class ValidatorPrimitiveTests
{
    // ── PAN ───────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("ABCDE1234F", true)]
    [InlineData("abcde1234f", true)]   // lowercase normalised
    [InlineData("ABCD1234F",  false)]  // only 4 letters
    [InlineData("ABCDE12345", false)]  // missing trailing letter
    [InlineData("ABCDEF234F", false)]  // letter in digit position
    [InlineData("",           false)]
    [InlineData(null,         false)]
    public void IsValidPan_Cases(string? pan, bool expected)
        => Assert.Equal(expected, Validators.IsValidPan(pan));

    // ── Aadhaar ───────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("234567890123",    true)]
    [InlineData("2345 6789 0123",  true)]   // spaces tolerated
    [InlineData("1234567890123",   false)]  // 13 digits
    [InlineData("123456789012",    false)]  // starts with 1
    [InlineData("",                false)]
    public void IsValidAadhaar_Cases(string s, bool expected)
        => Assert.Equal(expected, Validators.IsValidAadhaar(s));

    // ── IFSC ──────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("HDFC0001234", true)]
    [InlineData("hdfc0001234", true)]   // case insensitive
    [InlineData("HDFC1001234", false)]  // 5th char must be 0
    [InlineData("HDF00001234", false)]  // only 3 alpha
    public void IsValidIfsc_Cases(string s, bool expected)
        => Assert.Equal(expected, Validators.IsValidIfsc(s));

    // ── PIN ───────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("400001", true)]
    [InlineData("000001", false)]   // can't start with 0
    [InlineData("40000",  false)]   // 5 digits
    public void IsValidPin_Cases(string s, bool expected)
        => Assert.Equal(expected, Validators.IsValidPin(s));

    // ── Mobile ────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("9876543210",   true)]
    [InlineData("+91-9876543210", true)]  // formatted
    [InlineData("5876543210",   false)]   // starts with 5
    [InlineData("987654321",    false)]   // 9 digits
    public void IsValidMobile_Cases(string s, bool expected)
        => Assert.Equal(expected, Validators.IsValidMobile(s));

    // ── Email ─────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("user@@example.com", false)]
    [InlineData("user.example.com",  false)]
    public void IsValidEmail_Cases(string s, bool expected)
        => Assert.Equal(expected, Validators.IsValidEmail(s));
}

public class ValidationResultTests
{
    [Fact]
    public void DefaultIsOk_NoErrors()
    {
        var r = new ValidationResult();
        Assert.True(r.Ok);
        Assert.Empty(r.Errors);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void AddError_NotOk()
    {
        var r = new ValidationResult();
        r.AddError("bad");
        Assert.False(r.Ok);
        Assert.Single(r.Errors);
    }

    [Fact]
    public void Merge_AccumulatesBoth()
    {
        var a = new ValidationResult(); a.AddError("e1"); a.AddWarning("w1");
        var b = new ValidationResult(); b.AddError("e2");
        a.Merge(b);
        Assert.Equal(2, a.Errors.Count);
        Assert.Single(a.Warnings);
    }

    [Fact]
    public void EmptyErrorIgnored()
    {
        var r = new ValidationResult();
        r.AddError("   ");
        r.AddError("");
        Assert.True(r.Ok);
    }
}

public class EmployeeValidatorTests
{
    private static Employee GoodEmployee() => new()
    {
        EmployeeCode = "EMP-001",
        Name         = "Ajay Sharma",
        Pan          = "ABCDE1234F",
        DateOfBirth  = "15-Jan-1985",
        JoinDate     = "01-Apr-2020",
        TaxRegime    = "New",
        HraCityType  = "Metro",
        Salary       = new SalaryStructure { Basic = 30000, Hra = 15000 },
    };

    [Fact]
    public void GoodEmployee_Passes()
    {
        var r = PayrollValidators.ValidateEmployee(GoodEmployee());
        Assert.True(r.Ok, r.ErrorSummary);
    }

    [Fact]
    public void MissingPan_Fails()
    {
        var e = GoodEmployee(); e.Pan = "";
        var r = PayrollValidators.ValidateEmployee(e);
        Assert.False(r.Ok);
        Assert.Contains("PAN is required", r.ErrorSummary);
    }

    [Fact]
    public void InvalidPanFormat_Fails()
    {
        var e = GoodEmployee(); e.Pan = "ABC12";
        var r = PayrollValidators.ValidateEmployee(e);
        Assert.False(r.Ok);
        Assert.Contains("invalid", r.ErrorSummary);
    }

    [Fact]
    public void MissingDob_Fails()
    {
        var e = GoodEmployee(); e.DateOfBirth = "";
        var r = PayrollValidators.ValidateEmployee(e);
        Assert.False(r.Ok);
        Assert.Contains("Date of Birth", r.ErrorSummary);
    }

    [Fact]
    public void FutureJoinDate_Fails()
    {
        var e = GoodEmployee(); e.JoinDate = DateTime.Today.AddDays(30).ToString("dd-MMM-yyyy");
        var r = PayrollValidators.ValidateEmployee(e);
        Assert.False(r.Ok);
        Assert.Contains("future", r.ErrorSummary);
    }

    [Fact]
    public void LeavingBeforeJoining_Fails()
    {
        var e = GoodEmployee();
        e.JoinDate    = "01-Apr-2020";
        e.LeavingDate = "01-Jan-2019";
        var r = PayrollValidators.ValidateEmployee(e);
        Assert.False(r.Ok);
        Assert.Contains("Leaving", r.ErrorSummary);
    }

    [Fact]
    public void InvalidRegime_Fails()
    {
        var e = GoodEmployee(); e.TaxRegime = "Hybrid";
        var r = PayrollValidators.ValidateEmployee(e);
        Assert.False(r.Ok);
    }

    [Fact]
    public void MalformedPhone_WarnsButPasses()
    {
        var e = GoodEmployee(); e.Phone = "12345";
        var r = PayrollValidators.ValidateEmployee(e);
        Assert.True(r.Ok);
        Assert.Single(r.Warnings);
    }

    [Fact]
    public void Under15YearsOld_Fails()
    {
        var e = GoodEmployee(); e.DateOfBirth = DateTime.Today.AddYears(-10).ToString("dd-MMM-yyyy");
        var r = PayrollValidators.ValidateEmployee(e);
        Assert.False(r.Ok);
        Assert.Contains("at least 15", r.ErrorSummary);
    }
}

public class SalaryStructureValidatorTests
{
    [Fact]
    public void NegativeBasic_Fails()
    {
        var s = new SalaryStructure { Basic = -1000 };
        var r = PayrollValidators.ValidateSalaryStructure(s);
        Assert.Contains("Basic Salary cannot be negative", r.ErrorSummary);
    }

    [Fact]
    public void NegativeComponentPaid_Fails()
    {
        var s = new SalaryStructure { Basic = 30000 };
        s.Components.Add(new SalaryComponent { Name = "LTA", Received = 1000, Paid = -100 });
        var r = PayrollValidators.ValidateSalaryStructure(s);
        Assert.False(r.Ok);
    }

    [Fact]
    public void PaidExceedsReceived_Warns()
    {
        var s = new SalaryStructure { Basic = 30000 };
        s.Components.Add(new SalaryComponent { Name = "LTA", Received = 1000, Paid = 1500 });
        var r = PayrollValidators.ValidateSalaryStructure(s);
        Assert.True(r.Ok);   // it's a warning, not error
        Assert.Contains("Paid", r.WarningSummary);
    }

    [Fact]
    public void EmptyComponentName_Fails()
    {
        var s = new SalaryStructure { Basic = 30000 };
        s.Components.Add(new SalaryComponent { Name = "", Received = 1000 });
        var r = PayrollValidators.ValidateSalaryStructure(s);
        Assert.False(r.Ok);
    }

    [Fact]
    public void CtcReconciliation_OffByMoreThan100_Warns()
    {
        // Basic 22000 + HRA 11000 + Special 1000 = 34000; target = 55000; off by 21000
        var s = new SalaryStructure
        {
            TargetCtc = 55000, Basic = 22000, Hra = 11000, SpecialAllowance = 1000,
            PfApplicable = true, IncludeGratuity = true,
        };
        var r = PayrollValidators.ValidateSalaryStructure(s);
        Assert.True(r.Ok);
        Assert.Contains("reconciliation is off", r.WarningSummary);
    }

    [Fact]
    public void Ctc10Crore_Fails()
    {
        var s = new SalaryStructure { Basic = 50000, TargetCtc = 1_000_000_000 };
        var r = PayrollValidators.ValidateSalaryStructure(s);
        Assert.False(r.Ok);
        Assert.Contains("exceeds", r.ErrorSummary);
    }
}

public class MonthlyEntryValidatorTests
{
    [Fact]
    public void InvalidMonth_Fails()
    {
        var e = new MonthlySalaryEntry { Month = 13, Year = 2026, FinancialYear = "2026-27" };
        var r = PayrollValidators.ValidateMonthlyEntry(e);
        Assert.False(r.Ok);
    }

    [Fact]
    public void NegativeTds_Fails()
    {
        var e = new MonthlySalaryEntry { Month = 4, Year = 2026, FinancialYear = "2026-27", TdsDeducted = -100 };
        var r = PayrollValidators.ValidateMonthlyEntry(e);
        Assert.False(r.Ok);
    }

    [Fact]
    public void DaysWorkedExceedsWorkingDays_Fails()
    {
        var e = new MonthlySalaryEntry
        {
            Month = 5, Year = 2026, FinancialYear = "2026-27",
            WorkingDays = 30, DaysWorked = 35,
        };
        var r = PayrollValidators.ValidateMonthlyEntry(e);
        Assert.False(r.Ok);
    }
}
