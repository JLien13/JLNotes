using JLNotes.Helpers;

namespace JLNotes.Tests.Helpers;

// When two PCs save the same file before either hears of the other, the sync tool
// keeps both and gives one a "conflicted copy" name. These names are how the app
// knows a file is a second version of a note and not a note of its own.
public class SyncConflictNameTests
{
    [Theory]
    [InlineData("2026-11-20-plan (JL's conflicted copy 2026-11-20)", "2026-11-20-plan")]
    [InlineData("2026-11-20-plan (JL Lien's conflicted copy 2026-11-20 (1))", "2026-11-20-plan")]
    [InlineData("2026-11-20-plan (conflicted copy 2026-11-20 101112)", "2026-11-20-plan")]
    [InlineData("2026-11-20-plan (DESKTOP-AB12's Conflicted Copy 2026-11-20)", "2026-11-20-plan")]
    [InlineData("2026-11-20-plan.sync-conflict-20261120-101112-ABCDEF7", "2026-11-20-plan")]
    [InlineData("projects (JL's conflicted copy 2026-11-20)", "projects")]
    public void OriginalOf_AConflictedCopy_IsTheFileItCameFrom(string name, string original)
    {
        Assert.Equal(original, SyncConflictName.OriginalOf(name));
    }

    [Theory]
    [InlineData("2026-11-20-plan")]
    [InlineData("2026-11-20-plan (draft)")]
    [InlineData("2026-11-20-why-conflicted-copy-names-matter")]
    [InlineData(" (JL's conflicted copy 2026-11-20)")]
    public void OriginalOf_AnOrdinaryName_IsNothing(string name)
    {
        Assert.Null(SyncConflictName.OriginalOf(name));
    }
}
