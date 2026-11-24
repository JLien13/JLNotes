using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using JLNotes.Helpers;
using JLNotes.Models;

namespace JLNotes.Services;

/// <summary>One project as Settings lists it: its name, its repo folder on this PC
/// ("" if none) and how many notes are in it.</summary>
public sealed record ProjectRow(string Name, string Repo, int Notes)
{
    public string RepoText => Repo.Length > 0 ? Repo : "No repo folder on this PC";
}

/// <summary>
/// The one list of projects and the one rule for comparing their names. A project
/// exists if it is registered in projects.json or any note uses it, and names that
/// differ only by case are the same project. Header, split pane picker, filter,
/// grouping, colors, Settings and repo lookup all ask here.
/// </summary>
public sealed class ProjectCatalog
{
    /// <summary>Header entry meaning "do not filter by project".</summary>
    public const string AllProjects = "Projects";

    /// <summary>What a note with a blank project shows as.</summary>
    public const string NoProject = "No project";

    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    private HashSet<string> _names = new(Comparer);
    private Dictionary<string, ProjectInfo> _registered = new(Comparer);
    private Dictionary<string, Brush> _brushes = new(Comparer);
    private Dictionary<string, string> _reposOnThisPc = new(Comparer);

    /// <summary>Header dropdown: every project, after the "all projects" entry.</summary>
    public ObservableCollection<string> FilterChoices { get; } = [AllProjects];

    /// <summary>Split pane picker: the same projects, after the "no project" entry.</summary>
    public ObservableCollection<string> PickerChoices { get; } = [NoProject];

    /// <summary>Settings: the same projects, each with its repo folder on this PC.</summary>
    public IReadOnlyList<ProjectRow> Rows { get; private set; } = [];

    /// <param name="registered">Projects from projects.json.</param>
    /// <param name="reposOnThisPc">This PC's repo folder for each project (AppSettings.ProjectRepos).</param>
    /// <param name="notes">Every note, wherever it is filtered to.</param>
    /// <param name="selectedFilter">Stays listed even with no notes left, so the
    /// header dropdown never loses its current selection.</param>
    public void Refresh(IReadOnlyList<ProjectInfo> registered, IReadOnlyDictionary<string, string> reposOnThisPc,
        IEnumerable<Note> notes, string selectedFilter)
    {
        _registered = registered.GroupBy(p => p.Name, Comparer).ToDictionary(g => g.Key, g => g.First(), Comparer);
        _brushes = _registered.Values
            .Select(p => (p.Name, Brush: ParseBrush(p.Color)))
            .Where(p => p.Brush != null)
            .ToDictionary(p => p.Name, p => p.Brush!, Comparer);
        _reposOnThisPc = new(Comparer);
        foreach (var (project, folder) in reposOnThisPc)
            _reposOnThisPc.TryAdd(project, folder);

        // The first spelling added wins, and the note spellings go in a fixed order,
        // so VG-Fleet and vg-fleet settle on one name that does not change between refreshes.
        var used = notes.Select(n => n.Project).Where(p => !string.IsNullOrWhiteSpace(p)).Order(StringComparer.Ordinal).ToList();
        var noteCounts = used.GroupBy(p => p, Comparer).ToDictionary(g => g.Key, g => g.Count(), Comparer);
        var names = new HashSet<string>(Comparer);
        if (!string.IsNullOrWhiteSpace(selectedFilter))
            names.Add(selectedFilter);
        names.UnionWith(_registered.Keys);
        names.UnionWith(used);
        names.Remove(AllProjects);
        names.Remove(NoProject);
        _names = names;

        var sorted = names.Order(Comparer).ToList();
        FilterChoices.SyncTo([AllProjects, .. sorted]);
        PickerChoices.SyncTo([NoProject, .. sorted]);
        // Not a selection the header only keeps listed: that is no project any more.
        Rows = sorted.Where(name => _registered.ContainsKey(name) || noteCounts.ContainsKey(name))
            .Select(name => new ProjectRow(name, RepoOf(name), noteCounts.GetValueOrDefault(name)))
            .ToList();
    }

    public static bool Same(string? a, string? b) => Comparer.Equals(a, b);

    /// <summary>True if a note in <paramref name="project"/> passes the header filter.</summary>
    public static bool PassesFilter(string project, string selectedFilter) =>
        selectedFilter == AllProjects || Same(project, selectedFilter);

    /// <summary>The spelling every list shows for a note's project.</summary>
    public string DisplayName(string? project)
    {
        if (string.IsNullOrWhiteSpace(project)) return NoProject;
        return _names.TryGetValue(project, out var listed) ? listed : project;
    }

    /// <summary>The project's color, or null if it has none.</summary>
    public Brush? BrushOf(string project) => _brushes.GetValueOrDefault(project);

    /// <summary>
    /// The project's repo folder on this PC, or "" if none. The same repo sits in a
    /// different place on each PC, so each PC picks its own (Settings > Projects) and
    /// keeps it in its own settings. Before 1.3.9 it was one value in the shared list:
    /// that one still counts on a PC that has the folder and has picked none of its own.
    /// </summary>
    public string RepoOf(string project)
    {
        if (_reposOnThisPc.GetValueOrDefault(project) is { Length: > 0 } picked) return picked;
        var shared = _registered.GetValueOrDefault(project)?.Repo;
        return !string.IsNullOrEmpty(shared) && Directory.Exists(shared) ? shared : "";
    }

    private static Brush? ParseBrush(string? color)
    {
        if (string.IsNullOrEmpty(color)) return null;
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
