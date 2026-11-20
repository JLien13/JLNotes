using System.IO;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.Tests.Services;

// Where the notes live is a choice made in Settings, one per PC. Pointing several
// PCs at one Dropbox folder is how they share notes. Window places and other
// settings never move: they stay in this PC's own folder.
public class NotesFolderTests : IDisposable
{
    private readonly string _root;
    private readonly string _localDir;
    private readonly SettingsService _settings;

    public NotesFolderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"jlnotes-folder-{Guid.NewGuid()}");
        _localDir = Path.Combine(_root, ".jlnotes");
        Directory.CreateDirectory(_localDir);
        _settings = new SettingsService(_localDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private string Dir(string name) => Path.Combine(_root, name);

    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Runs <paramref name="act"/> while this user may not list <paramref name="dir"/>.</summary>
    private static void WhileUnreadable(string dir, Action act)
    {
        var user = Environment.UserName;
        void Icacls(string args)
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("icacls", args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            p.WaitForExit();
        }
        Icacls($"\"{dir}\" /deny \"{user}\":(RD)");
        try { act(); }
        finally { Icacls($"\"{dir}\" /remove:d \"{user}\""); }
    }

    /// <summary>A move that stops partway (the sync tool holds one of the note's pictures),
    /// then is picked again, as the error message says to.</summary>
    private void MoveStoppedPartwayThenRetried(string target, string heldPicture)
    {
        using (new FileStream(heldPicture, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => NotesFolder.Change(_settings, _localDir, _localDir, target));
        NotesFolder.Change(_settings, _localDir, _localDir, target);
    }

    private static string[] AllTextUnder(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(File.ReadAllText).ToArray() : [];

    /// <summary>A notes folder as the app leaves it: notes, a picture, history, projects.</summary>
    private void FillLocal()
    {
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-20-one.md"), "one");
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-20-two.md"), "two");
        WriteFile(Path.Combine(_localDir, "attachments", "2026-11-20-one", "pic.png"), "png");
        WriteFile(Path.Combine(_localDir, "history", "2026-11-20-one", "2026-11-19_10-00-00-000.md"), "old");
        WriteFile(Path.Combine(_localDir, "projects.json"), "[]");
    }

    [Fact]
    public void DataDir_WithNoChoiceMade_IsThisPcsOwnFolder()
    {
        Assert.Equal(_localDir, NotesFolder.DataDirFor(new AppSettings(), _localDir));
    }

    [Fact]
    public void DataDir_IsTheChosenFolder()
    {
        var chosen = Dir("Dropbox");

        Assert.Equal(chosen, NotesFolder.DataDirFor(new AppSettings { NotesFolder = chosen }, _localDir));
    }

    [Fact]
    public void DataDir_IsReadFromThisPcsSettings()
    {
        var chosen = Dir("Dropbox");
        _settings.Save(new AppSettings { NotesFolder = chosen });

        Assert.Equal(chosen, NotesFolder.DataDirFor(_localDir));
    }

    [Fact]
    public void ProblemWith_TheSameFolder_SaysSo()
    {
        Assert.NotNull(NotesFolder.ProblemWith(_localDir, _localDir, _localDir + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void ProblemWith_AFolderInsideTheNotesFolder_SaysSo()
    {
        Assert.NotNull(NotesFolder.ProblemWith(_localDir, _localDir, Path.Combine(_localDir, "notes", "deeper")));
    }

    [Fact]
    public void ProblemWith_AnOrdinaryFolder_IsNothing()
    {
        Assert.Null(NotesFolder.ProblemWith(_localDir, _localDir, Dir("Dropbox")));
    }

    [Fact]
    public void ProblemWith_TheFolderTheNoteFilesAreIn_PointsToTheOneAboveIt()
    {
        // Another PC's note files are in the "notes" folder of the shared one; picking
        // that would put a second notes folder inside it, and this PC would not see them.
        var shared = Dir("JL Notes");
        WriteFile(Path.Combine(shared, "notes", "2026-11-20-one.md"), "one");

        var problem = NotesFolder.ProblemWith(_localDir, _localDir, Path.Combine(shared, "notes"));

        Assert.NotNull(problem);
        Assert.Contains(shared, problem);
    }

    [Fact]
    public void ProblemWith_AFolderWithOtherThingsInIt_SaysToMakeANewOne()
    {
        // The Dropbox folder itself, or Documents: the notes would mix with other files.
        WriteFile(Path.Combine(Dir("Dropbox"), "report.docx"), "x");

        Assert.NotNull(NotesFolder.ProblemWith(_localDir, _localDir, Dir("Dropbox")));
    }

    [Fact]
    public void ProblemWith_AnotherPcsNotesFolder_IsNothing()
    {
        var shared = Dir("JL Notes");
        WriteFile(Path.Combine(shared, "notes", "2026-11-20-one.md"), "one");
        WriteFile(Path.Combine(shared, "history", "2026-11-20-one", "2026-11-19_10-00-00-000.md"), "old");
        WriteFile(Path.Combine(shared, "projects.json"), "[]");
        WriteFile(Path.Combine(shared, "projects (JL's conflicted copy 2026-11-20).json"), "[]");
        WriteFile(Path.Combine(shared, "desktop.ini"), "");

        Assert.Null(NotesFolder.ProblemWith(_localDir, _localDir, shared));
    }

    [Fact]
    public void ProblemWith_GoingBackToThisPcsOwnFolder_IsNothing()
    {
        // It holds this PC's settings too, and that is fine.
        WriteFile(Path.Combine(_localDir, "settings.json"), "{}");

        Assert.Null(NotesFolder.ProblemWith(_localDir, Dir("Dropbox"), _localDir));
    }

    [Fact]
    public void ForThisRun_AFolderThatIsThere_IsUsedWithoutAsking()
    {
        Directory.CreateDirectory(Dir("Dropbox"));
        _settings.Save(new AppSettings { NotesFolder = Dir("Dropbox") });

        Assert.Equal(Dir("Dropbox"), NotesFolder.ForThisRun(_settings, _localDir, _ => throw new InvalidOperationException("asked")));
    }

    [Fact]
    public void ForThisRun_AMissingFolder_UsesThisPcsOwnFolder_OnlyIfTheUserSaysSo()
    {
        _settings.Save(new AppSettings { NotesFolder = Dir("Dropbox") });
        string? asked = null;

        var dataDir = NotesFolder.ForThisRun(_settings, _localDir, problem => { asked = problem; return true; });

        Assert.Equal(_localDir, dataDir);
        Assert.Contains(Dir("Dropbox"), asked);
        Assert.Equal("", _settings.Load().NotesFolder);
        Assert.Null(NotesFolder.FolderToGoBackTo(_settings.Load(), _localDir)); // still gone
    }

    [Fact]
    public void FolderToGoBackTo_AfterUsingThisPcsFolder_IsTheMissingFolderOnceItIsBack()
    {
        _settings.Save(new AppSettings { NotesFolder = Dir("Dropbox") });
        NotesFolder.ForThisRun(_settings, _localDir, _ => true);
        WriteFile(Path.Combine(Dir("Dropbox"), "notes", "2026-11-20-one.md"), "one");

        Assert.Equal(Dir("Dropbox"), NotesFolder.FolderToGoBackTo(_settings.Load(), _localDir));
    }

    [Fact]
    public void FolderToGoBackTo_IsForgottenOnceTheFolderIsChanged()
    {
        _settings.Save(new AppSettings { NotesFolder = Dir("Dropbox") });
        NotesFolder.ForThisRun(_settings, _localDir, _ => true);
        Directory.CreateDirectory(Dir("Dropbox"));

        NotesFolder.Change(_settings, _localDir, _localDir, Dir("Dropbox"));

        Assert.Equal(Dir("Dropbox"), _settings.Load().NotesFolder);
        Assert.Null(NotesFolder.FolderToGoBackTo(_settings.Load(), Dir("Dropbox")));
        Assert.Equal("", _settings.Load().NotesFolderAway);
    }

    [Fact]
    public void FolderToGoBackTo_IsForgottenWhenTheUserSaysNo()
    {
        _settings.Save(new AppSettings { NotesFolder = Dir("Dropbox") });
        NotesFolder.ForThisRun(_settings, _localDir, _ => true);
        Directory.CreateDirectory(Dir("Dropbox"));

        NotesFolder.ForgetFolderToGoBackTo(_settings);

        Assert.Null(NotesFolder.FolderToGoBackTo(_settings.Load(), _localDir));
    }

    [Fact]
    public void ForThisRun_AMissingFolder_TheUserSaysNo_ClosesAndKeepsTheChoice()
    {
        _settings.Save(new AppSettings { NotesFolder = Dir("Dropbox") });

        Assert.Null(NotesFolder.ForThisRun(_settings, _localDir, _ => false));
        Assert.Equal(Dir("Dropbox"), _settings.Load().NotesFolder);
        Assert.False(Directory.Exists(Dir("Dropbox")));
    }

    [Fact]
    public void Change_ToAnEmptyFolder_CopiesEverythingThereAndRemembersIt()
    {
        FillLocal();
        var target = Dir("Dropbox");

        var outcome = NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.Equal(NotesFolderOutcome.CopiedNotesThere, outcome);
        Assert.Equal("one", File.ReadAllText(Path.Combine(target, "notes", "2026-11-20-one.md")));
        Assert.Equal("two", File.ReadAllText(Path.Combine(target, "notes", "2026-11-20-two.md")));
        Assert.True(File.Exists(Path.Combine(target, "attachments", "2026-11-20-one", "pic.png")));
        Assert.True(File.Exists(Path.Combine(target, "history", "2026-11-20-one", "2026-11-19_10-00-00-000.md")));
        Assert.True(File.Exists(Path.Combine(target, "projects.json")));
        Assert.Equal(target, _settings.Load().NotesFolder);
    }

    [Fact]
    public void Change_NeverMovesOrDeletesTheOldNotes()
    {
        FillLocal();

        NotesFolder.Change(_settings, _localDir, _localDir, Dir("Dropbox"));

        Assert.Equal(2, Directory.GetFiles(Path.Combine(_localDir, "notes")).Length);
        Assert.True(File.Exists(Path.Combine(_localDir, "attachments", "2026-11-20-one", "pic.png")));
    }

    [Fact]
    public void Change_NeverMovesWindowSettings()
    {
        FillLocal();
        var target = Dir("Dropbox");

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.False(File.Exists(Path.Combine(target, "settings.json")));
        Assert.True(File.Exists(Path.Combine(_localDir, "settings.json")));
    }

    [Fact]
    public void Change_ToAFolderThatAlreadyHasNotes_AddsThisPcsNotesToThem()
    {
        // Another PC joining: the Dropbox folder already holds the notes, and any
        // notes this PC had are added, so none is left behind.
        FillLocal();
        var target = Dir("Dropbox");
        WriteFile(Path.Combine(target, "notes", "2026-11-18-shared.md"), "shared");

        var outcome = NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.Equal(NotesFolderOutcome.AddedToNotesThere, outcome);
        Assert.Equal(["2026-11-18-shared.md", "2026-11-20-one.md", "2026-11-20-two.md"],
            Directory.GetFiles(Path.Combine(target, "notes")).Select(Path.GetFileName).Order());
        Assert.Equal("shared", File.ReadAllText(Path.Combine(target, "notes", "2026-11-18-shared.md")));
        Assert.Equal(target, _settings.Load().NotesFolder);
    }

    [Fact]
    public void Change_WhereTheFolderHasAnEarlierVersionOfANote_KeepsTheNewerQuietly()
    {
        // Back to a folder left behind earlier: its copy is an earlier version of this one.
        var target = Dir("Dropbox");
        var older = new Note { Title = "Plan", Body = "older", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 2) };
        var newer = new Note { Title = "Plan", Body = "newer", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 9) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), newer.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "history", "2026-11-01-plan", "2026-11-05_10-00-00-000.md"), older.ToMarkdown());
        WriteFile(Path.Combine(target, "notes", "2026-11-01-plan.md"), older.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        using var there = new NoteService(Path.Combine(target, "notes"));
        var path = Path.Combine(target, "notes", "2026-11-01-plan.md");
        Assert.Equal("newer", Assert.Single(there.LoadAll()).Body);
        Assert.False(there.HasClash(path));
        Assert.Contains("older", there.GetVersions(path).Select(v => v.Read().Body));
    }

    [Fact]
    public void Change_WhereBothChangedTheSameNote_KeepsTheNewer_AndAsksAboutTheOther()
    {
        // This PC wrote while the shared folder was away, and another PC wrote there too.
        var target = Dir("Dropbox");
        var here = new Note { Title = "Plan", Body = "changed here", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 9) };
        var there = new Note { Title = "Plan", Body = "changed on the other PC", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 8) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), here.ToMarkdown());
        WriteFile(Path.Combine(target, "notes", "2026-11-01-plan.md"), there.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        using var service = new NoteService(Path.Combine(target, "notes"));
        var path = Path.Combine(target, "notes", "2026-11-01-plan.md");
        Assert.Equal("changed here", Assert.Single(service.LoadAll()).Body);
        Assert.True(service.HasClash(path));
        Assert.Equal("changed on the other PC", Assert.Single(service.GetClashes(path)).Read().Body);
    }

    [Fact]
    public void Change_ADifferentNoteWithTheSameNameAndPictureName_KeepsItsOwnPicture()
    {
        var target = Dir("Dropbox");
        var mine = new Note { Title = "Meeting", Body = "{{pic.png}} mine", Created = new DateTime(2026, 11, 20, 9, 0, 0) };
        var theirs = new Note { Title = "Meeting", Body = "{{pic.png}} theirs", Created = new DateTime(2026, 11, 20, 15, 0, 0) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-20-meeting.md"), mine.ToMarkdown());
        File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(_localDir, "attachments", "2026-11-20-meeting")).FullName, "pic.png"), [1]);
        WriteFile(Path.Combine(target, "notes", "2026-11-20-meeting.md"), theirs.ToMarkdown());
        File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(Path.Combine(target, "attachments", "2026-11-20-meeting")).FullName, "pic.png"), [2]);

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.Equal([2], File.ReadAllBytes(Path.Combine(target, "attachments", "2026-11-20-meeting", "pic.png")));
        Assert.Equal([1], File.ReadAllBytes(Path.Combine(target, "attachments", "2026-11-20-meeting-2", "pic.png")));
    }

    [Fact]
    public void Change_BackToAFolderWhereANoteWasDeleted_LeavesItDeleted()
    {
        // This PC used its own folder while the shared one was away; meanwhile another PC
        // deleted a note there. This PC's copy is a version the shared folder already had.
        var target = Dir("Dropbox");
        var plan = new Note { Title = "Plan", Body = "the plan", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 2) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), plan.ToMarkdown());
        WriteFile(Path.Combine(target, "history", "2026-11-01-plan", "2026-11-10_10-00-00-000.md"), plan.ToMarkdown());
        WriteFile(Path.Combine(target, "notes", "2026-11-18-shared.md"), "shared");

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.False(File.Exists(Path.Combine(target, "notes", "2026-11-01-plan.md")));
        using var there = new NoteService(Path.Combine(target, "notes"));
        Assert.Contains(there.GetDeleted(), v => v.Read().Body == "the plan");
    }

    [Fact]
    public void Change_BackToAFolderWhereAClashWasSettled_DoesNotAskAgain()
    {
        // This PC left while a note had two versions; the choice was made in the shared
        // folder afterwards (the unkept one went to its history).
        var target = Dir("Dropbox");
        var kept = new Note { Title = "Plan", Body = "kept", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 9) };
        var other = new Note { Title = "Plan", Body = "not kept", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 8) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), kept.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "conflicts", "2026-11-01-plan", "2026-11-08_10-00-00-000.md"), other.ToMarkdown());
        WriteFile(Path.Combine(target, "notes", "2026-11-01-plan.md"), kept.ToMarkdown());
        WriteFile(Path.Combine(target, "history", "2026-11-01-plan", "2026-11-10_10-00-00-000.md"), other.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        using var there = new NoteService(Path.Combine(target, "notes"));
        there.LoadAll();
        Assert.False(there.HasClash(Path.Combine(target, "notes", "2026-11-01-plan.md")));
    }

    [Fact]
    public void Change_BringsAlongAVersionStillWaitingForADecision()
    {
        var target = Dir("Dropbox");
        var mine = new Note { Title = "Plan", Body = "mine", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 9) };
        var other = new Note { Title = "Plan", Body = "other", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 8) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), mine.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "conflicts", "2026-11-01-plan", "2026-11-08_10-00-00-000.md"), other.ToMarkdown());
        // A save that finds a clash keeps the other version in history as well.
        WriteFile(Path.Combine(_localDir, "history", "2026-11-01-plan", "2026-11-09_10-00-00-000.md"), other.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        using var there = new NoteService(Path.Combine(target, "notes"));
        there.LoadAll();
        var path = Path.Combine(target, "notes", "2026-11-01-plan.md");
        Assert.True(there.HasClash(path));
        Assert.Equal("other", Assert.Single(there.GetClashes(path)).Read().Body);
    }

    [Fact]
    public void Change_ANoteChangedThereSeparately_IsAskedAbout_EvenIfThisPcsHistoryHasItsText()
    {
        // "Keep the other" on this PC left the note's own text in its history too (a save
        // that finds a clash keeps the other version there). That is no reason to call it an
        // earlier version of a note changed separately, later, in the shared folder.
        var target = Dir("Dropbox");
        var created = new DateTime(2026, 11, 1, 9, 0, 0);
        var mine = new Note { Title = "Plan", Body = "mine", Created = created, Updated = new DateTime(2026, 11, 2) };
        var there = new Note { Title = "Plan", Body = "changed there", Created = created, Updated = new DateTime(2026, 11, 9) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), mine.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "history", "2026-11-01-plan", "2026-11-02_10-00-00-000.md"), mine.ToMarkdown());
        WriteFile(Path.Combine(target, "notes", "2026-11-01-plan.md"), there.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        using var service = new NoteService(Path.Combine(target, "notes"));
        service.LoadAll();
        var path = Path.Combine(target, "notes", "2026-11-01-plan.md");
        Assert.Equal("changed there", Note.ParseFromMarkdown(File.ReadAllText(path), path).Body);
        Assert.Equal("mine", Assert.Single(service.GetClashes(path)).Read().Body);
    }

    [Fact]
    public void Change_BackToAFolderWhereANoteTookAFreeName_ChangesThatNote_NotANewOne()
    {
        // The first move put this PC's "Meeting" at -2 (the folder had a different one).
        // Edited here since (the app kept the old text in history); moving back again
        // must change -2, not make a -3.
        var target = Dir("Dropbox");
        var mine = new Note { Title = "Meeting", Body = "mine", Created = new DateTime(2026, 11, 20, 9, 0, 0), Updated = new DateTime(2026, 11, 20, 9, 0, 0) };
        var theirs = new Note { Title = "Meeting", Body = "theirs", Created = new DateTime(2026, 11, 20, 15, 0, 0) };
        var localNote = Path.Combine(_localDir, "notes", "2026-11-20-meeting.md");
        WriteFile(localNote, mine.ToMarkdown());
        WriteFile(Path.Combine(target, "notes", "2026-11-20-meeting.md"), theirs.ToMarkdown());
        NotesFolder.Change(_settings, _localDir, _localDir, target);
        WriteFile(Path.Combine(_localDir, "history", "2026-11-20-meeting", "2026-11-21_09-00-00-000.md"), mine.ToMarkdown());
        mine.Body = "mine, edited while away";
        mine.Updated = new DateTime(2026, 11, 21, 9, 0, 0);
        File.WriteAllText(localNote, mine.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.Equal(["2026-11-20-meeting-2.md", "2026-11-20-meeting.md"],
            Directory.GetFiles(Path.Combine(target, "notes"), "*.md").Select(Path.GetFileName).Order());
        var moved = Path.Combine(target, "notes", "2026-11-20-meeting-2.md");
        Assert.Equal("mine, edited while away", Note.ParseFromMarkdown(File.ReadAllText(moved), moved).Body);
        using var service = new NoteService(Path.Combine(target, "notes"));
        service.LoadAll();
        Assert.False(service.HasClash(moved));
    }

    [Fact]
    public void Change_WhereTheFoldersProjectListCannotBeRead_ChangesNothing()
    {
        // A hand edit with a trailing comma, or a list still syncing: it is not written over.
        var target = Dir("Dropbox");
        WriteFile(Path.Combine(target, "notes", "2026-11-18-shared.md"), "shared");
        const string theirs = "[{\"name\":\"A1i\"},{\"name\":\"Work\"},]";
        WriteFile(Path.Combine(target, "projects.json"), theirs);
        new ProjectService(_localDir).Save([new ProjectInfo { Name = "VasoGuard" }]);

        Assert.ThrowsAny<Exception>(() => NotesFolder.Change(_settings, _localDir, _localDir, target));

        Assert.Equal(theirs, File.ReadAllText(Path.Combine(target, "projects.json")));
        Assert.Equal("", _settings.Load().NotesFolder);
    }

    [Fact]
    public void Change_ANoteThatDiffersOnlyInALineTheAppHasNoFieldFor_KeepsThatLine()
    {
        var target = Dir("Dropbox");
        var plan = new Note { Title = "Plan", Body = "same", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 2) };
        WriteFile(Path.Combine(target, "notes", "2026-11-01-plan.md"), plan.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), plan.ToMarkdown().Replace("title: Plan", "title: Plan\ndue: 2026-12-01"));

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.Contains(AllTextUnder(target), t => t.Contains("due: 2026-12-01"));
    }

    [Fact]
    public void Change_ADifferentNoteNamedLikeOneDeletedThere_KeepsThemApart()
    {
        var target = Dir("Dropbox");
        var theirs = new Note { Title = "Meeting", Body = "theirs, deleted there", Created = new DateTime(2026, 11, 20, 15, 0, 0) };
        var mine = new Note { Title = "Meeting", Body = "mine", Created = new DateTime(2026, 11, 20, 9, 0, 0) };
        WriteFile(Path.Combine(target, "notes", "2026-11-18-other.md"), "other");
        WriteFile(Path.Combine(target, "history", "2026-11-20-meeting", "2026-11-20_16-00-00-000.md"), theirs.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-20-meeting.md"), mine.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        using var there = new NoteService(Path.Combine(target, "notes"));
        var mineThere = Assert.Single(there.LoadAll(), n => n.Body == "mine");
        Assert.Contains(there.GetDeleted(), v => v.Read().Body == "theirs, deleted there");
        Assert.DoesNotContain(there.GetVersions(mineThere.FilePath), v => v.Read().Body == "theirs, deleted there");
    }

    [Fact]
    public void Change_ANoteDeletedHere_NamedLikeADifferentNoteThere_StaysInDeletedNotes()
    {
        var target = Dir("Dropbox");
        var mineDeleted = new Note { Title = "Meeting", Body = "mine, deleted here", Created = new DateTime(2026, 11, 20, 9, 0, 0) };
        var theirs = new Note { Title = "Meeting", Body = "theirs", Created = new DateTime(2026, 11, 20, 15, 0, 0) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-18-other.md"), "other");
        WriteFile(Path.Combine(_localDir, "history", "2026-11-20-meeting", "2026-11-20_10-00-00-000.md"), mineDeleted.ToMarkdown());
        WriteFile(Path.Combine(target, "notes", "2026-11-20-meeting.md"), theirs.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        using var there = new NoteService(Path.Combine(target, "notes"));
        var path = Path.Combine(target, "notes", "2026-11-20-meeting.md");
        Assert.DoesNotContain(there.GetVersions(path), v => v.Read().Body == "mine, deleted here");
        Assert.Contains(there.GetDeleted(), v => v.Read().Body == "mine, deleted here");
    }

    [Theory]
    [InlineData(9, "MY PIXELS", "THEIR PIXELS")]  // this PC's note is newer: its picture shows
    [InlineData(1, "THEIR PIXELS", "MY PIXELS")]  // the folder's is newer: its picture shows
    public void Change_TheSameNoteWithADifferentPictureOfTheSameName_KeepsBothPictures(int myDay, string shown, string kept)
    {
        var target = Dir("Dropbox");
        var created = new DateTime(2026, 11, 1);
        var theirs = new Note { Title = "Plan", Body = "{{shot.png}} theirs", Created = created, Updated = new DateTime(2026, 11, 5) };
        var mine = new Note { Title = "Plan", Body = "{{shot.png}} mine", Created = created, Updated = new DateTime(2026, 11, myDay) };
        WriteFile(Path.Combine(target, "notes", "2026-11-01-plan.md"), theirs.ToMarkdown());
        WriteFile(Path.Combine(target, "attachments", "2026-11-01-plan", "shot.png"), "THEIR PIXELS");
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), mine.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "attachments", "2026-11-01-plan", "shot.png"), "MY PIXELS");

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.Equal(shown, File.ReadAllText(Path.Combine(target, "attachments", "2026-11-01-plan", "shot.png")));
        Assert.Contains(AllTextUnder(Path.Combine(target, "history", "2026-11-01-plan")), t => t == kept);
    }

    [Fact]
    public void Change_WhereTheFolderHasAnEarlierVersionOfANote_KeepsTheNewerQuietly_ThroughTheWinnersOwnHistory()
    {
        // The folder's text is in the folder's own history only: still just an earlier version
        // of this PC's newer note? No: only this PC's history can say that. Here it does.
        var target = Dir("Dropbox");
        var older = new Note { Title = "Plan", Body = "older", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 2) };
        var newer = new Note { Title = "Plan", Body = "newer", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 9) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), newer.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "history", "2026-11-01-plan", "2026-11-05_10-00-00-000.md"), older.ToMarkdown());
        WriteFile(Path.Combine(target, "notes", "2026-11-01-plan.md"), older.ToMarkdown());
        WriteFile(Path.Combine(target, "history", "2026-11-01-plan", "2026-11-01_10-00-00-000.md"), older.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        using var service = new NoteService(Path.Combine(target, "notes"));
        service.LoadAll();
        Assert.False(service.HasClash(Path.Combine(target, "notes", "2026-11-01-plan.md")));
    }

    [Fact]
    public void FolderToGoBackTo_IsNotOfferedWhileTheFolderIsStillEmpty()
    {
        // Dropbox has just made the folder and is still bringing the notes down.
        _settings.Save(new AppSettings { NotesFolder = Dir("Dropbox") });
        NotesFolder.ForThisRun(_settings, _localDir, _ => true);
        Directory.CreateDirectory(Dir("Dropbox"));

        Assert.Null(NotesFolder.FolderToGoBackTo(_settings.Load(), _localDir));
    }

    [Fact]
    public void ProblemWith_AFolderThatCannotBeRead_SaysSo()
    {
        var locked = Dir("Locked");
        WriteFile(Path.Combine(locked, "x.txt"), "x");
        var user = Environment.UserName;
        void Icacls(string args)
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("icacls", args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            p.WaitForExit();
        }
        try
        {
            Icacls($"\"{locked}\" /deny \"{user}\":(RD)");
            Assert.NotNull(NotesFolder.ProblemWith(_localDir, _localDir, locked));
        }
        finally
        {
            Icacls($"\"{locked}\" /remove:d \"{user}\"");
        }
    }

    [Fact]
    public void Change_RetriedAfterStoppingPartway_StillAsksAboutANoteChangedSeparately()
    {
        var target = Dir("Dropbox");
        var created = new DateTime(2026, 11, 1, 9, 0, 0);
        var mine = new Note { Title = "Plan", Body = "{{shot.png}} mine", Created = created, Updated = new DateTime(2026, 11, 2) };
        var there = new Note { Title = "Plan", Body = "changed there", Created = created, Updated = new DateTime(2026, 11, 9) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), mine.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "history", "2026-11-01-plan", "2026-11-02_10-00-00-000.md"), mine.ToMarkdown());
        var pic = Path.Combine(_localDir, "attachments", "2026-11-01-plan", "shot.png");
        WriteFile(pic, "PIXELS");
        WriteFile(Path.Combine(target, "notes", "2026-11-01-plan.md"), there.ToMarkdown());

        MoveStoppedPartwayThenRetried(target, pic);

        using var service = new NoteService(Path.Combine(target, "notes"));
        service.LoadAll();
        Assert.True(service.HasClash(Path.Combine(target, "notes", "2026-11-01-plan.md")));
    }

    [Fact]
    public void Change_RetriedAfterStoppingPartway_StillBringsANoteThatIsItsOwnEarlierVersion()
    {
        var target = Dir("Dropbox");
        Directory.CreateDirectory(target);
        var restored = new Note { Title = "Plan", Body = "{{shot.png}} restored", Created = new DateTime(2026, 11, 1, 9, 0, 0), Updated = new DateTime(2026, 11, 2) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), restored.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "history", "2026-11-01-plan", "2026-11-02_10-00-00-000.md"), restored.ToMarkdown());
        var pic = Path.Combine(_localDir, "attachments", "2026-11-01-plan", "shot.png");
        WriteFile(pic, "PIXELS");

        MoveStoppedPartwayThenRetried(target, pic);

        using var service = new NoteService(Path.Combine(target, "notes"));
        Assert.Single(service.LoadAll());
    }

    [Theory]
    [InlineData(true)]   // this PC's note is live
    [InlineData(false)]  // this PC's note was deleted here too
    public void Change_ANoteNamedLikeOneDeletedThereThatOnlyHasAWaitingVersion_KeepsThemApart(bool mineIsLive)
    {
        var target = Dir("Dropbox");
        var theirs = new Note { Title = "Meeting", Body = "theirs, deleted there", Created = new DateTime(2026, 11, 20, 15, 0, 0) };
        var mine = new Note { Title = "Meeting", Body = "mine", Created = new DateTime(2026, 11, 20, 9, 0, 0) };
        WriteFile(Path.Combine(target, "notes", "2026-11-18-other.md"), "other");
        WriteFile(Path.Combine(target, "conflicts", "2026-11-20-meeting", "2026-11-20_16-00-00-000.md"), theirs.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-18-x.md"), "x");
        WriteFile(mineIsLive
            ? Path.Combine(_localDir, "notes", "2026-11-20-meeting.md")
            : Path.Combine(_localDir, "history", "2026-11-20-meeting", "2026-11-20_10-00-00-000.md"), mine.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        using var there = new NoteService(Path.Combine(target, "notes"));
        var live = there.LoadAll();
        Assert.Contains(there.GetDeleted(), v => v.Read().Body == "theirs, deleted there");
        if (mineIsLive)
            Assert.False(there.HasClash(Assert.Single(live, n => n.Body == "mine").FilePath));
        else
            Assert.Contains(there.GetDeleted(), v => v.Read().Body == "mine");
    }

    [Fact]
    public void Change_BackToAFolderWhereANoteThatTookAFreeNameWasDeleted_LeavesItDeleted()
    {
        var target = Dir("Dropbox");
        var mine = new Note { Title = "Meeting", Body = "mine", Created = new DateTime(2026, 11, 20, 9, 0, 0), Updated = new DateTime(2026, 11, 20, 9, 0, 0) };
        var theirs = new Note { Title = "Meeting", Body = "theirs", Created = new DateTime(2026, 11, 20, 15, 0, 0) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-20-meeting.md"), mine.ToMarkdown());
        WriteFile(Path.Combine(target, "notes", "2026-11-20-meeting.md"), theirs.ToMarkdown());
        NotesFolder.Change(_settings, _localDir, _localDir, target);
        using (var there = new NoteService(Path.Combine(target, "notes")))
            there.Delete(there.LoadAll().Single(n => n.Body == "mine")); // another PC deletes it

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        using var after = new NoteService(Path.Combine(target, "notes"));
        Assert.DoesNotContain(after.LoadAll(), n => n.Body == "mine");
        Assert.Single(after.GetDeleted(), v => v.Read().Body == "mine");
    }

    [Fact]
    public void Change_WhereTheProjectListHasAMistake_SaysWhichFile()
    {
        var target = Dir("Dropbox");
        WriteFile(Path.Combine(target, "notes", "2026-11-18-shared.md"), "shared");
        WriteFile(Path.Combine(target, "projects.json"), "[{\"name\":\"A1i\"},]");

        var error = Assert.ThrowsAny<IOException>(() => NotesFolder.Change(_settings, _localDir, _localDir, target));

        Assert.Contains(Path.Combine(target, "projects.json"), error.Message);
    }

    [Fact]
    public void ProblemWith_AFolderWhoseNotesCannotBeRead_SaysSo()
    {
        var shared = Dir("JL Notes");
        WriteFile(Path.Combine(shared, "notes", "2026-11-20-one.md"), "one");
        string? problem = null;

        WhileUnreadable(Path.Combine(shared, "notes"), () => problem = NotesFolder.ProblemWith(_localDir, _localDir, shared));

        Assert.NotNull(problem);
    }

    [Fact]
    public void FolderToGoBackTo_AFolderWhoseNotesCannotBeRead_IsNotOfferedYet()
    {
        _settings.Save(new AppSettings { NotesFolder = Dir("Dropbox") });
        NotesFolder.ForThisRun(_settings, _localDir, _ => true);
        WriteFile(Path.Combine(Dir("Dropbox"), "notes", "2026-11-20-one.md"), "one");
        string? offered = "not asked";

        WhileUnreadable(Path.Combine(Dir("Dropbox"), "notes"), () => offered = NotesFolder.FolderToGoBackTo(_settings.Load(), _localDir));

        Assert.Null(offered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]  // stopped partway (the sync tool held a picture), then picked again
    public void Change_TheWinningNote_ShowsItsOwnPictures(bool stoppedPartway)
    {
        var target = Dir("Dropbox");
        var created = new DateTime(2026, 11, 1, 9, 0, 0);
        var mine = new Note { Title = "Plan", Body = "{{a.png}} {{b.png}} mine", Created = created, Updated = new DateTime(2026, 11, 9) };
        var theirs = new Note { Title = "Plan", Body = "{{a.png}} {{b.png}} theirs", Created = created, Updated = new DateTime(2026, 11, 2) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), mine.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "attachments", "2026-11-01-plan", "a.png"), "MINE-A");
        var held = Path.Combine(_localDir, "attachments", "2026-11-01-plan", "b.png");
        WriteFile(held, "MINE-B");
        WriteFile(Path.Combine(target, "notes", "2026-11-01-plan.md"), theirs.ToMarkdown());
        WriteFile(Path.Combine(target, "attachments", "2026-11-01-plan", "a.png"), "THEIRS-A");
        WriteFile(Path.Combine(target, "attachments", "2026-11-01-plan", "b.png"), "THRS-B"); // same length as MINE-B, so the bytes are compared

        if (stoppedPartway)
            MoveStoppedPartwayThenRetried(target, held);
        else
            NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.Equal("MINE-A", File.ReadAllText(Path.Combine(target, "attachments", "2026-11-01-plan", "a.png")));
        Assert.Equal("MINE-B", File.ReadAllText(Path.Combine(target, "attachments", "2026-11-01-plan", "b.png")));
    }

    [Fact]
    public void Change_StoppedWhileBringingBackTheNotesPictures_StillAsksAboutTheOtherVersion()
    {
        var target = Dir("Dropbox");
        var created = new DateTime(2026, 11, 1, 9, 0, 0);
        var mine = new Note { Title = "Plan", Body = "{{old.png}} mine", Created = created, Updated = new DateTime(2026, 11, 9) };
        var theirs = new Note { Title = "Plan", Body = "theirs, changed separately", Created = created, Updated = new DateTime(2026, 11, 2) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), mine.ToMarkdown());
        WriteFile(Path.Combine(target, "notes", "2026-11-01-plan.md"), theirs.ToMarkdown());
        var keptThere = Path.Combine(target, "history", "2026-11-01-plan", "attachments", "old.png");
        WriteFile(keptThere, "OLD");

        MoveStoppedPartwayThenRetried(target, keptThere);

        using var service = new NoteService(Path.Combine(target, "notes"));
        Assert.True(service.HasClash(Assert.Single(service.LoadAll()).FilePath));
    }

    [Fact]
    public void Change_ANoteDeletedThereThatOnlyHasAWaitingVersion_StaysDeleted()
    {
        var target = Dir("Dropbox");
        var created = new DateTime(2026, 11, 20, 15, 0, 0);
        var meeting = new Note { Title = "Meeting", Body = "same text", Created = created, Updated = created };
        WriteFile(Path.Combine(target, "notes", "2026-11-18-other.md"), "other");
        WriteFile(Path.Combine(target, "conflicts", "2026-11-20-meeting", "2026-11-20_16-00-00-000.md"), meeting.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-20-meeting.md"), meeting.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        using var service = new NoteService(Path.Combine(target, "notes"));
        Assert.DoesNotContain(service.LoadAll(), n => n.Body == "same text");
        Assert.Contains(service.GetDeleted(), v => v.Read().Body == "same text");
    }

    [Fact]
    public void Change_BackToAFolderWhereANoteWithADotInItsNameWasDeleted_LeavesItDeleted()
    {
        // Made by hand or another tool (the app's own names have no dot).
        var target = Dir("Dropbox");
        var mine = new Note { Title = "Meeting", Body = "mine", Created = new DateTime(2026, 11, 20, 9, 0, 0), Updated = new DateTime(2026, 11, 20, 9, 0, 0) };
        var theirs = new Note { Title = "Meeting", Body = "theirs", Created = new DateTime(2026, 11, 20, 15, 0, 0) };
        WriteFile(Path.Combine(_localDir, "notes", "notes-v1.2.md"), mine.ToMarkdown());
        WriteFile(Path.Combine(target, "notes", "notes-v1.2.md"), theirs.ToMarkdown());
        NotesFolder.Change(_settings, _localDir, _localDir, target);
        using (var there = new NoteService(Path.Combine(target, "notes")))
            there.Delete(there.LoadAll().Single(n => n.Body == "mine"));

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        using var after = new NoteService(Path.Combine(target, "notes"));
        Assert.DoesNotContain(after.LoadAll(), n => n.Body == "mine");
    }

    [Fact]
    public void Change_WhereANoteDeletedThereWasChangedHere_BringsItBack()
    {
        // Deleted on one PC, but written to on this one after: that writing is kept.
        var target = Dir("Dropbox");
        var old = new Note { Title = "Plan", Body = "the plan", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 2) };
        var changed = new Note { Title = "Plan", Body = "the plan, changed here", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 12) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), changed.ToMarkdown());
        WriteFile(Path.Combine(target, "history", "2026-11-01-plan", "2026-11-10_10-00-00-000.md"), old.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.Equal(changed.ToMarkdown(), File.ReadAllText(Path.Combine(target, "notes", "2026-11-01-plan.md")));
    }

    [Fact]
    public void Change_ANoteThatIsItsOwnEarlierVersion_StillArrives()
    {
        // Put back from Earlier Versions, so its text is in its own history too.
        var target = Dir("Dropbox");
        var plan = new Note { Title = "Plan", Body = "the plan", Created = new DateTime(2026, 11, 1), Updated = new DateTime(2026, 11, 2) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-01-plan.md"), plan.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "history", "2026-11-01-plan", "2026-11-10_10-00-00-000.md"), plan.ToMarkdown());

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.True(File.Exists(Path.Combine(target, "notes", "2026-11-01-plan.md")));
    }

    [Fact]
    public void Change_ADifferentNoteWithTheSameName_BringsItsOwnHistoryAndPictures()
    {
        var target = Dir("Dropbox");
        var mine = new Note { Title = "Meeting", Body = "{{mine.png}} mine", Created = new DateTime(2026, 11, 20, 9, 0, 0) };
        var theirs = new Note { Title = "Meeting", Body = "{{theirs.png}} theirs", Created = new DateTime(2026, 11, 20, 15, 0, 0) };
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-20-meeting.md"), mine.ToMarkdown());
        WriteFile(Path.Combine(_localDir, "attachments", "2026-11-20-meeting", "mine.png"), "m");
        WriteFile(Path.Combine(_localDir, "history", "2026-11-20-meeting", "2026-11-20_09-30-00-000.md"), "my earlier text");
        WriteFile(Path.Combine(target, "notes", "2026-11-20-meeting.md"), theirs.ToMarkdown());
        WriteFile(Path.Combine(target, "attachments", "2026-11-20-meeting", "theirs.png"), "t");

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.Equal(["theirs.png"], Directory.GetFiles(Path.Combine(target, "attachments", "2026-11-20-meeting")).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(target, "attachments", "2026-11-20-meeting-2", "mine.png")));
        Assert.False(Directory.Exists(Path.Combine(target, "history", "2026-11-20-meeting")));
        Assert.Equal("my earlier text", File.ReadAllText(Path.Combine(target, "history", "2026-11-20-meeting-2", "2026-11-20_09-30-00-000.md")));
    }

    [Fact]
    public void Change_LeavesTheOldProjectListAsItWas()
    {
        // The old folder is a backup: even its sync tool's leftovers stay as they are.
        new ProjectService(_localDir).Save([new ProjectInfo { Name = "A1i" }]);
        var copy = Path.Combine(_localDir, "projects (JL's conflicted copy 2026-11-20).json");
        WriteFile(copy, "[{\"name\":\"VasoGuard\"}]");
        var before = File.ReadAllText(Path.Combine(_localDir, "projects.json"));
        var target = Dir("Dropbox");

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.Equal(before, File.ReadAllText(Path.Combine(_localDir, "projects.json")));
        Assert.True(File.Exists(copy));
        Assert.Equal(["A1i", "VasoGuard"], new ProjectService(target).Load().Select(p => p.Name));
    }

    [Fact]
    public void Change_AfterACopyThatStoppedPartway_CopiesWhatIsStillMissing()
    {
        FillLocal();
        var target = Dir("Dropbox");
        WriteFile(Path.Combine(target, "notes", "2026-11-20-one.md"), "one"); // the first try got this far

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.True(File.Exists(Path.Combine(target, "notes", "2026-11-20-two.md")));
        Assert.True(File.Exists(Path.Combine(target, "attachments", "2026-11-20-one", "pic.png")));
        Assert.True(File.Exists(Path.Combine(target, "history", "2026-11-20-one", "2026-11-19_10-00-00-000.md")));
    }

    [Fact]
    public void Change_JoinsTheProjectLists()
    {
        new ProjectService(_localDir).Save([new ProjectInfo { Name = "A1i" }]);
        var target = Dir("Dropbox");
        Directory.CreateDirectory(target);
        new ProjectService(target).Save([new ProjectInfo { Name = "VasoGuard" }]);
        WriteFile(Path.Combine(target, "notes", "2026-11-18-shared.md"), "shared");

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.Equal(["VasoGuard", "A1i"], new ProjectService(target).Load().Select(p => p.Name));
    }

    [Fact]
    public void MissingFolderProblem_ThisPcsOwnFolder_IsNone()
    {
        Assert.Null(NotesFolder.MissingFolderProblem(Dir("not-made-yet"), Dir("not-made-yet")));
    }

    [Fact]
    public void MissingFolderProblem_AChosenFolderThatIsGone_SaysSo()
    {
        Assert.NotNull(NotesFolder.MissingFolderProblem(Dir("Dropbox"), _localDir));
    }

    [Fact]
    public void MissingFolderProblem_AChosenFolderThatIsThere_IsNone()
    {
        Directory.CreateDirectory(Dir("Dropbox"));

        Assert.Null(NotesFolder.MissingFolderProblem(Dir("Dropbox"), _localDir));
    }

    [Fact]
    public void Change_KeepsWhatIsAlreadyThere()
    {
        FillLocal();
        var target = Dir("Dropbox");
        WriteFile(Path.Combine(target, "projects.json"), "[{\"name\":\"Theirs\"}]");
        WriteFile(Path.Combine(target, "history", "2026-11-20-one", "2026-11-19_10-00-00-000.md"), "theirs");

        NotesFolder.Change(_settings, _localDir, _localDir, target);

        Assert.Contains("Theirs", new ProjectService(target).Load().Select(p => p.Name));
        Assert.Equal("theirs", File.ReadAllText(Path.Combine(target, "history", "2026-11-20-one", "2026-11-19_10-00-00-000.md")));
    }

    [Fact]
    public void Change_BackToThisPcsFolder_ClearsTheChoice()
    {
        var target = Dir("Dropbox");
        _settings.Save(new AppSettings { NotesFolder = target });
        WriteFile(Path.Combine(_localDir, "notes", "2026-11-20-one.md"), "one");

        var outcome = NotesFolder.Change(_settings, _localDir, target, _localDir);

        Assert.Equal(NotesFolderOutcome.AddedToNotesThere, outcome);
        Assert.Equal("", _settings.Load().NotesFolder);
    }

    [Fact]
    public void Change_KeepsEveryOtherSetting()
    {
        _settings.Save(new AppSettings { Theme = "light", SplitListWidth = 321 });

        NotesFolder.Change(_settings, _localDir, _localDir, Dir("Dropbox"));

        var settings = _settings.Load();
        Assert.Equal("light", settings.Theme);
        Assert.Equal(321, settings.SplitListWidth);
    }

    [Fact]
    public void Change_ToTheSameFolder_DoesNothing()
    {
        FillLocal();

        Assert.Equal(NotesFolderOutcome.Unchanged, NotesFolder.Change(_settings, _localDir, _localDir, _localDir));
        Assert.Equal("", _settings.Load().NotesFolder);
    }

    [Fact]
    public void Change_ToAFolderInsideTheNotesFolder_IsRefused()
    {
        FillLocal();

        Assert.Throws<ArgumentException>(() =>
            NotesFolder.Change(_settings, _localDir, _localDir, Path.Combine(_localDir, "notes", "deeper")));
        Assert.Equal("", _settings.Load().NotesFolder);
    }
}
