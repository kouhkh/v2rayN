namespace ServiceLib.Models.Entities;

[Serializable]
public class CodexNetworkProbeItem
{
    [PrimaryKey]
    public string Id { get; set; }

    [Indexed]
    public long CreatedAtUnixMs { get; set; }

    [Indexed]
    public string ProfileIndexId { get; set; }

    [Indexed]
    public string NodeFingerprint { get; set; }

    [Indexed]
    public string NetworkFingerprint { get; set; }

    public string NetworkKind { get; set; }
    public string IdentityConfidence { get; set; }
    public string ProbeMode { get; set; }
    public int SampleCount { get; set; }
    public int SuccessCount { get; set; }
    public int MedianMs { get; set; }
    public int P90Ms { get; set; }
    public string HttpStatusSummary { get; set; }
    public string ErrorKind { get; set; }
    public bool CodexSignalsAvailable { get; set; }
    public bool CodexSignalsAttributed { get; set; }
    public string CodexSignalsStatus { get; set; }
    public string CodexSignalAttributionStatus { get; set; }
    public long SignalWindowStartUnixMs { get; set; }
    public long SignalWindowEndUnixMs { get; set; }
    public int CodexRetryCount { get; set; }
    public int CodexRequestTimeoutCount { get; set; }
    public int CodexStreamDisconnectCount { get; set; }
    public int CodexSendFailureCount { get; set; }
    public int CodexHttpFallbackCount { get; set; }
    public int CodexOutputItemCount { get; set; }
    public int ProbeVersion { get; set; } = 2;
    public int NetworkIdentityVersion { get; set; } = 1;
    public bool NodeChangedDuringProbe { get; set; }
}
