using JLNotes.Models;
using JLNotes.ViewModels;

namespace JLNotes.Tests.ViewModels;

public class ViewModeResolutionTests
{
    [Theory]
    [InlineData("split")]
    [InlineData("grid")]
    [InlineData("list")]
    public void ExplicitChoice_AlwaysWins_RegardlessOfFileOrLegacyFlag(string chosen)
    {
        // An explicit prior choice is the source of truth — "remember the last
        // selected view, always" — even over the legacy gridView flag.
        var settings = new AppSettings { ViewMode = chosen, GridView = true };

        Assert.Equal(chosen, MainViewModel.ResolveInitialViewMode(settings, settingsFileExisted: true));
        Assert.Equal(chosen, MainViewModel.ResolveInitialViewMode(settings, settingsFileExisted: false));
    }

    [Fact]
    public void FreshInstall_DefaultsToSplit()
    {
        var settings = new AppSettings(); // viewMode "", gridView false

        Assert.Equal("split", MainViewModel.ResolveInitialViewMode(settings, settingsFileExisted: false));
        Assert.Equal(MainViewModel.DefaultViewMode,
            MainViewModel.ResolveInitialViewMode(settings, settingsFileExisted: false));
    }

    [Fact]
    public void ExistingUser_NoChoice_NoLegacyFlag_KeepsList()
    {
        // Settings file already exists but predates viewMode — keep the historical
        // list default rather than flipping them to split on upgrade.
        var settings = new AppSettings();

        Assert.Equal("list", MainViewModel.ResolveInitialViewMode(settings, settingsFileExisted: true));
    }

    [Fact]
    public void LegacyGridUser_MigratesToGrid()
    {
        var settings = new AppSettings { GridView = true }; // viewMode still ""

        Assert.Equal("grid", MainViewModel.ResolveInitialViewMode(settings, settingsFileExisted: true));
    }
}
