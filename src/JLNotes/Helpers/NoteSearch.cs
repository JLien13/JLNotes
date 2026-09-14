using JLNotes.Models;

namespace JLNotes.Helpers;

/// <summary>
/// The one search grammar. "#tag" tokens match a tag exactly (case-insensitive)
/// and every tag token must match (AND); everything else is the free-text part,
/// matched as one substring against title, body, and tags exactly as before.
/// Used by MainViewModel's filter, and by AddTag when a tag chip is clicked.
/// </summary>
public static class NoteSearch
{
    public sealed record Query(IReadOnlyList<string> Tags, string Text)
    {
        public bool IsEmpty => Tags.Count == 0 && Text.Length == 0;
    }

    public static Query Parse(string? raw)
    {
        var tags = new List<string>();
        var text = new List<string>();
        foreach (var token in (raw ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length > 1 && token[0] == '#')
                tags.Add(token[1..]);
            else
                text.Add(token);
        }
        return new Query(tags, string.Join(' ', text));
    }

    public static bool Matches(Note note, Query query)
    {
        foreach (var tag in query.Tags)
            if (!note.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                return false;

        if (query.Text.Length == 0) return true;
        return note.Title.Contains(query.Text, StringComparison.OrdinalIgnoreCase)
            || note.Body.Contains(query.Text, StringComparison.OrdinalIgnoreCase)
            || note.Tags.Any(t => t.Contains(query.Text, StringComparison.OrdinalIgnoreCase));
    }

    public static string TagToken(string tag) => "#" + tag;

    /// <summary>Search text with <paramref name="tag"/> required, appended once.</summary>
    public static string AddTag(string? raw, string tag)
    {
        var current = raw ?? "";
        if (Parse(current).Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            return current;
        return (current.Trim() + " " + TagToken(tag)).Trim();
    }
}
