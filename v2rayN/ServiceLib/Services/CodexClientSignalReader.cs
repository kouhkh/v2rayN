namespace ServiceLib.Services;

/// <summary>
/// Reads aggregate connectivity signals from the local Codex log database.
/// The SQL is deliberately restricted to known targets and fixed patterns;
/// raw log bodies, request headers, prompts, and output text never leave SQLite.
/// </summary>
public sealed class CodexClientSignalReader
{
    private static readonly HashSet<string> RequiredColumns =
    [
        "ts",
        "target",
        "feedback_log_body",
    ];

    private readonly string _databasePath;

    public CodexClientSignalReader(string? databasePath = null)
    {
        _databasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "logs_2.sqlite");
    }

    public Task<CodexClientSignals> ReadAsync(long windowStartUnixMs, long windowEndUnixMs)
    {
        if (windowEndUnixMs <= windowStartUnixMs)
        {
            return Task.FromResult(Unavailable(windowStartUnixMs, windowEndUnixMs, "invalid_window"));
        }
        if (!File.Exists(_databasePath))
        {
            return Task.FromResult(Unavailable(windowStartUnixMs, windowEndUnixMs, "database_missing"));
        }

        try
        {
            using var db = new SQLiteConnection(
                _databasePath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex,
                false)
            {
                BusyTimeout = TimeSpan.FromSeconds(2),
            };

            var columns = db.GetTableInfo("logs").Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!RequiredColumns.IsSubsetOf(columns))
            {
                return Task.FromResult(Unavailable(windowStartUnixMs, windowEndUnixMs, "unsupported_schema"));
            }

            // Use only complete seconds inside the window. Codex stores seconds and
            // nanoseconds separately; trimming partial boundary seconds prevents an
            // event from being counted in two adjacent audit windows.
            var startSeconds = (windowStartUnixMs + 999) / 1_000;
            var endSeconds = windowEndUnixMs / 1_000;
            var row = db.Query<AggregateRow>("""
                select
                    coalesce(sum(case when target = 'codex_core::responses_retry' then 1 else 0 end), 0) as RetryCount,
                    coalesce(sum(case when target = 'codex_core::responses_retry'
                        and feedback_log_body like '%request timed out%' then 1 else 0 end), 0) as RequestTimeoutCount,
                    coalesce(sum(case when target = 'codex_core::responses_retry'
                        and (feedback_log_body like '%stream disconnected%'
                            or feedback_log_body like '%stream error%') then 1 else 0 end), 0) as StreamDisconnectCount,
                    coalesce(sum(case when target = 'codex_core::responses_retry'
                        and feedback_log_body like '%error sending request%' then 1 else 0 end), 0) as SendFailureCount,
                    coalesce(sum(case when target = 'codex_core::client'
                        and feedback_log_body like '%falling back to HTTP%' then 1 else 0 end), 0) as HttpFallbackCount,
                    coalesce(sum(case when target = 'codex_core::stream_events_utils'
                        and feedback_log_body like '%Output item item_type=%' then 1 else 0 end), 0) as OutputItemCount
                from logs
                where ts >= ? and ts < ?
                  and target in (
                      'codex_core::responses_retry',
                      'codex_core::client',
                      'codex_core::stream_events_utils'
                  )
                """, startSeconds, endSeconds).FirstOrDefault() ?? new();

            return Task.FromResult(new CodexClientSignals
            {
                WindowStartUnixMs = windowStartUnixMs,
                WindowEndUnixMs = windowEndUnixMs,
                Available = true,
                Status = "ok",
                RetryCount = row.RetryCount,
                RequestTimeoutCount = row.RequestTimeoutCount,
                StreamDisconnectCount = row.StreamDisconnectCount,
                SendFailureCount = row.SendFailureCount,
                HttpFallbackCount = row.HttpFallbackCount,
                OutputItemCount = row.OutputItemCount,
            });
        }
        catch (SQLiteException)
        {
            return Task.FromResult(Unavailable(windowStartUnixMs, windowEndUnixMs, "read_failed"));
        }
        catch (IOException)
        {
            return Task.FromResult(Unavailable(windowStartUnixMs, windowEndUnixMs, "read_failed"));
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(Unavailable(windowStartUnixMs, windowEndUnixMs, "access_denied"));
        }
        catch (Exception)
        {
            return Task.FromResult(Unavailable(windowStartUnixMs, windowEndUnixMs, "read_failed"));
        }
    }

    private static CodexClientSignals Unavailable(long start, long end, string status)
    {
        return new()
        {
            WindowStartUnixMs = start,
            WindowEndUnixMs = end,
            Available = false,
            Status = status,
        };
    }

    private sealed class AggregateRow
    {
        public int RetryCount { get; set; }
        public int RequestTimeoutCount { get; set; }
        public int StreamDisconnectCount { get; set; }
        public int SendFailureCount { get; set; }
        public int HttpFallbackCount { get; set; }
        public int OutputItemCount { get; set; }
    }
}
