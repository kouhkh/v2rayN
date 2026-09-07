namespace ServiceLib.Models.Dto;

public enum CodexNetworkAssessmentKind
{
    NoData,
    ContextChanged,
    DataStale,
    SwitchCandidate,
    ChangeAccessNetwork,
    LongStreamIssue,
    ClientSignalsUnavailable,
    NoClientActivity,
    NetworkHealthy,
    TailLatency,
}

public sealed class CodexNetworkAssessment
{
    public CodexNetworkAssessmentKind Kind { get; init; }
    public string CandidateProfileIndexId { get; init; } = string.Empty;
    public int CurrentP90Ms { get; init; } = -1;
    public int CandidateP90Ms { get; init; } = -1;
}
