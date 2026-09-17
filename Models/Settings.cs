namespace AM_RPC.Models;

public sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public bool ShowArtwork { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    public string DiscordAppId { get; set; } = "";
}