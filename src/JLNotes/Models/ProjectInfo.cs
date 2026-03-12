using System.Text.Json.Serialization;

namespace JLNotes.Models;

public class ProjectInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("repo")]
    public string Repo { get; set; } = "";

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#4a9eff";
}
