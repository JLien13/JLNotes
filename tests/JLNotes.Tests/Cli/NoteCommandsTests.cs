using System.IO;
using JLNotes.Cli;
using JLNotes.Models;

namespace JLNotes.Tests.Cli;

// jln is how Claude (or any tool that runs commands) reads and writes notes. Every
// write goes through the app's own note format and save code, so a note written
// this way can never be one the app fails to show.
public class NoteCommandsTests : IDisposable
{
    private readonly string _root;
    private readonly string _notesDir;

    public NoteCommandsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"jlnotes-cli-{Guid.NewGuid()}");
        _notesDir = Path.Combine(_root, "notes");
        Directory.CreateDirectory(_notesDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private (int Code, string Out, string Err) JlnWithInput(string input, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = NoteCommands.Run(args, _notesDir, new StringReader(input), stdout, stderr);
        return (code, stdout.ToString().Trim(), stderr.ToString().Trim());
    }

    private (int Code, string Out, string Err) Jln(params string[] args) => JlnWithInput("", args);

    private static Note Read(string path) => Note.ParseFromMarkdown(File.ReadAllText(path), path);

    [Fact]
    public void New_SavesThroughTheNoteFormat_SoATitleWithAColonStillReads()
    {
        var (code, path, _) = Jln("new", "--title", "Call back: Dr. Lee", "--project", "A1i",
            "--priority", "high", "--tags", "sales, follow-up", "--body", "Ask about the quote.");

        Assert.Equal(0, code);
        var note = Read(path);
        Assert.Equal("Call back: Dr. Lee", note.Title);
        Assert.Equal("A1i", note.Project);
        Assert.Equal(NotePriority.High, note.Priority);
        Assert.Equal(["sales", "follow-up"], note.Tags);
        Assert.Equal("Ask about the quote.", note.Body);
    }

    [Fact]
    public void New_ReadsTheBodyFromInput_WhenGivenADash()
    {
        var (_, path, _) = JlnWithInput("line one\nline two\n", "new", "--title", "From input", "--body", "-");

        Assert.Equal("line one\nline two", Read(path).Body);
    }

    [Fact]
    public void New_WithoutATitle_FailsAndWritesNothing()
    {
        var (code, _, err) = Jln("new", "--body", "x");

        Assert.NotEqual(0, code);
        Assert.Contains("--title", err);
        Assert.Empty(Directory.GetFiles(_notesDir));
    }

    [Fact]
    public void Find_UsesTheAppsSearch_AndFilters()
    {
        Jln("new", "--title", "DICOM dates", "--project", "VasoGuard", "--tags", "bug");
        Jln("new", "--title", "DICOM send", "--project", "VasoGuard", "--status", "done", "--tags", "bug");
        Jln("new", "--title", "Quote for Lee", "--project", "A1i");

        var (_, all, _) = Jln("find", "#bug", "dicom");
        var (_, open, _) = Jln("find", "#bug", "--status", "open");
        var (_, a1i, _) = Jln("find", "--project", "a1i");

        Assert.Equal(2, all.Split('\n').Length);
        Assert.Contains("DICOM dates", open);
        Assert.DoesNotContain("DICOM send", open);
        Assert.Contains("Quote for Lee", a1i);
        Assert.DoesNotContain("DICOM", a1i);
    }

    [Fact]
    public void Show_PrintsThePathAndTheFile()
    {
        var (_, path, _) = Jln("new", "--title", "Show me", "--body", "the body");

        var (code, shown, _) = Jln("show", Path.GetFileNameWithoutExtension(path));

        Assert.Equal(0, code);
        Assert.StartsWith(path, shown);
        Assert.Contains("the body", shown);
    }

    [Fact]
    public void Set_ChangesOnlyWhatItIsGiven_AndKeepsTheFileName()
    {
        var (_, path, _) = Jln("new", "--title", "Old", "--tags", "a, b", "--body", "keep");

        var (code, after, _) = Jln("set", Path.GetFileName(path), "--title", "New: name",
            "--status", "done", "--add-tag", "c", "--remove-tag", "a");

        Assert.Equal(0, code);
        Assert.Equal(path, after);
        var note = Read(path);
        Assert.Equal("New: name", note.Title);
        Assert.Equal(NoteStatus.Done, note.Status);
        Assert.Equal(["b", "c"], note.Tags);
        Assert.Equal("keep", note.Body);
    }

    [Fact]
    public void Append_AddsALineToTheBody()
    {
        var (_, path, _) = Jln("new", "--title", "Log", "--body", "first");

        JlnWithInput("second\n", "append", Path.GetFileName(path), "--text", "-");

        Assert.Equal("first\nsecond", Read(path).Body);
    }

    [Fact]
    public void Links_ListsBothDirections()
    {
        var (_, target, _) = Jln("new", "--title", "Target");
        var name = Path.GetFileNameWithoutExtension(target);
        Jln("new", "--title", "Source", "--body", $"see [[{name}]]");

        var (_, links, _) = Jln("links", name);

        Assert.Contains("Linked from:", links);
        Assert.Contains("Source", links);
    }

    [Fact]
    public void History_AndRestore_PutAnEarlierVersionBack()
    {
        var (_, path, _) = Jln("new", "--title", "Draft", "--body", "first");
        Jln("set", Path.GetFileName(path), "--body", "second");

        var (_, history, _) = Jln("history", Path.GetFileName(path));
        var versionId = history.Split('\n')[0].Split(" | ")[0];
        var (code, _, _) = Jln("restore", Path.GetFileName(path), versionId);

        Assert.Equal(0, code);
        Assert.Equal("first", Read(path).Body);
    }

    [Fact]
    public void Restore_BringsBackADeletedNote_ByItsName()
    {
        var (_, path, _) = Jln("new", "--title", "Lost", "--body", "found again");
        using (var app = new JLNotes.Services.NoteService(_notesDir))
            app.Delete(Read(path)); // JL deletes it in the app

        var (_, deleted, _) = Jln("deleted");
        var (code, restored, _) = Jln("restore", Path.GetFileNameWithoutExtension(path));

        Assert.Contains("Lost", deleted);
        Assert.Equal(0, code);
        Assert.Equal(path, restored);
        Assert.Equal("found again", Read(path).Body);
    }

    [Fact]
    public void AnUnknownNote_IsAnError()
    {
        var (code, _, err) = Jln("show", "no-such-note");

        Assert.Equal(1, code);
        Assert.Contains("no-such-note", err);
    }

    [Fact]
    public void NoCommand_PrintsUsage()
    {
        var (code, _, err) = Jln();

        Assert.Equal(2, code);
        Assert.Contains("jln find", err);
    }

    [Fact]
    public void AnAmbiguousName_SaysSo_AndListsTheMatches()
    {
        using (var app = new JLNotes.Services.NoteService(_notesDir))
        {
            app.Save(new Note { Title = "Same", Created = new DateTime(2026, 1, 1) });
            app.Save(new Note { Title = "Same", Created = new DateTime(2026, 2, 1) });
        }

        var (code, _, err) = Jln("show", "same");

        Assert.Equal(1, code);
        Assert.Contains("2026-01-01-same", err);
        Assert.Contains("2026-02-01-same", err);
    }

    [Fact]
    public void AFileInUse_IsAPlainError()
    {
        var (_, path, _) = Jln("new", "--title", "Busy");
        using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) // another program has it open
        {
            var (code, _, err) = Jln("set", Path.GetFileName(path), "--status", "done");

            Assert.Equal(1, code);
            Assert.Contains("Busy", File.ReadAllText(path));
            Assert.DoesNotContain("   at ", err);
        }
    }

    [Fact]
    public void Where_PrintsTheNotesFolder()
    {
        // Tools that read note files themselves ask jln where they are, so moving the
        // notes to a shared folder in Settings never leaves them looking in the old place.
        var (code, output, _) = Jln("where");

        Assert.Equal(0, code);
        Assert.Equal(_root, output);
    }

    [Fact]
    public void Find_NeverListsASyncToolsConflictedCopyAsANote()
    {
        var (_, path, _) = Jln("new", "--title", "Plan", "--body", "from A");
        var copy = Path.Combine(_notesDir, Path.GetFileNameWithoutExtension(path) + " (JL's conflicted copy 2026-11-20).md");
        File.WriteAllText(copy, File.ReadAllText(path).Replace("from A", "from B"));

        var (_, output, _) = Jln("find");

        Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Restore_BringsBackADeletedNoteThatOnlyHasAWaitingVersion()
    {
        // Deleted on another PC while this one held its other version: "deleted" lists
        // it, so "restore" must find it too.
        var conflicts = Path.Combine(_root, "conflicts", "2026-11-20-plan");
        Directory.CreateDirectory(conflicts);
        File.WriteAllText(Path.Combine(conflicts, "2026-11-20_10-00-00-000.md"),
            new Note { Title = "Plan", Body = "held", Created = new DateTime(2026, 11, 20, 9, 0, 0) }.ToMarkdown());
        Assert.Contains("2026-11-20-plan", Jln("deleted").Out);

        var (code, _, err) = Jln("restore", "2026-11-20-plan");

        Assert.Equal(0, code);
        Assert.Equal("", err);
        Assert.Equal("held", Read(Path.Combine(_notesDir, "2026-11-20-plan.md")).Body);
    }

}
