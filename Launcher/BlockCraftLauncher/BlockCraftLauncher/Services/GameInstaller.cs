using BlockCraftLauncher.Models;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace BlockCraftLauncher.Services;

public class GameInstaller
{
    public async Task InstallAsync(GameVersion version, IProgress<double> progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Config.TempDir);
        Directory.CreateDirectory(Config.VersionsDir);

        string zipPath = Path.Combine(Config.TempDir, $"{version.Tag}.zip");
        string targetDir = Path.Combine(Config.VersionsDir, version.Tag);
        string tempExtract = targetDir + "_tmp";

        // 1. Скачивание с прогрессом
        using (var response = await GitHubService.SharedClient.GetAsync(
                     version.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? version.Size;

            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(zipPath);

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

        // 2. Распаковка во временную папку, потом переименование,
        //    чтобы при обрыве не осталась "полуустановленная" версия
        if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, tempExtract), ct);

        if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
        Directory.Move(tempExtract, targetDir);

        File.Delete(zipPath);
        version.IsInstalled = true;
    }

    public void Launch(GameVersion version, string nickname)
    {
        string dir = Path.Combine(Config.VersionsDir, version.Tag);
        string? exePath = FindGameExe(dir);
        if (exePath == null)
            throw new FileNotFoundException("В папке версии не найден exe игры: " + dir);

        Process.Start(new ProcessStartInfo(exePath)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            Arguments = $"-username {nickname}",
            UseShellExecute = true
        });
    }

    private static string? FindGameExe(string dir)
    {
        if (!Directory.Exists(dir)) return null;

        return Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).Contains("UnityCrashHandler", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar)) // ближайший к корню
            .FirstOrDefault();
    }

    public void Uninstall(GameVersion version)
    {
        string dir = Path.Combine(Config.VersionsDir, version.Tag);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        version.IsInstalled = false;
    }
}