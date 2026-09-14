using System.IO;
using System.Linq;
using JLNotes.Models;
using JLNotes.Services;
using JLNotes.ViewModels;

namespace JLNotes.Tests.ViewModels;

// The list filter uses NoteSearch, and a tag chip click adds its "#tag" token
// to the search box through the same grammar.
public class SearchTests : IDisposable
{
    private readonly string _testDir;
    private readonly NoteService _noteService;
    private readonly SettingsService _settingsService;
    private readonly ProjectService _projectService;

    public SearchTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"jlnotes-search-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_testDir, "notes"));
        _noteService = new NoteService(Path.Combine(_testDir, "notes"));
        _settingsService = new SettingsService(_testDir);
        _projectService = new ProjectService(_testDir);
        _noteService.Save(new Note { Title = "Hokanson intel", Body = "b", Tags = ["hokanson", "intel"], Created = new DateTime(2026, 9, 1) });
        _noteService.Save(new Note { Title = "Only hokanson", Body = "b", Tags = ["hokanson"], Created = new DateTime(2026, 9, 2) });
        _noteService.Save(new Note { Title = "Mentions intel in body", Body = "intel", Tags = [], Created = new DateTime(2026, 9, 3) });
    }

    public void Dispose()
    {
        _noteService.Dispose();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private MainViewModel Vm()
    {
        var vm = new MainViewModel(_noteService, _projectService, _settingsService);
        vm.ViewMode = "split";
        return vm;
    }

    private static string[] Titles(MainViewModel vm) => vm.SplitNotes.Select(v => v.Title).OrderBy(t => t).ToArray();

    [Fact]
    public void Filter_TagToken_MatchesTagOnly()
    {
        var vm = Vm();
        vm.SearchText = "#intel";
        vm.RefreshNotes();

        Assert.Equal(["Hokanson intel"], Titles(vm));
    }

    [Fact]
    public void Filter_TwoTagTokens_RequireBoth()
    {
        var vm = Vm();
        vm.SearchText = "#hokanson #intel";
        vm.RefreshNotes();

        Assert.Equal(["Hokanson intel"], Titles(vm));
    }

    [Fact]
    public void Filter_PlainText_StillMatchesBody()
    {
        var vm = Vm();
        vm.SearchText = "intel";
        vm.RefreshNotes();

        Assert.Equal(["Hokanson intel", "Mentions intel in body"], Titles(vm));
    }

    [Fact]
    public void AddTagToSearch_AppendsTokenOnce()
    {
        var vm = Vm();

        vm.AddTagToSearch("hokanson");
        vm.AddTagToSearch("intel");
        vm.AddTagToSearch("hokanson");

        Assert.Equal("#hokanson #intel", vm.SearchText);
    }
}
