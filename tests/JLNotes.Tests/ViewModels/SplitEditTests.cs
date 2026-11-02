using System.IO;
using System.Linq;
using System.Windows.Documents;
using JLNotes.Models;
using JLNotes.Services;
using JLNotes.ViewModels;

namespace JLNotes.Tests.ViewModels;

// Covers the always-editable split detail pane's in-place save logic
// (LoadEditor / SaveEditor). Plain-text bodies only, so the WPF
// FlowDocument objects stay creatable on the test (MTA) thread.
public class SplitEditTests : IDisposable
{
    private readonly string _testDir;
    private readonly NoteService _noteService;
    private readonly SettingsService _settingsService;
    private readonly ProjectService _projectService;

    public SplitEditTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"jlnotes-splitedit-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_testDir, "notes"));
        _noteService = new NoteService(Path.Combine(_testDir, "notes"));
        _settingsService = new SettingsService(_testDir);
        _projectService = new ProjectService(_testDir);
    }

    public void Dispose()
    {
        _noteService.Dispose();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private NoteItemViewModel SavedVm(string title, string body)
    {
        var note = new Note { Title = title, Body = body, Created = new DateTime(2026, 6, 12) };
        _noteService.Save(note);
        return new NoteItemViewModel(note, _noteService, _settingsService, new ProjectCatalog());
    }

    [Fact]
    public void Commit_BeforePrime_IsNoOp()
    {
        var vm = SavedVm("Title", "body");

        vm.SaveEditor(); // never primed

        Assert.Equal("Title", vm.Note.Title);
        Assert.Equal("body", vm.Note.Body);
    }

    [Fact]
    public void Commit_WithoutEditing_LeavesNoteUntouched()
    {
        // Also proves the BuildDocument -> SerializeDocument round-trip is stable,
        // which is what keeps mere browsing from rewriting files.
        var vm = SavedVm("Title A", "line one\nline two");

        vm.LoadEditor();
        vm.SaveEditor();

        Assert.Equal("Title A", vm.Note.Title);
        Assert.Equal("line one\nline two", vm.Note.Body);
    }

    [Fact]
    public void Commit_AfterTitleEdit_PersistsToDisk()
    {
        var vm = SavedVm("Old", "body");

        vm.LoadEditor();
        vm.EditTitle = "New Title";
        vm.SaveEditor();

        Assert.Equal("New Title", vm.Note.Title);
        Assert.Equal("New Title", _noteService.LoadAll().Single().Title);
    }

    [Fact]
    public void Commit_AfterBodyEdit_PersistsToDisk()
    {
        var vm = SavedVm("T", "original");

        vm.LoadEditor();
        // Mutate the live edit document the way the bound RichTextBox would.
        vm.EditDocument!.Blocks.Clear();
        vm.EditDocument.Blocks.Add(new Paragraph(new Run("changed text")));
        vm.SaveEditor();

        Assert.Equal("changed text", vm.Note.Body);
        Assert.Equal("changed text", _noteService.LoadAll().Single().Body);
    }

    private MainViewModel SplitView()
    {
        var vm = new MainViewModel(_noteService, _projectService, _settingsService);
        vm.ViewMode = "split";
        return vm;
    }

    [Fact]
    public void SelectMode_WorksInSplitView()
    {
        SavedVm("One", "body");
        SavedVm("Two", "body");
        var vm = SplitView();

        vm.IsSelectMode = true;
        Assert.All(vm.SplitNotes, row => Assert.True(row.IsSelectMode));
        vm.SplitNotes[0].IsSelected = true;

        Assert.Equal(1, vm.SelectedCount);
    }

    [Fact]
    public void CtrlS_SavesTheSelectedNoteInSplitView()
    {
        SavedVm("Before", "body");
        var vm = SplitView();

        vm.SelectedSplitNote!.EditTitle = "After";
        vm.SaveOpenEditors();

        Assert.Equal("After", _noteService.LoadAll().Single().Title);
    }

    [Fact]
    public void ANote_KeepsOneViewModel_AcrossRefreshesAndLayouts()
    {
        SavedVm("Only", "body");
        var vm = SplitView();
        var item = vm.SplitNotes.Single();

        vm.RefreshNotes();
        Assert.Same(item, vm.SplitNotes.Single());

        vm.ViewMode = "list";
        Assert.Same(item, vm.MediumPriority.Single());
        Assert.Same(item, vm.SplitNotes.Single());
    }

    [Fact]
    public void ARefresh_KeepsUnsavedEdits_AndFollowsOutsideChangesWhenThereAreNone()
    {
        SavedVm("Title", "body");
        var vm = SplitView();
        var item = vm.SelectedSplitNote!;

        File.WriteAllText(item.Note.FilePath, "---\ntitle: Changed outside\n---\n\nbody");
        vm.RefreshNotes();
        Assert.Equal("Changed outside", item.EditTitle);

        item.EditTitle = "Typed by me";
        File.WriteAllText(item.Note.FilePath, "---\ntitle: Changed again\n---\n\nbody");
        vm.RefreshNotes();
        Assert.Equal("Typed by me", item.EditTitle);
    }
}
