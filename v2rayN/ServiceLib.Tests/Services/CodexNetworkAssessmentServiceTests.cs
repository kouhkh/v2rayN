namespace ServiceLib.Tests.Services;

public class CodexNetworkAssessmentServiceTests
{
    private const long Now = 1_000_000;

    [Test]
    public async Task Assess_RecommendsMeaningfullyBetterMeasuredNode()
    {
        var current = Probe("current", 2_100, "network-a", Now - 1_000);
        var candidate = Probe("candidate", 700, "network-a", Now - 2_000, "manual-selected");

        var result = CodexNetworkAssessmentService.Assess([current, candidate], "current", "network-a", 0, Now);

        await result.Kind.Should().BeEqualTo(CodexNetworkAssessmentKind.SwitchCandidate);
        await result.CandidateProfileIndexId.Should().BeEqualTo("candidate");
    }

    [Test]
    public async Task Assess_AcceptsConfirmedCandidateFromFullProbe()
    {
        var current = Probe("current", 2_100, "network-a", Now - 1_000);
        var candidate = Probe("candidate", 700, "network-a", Now - 2_000, "manual-full-confirm");

        var result = CodexNetworkAssessmentService.Assess([current, candidate], "current", "network-a", 0, Now);

        await result.Kind.Should().BeEqualTo(CodexNetworkAssessmentKind.SwitchCandidate);
        await result.CandidateProfileIndexId.Should().BeEqualTo("candidate");
    }

    [Test]
    public async Task Assess_DoesNotRecommendAnotherNodeWithoutClearAdvantage()
    {
        var current = Probe("current", 1_800, "network-a", Now - 1_000);
        var candidate = Probe("candidate", 1_100, "network-a", Now - 2_000, "manual-selected");

        var result = CodexNetworkAssessmentService.Assess([current, candidate], "current", "network-a", 0, Now);

        await result.Kind.Should().BeEqualTo(CodexNetworkAssessmentKind.ChangeAccessNetwork);
    }

    [Test]
    public async Task Assess_SeparatesHealthyShortRequestsFromCodexStreamFailures()
    {
        var current = Probe("current", 420, "network-a", Now - 1_000);
        current.CodexSignalsAvailable = true;
        current.CodexSignalsAttributed = true;
        current.SignalWindowEndUnixMs = Now - 1_000;
        current.CodexOutputItemCount = 3;
        current.CodexRetryCount = 2;

        var result = CodexNetworkAssessmentService.Assess([current], "current", "network-a", 0, Now);

        await result.Kind.Should().BeEqualTo(CodexNetworkAssessmentKind.LongStreamIssue);
    }

    [Test]
    public async Task Assess_AttributesSlowOutputAwayFromNetworkWhenSignalsAreClean()
    {
        var current = Probe("current", 420, "network-a", Now - 1_000);
        current.CodexSignalsAvailable = true;
        current.CodexSignalsAttributed = true;
        current.SignalWindowEndUnixMs = Now - 1_000;
        current.CodexOutputItemCount = 3;

        var result = CodexNetworkAssessmentService.Assess([current], "current", "network-a", 0, Now);

        await result.Kind.Should().BeEqualTo(CodexNetworkAssessmentKind.NetworkHealthy);
    }

    [Test]
    public async Task Assess_DoesNotClaimClientHealthWithoutCodexActivity()
    {
        var current = Probe("current", 420, "network-a", Now - 1_000);
        current.CodexSignalsAvailable = true;
        current.CodexSignalsAttributed = true;
        current.SignalWindowEndUnixMs = Now - 1_000;

        var result = CodexNetworkAssessmentService.Assess([current], "current", "network-a", 0, Now);

        await result.Kind.Should().BeEqualTo(CodexNetworkAssessmentKind.NoClientActivity);
    }

    [Test]
    public async Task Assess_DoesNotAttributeMachineWideFailuresAfterContextChange()
    {
        var current = Probe("current", 420, "network-a", Now - 1_000);
        current.CodexSignalsAvailable = true;
        current.CodexSignalsAttributed = false;
        current.SignalWindowEndUnixMs = Now - 1_000;
        current.CodexRetryCount = 4;

        var result = CodexNetworkAssessmentService.Assess([current], "current", "network-a", 0, Now);

        await result.Kind.Should().BeEqualTo(CodexNetworkAssessmentKind.ClientSignalsUnavailable);
    }

    [Test]
    public async Task Assess_RejectsSparseSwitchCandidate()
    {
        var current = Probe("current", 2_000, "network-a", Now - 1_000);
        var candidate = Probe("candidate", 400, "network-a", Now - 2_000, "manual-selected");
        candidate.SampleCount = 3;
        candidate.SuccessCount = 3;

        var result = CodexNetworkAssessmentService.Assess([current, candidate], "current", "network-a", 0, Now);

        await result.Kind.Should().BeEqualTo(CodexNetworkAssessmentKind.ChangeAccessNetwork);
    }

    [Test]
    public async Task Assess_RequiresRetestAfterAccessNetworkChanges()
    {
        var oldWifi = Probe("current", 420, "wifi-network", Now - 1_000);

        var result = CodexNetworkAssessmentService.Assess(
            [oldWifi],
            "current",
            "hotspot-network",
            Now - 500,
            Now);

        await result.Kind.Should().BeEqualTo(CodexNetworkAssessmentKind.ContextChanged);
    }

    private static CodexNetworkProbeItem Probe(
        string profileId,
        int p90,
        string network,
        long createdAt,
        string mode = "scheduled-active")
    {
        return new()
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAtUnixMs = createdAt,
            ProfileIndexId = profileId,
            NodeFingerprint = profileId,
            NetworkFingerprint = network,
            NetworkKind = "WiFi",
            IdentityConfidence = "test",
            ProbeMode = mode,
            SampleCount = 5,
            SuccessCount = 5,
            MedianMs = p90 / 2,
            P90Ms = p90,
            HttpStatusSummary = "405:5",
            ErrorKind = string.Empty,
            CodexSignalsStatus = "not_collected",
        };
    }
}
