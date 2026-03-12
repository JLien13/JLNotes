using System.IO;
using System.Text.Json;
using JLNotes.Models;

namespace JLNotes.Services;

public class ProjectService
{
    private readonly string _projectsPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ProjectService(string baseDir)
    {
        _projectsPath = Path.Combine(baseDir, "projects.json");
    }

    public List<ProjectInfo> Load()
    {
        if (!File.Exists(_projectsPath))
            return [];

        var json = File.ReadAllText(_projectsPath);
        return JsonSerializer.Deserialize<List<ProjectInfo>>(json, JsonOptions) ?? [];
    }

    public void Save(List<ProjectInfo> projects)
    {
        var json = JsonSerializer.Serialize(projects, JsonOptions);
        File.WriteAllText(_projectsPath, json);
    }
}
