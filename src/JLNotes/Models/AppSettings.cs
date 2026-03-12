using System.Text.Json.Serialization;

namespace JLNotes.Models;

public class AppSettings
{
    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; } = false;

    [JsonPropertyName("panelPosition")]
    public WindowPosition PanelPosition { get; set; } = new();

    [JsonPropertyName("widgets")]
    public List<WidgetPosition> Widgets { get; set; } = [];

    [JsonPropertyName("customLabels")]
    public List<string> CustomLabels { get; set; } = [];

    [JsonPropertyName("showCompleted")]
    public bool ShowCompleted { get; set; } = false;

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "dark";

    [JsonPropertyName("confirmDelete")]
    public bool ConfirmDelete { get; set; } = true;

    [JsonPropertyName("subtitleDisplay")]
    public string SubtitleDisplay { get; set; } = "project";

    [JsonPropertyName("closeBehavior")]
    public string CloseBehavior { get; set; } = "tray";
}

public class WindowPosition
{
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }
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
