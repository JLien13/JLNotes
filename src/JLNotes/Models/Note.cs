using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace JLNotes.Models;

public enum NotePriority { Low, Medium, High }
public enum NoteStatus { Open, Done }

public class Note
{
    public string Title { get; set; } = "";
    public string Project { get; set; } = "";
    public NotePriority Priority { get; set; } = NotePriority.Medium;
    public NoteStatus Status { get; set; } = NoteStatus.Open;
    public List<string> Tags { get; set; } = [];
    public DateTime Created { get; set; } = DateTime.Now;
    public DateTime Updated { get; set; } = DateTime.Now;
    public string Repo { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Body { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int SortOrder { get; set; } = 0;

    public static Note ParseFromMarkdown(string markdown, string filePath)
    {
        var lines = markdown.Split('\n');
        var inFrontmatter = false;
        var frontmatterLines = new List<string>();
        var bodyLines = new List<string>();
        var pastFrontmatter = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Trim() == "---" && !pastFrontmatter)
            {
                if (!inFrontmatter)
                    inFrontmatter = true;
                else
                {
                    inFrontmatter = false;
                    pastFrontmatter = true;
                }
                continue;
            }

            if (inFrontmatter)
                frontmatterLines.Add(trimmed);
            else if (pastFrontmatter)
                bodyLines.Add(trimmed);
        }

        var yaml = string.Join("\n", frontmatterLines);
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var frontmatter = deserializer.Deserialize<NoteFrontmatter>(yaml);

        return new Note
        {
            Title = frontmatter.Title ?? "",
            Project = frontmatter.Project ?? "",
            Priority = Enum.TryParse<NotePriority>(frontmatter.Priority, true, out var p) ? p : NotePriority.Medium,
            Status = Enum.TryParse<NoteStatus>(frontmatter.Status, true, out var s) ? s : NoteStatus.Open,
            Tags = frontmatter.Tags ?? [],
            Created = frontmatter.Created,
            Updated = frontmatter.Updated,
            Repo = frontmatter.Repo ?? "",
            Branch = frontmatter.Branch ?? "",
            Body = string.Join("\n", bodyLines).Trim(),
            FilePath = filePath
        };
    }

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"title: {Title}");
        sb.AppendLine($"project: {Project}");
        sb.AppendLine($"priority: {Priority.ToString().ToLower()}");
        sb.AppendLine($"status: {Status.ToString().ToLower()}");
        if (Tags.Count > 0)
            sb.AppendLine($"tags: [{string.Join(", ", Tags)}]");
        else
            sb.AppendLine("tags: []");
        sb.AppendLine($"created: {Created:yyyy-MM-ddTHH:mm:ss}");
        sb.AppendLine($"updated: {Updated:yyyy-MM-ddTHH:mm:ss}");
        if (!string.IsNullOrEmpty(Repo))
            sb.AppendLine($"repo: {Repo}");
        if (!string.IsNullOrEmpty(Branch))
            sb.AppendLine($"branch: {Branch}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(Body);
        return sb.ToString();
    }

    public string GenerateFileName()
    {
        var slug = Regex.Replace(Title.ToLower(), @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-").Trim('-');
        return $"{Created:yyyy-MM-dd}-{slug}.md";
    }

    private class NoteFrontmatter
    {
        public string Title { get; set; } = "";
        public string Project { get; set; } = "";
        public string Priority { get; set; } = "medium";
        public string Status { get; set; } = "open";
        public List<string> Tags { get; set; } = [];
        public DateTime Created { get; set; }
        public DateTime Updated { get; set; }
        public string Repo { get; set; } = "";
        public string Branch { get; set; } = "";
    }
}
