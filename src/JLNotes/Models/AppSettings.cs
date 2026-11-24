using System.Text.Json.Serialization;

namespace JLNotes.Models;

public class AppSettings
{
    // Auto-start intentionally lives ONLY in the HKCU Run registry value
    // (see SettingsService.Get/SetAutoStart) -- no json mirror to drift.

    [JsonPropertyName("panelPosition")]
    public WindowPosition PanelPosition { get; set; } = new();

    [JsonPropertyName("widgets")]
    public List<WidgetPosition> Widgets { get; set; } = [];

    [JsonPropertyName("customLabels")]
    public List<string> CustomLabels { get; set; } = [];

    [JsonPropertyName("statusFilter")]
    public string StatusFilter { get; set; } = "all";

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "dark";

    [JsonPropertyName("confirmDelete")]
    public bool ConfirmDelete { get; set; } = true;

    [JsonPropertyName("subtitleDisplay")]
    public string SubtitleDisplay { get; set; } = "project";

    [JsonPropertyName("closeBehavior")]
    public string CloseBehavior { get; set; } = "tray";

    [JsonPropertyName("groupByProject")]
    public bool GroupByProject { get; set; } = false;

    [JsonPropertyName("sortByDate")]
    public bool SortByDate { get; set; } = false;

    [JsonPropertyName("gridView")]
    public bool GridView { get; set; } = false;

    // Layout mode for the note area: "list" | "grid" | "split".
    // Empty = not yet set; MainViewModel migrates from the legacy gridView flag.
    [JsonPropertyName("viewMode")]
    public string ViewMode { get; set; } = "";

    // Width of the split view's note-list pane (left of the divider).
    // 0 (older json, or never saved) = keep the XAML default.
    [JsonPropertyName("splitListWidth")]
    public double SplitListWidth { get; set; }

    // Where this PC keeps its notes, pictures, history and projects (Settings > Notes
    // folder). Empty = this PC's own .jlnotes folder. Several PCs pointed at one
    // Dropbox folder share their notes. These settings themselves never move: window
    // places differ per PC. NotesFolder.DataDirFor is the one reader of this value.
    [JsonPropertyName("notesFolder")]
    public string NotesFolder { get; set; } = "";

    // The notes folder that could not be found at a start, when this PC's own folder was
    // used instead: once it is back, the app offers to go back to it. Empty otherwise.
    [JsonPropertyName("notesFolderAway")]
    public string NotesFolderAway { get; set; } = "";

    // This PC's repo folder for each project, by project name (Settings > Projects).
    // Here and not in the shared project list, because the same repo sits in a different
    // place on each PC. SettingsService.SetProjectRepo is the one writer of this value,
    // ProjectCatalog.RepoOf the one reader.
    [JsonPropertyName("projectRepos")]
    public Dictionary<string, string> ProjectRepos { get; set; } = [];
}

public class WindowPosition
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    // 0 (json from older versions, or never saved) = no stored size.
    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
}

public class WidgetPosition
{
    [JsonPropertyName("noteId")]
    public string NoteId { get; set; } = "";

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
}
