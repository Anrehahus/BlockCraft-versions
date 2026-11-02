using BlockCraftLauncher.Models;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace BlockCraftLauncher.Services;

public class GitHubService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MyLauncher/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static HttpClient SharedClient => Http;

    public async Task<List<GameVersion>> GetVersionsAsync()
    {
        string url = $"https://api.github.com/repos/{Config.GitHubOwner}/{Config.GitHubRepo}/releases";
        string json = await Http.GetStringAsync(url);

        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json) ?? new();
        var result = new List<GameVersion>();

        foreach (var r in releases)
        {
            var asset = r.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null) continue; // релиз без zip пропускаем

            result.Add(new GameVersion
            {
                Tag = r.TagName,
                Name = string.IsNullOrWhiteSpace(r.Name) ? r.TagName : r.Name!,
                DownloadUrl = asset.DownloadUrl,
                Size = asset.Size,
                PublishedAt = r.PublishedAt,
                IsInstalled = Directory.Exists(Path.Combine(Config.VersionsDir, r.TagName))
            });
        }
        return result.OrderByDescending(v => v.PublishedAt).ToList();
    }
}