using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Windows.Media.Control;
using AM_RPC.Models;

namespace AM_RPC.Services;

public sealed class MediaWatcher
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private CancellationTokenSource? _debounce;
    private readonly object _gate = new();
    private TrackInfo? _last;

    public event Action<TrackInfo?>? TrackInfoChanged;
    public event Action<TrackInfo>? TimelineChanged;

    public async Task StartAsync()
    {
        GlobalSystemMediaTransportControlsSessionManager? manager = null;
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        }
        catch
        {
            RaiseTrackChanged(null);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _manager = manager;
            _manager.CurrentSessionChanged += (_, _) => BindSession(_manager.GetCurrentSession());
            BindSession(_manager.GetCurrentSession());
        });
    }

    public void Stop()
    {
        _debounce?.Cancel();
        _debounce = null;

        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnSessionEvent;
            _session.PlaybackInfoChanged -= OnSessionEvent;
            _session.TimelinePropertiesChanged -= OnSessionEvent;
            _session = null;
        }

        if (_manager != null)
        {
            _manager.CurrentSessionChanged -= (_, _) => BindSession(_manager.GetCurrentSession());
            _manager = null;
        }
    }

    private void BindSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(session, _session))
            return;

        if (_session != null)
        {
            _session.MediaPropertiesChanged -= OnSessionEvent;
            _session.PlaybackInfoChanged -= OnSessionEvent;
            _session.TimelinePropertiesChanged -= OnSessionEvent;
        }

        _session = session;

        if (_session != null)
        {
            _session.MediaPropertiesChanged += OnSessionEvent;
            _session.PlaybackInfoChanged += OnSessionEvent;
            _session.TimelinePropertiesChanged += OnSessionEvent;
        }

        SchedulePublish();
    }

    private void OnSessionEvent(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        SchedulePublish();
    }

    private void SchedulePublish()
    {
        _debounce?.Cancel();
        var cts = new CancellationTokenSource();
        _debounce = cts;
        _ = PublishAsync(cts.Token);
    }

    private async Task PublishAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(120, token).ConfigureAwait(false);

            var session = _session;
            if (session == null)
            {
                RaiseTrackChanged(null);
                return;
            }

            var source = session.SourceAppUserModelId ?? "";
            var isApple = source.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) || source.Length == 0;
            if (!isApple)
            {
                RaiseTrackChanged(null);
                return;
            }

            var properties = await session.TryGetMediaPropertiesAsync();
            if (properties == null || (string.IsNullOrWhiteSpace(properties.Title) && string.IsNullOrWhiteSpace(properties.Artist)))
            {
                RaiseTrackChanged(null);
                return;
            }

            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            var position = timeline.Position < TimeSpan.Zero ? TimeSpan.Zero : timeline.Position;
            var duration = timeline.EndTime - timeline.StartTime;
            if (duration <= TimeSpan.Zero)
                duration = TimeSpan.Zero;

            var info = new TrackInfo
            {
                Title = properties.Title ?? "",
                Artist = properties.Artist ?? "",
                Album = properties.AlbumTitle ?? "",
                AlbumArtist = properties.AlbumArtist ?? "",
                IsPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                Position = position,
                Duration = duration,
                Thumbnail = properties.Thumbnail,
            };

            bool identityChanged;
            lock (_gate)
            {
                identityChanged = _last == null
                    || _last.Title != info.Title
                    || _last.Artist != info.Artist
                    || _last.Album != info.Album
                    || _last.IsPlaying != info.IsPlaying;
                _last = info;
            }

            if (identityChanged)
                TrackInfoChanged?.Invoke(info);
            else
                TimelineChanged?.Invoke(info);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void RaiseTrackChanged(TrackInfo? info)
    {
        bool changed;
        lock (_gate)
        {
            changed = !ReferenceEquals(_last, null) || info != null;
            _last = info;
        }

        if (changed)
            TrackInfoChanged?.Invoke(info);
    }
}