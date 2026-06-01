using CDeTDS.Common;

namespace CDeTDS.Tests;

public class ErrorTranslatorTests
{
    // ── SQLite constraint patterns ───────────────────────────────────────────
    [Fact]
    public void UniqueConstraint_OnPan_PrettifiesField()
    {
        var ex = new Exception("UNIQUE constraint failed: employees.pan");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("already exists", msg);
        Assert.Contains("PAN", msg);
    }

    [Fact]
    public void UniqueConstraint_OnChallanNo_TitleCases()
    {
        var ex = new Exception("UNIQUE constraint failed: challans.challan_no");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("Challan No", msg);
    }

    [Fact]
    public void NotNullConstraint_NamesField()
    {
        var ex = new Exception("NOT NULL constraint failed: employees.name");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("Required field", msg);
        Assert.Contains("Name", msg);
    }

    [Fact]
    public void ForeignKey_ExplainsDependency()
    {
        var ex = new Exception("FOREIGN KEY constraint failed");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("referenced", msg);
    }

    [Fact]
    public void Sqlite_Busy_TellsUserToRetry()
    {
        var ex = new Exception("database is locked");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("busy", msg);
        Assert.Contains("try again", msg);
    }

    [Fact]
    public void Sqlite_Readonly_PointsAtPermissions()
    {
        var ex = new Exception("attempt to write a readonly database");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("read-only", msg);
        Assert.Contains("permissions", msg);
    }

    [Fact]
    public void NoSuchTable_SuggestsRestart()
    {
        var ex = new Exception("SQLite Error 1: 'no such table: employees'");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("schema mismatch", msg);
    }

    // ── File I/O exceptions ──────────────────────────────────────────────────
    [Fact]
    public void FileNotFound_ExtractsPath()
    {
        var ex = new FileNotFoundException("Could not find file 'C:\\data\\missing.db'.");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("File not found", msg);
        Assert.Contains("missing.db", msg);
    }

    [Fact]
    public void DirectoryNotFound_LabelsFolder()
    {
        var ex = new DirectoryNotFoundException("Could not find a part of the path 'C:\\nope\\'.");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("Folder not found", msg);
    }

    [Fact]
    public void UnauthorizedAccess_ExplainsPermission()
    {
        var ex = new UnauthorizedAccessException();
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("Access denied", msg);
    }

    [Fact]
    public void FileInUse_TellsUserToCloseIt()
    {
        var ex = new IOException("The process cannot access the file because it is being used by another process.");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("another program", msg);
    }

    // ── Network ──────────────────────────────────────────────────────────────
    [Fact]
    public void HttpRequest_MentionsInternet()
    {
        var ex = new HttpRequestException("connection refused");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("Network", msg);
    }

    [Fact]
    public void TaskCanceled_MentionsTimeout()
    {
        var ex = new TaskCanceledException();
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Contains("timed out", msg);
    }

    // ── Argument ─────────────────────────────────────────────────────────────
    [Fact]
    public void FormatException_PrefixesInvalidFormat()
    {
        var ex = new FormatException("input not in correct format");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.StartsWith("Invalid format", msg);
    }

    // ── Inner exception walking ──────────────────────────────────────────────
    [Fact]
    public void Walks_To_Innermost_Exception()
    {
        var inner = new Exception("UNIQUE constraint failed: deductors.tan");
        var middle = new Exception("middle layer", inner);
        var outer = new InvalidOperationException("outer wrapper", middle);
        var msg = ErrorTranslator.UserFriendly(outer);
        Assert.Contains("TAN", msg);
        Assert.Contains("already exists", msg);
    }

    // ── Null guard + fallback ────────────────────────────────────────────────
    [Fact]
    public void NullException_ReturnsGenericMessage()
    {
        Assert.Equal("Unknown error.", ErrorTranslator.UserFriendly(null));
    }

    [Fact]
    public void UnknownException_FallsBackToCleanedMessage()
    {
        var ex = new InvalidOperationException("Something specific happened");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Equal("Something specific happened", msg);
    }

    [Fact]
    public void Sqlite_ErrorPrefix_Stripped()
    {
        // Fallback path strips "SQLite Error 5: " prefix
        var ex = new Exception("SQLite Error 5: some unusual condition");
        var msg = ErrorTranslator.UserFriendly(ex);
        Assert.Equal("some unusual condition", msg);
    }

    // ── Fail() convenience ───────────────────────────────────────────────────
    [Fact]
    public void Fail_ReturnsOkFalse_WithTranslatedMessage()
    {
        var (ok, msg) = ErrorTranslator.Fail(new Exception("database is locked"));
        Assert.False(ok);
        Assert.Contains("busy", msg);
    }
}
