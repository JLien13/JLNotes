using System.IO;

namespace JLNotes.Helpers;

/// <summary>The one folder copy: everything in a folder, subfolders included, that the
/// destination lacks. What is already there stays as it is, so a copy that stopped
/// partway is finished by running it again.</summary>
public static class FolderCopy
{
    public static void CopyMissing(string source, string dest)
    {
        if (!Directory.Exists(source)) return;
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
        {
            var target = Path.Combine(dest, Path.GetFileName(file));
            if (!File.Exists(target))
                File.Copy(file, target);
        }
        foreach (var dir in Directory.GetDirectories(source))
            CopyMissing(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
