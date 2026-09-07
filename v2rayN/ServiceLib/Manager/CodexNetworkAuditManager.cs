namespace ServiceLib.Manager;

public sealed class CodexNetworkAuditManager
{
    private const int MaxRows = 50_000;
    private static readonly Lazy<CodexNetworkAuditManager> _instance = new(() => new());
    public static CodexNetworkAuditManager Instance => _instance.Value;
    private CancellationTokenSource? _cancellation;
    private Task? _loopTask;
    private Config? _config;
    private readonly object _contextLock = new();
    private string _observedProfileIndexId = string.Empty;
    private string _observedNetworkFingerprint = string.Empty;
    private string _observedNetworkKind = string.Empty;
    private string _observedIdentityConfidence = string.Empty;
    private long _contextStableSinceUnixMs;
    private bool _networkChangeSubscribed;
    private readonly SemaphoreSlim _probeGate = new(1, 1);

    public async Task<IDisposable> AcquireManualProbeLeaseAsync(CancellationToken cancellationToken)
    {
        await _probeGate.WaitAsync(cancellationToken);
        return new ProbeLease(_probeGate);
    }

    public void Start(Config config)
    {
        if (_loopTask is { IsCompleted: false })
        {
            return;
        }
        _config = config;
        if (_config.SpeedTestItem.CodexAuditSalt.IsNullOrEmpty())
        {
            _config.SpeedTestItem.CodexAuditSalt = Utils.GetGuid(false);
            _ = PersistSaltSafelyAsync(_config);
        }
        _cancellation = new CancellationTokenSource();
        InitializeContext(_config);
        if (!_networkChangeSubscribed)
        {
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
            _networkChangeSubscribed = true;
        }
        _ = PurgeSafelyAsync(_config.SpeedTestItem.CodexAuditRetentionDays);
        _loopTask = RunLoopAsync(_cancellation.Token);
    }

    public async Task StopAsync()
    {
        if (_cancellation is null || _loopTask is null)
        {
            return;
        }
        await _cancellation.CancelAsync();
        try
        {
            await _loopTask;
        }
        catch (OperationCanceledException)
        {
        }
        _cancellation.Dispose();
        _cancellation = null;
        _loopTask = null;
        if (_networkChangeSubscribed)
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            _networkChangeSubscribed = false;
        }
    }

    public void NotifyContextChanged()
    {
        lock (_contextLock)
        {
            _observedProfileIndexId = string.Empty;
            _observedNetworkFingerprint = string.Empty;
            _observedNetworkKind = string.Empty;
            _observedIdentityConfidence = string.Empty;
            _contextStableSinceUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    public CodexObservedContext GetCurrentContext()
    {
        var config = _config ?? AppManager.Instance.Config;
        var context = ObserveCurrentContext(config);
        return new()
        {
            ProfileIndexId = context.ProfileIndexId,
            NetworkFingerprint = context.NetworkFingerprint,
            StableSinceUnixMs = context.StableSinceUnixMs,
        };
    }

    public async Task SaveAsync(
        ProfileItem? profile,
        string profileIndexId,
        string mode,
        CodexConnectivityResult result,
        bool nodeChangedDuringProbe = false,
        CodexClientSignals? signals = null,
        bool signalsAttributed = false,
        string? networkFingerprint = null,
        string? networkKind = null,
        string? identityConfidence = null)
    {
        var network = networkFingerprint is null
            ? GetNetworkIdentity(_config?.SpeedTestItem.CodexAuditSalt ?? string.Empty)
            : (Kind: networkKind ?? "Unknown", Fingerprint: networkFingerprint, Confidence: identityConfidence ?? "none");
        var item = new CodexNetworkProbeItem
        {
            Id = Utils.GetGuid(false),
            CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ProfileIndexId = profileIndexId ?? string.Empty,
            NodeFingerprint = GetNodeFingerprint(profile, _config?.SpeedTestItem.CodexAuditSalt ?? string.Empty),
            NetworkFingerprint = network.Fingerprint,
            NetworkKind = network.Kind,
            IdentityConfidence = network.Confidence,
            ProbeMode = mode,
            SampleCount = result.SampleCount,
            SuccessCount = result.SuccessCount,
            MedianMs = result.MedianMs,
            P90Ms = result.P90Ms,
            HttpStatusSummary = result.HttpStatusSummary,
            ErrorKind = result.ErrorKind,
            CodexSignalsAvailable = signals?.Available == true,
            CodexSignalsAttributed = signals?.Available == true && signalsAttributed,
            CodexSignalsStatus = signals?.Status ?? "not_collected",
            CodexSignalAttributionStatus = signals?.Available != true
                ? "not_available"
                : signalsAttributed ? "stable_context" : "machine_wide_unattributed",
            SignalWindowStartUnixMs = signals?.WindowStartUnixMs ?? 0,
            SignalWindowEndUnixMs = signals?.WindowEndUnixMs ?? 0,
            CodexRetryCount = signals?.RetryCount ?? 0,
            CodexRequestTimeoutCount = signals?.RequestTimeoutCount ?? 0,
            CodexStreamDisconnectCount = signals?.StreamDisconnectCount ?? 0,
            CodexSendFailureCount = signals?.SendFailureCount ?? 0,
            CodexHttpFallbackCount = signals?.HttpFallbackCount ?? 0,
            CodexOutputItemCount = signals?.OutputItemCount ?? 0,
            ProbeVersion = 2,
            NetworkIdentityVersion = 1,
            NodeChangedDuringProbe = nodeChangedDuringProbe,
        };
        await SQLiteHelper.Instance.InsertAsync(item);
    }

    public async Task<List<CodexNetworkProbeItem>> GetRecentAsync(int limit = 1_000)
    {
        limit = Math.Clamp(limit, 1, 5_000);
        return await SQLiteHelper.Instance.TableAsync<CodexNetworkProbeItem>()
            .OrderByDescending(x => x.CreatedAtUnixMs)
            .Take(limit)
            .ToListAsync();
    }

    public async Task PurgeAsync(int retentionDays)
    {
        retentionDays = Math.Clamp(retentionDays, 1, 365);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToUnixTimeMilliseconds();
        await SQLiteHelper.Instance.ExecuteAsync($"delete from CodexNetworkProbeItem where CreatedAtUnixMs < {cutoff}");
        await SQLiteHelper.Instance.ExecuteAsync(
            $"delete from CodexNetworkProbeItem where Id not in " +
            $"(select Id from CodexNetworkProbeItem order by CreatedAtUnixMs desc limit {MaxRows})");
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var config = _config;
            var beforeContext = config is null ? default : ObserveCurrentContext(config);
            if (config is null || !config.SpeedTestItem.CodexAuditEnabled)
            {
                continue;
            }
            var interval = Math.Clamp(config.SpeedTestItem.CodexAuditIntervalMinutes, 5, 1440);
            var nowMinutes = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            if (nowMinutes % interval != 0)
            {
                continue;
            }
            if (!await _probeGate.WaitAsync(0, cancellationToken))
            {
                continue;
            }
            try
            {
                var profileIndexId = config.IndexId;
                var profile = await AppManager.Instance.GetProfileItem(profileIndexId);
                var proxy = new WebProxy($"socks5://{Global.Loopback}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}");
                var result = await new CodexConnectivityProbe().MeasureAsync(
                    proxy,
                    config.SpeedTestItem.CodexProbeSamples,
                    cancellationToken);
                var signalWindowEnd = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var signals = await new CodexClientSignalReader().ReadAsync(
                    signalWindowEnd - (long)TimeSpan.FromMinutes(interval).TotalMilliseconds,
                    signalWindowEnd);
                var nodeChanged = config.IndexId != profileIndexId;
                var afterContext = ObserveCurrentContext(config);
                var signalsAttributed = !nodeChanged
                    && beforeContext.ProfileIndexId == profileIndexId
                    && afterContext.ProfileIndexId == profileIndexId
                    && beforeContext.NetworkFingerprint == afterContext.NetworkFingerprint
                    && beforeContext.StableSinceUnixMs == afterContext.StableSinceUnixMs
                    && afterContext.StableSinceUnixMs <= signals.WindowStartUnixMs;
                var mode = nodeChanged ? "scheduled-node-changed" : "scheduled-active";
                await SaveAsync(
                    profile,
                    profileIndexId,
                    mode,
                    result,
                    nodeChanged,
                    signals,
                    signalsAttributed,
                    afterContext.NetworkFingerprint,
                    afterContext.NetworkKind,
                    afterContext.IdentityConfidence);
                await PurgeAsync(config.SpeedTestItem.CodexAuditRetentionDays);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logging.SaveLog("CodexNetworkAudit", ex);
            }
            finally
            {
                _probeGate.Release();
            }
        }
    }

    private sealed class ProbeLease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }

    private async Task PurgeSafelyAsync(int retentionDays)
    {
        try
        {
            await PurgeAsync(retentionDays);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("CodexNetworkAuditPurge", ex);
        }
    }

    private static async Task PersistSaltSafelyAsync(Config config)
    {
        try
        {
            await ConfigHandler.SaveConfig(config);
        }
        catch (Exception ex)
        {
            Logging.SaveLog("CodexNetworkAuditSalt", ex);
        }
    }

    private void InitializeContext(Config config)
    {
        var network = GetNetworkIdentity(config.SpeedTestItem.CodexAuditSalt ?? string.Empty);
        lock (_contextLock)
        {
            _observedProfileIndexId = config.IndexId;
            _observedNetworkFingerprint = network.Fingerprint;
            _observedNetworkKind = network.Kind;
            _observedIdentityConfidence = network.Confidence;
            _contextStableSinceUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }

    private ContextSnapshot ObserveCurrentContext(Config config)
    {
        var network = GetNetworkIdentity(config.SpeedTestItem.CodexAuditSalt ?? string.Empty);
        lock (_contextLock)
        {
            if (_observedProfileIndexId != config.IndexId
                || _observedNetworkFingerprint != network.Fingerprint)
            {
                _observedProfileIndexId = config.IndexId;
                _observedNetworkFingerprint = network.Fingerprint;
                _observedNetworkKind = network.Kind;
                _observedIdentityConfidence = network.Confidence;
                _contextStableSinceUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            return new(
                _observedProfileIndexId,
                _observedNetworkFingerprint,
                _observedNetworkKind,
                _observedIdentityConfidence,
                _contextStableSinceUnixMs);
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs args)
    {
        NotifyContextChanged();
    }

    private static string GetNodeFingerprint(ProfileItem? profile, string salt)
    {
        if (profile is null)
        {
            return "unknown";
        }
        var raw = string.Join('|', profile.ConfigType, profile.Address, profile.Port, profile.Password,
            profile.Network, profile.StreamSecurity, profile.Sni, profile.ProtoExtra, profile.TransportExtra);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(salt));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant()[..16];
    }

    private static (string Kind, string Fingerprint, string Confidence) GetNetworkIdentity(string salt)
    {
        try
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up
                    && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(x => new
                {
                    Kind = x.NetworkInterfaceType.ToString(),
                    Gateways = x.GetIPProperties().GatewayAddresses
                        .Select(g => g.Address.ToString())
                        .OrderBy(g => g, StringComparer.Ordinal)
                        .ToArray(),
                })
                .Where(x => x.Gateways.Length > 0)
                .OrderBy(x => x.Kind, StringComparer.Ordinal)
                .ThenBy(x => string.Join(',', x.Gateways), StringComparer.Ordinal)
                .ToList();
            if (candidates.Count == 0)
            {
                return ("Unknown", "unknown", "none");
            }
            var raw = string.Join('|', candidates.Select(x => $"{x.Kind}:{string.Join(',', x.Gateways)}"));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}|{raw}"))).ToLowerInvariant()[..16];
            return (string.Join(',', candidates.Select(x => x.Kind).Distinct()), hash, "gateway-set");
        }
        catch
        {
            return ("Unknown", "unknown", "none");
        }
    }

    private readonly record struct ContextSnapshot(
        string ProfileIndexId,
        string NetworkFingerprint,
        string NetworkKind,
        string IdentityConfidence,
        long StableSinceUnixMs);
}
