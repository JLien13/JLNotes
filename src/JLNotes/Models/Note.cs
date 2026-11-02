using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

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

    /// <summary>One header line of a note file, as text and as the YAML library read it.</summary>
    private readonly record struct HeaderValue(string? Raw, object? Parsed);

    /// <summary>A header field: its key, how to write it (null = leave the line out), how to read it.</summary>
    private sealed record HeaderField(string Key, Func<Note, string?> Write, Action<Note, HeaderValue> Read);

    /// <summary>
    /// The one list of what a note stores in its file header, in file order.
    /// Reading and writing both go through it, so a field cannot exist on one
    /// side only (sort order once did, and drag reorder was forgotten on reload).
    /// A guard test checks every Note property is covered here.
    /// </summary>
    private static readonly HeaderField[] HeaderFields =
    [
        new("title", n => QuoteIfNeeded(n.Title), (n, v) => n.Title = ReadText(v)),
        new("project", n => QuoteIfNeeded(n.Project), (n, v) => n.Project = ReadText(v)),
        new("priority", n => n.Priority.ToString().ToLowerInvariant(), (n, v) => n.Priority = ReadEnum(v, NotePriority.Medium)),
        new("status", n => n.Status.ToString().ToLowerInvariant(), (n, v) => n.Status = ReadEnum(v, NoteStatus.Open)),
        new("tags", n => WriteList(n.Tags), (n, v) => n.Tags = ReadList(v)),
        new("created", n => WriteDate(n.Created), (n, v) => n.Created = ReadDate(v)),
        new("updated", n => WriteDate(n.Updated), (n, v) => n.Updated = ReadDate(v)),
        new("repo", n => OmitIfEmpty(n.Repo), (n, v) => n.Repo = ReadText(v)),
        new("branch", n => OmitIfEmpty(n.Branch), (n, v) => n.Branch = ReadText(v)),
        new("sortOrder", n => n.SortOrder == 0 ? null : n.SortOrder.ToString(CultureInfo.InvariantCulture),
            (n, v) => n.SortOrder = int.TryParse(ReadText(v), out var order) ? order : 0),
    ];

    private static readonly IDeserializer Yaml = new DeserializerBuilder().Build();
    private static readonly Regex HeaderLine = new(@"^([A-Za-z][A-Za-z0-9_-]*):(?:[ \t]+(.*))?$", RegexOptions.Compiled);
    private static readonly Regex FirstHeading = new(@"^#{1,6}[ \t]+(.+?)[ \t]*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex DatePrefix = new(@"^(\d{4}-\d{2}-\d{2})-", RegexOptions.Compiled);

    /// <summary>The {{file}} grammar. The tokens in a note's body are the only
    /// record of its attachments; no list of them is kept anywhere else.</summary>
    internal static readonly Regex AttachmentToken = new(@"\{\{(.+?)\}\}", RegexOptions.Compiled);

    // Text that YAML reads back exactly as written with no quotes around it.
    private static readonly Regex PlainText = new(@"^[A-Za-z0-9](?:[A-Za-z0-9 ,.'()/_+\-]*[A-Za-z0-9.)'])?$", RegexOptions.Compiled);

    /// <summary>
    /// Reads a note file, whatever state it is in. Notes are also written by hand
    /// and by Claude, so a bad header must never make a note disappear: a file with
    /// no header still loads (title from its first heading, or its file name), a
    /// title with an unquoted colon or # is taken as typed, and a header that was
    /// never closed ends at the first line that is not a field.
    /// </summary>
    public static Note ParseFromMarkdown(string markdown, string filePath)
    {
        var lines = markdown.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var (header, bodyStart) = SplitHeader(lines);

        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in header)
        {
            var match = HeaderLine.Match(line);
            if (match.Success)
                raw[match.Groups[1].Value] = match.Groups[2].Value.Trim(); // a repeated key: the last one wins
        }
        var parsed = ParseYaml(header);

        var note = new Note { FilePath = filePath, Created = default, Updated = default };
        foreach (var field in HeaderFields)
        {
            raw.TryGetValue(field.Key, out var rawValue);
            parsed.TryGetValue(field.Key, out var parsedValue);
            if (rawValue != null || parsedValue != null)
                field.Read(note, new HeaderValue(rawValue, parsedValue));
        }

        note.Body = string.Join("\n", lines[bodyStart..]).Trim();
        if (string.IsNullOrEmpty(note.Title))
            note.Title = TitleWithoutHeader(note.Body, filePath);
        if (note.Created == default)
            note.Created = DateWithoutHeader(filePath);
        if (note.Updated == default)
            note.Updated = note.Created;
        return note;
    }

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        foreach (var field in HeaderFields)
        {
            var value = field.Write(this);
            if (value != null)
                sb.AppendLine($"{field.Key}: {value}");
        }
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(Body);
        return sb.ToString();
    }

    /// <summary>The file name without its date prefix. Two notes can share this, so
    /// it never identifies a note; use it only for suggesting a name to the user.</summary>
    public string GetSlug()
    {
        var fileName = Path.GetFileNameWithoutExtension(FilePath);
        var slug = DatePrefix.Replace(fileName, "");
        return slug.Length > 0 ? slug : fileName;
    }

    /// <summary>What the {{...}} tokens in the body name. Whether one is really an
    /// attachment depends on the file existing (FlowDocumentHelper.IsAttachment).</summary>
    public IEnumerable<string> GetAttachmentTokens() =>
        AttachmentToken.Matches(Body).Select(m => m.Groups[1].Value);

    public string GenerateFileName()
    {
        var slug = Regex.Replace(Title.ToLower(), @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-").Trim('-');
        return $"{Created:yyyy-MM-dd}-{slug}.md";
    }

    /// <summary>Finds the header lines and where the body starts. A header only
    /// exists if the very first line is "---", so a horizontal rule further down a
    /// note with no header is left in its body.</summary>
    private static (string[] Header, int BodyStart) SplitHeader(string[] lines)
    {
        if (lines.Length == 0 || lines[0].Trim() != "---")
            return ([], 0);

        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == "---")
                return (lines[1..i], i + 1);
            if (!IsHeaderContent(lines[i]))
                return (lines[1..i], i); // never closed: the body starts here
        }
        return (lines[1..], lines.Length);
    }

    private static bool IsHeaderContent(string line) =>
        HeaderLine.IsMatch(line) || line.StartsWith(' ') || line.StartsWith('\t') || line.StartsWith("- ");

    /// <summary>What the YAML library makes of the header, or nothing if it refuses
    /// it. A refusal is normal (an unquoted colon), and the raw lines still hold every value.</summary>
    private static Dictionary<string, object?> ParseYaml(string[] header)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (header.Length == 0) return result;
        try
        {
            var parsed = Yaml.Deserialize<Dictionary<string, object?>>(string.Join("\n", header));
            if (parsed != null)
                foreach (var (key, value) in parsed)
                    result.TryAdd(key, value);
        }
        catch (YamlException)
        {
        }
        return result;
    }

    private static string TitleWithoutHeader(string body, string filePath)
    {
        var heading = FirstHeading.Match(body);
        if (heading.Success)
            return heading.Groups[1].Value;
        var name = Path.GetFileNameWithoutExtension(filePath);
        return DatePrefix.Replace(name, "");
    }

    private static DateTime DateWithoutHeader(string filePath)
    {
        var match = DatePrefix.Match(Path.GetFileName(filePath));
        return match.Success && DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromName)
            ? fromName
            : File.Exists(filePath) ? File.GetLastWriteTime(filePath) : DateTime.Now;
    }

    /// <summary>A plain value is taken exactly as typed, which keeps a colon or #
    /// the YAML library would choke on or cut off. Quoted and block values are left
    /// to the library, with a simple unquote if it refused the header.</summary>
    private static string ReadText(HeaderValue value)
    {
        if (value.Raw is null)
            return value.Parsed?.ToString() ?? "";
        if (value.Raw.Length == 0 || !"\"'>|".Contains(value.Raw[0]))
            return value.Raw;
        return value.Parsed as string ?? Unquote(value.Raw);
    }

    private static T ReadEnum<T>(HeaderValue value, T fallback) where T : struct, Enum =>
        Enum.TryParse<T>(ReadText(value), ignoreCase: true, out var parsed) ? parsed : fallback;

    private static DateTime ReadDate(HeaderValue value) =>
        DateTime.TryParse(ReadText(value), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : default;

    private static List<string> ReadList(HeaderValue value)
    {
        if (value.Parsed is string single)
            return single.Length == 0 ? [] : [single];
        if (value.Parsed is IEnumerable items)
            return items.Cast<object?>().Select(i => i?.ToString() ?? "").Where(i => i.Length > 0).ToList();

        // The library refused the header, so split the "[a, b]" text by hand.
        return (value.Raw ?? "").Trim('[', ']')
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Unquote)
            .ToList();
    }

    private static string Unquote(string text)
    {
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
            return text[1..^1].Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\r", "\r").Replace("\\\\", "\\");
        if (text.Length >= 2 && text[0] == '\'' && text[^1] == '\'')
            return text[1..^1].Replace("''", "'");
        return text;
    }

    private static string? OmitIfEmpty(string text) => text.Length == 0 ? null : QuoteIfNeeded(text);

    private static string WriteDate(DateTime date) => date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    // Inside [a, b] a comma separates items, so an item holding one must be quoted.
    private static string WriteList(IEnumerable<string> items) =>
        $"[{string.Join(", ", items.Select(i => i.Contains(',') ? Quote(i) : QuoteIfNeeded(i)))}]";

    /// <summary>The one place text becomes YAML. Anything not plainly safe is
    /// double-quoted, so no title can ever make its own note unreadable.</summary>
    private static string QuoteIfNeeded(string text)
    {
        if (text.Length == 0) return "";
        return PlainText.IsMatch(text) && !text.Equals("null", StringComparison.OrdinalIgnoreCase) ? text : Quote(text);
    }

    private static string Quote(string text)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        return $"\"{escaped}\"";
    }
}
