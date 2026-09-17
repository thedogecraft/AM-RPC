using Avalonia.Controls;
using AM_RPC.Models;
using AM_RPC.Services;

namespace AM_RPC;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;

    public SettingsWindow()
        : this(new Settings())
    {
    }

    public SettingsWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        EnabledCheck.IsChecked = _settings.Enabled;
        ArtworkCheck.IsChecked = _settings.ShowArtwork;
        ProgressCheck.IsChecked = _settings.ShowProgress;
        AppIdBox.Text = _settings.DiscordAppId;
    }

    private void OnSaveClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _settings.Enabled = EnabledCheck.IsChecked == true;
        _settings.ShowArtwork = ArtworkCheck.IsChecked == true;
        _settings.ShowProgress = ProgressCheck.IsChecked == true;
        _settings.DiscordAppId = AppIdBox.Text?.Trim() ?? "";
        SettingsStore.Save(_settings);
        Close();
    }

    private void OnCancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}