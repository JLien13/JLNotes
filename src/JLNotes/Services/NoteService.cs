using System.Globalization;
using System.IO;
using JLNotes.Helpers;
using JLNotes.Models;

namespace JLNotes.Services;

/// <summary>What to do with a note that was changed in two places.</summary>
public enum ClashChoice
{
    /// <summary>Keep the note as it is; the other version goes to Earlier Versions.</summary>
    KeepThis,
    /// <summary>Put the other version in the note; this one goes to Earlier Versions.</summary>
    KeepOther,
    /// <summary>Every line of both in the note (NoteMerge); both go to Earlier Versions.</summary>
    Combine,
}

public class NoteService : IDisposable
{
    private readonly string _notesDir;
    private readonly string _attachmentsRoot;
    private readonly string _historyRoot;
    private readonly string _conflictsRoot;
    private FileSystemWatcher? _watcher;
    private FileSystemWatcher? _conflictsWatcher;

    // Notes with another version waiting for the user to pick (see GetClashes), as the
    // last LoadAll found them, kept up to date by this service's own changes.
    private HashSet<string> _clashNotes = new(StringComparer.OrdinalIgnoreCase);

    // Each note's text as this service last read or wrote it. LoadAll compares the
    // folder against it, so an edit or delete made outside the app (Notepad, Claude
    // writing the file itself) still has what it replaced kept in history.
    private Dictionary<string, string> _lastSeen = new(StringComparer.OrdinalIgnoreCase);

    // History file names: when the kept text was replaced, sortable as text.
    private const string VersionStamp = "yyyy-MM-dd_HH-mm-ss-fff";

    // Paths this process just wrote, with the time of the write. The watcher
    // skips Changed events for these so the app's OWN saves don't trigger a
    // full reload-from-disk — that reload is what made editing then clicking
    // another note feel laggy. External edits (e.g. Claude) still refresh.
    private readonly Dictionary<string, DateTime> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan SelfWriteWindow = TimeSpan.FromSeconds(1);

    public event Action? NotesChanged;

    /// <summary>A save wrote over a change this service never read: another PC, another
    /// program or Claude changed the note while it was open here. Both versions are
    /// kept and the note now asks which to keep (GetClashes); this lets the app say so.</summary>
    public event Action<Note>? ClashFound;

    public NoteService(string notesDir)
    {
        _notesDir = notesDir;
        if (!Directory.Exists(_notesDir))
            Directory.CreateDirectory(_notesDir);
        _attachmentsRoot = AppPaths.AttachmentsRootFor(_notesDir);
        _historyRoot = AppPaths.HistoryRootFor(_notesDir);
        _conflictsRoot = AppPaths.ConflictsRootFor(_notesDir);
    }

    /// <summary>The notes folder: notes, pictures, history and waiting versions all live in it.</summary>
    public string DataDir => AppPaths.DataDirOf(_notesDir);

    /// <summary>A note's text as this service last read or wrote it: what an edit
    /// started now is based on (see the basedOn of <see cref="Save"/>).</summary>
    public string? LastSeenText(string notePath) => _lastSeen.TryGetValue(notePath, out var text) ? text : null;

    /// <summary>File names the last LoadAll could not read. Bad headers load fine,
    /// so this is for real read failures (a locked file), which the panel shows
    /// instead of letting a note vanish silently.</summary>
    public IReadOnlyList<string> UnreadableFiles { get; private set; } = [];

    public List<Note> LoadAll()
    {
        TakeInConflictedCopies();

        var notes = new List<Note>();
        var unreadable = new List<string>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(_notesDir, "*.md"))
        {
            if (IsConflictedCopy(file))
                continue; // not taken in yet (still syncing); the next load tries again
            try
            {
                var content = File.ReadAllText(file);
                notes.Add(Note.ParseFromMarkdown(content, file));
                seen[file] = content;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                unreadable.Add(Path.GetFileName(file));
                if (_lastSeen.TryGetValue(file, out var before))
                    seen[file] = before; // locked, not gone
            }
        }
        UnreadableFiles = unreadable;
        SweepClashes();

        foreach (var (path, before) in _lastSeen)
        {
            // Whoever changed it may have kept this text already (jln does), maybe
            // not as the newest version, so any kept copy counts. A change that only
            // moved the note in a list (a drag on another PC) is not worth a version.
            if (!seen.TryGetValue(path, out var now) || !Note.SameApartFromBookkeeping(before, now))
                KeepText(path, before, anyVersion: true);
        }
        _lastSeen = seen;

        return notes.OrderBy(n => n.SortOrder).ThenByDescending(n => n.Created).ToList();
    }

    /// <param name="note">The note to write.</param>
    /// <param name="stampUpdated">False when nothing the user wrote changed, such as a new sort position.</param>
    /// <param name="basedOn">The file text this change started from (an editor opened a
    /// while ago). Null means the text this service last read or wrote. If the file no
    /// longer says that, someone else changed the note meanwhile: this save still goes
    /// through, and their version is held as a clash for the user to settle.</param>
    public void Save(Note note, bool stampUpdated = true, string? basedOn = null)
    {
        string? previous = null;
        string? previousBody = null;
        string? movedFrom = null;
        if (string.IsNullOrEmpty(note.FilePath) || !File.Exists(note.FilePath))
        {
            // A note whose file is gone (deleted elsewhere a moment ago) comes back under
            // a free name, with its pictures.
            if (!string.IsNullOrEmpty(note.FilePath))
                movedFrom = note.FilePath;
            note.FilePath = FreeNotePath(note.GenerateFileName());
        }
        else
        {
            previous = WithRetry(() => File.ReadAllText(note.FilePath));
            var onDisk = Note.ParseFromMarkdown(previous, note.FilePath);
            if (Note.AreSameNote(onDisk, note) == false)
            {
                // The file now holds a different note: another PC made one with the same
                // title the same day and the sync tool put it here. This note takes a free
                // name (and its pictures) instead of landing on that one.
                movedFrom = note.FilePath;
                previous = null;
                note.FilePath = FreeNotePath(note.GenerateFileName());
            }
            else
            {
                previousBody = onDisk.Body;
            }
        }

        if (stampUpdated)
            note.Updated = DateTime.Now;
        var text = note.ToMarkdown();
        var seenBefore = _lastSeen.TryGetValue(note.FilePath, out var seen);
        var changedOutside = seenBefore && previous != null && previous != seen;
        var expected = basedOn ?? (seenBefore ? seen : null);
        var clash = previous != null && expected != null && previous != expected
                    && !SaysTheSame(previous, expected, note.FilePath) // only a date or list position moved
                    && !SaysTheSame(previous, text, note.FilePath);    // both made the same change
        // A new position in a list over this service's own last write is not worth a version.
        // A clash's other version goes to history too, before the write: it is also held to
        // ask about (below, after the write, so the note's new text reaches other PCs first),
        // but a PC that settles it from a stale copy can never lose it.
        if (previous != null && previous != text && (stampUpdated || !seenBefore || changedOutside || clash))
            KeepText(note.FilePath, previous);
        // Record before writing: the watcher fires asynchronously and may beat
        // the line after WriteAllText, so the entry must already be in place.
        RecordSelfWrite(note.FilePath);
        WithRetry(() => File.WriteAllText(note.FilePath, text));
        _lastSeen[note.FilePath] = text;
        if (clash)
        {
            AddClash(note.FilePath, previous!);
            ClashFound?.Invoke(note);
        }

        // A save that leaves the body alone (priority, status, sort order) cannot
        // change what the note references. Without this check an image still waiting
        // for its token to reach the body would be deleted.
        if (previousBody != note.Body.Trim())
            DeleteUnreferencedAttachments(note);
        // A picture this text shows may have gone to history meanwhile: pasted here,
        // then another PC (or Claude) saved the note before this save carried its token.
        BringBackAttachments(note, movedFrom == null
            ? [KeptAttachmentsDir(note.FilePath)]
            : [KeptAttachmentsDir(note.FilePath), AttachmentsDirOf(movedFrom), KeptAttachmentsDir(movedFrom)]);
    }

    /// <summary>A sync tool may hold a note file for a moment while it uploads or
    /// downloads it: try again for up to a second before giving up.</summary>
    private static T WithRetry<T>(Func<T> io)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return io(); }
            catch (IOException) when (attempt < 10) { Thread.Sleep(100); }
        }
    }

    private static void WithRetry(Action io) => WithRetry(() => { io(); return 0; });

    /// <summary>Removes a note, keeping it and its pictures in history first.
    /// Returns the kept version, which <see cref="Restore"/> puts back.</summary>
    public NoteVersion? Delete(Note note)
    {
        // Another version still waiting for a decision goes to history too, older than
        // the note's own last text, so Deleted Notes brings back what was deleted.
        KeepClashesInHistory(note.FilePath);

        NoteVersion? kept = null;
        if (File.Exists(note.FilePath))
        {
            kept = KeepText(note.FilePath, File.ReadAllText(note.FilePath));
            File.Delete(note.FilePath);
        }
        _lastSeen.Remove(note.FilePath);

        var attachDir = GetAttachmentsDir(note);
        if (Directory.Exists(attachDir))
        {
            foreach (var file in Directory.GetFiles(attachDir))
                KeepAttachment(note.FilePath, file);
            Directory.Delete(attachDir, true);
        }
        return kept;
    }

    /// <summary>Notes that are gone from the notes folder, each as its last kept
    /// version, most recently removed first.</summary>
    public IReadOnlyList<NoteVersion> GetDeleted()
    {
        var deleted = !Directory.Exists(_historyRoot)
            ? []
            : Directory.GetDirectories(_historyRoot)
                .Select(dir => Path.Combine(_notesDir, Path.GetFileName(dir) + ".md"))
                .Where(path => !File.Exists(path))
                .Select(path => GetVersions(path).FirstOrDefault())
                .OfType<NoteVersion>()
                .ToList();

        // A note deleted elsewhere while this PC was off can leave a version that was
        // waiting for a decision and nothing in history: that version is listed, so it
        // can be brought back like any deleted note.
        if (Directory.Exists(_conflictsRoot))
        {
            foreach (var dir in Directory.GetDirectories(_conflictsRoot))
            {
                var path = Path.Combine(_notesDir, Path.GetFileName(dir) + ".md");
                if (File.Exists(path) || deleted.Any(v => string.Equals(v.NotePath, path, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (StampedIn(dir, path).LastOrDefault() is { } held)
                    deleted.Add(held);
            }
        }
        return deleted.OrderByDescending(v => v.SavedAt).ToList();
    }

    /// <summary>
    /// Puts a kept version back where its note lives, as it was, pictures included.
    /// What it replaces is kept first, so a restore can itself be undone. The caller
    /// refreshes (the folder watcher may refresh again, which is harmless).
    /// </summary>
    public void Restore(NoteVersion version)
    {
        var text = File.ReadAllText(version.VersionPath);
        if (File.Exists(version.NotePath))
        {
            var current = File.ReadAllText(version.NotePath);
            if (current == text) return;
            KeepText(version.NotePath, current);
        }
        WriteNote(version.NotePath, text);
    }

    /// <summary>Puts text in a note's file as it is (a restore or a settled clash), with
    /// the pictures it shows brought back from history.</summary>
    private void WriteNote(string notePath, string text)
    {
        WriteText(notePath, text);
        BringBackAttachments(Note.ParseFromMarkdown(text, notePath), [KeptAttachmentsDir(notePath)]);
    }

    private void WriteText(string notePath, string text)
    {
        RecordSelfWrite(notePath);
        WithRetry(() => File.WriteAllText(notePath, text));
        _lastSeen[notePath] = text;
    }

    /// <summary>The one way pictures come back: any picture this note shows that is not
    /// beside it is copied from the first of <paramref name="sources"/> that has it (its
    /// history, or where the note it came from kept it). A copy, since other versions
    /// may show it too.</summary>
    private void BringBackAttachments(Note note, string[] sources)
    {
        var liveDir = GetAttachmentsDir(note);
        foreach (var token in note.GetAttachmentTokens())
        {
            var live = Path.Combine(liveDir, token);
            if (token != Path.GetFileName(token) || File.Exists(live)) continue;
            if (sources.Select(dir => Path.Combine(dir, token)).FirstOrDefault(File.Exists) is not { } kept) continue;
            Directory.CreateDirectory(liveDir);
            File.Copy(kept, live);
        }
    }

    /// <summary>A file name for a new note that no note, kept history or clash uses yet.</summary>
    private string FreeNotePath(string fileName)
    {
        // Never reuse another note's file: same title on the same day (or two
        // inline-created notes in one minute) used to overwrite each other.
        var candidate = Path.Combine(_notesDir, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        // Nor a deleted note's: its history would mix with the new note's.
        for (var n = 2; File.Exists(candidate) || Directory.Exists(HistoryDirOf(candidate)) || Directory.Exists(ClashDirOf(candidate)); n++)
            candidate = Path.Combine(_notesDir, $"{stem}-{n}{ext}");
        return candidate;
    }

    /// <summary>Earlier versions of the note at this path, live or deleted, newest first.
    /// Two PCs that both saw the same change may each have kept the version it replaced, a
    /// moment apart: such a version is listed once. (Both files stay: deleting one on each
    /// PC could delete both.)</summary>
    public IReadOnlyList<NoteVersion> GetVersions(string notePath)
    {
        var versions = new List<NoteVersion>();
        foreach (var version in StampedIn(HistoryDirOf(notePath), notePath))
        {
            if (versions.Count > 0 && SameFileContent(versions[^1].VersionPath, version.VersionPath))
                versions[^1] = version; // the later of the two stands for both
            else
                versions.Add(version);
        }
        versions.Reverse();
        return versions;
    }

    private static bool SameFileContent(string a, string b)
    {
        try
        {
            return new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllText(a) == File.ReadAllText(b);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Stamped versions kept in a folder (history or clashes), oldest first.</summary>
    private static IEnumerable<NoteVersion> StampedIn(string dir, string notePath) =>
        !Directory.Exists(dir)
            ? []
            : Directory.GetFiles(dir, "*.md")
                .Select(file => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), VersionStamp,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                    ? new NoteVersion(notePath, file, at)
                    : null)
                .OfType<NoteVersion>()
                .OrderBy(v => v.SavedAt);

    // ---- Clashes: a note changed in two places ----------------------------------
    //
    // When two PCs (or this app and Claude) change the same note before either sees
    // the other's change, the version that did not land in the note waits in
    // conflicts/<note file name>/ until the user picks: keep this, keep the other, or
    // combine. The folder sits beside the notes, so every PC sharing them asks, and a
    // choice made on one PC clears it on all. Nothing is dropped: whatever is not kept
    // goes to the note's history.

    /// <summary>The other versions of this note waiting for a decision, oldest first.</summary>
    public IReadOnlyList<NoteVersion> GetClashes(string notePath)
    {
        var distinct = new List<NoteVersion>();
        var texts = new List<string>();
        foreach (var clash in StampedIn(ClashDirOf(notePath), notePath))
        {
            string text;
            try { text = File.ReadAllText(clash.VersionPath); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            if (texts.Any(t => SaysTheSame(t, text, notePath))) continue; // the same version held twice
            texts.Add(text);
            distinct.Add(clash);
        }
        return distinct;
    }

    /// <summary>True if this note has another version waiting for a decision.</summary>
    public bool HasClash(string notePath) => _clashNotes.Contains(notePath);

    /// <summary>Settles one clash as the user chose. The caller refreshes.</summary>
    public void Resolve(NoteVersion clash, ClashChoice choice)
    {
        var notePath = clash.NotePath;
        // Settled meanwhile on another PC (or a second click): nothing left to do.
        if (TryRead(clash.VersionPath) is not { } other) return;
        var current = File.Exists(notePath) ? WithRetry(() => File.ReadAllText(notePath)) : null;

        if (current == null || choice == ClashChoice.KeepOther)
        {
            if (current != null)
                KeepText(notePath, current);
            WriteNote(notePath, other);
        }
        else
        {
            KeepText(notePath, other, anyVersion: true, when: NoLaterThanNow(clash.SavedAt));
            if (choice == ClashChoice.KeepThis)
                BringBackAttachments(Note.ParseFromMarkdown(current, notePath), [KeptAttachmentsDir(notePath)]);
            if (choice == ClashChoice.Combine)
            {
                var combined = NoteMerge.Combine(Note.ParseFromMarkdown(current, notePath), Note.ParseFromMarkdown(other, notePath));
                combined.Updated = DateTime.Now;
                KeepText(notePath, current);
                WriteNote(notePath, combined.ToMarkdown());
            }
        }

        // The same version may sit here twice (two PCs took in one conflicted copy).
        File.Delete(clash.VersionPath);
        foreach (var held in StampedIn(ClashDirOf(notePath), notePath).ToList())
        {
            if (TryRead(held.VersionPath) is { } text && SaysTheSame(text, other, notePath))
                Drop(held.VersionPath, text, notePath, other);
        }
        RemoveClashDirIfEmpty(notePath);
    }

    // ---- Another notes folder's notes coming in (NotesFolder.Change) ---------------
    //
    // The other folder is only read. Each note lands on the same note here, and brings
    // its history, pictures and versions waiting for a decision. Nothing it brings is
    // dropped: what this folder already has goes to history if it differs in anything
    // someone wrote.

    /// <summary>
    /// Adds a note from another notes folder (moving the notes to a folder that already
    /// has some).
    /// <list type="bullet">
    /// <item>It lands where <see cref="PlaceFor"/> says: on the same note here.</item>
    /// <item>If this folder deleted it, a version it already had stays deleted (Deleted
    /// Notes still has it); only one written after that comes back.</item>
    /// <item>Where both have it, the newer becomes the note. The other goes to Earlier
    /// Versions quietly if the newer one's own history has it (it is an earlier version);
    /// if it was changed separately, it waits as a second version and the user is asked.</item>
    /// </list>
    /// </summary>
    /// <param name="fromNotesDir">The notes folder it comes from. Null for a note that
    /// brings nothing else.</param>
    public void MergeIn(string fileName, string text, string? fromNotesDir = null)
    {
        var from = fromNotesDir == null ? null : Path.Combine(fromNotesDir, fileName);
        var incoming = Note.ParseFromMarkdown(text, Path.Combine(_notesDir, fileName));
        var dest = PlaceFor(fileName, incoming);
        var current = File.Exists(dest) ? WithRetry(() => File.ReadAllText(dest)) : null;

        // Everything is decided first, then done in an order that a move stopped partway and
        // picked again judges the same way: pictures (no decision reads them), the note's
        // text with its question right after it, and last the incoming history (which would
        // otherwise change what the decisions see) and pictures brought back from history.
        var deletedHere = current == null
                          && (HasVersion(HistoryDirOf(dest), dest, text) || HasVersion(ClashDirOf(dest), dest, text));
        var same = current != null && SaysTheSame(current, text, dest);
        var incomingWins = current == null || !same && incoming.Updated > Note.ParseFromMarkdown(current, dest).Updated;
        var other = current == null || same ? null : incomingWins ? current : text;
        var earlierVersion = other != null && HasVersion(incomingWins ? HistoryDirIn(from) : HistoryDirOf(dest), dest, other);
        var held = deletedHere ? [] : HeldVersionsIn(from, dest);

        BringPictures(from, dest, deletedHere ? Pictures.ToHistory : same ? Pictures.BesideIfFree : incomingWins ? Pictures.Beside : Pictures.ToHistory);
        if (deletedHere)
            KeepUnlessKept(dest, text);
        else if (current == null)
            WriteText(dest, text);
        else if (same)
            KeepUnlessKept(dest, text, sameAs: current);
        else
        {
            if (incomingWins)
            {
                KeepText(dest, current);
                WriteText(dest, text); // before the question: other PCs see the new text first
            }
            if (earlierVersion)
                KeepUnlessKept(dest, other!);
            else
                AddClash(dest, other!);
        }
        RememberPlace(dest, incoming);

        // What still waits for a decision there waits here too, unless it was settled here.
        var note = TryRead(dest);
        foreach (var (heldText, savedAt, settledHere) in held)
        {
            if (settledHere)
                KeepUnlessKept(dest, heldText);
            else if (note != null && SaysTheSame(note, heldText, dest))
                KeepUnlessKept(dest, heldText, sameAs: note);
            else
                AddClash(dest, heldText, savedAt);
        }

        CopyHistory(from, dest, withHeldVersions: deletedHere);
        if (!deletedHere && TryRead(dest) is { } now)
            BringBackAttachments(Note.ParseFromMarkdown(now, dest), [KeptAttachmentsDir(dest)]);
    }

    /// <summary>
    /// A note deleted in the other folder (its history, any version that was still waiting,
    /// and its pictures are all that is left of it there) joins the history here, so Deleted
    /// Notes lists it; under another name if this name belongs to a different note.
    /// </summary>
    public void MergeInDeleted(string name, string fromNotesDir)
    {
        var from = Path.Combine(fromNotesDir, name + ".md");
        var last = LastKept(HistoryDirIn(from), ClashDirIn(from), from);
        var dest = last == null ? Path.Combine(_notesDir, name + ".md") : PlaceFor(name + ".md", last);
        if (last != null)
            RememberPlace(dest, last);
        CopyHistory(from, dest, withHeldVersions: true);
        BringPictures(from, dest, Pictures.ToHistory);
    }

    /// <summary>
    /// Where a note from another folder belongs here: under its own name if that is free,
    /// or holds the same note (<see cref="WhatIsAt"/>); else on the note here that is surely
    /// the same under another name (an earlier move gave it a free name); else a free name,
    /// so two different notes never share one.
    /// </summary>
    private string PlaceFor(string fileName, Note incoming)
    {
        var path = Path.Combine(_notesDir, fileName);
        if (WhatIsAt(path) is not { } here || Note.AreSameNote(here, incoming) != false)
            return path;
        return SameNoteElsewhere(incoming, path) ?? FreeNotePath(fileName);
    }

    /// <summary>The note that has this name here: the live one, or what is left of a deleted
    /// one (its newest earlier version, or else a version that was still waiting). Null if
    /// the name is free. Deleted Notes (<see cref="GetDeleted"/>) lists by the same rule.</summary>
    private Note? WhatIsAt(string notePath) =>
        File.Exists(notePath)
            ? Note.ParseFromMarkdown(WithRetry(() => File.ReadAllText(notePath)), notePath)
            : LastKept(HistoryDirOf(notePath), ClashDirOf(notePath), notePath);

    /// <summary>The newest version in a history folder, or else in a waiting-versions folder.</summary>
    private static Note? LastKept(string? historyDir, string? clashDir, string notePath) =>
        LastVersionIn(historyDir, notePath) ?? LastVersionIn(clashDir, notePath);

    // The notes here, live and deleted, by created time: for finding the same note under
    // another name while another folder's notes come in. Built on first use; RememberPlace
    // keeps it current. (TwinOf answers the same question for a running app, from the notes
    // it has seen, which stay current as it saves.)
    private Dictionary<DateTime, List<string>>? _byCreated;

    private string? SameNoteElsewhere(Note note, string notPath)
    {
        if (!note.CreatedIsKnown) return null;
        if (_byCreated == null)
        {
            _byCreated = [];
            var names = Directory.GetFiles(_notesDir, "*.md").Where(f => !IsConflictedCopy(f)).Select(Path.GetFileNameWithoutExtension)
                .Concat(new[] { _historyRoot, _conflictsRoot }.Where(Directory.Exists).SelectMany(Directory.GetDirectories).Select(Path.GetFileName))
                .OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                var path = Path.Combine(_notesDir, name + ".md");
                if (WhatIsAt(path) is { } there)
                    RememberPlace(path, there);
            }
        }
        if (!_byCreated.TryGetValue(note.Created, out var paths)) return null;
        // The live note if there is one; else the one deleted note.
        var others = paths.Where(p => !string.Equals(p, notPath, StringComparison.OrdinalIgnoreCase)).ToList();
        return others.Where(File.Exists).ToList() is [var live] ? live
            : !others.Any(File.Exists) && others is [var deleted] ? deleted
            : null;
    }

    private void RememberPlace(string path, Note note)
    {
        if (_byCreated == null || !note.CreatedIsKnown) return;
        if (!_byCreated.TryGetValue(note.Created, out var paths))
            _byCreated[note.Created] = paths = [];
        if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            paths.Add(path);
    }

    /// <summary>True if this folder of versions has one saying the same as the text.</summary>
    private static bool HasVersion(string? dir, string notePath, string text) =>
        dir != null && StampedIn(dir, notePath).Any(v => TryRead(v.VersionPath) is { } kept && SaysTheSame(kept, text, notePath));

    /// <summary>The newest version in a folder of versions, read as a note; null if none.</summary>
    private static Note? LastVersionIn(string? dir, string notePath) =>
        dir != null && StampedIn(dir, notePath).LastOrDefault() is { } last && TryRead(last.VersionPath) is { } text
            ? Note.ParseFromMarkdown(text, notePath)
            : null;

    // Where a note of another notes folder keeps its history, waiting versions and pictures.
    private static string? HistoryDirIn(string? fromNotePath) => FolderOfIn(fromNotePath, AppPaths.HistoryRootFor);
    private static string? ClashDirIn(string? fromNotePath) => FolderOfIn(fromNotePath, AppPaths.ConflictsRootFor);
    private static string? AttachmentsDirIn(string? fromNotePath) => FolderOfIn(fromNotePath, AppPaths.AttachmentsRootFor);

    private static string? FolderOfIn(string? notePath, Func<string, string> root) =>
        notePath == null ? null : FolderOf(root(Path.GetDirectoryName(notePath)!), notePath);

    /// <summary>The incoming note's earlier versions join the history of the note it lands
    /// on (missing ones only). For a note that stays deleted, so do its waiting versions.</summary>
    private void CopyHistory(string? fromNotePath, string destNotePath, bool withHeldVersions)
    {
        foreach (var dir in withHeldVersions ? [HistoryDirIn(fromNotePath), ClashDirIn(fromNotePath)] : new[] { HistoryDirIn(fromNotePath) })
        {
            if (dir == null || !Directory.Exists(dir)) continue;
            Directory.CreateDirectory(HistoryDirOf(destNotePath));
            foreach (var file in Directory.GetFiles(dir, "*.md"))
            {
                var target = Path.Combine(HistoryDirOf(destNotePath), Path.GetFileName(file));
                if (!File.Exists(target))
                    File.Copy(file, target);
            }
        }
    }

    /// <summary>Where an incoming note's pictures go: beside the note when its text becomes
    /// the note (one of the same name with other content there goes to history first, as a
    /// replaced picture does); beside it only where the name is free when both say the
    /// same; to history when its text is not the note, so that version can still show them.</summary>
    private enum Pictures { Beside, BesideIfFree, ToHistory }

    /// <summary>The incoming note's pictures, as <paramref name="fate"/> says. Its kept
    /// pictures join the kept ones. Nothing is written over.</summary>
    private void BringPictures(string? fromNotePath, string destNotePath, Pictures fate)
    {
        foreach (var file in FilesIn(AttachmentsDirIn(fromNotePath)))
        {
            var live = Path.Combine(AttachmentsDirOf(destNotePath), Path.GetFileName(file));
            if (File.Exists(live) && SameBytes(live, file))
                continue;
            if (fate == Pictures.ToHistory || fate == Pictures.BesideIfFree && File.Exists(live))
            {
                KeepAttachment(destNotePath, file, copy: true);
                continue;
            }
            if (File.Exists(live))
                KeepAttachment(destNotePath, live);
            Directory.CreateDirectory(AttachmentsDirOf(destNotePath));
            File.Copy(file, live);
        }
        var keptThere = HistoryDirIn(fromNotePath) is { } history ? Path.Combine(history, "attachments") : null;
        foreach (var file in FilesIn(keptThere))
            KeepAttachment(destNotePath, file, copy: true);
    }

    private static string[] FilesIn(string? dir) => dir != null && Directory.Exists(dir) ? Directory.GetFiles(dir) : [];

    private static bool SameBytes(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));

    /// <summary>The incoming note's versions waiting for a decision there, each marked
    /// settled if this folder made that choice already (the version not kept went to this
    /// note's history), judged before the incoming history joins it.</summary>
    private List<(string Text, DateTime SavedAt, bool SettledHere)> HeldVersionsIn(string? fromNotePath, string destNotePath) =>
        ClashDirIn(fromNotePath) is not { } dir
            ? []
            : StampedIn(dir, destNotePath)
                .Select(held => (Text: TryRead(held.VersionPath), held.SavedAt))
                .Where(held => held.Text != null)
                .Select(held => (held.Text!, held.SavedAt, HasVersion(HistoryDirOf(destNotePath), destNotePath, held.Text!)))
                .ToList();

    /// <summary>Keeps text in this note's history unless history already has it, apart from
    /// when it was updated or its place in a list. With <paramref name="sameAs"/> (what the
    /// note says now), nor if it differs from that only so.</summary>
    private void KeepUnlessKept(string notePath, string text, string? sameAs = null)
    {
        if (sameAs != null && Note.SameApartFromBookkeeping(text, sameAs)) return;
        if (!GetVersions(notePath).Any(v => TryRead(v.VersionPath) is { } kept && Note.SameApartFromBookkeeping(kept, text)))
            KeepText(notePath, text, anyVersion: true);
    }

    /// <summary>Holds text as another version of a note, unless one saying the same is
    /// held already (then the text goes to history if it differs in anything someone wrote).</summary>
    private void AddClash(string notePath, string text, DateTime? when = null)
    {
        if (GetClashes(notePath).Select(c => TryRead(c.VersionPath)).FirstOrDefault(held => held != null && SaysTheSame(held, text, notePath)) is { } same)
            KeepUnlessKept(notePath, text, sameAs: same);
        else
            WriteStamped(ClashDirOf(notePath), notePath, text, NoLaterThanNow(when ?? DateTime.Now));
        _clashNotes.Add(notePath);
    }

    /// <summary>A file's text, or null if it is gone or cannot be read right now
    /// (another PC removed it, or the sync tool has it open).</summary>
    private static string? TryRead(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>A time from another PC's clock, never later than now: a version that
    /// seemed to come from the future would sort as newer than what replaced it.</summary>
    private static DateTime NoLaterThanNow(DateTime at)
    {
        var latest = DateTime.Now.AddMilliseconds(-1);
        return at < latest ? at : latest;
    }

    /// <summary>Moves every version still waiting for this note into its history.</summary>
    private void KeepClashesInHistory(string notePath)
    {
        foreach (var clash in StampedIn(ClashDirOf(notePath), notePath).ToList())
        {
            if (TryRead(clash.VersionPath) is not { } text) continue;
            KeepText(notePath, text, anyVersion: true, when: NoLaterThanNow(clash.SavedAt));
            File.Delete(clash.VersionPath);
        }
        RemoveClashDirIfEmpty(notePath);
    }

    private void RemoveClashDirIfEmpty(string notePath)
    {
        var dir = ClashDirOf(notePath);
        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            Directory.Delete(dir);
        if (!Directory.Exists(dir))
            _clashNotes.Remove(notePath);
    }

    /// <summary>
    /// Brings in the clash folder as other PCs and this one left it: a version that now
    /// says the same as its note, or the same as another held version, needs no
    /// decision and goes; versions of a note this PC saw and that is now gone (deleted
    /// elsewhere) go to its history, so Deleted Notes can bring it back. A note this PC
    /// has not seen yet may simply not have arrived (a new PC still syncing): its
    /// versions wait. What is left marks its note as asking.
    /// </summary>
    private void SweepClashes()
    {
        var asking = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(_conflictsRoot))
        {
            foreach (var dir in Directory.GetDirectories(_conflictsRoot))
            {
                var notePath = Path.Combine(_notesDir, Path.GetFileName(dir) + ".md");
                try
                {
                    if (!File.Exists(notePath))
                    {
                        if (_lastSeen.ContainsKey(notePath))
                            KeepClashesInHistory(notePath);
                        continue;
                    }
                    var noteText = File.ReadAllText(notePath);
                    var held = new List<string>();
                    foreach (var clash in StampedIn(dir, notePath).ToList())
                    {
                        var text = File.ReadAllText(clash.VersionPath);
                        if (new[] { noteText }.Concat(held).FirstOrDefault(h => SaysTheSame(h, text, notePath)) is { } same)
                            Drop(clash.VersionPath, text, notePath, same);
                        else
                            held.Add(text);
                    }
                    if (held.Count > 0)
                        asking.Add(notePath);
                    else
                        RemoveClashDirIfEmpty(notePath);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Mid-sync or in use: leave it as it is and ask, to be safe.
                    if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.md").Any())
                        asking.Add(notePath);
                }
            }
        }
        _clashNotes = asking;
    }

    /// <summary>The one other note (not at <paramref name="notPath"/>), among those this
    /// service has read or written, that is surely the same note: the same note under
    /// another name. Null if none.</summary>
    private string? TwinOf(Note note, string notPath) =>
        _lastSeen
            .Where(p => !string.Equals(p.Key, notPath, StringComparison.OrdinalIgnoreCase) && File.Exists(p.Key)
                        && Note.AreSameNote(Note.ParseFromMarkdown(p.Value, p.Key), note) == true)
            .Select(p => p.Key)
            .ToList() is [var only] ? only : null;

    private static bool IsConflictedCopy(string file) =>
        SyncConflictName.OriginalOf(Path.GetFileNameWithoutExtension(file)) != null;

    /// <summary>
    /// A sync tool's conflicted copy in the notes folder is another PC's version of a
    /// note, saved at the same time as this one. It never shows as a note of its own:
    /// it is held as a clash on its note, becomes the note if that is gone, or, when it
    /// was made as a different note that happens to share the name (same title, same
    /// day, on two PCs), becomes a note under a free name.
    /// </summary>
    private void TakeInConflictedCopies()
    {
        foreach (var copy in Directory.GetFiles(_notesDir, "*.md").Where(IsConflictedCopy))
        {
            var notePath = Path.Combine(_notesDir, SyncConflictName.OriginalOf(Path.GetFileNameWithoutExtension(copy)) + ".md");
            try
            {
                if (!File.Exists(notePath))
                {
                    File.Move(copy, notePath);
                    continue;
                }
                var copyText = File.ReadAllText(copy);
                var noteText = File.ReadAllText(notePath);
                var copyNote = Note.ParseFromMarkdown(copyText, notePath);
                if (Note.AreSameNote(copyNote, Note.ParseFromMarkdown(noteText, notePath)) == false)
                {
                    // This PC may already have saved that note under a free name (a save in
                    // the moment before the copy arrived): then the copy is its other version.
                    if (TwinOf(copyNote, notePath) is { } twin)
                    {
                        HoldOrDrop(copy, copyText, twin);
                        continue;
                    }
                    var freePath = FreeNotePath(Path.GetFileName(notePath));
                    File.Move(copy, freePath);
                    BringBackAttachments(Note.ParseFromMarkdown(copyText, freePath),
                        [AttachmentsDirOf(notePath), KeptAttachmentsDir(notePath)]);
                    // If the copy is this PC's own note, what this PC last saw of it moved
                    // too; otherwise the other note would be thought changed under it.
                    if (_lastSeen.TryGetValue(notePath, out var seenHere) && seenHere == copyText)
                    {
                        _lastSeen.Remove(notePath);
                        _lastSeen[freePath] = copyText;
                    }
                    continue;
                }
                HoldOrDrop(copy, copyText, notePath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still syncing, or the other PC took it in first: the next load sees.
            }
        }
    }

    /// <summary>A conflicted copy of this note is held as its other version, unless it says
    /// the same as the note; either way the copy goes.</summary>
    private void HoldOrDrop(string copy, string copyText, string notePath)
    {
        var noteText = WithRetry(() => File.ReadAllText(notePath));
        if (SaysTheSame(noteText, copyText, notePath))
        {
            Drop(copy, copyText, notePath, noteText);
            return;
        }
        AddClash(notePath, copyText, File.GetLastWriteTime(copy));
        File.Delete(copy);
    }

    /// <summary>Removes a conflicted copy or waiting version that needs no decision: it says
    /// the same as <paramref name="sameAs"/>. If its text differs in more than when it was
    /// updated or its place in a list (a header line the app has no field for, say), that
    /// text is kept in history first, so nothing anyone wrote is lost.</summary>
    private void Drop(string file, string text, string notePath, string sameAs)
    {
        KeepUnlessKept(notePath, text, sameAs);
        File.Delete(file);
    }

    /// <summary>Two texts of one note say the same if they differ at most in when it
    /// was last updated or where it sits in a list. Such a difference needs no decision.</summary>
    private static bool SaysTheSame(string a, string b, string notePath)
    {
        if (a == b) return true;
        var first = Note.ParseFromMarkdown(a, notePath);
        var second = Note.ParseFromMarkdown(b, notePath);
        second.Updated = first.Updated;
        second.SortOrder = first.SortOrder;
        return first.ToMarkdown() == second.ToMarkdown();
    }

    private static string Stamp(DateTime at) => at.ToString(VersionStamp, CultureInfo.InvariantCulture);

    /// <summary>The one naming rule for a note's own folders (history, waiting versions,
    /// pictures): named after its file, in the given root.</summary>
    private static string FolderOf(string root, string notePath) =>
        Path.Combine(root, Path.GetFileNameWithoutExtension(notePath));

    private string HistoryDirOf(string notePath) => FolderOf(_historyRoot, notePath);

    private string KeptAttachmentsDir(string notePath) => Path.Combine(HistoryDirOf(notePath), "attachments");

    private string ClashDirOf(string notePath) => FolderOf(_conflictsRoot, notePath);

    /// <summary>The one way text goes into history. Text already kept as the newest
    /// version (or, with <paramref name="anyVersion"/>, as any version) is not kept again.</summary>
    /// <param name="when">When this text stopped being the note; now if not given.</param>
    private NoteVersion KeepText(string notePath, string text, bool anyVersion = false, DateTime? when = null)
    {
        var versions = GetVersions(notePath);
        if ((anyVersion ? versions : versions.Take(1)).FirstOrDefault(v => File.ReadAllText(v.VersionPath) == text) is { } kept)
            return kept;
        return WriteStamped(HistoryDirOf(notePath), notePath, text, when ?? DateTime.Now);
    }

    /// <summary>Writes text into a history or clash folder, named for when.</summary>
    private static NoteVersion WriteStamped(string dir, string notePath, string text, DateTime when)
    {
        Directory.CreateDirectory(dir);
        var at = when.AddTicks(-(when.Ticks % TimeSpan.TicksPerMillisecond)); // what the name can hold
        string path;
        while (File.Exists(path = Path.Combine(dir, Stamp(at) + ".md")))
            at = at.AddMilliseconds(1);
        File.WriteAllText(path, text);
        return new NoteVersion(notePath, path, at);
    }

    /// <summary>Moves a picture into history instead of erasing it (or, with
    /// <paramref name="copy"/>, copies one from another notes folder, which is only read).
    /// An older kept picture with the same name and different content stays, under a new name.</summary>
    private void KeepAttachment(string notePath, string file, bool copy = false)
    {
        var dir = KeptAttachmentsDir(notePath);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, Path.GetFileName(file));
        if (File.Exists(dest))
        {
            if (SameBytes(dest, file))
            {
                if (!copy) File.Delete(file);
                return;
            }
            var older = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(dest)}.{Stamp(DateTime.Now)}{Path.GetExtension(dest)}");
            File.Move(dest, older);
        }
        if (copy) File.Copy(file, dest);
        else File.Move(file, dest);
    }

    /// <summary>The one answer to where a note's attachments live: a folder named
    /// after its full file name, date included, which no other note can share.</summary>
    public string GetAttachmentsDir(Note note) => AttachmentsDirOf(note.FilePath);

    private string AttachmentsDirOf(string notePath) => FolderOf(_attachmentsRoot, notePath);

    /// <summary>
    /// Attachment folders used to be named without the date, so notes with the same
    /// title shared one. This moves each note's files into its own folder, copying a
    /// file that two notes both use. Files no note references stay where they are, and
    /// a folder no note claims is someone's own and is left alone. Safe to run on every
    /// start: it does nothing once the old folders are gone, and a run that fails
    /// partway finishes on the next start.
    /// </summary>
    public void MigrateAttachmentFolders()
    {
        if (!Directory.Exists(_attachmentsRoot)) return;

        var notes = LoadAll();
        foreach (var oldDir in Directory.GetDirectories(_attachmentsRoot))
        {
            try
            {
                MigrateAttachmentFolder(oldDir, notes);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Something has a file open. The originals are untouched until every
                // copy has landed, so the next start picks this folder up again.
            }
        }
    }

    private void MigrateAttachmentFolder(string oldDir, List<Note> notes)
    {
        var folderName = Path.GetFileName(oldDir);
        var isSomeNotesCurrentFolder = notes.Any(n => FolderNameMatches(GetAttachmentsDir(n), folderName));
        var copied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var owner in notes.Where(n => n.GetSlug().Equals(folderName, StringComparison.OrdinalIgnoreCase)
                                               && !FolderNameMatches(GetAttachmentsDir(n), folderName)))
        {
            var newDir = GetAttachmentsDir(owner);
            foreach (var token in owner.GetAttachmentTokens().Where(t => Helpers.FlowDocumentHelper.IsAttachment(t, oldDir)))
            {
                Directory.CreateDirectory(newDir);
                File.Copy(Path.Combine(oldDir, token), Path.Combine(newDir, token), overwrite: true);
                copied.Add(token);
            }
        }

        if (isSomeNotesCurrentFolder) return;
        foreach (var token in copied)
            File.Delete(Path.Combine(oldDir, token));
        if (copied.Count > 0 && !Directory.EnumerateFileSystemEntries(oldDir).Any())
            Directory.Delete(oldDir);
    }

    private static bool FolderNameMatches(string dir, string folderName) =>
        Path.GetFileName(dir).Equals(folderName, StringComparison.OrdinalIgnoreCase);

    public string AddAttachment(Note note, string sourceFilePath)
    {
        var dir = GetAttachmentsDir(note);
        Directory.CreateDirectory(dir);

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(dir, fileName);

        // Handle filename collisions
        var counter = 2;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        while (AttachmentNameTaken(note, fileName))
        {
            fileName = $"{nameWithoutExt}-{counter}{ext}";
            destPath = Path.Combine(dir, fileName);
            counter++;
        }

        File.Copy(sourceFilePath, destPath);
        return fileName;
    }

    public string AddAttachmentFromImage(Note note, System.Windows.Media.Imaging.BitmapSource image)
    {
        var dir = GetAttachmentsDir(note);
        Directory.CreateDirectory(dir);

        var baseName = $"pasted-{DateTime.Now:yyyyMMdd-HHmmss}";
        var fileName = $"{baseName}.png";
        var destPath = Path.Combine(dir, fileName);

        var counter = 2;
        while (AttachmentNameTaken(note, fileName))
        {
            fileName = $"{baseName}-{counter}.png";
            destPath = Path.Combine(dir, fileName);
            counter++;
        }

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
        using var stream = File.Create(destPath);
        encoder.Save(stream);
        return fileName;
    }

    /// <summary>A picture name is taken if the note uses it now or history keeps one
    /// by that name; reusing a kept name would make an older version show the new picture.</summary>
    private bool AttachmentNameTaken(Note note, string fileName) =>
        File.Exists(Path.Combine(GetAttachmentsDir(note), fileName))
        || File.Exists(Path.Combine(KeptAttachmentsDir(note.FilePath), fileName));

    private void DeleteUnreferencedAttachments(Note note)
    {
        var dir = GetAttachmentsDir(note);
        if (!Directory.Exists(dir)) return;

        var referenced = new HashSet<string>(note.GetAttachmentTokens(), StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(dir))
        {
            if (!referenced.Contains(Path.GetFileName(file)))
                KeepAttachment(note.FilePath, file); // an older version may still show it
        }

        if (!Directory.EnumerateFileSystemEntries(dir).Any())
            Directory.Delete(dir);
    }

    public void StartWatching()
    {
        _watcher = new FileSystemWatcher(_notesDir, "*.md")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        _watcher.Created += (_, _) => NotesChanged?.Invoke();
        _watcher.Changed += (_, e) => { if (!IsSelfWrite(e.FullPath)) NotesChanged?.Invoke(); };
        _watcher.Deleted += (_, _) => NotesChanged?.Invoke();
        _watcher.Renamed += (_, _) => NotesChanged?.Invoke();
        // Too many changes at once (a sync burst) overflow the watcher: read it all again.
        _watcher.InternalBufferSize = 64 * 1024;
        _watcher.Error += (_, _) => NotesChanged?.Invoke();

        // A clash held or settled on another PC changes no note file here, but the
        // note must start (or stop) asking.
        Directory.CreateDirectory(_conflictsRoot);
        _conflictsWatcher = new FileSystemWatcher(_conflictsRoot, "*.md")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true
        };
        _conflictsWatcher.Created += (_, _) => NotesChanged?.Invoke();
        _conflictsWatcher.Deleted += (_, _) => NotesChanged?.Invoke();
        _conflictsWatcher.Renamed += (_, _) => NotesChanged?.Invoke();
    }

    private void RecordSelfWrite(string path)
    {
        lock (_selfWrites)
            _selfWrites[path] = DateTime.UtcNow;
    }

    // True if WE wrote this path within the suppression window. Consumes expired
    // entries so the dictionary can't grow unbounded.
    private bool IsSelfWrite(string path)
    {
        lock (_selfWrites)
        {
            if (_selfWrites.TryGetValue(path, out var when) && DateTime.UtcNow - when < SelfWriteWindow)
                return true;
            _selfWrites.Remove(path);
            return false;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _conflictsWatcher?.Dispose();
    }
}
