using System;
using System.IO;

namespace BlockCraftLauncher.Services;

public static class Config
{
    // ЗАМЕНИ на свои значения
    public const string GitHubOwner = "Anrehahus";
    public const string GitHubRepo = "BlockCraft-versions";
    public const string GameExeName = "BlockCraft.exe";
    public const string LauncherTag = "Launcher";   // тег релиза с лаунчером (регистр важен)

    public static readonly string DefaultRootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".BlockCraft");

    public static string RootDir =>
        string.IsNullOrWhiteSpace(LauncherSettings.Current.InstallDir)
            ? DefaultRootDir
            : LauncherSettings.Current.InstallDir;

    public static string VersionsDir => Path.Combine(RootDir, "versions");
    public static string TempDir => Path.Combine(RootDir, "temp");
}