using System.IO;

namespace JLNotes;

/// <summary>
/// Single source of truth for JL Notes' on-disk locations. The notes root
/// (<c>.jlnotes</c>) used to be hardcoded separately in App and Note; every
/// path now derives from here so the folder layout lives in exactly one place.
/// </summary>
internal static class AppPaths
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Notes root: <c>%USERPROFILE%\.jlnotes</c>.</summary>
    public static string BaseDir { get; } = Path.Combine(Home, ".jlnotes");

    /// <summary>Pre-rename notes root, migrated from on first run if present.</summary>
    public static string LegacyDir { get; } = Path.Combine(Home, ".claude-notes");

    /// <summary>Markdown notes folder.</summary>
    public static string NotesDir { get; } = Path.Combine(BaseDir, "notes");

    /// <summary>Per-note image attachments folder.</summary>
    public static string AttachmentsDir { get; } = Path.Combine(BaseDir, "attachments");
}
