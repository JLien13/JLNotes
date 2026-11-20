using System.IO;
using JLNotes.Helpers;
using JLNotes.Models;
using JLNotes.Services;
using JLNotes.ViewModels;

namespace JLNotes.Cli;

/// <summary>
/// The jln command: how Claude, or any tool that can run a command, finds, reads,
/// writes and restores notes. It holds no rules of its own. Search is NoteSearch,
/// note names are NoteIndex, the file format is Note, tags are NoteItemViewModel's
/// one tag parser, and every write and restore is NoteService, so history keeps
/// whatever a command replaces.
/// </summary>
public static class NoteCommands
{
    public const string Usage = """
        jln find [words] [#tag ...] [--project P] [--status open|done]
        jln show <note>
        jln new --title T [--project P] [--priority high|medium|low] [--status open|done]
                [--tags "a, b"] [--repo R] [--branch B] [--body TEXT]
        jln set <note> [any option of new] [--add-tag T] [--remove-tag T]
        jln append <note> --text TEXT
        jln links <note>
        jln history <note>
        jln deleted
        jln restore <note> [version]

        <note> is a note's file name, with or without .md, or without its date when
        only one note has that name. A value of - is read from input.
        """;

    /// <summary>The installed jln.exe: the real notes folder and the console.</summary>
    public static int Run(string[] args) =>
        Run(args, AppPaths.NotesDir, Console.In, Console.Out, Console.Error);

    public static int Run(string[] args, string notesDir, TextReader input, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            error.WriteLine(Usage);
            return 2;
        }

        try
        {
            var options = new Options(args.Skip(1), input);
            using var notes = new NoteService(notesDir);
            switch (args[0].ToLowerInvariant())
            {
                case "find": Find(notes, options, output); break;
                case "show": Show(notes, options, output); break;
                case "new": New(notes, options, output); break;
                case "set": Set(notes, options, output); break;
                case "append": Append(notes, options, output); break;
                case "links": Links(notes, options, output); break;
                case "history": History(notes, options, output); break;
                case "deleted": Deleted(notes, output); break;
                case "restore": Restore(notes, options, output); break;
                default:
                    error.WriteLine($"Unknown command \"{args[0]}\".");
                    error.WriteLine(Usage);
                    return 2;
            }
            return 0;
        }
        catch (CommandError e)
        {
            error.WriteLine(e.Message);
            return 1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"A note file could not be read or written: {e.Message}");
            error.WriteLine("Another program may have it open. Try again in a moment.");
            return 1;
        }
    }

    private static void Find(NoteService notes, Options options, TextWriter output)
    {
        var query = NoteSearch.Parse(string.Join(' ', options.Positional));
        var project = options.Get("project");
        var status = options.Get("status") is { } s ? ParseEnum<NoteStatus>(s, "--status") : (NoteStatus?)null;

        var hits = notes.LoadAll()
            .Where(n => NoteSearch.Matches(n, query)
                        && (project == null || ProjectCatalog.Same(n.Project, project))
                        && (status == null || n.Status == status))
            .OrderByDescending(n => n.Updated)
            .ToList();

        foreach (var note in hits)
            output.WriteLine(Line(note));
        if (hits.Count == 0)
            output.WriteLine("No notes match.");
    }

    private static void Show(NoteService notes, Options options, TextWriter output)
    {
        var note = FindNote(notes.LoadAll(), options);
        output.WriteLine(note.FilePath);
        output.WriteLine(File.ReadAllText(note.FilePath).TrimEnd());
    }

    private static void New(NoteService notes, Options options, TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(options.Get("title")))
            throw new CommandError("A new note needs --title.");
        var note = new Note();
        Apply(note, options);
        notes.Save(note);
        output.WriteLine(note.FilePath);
    }

    private static void Set(NoteService notes, Options options, TextWriter output)
    {
        var note = FindNote(notes.LoadAll(), options);
        if (!Apply(note, options))
            throw new CommandError("Nothing to change. Give an option such as --status done.");
        notes.Save(note);
        output.WriteLine(note.FilePath);
    }

    private static void Append(NoteService notes, Options options, TextWriter output)
    {
        var note = FindNote(notes.LoadAll(), options);
        var text = options.Get("text") ?? throw new CommandError("append needs --text.");
        note.Body = note.Body.Length == 0 ? text : note.Body + "\n" + text;
        notes.Save(note);
        output.WriteLine(note.FilePath);
    }

    private static void Links(NoteService notes, Options options, TextWriter output)
    {
        var all = notes.LoadAll();
        var note = FindNote(all, options);
        var index = new NoteIndex();
        index.Refresh(all);

        output.WriteLine("Links to:");
        var targets = note.GetLinkTargets().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var target in targets)
            output.WriteLine(index.Resolve(target) is { } to ? Line(to) : $"{target} | (no such note)");
        if (targets.Count == 0)
            output.WriteLine("(none)");

        output.WriteLine("Linked from:");
        var from = index.LinkedFrom(note);
        foreach (var source in from)
            output.WriteLine(Line(source));
        if (from.Count == 0)
            output.WriteLine("(none)");
    }

    private static void History(NoteService notes, Options options, TextWriter output)
    {
        var path = FindNoteOrDeleted(notes, options);
        var versions = notes.GetVersions(path);
        foreach (var version in versions)
            output.WriteLine($"{Path.GetFileNameWithoutExtension(version.VersionPath)} | {version.SavedAt:yyyy-MM-dd HH:mm} | {version.Read().Title}");
        if (versions.Count == 0)
            output.WriteLine("No earlier versions.");
    }

    private static void Deleted(NoteService notes, TextWriter output)
    {
        var deleted = notes.GetDeleted();
        foreach (var version in deleted)
            output.WriteLine($"{Path.GetFileNameWithoutExtension(version.NotePath)} | {version.SavedAt:yyyy-MM-dd HH:mm} | {version.Read().Title}");
        if (deleted.Count == 0)
            output.WriteLine("No deleted notes.");
    }

    private static void Restore(NoteService notes, Options options, TextWriter output)
    {
        var path = FindNoteOrDeleted(notes, options);
        var versions = notes.GetVersions(path);
        var wanted = options.Positional.ElementAtOrDefault(1);
        var version = wanted == null
            ? versions.FirstOrDefault() ?? throw new CommandError("That note has no earlier versions.")
            : versions.FirstOrDefault(v => Path.GetFileNameWithoutExtension(v.VersionPath) == wanted)
              ?? throw new CommandError($"No version \"{wanted}\". See: jln history {options.Positional[0]}");
        notes.Restore(version);
        output.WriteLine(path);
    }

    /// <summary>Applies the given options to a note. False if none were given.</summary>
    private static bool Apply(Note note, Options options)
    {
        var changed = false;
        if (options.Get("title") is { } title) { note.Title = title; changed = true; }
        if (options.Get("project") is { } project) { note.Project = project; changed = true; }
        if (options.Get("priority") is { } priority) { note.Priority = ParseEnum<NotePriority>(priority, "--priority"); changed = true; }
        if (options.Get("status") is { } status) { note.Status = ParseEnum<NoteStatus>(status, "--status"); changed = true; }
        if (options.Get("tags") is { } tags) { note.Tags = NoteItemViewModel.ParseTags(tags); changed = true; }
        foreach (var tag in options.All("add-tag"))
        {
            note.Tags = NoteItemViewModel.ParseTags(NoteItemViewModel.FormatTags([.. note.Tags, tag]));
            changed = true;
        }
        foreach (var tag in options.All("remove-tag"))
        {
            note.Tags = note.Tags.Where(t => !t.Equals(tag.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            changed = true;
        }
        if (options.Get("repo") is { } repo) { note.Repo = repo; changed = true; }
        if (options.Get("branch") is { } branch) { note.Branch = branch; changed = true; }
        if (options.Get("body") is { } body) { note.Body = body; changed = true; }
        return changed;
    }

    private static Note FindNote(IReadOnlyList<Note> all, Options options)
    {
        var name = options.Positional.FirstOrDefault() ?? throw new CommandError("Name the note. See: jln find");
        return Pick(all, name) ?? throw new CommandError($"No note named \"{name}\". See: jln find");
    }

    /// <summary>The note a name means; an error listing them if it could mean several.</summary>
    private static Note? Pick(IReadOnlyList<Note> notes, string name)
    {
        var index = new NoteIndex();
        index.Refresh(notes);
        var matches = index.Candidates(name);
        if (matches.Count > 1)
            throw new CommandError($"\"{name}\" could mean any of these. Use the full name:{Environment.NewLine}" +
                                   string.Join(Environment.NewLine, matches.Select(Line)));
        return matches.FirstOrDefault();
    }

    /// <summary>A live note's path, or a deleted one's (history and restore work on both).</summary>
    private static string FindNoteOrDeleted(NoteService notes, Options options)
    {
        var name = options.Positional.FirstOrDefault() ?? throw new CommandError("Name the note. See: jln find");
        return (Pick(notes.LoadAll(), name) ?? Pick(notes.GetDeleted().Select(v => v.Read()).ToList(), name))?.FilePath
               ?? throw new CommandError($"No note or deleted note named \"{name}\". See: jln find, jln deleted");
    }

    /// <summary>One note per line: name | status | priority | project | title | tags.</summary>
    private static string Line(Note note)
    {
        var line = $"{Path.GetFileNameWithoutExtension(note.FilePath)} | {note.Status.ToString().ToLowerInvariant()} | " +
                   $"{note.Priority.ToString().ToLowerInvariant()} | {(note.Project.Length > 0 ? note.Project : "-")} | {note.Title}";
        return note.Tags.Count > 0 ? $"{line} | {string.Join(' ', note.Tags.Select(NoteSearch.TagToken))}" : line;
    }

    private static T ParseEnum<T>(string value, string option) where T : struct, Enum =>
        Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new CommandError($"{option} must be one of: {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}.");

    private sealed class CommandError(string message) : Exception(message);

    /// <summary>Words, then "--name value" pairs. Some names may repeat (--add-tag).</summary>
    private sealed class Options
    {
        private static readonly HashSet<string> Known =
            ["title", "project", "priority", "status", "tags", "add-tag", "remove-tag", "repo", "branch", "body", "text"];

        private readonly Dictionary<string, List<string>> _values = [];

        public List<string> Positional { get; } = [];

        public Options(IEnumerable<string> args, TextReader input)
        {
            using var arg = args.GetEnumerator();
            while (arg.MoveNext())
            {
                if (!arg.Current.StartsWith("--"))
                {
                    Positional.Add(arg.Current);
                    continue;
                }
                var name = arg.Current[2..].ToLowerInvariant();
                if (!Known.Contains(name))
                    throw new CommandError($"Unknown option {arg.Current}.");
                if (!arg.MoveNext())
                    throw new CommandError($"{arg.Current} needs a value.");
                var value = arg.Current == "-" ? input.ReadToEnd().TrimEnd('\r', '\n') : arg.Current;
                if (!_values.TryGetValue(name, out var list))
                    _values[name] = list = [];
                list.Add(value);
            }
        }

        public string? Get(string name) => _values.TryGetValue(name, out var list) ? list[^1] : null;

        public IReadOnlyList<string> All(string name) => _values.TryGetValue(name, out var list) ? list : [];
    }
}
