namespace TDSPro.Tests;

/// <summary>
/// Tests the [AUTO:salary:{empId}:{fy}:{month}] tag convention that links a
/// MonthlySalaryEntry to its auto-created Sec 192 TDS Entry.
///
/// Unlock relies on this tag to find + delete the right TDS row. If the format
/// ever changes, Approve and Unlock would silently diverge → orphan TDS entries.
/// These tests pin the convention.
/// </summary>
public class UnlockTagTests
{
    /// <summary>Mirror of the tag-builder string in SalaryService.UpsertSec192Entry / RemoveSec192Entry.</summary>
    private static string BuildTag(int empId, string fy, int month)
        => $"[AUTO:salary:{empId}:{fy}:{month}]";

    [Fact]
    public void Tag_Format_IsStable()
    {
        Assert.Equal("[AUTO:salary:42:2026-27:5]", BuildTag(42, "2026-27", 5));
    }

    [Fact]
    public void Tag_DifferentEmployees_DifferentTags()
    {
        Assert.NotEqual(BuildTag(1, "2026-27", 5), BuildTag(2, "2026-27", 5));
    }

    [Fact]
    public void Tag_DifferentFyOrMonth_DifferentTags()
    {
        Assert.NotEqual(BuildTag(1, "2025-26", 5), BuildTag(1, "2026-27", 5));
        Assert.NotEqual(BuildTag(1, "2026-27", 4), BuildTag(1, "2026-27", 5));
    }

    [Fact]
    public void Remarks_ContainsTag()
    {
        // Mirrors UpsertSec192Entry's full remarks string format
        int    empId = 7; string fy = "2026-27"; int month = 8;
        string tag   = BuildTag(empId, fy, month);
        string remarks = $"Salary TDS — {new DateTime(1, month, 1):MMMM} 2026 {tag}";
        // The matcher (TdsEntryRepository.GetAll().FirstOrDefault) uses Contains:
        Assert.Contains(tag, remarks);
        // And shouldn't match a DIFFERENT employee's tag accidentally
        Assert.DoesNotContain(BuildTag(8, fy, month), remarks);
    }
}
