using System.IO;
using JLNotes.Helpers;
using JLNotes.Models;

namespace JLNotes.Services;

/// <summary>What changing the notes folder did.</summary>
public enum NotesFolderOutcome
{
    /// <summary>It was already the notes folder.</summary>
    Unchanged,
    /// <summary>The folder already held notes (another PC put them there); this PC's were added.</summary>
    AddedToNotesThere,
    /// <summary>The folder held no notes, so this PC's were copied there.</summary>
    CopiedNotesThere,
}

/// <summary>
/// The one answer to "where do the notes live", for the app and the jln command
/// alike, and the one way to move them. A PC's own folder (%USERPROFILE%\.jlnotes)
/// always holds its settings; the notes, pictures, history and projects live there
/// too unless Settings points them at another folder, such as a Dropbox folder that
/// several PCs share. Moving copies and never deletes: the old folder stays as it was.
/// </summary>
public static class NotesFolder
{
    /// <summary>The notes folder these settings name; this PC's own folder when none.</summary>
    public static string DataDirFor(AppSettings settings, string localDir) =>
        string.IsNullOrWhiteSpace(settings.NotesFolder) ? localDir : Path.GetFullPath(settings.NotesFolder);

    /// <summary>The notes folder this PC's settings name now (jln reads it this way).</summary>
    public static string DataDirFor(string localDir) => DataDirFor(new SettingsService(localDir).Load(), localDir);

    /// <summary>
    /// The notes folder for this run of the app, decided once at start, before anything
    /// reads it. A chosen folder that cannot be found is never quietly made again empty:
    /// <paramref name="useThisPcsFolder"/> is asked (with the reason) whether to use this
    /// PC's own folder instead, which then becomes the choice. Null means no: close.
    /// </summary>
    public static string? ForThisRun(SettingsService settingsService, string localDir, Func<string, bool> useThisPcsFolder)
    {
        var settings = settingsService.Load();
        var chosen = DataDirFor(settings, localDir);
        if (MissingFolderProblem(chosen, localDir) is not { } problem) return chosen;
        if (!useThisPcsFolder(problem)) return null;
        settings.NotesFolderAway = chosen;
        settings.NotesFolder = "";
        settingsService.Save(settings);
        return localDir;
    }

    /// <summary>The notes folder this PC stopped using because it could not be found
    /// (<see cref="ForThisRun"/>), now that it is back; null if there is none.</summary>
    public static string? FolderToGoBackTo(AppSettings settings, string currentDataDir)
    {
        if (settings.NotesFolderAway is not { Length: > 0 } away
            || Normalize(away).Equals(Normalize(currentDataDir), StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            return HasNotes(away) ? away : null; // not while it is still empty
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null; // not readable yet: not back
        }
    }

    /// <summary>The user would rather stay: <see cref="FolderToGoBackTo"/> is not offered again.</summary>
    public static void ForgetFolderToGoBackTo(SettingsService settingsService)
    {
        var settings = settingsService.Load();
        settings.NotesFolderAway = "";
        settingsService.Save(settings);
    }

    /// <summary>Why the notes cannot move to <paramref name="target"/>, or null if they can.
    /// The folder must be empty or a notes folder already (another PC's, or this PC's own):
    /// notes mixed into a folder of other things would be hard to tell apart and share.</summary>
    public static string? ProblemWith(string localDir, string currentDataDir, string target)
    {
        var current = Normalize(currentDataDir);
        var wanted = Normalize(target);
        var nl = Environment.NewLine;
        if (wanted.Equals(current, StringComparison.OrdinalIgnoreCase))
            return "The notes are already in that folder.";
        if (wanted.StartsWith(current + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return "That folder is inside the notes folder. Pick one outside it.";
        if (wanted.Equals(Normalize(localDir), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(wanted))
            return null;
        try
        {
            var above = Path.GetDirectoryName(wanted);
            if (above != null && Normalize(AppPaths.NotesDirIn(above)).Equals(wanted, StringComparison.OrdinalIgnoreCase)
                && Directory.EnumerateFiles(wanted, "*.md").Any())
                return $"That is the folder the note files are kept in. Pick the folder it is in instead:{nl}{above}";
            if (Directory.EnumerateFileSystemEntries(wanted).Any(entry => !BelongsToANotesFolder(wanted, entry)))
                return "That folder has other things in it. Make a new, empty folder for the notes inside it " +
                       "(New folder, at the top of the window) and pick that one.";
            HasNotes(wanted); // the notes in it must be readable too
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "That folder can't be read. Pick another one.";
        }
    }

    /// <summary>What a notes folder holds: the notes and the folders beside them, the
    /// project list (and a sync tool's conflicted copy of it), and a sync tool's own
    /// hidden files.</summary>
    private static bool BelongsToANotesFolder(string dataDir, string entry)
    {
        var name = Path.GetFileName(entry);
        if (Directory.Exists(entry))
            return Normalize(entry).Equals(Normalize(AppPaths.NotesDirIn(dataDir)), StringComparison.OrdinalIgnoreCase)
                   || AppPaths.NoteFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        var projects = Path.GetFileNameWithoutExtension(AppPaths.ProjectsFileName);
        var stem = Path.GetFileNameWithoutExtension(name);
        return name.Equals(AppPaths.ProjectsFileName, StringComparison.OrdinalIgnoreCase)
               || projects.Equals(SyncConflictName.OriginalOf(stem), StringComparison.OrdinalIgnoreCase)
               || name.StartsWith('.') || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
               || (File.GetAttributes(entry) & (FileAttributes.Hidden | FileAttributes.System)) != 0;
    }

    /// <summary>Why the notes folder these settings name cannot be used now, or null.
    /// This PC's own folder is always fine (it is made when missing); a chosen folder
    /// that is gone must never be quietly made again empty, by the app or by jln.
    /// (A stopped Dropbox leaves its folder in place; one that is gone was never set up
    /// on this PC, or was moved or renamed.)</summary>
    public static string? MissingFolderProblem(string dataDir, string localDir) =>
        Normalize(dataDir).Equals(Normalize(localDir), StringComparison.OrdinalIgnoreCase) || Directory.Exists(dataDir)
            ? null
            : $"The notes folder can't be found: {dataDir}. If it is in Dropbox, Dropbox may not be set up " +
              "on this PC yet, or the folder was moved or renamed.";

    /// <summary>True if the folder already holds notes.</summary>
    public static bool HasNotes(string dataDir)
    {
        var notes = AppPaths.NotesDirIn(dataDir);
        return Directory.Exists(notes) && Directory.EnumerateFiles(notes, "*.md").Any();
    }

    /// <summary>What <see cref="Change"/> will do, in words, for the user to confirm.</summary>
    public static string WhatChangeWillDo(string currentDataDir, string target)
    {
        var nl = Environment.NewLine;
        return HasNotes(target)
            ? $"That folder already has notes:{nl}{target}{nl}{nl}" +
              $"Your notes from {currentDataDir} will be added to them:{nl}" +
              $"- notes it lacks are copied in, except ones deleted there{nl}" +
              $"- where both have a note, the newer is kept; if the other was changed separately, " +
              $"the note asks you which to keep{nl}{nl}" +
              $"JL Notes then uses that folder and restarts. {currentDataDir} stays as it is."
            : $"Your notes, pictures and history will be copied to:{nl}{target}{nl}{nl}" +
              "JL Notes will then use that folder and restart. The old folder stays as it is, as a backup.";
    }

    /// <summary>
    /// Makes <paramref name="target"/> this PC's notes folder, bringing this PC's notes
    /// along (<see cref="WhatChangeWillDo"/> says how). Nothing is overwritten without
    /// being kept, and the current folder is only read. A copy that stopped partway is
    /// finished by trying again. The app reads the folder once at start, so it restarts
    /// afterwards.
    /// </summary>
    /// <exception cref="ArgumentException">The folder cannot be used (see <see cref="ProblemWith"/>).</exception>
    public static NotesFolderOutcome Change(SettingsService settingsService, string localDir, string currentDataDir, string target)
    {
        if (Normalize(target).Equals(Normalize(currentDataDir), StringComparison.OrdinalIgnoreCase))
            return NotesFolderOutcome.Unchanged;
        if (ProblemWith(localDir, currentDataDir, target) is { } problem)
            throw new ArgumentException(problem, nameof(target));

        var outcome = HasNotes(target) ? NotesFolderOutcome.AddedToNotesThere : NotesFolderOutcome.CopiedNotesThere;
        MergeInto(currentDataDir, target);

        var settings = settingsService.Load();
        settings.NotesFolder = Normalize(target).Equals(Normalize(localDir), StringComparison.OrdinalIgnoreCase)
            ? ""
            : Normalize(target);
        settings.NotesFolderAway = ""; // the user has chosen
        settingsService.Save(settings);
        return outcome;
    }

    /// <summary>What lives in the notes folder moves with it; everything else in a PC's own
    /// folder (settings, the update log) stays with the PC. Each note comes with its
    /// history, pictures and waiting versions (<see cref="NoteService.MergeIn"/>). What
    /// belongs to no note there (deleted notes' history, so Deleted Notes still lists them)
    /// is copied by name. The old folder is only read.</summary>
    private static void MergeInto(string from, string to)
    {
        var notesFrom = AppPaths.NotesDirIn(from);
        var notes = Directory.Exists(notesFrom) ? Directory.GetFiles(notesFrom, "*.md") : [];
        var noteNames = notes.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Notes deleted there: what is left of each (history, waiting versions, pictures).
        var deleted = new[] { AppPaths.HistoryRootFor(notesFrom), AppPaths.ConflictsRootFor(notesFrom), AppPaths.AttachmentsRootFor(notesFrom) }
            .Where(Directory.Exists)
            .SelectMany(Directory.GetDirectories)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !noteNames.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Both project lists are read whole first: one that is not (being written on another
        // PC) stops the move before anything is written.
        var projectsThere = new ProjectService(from).Peek();
        Directory.CreateDirectory(to);
        new ProjectService(to).Peek();

        using (var target = new NoteService(AppPaths.NotesDirIn(to)))
        {
            foreach (var file in notes)
                target.MergeIn(Path.GetFileName(file), File.ReadAllText(file), notesFrom);
            foreach (var name in deleted)
                target.MergeInDeleted(name, notesFrom);
        }
        new ProjectService(to).AddMissing(projectsThere);
    }

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
