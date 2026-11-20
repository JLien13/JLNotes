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

        Assert.ThrowsAny<Exception>(() => service.Add(new ProjectInfo { Name = "VasoGuard" }));
        Assert.Equal(partial, File.ReadAllText(Path.Combine(_dir, "projects.json")));
    }

    [Fact]
    public void Changes_AreMadeToTheListAsItIsNow_NotAsItWasWhenRead()
    {
        // Settings stays open while another PC adds a project: nothing it adds is lost.
        var openSettings = new ProjectService(_dir);
        openSettings.Save([new ProjectInfo { Name = "A1i" }]);
        new ProjectService(_dir).Add(new ProjectInfo { Name = "From the other PC" });

        openSettings.Add(new ProjectInfo { Name = "VasoGuard" });
        openSettings.SetRepo("A1i", @"C:\repo");
        openSettings.Remove("VasoGuard");

        var projects = openSettings.Load();
        Assert.Equal(["A1i", "From the other PC"], projects.Select(p => p.Name));
        Assert.Equal(@"C:\repo", projects[0].Repo);
    }
}
