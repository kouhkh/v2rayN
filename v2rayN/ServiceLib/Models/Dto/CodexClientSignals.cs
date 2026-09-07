namespace ServiceLib.Models.Dto;

public sealed class CodexClientSignals
{
    public long WindowStartUnixMs { get; init; }
    public long WindowEndUnixMs { get; init; }
    public bool Available { get; init; }
    public string Status { get; init; } = "unavailable";
    public int RetryCount { get; init; }
    public int RequestTimeoutCount { get; init; }
    public int StreamDisconnectCount { get; init; }
    public int SendFailureCount { get; init; }
    public int HttpFallbackCount { get; init; }
    public int OutputItemCount { get; init; }
}
