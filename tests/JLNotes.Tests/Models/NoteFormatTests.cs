using System.IO;
using System.Linq;
using JLNotes.Models;
using JLNotes.Services;
using JLNotes.ViewModels;

namespace JLNotes.Tests.Models;

// A note must never disappear because of what is in its header. These cases all
// come from real notes: hand-written files with no header, titles with a colon or
// a #, and quoted titles that used to break on their first save.
public class NoteFormatTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _notesDir;
    private readonly NoteService _service;

    public NoteFormatTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"jlnotes-format-{Guid.NewGuid()}");
        _notesDir = Path.Combine(_testDir, "notes");
        Directory.CreateDirectory(_notesDir);
        _service = new NoteService(_notesDir);
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    public static TheoryData<string> HostileValues => new()
    {
        "Meeting: Q3 plan", "Bug #42 in login", "- dash first", "[bracket] title", "\"quoted\" title",
        "it's", "50% done", "IN-1722: fix", "a: b: c", "'single'", "{brace}", "true", "null", "123", "~",
        "2026-01-01", "*star", "&amp", "!bang", "|pipe", ">gt", "@at", "`tick", "trailing:", " leading space",
        "trailing space ", "emoji \U0001F600 and dash \u2014", @"C:\Users\someone\x", @"back\slash: y",
        "Sunday, Nov 1, 2026 at 5:05 PM", "comma, inside",
    };

    [Theory]
    [MemberData(nameof(HostileValues))]
    public void EveryTextField_SurvivesAWriteAndRead(string value)
    {
        var note = new Note
        {
            Title = value, Project = value, Repo = value, Branch = value,
            Tags = [value, "plain"],
            Body = "body", Created = new DateTime(2026, 1, 2, 3, 4, 5), Updated = new DateTime(2026, 2, 3, 4, 5, 6),
        };

        var read = Note.ParseFromMarkdown(note.ToMarkdown(), "x.md");

        Assert.Equal(value, read.Title);
        Assert.Equal(value, read.Project);
        Assert.Equal(value, read.Repo);
        Assert.Equal(value, read.Branch);
        Assert.Equal(new[] { value, "plain" }, read.Tags);
    }

    // Guard: a new Note property must be added to the header list or marked
    // file-only here. This is what would have caught SortOrder never being saved.
    [Fact]
    public void EveryNoteProperty_IsSavedInTheFile()
    {
        string[] notInHeader = [nameof(Note.Body), nameof(Note.FilePath)];
        var note = new Note();
        var props = typeof(Note).GetProperties().Where(p => !notInHeader.Contains(p.Name)).ToList();
        foreach (var p in props)
        {
            object value = p.PropertyType switch
            {
                var t when t == typeof(string) => "value-" + p.Name,
                var t when t == typeof(int) => 7,
                var t when t == typeof(DateTime) => new DateTime(2026, 3, 4, 5, 6, 7),
                var t when t == typeof(List<string>) => new List<string> { "item" },
                var t when t == typeof(NotePriority) => NotePriority.High,
                var t when t == typeof(NoteStatus) => NoteStatus.Done,
                var t => throw new InvalidOperationException($"Add {t} to this test for Note.{p.Name}"),
            };
            p.SetValue(note, value);
        }

        var read = Note.ParseFromMarkdown(note.ToMarkdown(), "x.md");

        foreach (var p in props)
        {
            if (p.GetValue(note) is List<string> list)
                Assert.Equal(list, (List<string>)p.GetValue(read)!);
            else
                Assert.Equal(p.GetValue(note), p.GetValue(read));
        }
    }

    [Fact]
    public void QuotedTitleWithColon_StaysVisibleAfterASave()
    {
        var path = WriteNote("2026-03-25-a.md",
            "---\ntitle: \"Competitor Profile: Example\"\nproject: A\npriority: low\nstatus: open\ntags: [x]\n" +
            "created: 2026-03-25T10:00:00\nupdated: 2026-03-25T10:00:00\n---\n\nThe body.\n");

        var note = _service.LoadAll().Single();
        note.Status = NoteStatus.Done;
        _service.Save(note);

        var reloaded = _service.LoadAll().Single();
        Assert.Equal("Competitor Profile: Example", reloaded.Title);
        Assert.Equal(NoteStatus.Done, reloaded.Status);
        Assert.Equal("The body.", reloaded.Body);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void UnquotedColonInTitle_StillLoadsEveryField()
    {
        WriteNote("2026-10-13-a.md",
            "---\ntitle: A1i tenant fixes: what to do first\nproject: A1i\npriority: high\nstatus: done\n" +
            "tags: [plan, next-steps]\ncreated: 2026-10-13T10:40:00\nupdated: 2026-10-14T11:41:00\nrepo: A1i\nbranch:\n---\n\nThe body.\n");

        var note = _service.LoadAll().Single();

        Assert.Equal("A1i tenant fixes: what to do first", note.Title);
        Assert.Equal("A1i", note.Project);
        Assert.Equal(NotePriority.High, note.Priority);
        Assert.Equal(NoteStatus.Done, note.Status);
        Assert.Equal(new[] { "plan", "next-steps" }, note.Tags);
        Assert.Equal(new DateTime(2026, 10, 13, 10, 40, 0), note.Created);
        Assert.Equal(new DateTime(2026, 10, 14, 11, 41, 0), note.Updated);
        Assert.Equal("A1i", note.Repo);
        Assert.Equal("", note.Branch);
        Assert.Equal("The body.", note.Body);
    }

    [Fact]
    public void HashInUnquotedTitle_IsNotCutOff()
    {
        var note = Note.ParseFromMarkdown(
            "---\ntitle: PR #14 / IN-1672 audit\ncreated: 2026-05-07T00:00:00\nupdated: 2026-05-07T00:00:00\n---\n\nb\n", "x.md");

        Assert.Equal("PR #14 / IN-1672 audit", note.Title);
    }

    [Fact]
    public void FileWithNoHeader_LoadsAsOpenMediumWithItsWholeBody()
    {
        const string text = "# A1i Skills Playbook\n\nIntro line.\n\n---\n\n## The principle\n\nMore text.\n";
        WriteNote("2026-05-08-a1i-skills-playbook.md", text);

        var note = _service.LoadAll().Single();

        Assert.Equal("A1i Skills Playbook", note.Title);
        Assert.Equal(NoteStatus.Open, note.Status);
        Assert.Equal(NotePriority.Medium, note.Priority);
        Assert.Equal(new DateTime(2026, 5, 8), note.Created);
        Assert.Equal(text.Trim(), note.Body);
    }

    [Fact]
    public void FileWithNoHeaderOrHeading_TakesItsTitleFromTheFileName()
    {
        WriteNote("2026-05-08-some-loose-note.md", "Just text.\n");

        Assert.Equal("some-loose-note", _service.LoadAll().Single().Title);
    }

    [Fact]
    public void FileWithNoHeader_GainsOneOnSaveAndKeepsItsBody()
    {
        WriteNote("2026-05-08-a.md", "# Heading\n\nText: with a colon.\n\n---\n\nAfter the rule.\n");

        var note = _service.LoadAll().Single();
        note.Priority = NotePriority.High;
        _service.Save(note);

        var reloaded = _service.LoadAll().Single();
        Assert.Equal("Heading", reloaded.Title);
        Assert.Equal(NotePriority.High, reloaded.Priority);
        Assert.Equal("# Heading\n\nText: with a colon.\n\n---\n\nAfter the rule.", reloaded.Body);
    }

    [Fact]
    public void HeaderNeverClosed_KeepsFieldsAndBody_EvenWithARuleLater()
    {
        var note = Note.ParseFromMarkdown(
            "---\ntitle: Extend the skill\nproject: VasoGuard\nstatus: done\ncreated: 2026-05-01T00:00:00\n" +
            "updated: 2026-05-04T00:00:00\nbranch:\n\n## Resolution\n\nFixed.\n\n---\n\nMore.\n", "x.md");

        Assert.Equal("Extend the skill", note.Title);
        Assert.Equal("VasoGuard", note.Project);
        Assert.Equal(NoteStatus.Done, note.Status);
        Assert.Equal("## Resolution\n\nFixed.\n\n---\n\nMore.", note.Body);
    }

    [Fact]
    public void RepeatedKey_LastOneWins()
    {
        var note = Note.ParseFromMarkdown(
            "---\ntitle: x\nstatus: open\ncreated: 2026-04-21T20:30:00\nstatus: done\n---\n\nb\n", "x.md");

        Assert.Equal(NoteStatus.Done, note.Status);
    }

    [Fact]
    public void UnknownHyphenatedKey_StaysInTheHeaderNotTheBody()
    {
        var note = Note.ParseFromMarkdown(
            "---\ntitle: x\nparent-branch: fix/something\n---\n\nThe body.\n", "x.md");

        Assert.Equal("x", note.Title);
        Assert.Equal("The body.", note.Body);
    }

    [Fact]
    public void EmptyFile_StillLoads()
    {
        WriteNote("2026-05-08-empty.md", "");

        Assert.Equal("empty", _service.LoadAll().Single().Title);
    }

    [Fact]
    public void SortOrder_IsSavedOnlyWhenSet()
    {
        var plain = new Note { Title = "x" };
        var moved = new Note { Title = "x", SortOrder = 5 };

        Assert.DoesNotContain("sortOrder", plain.ToMarkdown());
        Assert.Equal(5, Note.ParseFromMarkdown(moved.ToMarkdown(), "x.md").SortOrder);
    }

    [Fact]
    public void Reorder_SurvivesReload_AndDoesNotTouchUpdatedOrUnmovedFiles()
    {
        var oldStamp = new DateTime(2026, 1, 1, 9, 0, 0);
        WriteNote("2026-01-01-oldest.md", Header("Oldest", "2026-01-01T00:00:00", oldStamp));
        WriteNote("2026-01-02-middle.md", Header("Middle", "2026-01-02T00:00:00", oldStamp));
        WriteNote("2026-01-03-newest.md", Header("Newest", "2026-01-03T00:00:00", oldStamp));
        var untouchedFile = File.ReadAllText(Path.Combine(_notesDir, "2026-01-01-oldest.md"));

        var settings = new SettingsService(_testDir);
        var vm = new MainViewModel(_service, new ProjectService(_testDir), settings);
        var group = vm.MediumPriority;
        Assert.Equal(new[] { "Newest", "Middle", "Oldest" }, group.Select(n => n.Title));

        // Drop Oldest onto Newest: Oldest moves to the top.
        group[0].AcceptDrop(group[2]);

        var titles = _service.LoadAll().Select(n => n.Title).ToArray();
        Assert.Equal(new[] { "Oldest", "Newest", "Middle" }, titles);
        Assert.All(_service.LoadAll(), n => Assert.Equal(oldStamp, n.Updated));
        Assert.Equal(untouchedFile, File.ReadAllText(Path.Combine(_notesDir, "2026-01-01-oldest.md")));
    }

    [Fact]
    public void LockedFile_IsReportedInsteadOfVanishingSilently()
    {
        var path = WriteNote("2026-01-01-locked.md", Header("Locked", "2026-01-01T00:00:00", DateTime.Now));
        WriteNote("2026-01-02-fine.md", Header("Fine", "2026-01-02T00:00:00", DateTime.Now));

        var vm = new MainViewModel(_service, new ProjectService(_testDir), new SettingsService(_testDir));
        Assert.Null(vm.UnreadableNotesMessage);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            vm.RefreshNotes();

            Assert.Equal("1 note could not be read", vm.UnreadableNotesMessage);
            Assert.Contains("2026-01-01-locked.md", vm.UnreadableNotesDetail);
            Assert.Equal("Fine", vm.MediumPriority.Single().Title);
        }

        vm.RefreshNotes();
        Assert.Null(vm.UnreadableNotesMessage);
    }

    private string WriteNote(string fileName, string content)
    {
        var path = Path.Combine(_notesDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Header(string title, string created, DateTime updated) =>
        $"---\ntitle: {title}\nproject: \npriority: medium\nstatus: open\ntags: []\nattachments: []\n" +
        $"created: {created}\nupdated: {updated:yyyy-MM-ddTHH:mm:ss}\n---\n\nBody of {title}.\n";
}
