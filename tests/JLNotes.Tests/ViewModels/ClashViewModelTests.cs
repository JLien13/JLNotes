using System.IO;
using JLNotes.Models;
using JLNotes.Services;
using JLNotes.ViewModels;

namespace JLNotes.Tests.ViewModels;

// In the app: a note open with unsaved typing while the other PC changes it keeps
// both versions when it saves, shows that it has two, and the choice made puts the
// right text on screen. Plain-text bodies, so FlowDocuments build on the test thread.
public class ClashViewModelTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _notesDir;
    private readonly NoteService _thisPc;
    private readonly NoteService _otherPc;
    private readonly SettingsService _settingsService;
    private readonly ProjectService _projectService;

    public ClashViewModelTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"jlnotes-clash-vm-{Guid.NewGuid()}");
        _notesDir = Path.Combine(_testDir, "notes");
        Directory.CreateDirectory(_notesDir);
        _thisPc = new NoteService(_notesDir);
        _otherPc = new NoteService(_notesDir);
        _settingsService = new SettingsService(_testDir);
        _projectService = new ProjectService(_testDir);
    }

    public void Dispose()
    {
        _thisPc.Dispose();
        _otherPc.Dispose();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private NoteItemViewModel OpenOnThisPc(string title, string body)
    {
        var note = new Note { Title = title, Body = body, Created = new DateTime(2026, 11, 20, 9, 30, 0) };
        _thisPc.Save(note);
        var loaded = _thisPc.LoadAll().Single();
        var vm = new NoteItemViewModel(loaded, _thisPc, _settingsService, new ProjectCatalog());
        vm.LoadEditor();
        return vm;
    }

    private void OtherPcWrites(string body)
    {
        var note = _otherPc.LoadAll().Single();
        note.Body = body;
        _otherPc.Save(note);
    }

    /// <summary>The folder watcher's refresh: read the folder again, hand the note to its view model.</summary>
    private void Refresh(NoteItemViewModel vm) => vm.Reload(_thisPc.LoadAll().Single());

    [Fact]
    public void TypingDropped_ThenAnotherChange_AsksNothing()
    {
        // The other PC's change arrived while typing, so it is on screen once the typing
        // is dropped (Esc): a change made after that is based on it, not a clash.
        var vm = OpenOnThisPc("Plan", "start");
        vm.EditTitle = "Plan, retitled here";
        OtherPcWrites("from the other PC");
        Refresh(vm);
        vm.ToggleExpandCommand.Execute(null); // open...
        vm.ToggleExpandCommand.Execute(null); // ...and closed again without saving

        vm.SetPriorityHighCommand.Execute(null);

        Assert.False(vm.HasClash);
        Assert.Equal("from the other PC", vm.Note.Body);
    }

    [Fact]
    public void ANoteWrittenByHand_IsStillTheSameNoteAfterItsFirstSave()
    {
        // No header and no date in the name: its created time comes from the file's date
        // until the first save writes one. The open editor must stay on it afterwards.
        File.WriteAllText(Path.Combine(_notesDir, "plan.md"), "Written by hand.");
        var vm = new NoteItemViewModel(_thisPc.LoadAll().Single(), _thisPc, _settingsService, new ProjectCatalog());
        vm.LoadEditor();
        vm.EditTitle = "Plan";
        vm.SaveEditor();
        vm.EditTitle = "Plan, retitled";

        var main = _thisPc.LoadAll().Single();
        Assert.Equal(vm.Note.Created, main.Created);
        Assert.Single(Directory.GetFiles(_notesDir));
    }

    [Fact]
    public void UnsavedTyping_OverAChangeFromTheOtherPc_KeepsBothAndShowsIt()
    {
        var vm = OpenOnThisPc("Plan", "start");
        vm.EditTitle = "Plan, retitled here";
        OtherPcWrites("from the other PC");
        Refresh(vm); // arrives while this PC has unsaved typing

        vm.SaveEditor();

        Assert.Equal("Plan, retitled here", vm.Note.Title);
        Assert.True(vm.HasClash);
        Assert.Equal("from the other PC", Assert.Single(_thisPc.GetClashes(vm.Note.FilePath)).Read().Body);
    }

    [Fact]
    public void NothingUnsaved_FollowsTheOtherPc_WithNoClash()
    {
        var vm = OpenOnThisPc("Plan", "start");
        OtherPcWrites("from the other PC");
        Refresh(vm);

        vm.SaveEditor();

        Assert.False(vm.HasClash);
        Assert.Equal("from the other PC", vm.Note.Body);
    }

    [Fact]
    public void ChangingPriority_AfterTheOtherPcsChangeArrived_IsNoClash()
    {
        var vm = OpenOnThisPc("Plan", "start");
        OtherPcWrites("from the other PC");
        Refresh(vm);

        vm.SetPriorityHighCommand.Execute(null);

        Assert.False(vm.HasClash);
        Assert.Equal("from the other PC", vm.Note.Body);
    }

    [Fact]
    public void KeepOther_PutsTheOtherVersionOnScreen()
    {
        var vm = OpenOnThisPc("Plan", "start");
        vm.EditTitle = "Mine";
        OtherPcWrites("from the other PC");
        Refresh(vm);
        vm.SaveEditor();
        Refresh(vm);

        vm.Resolve(ClashChoice.KeepOther);
        Refresh(vm);

        Assert.False(vm.HasClash);
        Assert.Equal("Plan", vm.Note.Title);
        Assert.Equal("Plan", vm.EditTitle);
        Assert.Equal("from the other PC", vm.Note.Body);
    }

    [Fact]
    public void KeepThis_LeavesWhatIsOnScreen()
    {
        var vm = OpenOnThisPc("Plan", "start");
        vm.EditTitle = "Mine";
        OtherPcWrites("from the other PC");
        Refresh(vm);
        vm.SaveEditor();
        Refresh(vm);

        vm.Resolve(ClashChoice.KeepThis);
        Refresh(vm);

        Assert.False(vm.HasClash);
        Assert.Equal("Mine", vm.Note.Title);
        Assert.Equal("Mine", vm.EditTitle);
    }

    [Fact]
    public void Resolve_SettlesTheVersionShown_EvenIfAnotherArrivedMeanwhile()
    {
        var vm = OpenOnThisPc("Plan", "start");
        var stem = Path.GetFileNameWithoutExtension(vm.Note.FilePath);
        var dir = Path.Combine(_testDir, "conflicts", stem);
        Directory.CreateDirectory(dir);
        string Held(string body, DateTime at)
        {
            var other = Note.ParseFromMarkdown(File.ReadAllText(vm.Note.FilePath), vm.Note.FilePath);
            other.Body = body;
            var path = Path.Combine(dir, at.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".md");
            File.WriteAllText(path, other.ToMarkdown());
            return path;
        }
        var shownPath = Held("the one shown", new DateTime(2026, 11, 20, 10, 0, 0));
        Refresh(vm);
        var shown = _thisPc.GetClashes(vm.Note.FilePath).Single();
        Held("arrived while the window was open", new DateTime(2026, 11, 20, 9, 0, 0)); // older stamp: now first
        Refresh(vm);

        vm.Resolve(ClashChoice.KeepOther, shown);

        Assert.Equal(shownPath, shown.VersionPath);
        Assert.Equal("the one shown", Note.ParseFromMarkdown(File.ReadAllText(vm.Note.FilePath), vm.Note.FilePath).Body);
    }

    [Fact]
    public void TwoNotesWithOneName_WhileTheCopyIsStillSyncing_TypingNeverLandsOnTheOtherNote()
    {
        // As in TwoNotesWithOneName_TheOpenEditorsTypingStaysWithItsOwnNote, but the renamed
        // copy cannot be read yet when the app looks.
        var mine = new Note { Title = "Meeting", Body = "my meeting", Created = new DateTime(2026, 11, 20, 9, 0, 0) };
        _thisPc.Save(mine);
        var main = new MainViewModel(_thisPc, _projectService, _settingsService) { ViewMode = "split" };
        var item = main.SplitNotes.Single();
        main.SelectedSplitNote = item;
        item.EditTitle = "Meeting, typed here";

        var theirs = new Note { Title = "Meeting", Body = "their meeting", Created = new DateTime(2026, 11, 20, 9, 45, 0) };
        var copy = Path.Combine(_notesDir, Path.GetFileNameWithoutExtension(mine.FilePath) + " (JL's conflicted copy 2026-11-20).md");
        File.Move(mine.FilePath, copy);
        File.WriteAllText(mine.FilePath, theirs.ToMarkdown());
        using (File.Open(copy, FileMode.Open, FileAccess.Read, FileShare.None))
            main.RefreshNotes();
        main.SaveSplitEditor();
        main.RefreshNotes(); // the copy arrives

        var notes = _thisPc.LoadAll();
        var atName = notes.Single(n => n.FilePath == mine.FilePath);
        Assert.Equal("their meeting", atName.Body);
        Assert.Equal(new DateTime(2026, 11, 20, 9, 45, 0), atName.Created);
        Assert.False(_thisPc.HasClash(mine.FilePath));
        Assert.Contains(notes, n => n.Title == "Meeting, typed here");
        Assert.Equal(2, notes.Count); // never a third copy of this PC's note
    }

    [Fact]
    public void TwoNotesWithOneName_TheOpenEditorsTypingStaysWithItsOwnNote()
    {
        // This PC and the other both made "Meeting" today. Dropbox keeps the other PC's
        // at the name and renames this PC's to a conflicted copy, which becomes -2.
        var mine = new Note { Title = "Meeting", Body = "my meeting", Created = new DateTime(2026, 11, 20, 9, 0, 0) };
        _thisPc.Save(mine);
        var main = new MainViewModel(_thisPc, _projectService, _settingsService) { ViewMode = "split" };
        var item = main.SplitNotes.Single();
        main.SelectedSplitNote = item;
        item.EditTitle = "Meeting, typed here";

        var theirs = new Note { Title = "Meeting", Body = "their meeting", Created = new DateTime(2026, 11, 20, 9, 45, 0) };
        File.Move(mine.FilePath, Path.Combine(_notesDir, Path.GetFileNameWithoutExtension(mine.FilePath) + " (JL's conflicted copy 2026-11-20).md"));
        File.WriteAllText(mine.FilePath, theirs.ToMarkdown());
        main.RefreshNotes();
        main.SaveSplitEditor();

        var notes = _thisPc.LoadAll();
        Assert.Equal(2, notes.Count);
        Assert.Equal("their meeting", notes.Single(n => n.FilePath == mine.FilePath).Body);
        Assert.Equal("Meeting, typed here", notes.Single(n => n.FilePath != mine.FilePath).Title);
        Assert.False(_thisPc.HasClash(mine.FilePath));
    }

    [Fact]
    public void ANoteDeletedElsewhereWhileTyping_KeepsTheTypingAsANewNote()
    {
        var note = new Note { Title = "Plan", Body = "start", Created = new DateTime(2026, 11, 20, 9, 0, 0) };
        _thisPc.Save(note);
        var main = new MainViewModel(_thisPc, _projectService, _settingsService) { ViewMode = "split" };
        var item = main.SplitNotes.Single();
        main.SelectedSplitNote = item;
        item.EditTitle = "Plan, typed here";

        File.Delete(note.FilePath); // deleted on the other PC
        main.RefreshNotes();

        Assert.Equal("Plan, typed here", Assert.Single(_thisPc.LoadAll()).Title);
        Assert.Contains("deleted", main.NoticeText);
    }

    [Fact]
    public void ANoteLockedForAMomentWhileTyping_StaysOpen_AndNothingIsMade()
    {
        var note = new Note { Title = "Plan", Body = "start", Created = new DateTime(2026, 11, 20, 9, 0, 0) };
        _thisPc.Save(note);
        var main = new MainViewModel(_thisPc, _projectService, _settingsService) { ViewMode = "split" };
        var item = main.SplitNotes.Single();
        main.SelectedSplitNote = item;
        item.EditTitle = "Plan, typed here";

        using (File.Open(note.FilePath, FileMode.Open, FileAccess.Read, FileShare.None)) // the sync tool has it
            main.RefreshNotes();

        Assert.Same(item, main.SelectedSplitNote);
        Assert.Null(main.NoticeText);
        Assert.Single(Directory.GetFiles(_notesDir));
        main.SaveSplitEditor();
        Assert.Equal("Plan, typed here", _thisPc.LoadAll().Single().Title);
    }

    /// <summary>A note showing a picture builds image controls, which need a UI (STA)
    /// thread; xUnit runs tests on MTA threads.</summary>
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void TypingKeptFromADeletedNote_StillShowsItsPictures() => Sta(() =>
    {
        var note = new Note { Title = "Plan", Body = "start", Created = new DateTime(2026, 11, 20, 9, 0, 0) };
        _thisPc.Save(note);
        var picture = Path.Combine(_testDir, "shot.png");
        File.WriteAllBytes(picture, [1, 2, 3]);
        var token = _thisPc.AddAttachment(note, picture);
        note.Body = $"start {{{{{token}}}}}";
        _thisPc.Save(note);
        var main = new MainViewModel(_thisPc, _projectService, _settingsService) { ViewMode = "split" };
        var item = main.SplitNotes.Single();
        main.SelectedSplitNote = item;
        item.EditTitle = "Plan, typed here";

        _otherPc.Delete(_otherPc.LoadAll().Single()); // its pictures go to its history
        main.RefreshNotes();

        var kept = _thisPc.LoadAll().Single();
        Assert.Equal("Plan, typed here", kept.Title);
        Assert.True(File.Exists(Path.Combine(_thisPc.GetAttachmentsDir(kept), token)));
    });

    [Fact]
    public void ANoteWithNoHeader_RewrittenOutside_KeepsItsOpenEditor()
    {
        // With no header and no date in its name, a note's created time comes from its
        // file's date, which changes with every outside write.
        var path = Path.Combine(_notesDir, "plain.md");
        File.WriteAllText(path, "# Plain\n\nfirst");
        File.SetLastWriteTime(path, new DateTime(2026, 11, 1, 8, 0, 0));
        var main = new MainViewModel(_thisPc, _projectService, _settingsService) { ViewMode = "split" };
        var item = main.SplitNotes.Single();
        main.SelectedSplitNote = item;
        item.EditTitle = "Plain, typed here";

        File.WriteAllText(path, "# Plain\n\nwritten by Claude");
        File.SetLastWriteTime(path, new DateTime(2026, 11, 20, 8, 0, 0));
        main.RefreshNotes();

        Assert.Same(item, main.SelectedSplitNote);
        Assert.Null(main.NoticeText);
        Assert.Equal("Plain, typed here", item.EditTitle);
    }

    [Fact]
    public void TheMainList_ShowsAClashFoundInTheFolder()
    {
        var note = new Note { Title = "Plan", Body = "from A", Created = new DateTime(2026, 11, 20, 9, 30, 0) };
        _thisPc.Save(note);
        var other = Note.ParseFromMarkdown(File.ReadAllText(note.FilePath), note.FilePath);
        other.Body = "from B";
        File.WriteAllText(Path.Combine(_notesDir,
            Path.GetFileNameWithoutExtension(note.FilePath) + " (JL's conflicted copy 2026-11-20).md"), other.ToMarkdown());

        var main = new MainViewModel(_thisPc, _projectService, _settingsService) { ViewMode = "split" };

        var item = Assert.Single(main.SplitNotes);
        Assert.True(item.HasClash);
    }
}
