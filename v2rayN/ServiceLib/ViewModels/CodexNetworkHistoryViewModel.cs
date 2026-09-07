namespace ServiceLib.ViewModels;

public partial class CodexNetworkHistoryViewModel : MyReactiveObject
{
    public BulkObservableCollection<CodexNetworkHistoryRow> Items { get; } = [];
    public ReactiveCommand<RxVoid, RxVoid> RefreshCmd { get; }

    [Reactive]
    public partial string Summary { get; set; } = string.Empty;

    public CodexNetworkHistoryViewModel()
    {
        RefreshCmd = ReactiveCommand.CreateFromTask(RefreshAsync);
        RefreshCmd.ThrownExceptions.Subscribe(ex => Logging.SaveLog(nameof(CodexNetworkHistoryViewModel), ex));
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var records = await CodexNetworkAuditManager.Instance.GetRecentAsync();
        var rows = records.Select(x => new CodexNetworkHistoryRow
        {
            Time = DateTimeOffset.FromUnixTimeMilliseconds(x.CreatedAtUnixMs).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            Node = ShortFingerprint(x.NodeFingerprint),
            Network = ShortFingerprint(x.NetworkFingerprint),
            NetworkKind = x.NetworkKind,
            Mode = x.NodeChangedDuringProbe ? $"{x.ProbeMode}*" : x.ProbeMode,
            Success = $"{x.SuccessCount}/{x.SampleCount}",
            MedianMs = x.MedianMs,
            P90Ms = x.P90Ms,
            HttpStatus = x.HttpStatusSummary,
            Error = x.ErrorKind,
        }).ToList();

        Items.ReplaceRange(rows);
        var successful = records.Count(x => x.SuccessCount == x.SampleCount && x.SampleCount > 0);
        Summary = string.Format(ResUI.TbCodexAuditSummary, records.Count, successful);
    }

    private static string ShortFingerprint(string value)
    {
        if (value.IsNullOrEmpty() || value == "unknown")
        {
            return "unknown";
        }
        return value.Length <= 8 ? value : value[..8];
    }
}
