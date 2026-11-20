using System.Text.RegularExpressions;

namespace JLNotes.Helpers;

/// <summary>
/// The one place that knows how sync tools name a second version of a file. When two
/// PCs save the same file before either hears of the other, Dropbox keeps the other
/// one as "name (JL's conflicted copy 2026-11-20).md" and Syncthing as
/// "name.sync-conflict-20261120-101112-ABCDEF7.md". Such a file is another version of
/// an existing note (or of projects.json), never a note of its own.
/// </summary>
public static class SyncConflictName
{
    private static readonly Regex[] Patterns =
    [
        // Dropbox, in English: " (<who>'s conflicted copy <date>[ (n)])", or with no name.
        new(@"^(?<original>.*\S) \([^()]*conflicted copy[^()]*(?:\(\d+\)[^()]*)?\)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Syncthing: ".sync-conflict-<date>-<time>-<device id>".
        new(@"^(?<original>.+)\.sync-conflict-\d{8}-\d{6}-[A-Z0-9]{7}$", RegexOptions.Compiled),
    ];

    /// <summary>The name (without extension) of the file this one is a conflicted copy
    /// of, or null when it is an ordinary file.</summary>
    public static string? OriginalOf(string nameWithoutExtension)
    {
        foreach (var pattern in Patterns)
        {
            var match = pattern.Match(nameWithoutExtension);
            if (match.Success)
                return match.Groups["original"].Value;
        }
        return null;
    }
}
