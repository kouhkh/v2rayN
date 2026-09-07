namespace ServiceLib.Models.Dto;

public sealed record CodexConnectivityResult(
    int SampleCount,
    int SuccessCount,
    int MedianMs,
    int P90Ms,
    string HttpStatusSummary,
    string ErrorKind);
