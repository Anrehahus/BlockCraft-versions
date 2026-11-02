using System.IO;
using System.Windows.Media.Imaging;

namespace BlockCraftLauncher.Models;

public class SkinItem
{
    public string FileName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string Name => Path.GetFileNameWithoutExtension(FileName);
    public bool IsActive { get; init; }
    public string SizeText { get; init; } = "";
    public BitmapSource Full { get; init; } = null!;
    public BitmapSource Head { get; init; } = null!;
    public BitmapSource Hat { get; init; } = null!;
}