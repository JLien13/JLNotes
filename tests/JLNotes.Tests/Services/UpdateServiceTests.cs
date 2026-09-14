using JLNotes.Services;

namespace JLNotes.Tests.Services;

/// <summary>
/// Regression for the 1.2.7 Settings crash: UpdateService's static fields were
/// declared in an order where the HttpClient was built before CurrentVersion
/// existed, so the type initializer threw and every static access after that
/// raised TypeInitializationException. Touching the statics here forces the
/// initializer to run under test.
/// </summary>
public class UpdateServiceTests
{
    [Fact]
    public void StaticMembers_InitializeWithoutThrowing()
    {
        var version = UpdateService.CurrentVersion;
        var slug = UpdateService.RepoSlug;

        Assert.NotNull(version);
        Assert.True(version.Major >= 1);
        Assert.Equal("JLien13/JLNotes", slug);
    }

    [Fact]
    public void Instance_CanBeConstructed()
    {
        var svc = new UpdateService(System.IO.Path.GetTempPath());
        Assert.NotNull(svc);
    }
}
