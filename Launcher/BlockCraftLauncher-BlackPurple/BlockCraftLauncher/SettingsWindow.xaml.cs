using System.Windows;
using System.Windows.Input;
using BlockCraftLauncher.Services;

namespace BlockCraftLauncher;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DirBox.Text = Config.RootDir;
        CloseCheck.IsChecked = LauncherSettings.Current.CloseOnLaunch;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Выберите папку для игры" };
        if (dlg.ShowDialog() == true) DirBox.Text = dlg.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = LauncherSettings.Current;
        string dir = DirBox.Text.Trim();
        settings.InstallDir = string.Equals(dir, Config.DefaultRootDir, StringComparison.OrdinalIgnoreCase) ? "" : dir;
        settings.CloseOnLaunch = CloseCheck.IsChecked == true;
        settings.Save();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
