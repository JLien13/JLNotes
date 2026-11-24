using System.IO;
using System.Text.Json;
using JLNotes.Converters;
using JLNotes.Helpers;
using JLNotes.Models;

namespace JLNotes.Services;

public class ProjectService
{
    private readonly string _projectsPath;

    // The list as this service last read it whole. Another PC may be writing the shared
    // file, and the sync tool may hold only part of it for a moment.
    private List<ProjectInfo> _lastWhole = [];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ProjectService(string baseDir)
    {
        _projectsPath = Path.Combine(baseDir, AppPaths.ProjectsFileName);
    }

    /// <summary>The list to show, with any conflicted copy the sync tool left taken in for
    /// good. While another PC is writing it and only part has arrived, the list as this
    /// service last read it whole.</summary>
    public List<ProjectInfo> Load()
    {
        try
        {
            var (projects, takenIn) = ReadWhole();
            if (takenIn.Count > 0)
                Write(projects, takenIn);
            _lastWhole = Copy(projects);
            return projects;
        }
        catch (IOException)
        {
            return Copy(_lastWhole);
        }
    }

    /// <summary>The list, read whole, changing nothing on disk: for a notes folder that must
    /// be left as it is (the old one, after a move). Throws if it is not whole.</summary>
    public List<ProjectInfo> Peek() => ReadWhole().List;

    public void Save(List<ProjectInfo> projects)
    {
        var json = JsonSerializer.Serialize(projects, JsonOptions);
        File.WriteAllText(_projectsPath, json);
    }

    // Each change is made to the list as it is on disk now, read whole, not as some window
    // read it earlier: another PC sharing the notes folder may have changed it meanwhile.
    // A list not whole (another PC is writing it) is never written over: the change throws.

    /// <summary>Adds a project, in a color no project in the list has yet. One already
    /// listed is left as it is.</summary>
    public void Add(string name) => Change(projects =>
    {
        if (projects.Any(p => ProjectCatalog.Same(p.Name, name))) return;
        projects.Add(new ProjectInfo { Name = name, Color = ColorPalette.ForNewProject(name, projects.Select(p => p.Color)) });
    });

    public void Remove(string name) => Change(projects => projects.RemoveAll(p => ProjectCatalog.Same(p.Name, name)));

    /// <summary>Adds each project the list lacks (by name). Where both have a project, the
    /// list's own details win.</summary>
    public void AddMissing(IEnumerable<ProjectInfo> more) => Change(projects => AddMissing(projects, more));

    private void Change(Action<List<ProjectInfo>> change)
    {
        var (projects, takenIn) = ReadWhole();
        change(projects);
        Write(projects, takenIn);
    }

    private void Write(List<ProjectInfo> projects, List<string> takenIn)
    {
        Save(projects);
        _lastWhole = Copy(projects);
        foreach (var copy in takenIn)
        {
            try
            {
                File.Delete(copy);
            }
            catch (IOException)
            {
                // The sync tool holds it: the next read takes it in again, which changes nothing.
            }
        }
    }

    private static void AddMissing(List<ProjectInfo> projects, IEnumerable<ProjectInfo> more) =>
        projects.AddRange(more.Where(p => !projects.Any(have => ProjectCatalog.Same(have.Name, p.Name))).ToList());

    /// <summary>A project list file; none is an empty list. One that is not whole says which
    /// file it is and why, as an IOException.</summary>
    private static List<ProjectInfo> Read(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ProjectInfo>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch (JsonException e)
        {
            throw new IOException($"The project list can't be read: {path}. Another PC may be saving it " +
                                  "(try again in a moment), or it was edited by hand and has a mistake in it.", e);
        }
    }

    /// <summary>
    /// The list read whole (throws if it is not), with the conflicted copies it took in.
    /// The project list is shared by every PC using the notes folder. If two changed it at
    /// once, the sync tool kept the other list as a conflicted copy: every project in it
    /// that this list lacks is added (this list's details win for the rest). A project
    /// removed on one PC may come back; none is ever lost. A copy not yet whole is left
    /// for a later read.
    /// </summary>
    private (List<ProjectInfo> List, List<string> TakenIn) ReadWhole()
    {
        var projects = Read(_projectsPath);
        var takenIn = new List<string>();
        foreach (var copy in ConflictedCopies())
        {
            try
            {
                AddMissing(projects, Read(copy));
                takenIn.Add(copy);
            }
            catch (IOException)
            {
                // Not whole yet: left for a later read.
            }
        }
        return (projects, takenIn);
    }

    private static List<ProjectInfo> Copy(List<ProjectInfo> projects) =>
        projects.Select(p => new ProjectInfo { Name = p.Name, Repo = p.Repo, Color = p.Color }).ToList();

    private List<string> ConflictedCopies()
    {
        var dir = Path.GetDirectoryName(_projectsPath)!;
        if (!Directory.Exists(dir)) return [];
        var stem = Path.GetFileNameWithoutExtension(_projectsPath);
        return Directory.GetFiles(dir, "*" + Path.GetExtension(_projectsPath))
            .Where(f => SyncConflictName.OriginalOf(Path.GetFileNameWithoutExtension(f)) is { } original
                        && original.Equals(stem, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
