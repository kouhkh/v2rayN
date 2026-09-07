namespace ServiceLib.Models.Dto;

public sealed class CodexObservedContext
{
    public string ProfileIndexId { get; init; } = string.Empty;
    public string NetworkFingerprint { get; init; } = string.Empty;
    public long StableSinceUnixMs { get; init; }
}
