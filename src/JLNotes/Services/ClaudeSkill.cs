using System.IO;
using JLNotes.Cli;

namespace JLNotes.Services;

/// <summary>How Claude Code and JL Notes are joined on this PC.</summary>
public enum ClaudeSkillState
{
    /// <summary>jln.exe is not beside the app, so there is nothing for Claude to run.</summary>
    NoJln,
    /// <summary>Claude Code has no folder on this PC, so there is nowhere to put the skill.</summary>
    NoClaude,
    /// <summary>Claude has no JL Notes skill.</summary>
    NotConnected,
    /// <summary>The skill is the one this JL Notes writes.</summary>
    Connected,
    /// <summary>JL Notes wrote the skill, but not this text (an older JL Notes, or the app moved).</summary>
    OutOfDate,
    /// <summary>A JL Notes skill is there that JL Notes did not write (made by hand, or a
    /// link to a folder kept somewhere else). It is never touched.</summary>
    NotOurs,
}

/// <summary>What Settings says about a state, and the one button it offers (null: none).</summary>
public sealed record ClaudeSkillStatus(string Text, string? Button);

/// <summary>
/// The one owner of how Claude Code is joined to JL Notes on this PC: a skill file in
/// Claude's own folder (%USERPROFILE%\.claude\skills\jl-notes\SKILL.md) that tells Claude
/// to find, save and restore notes through jln.exe. JL Notes writes it when the user
/// asks (Settings > Claude > Connect) and from then on keeps it up to date at each
/// start, so the skill can never fall behind the app. The commands in it are
/// NoteCommands.Usage itself, not a copy. A skill JL Notes did not write is left alone.
/// </summary>
public sealed class ClaudeSkill(string claudeDir, string jlnPath)
{
    private const string SkillName = "jl-notes";

    // Every skill JL Notes writes carries this, which is how it knows its own.
    private const string WrittenByJlNotes = "<!-- Written by JL Notes";

    /// <summary>Claude Code's folder for this user, and the jln.exe installed beside this app.</summary>
    public static ClaudeSkill ForThisPc() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
        Path.Combine(AppContext.BaseDirectory, "jln.exe"));

    public string SkillDir => Path.Combine(claudeDir, "skills", SkillName);

    public string SkillPath => Path.Combine(SkillDir, "SKILL.md");

    public ClaudeSkillState State
    {
        get
        {
            if (!File.Exists(jlnPath)) return ClaudeSkillState.NoJln;
            if (!Directory.Exists(claudeDir)) return ClaudeSkillState.NoClaude;
            if (LinkTarget() != null) return ClaudeSkillState.NotOurs;
            if (!File.Exists(SkillPath)) return ClaudeSkillState.NotConnected;

            var there = Normalize(File.ReadAllText(SkillPath));
            if (there == SkillText) return ClaudeSkillState.Connected;
            return there.Contains(WrittenByJlNotes) ? ClaudeSkillState.OutOfDate : ClaudeSkillState.NotOurs;
        }
    }

    /// <summary>Writes the skill where JL Notes may (there is none yet, or its own is out
    /// of date) and returns the state afterwards. Any other state is returned as it is.</summary>
    public ClaudeSkillState Connect()
    {
        var state = State;
        if (state is not (ClaudeSkillState.NotConnected or ClaudeSkillState.OutOfDate)) return state;
        Directory.CreateDirectory(SkillDir);
        File.WriteAllText(SkillPath, SkillText);
        return ClaudeSkillState.Connected;
    }

    /// <summary>Run at each start: a skill JL Notes wrote is brought up to date with this
    /// version. It never makes one, since connecting is the user's choice in Settings.</summary>
    public void KeepUpToDate()
    {
        if (State == ClaudeSkillState.OutOfDate) Connect();
    }

    public ClaudeSkillStatus Describe(ClaudeSkillState state) => state switch
    {
        ClaudeSkillState.NoJln => new(
            $"jln.exe, the command Claude runs, is missing: {jlnPath}. Install JL Notes again to put it back.", null),
        ClaudeSkillState.NoClaude => new(
            "Claude Code was not found on this PC. Install it and start it once, then come back here.", null),
        ClaudeSkillState.NotConnected => new(
            "Not connected. Connect lets Claude Code find, save and restore your notes.", "Connect"),
        ClaudeSkillState.Connected => new(
            "Connected. In a new Claude Code session, say \"log this to my notes\".", null),
        ClaudeSkillState.OutOfDate => new(
            "Connected, but Claude's copy of the instructions is out of date.", "Update"),
        _ => new(
            $"Claude already has a JL Notes skill that JL Notes did not write, so it is left alone: {WhereNotOurs()}. " +
            "Remove it, then Connect, and JL Notes keeps it up to date.", null),
    };

    /// <summary>The whole skill file: Claude's header, the mark of JL Notes, then the guide.</summary>
    public string SkillText => Normalize($"""
        ---
        name: {SkillName}
        description: Use when the user says "log to my notes", "add to my notes", "save to notes", "note this", or any variation of saving information to their JL Notes app. Also use when the user mentions their notes app, wants to find or read a note, or wants an earlier or deleted note back.
        ---
        {WrittenByJlNotes} (Settings > Claude). JL Notes keeps this file up to date: changes made here are replaced. -->

        # JL Notes

        {Guide}

        """);

    /// <summary>The same guide as a message to paste to an AI that JL Notes cannot set up
    /// by itself (anything that is not Claude Code but can run a command on this PC).</summary>
    public string Instructions => Normalize($"""
        Set yourself up to use my JL Notes app on this PC. Keep what follows as a standing instruction (a skill, rule or memory, whichever you have) and use it whenever I ask you to save something to my notes, find a note, or bring one back.

        {Guide}

        """);

    // How an AI uses the notes. The skill and the pasted instructions are both this text.
    private string Guide => $"""
        JL Notes is the user's notes app on this PC. Find, read, save and restore its notes
        only through jln, the command that ships with the app:

        ```
        "{jlnPath.Replace('\\', '/')}"
        ```

        Below, `jln` stands for that quoted path (in PowerShell, put `&` in front of it).
        jln saves through the app's own code, so a note it writes always shows in the app,
        and whatever it changes is kept in the app's history.

        Never write or edit a note file yourself. If jln.exe is not at that path, JL Notes
        was moved or removed: stop and tell the user to open JL Notes, Settings, Claude. Do
        not fall back to writing a file.

        ## Commands

        ```
        {NoteCommands.Usage}
        ```

        A note is one jln call. No agent is needed.

        - **Save:** `jln new` prints the new note's path. Only `--title` is required. For a
          body of several lines, give `--body -` and send the body on input.
        - **Find:** `jln find` prints one note per line, newest change first. The first
          column is the note's name, which every other command takes.
        - **Change:** `jln set` changes only the options given, and `jln append` adds a line
          to the end of the body. To change part of a body, `jln show` it, then
          `jln set <note> --body -` with the whole new text.
        - **Link:** write `[[name]]` in a body to link to another note, or `[[name|words]]`
          to show other words.
        - **Undo:** every change and delete is kept. `jln history` lists a note's earlier
          versions, `jln deleted` lists deleted notes, and `jln restore` puts one back.

        ## Where the notes are

        `jln where` prints the notes folder. Settings can move it (to a Dropbox folder that
        several PCs share), so never assume a path. If jln says the folder can't be found,
        tell the user what it said, and do not write the note anywhere else.
        """;

    /// <summary>Where the skill folder, or the skill file in it, is a link to; null when neither is.</summary>
    private string? LinkTarget() =>
        new DirectoryInfo(SkillDir) is { Exists: true, LinkTarget: { } folder } ? folder
        : new FileInfo(SkillPath) is { Exists: true, LinkTarget: { } file } ? file
        : null;

    private string WhereNotOurs() => LinkTarget() is { } target ? $"{SkillDir} (a link to {target})" : SkillPath;

    // One spelling of a line end, so a skill written on one PC reads as the same text on another.
    private static string Normalize(string text) => text.ReplaceLineEndings("\n");
}
