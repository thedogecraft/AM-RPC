using System;

namespace AM_RPC.Models;

public sealed class TrackInfo
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";
    public string AlbumArtist { get; init; } = "";
    public bool IsPlaying { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    public object? Thumbnail { get; init; }

    public bool HasMedia => Title.Length > 0 || Artist.Length > 0 || Album.Length > 0;
}