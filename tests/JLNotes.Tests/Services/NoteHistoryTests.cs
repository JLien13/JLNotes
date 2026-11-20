using System.IO;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.Tests.Services;

// Nothing the app, the jln command or an outside editor does to a note is lost:
// the file as it was goes into the history folder before it is overwritten or
// removed, pictures included, and any kept version can be put back.
public class NoteHistoryTests : IDisposable
{
    private readonly string _root;
    private readonly string _notesDir;
    private readonly NoteService _service;

    public NoteHistoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"jlnotes-history-{Guid.NewGuid()}");
        _notesDir = Path.Combine(_root, "notes");
        _service = new NoteService(_notesDir);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private Note SaveNew(string body)
    {
        var note = new Note { Title = "Plan", Body = body, Created = new DateTime(2026, 11, 19) };
        _service.Save(note);
        return note;
    }

    private static string BodyOf(NoteVersion version) => version.Read().Body;

    [Fact]
    public void Save_OverAnEditedNote_KeepsTheOldFile()
    {
        var note = SaveNew("one");
        note.Body = "two";
        _service.Save(note);

        var version = Assert.Single(_service.GetVersions(note.FilePath));
        Assert.Equal("one", BodyOf(version));
    }

    [Fact]
    public void Save_WithNothingChanged_KeepsNothing()
    {
        var note = SaveNew("one");
        _service.Save(note, stampUpdated: false);

        Assert.Empty(_service.GetVersions(note.FilePath));
    }

    [Fact]
    public void GetVersions_IsNewestFirst()
    {
        var note = SaveNew("one");
        note.Body = "two";
        _service.Save(note);
        note.Body = "three";
        _service.Save(note);

        Assert.Equal(["two", "one"], _service.GetVersions(note.FilePath).Select(BodyOf));
    }

    [Fact]
    public void Delete_KeepsTheNote_AndRestorePutsItBackUnderItsOwnName()
    {
        var note = SaveNew("keep me");
        var path = note.FilePath;
        var original = File.ReadAllText(path);

        var kept = _service.Delete(note);
        Assert.False(File.Exists(path));
        Assert.Equal(path, Assert.Single(_service.GetDeleted()).NotePath);

        _service.Restore(kept!);
        Assert.Equal(original, File.ReadAllText(path));
        Assert.Empty(_service.GetDeleted());
    }

    [Fact]
    public void Delete_KeepsThePictures_AndRestoreBringsThemBack()
    {
        var note = SaveNew("");
        var picture = Path.Combine(_root, "pic.png");
        File.WriteAllBytes(picture, [1, 2, 3]);
        var name = _service.AddAttachment(note, picture);
        note.Body = $"{{{{{name}}}}}";
        _service.Save(note);
        var live = Path.Combine(_service.GetAttachmentsDir(note), name);

        var kept = _service.Delete(note);
        Assert.False(File.Exists(live));

        _service.Restore(kept!);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(live));
    }

    [Fact]
    public void Save_ThatDropsAPicture_KeepsIt_SoAnOlderVersionStillHasIt()
    {
        var note = SaveNew("");
        var picture = Path.Combine(_root, "pic.png");
        File.WriteAllBytes(picture, [4, 5, 6]);
        var name = _service.AddAttachment(note, picture);
        note.Body = $"look {{{{{name}}}}}";
        _service.Save(note);
        var live = Path.Combine(_service.GetAttachmentsDir(note), name);

        note.Body = "picture removed";
        _service.Save(note);
        Assert.False(File.Exists(live));

        _service.Restore(_service.GetVersions(note.FilePath)[0]);
        Assert.Equal([4, 5, 6], File.ReadAllBytes(live));
    }

    [Fact]
    public void Restore_KeepsTheVersionItReplaces_SoARestoreCanBeUndone()
    {
        var note = SaveNew("one");
        note.Body = "two";
        _service.Save(note);

        _service.Restore(_service.GetVersions(note.FilePath).Single());

        Assert.Equal("one", Note.ParseFromMarkdown(File.ReadAllText(note.FilePath), note.FilePath).Body);
        Assert.Equal(["two", "one"], _service.GetVersions(note.FilePath).Select(BodyOf));
    }

    [Fact]
    public void LoadAll_AfterAnOutsideEdit_KeepsWhatWasThereBefore()
    {
        var note = SaveNew("before");
        _service.LoadAll();
        File.WriteAllText(note.FilePath, File.ReadAllText(note.FilePath).Replace("before", "after"));

        _service.LoadAll();

        Assert.Equal("before", BodyOf(Assert.Single(_service.GetVersions(note.FilePath))));
    }

    [Fact]
    public void LoadAll_AfterAnOutsideDelete_ListsTheNoteAsDeleted()
    {
        var note = SaveNew("gone");
        _service.LoadAll();
        File.Delete(note.FilePath);

        _service.LoadAll();

        Assert.Equal("gone", BodyOf(Assert.Single(_service.GetDeleted())));
    }

    [Fact]
    public void ANewNote_NeverTakesTheNameOfADeletedOne()
    {
        // Same title, same day: the new note would get the deleted note's file name
        // and inherit its history, and the deleted note would drop off the list.
        var first = SaveNew("one");
        var path = first.FilePath;
        _service.Delete(first);

        var second = SaveNew("two");

        Assert.NotEqual(path, second.FilePath);
        Assert.Equal(path, Assert.Single(_service.GetDeleted()).NotePath);
    }

    [Fact]
    public void TwoQuickOutsideWrites_LeaveTheLatestKeptVersionNewest()
    {
        var note = SaveNew("A");
        _service.LoadAll();
        using var other = new NoteService(_notesDir); // jln, run twice
        var copy = other.LoadAll().Single();
        copy.Body = "B";
        other.Save(copy);
        copy.Body = "C";
        other.Save(copy);

        _service.LoadAll(); // the app saw A, now sees C

        Assert.Equal(["B", "A"], _service.GetVersions(note.FilePath).Select(BodyOf));
    }

    [Fact]
    public void Save_OverAChangeThisAppNeverSaw_KeepsItAndSaysSo()
    {
        var note = SaveNew("mine");
        File.WriteAllText(note.FilePath, File.ReadAllText(note.FilePath).Replace("mine", "theirs"));
        var reported = new List<string>();
        _service.ClashFound += n => reported.Add(n.FilePath);

        note.Body = "mine, edited";
        _service.Save(note);

        Assert.Equal([note.FilePath], reported);
        Assert.Equal("theirs", BodyOf(_service.GetVersions(note.FilePath)[0]));
        Assert.Equal("theirs", BodyOf(Assert.Single(_service.GetClashes(note.FilePath))));
    }

    [Fact]
    public void Save_OverItsOwnLastWrite_SaysNothing()
    {
        var note = SaveNew("one");
        var reported = 0;
        _service.ClashFound += _ => reported++;

        note.Body = "two";
        _service.Save(note);

        Assert.Equal(0, reported);
    }

    [Fact]
    public void Reordering_KeepsNoVersion()
    {
        var note = SaveNew("one");
        note.SortOrder = 3;
        _service.Save(note, stampUpdated: false);

        Assert.Empty(_service.GetVersions(note.FilePath));
    }

    [Fact]
    public void Reordering_OverAnOutsideChange_StillKeepsIt()
    {
        var note = SaveNew("mine");
        File.WriteAllText(note.FilePath, File.ReadAllText(note.FilePath).Replace("mine", "theirs"));

        note.SortOrder = 3;
        _service.Save(note, stampUpdated: false);

        Assert.Equal("theirs", BodyOf(Assert.Single(_service.GetVersions(note.FilePath))));
    }

    [Fact]
    public void AddAttachment_NeverReusesTheNameOfAKeptPicture()
    {
        // Otherwise restoring the first version would show the second picture.
        var note = SaveNew("");
        var picture = Path.Combine(_root, "shot.png");
        File.WriteAllBytes(picture, [1]);
        var first = _service.AddAttachment(note, picture);
        note.Body = $"{{{{{first}}}}}";
        _service.Save(note);
        note.Body = "";
        _service.Save(note);

        File.WriteAllBytes(picture, [2]);
        var second = _service.AddAttachment(note, picture);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TheSameContent_IsNeverKeptTwice()
    {
        // The jln command keeps the old file when it saves; the app, which saw the
        // old file earlier, must not keep it a second time when it notices the change.
        var note = SaveNew("one");
        _service.LoadAll();
        using var other = new NoteService(_notesDir);
        var copy = other.LoadAll().Single();
        copy.Body = "two";
        other.Save(copy);

        _service.LoadAll();

        Assert.Single(_service.GetVersions(note.FilePath));
    }
}
