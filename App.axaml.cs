using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Windows.Storage.Streams;
using AM_RPC.Models;
using AM_RPC.Services;

namespace AM_RPC;

public partial class App : Application
{
    private readonly Settings _settings = SettingsStore.Load();
    private MediaWatcher _watcher = null!;
    private DiscordPresence _discord = null!;
    private ArtworkResolver _artworks = null!;
    private MainWindow _mainWindow = null!;
    private TrackInfo? _currentTrack;
    private bool _exiting;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _mainWindow = new MainWindow();
            desktop.Exit += OnExit;
            _mainWindow.Closing += OnMainWindowClosing;
            _mainWindow.SettingsRequested += OnSettingsRequested;

            _discord = new DiscordPresence();
            _artworks = new ArtworkResolver();
            _watcher = new MediaWatcher();
            _watcher.TrackInfoChanged += OnTrackInfoChanged;
            _watcher.TimelineChanged += OnTimelineChanged;

            SetupTray();
            ApplyCurrentState();
            _ = _watcher.StartAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTrackInfoChanged(TrackInfo? info)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _currentTrack = info;
            _mainWindow.SetTrack(info);
            UpdateArtwork(info);
            ApplyCurrentState();
            _ = PushPresenceAsync(info);
        });
    }

    private void OnTimelineChanged(TrackInfo info)
    {
        Dispatcher.UIThread.Post(() => _mainWindow.SetProgress(info.Position, info.Duration));
    }

    private void UpdateArtwork(TrackInfo? info)
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                if (info?.Thumbnail is not IRandomAccessStreamReference reference)
                {
                    _mainWindow.SetArtwork(null);
                    return;
                }

                var source = await reference.OpenReadAsync();
                var length = (uint)source.Size;
                using var reader = new DataReader(source);
                await reader.LoadAsync(length);
                var bytes = new byte[length];
                if (length > 0)
                    reader.ReadBytes(bytes);
                using var stream = new MemoryStream(bytes);
                _mainWindow.SetArtwork(new Bitmap(stream));
            }
            catch
            {
                _mainWindow.SetArtwork(null);
            }
        });
    }

    private async Task PushPresenceAsync(TrackInfo? track)
    {
        try
        {
            if (!_settings.Enabled || string.IsNullOrWhiteSpace(_settings.DiscordAppId))
                return;

            string? artwork = null;
            if (track != null && _settings.ShowArtwork)
                artwork = await _artworks.ResolveAsync(track.Title, track.Artist).ConfigureAwait(false);

            await _discord.UpdateAsync(track, _settings.DiscordAppId, artwork, _settings.ShowProgress).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async void OnSettingsRequested()
    {
        var dialog = new SettingsWindow(_settings);
        await dialog.ShowDialog(_mainWindow);

        ApplyCurrentState();

        if (!_settings.Enabled)
        {
            _discord.Dispose();
            return;
        }

        await PushPresenceAsync(_currentTrack).ConfigureAwait(false);
    }

    private void ApplyCurrentState()
    {
        if (!_settings.Enabled)
        {
            _mainWindow.SetStatus("Rich presence disabled");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.DiscordAppId))
        {
            _mainWindow.SetStatus("Add a Discord Application ID in Settings");
            return;
        }

        if (_currentTrack == null || !_currentTrack.HasMedia)
        {
            _mainWindow.SetStatus("Waiting for Apple Music...");
            return;
        }

        _mainWindow.SetStatus(_currentTrack.IsPlaying
            ? $"Tracking \"{_currentTrack.Title}\""
            : $"Paused on \"{_currentTrack.Title}\"");
    }

    private void SetupTray()
    {
        var menu = new NativeMenu();

        var showItem = new NativeMenuItem("Show Apple Music RPC");
        showItem.Click += (_, _) => ShowMainWindow();
        menu.Add(showItem);

        var settingsItem = new NativeMenuItem("Settings");
        settingsItem.Click += (_, _) => OnSettingsRequested();
        menu.Add(settingsItem);

        menu.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        menu.Add(exitItem);

        var tray = new TrayIcon
        {
            Icon = new WindowIcon("Assets/icon.ico"),
            ToolTipText = "Apple Music RPC",
            Menu = menu,
            IsVisible = true,
        };
        tray.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(this, new TrayIcons { tray });
    }

    // private static WindowIcon CreateTrayIcon()
    // {
    //     const int size = 64;
    //     using var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
    //     using (var context = bitmap.CreateDrawingContext())
    //     {
    //         var brush = new LinearGradientBrush
    //         {
    //             StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
    //             EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
    //             GradientStops =
    //             {
    //                 new GradientStop(Color.Parse("#FF4D5A"), 0),
    //                 new GradientStop(Color.Parse("#FA243C"), 1),
    //             },
    //         };

    //         context.DrawRectangle(brush, null, new RoundedRect(new Rect(0, 0, size, size), size * 0.24));

    //         var text = new FormattedText(
    //             "\u266A",
    //             CultureInfo.InvariantCulture,
    //             FlowDirection.LeftToRight,
    //             new Typeface("Segoe UI Symbol"),
    //             size * 0.62,
    //             new SolidColorBrush(Colors.White));

    //         context.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2 - 2));
    //     }

    //     return new WindowIcon(bitmap);
    // }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
            return;

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_exiting)
        {
            e.Cancel = true;
            _mainWindow?.Hide();
        }
    }

    private void ExitApplication()
    {
        _exiting = true;
        _watcher?.Stop();
        _discord.Dispose();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _watcher?.Stop();
        _discord.Dispose();
    }
}