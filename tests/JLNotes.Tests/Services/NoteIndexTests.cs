using System.IO;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.Tests.Services;

// Links between notes use Obsidian's grammar, [[target]] or [[target|text]], and a
// target names a note by its file name. File names never change after a note is
// made, so a link survives the note's title being edited.
public class NoteIndexTests
{
    private static Note At(string fileName, string title, string body = "") =>
        new() { Title = title, Body = body, FilePath = Path.Combine(@"C:\notes", fileName) };

    [Fact]
    public void LinkTargets_ReadsPlainAndLabelledLinks()
    {
        var note = At("2026-01-01-a.md", "A", "See [[2026-01-02-b]] and [[2026-01-03-c|the C note]].");

        Assert.Equal(["2026-01-02-b", "2026-01-03-c"], note.GetLinkTargets());
    }

    [Fact]
    public void LinkTargets_IgnoresEmptyBrackets()
    {
        var note = At("2026-01-01-a.md", "A", "Nothing here: [[]] or [[ ]].");

        Assert.Empty(note.GetLinkTargets());
    }

    [Fact]
    public void LinkText_IsTheFileNameWithoutExtension()
    {
        Assert.Equal("[[2026-01-02-b]]", At("2026-01-02-b.md", "B").GetLinkText());
    }

    [Fact]
    public void Resolve_FindsAFullFileName_IgnoringCaseAndExtension()
    {
        var b = At("2026-01-02-b.md", "B");
        var index = new NoteIndex();
        index.Refresh([At("2026-01-01-a.md", "A"), b]);

        Assert.Same(b, index.Resolve("2026-01-02-B"));
        Assert.Same(b, index.Resolve("2026-01-02-b.md"));
    }

    [Fact]
    public void Resolve_FindsAFileNameWithoutItsDate_WhenOnlyOneNoteHasIt()
    {
        var b = At("2026-01-02-market-data.md", "B");
        var index = new NoteIndex();
        index.Refresh([At("2026-01-01-a.md", "A"), b]);

        Assert.Same(b, index.Resolve("market-data"));
    }

    [Fact]
    public void Resolve_GivesNothing_WhenTwoNotesShareTheDatelessName()
    {
        var index = new NoteIndex();
        index.Refresh([At("2026-01-01-same.md", "One"), At("2026-02-01-same.md", "Two")]);

        Assert.Null(index.Resolve("same"));
    }

    [Fact]
    public void Resolve_FindsAFullPath()
    {
        var b = At("2026-01-02-b.md", "B");
        var index = new NoteIndex();
        index.Refresh([b]);

        Assert.Same(b, index.Resolve(@"C:\notes\2026-01-02-b.md"));
    }

    [Fact]
    public void Resolve_DoesNotMatchTitles()
    {
        var index = new NoteIndex();
        index.Refresh([At("2026-01-02-b.md", "Budget review")]);

        Assert.Null(index.Resolve("Budget review"));
    }

    [Fact]
    public void LinkedFrom_ListsEveryOtherNoteThatLinksHere_OnceEach()
    {
        var target = At("2026-01-02-b.md", "B");
        var one = At("2026-01-01-a.md", "A", "[[2026-01-02-b]] and again [[2026-01-02-b|B]]");
        var two = At("2026-01-03-c.md", "C", "see [[b]]");
        var self = At("2026-01-04-d.md", "D", "[[2026-01-04-d]]");
        var index = new NoteIndex();
        index.Refresh([target, one, two, self]);

        Assert.Equal([one, two], index.LinkedFrom(target));
        Assert.Empty(index.LinkedFrom(self));
    }

    [Fact]
    public void RequestOpen_RaisesTheResolvedNote_AndIgnoresAMissingOne()
    {
        var b = At("2026-01-02-b.md", "B");
        var index = new NoteIndex();
        index.Refresh([b]);
        var opened = new List<Note>();
        index.OpenRequested += opened.Add;

        index.RequestOpen("2026-01-02-b");
        index.RequestOpen("no-such-note");

        Assert.Equal([b], opened);
    }
}
