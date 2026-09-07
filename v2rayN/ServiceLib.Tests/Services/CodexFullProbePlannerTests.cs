namespace ServiceLib.Tests.Services;

public class CodexFullProbePlannerTests
{
    [Test]
    public async Task SelectFinalistIds_PicksFiveFastestHealthyNodesAndActiveBaseline()
    {
        var candidates = new List<CodexFullProbeCandidate>
        {
            Candidate("active", 900, isActive: true),
            Candidate("fast-1", 100),
            Candidate("fast-2", 200),
            Candidate("fast-3", 300),
            Candidate("fast-4", 400),
            Candidate("fast-5", 500),
            Candidate("fast-6", 600),
            new("failed", 1, 0, -1, false),
        };

        var result = CodexFullProbePlanner.SelectFinalistIds(candidates);

        await result.SequenceEqual(["active", "fast-1", "fast-2", "fast-3", "fast-4", "fast-5"])
            .Should().BeTrue();
    }

    [Test]
    public async Task SelectFinalistIds_DoesNotDuplicateActiveNodeAlreadyInFastestFive()
    {
        var candidates = new List<CodexFullProbeCandidate>
        {
            Candidate("active", 100, isActive: true),
            Candidate("other", 200),
        };

        var result = CodexFullProbePlanner.SelectFinalistIds(candidates);

        await result.SequenceEqual(["active", "other"]).Should().BeTrue();
    }

    [Test]
    public async Task FullProbeConstantsBoundBackgroundImpact()
    {
        await CodexFullProbePlanner.ScreeningSamples.Should().BeEqualTo(1);
        await CodexFullProbePlanner.ScreeningConcurrency.Should().BeLessThanOrEqualTo(2);
        await CodexFullProbePlanner.ScreeningPageSize.Should().BeLessThanOrEqualTo(20);
        await CodexFullProbePlanner.ConfirmationSamples.Should().BeGreaterThanOrEqualTo(5);
        await CodexFullProbePlanner.ConfirmationConcurrency.Should().BeEqualTo(1);
    }

    private static CodexFullProbeCandidate Candidate(string id, int p90, bool isActive = false)
    {
        return new(id, 1, 1, p90, isActive);
    }
}
