using System.Text.Json.Serialization;

namespace JLNotes.Models;

/// <summary>A project in the list every PC shares (projects.json): its name and color.</summary>
public class ProjectInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    // From before 1.3.9, when a project's repo folder was one value for every PC. Kept
    // as it was written, so a PC still on an older version keeps working, and never
    // written now: each PC keeps its own (AppSettings.ProjectRepos). ProjectCatalog.RepoOf
    // is its one reader.
    [JsonPropertyName("repo")]
    public string Repo { get; set; } = "";

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#4a9eff";
}
