using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AM_RPC.Models;

namespace AM_RPC.Services;

public sealed class DiscordRpc : IDisposable
{
    private const int OpHandshake = 0;
    private const int OpFrame = 1;
    private const int OpClose = 2;
    private const int OpPing = 3;
    private const int OpPong = 4;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _cts;
    private string _clientId = "";
    private long _nonce;
    private bool _disposed;

    public Task UpdateAsync(TrackInfo? track, string clientId, string? artworkUrl, bool showProgress)
        => UpdateCoreAsync(track, clientId, artworkUrl, showProgress);

    private async Task UpdateCoreAsync(TrackInfo? track, string clientId, string? artworkUrl, bool showProgress)
    {
        if (_disposed || string.IsNullOrWhiteSpace(clientId))
            return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_pipe == null || !string.Equals(_clientId, clientId, StringComparison.Ordinal))
            {
                Disconnect();
                _clientId = clientId;
                if (!await TryConnectAsync().ConfigureAwait(false))
                    return;
            }

            var pipe = _pipe;
            if (pipe == null)
                return;

            var activity = BuildActivity(track, artworkUrl, showProgress);
            var payload = JsonSerializer.Serialize(new
            {
                cmd = "SET_ACTIVITY",
                nonce = NextNonce(),
                args = new { pid = Environment.ProcessId, activity },
            });

            await WriteFrameAsync(pipe, OpFrame, payload).ConfigureAwait(false);
        }
        catch
        {
            Disconnect();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cts?.Cancel();
        _gate.Wait(1000);
        Disconnect();
        _gate.Dispose();
    }

    private async Task<bool> TryConnectAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut);
                pipe.Connect(1000);

                _cts = new CancellationTokenSource();
                var handshake = JsonSerializer.Serialize(new { v = 1, client_id = _clientId });
                await WriteFrameAsync(pipe, OpHandshake, handshake).ConfigureAwait(false);

                var readTask = ReadFrameAsync(pipe, _cts.Token);
                var completed = await Task.WhenAny(readTask, Task.Delay(2000)).ConfigureAwait(false);
                if (completed != readTask)
                    throw new TimeoutException();
                var (opcode, _) = await readTask.ConfigureAwait(false);
                if (opcode == OpClose)
                {
                    pipe.Dispose();
                    _pipe = null;
                    return false;
                }

                if (opcode == OpFrame)
                {
                    _pipe = pipe;
                    _ = Task.Run(() => ReadLoopAsync(pipe, _cts.Token));
                    return true;
                }

                pipe.Dispose();
                _pipe = null;
            }
            catch
            {
                try
                {
                    pipe?.Dispose();
                }
                catch
                {
                }

                _pipe = null;
            }
        }

        return false;
    }

    private async Task ReadLoopAsync(NamedPipeClientStream pipe, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var (opcode, payload) = await ReadFrameAsync(pipe, token).ConfigureAwait(false);
                if (opcode == OpClose)
                    break;

                if (opcode == OpPing && await _gate.WaitAsync(1000).ConfigureAwait(false))
                {
                    try
                    {
                        await WriteFrameAsync(pipe, OpPong, payload).ConfigureAwait(false);
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }
            }
        }
        catch
        {
        }
        finally
        {
            Disconnect();
        }
    }

    private static async Task WriteFrameAsync(Stream stream, int opcode, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var header = new byte[8];
        WriteInt(header, 0, opcode);
        WriteInt(header, 4, bytes.Length);

        await stream.WriteAsync(header.AsMemory()).ConfigureAwait(false);
        await stream.WriteAsync(bytes).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<(int Opcode, string Payload)> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);

        var opcode = ReadInt(header, 0);
        var length = ReadInt(header, 4);
        if (length < 0 || length > 1 << 20)
            throw new EndOfStreamException();

        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, token).ConfigureAwait(false);
        return (opcode, Encoding.UTF8.GetString(buffer));
    }

    private static object? BuildActivity(TrackInfo? track, string? artworkUrl, bool showProgress)
    {
        if (track == null || !track.HasMedia)
            return null;

        var activity = new Dictionary<string, object>
        {
            ["details"] = track.Title,
            ["instance"] = false,
        };

        var state = track.Artist;
        if (!string.IsNullOrEmpty(track.Album))
            state = state.Length > 0 ? $"{state} — {track.Album}" : track.Album;
        activity["state"] = state;

        if (showProgress && track.IsPlaying && track.Duration > TimeSpan.Zero)
            activity["timestamps"] = new { start = (DateTimeOffset.UtcNow - track.Position).ToUnixTimeSeconds() };

        if (!string.IsNullOrEmpty(artworkUrl))
            activity["assets"] = new { large_image = artworkUrl, large_text = track.Album };

        return activity;
    }

    private string NextNonce() => Interlocked.Increment(ref _nonce).ToString();

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static int ReadInt(byte[] buffer, int offset)
        => buffer[offset]
           | (buffer[offset + 1] << 8)
           | (buffer[offset + 2] << 16)
           | (buffer[offset + 3] << 24);

    private void Disconnect()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        try
        {
            _pipe?.Dispose();
        }
        catch
        {
        }

        _pipe = null;
    }
}