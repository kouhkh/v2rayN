namespace ServiceLib.Tests.Services;

public class CodexClientSignalReaderTests
{
    [Test]
    public async Task ReadAsync_AggregatesOnlyWhitelistedTargetsAndFixedPatterns()
    {
        var path = Path.Combine(Path.GetTempPath(), $"codex-signals-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var db = new SQLiteConnection(path, false))
            {
                db.Execute("create table logs (ts integer not null, target text, feedback_log_body text)");
                db.Execute("insert into logs values (?, ?, ?)", 100L, "codex_core::responses_retry", "request timed out; stream disconnected; error sending request; secret prompt");
                db.Execute("insert into logs values (?, ?, ?)", 101L, "codex_core::client", "falling back to HTTP; authorization secret");
                db.Execute("insert into logs values (?, ?, ?)", 102L, "codex_core::stream_events_utils", "Output item item_type=message; private response");
                db.Execute("insert into logs values (?, ?, ?)", 103L, "untrusted_target", "request timed out; stream disconnected; falling back to HTTP");
                db.Execute("insert into logs values (?, ?, ?)", 200L, "codex_core::responses_retry", "request timed out outside window");
            }

            var result = await new CodexClientSignalReader(path).ReadAsync(100_000, 110_000);

            await result.Available.Should().BeTrue();
            await result.Status.Should().BeEqualTo("ok");
            await result.RetryCount.Should().BeEqualTo(1);
            await result.RequestTimeoutCount.Should().BeEqualTo(1);
            await result.StreamDisconnectCount.Should().BeEqualTo(1);
            await result.SendFailureCount.Should().BeEqualTo(1);
            await result.HttpFallbackCount.Should().BeEqualTo(1);
            await result.OutputItemCount.Should().BeEqualTo(1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ReadAsync_RejectsUnknownSchemaWithoutReadingBodies()
    {
        var path = Path.Combine(Path.GetTempPath(), $"codex-signals-schema-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var db = new SQLiteConnection(path, false))
            {
                db.Execute("create table logs (ts integer not null, target text)");
            }

            var result = await new CodexClientSignalReader(path).ReadAsync(100_000, 110_000);

            await result.Available.Should().BeFalse();
            await result.Status.Should().BeEqualTo("unsupported_schema");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task CreateTable_AddsSignalColumnsToExistingAuditTable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"codex-audit-migration-{Guid.NewGuid():N}.sqlite");
        try
        {
            using var db = new SQLiteConnection(path, false);
            db.Execute("create table CodexNetworkProbeItem (Id text primary key, CreatedAtUnixMs integer)");

            db.CreateTable<CodexNetworkProbeItem>();
            var columns = db.GetTableInfo(nameof(CodexNetworkProbeItem)).Select(x => x.Name).ToHashSet();

            await columns.Contains(nameof(CodexNetworkProbeItem.CodexSignalsAvailable)).Should().BeTrue();
            await columns.Contains(nameof(CodexNetworkProbeItem.CodexSignalsAttributed)).Should().BeTrue();
            await columns.Contains(nameof(CodexNetworkProbeItem.CodexRetryCount)).Should().BeTrue();
            await columns.Contains(nameof(CodexNetworkProbeItem.SignalWindowEndUnixMs)).Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
