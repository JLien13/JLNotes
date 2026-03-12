using JLNotes.Models;

namespace JLNotes.Tests.Models;

public class NoteParsingTests
{
    private const string SampleNote = """
        ---
        title: Fix PPG scroll bug
        project: VasoGuard
        priority: high
        status: open
        tags: [bugfix, ppg]
        created: 2026-03-11T14:32:00
        updated: 2026-03-11T14:32:00
        repo: C:\Users\JL\Desktop\Vaso Git\temp-repo
        branch: fix/scroll-bug
        ---

        ## Context
        The scroll position is wrong.

        ## Related Files
        - `PPGView.cs:348`

        ## Next Steps
        - Fix the bug

        ## Notes
        Part of IN-1722.
        """;

    [Fact]
    public void ParseFromMarkdown_ParsesTitle()
    {
        var note = Note.ParseFromMarkdown(SampleNote, "test.md");
        Assert.Equal("Fix PPG scroll bug", note.Title);
    }

    [Fact]
    public void ParseFromMarkdown_ParsesPriority()
    {
        var note = Note.ParseFromMarkdown(SampleNote, "test.md");
        Assert.Equal(NotePriority.High, note.Priority);
    }

    [Fact]
    public void ParseFromMarkdown_ParsesProject()
    {
        var note = Note.ParseFromMarkdown(SampleNote, "test.md");
        Assert.Equal("VasoGuard", note.Project);
    }

    [Fact]
    public void ParseFromMarkdown_ParsesStatus()
    {
        var note = Note.ParseFromMarkdown(SampleNote, "test.md");
        Assert.Equal(NoteStatus.Open, note.Status);
    }

    [Fact]
    public void ParseFromMarkdown_ParsesTags()
    {
        var note = Note.ParseFromMarkdown(SampleNote, "test.md");
        Assert.Equal(new[] { "bugfix", "ppg" }, note.Tags);
    }

    [Fact]
    public void ParseFromMarkdown_ParsesBody()
    {
        var note = Note.ParseFromMarkdown(SampleNote, "test.md");
        Assert.Contains("The scroll position is wrong.", note.Body);
    }

    [Fact]
    public void ParseFromMarkdown_StoresFilePath()
    {
        var note = Note.ParseFromMarkdown(SampleNote, "test.md");
        Assert.Equal("test.md", note.FilePath);
    }

    [Fact]
    public void ToMarkdown_RoundTrips()
    {
        var note = Note.ParseFromMarkdown(SampleNote, "test.md");
        var markdown = note.ToMarkdown();
        var reparsed = Note.ParseFromMarkdown(markdown, "test.md");
        Assert.Equal(note.Title, reparsed.Title);
        Assert.Equal(note.Priority, reparsed.Priority);
        Assert.Equal(note.Project, reparsed.Project);
        Assert.Equal(note.Status, reparsed.Status);
    }

    [Fact]
    public void GenerateFileName_UsesDateAndSlug()
    {
        var note = new Note
        {
            Title = "Fix PPG Scroll Bug!",
            Created = new DateTime(2026, 3, 11)
        };
        var fileName = note.GenerateFileName();
        Assert.Equal("2026-03-11-fix-ppg-scroll-bug.md", fileName);
    }
}
