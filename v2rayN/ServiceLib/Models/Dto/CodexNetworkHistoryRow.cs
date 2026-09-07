namespace ServiceLib.Models.Dto;

public sealed class CodexNetworkHistoryRow
{
    public string Time { get; init; }
    public string Node { get; init; }
    public string Network { get; init; }
    public string NetworkKind { get; init; }
    public string Mode { get; init; }
    public string Success { get; init; }
    public int MedianMs { get; init; }
    public int P90Ms { get; init; }
    public string HttpStatus { get; init; }
    public string CodexSignals { get; init; }
    public string Error { get; init; }
}
