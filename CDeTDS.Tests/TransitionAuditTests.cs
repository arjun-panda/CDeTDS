using CDeTDS.Common;
using CDeTDS.DAL;
using CDeTDS.DAL.Models;

namespace CDeTDS.Tests;

/// <summary>
/// AUDIT_PLAN.md Part 1 — 1961 Act → 2025 Act transition scenarios.
/// The governing Act/FY is decided by the EARLIER of credit date and payment date
/// (trigger-date rule), never by session FY, filing date or deposit date.
/// </summary>
public class TransitionAuditTests
{
    // ── Scenario 1.2: credit 15-Mar-2026, payment 10-Apr-2026 → OLD Act ──────
    [Fact]
    public void Credit_Mar2026_Paid_Apr2026_Stays_Old_Act()
    {
        var e = new TdsEntry
        {
            CreditDate = new DateTime(2026, 3, 15),
            EntryDate  = new DateTime(2026, 4, 10),   // payment date
        };
        Assert.Equal(new DateTime(2026, 3, 15), e.TriggerDate);
        Assert.Equal("2025-26", FolderManager.DetectFY(e.TriggerDate));
        Assert.False(TaxRules.IsNewAct(FolderManager.DetectFY(e.TriggerDate)));
        Assert.Equal("Q4", FolderManager.DetectQuarter(e.TriggerDate));
    }

    // ── Scenario 1.3: advance paid 20-Mar-2026, credited 05-Apr-2026 → OLD Act ─
    [Fact]
    public void Advance_Paid_Mar2026_Credited_Apr2026_Stays_Old_Act()
    {
        var e = new TdsEntry
        {
            EntryDate  = new DateTime(2026, 3, 20),   // advance payment
            CreditDate = new DateTime(2026, 4, 5),    // credited later
        };
        Assert.Equal(new DateTime(2026, 3, 20), e.TriggerDate);
        Assert.Equal("2025-26", FolderManager.DetectFY(e.TriggerDate));
    }

    // ── Scenario 1.4: credit and payment both 02-Apr-2026 → NEW Act ──────────
    [Fact]
    public void Credit_And_Payment_Apr2026_Is_New_Act()
    {
        var e = new TdsEntry
        {
            EntryDate  = new DateTime(2026, 4, 2),
            CreditDate = new DateTime(2026, 4, 2),
        };
        var fy = FolderManager.DetectFY(e.TriggerDate);
        Assert.Equal("2026-27", fy);
        Assert.True(TaxRules.IsNewAct(fy));
        Assert.Equal("Form 140", TaxRules.NonSalaryReturnForm(fy));
    }

    // ── Scenario 1.9: deducted Mar-2026, deposited late May-2026 → OLD Act ───
    [Fact]
    public void Late_Deposit_Does_Not_Change_Regime()
    {
        // Deposit date is NOT part of the trigger; entry stays in old-act stream
        var e = new TdsEntry
        {
            EntryDate   = new DateTime(2026, 3, 28),
            PaymentDate = new DateTime(2026, 5, 20),   // challan deposit (late)
        };
        Assert.Equal(new DateTime(2026, 3, 28), e.TriggerDate);
        Assert.Equal("2025-26", FolderManager.DetectFY(e.TriggerDate));
    }

    // ── Form-type ↔ FY mapping helpers ────────────────────────────────────────
    [Theory]
    [InlineData("24Q",  "2026-27", "138")]
    [InlineData("26Q",  "2026-27", "140")]
    [InlineData("27EQ", "2026-27", "143")]
    [InlineData("27Q",  "2026-27", "144")]
    [InlineData("138",  "2025-26", "24Q")]
    [InlineData("140",  "2025-26", "26Q")]
    [InlineData("143",  "2025-26", "27EQ")]
    [InlineData("24Q",  "2025-26", "24Q")]
    [InlineData("138",  "2026-27", "138")]
    public void FormTypeForFy_Maps_To_Correct_Act_Family(string form, string fy, string expected)
        => Assert.Equal(expected, TaxRules.FormTypeForFy(form, fy));

    [Fact]
    public void ReturnFormTypes_Are_Fy_Aware()
    {
        Assert.Equal(new[] { "24Q", "26Q", "27EQ" }, TaxRules.ReturnFormTypesForFy("2025-26"));
        Assert.Equal(new[] { "138", "140", "143" }, TaxRules.ReturnFormTypesForFy("2026-27"));
    }

    [Fact]
    public void YearLabel_Uses_TY_For_New_Act()
    {
        Assert.Equal("FY 2025-26", TaxRules.YearLabel("2025-26"));
        Assert.Equal("TY 2026-27", TaxRules.YearLabel("2026-27"));
    }

    [Fact]
    public void AssessmentYear_Is_Abolished_Under_New_Act()
    {
        // Old act: AY = FY + 1
        Assert.Equal("AY 2026-27", TaxRules.AssessmentYearLabel("2025-26"));
        // New act: AY abolished; Tax Year equals the financial year itself
        Assert.Equal("TY 2026-27", TaxRules.AssessmentYearLabel("2026-27"));
        Assert.Equal("TY 2027-28", TaxRules.AssessmentYearLabel("2027-28"));
    }

    // ── Mixing validation (Audit 1.7 / CLAUDE.md rule 5) ─────────────────────
    private static ReturnData MinimalReturn(string formType, string fy) => new()
    {
        Header = new ReturnHeader
        {
            FormType = formType, FinancialYear = fy, Quarter = "Q1",
            TanOfDeductor = "DELA12345A", PanOfDeductor = "AAAPL1234C",
            DeductorName = "Acme Corp", ResponsiblePan = "AAAPL1234C",
            ResponsibleName = "Rahul Sharma",
        },
    };

    [Fact]
    public void Old_Form_With_New_Act_FY_Is_Rejected()
    {
        var errors = FvuGenerator.Validate(MinimalReturn("24Q", "2026-27"));
        var e030 = errors.FirstOrDefault(x => x.Code == "E030");
        Assert.NotNull(e030);
        Assert.Contains("138", e030!.Message);
    }

    [Fact]
    public void New_Form_With_Old_Act_FY_Is_Rejected()
    {
        var errors = FvuGenerator.Validate(MinimalReturn("140", "2025-26"));
        Assert.Contains(errors, x => x.Code == "E031");
    }

    [Fact]
    public void Matching_Form_And_FY_Passes_Mixing_Check()
    {
        Assert.DoesNotContain(FvuGenerator.Validate(MinimalReturn("138", "2026-27")),
                              x => x.Code is "E030" or "E031");
        Assert.DoesNotContain(FvuGenerator.Validate(MinimalReturn("26Q", "2025-26")),
                              x => x.Code is "E030" or "E031");
    }

    // ── Paper return citations (Audit 1.6/1.8) ───────────────────────────────
    private static string GeneratePaper(string formType, string fy)
    {
        var tmp  = Path.Combine(Path.GetTempPath(), $"cdetds_paper_{Guid.NewGuid():N}.html");
        var path = PaperReturnGenerator.Generate(MinimalReturn(formType, fy), tmp);
        var html = File.ReadAllText(path);
        try { File.Delete(path); } catch { }
        return html;
    }

    [Fact]
    public void Paper_Return_FY2026_27_Uses_Act_2025_Citations()
    {
        var html = GeneratePaper("138", "2026-27");
        Assert.Contains("FORM NO. 138", html);
        Assert.Contains("erstwhile Form 24Q", html);
        Assert.Contains("Income-tax Act", html);
        Assert.Contains("2025", html);
        Assert.Contains("TY 2026-27", html);
        Assert.Contains("section&nbsp;392", html);
        Assert.DoesNotContain("1961", html);
        Assert.DoesNotContain("section 192 and rule 31A", html);
    }

    [Fact]
    public void Paper_Return_Heals_Old_Form_Code_For_New_Act_FY()
    {
        // Even if a stale caller passes 24Q with FY 2026-27, the printout must not cite 1961
        var html = GeneratePaper("24Q", "2026-27");
        Assert.Contains("FORM NO. 138", html);
        Assert.DoesNotContain("1961", html);
    }

    [Fact]
    public void Paper_Return_FY2025_26_Keeps_1961_Citations()
    {
        var html = GeneratePaper("24Q", "2025-26");
        Assert.Contains("FORM NO. 24Q", html);
        Assert.Contains("Income&#8209;tax Act,&nbsp;1961", html);
        Assert.Contains("section 192 and rule 31A", html);
        Assert.Contains("FY 2025-26", html);
        Assert.DoesNotContain("Act 2025", html);
        Assert.DoesNotContain("erstwhile", html);
    }

    // ── Aggregate threshold (Audit 3.3) — 194C: ₹30k single OR ₹1L FY aggregate ─
    [Theory]
    // single 25k, no YTD → below both thresholds → no TDS
    [InlineData(30000, 100000, 25000, 0,      true)]
    // single 25k but YTD 80k → 105k aggregate crosses ₹1L → TDS applies
    [InlineData(30000, 100000, 25000, 80000,  false)]
    // single 35k crosses the ₹30k per-payment threshold on its own → TDS applies
    [InlineData(30000, 100000, 35000, 0,      false)]
    // exactly at the aggregate boundary (75k + 25k = 100k) → TDS applies
    [InlineData(30000, 100000, 25000, 75000,  false)]
    // section with no aggregate rule: only the single threshold matters
    [InlineData(30000, 0,      25000, 999999, true)]
    public void Aggregate_Threshold_Rule(double single, double aggregate, double gross, double ytd, bool expectBelow)
        => Assert.Equal(expectBelow, TdsRulesEngine.IsBelowThreshold(single, aggregate, gross, ytd));
}
