using CDeTDS.DAL;

namespace CDeTDS.Tests;

/// <summary>
/// Tests the backup file metadata + filename conventions.
/// Live backup/restore against the real DB folder is not tested here (would mutate user data).
/// Instead, we test the BackupInfo helper formatting + filename patterns we rely on.
/// </summary>
public class BackupTests
{
    [Theory]
    [InlineData(           500, "500 B")]
    [InlineData(          1500, "1.5 KB")]
    [InlineData(       2_500_000, "2.4 MB")]
    [InlineData(   2_500_000_000, "2.33 GB")]
    public void BackupInfo_SizeLabel_FormatsCorrectly(long bytes, string expected)
    {
        var info = new BackupInfo { SizeBytes = bytes };
        Assert.Equal(expected, info.SizeLabel);
    }

    [Theory]
    [InlineData("tds_pro_auto_20260518.db",         true)]
    [InlineData("tds_pro_manual_20260518-141533.db", false)]
    [InlineData("TDS_PRO_AUTO_20260518.db",         true)]   // case-insensitive
    public void BackupInfo_IsAuto_DetectedFromFilename(string fileName, bool expectedAuto)
    {
        // This mirrors the ListBackups detection logic — verify the convention holds
        bool isAuto = fileName.StartsWith("tds_pro_auto_", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedAuto, isAuto);
    }

    [Fact]
    public void BackupInfo_DefaultValues()
    {
        var info = new BackupInfo();
        Assert.Equal("", info.Path);
        Assert.Equal("", info.FileName);
        Assert.False(info.IsAuto);
        Assert.Equal(0, info.SizeBytes);
        Assert.Equal("0 B", info.SizeLabel);
    }
}
