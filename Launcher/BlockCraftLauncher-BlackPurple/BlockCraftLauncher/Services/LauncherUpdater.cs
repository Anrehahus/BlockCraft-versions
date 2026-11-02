using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BlockCraftLauncher.Models;

namespace BlockCraftLauncher.Services;

public class LauncherUpdateInfo
{
    public Version Version { get; init; } = new(0, 0, 0);
    public string Tag { get; init; } = "";
    public string Notes { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public long Size { get; init; }
    public string SizeText => $"{Size / 1024.0 / 1024.0:F1} МБ";
}

public static class LauncherUpdater
{
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public static Version Current =>
        Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0));

    public static string CurrentText => "v" + Current;

    public static async Task<LauncherUpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        string url = $"https://api.github.com/repos/{Config.GitHubOwner}/{Config.LauncherRepo}/releases/latest";
        string json = await GitHubService.SharedClient.GetStringAsync(url, ct);
        var r = JsonSerializer.Deserialize<GitHubRelease>(json);
        if (r == null) return null;

        var asset = r.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (asset == null || !Version.TryParse(r.TagName.TrimStart('v', 'V'), out var ver)) return null;

        return new LauncherUpdateInfo
        {
            Version = Normalize(ver),
            Tag = r.TagName,
            Notes = r.Body?.Trim() ?? "",
            DownloadUrl = asset.DownloadUrl,
            Size = asset.Size
        };
    }

    public static bool IsNewer(LauncherUpdateInfo info) => info.Version > Current;

    /// Скачивает обновление, запускает скрипт замены файлов. После возврата нужно закрыть приложение.
    public static async Task ApplyAsync(LauncherUpdateInfo info, IProgress<double> progress, CancellationToken ct = default)
    {
        string work = Path.Combine(Path.GetTempPath(), "BlockCraftLauncherUpdate");
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);

        string zip = Path.Combine(work, "update.zip");
        string src = Path.Combine(work, "files");

        using (var response = await GitHubService.SharedClient.GetAsync(
                   info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? info.Size;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(zip);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress.Report((double)read / total * 100);
            }
        }

        await Task.Run(() => ZipFile.ExtractToDirectory(zip, src), ct);

        // если архив содержит одну папку внутри - берём её содержимое
        if (Directory.GetFiles(src).Length == 0 && Directory.GetDirectories(src).Length == 1)
            src = Directory.GetDirectories(src)[0];

        string exe = Environment.ProcessPath!;
        string dst = AppContext.BaseDirectory.TrimEnd('\\');
        int pid = Environment.ProcessId;

        string script = $"""
            @echo off
            chcp 65001 >nul
            :wait
            tasklist /FI "PID eq {pid}" | find "{pid}" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto wait
            )
            xcopy "{src}\*" "{dst}\" /E /Y /Q /I >nul
            start "" "{exe}"
            """;

        string bat = Path.Combine(work, "update.bat");
        File.WriteAllText(bat, script, new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo(bat) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
    }
}