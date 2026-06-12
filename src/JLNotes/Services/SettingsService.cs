using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using JLNotes.Models;

namespace JLNotes.Services;

public class SettingsService
{
    private readonly string _settingsPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsService(string baseDir)
    {
        _settingsPath = Path.Combine(baseDir, "settings.json");
    }

    /// <summary>True once settings.json exists on disk — i.e. this is NOT a
    /// first run. Lets callers apply first-install defaults exactly once.</summary>
    public bool Exists => File.Exists(_settingsPath);

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        var json = File.ReadAllText(_settingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", true);

        if (key == null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? "";
            key.SetValue("JLNotes", $"\"{exePath}\" --minimized");
        }
        else
        {
            key.DeleteValue("JLNotes", false);
        }
    }
}
