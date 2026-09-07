namespace ServiceLib.Services;

public static class CodexNetworkAssessmentService
{
    private static readonly TimeSpan Freshness = TimeSpan.FromHours(1);
    private static readonly TimeSpan CandidateWindow = TimeSpan.FromMinutes(30);

    public static CodexNetworkAssessment Assess(
        IReadOnlyList<CodexNetworkProbeItem> records,
        string activeProfileIndexId,
        string currentNetworkFingerprint,
        long contextStableSinceUnixMs,
        long nowUnixMs)
    {
        var current = records
            .Where(x => !x.NodeChangedDuringProbe
                && x.ProfileIndexId == activeProfileIndexId
                && x.NetworkFingerprint == currentNetworkFingerprint
                && x.CreatedAtUnixMs >= contextStableSinceUnixMs)
            .OrderByDescending(x => x.CreatedAtUnixMs)
            .FirstOrDefault();
        if (current is null)
        {
            var previousContextExists = records.Any(x => x.ProfileIndexId == activeProfileIndexId);
            return new()
            {
                Kind = previousContextExists
                    ? CodexNetworkAssessmentKind.ContextChanged
                    : CodexNetworkAssessmentKind.NoData,
            };
        }
        if (nowUnixMs - current.CreatedAtUnixMs > Freshness.TotalMilliseconds)
        {
            return new()
            {
                Kind = CodexNetworkAssessmentKind.DataStale,
                CurrentP90Ms = current.P90Ms,
            };
        }

        var currentSuccessRate = current.SampleCount > 0
            ? (double)current.SuccessCount / current.SampleCount
            : 0;
        var currentPoor = currentSuccessRate < 0.8 || current.P90Ms < 0 || current.P90Ms >= 1_500;
        var currentHealthy = currentSuccessRate == 1 && current.P90Ms is >= 0 and < 1_000;

        var candidate = records
            .Where(x => x.ProbeMode == "manual-selected"
                && x.ProfileIndexId != activeProfileIndexId
                && x.NetworkFingerprint == current.NetworkFingerprint
                && x.IdentityConfidence == current.IdentityConfidence
                && current.CreatedAtUnixMs - x.CreatedAtUnixMs <= CandidateWindow.TotalMilliseconds
                && x.CreatedAtUnixMs - current.CreatedAtUnixMs <= CandidateWindow.TotalMilliseconds
                && x.SampleCount >= 5
                && x.SuccessCount == x.SampleCount
                && x.P90Ms is >= 0 and < 1_000)
            .OrderBy(x => x.P90Ms)
            .FirstOrDefault();

        var candidateIsMeaningfullyBetter = candidate is not null
            && (current.P90Ms < 0 || candidate.P90Ms <= current.P90Ms * 0.6);
        if ((currentPoor || !currentHealthy) && candidateIsMeaningfullyBetter)
        {
            return new()
            {
                Kind = CodexNetworkAssessmentKind.SwitchCandidate,
                CandidateProfileIndexId = candidate!.ProfileIndexId,
                CurrentP90Ms = current.P90Ms,
                CandidateP90Ms = candidate.P90Ms,
            };
        }
        if (currentPoor)
        {
            return new()
            {
                Kind = CodexNetworkAssessmentKind.ChangeAccessNetwork,
                CurrentP90Ms = current.P90Ms,
            };
        }

        var signalsAvailable = current.CodexSignalsAvailable && current.CodexSignalsAttributed;
        var hasClientFailures = signalsAvailable
            && current.CodexRetryCount
                + current.CodexRequestTimeoutCount
                + current.CodexStreamDisconnectCount
                + current.CodexSendFailureCount
                + current.CodexHttpFallbackCount > 0;
        if (currentHealthy && hasClientFailures)
        {
            return new()
            {
                Kind = CodexNetworkAssessmentKind.LongStreamIssue,
                CurrentP90Ms = current.P90Ms,
            };
        }
        if (currentHealthy)
        {
            if (!signalsAvailable)
            {
                return new()
                {
                    Kind = CodexNetworkAssessmentKind.ClientSignalsUnavailable,
                    CurrentP90Ms = current.P90Ms,
                };
            }
            if (current.CodexOutputItemCount == 0)
            {
                return new()
                {
                    Kind = CodexNetworkAssessmentKind.NoClientActivity,
                    CurrentP90Ms = current.P90Ms,
                };
            }
            return new()
            {
                Kind = CodexNetworkAssessmentKind.NetworkHealthy,
                CurrentP90Ms = current.P90Ms,
            };
        }
        return new()
        {
            Kind = CodexNetworkAssessmentKind.TailLatency,
            CurrentP90Ms = current.P90Ms,
        };
    }
}
