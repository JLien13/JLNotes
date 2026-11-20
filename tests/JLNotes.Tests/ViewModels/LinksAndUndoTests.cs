using System.IO;
using JLNotes.Models;
using JLNotes.Services;
using JLNotes.ViewModels;

namespace JLNotes.Tests.ViewModels;

// In the app: a note shows who links to it, a link brings its note up wherever it
// is filtered to, a delete can be undone at once, and an earlier version can be
// put back with the editor following it.
public class LinksAndUndoTests : IDisposable
{
    private readonly string _testDir;
    private readonly NoteService _noteService;
    private readonly SettingsService _settingsService;
    private readonly ProjectService _projectService;

    public LinksAndUndoTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"jlnotes-links-undo-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_testDir, "notes"));
        _noteService = new NoteService(Path.Combine(_testDir, "notes"));
        _settingsService = new SettingsService(_testDir);
        _projectService = new ProjectService(_testDir);
        var settings = _settingsService.Load();
        settings.ConfirmDelete = false; // no dialog in a test
        _settingsService.Save(settings);
    }

    public void Dispose()
    {
        _noteService.Dispose();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private Note Saved(string title, string body = "", string project = "", NoteStatus status = NoteStatus.Open)
    {
        var note = new Note { Title = title, Body = body, Project = project, Status = status, Created = new DateTime(2026, 11, 19) };
        _noteService.Save(note);
        return note;
    }

    private static string Name(Note note) => Path.GetFileNameWithoutExtension(note.FilePath);

    private MainViewModel Vm(string viewMode)
    {
        var vm = new MainViewModel(_noteService, _projectService, _settingsService) { ViewMode = viewMode };
        return vm;
    }

    private static NoteItemViewModel Item(MainViewModel vm, Note note) =>
        vm.SplitNotes.Concat(vm.SortedByDate).Concat(vm.HighPriority).Concat(vm.MediumPriority).Concat(vm.LowPriority)
            .First(i => i.Note.FilePath == note.FilePath);

    [Fact]
    public void ANote_ListsTheNotesThatLinkToIt()
    {
        var target = Saved("Target");
        Saved("Source", $"see [[{Name(target)}]]");
        var vm = Vm("split");

        var item = Item(vm, target);

        Assert.Equal(["Source"], item.LinkedFrom.Select(n => n.Title));
        Assert.True(item.HasLinkedFrom);
    }

    [Fact]
    public void ShowNote_InSplitView_SelectsIt_ClearingFiltersThatHideIt()
    {
        var target = Saved("Target", project: "VasoGuard", status: NoteStatus.Done);
        Saved("Source", $"see [[{Name(target)}]]", project: "A1i");
        var vm = Vm("split");
        vm.SelectedProject = "A1i";
        vm.StatusFilter = "open";
        vm.SearchText = "Source";

        vm.ShowNote(target);

        Assert.Equal(target.FilePath, vm.SelectedSplitNote?.Note.FilePath);
    }

    [Fact]
    public void OpenNote_FromTheLinkedFromList_ShowsThatNote()
    {
        var target = Saved("Target");
        var source = Saved("Source", $"see [[{Name(target)}]]");
        var vm = Vm("split");
        vm.SelectedSplitNote = Item(vm, target);

        vm.SelectedSplitNote!.OpenNoteCommand.Execute(vm.SelectedSplitNote.LinkedFrom.Single());

        Assert.Equal(source.FilePath, vm.SelectedSplitNote?.Note.FilePath);
    }

    [Fact]
    public void ShowNote_InListView_OpensItsCard()
    {
        var target = Saved("Target");
        var vm = Vm("list");

        vm.ShowNote(target);

        Assert.True(Item(vm, target).IsExpanded);
    }

    [Fact]
    public void Delete_OffersUndo_AndUndoBringsTheNoteBack()
    {
        var note = Saved("Keep me", "body");
        var vm = Vm("split");

        Item(vm, note).DeleteCommand.Execute(null);
        Assert.False(File.Exists(note.FilePath));
        Assert.Contains("Keep me", vm.NoticeText);

        vm.NoticeActionCommand.Execute(null);

        Assert.True(File.Exists(note.FilePath));
        Assert.Null(vm.NoticeText);
        Assert.Equal(note.FilePath, vm.SelectedSplitNote?.Note.FilePath);
    }

    [Fact]
    public void RestoreVersion_PutsTheOldTextBack_AndTheEditorFollows()
    {
        var note = Saved("Draft", "first");
        note.Body = "second";
        _noteService.Save(note);
        var vm = Vm("split");
        var item = Item(vm, note);
        vm.SelectedSplitNote = item;

        item.RestoreVersion(_noteService.GetVersions(note.FilePath).Single());

        item = Item(vm, note);
        Assert.Equal("first", item.Note.Body);
        Assert.Equal("first", JLNotes.Helpers.FlowDocumentHelper.SerializeDocument(item.EditDocument!));
    }

    [Fact]
    public void ASave_OverAChangeMadeOutsideTheApp_SaysSo()
    {
        // JL has the note open and is typing when Claude (or another PC) changes the
        // file. His save lands, the other version is held, and the app asks which to keep.
        var note = Saved("Plan", "mine");
        var vm = Vm("split");
        var item = Item(vm, note);
        vm.SelectedSplitNote = item;
        File.WriteAllText(note.FilePath, File.ReadAllText(note.FilePath).Replace("mine", "theirs"));

        item.EditTitle = "Plan v2";
        item.SaveEditor();

        Assert.Contains("Both versions are kept", vm.NoticeText);
        Assert.Equal("Choose", vm.NoticeActionText);
        Assert.True(item.HasClash);
    }
}
