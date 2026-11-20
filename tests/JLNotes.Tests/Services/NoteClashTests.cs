using System.IO;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.Tests.Services;

// Two PCs share one notes folder through Dropbox. When the same note is changed on
// both before either hears of the other, neither version is lost: both are held and
// the user picks one (keep this, keep the other, or combine) the next time they open
// the note, on either PC. Whatever is not kept goes into Earlier Versions.
// Two NoteService objects on one folder stand in for the two PCs.
public class NoteClashTests : IDisposable
{
    private readonly string _root;
    private readonly string _notesDir;
    private readonly NoteService _pcA;
    private readonly NoteService _pcB;

    public NoteClashTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"jlnotes-clash-{Guid.NewGuid()}");
        _notesDir = Path.Combine(_root, "notes");
        _pcA = new NoteService(_notesDir);
        _pcB = new NoteService(_notesDir);
    }

    public void Dispose()
    {
        _pcA.Dispose();
        _pcB.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private Note SaveNew(NoteService pc, string body, string title = "Plan")
    {
        var note = new Note { Title = title, Body = body, Created = new DateTime(2026, 11, 20, 9, 30, 0) };
        pc.Save(note);
        return note;
    }

    /// <summary>What Dropbox does when both PCs saved the same file: the other PC's
    /// text lands beside it under a "conflicted copy" name.</summary>
    private string DropboxConflictedCopy(Note note, string body, DateTime? created = null)
    {
        var other = Note.ParseFromMarkdown(File.ReadAllText(note.FilePath), note.FilePath);
        other.Body = body;
        if (created != null) other.Created = created.Value;
        var stem = Path.GetFileNameWithoutExtension(note.FilePath);
        var path = Path.Combine(_notesDir, $"{stem} (JL's conflicted copy 2026-11-20).md");
        File.WriteAllText(path, other.ToMarkdown());
        return path;
    }

    private static string BodyOf(NoteVersion version) => version.Read().Body;

    private static string BodyAt(string path) => Note.ParseFromMarkdown(File.ReadAllText(path), path).Body;

    [Fact]
    public void AConflictedCopy_OfTheSameNote_IsHeldAsTheOtherVersion_NotShownAsANote()
    {
        var note = SaveNew(_pcA, "from A");
        var copy = DropboxConflictedCopy(note, "from B");

        var notes = _pcB.LoadAll();

        Assert.Equal(note.FilePath, Assert.Single(notes).FilePath);
        Assert.False(File.Exists(copy));
        Assert.Equal("from B", BodyOf(Assert.Single(_pcB.GetClashes(note.FilePath))));
        Assert.True(_pcB.HasClash(note.FilePath));
    }

    [Fact]
    public void ASyncthingConflictCopy_IsHeldTheSameWay()
    {
        var note = SaveNew(_pcA, "from A");
        var stem = Path.GetFileNameWithoutExtension(note.FilePath);
        var other = Note.ParseFromMarkdown(File.ReadAllText(note.FilePath), note.FilePath);
        other.Body = "from B";
        File.WriteAllText(Path.Combine(_notesDir, $"{stem}.sync-conflict-20261120-101112-ABCDEF7.md"), other.ToMarkdown());

        Assert.Single(_pcB.LoadAll());
        Assert.Equal("from B", BodyOf(Assert.Single(_pcB.GetClashes(note.FilePath))));
    }

    [Fact]
    public void AConflictedCopy_OfADifferentNote_WithTheSameName_StaysANoteOfItsOwn()
    {
        // Both PCs made a note with the same title on the same day: two notes, not a clash.
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "a different note", created: new DateTime(2026, 11, 20, 14, 5, 0));

        var notes = _pcB.LoadAll();

        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Body == "a different note" && n.FilePath != note.FilePath);
        Assert.False(_pcB.HasClash(note.FilePath));
        Assert.DoesNotContain(Directory.GetFiles(_notesDir), f => f.Contains("conflicted copy"));
    }

    [Fact]
    public void AConflictedCopy_WhoseNoteIsGone_TakesItsPlace()
    {
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "from B");
        File.Delete(note.FilePath);

        var only = Assert.Single(_pcB.LoadAll());

        Assert.Equal(note.FilePath, only.FilePath);
        Assert.Equal("from B", only.Body);
    }

    [Fact]
    public void AConflictedCopy_ThatSaysTheSameAsTheNote_IsDropped()
    {
        var note = SaveNew(_pcA, "same");
        var copy = DropboxConflictedCopy(note, "same");

        Assert.Single(_pcB.LoadAll());
        Assert.False(File.Exists(copy));
        Assert.False(_pcB.HasClash(note.FilePath));
    }

    [Fact]
    public void Save_OverAChangeThisPcNeverSaw_KeepsBothAndAsks()
    {
        var note = SaveNew(_pcA, "start");
        var onB = Assert.Single(_pcB.LoadAll());
        var startText = _pcB.LastSeenText(onB.FilePath);
        var onA = Assert.Single(_pcA.LoadAll());
        onA.Body = "from A";
        _pcA.Save(onA);
        var found = new List<string>();
        _pcB.ClashFound += n => found.Add(n.FilePath);

        onB.Body = "from B";
        _pcB.Save(onB, basedOn: startText);

        Assert.Equal("from B", BodyAt(note.FilePath));
        Assert.Equal("from A", BodyOf(Assert.Single(_pcB.GetClashes(note.FilePath))));
        Assert.Equal([note.FilePath], found);
    }

    [Fact]
    public void Save_BasedOnWhatIsOnDisk_IsNoClash()
    {
        var note = SaveNew(_pcA, "start");
        var onB = Assert.Single(_pcB.LoadAll());

        onB.Body = "from B";
        _pcB.Save(onB, basedOn: _pcB.LastSeenText(onB.FilePath));

        Assert.False(_pcB.HasClash(note.FilePath));
        Assert.Empty(_pcB.GetClashes(note.FilePath));
    }

    [Fact]
    public void AClash_IsAskedAboutOnEitherPc()
    {
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "from B");
        _pcB.LoadAll();

        _pcA.LoadAll();

        Assert.True(_pcA.HasClash(note.FilePath));
    }

    [Fact]
    public void KeepThis_LeavesTheNote_AndPutsTheOtherInEarlierVersions()
    {
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "from B");
        _pcA.LoadAll();

        _pcA.Resolve(_pcA.GetClashes(note.FilePath)[0], ClashChoice.KeepThis);

        Assert.Equal("from A", BodyAt(note.FilePath));
        Assert.False(_pcA.HasClash(note.FilePath));
        Assert.Empty(_pcA.GetClashes(note.FilePath));
        Assert.Contains("from B", _pcA.GetVersions(note.FilePath).Select(BodyOf));
    }

    [Fact]
    public void KeepOther_PutsItInTheNote_AndThisOneInEarlierVersions()
    {
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "from B");
        _pcA.LoadAll();

        _pcA.Resolve(_pcA.GetClashes(note.FilePath)[0], ClashChoice.KeepOther);

        Assert.Equal("from B", BodyAt(note.FilePath));
        Assert.False(_pcA.HasClash(note.FilePath));
        Assert.Contains("from A", _pcA.GetVersions(note.FilePath).Select(BodyOf));
    }

    [Fact]
    public void Combine_KeepsLinesFromBoth_AndBothVersionsInEarlierVersions()
    {
        var note = SaveNew(_pcA, "intro\nfrom A");
        DropboxConflictedCopy(note, "intro\nfrom B");
        _pcA.LoadAll();

        _pcA.Resolve(_pcA.GetClashes(note.FilePath)[0], ClashChoice.Combine);

        Assert.Equal("intro\nfrom A\nfrom B", BodyAt(note.FilePath));
        var kept = _pcA.GetVersions(note.FilePath).Select(BodyOf).ToList();
        Assert.Contains("intro\nfrom A", kept);
        Assert.Contains("intro\nfrom B", kept);
    }

    [Fact]
    public void ResolvingOnOnePc_ClearsItOnTheOther()
    {
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "from B");
        _pcA.LoadAll();
        _pcB.LoadAll();

        _pcA.Resolve(_pcA.GetClashes(note.FilePath)[0], ClashChoice.KeepThis);
        _pcB.LoadAll();

        Assert.False(_pcB.HasClash(note.FilePath));
    }

    [Fact]
    public void KeepOther_BringsBackThePicturesItShows()
    {
        // A added a picture; B, which never saw it, saved over A. The picture went to
        // history with A's text. Keeping A's version must show the picture again.
        var note = SaveNew(_pcA, "start");
        var onB = Assert.Single(_pcB.LoadAll());
        var startText = _pcB.LastSeenText(onB.FilePath);
        var picture = Path.Combine(_root, "shot.png");
        File.WriteAllBytes(picture, [1, 2, 3]);
        var onA = Assert.Single(_pcA.LoadAll());
        var token = _pcA.AddAttachment(onA, picture);
        onA.Body = $"start {{{{{token}}}}}";
        _pcA.Save(onA);
        onB.Body = "start, edited on B";
        _pcB.Save(onB, basedOn: startText);
        var live = Path.Combine(_pcB.GetAttachmentsDir(onB), token);
        Assert.False(File.Exists(live)); // moved into history by B's save

        _pcB.Resolve(_pcB.GetClashes(note.FilePath)[0], ClashChoice.KeepOther);

        Assert.True(File.Exists(live));
    }

    [Fact]
    public void TheSameOtherVersionHeldTwice_IsAskedAboutOnce()
    {
        // Both PCs may take in the same conflicted copy before either hears of the other.
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "from B");
        _pcA.LoadAll();
        DropboxConflictedCopy(note, "from B");
        _pcB.LoadAll();

        Assert.Single(_pcA.GetClashes(note.FilePath));
    }

    [Fact]
    public void AnOtherVersionThatNowMatchesTheNote_IsNoLongerAskedAbout()
    {
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "from B");
        _pcA.LoadAll();
        var onB = Assert.Single(_pcB.LoadAll());
        onB.Body = "from B";
        _pcB.Save(onB); // the note now says what the other version says

        _pcA.LoadAll();

        Assert.False(_pcA.HasClash(note.FilePath));
    }

    [Fact]
    public void DeletingANoteWithAClash_KeepsTheOtherVersionInHistory()
    {
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "from B");
        _pcA.LoadAll();

        _pcA.Delete(note);

        Assert.Contains("from B", _pcA.GetVersions(note.FilePath).Select(BodyOf));
        Assert.Empty(_pcA.GetClashes(note.FilePath));
    }

    [Fact]
    public void AConflictedCopy_OfADifferentNote_TakesItsPicturesAlong()
    {
        var note = SaveNew(_pcA, "from A");
        var stem = Path.GetFileNameWithoutExtension(note.FilePath);
        var pictures = Path.Combine(_root, "attachments", stem);
        Directory.CreateDirectory(pictures);
        File.WriteAllBytes(Path.Combine(pictures, "b-shot.png"), [9]); // the other PC's note's picture
        DropboxConflictedCopy(note, "a different note {{b-shot.png}}", created: new DateTime(2026, 11, 20, 14, 5, 0));

        var moved = _pcB.LoadAll().Single(n => n.FilePath != note.FilePath);

        Assert.True(File.Exists(Path.Combine(_pcB.GetAttachmentsDir(moved), "b-shot.png")));
    }

    [Fact]
    public void Save_BringsBackAPictureItShows_ThatAnotherSaveMovedAway()
    {
        // A pastes a picture (the file lands at once, its {{token}} only on save). B
        // saves the note first and, not seeing the token, moves the picture to history.
        var note = SaveNew(_pcA, "start");
        var onA = Assert.Single(_pcA.LoadAll());
        var onB = Assert.Single(_pcB.LoadAll());
        var picture = Path.Combine(_root, "shot.png");
        File.WriteAllBytes(picture, [1, 2, 3]);
        var token = _pcA.AddAttachment(onA, picture);
        onB.Body = "start, edited on B";
        _pcB.Save(onB);
        var live = Path.Combine(_pcA.GetAttachmentsDir(onA), token);
        Assert.False(File.Exists(live));

        onA.Body = $"start {{{{{token}}}}}";
        _pcA.Save(onA, basedOn: _pcA.LastSeenText(note.FilePath));

        Assert.True(File.Exists(live));
    }

    [Fact]
    public void KeepThis_BringsBackThePicturesTheNoteShows()
    {
        var note = SaveNew(_pcA, "start");
        var picture = Path.Combine(_root, "shot.png");
        File.WriteAllBytes(picture, [1]);
        var token = _pcA.AddAttachment(note, picture);
        note.Body = $"mine {{{{{token}}}}}";
        _pcA.Save(note);
        DropboxConflictedCopy(note, "theirs, no picture");
        _pcA.LoadAll();
        var live = Path.Combine(_pcA.GetAttachmentsDir(note), token);
        File.Move(live, live + ".gone"); // as if another save had moved it into history
        var kept = Path.Combine(_root, "history", Path.GetFileNameWithoutExtension(note.FilePath), "attachments");
        Directory.CreateDirectory(kept);
        File.Move(live + ".gone", Path.Combine(kept, token));

        _pcA.Resolve(_pcA.GetClashes(note.FilePath)[0], ClashChoice.KeepThis);

        Assert.True(File.Exists(live));
    }

    [Fact]
    public void AClashWhoseNoteThisPcHasNotSeenYet_KeepsWaiting()
    {
        // A new PC: Dropbox brought the clash folder before the note itself.
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "from B");
        _pcA.LoadAll();
        var noteText = File.ReadAllText(note.FilePath);
        File.Delete(note.FilePath); // not downloaded yet on the new PC
        using var newPc = new NoteService(_notesDir);

        newPc.LoadAll();
        File.WriteAllText(note.FilePath, noteText); // the note arrives
        newPc.LoadAll();

        Assert.True(newPc.HasClash(note.FilePath));
        Assert.Equal("from B", BodyOf(Assert.Single(newPc.GetClashes(note.FilePath))));
    }

    [Fact]
    public void Resolve_WhenTheOtherPcAlreadySettledIt_ChangesNothing()
    {
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "from B");
        _pcA.LoadAll();
        var clash = _pcA.GetClashes(note.FilePath)[0];
        File.Delete(clash.VersionPath); // settled on the other PC

        _pcA.Resolve(clash, ClashChoice.KeepOther);

        Assert.Equal("from A", BodyAt(note.FilePath));
    }

    [Fact]
    public void Delete_WithTheOtherVersionStampedLater_DeletedNotesStillBringsBackWhatWasDeleted()
    {
        // The other PC's clock runs ahead, so its version carries a later time.
        var note = SaveNew(_pcA, "from A");
        var stem = Path.GetFileNameWithoutExtension(note.FilePath);
        var dir = Path.Combine(_root, "conflicts", stem);
        Directory.CreateDirectory(dir);
        var other = Note.ParseFromMarkdown(File.ReadAllText(note.FilePath), note.FilePath);
        other.Body = "from B";
        File.WriteAllText(Path.Combine(dir, DateTime.Now.AddHours(1).ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".md"), other.ToMarkdown());
        _pcA.LoadAll();

        _pcA.Delete(note);

        Assert.Equal("from A", BodyOf(Assert.Single(_pcA.GetDeleted())));
        Assert.Contains("from B", _pcA.GetVersions(note.FilePath).Select(BodyOf));
    }

    [Fact]
    public void Save_OverAFileNowHoldingADifferentNote_MakesANewNote_AndLeavesTheOtherAlone()
    {
        // The moment between Dropbox putting the other PC's same-name note at this
        // note's file and the app noticing: a save then must not land on the other note.
        var mine = SaveNew(_pcB, "my meeting", title: "Meeting");
        var onB = Assert.Single(_pcB.LoadAll());
        var theirs = new Note { Title = "Meeting", Body = "their meeting", Created = new DateTime(2026, 11, 20, 14, 5, 0) };
        File.WriteAllText(mine.FilePath, theirs.ToMarkdown());

        onB.Body = "my meeting, typed here";
        _pcB.Save(onB, basedOn: _pcB.LastSeenText(mine.FilePath));

        Assert.Equal("their meeting", BodyAt(mine.FilePath));
        Assert.NotEqual(mine.FilePath, onB.FilePath);
        Assert.Equal("my meeting, typed here", BodyAt(onB.FilePath));
        Assert.False(_pcB.HasClash(mine.FilePath));
    }

    [Fact]
    public void ASaveInTheSyncGap_ThenItsConflictedCopy_MakeOneNote_NotTwo()
    {
        // This PC saved its note under a free name (the guard above) before the sync
        // tool's copy of the older text was taken in: the copy is that note's other version.
        var mine = SaveNew(_pcB, "my meeting", title: "Meeting");
        var onB = Assert.Single(_pcB.LoadAll());
        var older = File.ReadAllText(mine.FilePath);
        var theirs = new Note { Title = "Meeting", Body = "their meeting", Created = new DateTime(2026, 11, 20, 14, 5, 0) };
        File.WriteAllText(mine.FilePath, theirs.ToMarkdown());
        onB.Body = "my meeting, typed here";
        _pcB.Save(onB, basedOn: older);
        File.WriteAllText(Path.Combine(_notesDir, Path.GetFileNameWithoutExtension(mine.FilePath) + " (JL's conflicted copy 2026-11-20).md"), older);

        var notes = _pcB.LoadAll();

        Assert.Equal(2, notes.Count);
        Assert.Equal("my meeting, typed here", BodyAt(onB.FilePath));
        Assert.Equal("my meeting", BodyOf(Assert.Single(_pcB.GetClashes(onB.FilePath))));
    }

    [Fact]
    public void ASaveOntoANoteDeletedElsewhere_KeepsItsPictures()
    {
        // A tick or priority change on a note the other PC deleted a moment ago.
        var note = SaveNew(_pcA, "start");
        var picture = Path.Combine(_root, "shot.png");
        File.WriteAllBytes(picture, [1]);
        var token = _pcA.AddAttachment(note, picture);
        note.Body = $"start {{{{{token}}}}}";
        _pcA.Save(note);
        _pcB.Delete(Assert.Single(_pcB.LoadAll()));

        note.Priority = NotePriority.High;
        _pcA.Save(note);

        Assert.True(File.Exists(Path.Combine(_pcA.GetAttachmentsDir(note), token)));
    }

    [Fact]
    public void Save_WhileTheSyncToolHoldsTheFileAMoment_StillSaves()
    {
        var note = SaveNew(_pcA, "start");
        var held = new FileStream(note.FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        var release = Task.Delay(200).ContinueWith(_ => held.Dispose());

        note.Body = "saved anyway";
        _pcA.Save(note);
        release.Wait();

        Assert.Equal("saved anyway", BodyAt(note.FilePath));
    }

    [Fact]
    public void AClashForANoteDeletedWhileThisPcWasOff_IsInDeletedNotes()
    {
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "from B");
        _pcA.LoadAll();
        File.Delete(note.FilePath); // deleted on another PC while this one was off
        using var restarted = new NoteService(_notesDir);

        restarted.LoadAll();

        var deleted = Assert.Single(restarted.GetDeleted());
        Assert.Equal(note.FilePath, deleted.NotePath);
        Assert.Equal("from B", BodyOf(deleted));
    }

    [Theory]
    [InlineData("plan.md", "Just a plan, written by hand.")] // created comes from the file's time
    [InlineData("plan.md", "---\ntitle: Plan\ncreated: 2026-11-20T09:30:00.123\n---\n\nA plan.")]
    public void SavingTwice_ANoteWhoseCreatedTimeTheFileCannotHold_StaysOneNote(string fileName, string text)
    {
        // A file keeps times to the second. A note read with more than that is still
        // the same note once its file is written, not "a different note" to move away from.
        Directory.CreateDirectory(_notesDir);
        File.WriteAllText(Path.Combine(_notesDir, fileName), text);
        var note = _pcA.LoadAll().Single();

        note.Body += " One.";
        _pcA.Save(note);
        note.Body += " Two.";
        _pcA.Save(note);

        Assert.Equal([fileName], Directory.GetFiles(_notesDir).Select(Path.GetFileName));
        Assert.EndsWith("One. Two.", BodyAt(Path.Combine(_notesDir, fileName)));
    }

    [Fact]
    public void ANoteWhoseFileStatesNoCreatedTime_IsTheSameNoteWhateverItsFileDate()
    {
        // Written by hand twice (no header, no date in the name): the file's own date is all
        // there is, and it moves with every write. Nothing can tell the two apart, so the
        // conflicted copy is the other version of that note, not a note of its own.
        Directory.CreateDirectory(_notesDir);
        var path = Path.Combine(_notesDir, "plan.md");
        File.WriteAllText(path, "Plan, here.");
        File.SetLastWriteTime(path, new DateTime(2026, 11, 20, 9, 0, 0));
        _pcA.LoadAll();
        var copy = Path.Combine(_notesDir, "plan (JL's conflicted copy 2026-11-20).md");
        File.WriteAllText(copy, "Plan, there.");
        File.SetLastWriteTime(copy, new DateTime(2026, 11, 20, 9, 5, 0));

        _pcA.LoadAll();

        Assert.Equal(["plan.md"], Directory.GetFiles(_notesDir).Select(Path.GetFileName));
        Assert.True(_pcA.HasClash(path));
    }

    [Fact]
    public void AConflictedCopy_ThatDiffersOnlyInWhatTheAppDoesNotShow_IsStillKept()
    {
        // A header line the app has no field for (added by hand) reads the same in the
        // app, but it is still text someone wrote: it goes to Earlier Versions, not away.
        var note = SaveNew(_pcA, "the plan");
        var stem = Path.GetFileNameWithoutExtension(note.FilePath);
        var copyText = File.ReadAllText(note.FilePath).Replace("title: Plan", "title: Plan\ndue: 2026-12-01");
        File.WriteAllText(Path.Combine(_notesDir, $"{stem} (JL's conflicted copy 2026-11-20).md"), copyText);

        _pcA.LoadAll();

        Assert.Single(Directory.GetFiles(_notesDir));
        Assert.False(_pcA.HasClash(note.FilePath));
        Assert.Contains(_pcA.GetVersions(note.FilePath), v => File.ReadAllText(v.VersionPath) == copyText);
    }

    [Fact]
    public void AConflictedCopyLikeAWaitingVersion_ButWithALineTheAppHasNoFieldFor_IsStillKept()
    {
        var note = SaveNew(_pcA, "from A");
        var copy = DropboxConflictedCopy(note, "from B");
        _pcA.LoadAll();
        var withLine = File.ReadAllText(note.FilePath).Replace("from A", "from B").Replace("title: Plan", "title: Plan\ndue: 2026-12-01");
        File.WriteAllText(copy, withLine);

        _pcA.LoadAll();

        Assert.False(File.Exists(copy));
        Assert.Contains(Directory.GetFiles(_root, "*", SearchOption.AllDirectories), f => File.ReadAllText(f).Contains("due: 2026-12-01"));
    }

    [Fact]
    public void ANoteMovedInTheListOnOnePc_IsNoNewVersionOnTheOther()
    {
        var note = SaveNew(_pcA, "the plan");
        _pcB.LoadAll();
        note.SortOrder = 3;
        _pcA.Save(note, stampUpdated: false); // a drag: no version on this PC

        _pcB.LoadAll();

        Assert.Empty(_pcB.GetVersions(note.FilePath));
    }

    [Fact]
    public void TheSameVersionKeptOnTwoPcs_IsListedOnce()
    {
        // Each PC kept what the note said before an outside change, a moment apart.
        var note = SaveNew(_pcA, "the plan");
        var dir = Path.Combine(_root, "history", Path.GetFileNameWithoutExtension(note.FilePath));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "2026-11-20_10-00-00-001.md"), "older");
        File.WriteAllText(Path.Combine(dir, "2026-11-20_10-25-44-024.md"), "before");
        File.WriteAllText(Path.Combine(dir, "2026-11-20_10-25-44-025.md"), "before");

        Assert.Equal(["before", "older"], _pcA.GetVersions(note.FilePath).Select(v => File.ReadAllText(v.VersionPath)));
    }

    [Fact]
    public void MergeIn_ANoteTheFolderLacks_IsAdded()
    {
        var text = new Note { Title = "Elsewhere", Body = "x", Created = new DateTime(2026, 11, 1) }.ToMarkdown();

        _pcA.MergeIn("2026-11-01-elsewhere.md", text);

        Assert.Equal("x", BodyAt(Path.Combine(_notesDir, "2026-11-01-elsewhere.md")));
    }

    [Fact]
    public void MergeIn_ANewerVersionOfThatSameNote_BecomesTheNote_Quietly()
    {
        // The folder's text is an earlier version of the incoming one (it is in the
        // incoming note's own history, in the folder it comes from): nothing to decide.
        var note = SaveNew(_pcA, "older");
        var older = File.ReadAllText(note.FilePath);
        var fromNotes = Path.Combine(_root, "elsewhere", "notes");
        using (var elsewhere = new NoteService(fromNotes))
        {
            var there = Note.ParseFromMarkdown(older, Path.Combine(fromNotes, Path.GetFileName(note.FilePath)));
            File.WriteAllText(there.FilePath, older);
            elsewhere.LoadAll();
            there.Body = "newer";
            elsewhere.Save(there); // "older" is now in its history there
            there.Updated = DateTime.Now.AddMinutes(5); // saved later (times are kept to the second)
            elsewhere.Save(there, stampUpdated: false);
        }
        var newer = File.ReadAllText(Path.Combine(fromNotes, Path.GetFileName(note.FilePath)));

        _pcA.MergeIn(Path.GetFileName(note.FilePath), newer, fromNotes);

        Assert.Equal("newer", BodyAt(note.FilePath));
        Assert.False(_pcA.HasClash(note.FilePath));
    }

    [Fact]
    public void MergeIn_BothChangedTheNote_TheNewerIsTheNote_AndTheOtherIsAskedAbout()
    {
        var note = SaveNew(_pcA, "changed here");
        var elsewhere = Note.ParseFromMarkdown(File.ReadAllText(note.FilePath), note.FilePath);
        elsewhere.Body = "changed elsewhere";
        elsewhere.Updated = DateTime.Now.AddMinutes(5);

        _pcA.MergeIn(Path.GetFileName(note.FilePath), elsewhere.ToMarkdown());

        Assert.Equal("changed elsewhere", BodyAt(note.FilePath));
        Assert.Equal("changed here", BodyOf(Assert.Single(_pcA.GetClashes(note.FilePath))));
        Assert.True(_pcA.HasClash(note.FilePath));
    }

    [Fact]
    public void MergeIn_AnOlderUnseenVersion_IsAskedAbout_AndTheNoteStays()
    {
        var note = SaveNew(_pcA, "newer");
        var older = Note.ParseFromMarkdown(File.ReadAllText(note.FilePath), note.FilePath);
        older.Body = "older, changed elsewhere";
        older.Updated = DateTime.Now.AddDays(-3);

        _pcA.MergeIn(Path.GetFileName(note.FilePath), older.ToMarkdown());

        Assert.Equal("newer", BodyAt(note.FilePath));
        Assert.Equal("older, changed elsewhere", BodyOf(Assert.Single(_pcA.GetClashes(note.FilePath))));
    }

    [Fact]
    public void AClashForANoteDeletedOnTheOtherPc_GoesToItsHistory()
    {
        var note = SaveNew(_pcA, "from A");
        DropboxConflictedCopy(note, "from B");
        _pcA.LoadAll();
        File.Delete(note.FilePath); // the other PC deleted it

        _pcA.LoadAll();

        Assert.Empty(_pcA.GetClashes(note.FilePath));
        Assert.Contains("from B", _pcA.GetVersions(note.FilePath).Select(BodyOf));
    }
}
