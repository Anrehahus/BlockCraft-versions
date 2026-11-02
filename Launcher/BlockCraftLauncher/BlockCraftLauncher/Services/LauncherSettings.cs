using System.IO;
using System.Text.Json;

namespace BlockCraftLauncher.Services;

public class LauncherSettings
{
    public string Nickname { get; set; } = "Player";
    public string InstallDir { get; set; } = "";      // пусто = папка по умолчанию
    public bool CloseOnLaunch { get; set; } = false;
    public string LastVersion { get; set; } = "";
    public string ActiveSkin { get; set; } = "";   // имя файла в папке skins

    private static string SettingsPath => Path.Combine(Config.DefaultRootDir, "settings.json");

    public static LauncherSettings Current { get; } = Load();

    private static LauncherSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(SettingsPath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Config.DefaultRootDir);
        File.WriteAllText(SettingsPath,
            JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}