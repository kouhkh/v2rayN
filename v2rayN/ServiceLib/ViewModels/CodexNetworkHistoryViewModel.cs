namespace ServiceLib.ViewModels;

public partial class CodexNetworkHistoryViewModel : MyReactiveObject
{
    public BulkObservableCollection<CodexNetworkHistoryRow> Items { get; } = [];
    public ReactiveCommand<RxVoid, RxVoid> RefreshCmd { get; }

    [Reactive]
    public partial string Summary { get; set; } = string.Empty;

    [Reactive]
    public partial string Assessment { get; set; } = string.Empty;

    public CodexNetworkHistoryViewModel()
    {
        RefreshCmd = ReactiveCommand.CreateFromTask(RefreshAsync);
        RefreshCmd.ThrownExceptions.Subscribe(ex => Logging.SaveLog(nameof(CodexNetworkHistoryViewModel), ex));
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var records = await CodexNetworkAuditManager.Instance.GetRecentAsync();
        var profiles = await AppManager.Instance.GetProfileItemsByIndexIdsAsMap(records.Select(x => x.ProfileIndexId));
        var rows = records.Select(x => new CodexNetworkHistoryRow
        {
            Time = DateTimeOffset.FromUnixTimeMilliseconds(x.CreatedAtUnixMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            Node = profiles.TryGetValue(x.ProfileIndexId, out var profile)
                ? profile.Remarks
                : ShortFingerprint(x.NodeFingerprint),
            Network = ShortFingerprint(x.NetworkFingerprint),
            NetworkKind = x.NetworkKind,
            Mode = x.NodeChangedDuringProbe ? $"{x.ProbeMode}*" : x.ProbeMode,
            Success = $"{x.SuccessCount}/{x.SampleCount}",
            MedianMs = x.MedianMs,
            P90Ms = x.P90Ms,
            HttpStatus = x.HttpStatusSummary,
            CodexSignals = FormatSignals(x),
            Error = x.ErrorKind,
        }).ToList();

        Items.ReplaceRange(rows);
        var successful = records.Count(x => x.SuccessCount == x.SampleCount && x.SampleCount > 0);
        Summary = string.Format(ResUI.TbCodexAuditSummary, records.Count, successful);
        var currentContext = CodexNetworkAuditManager.Instance.GetCurrentContext();
        Assessment = FormatAssessment(
            CodexNetworkAssessmentService.Assess(
                records,
                currentContext.ProfileIndexId,
                currentContext.NetworkFingerprint,
                currentContext.StableSinceUnixMs,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()),
            profiles);
    }

    private static string ShortFingerprint(string value)
    {
        if (value.IsNullOrEmpty() || value == "unknown")
        {
            return "unknown";
        }
        return value.Length <= 8 ? value : value[..8];
    }

    private static string FormatSignals(CodexNetworkProbeItem item)
    {
        if (!item.CodexSignalsAvailable)
        {
            return item.CodexSignalsStatus.IsNullOrEmpty() || item.CodexSignalsStatus == "not_collected"
                ? "—"
                : item.CodexSignalsStatus;
        }
        var signals = string.Format(
            ResUI.TbCodexAuditSignalsFormat,
            item.CodexRetryCount,
            item.CodexRequestTimeoutCount,
            item.CodexStreamDisconnectCount,
            item.CodexSendFailureCount,
            item.CodexHttpFallbackCount);
        return item.CodexSignalsAttributed
            ? signals
            : string.Format(ResUI.TbCodexAuditSignalsUnattributed, signals);
    }

    private static string FormatAssessment(
        CodexNetworkAssessment assessment,
        IReadOnlyDictionary<string, ProfileItem> profiles)
    {
        return assessment.Kind switch
        {
            CodexNetworkAssessmentKind.NoData => ResUI.TbCodexAssessmentNoData,
            CodexNetworkAssessmentKind.ContextChanged => ResUI.TbCodexAssessmentContextChanged,
            CodexNetworkAssessmentKind.DataStale => ResUI.TbCodexAssessmentStale,
            CodexNetworkAssessmentKind.SwitchCandidate => string.Format(
                ResUI.TbCodexAssessmentSwitch,
                profiles.TryGetValue(assessment.CandidateProfileIndexId, out var candidate)
                    ? candidate.Remarks
                    : assessment.CandidateProfileIndexId,
                assessment.CandidateP90Ms,
                assessment.CurrentP90Ms),
            CodexNetworkAssessmentKind.ChangeAccessNetwork => string.Format(
                ResUI.TbCodexAssessmentAccessNetwork,
                assessment.CurrentP90Ms),
            CodexNetworkAssessmentKind.LongStreamIssue => string.Format(
                ResUI.TbCodexAssessmentLongStream,
                assessment.CurrentP90Ms),
            CodexNetworkAssessmentKind.ClientSignalsUnavailable => string.Format(
                ResUI.TbCodexAssessmentSignalsUnavailable,
                assessment.CurrentP90Ms),
            CodexNetworkAssessmentKind.NoClientActivity => string.Format(
                ResUI.TbCodexAssessmentNoClientActivity,
                assessment.CurrentP90Ms),
            CodexNetworkAssessmentKind.NetworkHealthy => string.Format(
                ResUI.TbCodexAssessmentHealthy,
                assessment.CurrentP90Ms),
            _ => string.Format(ResUI.TbCodexAssessmentTail, assessment.CurrentP90Ms),
        };
    }
}
