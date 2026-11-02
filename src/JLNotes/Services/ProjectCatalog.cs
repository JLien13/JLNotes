using System.Collections.ObjectModel;
using System.Windows.Media;
using JLNotes.Helpers;
using JLNotes.Models;

namespace JLNotes.Services;

/// <summary>
/// The one list of projects and the one rule for comparing their names. A project
/// exists if it is registered in projects.json or any note uses it, and names that
/// differ only by case are the same project. Header, split pane picker, filter,
/// grouping, colors and repo lookup all ask here.
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

    /// <summary>Header dropdown: every project, after the "all projects" entry.</summary>
    public ObservableCollection<string> FilterChoices { get; } = [AllProjects];

    /// <summary>Split pane picker: the same projects, after the "no project" entry.</summary>
    public ObservableCollection<string> PickerChoices { get; } = [NoProject];

    /// <param name="registered">Projects from projects.json.</param>
    /// <param name="notes">Every note, wherever it is filtered to.</param>
    /// <param name="selectedFilter">Stays listed even with no notes left, so the
    /// header dropdown never loses its current selection.</param>
    public void Refresh(IReadOnlyList<ProjectInfo> registered, IEnumerable<Note> notes, string selectedFilter)
    {
        _registered = registered.GroupBy(p => p.Name, Comparer).ToDictionary(g => g.Key, g => g.First(), Comparer);
        _brushes = _registered.Values
            .Select(p => (p.Name, Brush: ParseBrush(p.Color)))
            .Where(p => p.Brush != null)
            .ToDictionary(p => p.Name, p => p.Brush!, Comparer);

        // The first spelling added wins, and the note spellings go in a fixed order,
        // so VG-Fleet and vg-fleet settle on one name that does not change between refreshes.
        var names = new HashSet<string>(Comparer);
        if (!string.IsNullOrWhiteSpace(selectedFilter))
            names.Add(selectedFilter);
        names.UnionWith(_registered.Keys);
        names.UnionWith(notes.Select(n => n.Project).Where(p => !string.IsNullOrWhiteSpace(p)).Order(StringComparer.Ordinal));
        names.Remove(AllProjects);
        names.Remove(NoProject);
        _names = names;

        var sorted = names.Order(Comparer).ToList();
        FilterChoices.SyncTo([AllProjects, .. sorted]);
        PickerChoices.SyncTo([NoProject, .. sorted]);
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

    /// <summary>The repo folder registered for the project, or "" if none.</summary>
    public string RepoOf(string project) => _registered.GetValueOrDefault(project)?.Repo ?? "";

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
