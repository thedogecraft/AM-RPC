using System;
using Avalonia.Controls;
using Avalonia.Media;
using AM_RPC.Models;

namespace AM_RPC;

public partial class MainWindow : Window
{
    public event Action? SettingsRequested;

    public MainWindow()
    {
        InitializeComponent();
    }

    public void SetTrack(TrackInfo? info)
    {
        if (info == null || !info.HasMedia)
        {
            TitleText.Text = "Nothing playing";
            ArtistText.Text = "";
            AlbumText.Text = "";
            ProgressBar.Value = 0;
            TimeText.Text = "";
            return;
        }

        TitleText.Text = info.Title;
        ArtistText.Text = info.Artist;
        AlbumText.Text = info.Album;
        SetProgress(info.Position, info.Duration);
    }

    public void SetProgress(TimeSpan position, TimeSpan duration)
    {
        var max = duration.TotalSeconds;
        ProgressBar.Maximum = max > 0 ? max : 1;
        ProgressBar.Value = Math.Clamp(position.TotalSeconds, 0, max > 0 ? max : 1);
        TimeText.Text = $"{position:mm\\:ss} / {duration:mm\\:ss}";
    }

    public void SetArtwork(IImage? image)
    {
        if (image == null)
        {
            ArtworkImage.IsVisible = false;
            ArtworkImage.Source = null;
            ArtworkPlaceholder.IsVisible = true;
            return;
        }

        ArtworkPlaceholder.IsVisible = false;
        ArtworkImage.Source = image;
        ArtworkImage.IsVisible = true;
    }

    public void SetStatus(string text)
    {
        StatusText.Text = text;
    }

    private void OnSettingsClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SettingsRequested?.Invoke();
    }
}