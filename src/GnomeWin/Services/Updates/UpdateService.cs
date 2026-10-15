using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using GnomeWin.Platform.Startup;
using GnomeWin.Services.Logging;
using GnomeWin.Setup;

namespace GnomeWin.Services.Updates;

public sealed record UpdateInfo(Version Version, string Tag, string Name, string Notes, string PageUrl, string DownloadUrl, long Size);

/// <summary>
/// Looks for a newer GnomeWin release on GitHub and installs it with the published setup, which stops
/// the running shell, replaces the files and starts it again.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string Repository = "traxcs/GNOME-WINDOWS";
    private const string AssetName = "GnomeWin-Setup.exe";

    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GnomeWin/" + Installer.Version);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static Version CurrentVersion => Parse(Installer.Version) ?? new Version(0, 0);

    /// <summary>Returns the latest release when it is newer than the running build, otherwise null.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken token = default)
    {
        var release = await LatestReleaseAsync(token).ConfigureAwait(false);
        if (release == null) return null;
        if (release.Version <= CurrentVersion)
        {
            Log.Debug($"Update check: {release.Tag} is not newer than {CurrentVersion}.");
            return null;
        }
        Log.Info($"Update available: {release.Tag} (running {CurrentVersion}).");
        return release;
    }

    public async Task<UpdateInfo?> LatestReleaseAsync(CancellationToken token = default)
    {
        try
        {
            string json = await _http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases/latest", token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? string.Empty : string.Empty;
            var version = Parse(tag);
            if (version == null) return null;

            string url = string.Empty;
            long size = 0;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    if (!name.Equals(AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    url = asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() ?? string.Empty : string.Empty;
                    size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    break;
                }
            }
            if (url.Length == 0)
            {
                Log.Warn($"Release {tag} carries no {AssetName}.");
                return null;
            }

            return new UpdateInfo(
                version, tag,
                root.TryGetProperty("name", out var rn) ? rn.GetString() ?? tag : tag,
                root.TryGetProperty("body", out var b) ? b.GetString() ?? string.Empty : string.Empty,
                root.TryGetProperty("html_url", out var h) ? h.GetString() ?? string.Empty : string.Empty,
                url, size);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Warn("Update check failed", ex);
            return null;
        }
    }

    /// <summary>Downloads the setup into the cache folder and returns its path, or null when it fails.</summary>
    public async Task<string?> DownloadAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken token = default)
    {
        string dir = Path.Combine(AppPaths.Cache, "updates");
        string file = Path.Combine(dir, $"GnomeWin-Setup-{update.Tag}.exe");
        try
        {
            Directory.CreateDirectory(dir);
            if (File.Exists(file) && update.Size > 0 && new FileInfo(file).Length == update.Size)
            {
                progress?.Report(1);
                return file;
            }

            string temp = file + ".part";
            using (var response = await _http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? update.Size;
                using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                using var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[128 * 1024];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    done += read;
                    if (total > 0) progress?.Report(Math.Min(1, done / (double)total));
                }
            }
            File.Move(temp, file, true);
            Cleanup(dir, file);
            Log.Info($"Update {update.Tag} downloaded to {file}.");
            return file;
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not download update {update.Tag}", ex);
            try { if (File.Exists(file + ".part")) File.Delete(file + ".part"); } catch { }
            return null;
        }
    }

    /// <summary>
    /// Hands over to the downloaded setup: it quits this shell, replaces the installed files and starts it again.
    /// </summary>
    public static bool Install(string setupPath)
    {
        try
        {
            var psi = new ProcessStartInfo(setupPath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(setupPath) ?? string.Empty,
            };
            psi.ArgumentList.Add("--install");
            psi.ArgumentList.Add("--quiet");
            psi.ArgumentList.Add("--relaunch");
            if (StartupManager.IsEnabled()) psi.ArgumentList.Add("--startup");
            Process.Start(psi)?.Dispose();
            Log.Info("Update installer started.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the update installer", ex);
            return false;
        }
    }

    private static void Cleanup(string dir, string keep)
    {
        try
        {
            foreach (var old in Directory.EnumerateFiles(dir))
                if (!string.Equals(old, keep, StringComparison.OrdinalIgnoreCase)) File.Delete(old);
        }
        catch (Exception ex) { Log.Debug($"Update cache cleanup failed: {ex.Message}"); }
    }

    /// <summary>Reads "v1.2.3" or "1.2.3-beta" as a version, ignoring any suffix.</summary>
    public static Version? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string text = tag.Trim().TrimStart('v', 'V');
        int cut = text.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut > 0) text = text[..cut];
        return Version.TryParse(text, out var v)
            ? new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision))
            : null;
    }

    public void Dispose() => _http.Dispose();
}
