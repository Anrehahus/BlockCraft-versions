using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using BlockCraftLauncher.Models;

namespace BlockCraftLauncher.Services;

public static class SkinService
{
    private const long MaxBytes = 512 * 1024;
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static string Dir => Path.Combine(Config.DefaultRootDir, "skins");

    /// null = всё ок, иначе текст ошибки
    public static string? Validate(string path)
    {
        if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return "нужен файл .png";
        var info = new FileInfo(path);
        if (!info.Exists) return "файл не найден";
        if (info.Length > MaxBytes) return "файл слишком большой (макс. 512 КБ)";

        var header = new byte[24];
        using var fs = File.OpenRead(path);
        if (fs.Read(header, 0, 24) < 24 || !header.AsSpan(0, 8).SequenceEqual(PngSignature))
            return "это не PNG-файл";

        int w = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        int h = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        if (w != 64 || (h != 64 && h != 32))
            return $"размер {w}×{h}, нужен 64×64 или 64×32";
        return null;
    }

    public static string? Import(string sourcePath, out string fileName)
    {
        fileName = "";
        string? error = Validate(sourcePath);
        if (error != null) return error;

        Directory.CreateDirectory(Dir);
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        string name = baseName + ".png";
        for (int i = 2; File.Exists(Path.Combine(Dir, name)); i++) name = $"{baseName}_{i}.png";

        File.Copy(sourcePath, Path.Combine(Dir, name));
        fileName = name;
        return null;
    }

    public static string? GetActivePath()
    {
        string file = LauncherSettings.Current.ActiveSkin;
        if (string.IsNullOrEmpty(file)) return null;
        string path = Path.Combine(Dir, file);
        return File.Exists(path) ? path : null;
    }

    public static void Delete(string fileName)
    {
        string path = Path.Combine(Dir, fileName);
        if (File.Exists(path)) File.Delete(path);
        if (LauncherSettings.Current.ActiveSkin == fileName)
        {
            LauncherSettings.Current.ActiveSkin = "";
            LauncherSettings.Current.Save();
        }
    }

    public static List<SkinItem> LoadAll(string activeFile)
    {
        var list = new List<SkinItem>();
        if (!Directory.Exists(Dir)) return list;

        foreach (var path in Directory.EnumerateFiles(Dir, "*.png").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var full = LoadBitmap(path);
                list.Add(new SkinItem
                {
                    FileName = Path.GetFileName(path),
                    FilePath = path,
                    IsActive = Path.GetFileName(path) == activeFile,
                    SizeText = $"{full.PixelWidth}×{full.PixelHeight} PNG",
                    Full = full,
                    Head = Crop(full, 8, 8),   // лицо
                    Hat = Crop(full, 40, 8)    // второй слой головы
                });
            }
            catch { /* битый файл пропускаем */ }
        }
        return list;
    }

    private static BitmapSource Crop(BitmapSource src, int x, int y)
    {
        var c = new CroppedBitmap(src, new Int32Rect(x, y, 8, 8));
        c.Freeze();
        return c;
    }

    // OnLoad, чтобы файл не оставался заблокированным (иначе его не удалить)
    private static BitmapImage LoadBitmap(string path)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        b.UriSource = new Uri(path);
        b.EndInit();
        b.Freeze();
        return b;
    }
}