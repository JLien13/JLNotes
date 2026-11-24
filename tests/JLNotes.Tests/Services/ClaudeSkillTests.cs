using System.Diagnostics;
using System.IO;
using JLNotes.Cli;
using JLNotes.Services;

namespace JLNotes.Tests.Services;

// Settings > Claude > Connect writes one skill file into Claude Code's own folder, so
// Claude finds, saves and restores notes through jln. JL Notes keeps a skill it wrote
// up to date and never touches one it did not write.
public class ClaudeSkillTests : IDisposable
{
    private readonly string _root;
    private readonly string _claudeDir;
    private readonly string _jln;
    private readonly List<string> _links = [];

    public ClaudeSkillTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"jlnotes-claude-{Guid.NewGuid()}");
        _claudeDir = Path.Combine(_root, ".claude");
        Directory.CreateDirectory(_claudeDir);
        _jln = Path.Combine(_root, "JLNotes", "jln.exe");
        WriteFile(_jln, "");
    }

    public void Dispose()
    {
        foreach (var link in _links.Where(Directory.Exists))
            Directory.Delete(link); // the link itself, never what it points at
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private ClaudeSkill Skill => new(_claudeDir, _jln);

    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>A folder link that needs no admin rights, as a skills repo linked into Claude's folder is.</summary>
    private void LinkFolder(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        using var p = Process.Start(new ProcessStartInfo("cmd", $"/c mklink /J \"{link}\" \"{target}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.WaitForExit();
        Assert.True(Directory.Exists(link), "the link was not made");
        _links.Add(link);
    }

    [Fact]
    public void Connect_WritesTheSkillWhereClaudeLooksForIt()
    {
        Assert.Equal(ClaudeSkillState.NotConnected, Skill.State);

        Assert.Equal(ClaudeSkillState.Connected, Skill.Connect());

        var path = Path.Combine(_claudeDir, "skills", "jl-notes", "SKILL.md");
        Assert.Equal(path, Skill.SkillPath);
        Assert.Equal(Skill.SkillText, File.ReadAllText(path));
        Assert.Equal(ClaudeSkillState.Connected, Skill.State);
    }

    [Fact]
    public void TheSkill_OpensWithClaudesHeader_AndPointsAtThisPcsJln()
    {
        var text = Skill.SkillText;

        Assert.StartsWith("---\nname: jl-notes\ndescription: ", text);
        Assert.Contains($"\"{_jln.Replace('\\', '/')}\"", text);
        Assert.DoesNotContain("\r", text);
    }

    [Fact]
    public void TheSkillsCommands_AreTheOnesJlnPrints()
    {
        var usage = NoteCommands.Usage.ReplaceLineEndings("\n");

        Assert.Contains(usage, Skill.SkillText);
        Assert.Contains(usage, Skill.Instructions);
    }

    [Fact]
    public void Instructions_AreAMessageToPaste_NotASkillFile()
    {
        var text = Skill.Instructions;

        Assert.StartsWith("Set yourself up to use my JL Notes app", text);
        Assert.DoesNotContain("name: jl-notes", text);
        Assert.Contains($"\"{_jln.Replace('\\', '/')}\"", text);
    }

    [Fact]
    public void NoClaudeFolder_NothingIsMade()
    {
        Directory.Delete(_claudeDir);

        Assert.Equal(ClaudeSkillState.NoClaude, Skill.State);
        Assert.Equal(ClaudeSkillState.NoClaude, Skill.Connect());
        Assert.False(Directory.Exists(_claudeDir));
    }

    [Fact]
    public void NoJln_NothingIsWritten()
    {
        File.Delete(_jln);

        Assert.Equal(ClaudeSkillState.NoJln, Skill.State);
        Assert.Equal(ClaudeSkillState.NoJln, Skill.Connect());
        Assert.False(File.Exists(Skill.SkillPath));
    }

    [Fact]
    public void ASkillSavedWithOtherLineEnds_IsStillConnected()
    {
        WriteFile(Skill.SkillPath, Skill.SkillText.Replace("\n", "\r\n"));

        Assert.Equal(ClaudeSkillState.Connected, Skill.State);
    }

    [Fact]
    public void ASkillAnOlderJlNotesWrote_IsBroughtUpToDateAtStart()
    {
        Skill.Connect();
        // The same PC after an update that put the app, and so jln, somewhere else.
        var moved = Path.Combine(_root, "Moved", "jln.exe");
        WriteFile(moved, "");
        var updated = new ClaudeSkill(_claudeDir, moved);
        Assert.Equal(ClaudeSkillState.OutOfDate, updated.State);

        updated.KeepUpToDate();

        Assert.Equal(ClaudeSkillState.Connected, updated.State);
        Assert.Contains("Moved/jln.exe", File.ReadAllText(updated.SkillPath));
    }

    [Fact]
    public void KeepUpToDate_NeverConnectsByItself()
    {
        Skill.KeepUpToDate();

        Assert.Equal(ClaudeSkillState.NotConnected, Skill.State);
        Assert.False(Directory.Exists(Skill.SkillDir));
    }

    [Fact]
    public void ASkillSomeoneElseWrote_IsLeftAsItIs()
    {
        const string theirs = "---\nname: jl-notes\ndescription: mine\n---\nWrite files to my notes folder.\n";
        WriteFile(Skill.SkillPath, theirs);

        Assert.Equal(ClaudeSkillState.NotOurs, Skill.State);
        Assert.Equal(ClaudeSkillState.NotOurs, Skill.Connect());
        Skill.KeepUpToDate();

        Assert.Equal(theirs, File.ReadAllText(Skill.SkillPath));
        Assert.Contains(Skill.SkillPath, Skill.Describe(ClaudeSkillState.NotOurs).Text);
    }

    [Fact]
    public void ASkillFolderLinkedFromElsewhere_IsLeftAsItIs()
    {
        // A skills repo linked into Claude's folder: writing through the link would change the repo.
        var repo = Path.Combine(_root, "skills-repo", "jl-notes");
        Directory.CreateDirectory(repo);
        LinkFolder(Skill.SkillDir, repo);

        Assert.Equal(ClaudeSkillState.NotOurs, Skill.State);
        Assert.Equal(ClaudeSkillState.NotOurs, Skill.Connect());
        Skill.KeepUpToDate();

        Assert.Empty(Directory.GetFileSystemEntries(repo));
        Assert.Contains(repo, Skill.Describe(ClaudeSkillState.NotOurs).Text);
    }

    [Fact]
    public void EvenJlNotesOwnSkill_IsLeftAlone_WhenItsFolderIsALink()
    {
        var repo = Path.Combine(_root, "skills-repo", "jl-notes");
        var older = Skill.SkillText + "an older JL Notes wrote this\n";
        WriteFile(Path.Combine(repo, "SKILL.md"), older);
        LinkFolder(Skill.SkillDir, repo);

        Skill.KeepUpToDate();

        Assert.Equal(ClaudeSkillState.NotOurs, Skill.State);
        Assert.Equal(older, File.ReadAllText(Path.Combine(repo, "SKILL.md")));
    }

    [Theory]
    [InlineData(ClaudeSkillState.NotConnected, "Connect")]
    [InlineData(ClaudeSkillState.OutOfDate, "Update")]
    [InlineData(ClaudeSkillState.Connected, null)]
    [InlineData(ClaudeSkillState.NoClaude, null)]
    [InlineData(ClaudeSkillState.NoJln, null)]
    [InlineData(ClaudeSkillState.NotOurs, null)]
    public void SettingsOffersAButton_OnlyWhereConnectWouldWrite(ClaudeSkillState state, string? button)
    {
        var status = Skill.Describe(state);

        Assert.Equal(button, status.Button);
        Assert.NotEmpty(status.Text);
    }
}
