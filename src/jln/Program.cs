using System.Text;

// Titles and bodies are any text, so input and output are UTF-8 (no byte-order mark).
try
{
    Console.InputEncoding = new UTF8Encoding(false);
    Console.OutputEncoding = new UTF8Encoding(false);
}
catch (System.IO.IOException)
{
    // No console to set (started detached); the streams still work.
}

return JLNotes.Cli.NoteCommands.Run(args);
