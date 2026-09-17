using System;
using System.Threading.Tasks;
using DiscordRPC;
using AM_RPC.Models;

namespace AM_RPC.Services;

public sealed class DiscordPresence : IDisposable
{
    private readonly object _gate = new();
    private DiscordRpcClient? _client;
    private string _clientId = "";

    public Task UpdateAsync(TrackInfo? track, string clientId, string? artworkUrl, bool showProgress)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return Task.CompletedTask;

            EnsureClient(clientId);

            if (track == null || !track.HasMedia)
            {
                _client?.ClearPresence();
                return Task.CompletedTask;
            }

            var presence = new RichPresence
            {
                Details = track.Title,
                State = BuildState(track),
            };

            if (showProgress && track.IsPlaying && track.Duration > TimeSpan.Zero)
                presence.Timestamps = new Timestamps(DateTime.UtcNow - track.Position);

            if (!string.IsNullOrEmpty(artworkUrl))
                presence.Assets = new Assets
                {
                    LargeImageKey = artworkUrl,
                    LargeImageText = track.Album,
                };

            _client?.SetPresence(presence);
            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _client?.Dispose();
            _client = null;
            _clientId = "";
        }
    }

    private void EnsureClient(string clientId)
    {
        if (_client != null && !string.Equals(_clientId, clientId, StringComparison.Ordinal))
        {
            _client.Dispose();
            _client = null;
        }

        if (_client == null)
        {
            _clientId = clientId;
            var client = new DiscordRpcClient(clientId);
            try
            {
                client.Initialize();
                _client = client;
            }
            catch
            {
                client.Dispose();
                _client = null;
            }
        }
    }

    private static string BuildState(TrackInfo track)
    {
        if (!string.IsNullOrEmpty(track.Album))
            return track.Artist.Length > 0 ? $"{track.Artist} — {track.Album}" : track.Album;
        return track.Artist;
    }
}