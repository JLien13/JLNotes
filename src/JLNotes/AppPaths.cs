using System.IO;

namespace JLNotes;

/// <summary>
/// Single source of truth for JL Notes' on-disk locations. There are two folders:
/// this PC's own (<c>%USERPROFILE%\.jlnotes</c>: settings and the update log, which
/// differ per PC) and the notes folder (notes, pictures, history, waiting versions,
/// projects). They are the same folder unless Settings points the notes somewhere
/// else, such as a Dropbox folder several PCs share (Services.NotesFolder decides).
/// </summary>
internal static class AppPaths
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>This PC's own folder: <c>%USERPROFILE%\.jlnotes</c>. Never shared.</summary>
    public static string LocalDir { get; } = Path.Combine(Home, ".jlnotes");

    /// <summary>Pre-rename notes root, migrated from on first run if present.</summary>
    public static string LegacyDir { get; } = Path.Combine(Home, ".claude-notes");

    private static string? _dataDir;

    /// <summary>The notes folder for this run (what Settings shows and <c>jln where</c>
    /// prints), decided once at start (NotesFolder.ForThisRun); a change in Settings
    /// restarts the app. Reading it before then is a mistake, so it fails loudly rather
    /// than quietly reading settings that may be about to change.</summary>
    public static string DataDir
    {
        get => _dataDir ?? throw new InvalidOperationException("The notes folder is read before it is decided.");
        set => _dataDir = value;
    }

    /// <summary>The folder of note files inside the notes folder.</summary>
    public static string NotesDir => NotesDirIn(DataDir);

    private const string Notes = "notes", Attachments = "attachments", History = "history", Conflicts = "conflicts";

    /// <summary>The folders beside the note files with a subfolder per note (named after
    /// the note's file): its pictures, its history and its versions waiting for a decision.</summary>
    public static IReadOnlyList<string> NoteFolderNames { get; } = [Attachments, History, Conflicts];

    /// <summary>The project list, kept in the notes folder so every PC shares it.</summary>
    public const string ProjectsFileName = "projects.json";

    public static string NotesDirIn(string dataDir) => Path.Combine(dataDir, Notes);

    /// <summary>Attachments root: the <c>attachments</c> folder beside a notes folder.</summary>
    public static string AttachmentsRootFor(string notesDir) => Beside(notesDir, Attachments);

    /// <summary>History root: the <c>history</c> folder beside a notes folder, one
    /// subfolder per note, named after the note's file.</summary>
    public static string HistoryRootFor(string notesDir) => Beside(notesDir, History);

    /// <summary>Clash root: the <c>conflicts</c> folder beside a notes folder, one
    /// subfolder per note that was changed in two places, holding the other versions
    /// until the user picks what to keep.</summary>
    public static string ConflictsRootFor(string notesDir) => Beside(notesDir, Conflicts);

    /// <summary>The notes folder a folder of note files sits in.</summary>
    public static string DataDirOf(string notesDir) =>
        Path.GetDirectoryName(notesDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!;

    private static string Beside(string notesDir, string name) => Path.Combine(DataDirOf(notesDir), name);
}
