using JLNotes.Helpers;

namespace JLNotes.Tests.Helpers;

public class UpdateLogicTests
{
    private static readonly Version Current = new(1, 2, 6);

    private static string Release(string tag, params string[] assetNames)
    {
        var assets = string.Join(",", assetNames.Select(n =>
            $"{{\"name\":\"{n}\",\"browser_download_url\":\"https://github.com/JLien13/JLNotes/releases/download/{tag}/{n}\"}}"));
        return $"{{\"tag_name\":\"{tag}\",\"assets\":[{assets}]}}";
    }

    [Theory]
    [InlineData("v1.2.7", "1.2.7")]
    [InlineData("1.2.7", "1.2.7")]
    [InlineData("V1.3", "1.3.0")]
    [InlineData("1.2.7-rc1", "1.2.7")]
    [InlineData(" v1.2.7 ", "1.2.7")]
    public void ParseVersion_AcceptsTagShapes(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateLogic.ParseVersion(tag));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v")]
    public void ParseVersion_RejectsGarbage(string? tag)
    {
        Assert.Null(UpdateLogic.ParseVersion(tag));
    }

    [Fact]
    public void NewerRelease_IsAnUpdate()
    {
        var r = UpdateLogic.ParseLatestRelease(Release("v1.2.7", "JLNotes-Setup-1.2.7.exe"), Current);
        Assert.True(r.Ok);
        Assert.False(r.UpToDate);
        Assert.Equal(new Version(1, 2, 7), r.Latest);
        Assert.Equal("JLNotes-Setup-1.2.7.exe", r.AssetName);
        Assert.EndsWith("/v1.2.7/JLNotes-Setup-1.2.7.exe", r.AssetUrl);
    }

    [Fact]
    public void SameOrOlderRelease_IsUpToDate()
    {
        Assert.True(UpdateLogic.ParseLatestRelease(Release("v1.2.6", "JLNotes-Setup-1.2.6.exe"), Current).UpToDate);
        Assert.True(UpdateLogic.ParseLatestRelease(Release("v1.2.5", "JLNotes-Setup-1.2.5.exe"), Current).UpToDate);
    }

    [Fact]
    public void PicksTheInstallerAmongOtherAssets()
    {
        var r = UpdateLogic.ParseLatestRelease(Release("v1.2.7", "notes.zip", "JLNotes-Setup-1.2.7.exe", "SHA256SUMS"), Current);
        Assert.True(r.Ok);
        Assert.Equal("JLNotes-Setup-1.2.7.exe", r.AssetName);
    }

    [Fact]
    public void ReleaseWithoutInstaller_Fails()
    {
        var r = UpdateLogic.ParseLatestRelease(Release("v1.2.7", "notes.zip"), Current);
        Assert.False(r.Ok);
        Assert.Contains("installer", r.Error);
    }

    [Fact]
    public void ReleaseWithBadTag_Fails()
    {
        var r = UpdateLogic.ParseLatestRelease(Release("latest", "JLNotes-Setup-1.2.7.exe"), Current);
        Assert.False(r.Ok);
    }

    [Fact]
    public void UnreadableJson_FailsInsteadOfThrowing()
    {
        var r = UpdateLogic.ParseLatestRelease("<html>rate limited</html>", Current);
        Assert.False(r.Ok);
        Assert.Equal(Current, r.Current);
    }
}
