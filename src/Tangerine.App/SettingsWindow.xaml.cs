using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Tangerine.App.Settings;
using Tangerine.Core.Media;

namespace Tangerine.App;

public partial class SettingsWindow : Window
{
    private static readonly (TriggerKey Key, string Label)[] Keys =
    [
        (TriggerKey.Shift, "Shift"),
        (TriggerKey.Control, "Ctrl"),
        (TriggerKey.Alt, "Alt"),
        (TriggerKey.CapsLock, "Caps Lock"),
        (TriggerKey.None, "Off"),
    ];

    private static readonly int[] DpiChoices = [150, 200, 300, 600];

    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings, string officeStatus)
    {
        InitializeComponent();
        _settings = settings.Clone();

        foreach (var (_, label) in Keys)
        {
            ConvertKeyBox.Items.Add(label);
            ActionsKeyBox.Items.Add(label);
        }
        ConvertKeyBox.SelectedIndex = Array.FindIndex(Keys, k => k.Key == _settings.ConvertKey);
        ActionsKeyBox.SelectedIndex = Array.FindIndex(Keys, k => k.Key == _settings.ActionsKey);

        NextToOriginal.IsChecked = !_settings.UseOutputFolder;
        ToFolder.IsChecked = _settings.UseOutputFolder;
        OutputFolderBox.Text = _settings.OutputFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        RevealBox.IsChecked = _settings.RevealResults;

        QualitySlider.Value = _settings.ImageQuality;
        CompressSlider.Value = _settings.CompressQuality;
        foreach (var dpi in DpiChoices) DpiBox.Items.Add($"{dpi} DPI");
        DpiBox.SelectedIndex = Math.Max(0, Array.IndexOf(DpiChoices, _settings.PdfDpi));

        LaunchBox.IsChecked = _settings.LaunchAtLogin;
        PausedBox.IsChecked = _settings.Paused;
        OfficeStatus.Text = officeStatus;
        RefreshFfmpeg();
        Activated += (_, _) => RefreshFfmpeg();
    }

    public event Action<AppSettings>? Saved;

    public event Action? QuitRequested;

    private void RefreshFfmpeg()
    {
        var found = Ffmpeg.TryLocate(_settings.ToConverterSettings());
        FfmpegStatus.Text = found is null
            ? "Not found. Audio and video conversions need FFmpeg; install it with one click (winget)."
            : $"Found at {found}";
        InstallFfmpegButton.Visibility = found is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnKeysChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConvertKeyBox.SelectedIndex < 0 || ActionsKeyBox.SelectedIndex < 0) return;
        var convert = Keys[ConvertKeyBox.SelectedIndex].Key;
        var actions = Keys[ActionsKeyBox.SelectedIndex].Key;
        var clash = convert == actions && convert != TriggerKey.None;
        KeyWarning.Visibility = clash ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = !clash;
        ConvertKeyRun.Text = Keys[ConvertKeyBox.SelectedIndex].Label;
        ActionsKeyRun.Text = Keys[ActionsKeyBox.SelectedIndex].Label;
    }

    private void OnBrowseOutput(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Save converted files to…" };
        if (Directory.Exists(OutputFolderBox.Text)) dialog.InitialDirectory = OutputFolderBox.Text;
        if (dialog.ShowDialog(this) == true)
        {
            OutputFolderBox.Text = dialog.FolderName;
            ToFolder.IsChecked = true;
        }
    }

    private void OnInstallFfmpeg(object sender, RoutedEventArgs e) => Shell.InstallFfmpeg();

    private void OnLocateFfmpeg(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Locate ffmpeg.exe", Filter = "ffmpeg.exe|ffmpeg.exe|Programs|*.exe" };
        if (dialog.ShowDialog(this) == true)
        {
            _settings.FfmpegPath = dialog.FileName;
            RefreshFfmpeg();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _settings.ConvertKey = Keys[ConvertKeyBox.SelectedIndex].Key;
        _settings.ActionsKey = Keys[ActionsKeyBox.SelectedIndex].Key;
        _settings.UseOutputFolder = ToFolder.IsChecked == true && !string.IsNullOrWhiteSpace(OutputFolderBox.Text);
        _settings.OutputFolder = OutputFolderBox.Text.Trim();
        _settings.RevealResults = RevealBox.IsChecked == true;
        _settings.ImageQuality = (int)QualitySlider.Value;
        _settings.CompressQuality = (int)CompressSlider.Value;
        _settings.PdfDpi = DpiChoices[Math.Max(0, DpiBox.SelectedIndex)];
        _settings.LaunchAtLogin = LaunchBox.IsChecked == true;
        _settings.Paused = PausedBox.IsChecked == true;
        _settings.HasCompletedWelcome = true;
        Saved?.Invoke(_settings);
        Close();
    }

    private void OnQuit(object sender, RoutedEventArgs e) => QuitRequested?.Invoke();
}
