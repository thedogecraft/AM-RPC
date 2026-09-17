using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AM_RPC.Services;

public sealed class ArtworkResolver
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, string> _cache = new();

    public async Task<string?> ResolveAsync(string title, string artist)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var key = $"{artist}\u001F{title}";
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(key, out var cached))
                return cached.Length > 0 ? cached : null;

            try
            {
                var term = Uri.EscapeDataString($"{artist} {title}".Trim());
                using var response = await Http
                    .GetAsync($"https://itunes.apple.com/search?term={term}&media=music&entity=song&limit=1")
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    if (document.RootElement.TryGetProperty("results", out var results)
                        && results.ValueKind == JsonValueKind.Array
                        && results.GetArrayLength() > 0
                        && results[0].TryGetProperty("artworkUrl100", out var artwork)
                        && artwork.GetString() is { Length: > 0 } url)
                    {
                        url = url.Replace("100x100", "512x512");
                        _cache[key] = url;
                        return url;
                    }
                }
            }
            catch
            {
            }

            _cache[key] = "";
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}