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
