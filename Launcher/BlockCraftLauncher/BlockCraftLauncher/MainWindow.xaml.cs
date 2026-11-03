using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BlockCraftLauncher.Models;
using BlockCraftLauncher.Services;

namespace BlockCraftLauncher;

public partial class MainWindow : Window
{
    private readonly GitHubService _github = new();
    private readonly GameInstaller _installer = new();
    private List<GameVersion> _versions = new();
    private ICollectionView? _versionsView;
    private bool _busy;

    private enum Page { Home, Library, Skins, Update }
    private Page _page = Page.Home;

    private static readonly Brush NavActiveBg = new SolidColorBrush(Color.FromRgb(0x48, 0x26, 0x6A));
    private static readonly Brush NavIdleFg = new SolidColorBrush(Color.FromRgb(0xB8, 0xA9, 0xCC));

    private static void SetNav(Button b, bool active)
    {
        b.Background = active ? NavActiveBg : Brushes.Transparent;
        b.Foreground = active ? Brushes.White : NavIdleFg;
    }

    private GameVersion? Selected => VersionsList.SelectedItem as GameVersion;

    private LauncherUpdateInfo? _update;
    private bool _updating;

    public MainWindow()
    {
        InitializeComponent();
        VersionLabel.Text = "Клиент " + LauncherUpdater.CurrentText;
        NickBox.Text = LauncherSettings.Current.Nickname;
        Loaded += async (_, _) =>
        {
            var glowAnimation = new DoubleAnimation(0.16, 0.34, TimeSpan.FromSeconds(3.8))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            GlowOrb.BeginAnimation(UIElement.OpacityProperty, glowAnimation);
            await LoadVersionsAsync();
            LoadSkins();
            AnimatePage(HomePage);

            if (await CheckLauncherUpdateAsync() && _update != null)
            {
                var ask = MessageBox.Show(
                    $"Доступна новая версия лаунчера: {_update.Tag}\nОбновить сейчас?",
                    "Обновление лаунчера", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (ask == MessageBoxResult.Yes) await ApplyLauncherUpdateAsync();
            }
        };
        Closing += (_, _) => SaveNick();
    }

    private void SaveNick()
    {
        LauncherSettings.Current.Nickname = NickBox.Text.Trim();
        LauncherSettings.Current.Save();
    }

    private async Task LoadVersionsAsync()
    {
        StatusLabel.Text = "Загружаем каталог версий...";
        HeaderStatus.Text = "СИНХРОНИЗАЦИЯ";
        List<GameVersion> versions;
        try
        {
            versions = await _github.GetVersionsAsync();
            StatusLabel.Text = versions.Count == 0 ? "В репозитории пока нет сборок." : $"Каталог обновлён · {versions.Count} сборок";
        }
        catch
        {
            StatusLabel.Text = "Нет связи с сервером. Показаны установленные сборки.";
            versions = Directory.Exists(Config.VersionsDir)
                ? Directory.GetDirectories(Config.VersionsDir)
                    .Select(d => Path.GetFileName(d))
                    .Where(n => !n.EndsWith("_tmp", StringComparison.OrdinalIgnoreCase))
                    .Select(n => new GameVersion { Tag = n, Name = n, IsInstalled = true })
                    .ToList()
                : new List<GameVersion>();
        }

        _versions = versions;
        VersionsList.ItemsSource = _versions;
        _versionsView = CollectionViewSource.GetDefaultView(VersionsList.ItemsSource);
        ApplyLibraryFilter();
        VersionsList.SelectedItem = _versions.FirstOrDefault(v => v.Tag == LauncherSettings.Current.LastVersion)
                                    ?? _versions.FirstOrDefault();
        VersionCount.Text = $"{_versions.Count} сборок";
        HeaderStatus.Text = "ГОТОВ К РАБОТЕ";
        UpdateUi();
    }

    private void UpdateUi()
    {
        var v = Selected;
        if (v == null)
        {
            SelectedInfo.Text = "Сначала выбери сборку в библиотеке";
            PlayBtn.IsEnabled = false;
            DeleteBtn.IsEnabled = false;
            FolderBtn.IsEnabled = false;
            ControlTitle.Text = "Сборка не выбрана";
            ControlDescription.Text = "Открой библиотеку и выбери сборку для продолжения.";
            return;
        }

        SelectedInfo.Text = $"{v.Name}  ·  {(v.IsInstalled ? "УСТАНОВЛЕНА" : "ДОСТУПНА")}";
        PlayBtn.Content = v.IsInstalled ? "▶    ИГРАТЬ" : "↓    СКАЧАТЬ И ИГРАТЬ";
        PlayBtn.IsEnabled = !_busy;
        DeleteBtn.IsEnabled = v.IsInstalled && !_busy;
        FolderBtn.IsEnabled = !_busy;
        ControlTitle.Text = v.IsInstalled ? "Всё готово к игре" : "Сборка ещё не установлена";
        ControlDescription.Text = v.IsInstalled ? "Твоя сборка готова. Запускай BlockCraft!" : "Нажми «Скачать и играть», чтобы установить сборку.";
    }

    private void ApplyLibraryFilter()
    {
        if (_versionsView == null) return;
        string query = SearchBox?.Text?.Trim() ?? "";
        int filter = FilterBox?.SelectedIndex ?? 0;
        _versionsView.Filter = item =>
        {
            if (item is not GameVersion version) return false;
            bool matchesText = string.IsNullOrWhiteSpace(query)
                || version.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || version.Tag.Contains(query, StringComparison.OrdinalIgnoreCase);
            bool matchesFilter = filter == 0 || (filter == 1 && version.IsInstalled) || (filter == 2 && !version.IsInstalled);
            return matchesText && matchesFilter;
        };
        _versionsView.Refresh();
        VersionCount.Text = $"{_versionsView.Cast<object>().Count()} из {_versions.Count} сборок";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyLibraryFilter();
    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyLibraryFilter();
    private void VersionsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateUi();

    private void ShowPage(Page p)
    {
        if (_page == p) return;
        _page = p;
        HomePage.Visibility = p == Page.Home ? Visibility.Visible : Visibility.Collapsed;
        LibraryPage.Visibility = p == Page.Library ? Visibility.Visible : Visibility.Collapsed;
        SkinsPage.Visibility = p == Page.Skins ? Visibility.Visible : Visibility.Collapsed;
        UpdatePage.Visibility = p == Page.Update ? Visibility.Visible : Visibility.Collapsed;

        SetNav(HomeNav, p == Page.Home);
        SetNav(LibraryNav, p == Page.Library);
        SetNav(SkinsNav, p == Page.Skins);
        SetNav(UpdateNav, p == Page.Update);

        AnimatePage(p switch { Page.Library => LibraryPage, Page.Skins => SkinsPage, Page.Update => UpdatePage, _ => HomePage });
    }

    private void AnimatePage(FrameworkElement page)
    {
        page.Opacity = 0;
        if (page.RenderTransform is System.Windows.Media.TranslateTransform translate) translate.Y = 12;
        if (FindResource("PageIn") is Storyboard storyboard) storyboard.Begin(page, true);
    }

    private void HomeNav_Click(object sender, RoutedEventArgs e) => ShowPage(Page.Home);
    private void LibraryNav_Click(object sender, RoutedEventArgs e) => ShowPage(Page.Library);
    private void SkinsNav_Click(object sender, RoutedEventArgs e) => ShowPage(Page.Skins);
    private void UpdateNav_Click(object sender, RoutedEventArgs e) => ShowPage(Page.Update);

    private async void UpdateCheck_Click(object sender, RoutedEventArgs e) => await CheckLauncherUpdateAsync();
    private async void UpdateApply_Click(object sender, RoutedEventArgs e) => await ApplyLauncherUpdateAsync();

    private async Task<bool> CheckLauncherUpdateAsync()
    {
        UpdateCheckBtn.IsEnabled = false;
        UpdateState.Text = "Проверяем обновления...";
        UpdateCurrent.Text = LauncherUpdater.CurrentText;
        try { _update = await LauncherUpdater.CheckAsync(); }
        catch (Exception ex)
        {
            _update = null;
            UpdateState.Text = "Не удалось проверить обновления: " + ex.Message;
            UpdateCheckBtn.IsEnabled = true;
        }
        bool newer = _update != null && LauncherUpdater.IsNewer(_update);
        UpdateLatest.Text = _update?.Tag ?? "—";
        UpdateNotes.Text = _update == null ? "" : (string.IsNullOrWhiteSpace(_update.Notes) ? "Описание не указано." : _update.Notes);
        UpdateApplyBtn.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
        UpdateDot.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
        UpdateState.Text = _update == null ? "В репозитории лаунчера нет подходящего релиза (нужен .zip)."
            : newer ? $"Доступна версия {_update.Tag} · {_update.SizeText}"
            : "Установлена последняя версия.";
        return newer;


    }

    private async Task ApplyLauncherUpdateAsync()
    {
        if (_update == null || _updating || _busy) return;
        _updating = true; _busy = true;
        UpdateApplyBtn.IsEnabled = false;
        UpdateCheckBtn.IsEnabled = false;
        HeaderStatus.Text = "ОБНОВЛЕНИЕ ЛАУНЧЕРА";
        try
        {
            var progress = new Progress<double>(p =>
            {
                UpdateBar.Value = p;
                UpdateState.Text = p >= 100 ? "Подготавливаем установку..." : $"Загрузка обновления · {p:F0}%";
            });
            await LauncherUpdater.ApplyAsync(_update, progress);
            Application.Current.Shutdown();      // скрипт заменит файлы и сам перезапустит лаунчер
        }
        catch (Exception ex)
        {
            _updating = false; _busy = false;
            UpdateApplyBtn.IsEnabled = true;
            UpdateCheckBtn.IsEnabled = true;
            UpdateBar.Value = 0;
            HeaderStatus.Text = "ГОТОВ К РАБОТЕ";
            UpdateState.Text = "Не удалось обновить лаунчер.";
            MessageBox.Show(ex.Message, "Ошибка обновления", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadSkins(string? select = null)
    {
        string active = LauncherSettings.Current.ActiveSkin;
        var list = SkinService.LoadAll(active);
        SkinsList.ItemsSource = list;
        SkinsList.SelectedItem = list.FirstOrDefault(s => s.FileName == (select ?? active));
        SkinsEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSkinPanel();
    }

    private void UpdateSkinPanel()
    {
        var s = SkinsList.SelectedItem as SkinItem;
        SkinPreviewPanel.Visibility = s == null ? Visibility.Collapsed : Visibility.Visible;
        if (s == null) return;
        SkinName.Text = s.Name;
        SkinFull.Source = s.Full;
        SkinMeta.Text = s.SizeText;
        SkinApplyBtn.IsEnabled = !s.IsActive;
        SkinApplyBtn.Content = s.IsActive ? "✓  ИСПОЛЬЗУЕТСЯ" : "ИСПОЛЬЗОВАТЬ";
    }

    private void SkinsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSkinPanel();

    private void SkinUpload_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "PNG-скин (*.png)|*.png", Multiselect = true };
        if (dlg.ShowDialog() == true) ImportSkins(dlg.FileNames);
    }

    private void SkinsPage_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) ImportSkins(files);
    }

    private void ImportSkins(IEnumerable<string> files)
    {
        var errors = new List<string>();
        string? last = null;
        foreach (var f in files)
        {
            string? err = SkinService.Import(f, out string name);
            if (err != null) errors.Add($"{Path.GetFileName(f)}: {err}");
            else last = name;
        }
        LoadSkins(last);
        if (last != null) StatusLabel.Text = "Скин добавлен";
        if (errors.Count > 0)
            MessageBox.Show(string.Join("\n", errors), "Скин не загружен", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void SkinApply_Click(object sender, RoutedEventArgs e)
    {
        if (SkinsList.SelectedItem is not SkinItem s) return;
        LauncherSettings.Current.ActiveSkin = s.FileName;
        LauncherSettings.Current.Save();
        LoadSkins(s.FileName);
        StatusLabel.Text = $"Скин «{s.Name}» выбран";
    }

    private void SkinDelete_Click(object sender, RoutedEventArgs e)
    {
        if (SkinsList.SelectedItem is not SkinItem s) return;
        if (MessageBox.Show($"Удалить скин «{s.Name}»?", "Удаление скина",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { SkinService.Delete(s.FileName); LoadSkins(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка"); }
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy) await LoadVersionsAsync();
    }

    private async Task InstallAsync(GameVersion v)
    {
        _busy = true;
        v.IsBusy = true;
        v.Progress = 0;
        HeaderStatus.Text = "ЗАГРУЗКА СБОРКИ";
        UpdateUi();
        ControlTitle.Text = $"Загружаем {v.Name}...";
        try
        {
            var progress = new Progress<double>(p =>
            {
                v.Progress = p;
                Bar.Value = p;
                StatusLabel.Text = p >= 100 ? "Распаковка файлов..." : $"Загрузка файлов · {p:F0}%";
                ControlDescription.Text = p >= 100 ? "Распаковываем файлы игры" : $"Загрузка сборки · {p:F0}%";
            });
            await _installer.InstallAsync(v, progress);
        }
        finally
        {
            v.IsBusy = false;
            v.Progress = 0;
            _busy = false;
            Bar.Value = 0;
            HeaderStatus.Text = "ГОТОВ К РАБОТЕ";
            UpdateUi();
            ApplyLibraryFilter();
        }
    }

    private async void PlayBtn_Click(object sender, RoutedEventArgs e)
    {
        var v = Selected;
        if (v == null || _busy) return;
        string nick = NickBox.Text.Trim();
        if (!Regex.IsMatch(nick, "^[A-Za-z0-9_]{3,16}$"))
        {
            MessageBox.Show("Ник должен быть от 3 до 16 символов: латинские буквы, цифры и _", "Неверный ник", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LauncherSettings.Current.LastVersion = v.Tag;
        SaveNick();
        _busy = true;
        HeaderStatus.Text = "ПОДГОТОВКА ИГРЫ";
        UpdateUi();
        try
        {
            if (!v.IsInstalled) await InstallAsync(v);

            StatusLabel.Text = "Игра запущена";
            _installer.Launch(v, nick);
            if (LauncherSettings.Current.CloseOnLaunch) Close();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Произошла ошибка";
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _busy = false;
            Bar.Value = 0;
            HeaderStatus.Text = "ГОТОВ К РАБОТЕ";
            UpdateUi();
            ApplyLibraryFilter();
        }
    }

    private async void CardAction_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || (sender as FrameworkElement)?.DataContext is not GameVersion v) return;
        VersionsList.SelectedItem = v;
        if (v.IsInstalled) { PlayBtn_Click(PlayBtn, e); return; }
        try { await InstallAsync(v); StatusLabel.Text = $"{v.Name} установлена"; }
        catch (Exception ex)
        {
            StatusLabel.Text = "Произошла ошибка";
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CardDelete_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy && (sender as FrameworkElement)?.DataContext is GameVersion v) DeleteVersion(v);
    }

    private void FolderBtn_Click(object sender, RoutedEventArgs e)
    {
        string path = Selected is { IsInstalled: true } v ? Path.Combine(Config.VersionsDir, v.Tag) : Config.RootDir;
        Directory.CreateDirectory(path);
        Process.Start("explorer.exe", $"\"{path}\"");
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { IsInstalled: true } v) DeleteVersion(v);
    }

    private void DeleteVersion(GameVersion v)
    {
        if (!v.IsInstalled) return;
        var answer = MessageBox.Show($"Удалить сборку {v.Name} с компьютера?", "Удаление сборки",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            _installer.Uninstall(v);
            StatusLabel.Text = "Сборка удалена";
            UpdateUi();
            ApplyLibraryFilter();
        }
        catch (Exception ex) { MessageBox.Show("Не удалось удалить сборку (возможно, игра запущена): " + ex.Message); }
    }

    private async void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this };
        if (win.ShowDialog() == true && !_busy) await LoadVersionsAsync();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}