using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using JLNotes.Helpers;

namespace JLNotes.Services;

/// <summary>
/// Self-update over GitHub Releases, ported from the MG Manager Hub (which
/// ported it from the A1i Connector). CheckAsync answers "is a newer build
/// published?", ApplyAsync downloads the installer and launches it silently.
/// Two hard-won carry-overs:
///   1. The installer is launched OUTSIDE our process tree (via a short-lived
///      PowerShell Start-Process) so the installer's own taskkill /T can never
///      sweep the installer away with the app.
///   2. We never relaunch the app ourselves; the installer's [Run] line does it
///      after the files are in place (see WasAppRunningAtStart in JLNotes.iss).
/// JLNotes is a public repo, so no token: plain unauthenticated HTTPS.
/// </summary>
public sealed class UpdateService
{
    private const string FallbackRepoSlug = "JLien13/JLNotes";
    private const string UserAgent = "JLNotes";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>"owner/repo" from the csproj RepositoryUrl (single source shared
    /// with installer\build.ps1's -Release publish step).</summary>
    public static string RepoSlug { get; } = ResolveRepoSlug();

    public static Version CurrentVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0))
            : new Version(0, 0, 0);

    private readonly string _logDir;

    public UpdateService(string logDir) => _logDir = logDir;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            using var res = await Http.GetAsync($"https://api.github.com/repos/{RepoSlug}/releases/latest", cts.Token);
            if (!res.IsSuccessStatusCode)
                return UpdateCheckResult.Failed(CurrentVersion, $"GitHub {(int)res.StatusCode}");
            var json = await res.Content.ReadAsStringAsync(cts.Token);
            return UpdateLogic.ParseLatestRelease(json, CurrentVersion);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return UpdateCheckResult.Failed(CurrentVersion, e is TaskCanceledException ? "timeout" : e.Message);
        }
    }

    /// <summary>Downloads the latest installer and launches it silently. Returns
    /// (true, message) when the installer is running and this process is about to
    /// be closed by it; (false, reason) when nothing was changed.</summary>
    public async Task<(bool Launched, string Message)> ApplyAsync(
        IProgress<(long Got, long? Total)>? progress = null, CancellationToken ct = default)
    {
        var info = await CheckAsync(ct);
        if (!info.Ok) return (false, info.Error ?? "update check failed");
        if (info.UpToDate) return (false, $"Already on the latest version ({info.Current}).");

        var tmpDir = Path.Combine(Path.GetTempPath(), "jlnotes-update-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);
        var setupPath = Path.Combine(tmpDir, info.AssetName!);

        try
        {
            using var res = await Http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) return (false, $"Download failed: GitHub {(int)res.StatusCode}");
            var total = res.Content.Headers.ContentLength;
            await using var src = await res.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(setupPath);
            var buffer = new byte[81920];
            long got = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                got += n;
                progress?.Report((got, total));
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return (false, "Download failed: " + e.Message);
        }

        // /SILENT (not /VERYSILENT): Inno shows its small progress window while
        // the app is closed and files are copied, so the update never looks hung.
        // /RELAUNCHVISIBLE=1 tells the installer's [Run] relaunch to open the panel
        // instead of the tray-only --minimized relaunch a scripted upgrade gets.
        var logPath = Path.Combine(_logDir, "self-update.log");
        var args = new[] { "/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/RELAUNCHVISIBLE=1", $"/LOG={logPath}" };

        // Connector pattern: PowerShell 5.1 joins -ArgumentList naively, so each
        // element carries its own quotes; Start-Process breaks the parent chain.
        var psArgList = string.Join(",", args.Select(a => a.Contains(' ') ? $"'\"{a}\"'" : $"'{a}'"));
        var psCommand = $"$ErrorActionPreference='Stop'; Start-Process -FilePath '{setupPath}' -ArgumentList {psArgList}";

        try
        {
            using var ps = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{psCommand}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (ps == null) return (false, "Could not start the update installer. Nothing was changed.");
            await ps.WaitForExitAsync(ct);
            if (ps.ExitCode != 0) return (false, "Could not start the update installer. Nothing was changed.");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return (false, "Could not start the update installer: " + e.Message);
        }

        return (true, $"Updating to {info.Latest}. JL Notes will close and reopen.");
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, CurrentVersion.ToString()));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return c;
    }

    private static string ResolveRepoSlug()
    {
        var url = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;
        if (string.IsNullOrEmpty(url)) return FallbackRepoSlug;
        var path = url.Replace("https://github.com/", "").TrimEnd('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return path.Count(ch => ch == '/') == 1 ? path : FallbackRepoSlug;
    }
}
