namespace ServiceLib.Services;

public static class CodexFullProbePlanner
{
    public const int ScreeningSamples = 1;
    public const int ScreeningConcurrency = 2;
    public const int ScreeningPageSize = 20;
    public const int ScreeningCooldownMilliseconds = 250;
    public const int ConfirmationSamples = 5;
    public const int ConfirmationConcurrency = 1;
    public const int CandidateCount = 5;

    public static IReadOnlyList<string> SelectFinalistIds(
        IReadOnlyList<CodexFullProbeCandidate> candidates)
    {
        var finalists = candidates
            .Where(x => x.SuccessCount == x.SampleCount
                && x.SampleCount >= ScreeningSamples
                && x.P90Ms >= 0)
            .OrderBy(x => x.P90Ms)
            .ThenBy(x => x.IndexId, StringComparer.Ordinal)
            .Take(CandidateCount)
            .Select(x => x.IndexId)
            .ToList();

        var active = candidates.FirstOrDefault(x => x.IsActive);
        if (active is not null && !finalists.Contains(active.IndexId, StringComparer.Ordinal))
        {
            finalists.Insert(0, active.IndexId);
        }
        return finalists;
    }
}

public sealed record CodexFullProbeCandidate(
    string IndexId,
    int SampleCount,
    int SuccessCount,
    int P90Ms,
    bool IsActive);
