using JLNotes.Models;

namespace JLNotes.Helpers;

/// <summary>
/// "Combine both" for a note changed in two places. Nothing is dropped: every line of
/// both versions stays, a line they share appears once, and where they differ my
/// lines come before the other's. The person tidies the result; the two versions it
/// came from are both kept in Earlier Versions.
/// </summary>
public static class NoteMerge
{
    // Beyond this many line pairs the line-by-line match costs too much memory, and
    // the fallback below (all of mine, then the other's lines mine lacks) is used.
    private const long MaxCells = 4_000_000;

    /// <summary>Both versions in one note: my header (title, priority, status...) with
    /// anything mine left empty filled from the other, tags from both, the earlier
    /// created date, and both bodies combined. When it was updated is the caller's to set.</summary>
    public static Note Combine(Note mine, Note other) => new()
    {
        Title = mine.Title.Length > 0 ? mine.Title : other.Title,
        Project = mine.Project.Length > 0 ? mine.Project : other.Project,
        Priority = mine.Priority,
        Status = mine.Status,
        Tags = mine.Tags.Concat(other.Tags).Distinct().ToList(),
        Created = mine.Created <= other.Created ? mine.Created : other.Created,
        Updated = mine.Updated,
        Repo = mine.Repo.Length > 0 ? mine.Repo : other.Repo,
        Branch = mine.Branch.Length > 0 ? mine.Branch : other.Branch,
        SortOrder = mine.SortOrder,
        Body = CombineText(mine.Body, other.Body),
        FilePath = mine.FilePath,
    };

    /// <summary>Every line of both texts; shared lines once, in order.</summary>
    public static string CombineText(string mine, string other)
    {
        if (string.IsNullOrWhiteSpace(mine)) return other;
        if (string.IsNullOrWhiteSpace(other) || mine == other) return mine;

        var a = mine.Split('\n');
        var b = other.Split('\n');
        if ((long)a.Length * b.Length > MaxCells)
            return string.Join("\n", a.Concat(LinesNotIn(b, a)));

        // shared[i, j] = how many lines a[i..] and b[j..] have in common, in order.
        var shared = new int[a.Length + 1, b.Length + 1];
        for (var i = a.Length - 1; i >= 0; i--)
            for (var j = b.Length - 1; j >= 0; j--)
                shared[i, j] = a[i] == b[j] ? shared[i + 1, j + 1] + 1 : Math.Max(shared[i + 1, j], shared[i, j + 1]);

        var result = new List<string>(a.Length + b.Length);
        int x = 0, y = 0;
        while (x < a.Length && y < b.Length)
        {
            if (a[x] == b[y]) { result.Add(a[x]); x++; y++; }
            else if (shared[x + 1, y] >= shared[x, y + 1]) result.Add(a[x++]);
            else result.Add(b[y++]);
        }
        result.AddRange(a[x..]);
        result.AddRange(b[y..]);
        return string.Join("\n", result);
    }

    /// <summary>The lines of <paramref name="lines"/> left after taking out, once each,
    /// the lines <paramref name="have"/> already has. A line repeated more often than
    /// in <paramref name="have"/> keeps its extra copies.</summary>
    private static IEnumerable<string> LinesNotIn(string[] lines, string[] have)
    {
        var count = have.GroupBy(l => l).ToDictionary(g => g.Key, g => g.Count());
        foreach (var line in lines)
        {
            if (count.TryGetValue(line, out var n) && n > 0)
                count[line] = n - 1;
            else
                yield return line;
        }
    }
}
