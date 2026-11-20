using JLNotes.Helpers;
using JLNotes.Models;

namespace JLNotes.Tests.Helpers;

// "Combine both" when a note was changed in two places: every line from both
// versions stays, lines they share appear once, and nothing is ever dropped.
public class NoteMergeTests
{
    [Fact]
    public void CombineText_TheSameText_IsUnchanged()
    {
        Assert.Equal("a\nb", NoteMerge.CombineText("a\nb", "a\nb"));
    }

    [Fact]
    public void CombineText_EachSideAddedALine_KeepsBothInPlace()
    {
        var combined = NoteMerge.CombineText("intro\nmine added\nend", "intro\nend\nother added");

        Assert.Equal("intro\nmine added\nend\nother added", combined);
    }

    [Fact]
    public void CombineText_TheSameLineChangedOnBothSides_KeepsMineThenTheOther()
    {
        Assert.Equal("call Bob\ncall Bob at 3", NoteMerge.CombineText("call Bob", "call Bob at 3"));
    }

    [Fact]
    public void CombineText_ALineOnlyOneSideStillHas_IsKept()
    {
        Assert.Equal("a\nb\nc", NoteMerge.CombineText("a\nb\nc", "a\nc"));
        Assert.Equal("a\nb\nc", NoteMerge.CombineText("a\nc", "a\nb\nc"));
    }

    [Fact]
    public void CombineText_VeryLongNotes_KeepEveryRepeatedLine()
    {
        // Past the size where lines are matched one by one, nothing may still be dropped:
        // blank lines and repeated bullets of the other version stay.
        var mine = string.Join("\n", Enumerable.Range(0, 2100).Select(i => $"mine {i}"));
        var other = string.Join("\n", Enumerable.Range(0, 2100).Select(i => i % 2 == 0 ? "" : "- [ ] same"));

        var combined = NoteMerge.CombineText(mine, other).Split('\n');

        Assert.Equal(1050, combined.Count(l => l == ""));
        Assert.Equal(1050, combined.Count(l => l == "- [ ] same"));
        Assert.Equal(2100, combined.Count(l => l.StartsWith("mine ")));
    }

    [Theory]
    [InlineData("", "other", "other")]
    [InlineData("mine", "", "mine")]
    [InlineData("  ", "other", "other")]
    public void CombineText_OneSideEmpty_IsTheOther(string mine, string other, string expected)
    {
        Assert.Equal(expected, NoteMerge.CombineText(mine, other));
    }

    [Fact]
    public void Combine_KeepsMyHeaderAndJoinsTheTags()
    {
        var mine = new Note
        {
            Title = "Mine", Project = "", Priority = NotePriority.High, Status = NoteStatus.Open,
            Tags = ["a", "b"], Created = new DateTime(2026, 11, 20, 9, 0, 0), Body = "x", FilePath = @"C:\n\mine.md",
        };
        var other = new Note
        {
            Title = "Other", Project = "A1i", Priority = NotePriority.Low, Status = NoteStatus.Done,
            Tags = ["b", "c"], Created = new DateTime(2026, 11, 19, 9, 0, 0), Body = "y", Repo = "repo",
        };

        var combined = NoteMerge.Combine(mine, other);

        Assert.Equal("Mine", combined.Title);
        Assert.Equal("A1i", combined.Project); // mine had none
        Assert.Equal(NotePriority.High, combined.Priority);
        Assert.Equal(NoteStatus.Open, combined.Status);
        Assert.Equal(["a", "b", "c"], combined.Tags);
        Assert.Equal(new DateTime(2026, 11, 19, 9, 0, 0), combined.Created);
        Assert.Equal("repo", combined.Repo);
        Assert.Equal("x\ny", combined.Body);
        Assert.Equal(@"C:\n\mine.md", combined.FilePath);
    }
}
