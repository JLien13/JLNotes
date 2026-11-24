using System.IO;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.Tests.Services;

// The project list is shared by every PC that uses the notes folder. If two PCs
// change it at once and the sync tool keeps a conflicted copy, no project is lost.
public class ProjectServiceTests : IDisposable
{
    private readonly string _dir;

    public ProjectServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"jlnotes-projects-{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    [Fact]
    public void Load_TakesInAConflictedCopy_KeepingEveryProject()
    {
        var service = new ProjectService(_dir);
        service.Save([new ProjectInfo { Name = "A1i" }, new ProjectInfo { Name = "VasoGuard", Color = "#111111" }]);
        var copy = Path.Combine(_dir, "projects (JL's conflicted copy 2026-11-20).json");
        File.WriteAllText(copy, """
            [
              { "name": "VasoGuard", "color": "#222222" },
              { "name": "CorVascular" }
            ]
            """);

        var projects = service.Load();

        Assert.Equal(["A1i", "VasoGuard", "CorVascular"], projects.Select(p => p.Name));
        Assert.Equal("#111111", projects.Single(p => p.Name == "VasoGuard").Color); // this PC's copy wins
        Assert.False(File.Exists(copy));
        Assert.Equal(3, service.Load().Count); // and it stays merged
    }

    [Fact]
    public void AListThatIsHalfSynced_ReadsAsItLastWasWhole()
    {
        // Another PC is writing the shared list and the sync tool has only part of it.
        var service = new ProjectService(_dir);
        service.Save([new ProjectInfo { Name = "A1i" }]);
        Assert.Single(service.Load());
        File.WriteAllText(Path.Combine(_dir, "projects.json"), "[ { \"name\": \"A1");

        Assert.Equal(["A1i"], service.Load().Select(p => p.Name));
        Assert.Empty(new ProjectService(_dir).Load()); // one never read whole: nothing yet
    }

    [Fact]
    public void AChange_ToAListThatIsNotWhole_IsRefused_AndWritesNothing()
    {
        // Another PC is writing the list; adding to what this PC last saw would drop theirs.
        var service = new ProjectService(_dir);
        service.Save([new ProjectInfo { Name = "A1i" }]);
        service.Load();
        const string partial = "[{\"name\":\"A1i\"},{\"name\":\"Wo";
        File.WriteAllText(Path.Combine(_dir, "projects.json"), partial);

        Assert.ThrowsAny<Exception>(() => service.Add("VasoGuard"));
        Assert.Equal(partial, File.ReadAllText(Path.Combine(_dir, "projects.json")));
    }

    [Fact]
    public void Changes_AreMadeToTheListAsItIsNow_NotAsItWasWhenRead()
    {
        // Settings stays open while another PC adds a project: nothing it adds is lost.
        var openSettings = new ProjectService(_dir);
        openSettings.Save([new ProjectInfo { Name = "A1i" }]);
        new ProjectService(_dir).Add("From the other PC");

        openSettings.Add("VasoGuard");
        openSettings.Add("HQ");
        openSettings.Remove("VasoGuard");

        Assert.Equal(["A1i", "From the other PC", "HQ"], openSettings.Load().Select(p => p.Name));
    }

    [Fact]
    public void Add_GivesEachProjectItsOwnColor_AndListsNoProjectTwice()
    {
        var service = new ProjectService(_dir);
        service.Add("VasoGuard");
        service.Add("A1i");
        service.Add("vasoguard"); // the same project: names differing only by case are one

        var projects = service.Load();
        Assert.Equal(["VasoGuard", "A1i"], projects.Select(p => p.Name));
        Assert.NotEqual(projects[0].Color, projects[1].Color);
    }

    [Fact]
    public void ARepoFolderFromAnOlderVersion_StaysInTheListAsItWasWritten()
    {
        // Before 1.3.9 the shared list held each project's repo folder. A PC still on an
        // older version reads it there, so a change to the list must not drop it.
        File.WriteAllText(Path.Combine(_dir, "projects.json"),
            """[ { "name": "A1i", "repo": "C:\\Users\\JL\\A1i", "color": "#ff6b4a" } ]""");
        var service = new ProjectService(_dir);

        service.Add("HQ");
        service.Remove("HQ");

        var kept = Assert.Single(service.Load());
        Assert.Equal(@"C:\Users\JL\A1i", kept.Repo);
    }
}
