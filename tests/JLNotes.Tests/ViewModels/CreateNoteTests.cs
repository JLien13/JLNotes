using System.IO;
using System.Linq;
using JLNotes.Models;
using JLNotes.Services;
using JLNotes.ViewModels;

namespace JLNotes.Tests.ViewModels;

// MainViewModel.CreateNote is the single create path for every view: it saves
// a real file at once (no modal), reveals the note in the current layout, and
// clears anything that would hide it.
public class CreateNoteTests : IDisposable
{
    private readonly string _testDir;
    private readonly NoteService _noteService;
    private readonly SettingsService _settingsService;
    private readonly ProjectService _projectService;

    public CreateNoteTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"jlnotes-create-{Guid.NewGuid()}");
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

    private MainViewModel Vm(string viewMode)
    {
        var vm = new MainViewModel(_noteService, _projectService, _settingsService);
        vm.ViewMode = viewMode;
        return vm;
    }

    [Fact]
    public void CreateNote_SavesAFileImmediately()
    {
        var vm = Vm("split");

        var item = vm.CreateNote();

        Assert.True(File.Exists(item.Note.FilePath));
        Assert.Single(_noteService.LoadAll());
    }

    [Fact]
    public void CreateNote_DefaultTitleIsTheCreationTimestamp()
    {
        var vm = Vm("split");

        var item = vm.CreateNote();

        Assert.Equal(MainViewModel.DefaultNewNoteTitle(item.Note.Created), item.Note.Title);
        Assert.False(string.IsNullOrWhiteSpace(item.Note.Title));
    }

    [Fact]
    public void CreateNote_InSplitView_SelectsTheNewNoteAndPrimesEditing()
    {
        var vm = Vm("split");

        var item = vm.CreateNote();

        Assert.NotNull(vm.SelectedSplitNote);
        Assert.Equal(item.Note.FilePath, vm.SelectedSplitNote!.Note.FilePath);
        Assert.Equal(item.Note.Title, vm.SelectedSplitNote.EditTitle); // BeginSplitEdit ran
    }

    [Fact]
    public void CreateNote_InListView_ExpandsTheNewNoteInline()
    {
        var vm = Vm("list");

        var item = vm.CreateNote();

        Assert.True(item.IsExpanded);
        Assert.Equal(item.Note.Title, item.EditTitle);
        Assert.Contains(vm.MediumPriority, v => v.Note.FilePath == item.Note.FilePath);
    }

    [Fact]
    public void CreateNote_UsesTheSelectedProject()
    {
        var vm = Vm("split");
        vm.SelectedProject = "CorVascular";

        var item = vm.CreateNote();

        Assert.Equal("CorVascular", item.Note.Project);
    }

    [Fact]
    public void CreateNote_WithAllProjectsSelected_LeavesProjectBlank()
    {
        var vm = Vm("split");
        vm.SelectedProject = "Projects";

        var item = vm.CreateNote();

        Assert.Equal("", item.Note.Project);
    }

    [Fact]
    public void CreateNote_ClearsSearchAndDoneFilterSoTheNoteIsVisible()
    {
        var vm = Vm("split");
        vm.SearchText = "nothing-matches-this";
        vm.StatusFilter = "done";

        var item = vm.CreateNote();

        Assert.Equal("", vm.SearchText);
        Assert.Equal("all", vm.StatusFilter);
        Assert.Contains(vm.SplitNotes, v => v.Note.FilePath == item.Note.FilePath);
    }

    [Fact]
    public void CreateNote_TwiceInTheSameMinute_MakesTwoFiles()
    {
        var vm = Vm("split");

        var a = vm.CreateNote();
        var b = vm.CreateNote();

        Assert.NotEqual(a.Note.FilePath, b.Note.FilePath);
        Assert.Equal(2, _noteService.LoadAll().Count);
    }
}
