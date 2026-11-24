using System.IO;
using JLNotes.Models;
using JLNotes.Services;
using JLNotes.ViewModels;

namespace JLNotes.Tests.Services;

// One list of projects for the header and for Settings alike, and a repo folder for a
// project that each PC keeps for itself: the notes folder is shared between PCs, and the
// same repo sits in a different place on each.
public class ProjectCatalogTests : IDisposable
{
    private readonly string _dir;
    private readonly ProjectCatalog _catalog = new();
    private static readonly Dictionary<string, string> NoRepos = [];

    public ProjectCatalogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"jlnotes-catalog-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private static Note In(string project) => new() { Title = "T", Project = project };

    [Fact]
    public void Rows_ListEveryProject_RegisteredOrUsedByANote()
    {
        _catalog.Refresh([new ProjectInfo { Name = "A1i" }, new ProjectInfo { Name = "Empty" }], NoRepos,
            [In("A1i"), In("HQ"), In("vg-fleet"), In("VG-Fleet"), In("")], ProjectCatalog.AllProjects);

        Assert.Equal([("A1i", 1), ("Empty", 0), ("HQ", 1), ("VG-Fleet", 2)], _catalog.Rows.Select(r => (r.Name, r.Notes)));
        Assert.Equal(_catalog.FilterChoices.Skip(1), _catalog.Rows.Select(r => r.Name)); // the header's own list
    }

    [Fact]
    public void Rows_LeaveOutASelectionTheHeaderOnlyKeepsListed()
    {
        _catalog.Refresh([], NoRepos, [In("HQ")], selectedFilter: "Gone");

        Assert.Contains("Gone", _catalog.FilterChoices);
        Assert.Equal(["HQ"], _catalog.Rows.Select(r => r.Name));
    }

    [Fact]
    public void RepoOf_IsTheFolderPickedOnThisPc()
    {
        // The shared list still names a folder, from the PC that set it before 1.3.9.
        _catalog.Refresh([new ProjectInfo { Name = "A1i", Repo = _dir }],
            new Dictionary<string, string> { ["a1i"] = @"C:\picked on this PC" }, [], ProjectCatalog.AllProjects);

        Assert.Equal(@"C:\picked on this PC", _catalog.RepoOf("A1i"));
        Assert.Equal(@"C:\picked on this PC", _catalog.Rows.Single().Repo);
    }

    [Fact]
    public void RepoOf_AFolderFromTheSharedList_CountsOnlyOnAPcThatHasIt()
    {
        var onAnotherPc = Path.Combine(_dir, "only on another PC");
        _catalog.Refresh([new ProjectInfo { Name = "Here", Repo = _dir }, new ProjectInfo { Name = "There", Repo = onAnotherPc }],
            NoRepos, [], ProjectCatalog.AllProjects);

        Assert.Equal(_dir, _catalog.RepoOf("Here"));
        Assert.Equal("", _catalog.RepoOf("There"));
        Assert.Equal("No repo folder on this PC", _catalog.Rows.Single(r => r.Name == "There").RepoText);
        Assert.Equal("", _catalog.RepoOf("Never heard of it"));
    }

    [Fact]
    public void SetProjectRepo_KeepsOneFolderPerProject_InThisPcsSettingsOnly()
    {
        var settings = new SettingsService(_dir);
        settings.Save(new AppSettings { Theme = "light" });

        settings.SetProjectRepo("VG-Fleet", @"C:\one");
        settings.SetProjectRepo("vg-fleet", @"C:\two"); // the same project, spelled another way
        settings.SetProjectRepo("HQ", @"C:\hq");
        settings.SetProjectRepo("HQ", "");              // cleared

        var saved = settings.Load();
        Assert.Equal(new Dictionary<string, string> { ["vg-fleet"] = @"C:\two" }, saved.ProjectRepos);
        Assert.Equal("light", saved.Theme);
        Assert.False(File.Exists(Path.Combine(_dir, "projects.json"))); // the shared list is not written
    }

    [Fact]
    public void TheMainView_ListsEachProject_WithThisPcsRepoFolder()
    {
        using var notes = new NoteService(Path.Combine(_dir, "notes"));
        notes.Save(new Note { Title = "One", Project = "HQ" });
        var projects = new ProjectService(_dir);
        projects.Add("A1i");
        var settings = new SettingsService(_dir);
        settings.SetProjectRepo("HQ", _dir);

        var vm = new MainViewModel(notes, projects, settings);

        Assert.Equal([("A1i", "", 0), ("HQ", _dir, 1)], vm.ProjectCatalog.Rows.Select(r => (r.Name, r.Repo, r.Notes)));

        settings.SetProjectRepo("A1i", _dir);
        vm.RefreshNotes(); // what Settings asks for after each change

        Assert.Equal(_dir, vm.ProjectCatalog.RepoOf("a1i"));
    }
}
