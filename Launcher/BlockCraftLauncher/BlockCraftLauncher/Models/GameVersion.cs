using System.Text.Json.Serialization;
using System.ComponentModel;

namespace BlockCraftLauncher.Models;

public class GameVersion : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new(name));

    public string Tag { get; set; } = "";
    public string Name { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public long Size { get; set; }
    public DateTime PublishedAt { get; set; }

    private bool _isInstalled;
    public bool IsInstalled
    {
        get => _isInstalled;
        set { _isInstalled = value; Raise(nameof(IsInstalled)); Raise(nameof(StatusText)); Raise(nameof(ActionText)); }
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; Raise(nameof(IsBusy)); }
    }

    private double _progress;
    public double Progress
    {
        get => _progress;
        set { _progress = value; Raise(nameof(Progress)); }
    }

    public bool IsLatest { get; set; }
    public string ActionText => IsInstalled ? "ИГРАТЬ" : "УСТАНОВИТЬ";
    public string DateText => PublishedAt == default ? "" : PublishedAt.ToLocalTime().ToString("dd.MM.yyyy");

    public string StatusText => IsInstalled ? "Установлена" : "Не скачана";
    public string SizeText => Size > 0 ? $"{Size / 1024.0 / 1024.0:F1} МБ" : "—";

    public override string ToString() => Tag;
}

// Классы под JSON-ответ GitHub API
public class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("published_at")] public DateTime PublishedAt { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = new();

    [JsonPropertyName("body")] public string? Body { get; set; }
}

public class GitHubAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
}