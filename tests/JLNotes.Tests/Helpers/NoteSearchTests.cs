using JLNotes.Helpers;
using JLNotes.Models;

namespace JLNotes.Tests.Helpers;

public class NoteSearchTests
{
    private static Note N(string title, string body, params string[] tags) =>
        new() { Title = title, Body = body, Tags = [.. tags] };

    private static bool Hit(Note n, string query) => NoteSearch.Matches(n, NoteSearch.Parse(query));

    [Fact]
    public void Parse_SplitsTagTokensFromText()
    {
        var q = NoteSearch.Parse("  #intel foo #va-bids bar ");
        Assert.Equal(["intel", "va-bids"], q.Tags);
        Assert.Equal("foo bar", q.Text);
    }

    [Fact]
    public void Parse_LoneHashIsText()
    {
        var q = NoteSearch.Parse("#");
        Assert.Empty(q.Tags);
        Assert.Equal("#", q.Text);
    }

    [Fact]
    public void EmptyQuery_MatchesEverything()
    {
        Assert.True(NoteSearch.Parse("").IsEmpty);
        Assert.True(Hit(N("a", "b"), ""));
        Assert.True(Hit(N("a", "b"), "   "));
    }

    [Fact]
    public void TagToken_MatchesTagExactly_NotBodyOrPrefix()
    {
        Assert.True(Hit(N("t", "body", "intel"), "#intel"));
        Assert.True(Hit(N("t", "body", "Intel"), "#intel"));
        Assert.False(Hit(N("t", "mentions intel in the body", "other"), "#intel"));
        Assert.False(Hit(N("t", "b", "intelligence"), "#intel"));
    }

    [Fact]
    public void TwoTagTokens_RequireBoth()
    {
        var both = N("t", "b", "hokanson", "intel");
        var one = N("t", "b", "hokanson");
        Assert.True(Hit(both, "#hokanson #intel"));
        Assert.False(Hit(one, "#hokanson #intel"));
    }

    [Fact]
    public void PlainText_StillMatchesTitleBodyOrTagsAsSubstring()
    {
        Assert.True(Hit(N("Hokanson deep dive", "", ""), "deep dive"));
        Assert.True(Hit(N("t", "the VascuLab build", ""), "vasculab"));
        Assert.True(Hit(N("t", "", "competitor"), "compet"));
        Assert.False(Hit(N("t", "b", "x"), "nothing"));
    }

    [Fact]
    public void TagsAndText_Combine()
    {
        Assert.True(Hit(N("Hokanson", "b", "intel"), "#intel hokan"));
        Assert.False(Hit(N("Hokanson", "b", "intel"), "#intel zzz"));
        Assert.False(Hit(N("Hokanson", "b", "other"), "#intel hokan"));
    }

    [Fact]
    public void AddTag_AppendsOnceAndKeepsText()
    {
        Assert.Equal("#intel", NoteSearch.AddTag("", "intel"));
        Assert.Equal("foo #intel", NoteSearch.AddTag("foo", "intel"));
        Assert.Equal("#intel #hokanson", NoteSearch.AddTag("#intel", "hokanson"));
        Assert.Equal("#intel", NoteSearch.AddTag("#intel", "Intel"));
    }
}
