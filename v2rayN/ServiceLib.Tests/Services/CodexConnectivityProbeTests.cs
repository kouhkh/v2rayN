using System.Net.Http;

namespace ServiceLib.Tests.Services;

public class CodexConnectivityProbeTests
{
    [Test]
    public async Task MeasureAsync_AggregatesReachableAndServerErrorResponses()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.MethodNotAllowed),
            new HttpResponseMessage(HttpStatusCode.BadGateway),
            new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var probe = CreateProbe(handler);

        var result = await probe.MeasureAsync(null, 3);

        await result.SampleCount.Should().BeEqualTo(3);
        await result.SuccessCount.Should().BeEqualTo(2);
        await result.HttpStatusSummary.Should().BeEqualTo("401:1,405:1,502:1");
        await result.ErrorKind.Should().BeEqualTo("http_5xx:1");
        await result.MedianMs.Should().BeGreaterThanOrEqualTo(0);
        await result.P90Ms.Should().BeGreaterThanOrEqualTo(result.MedianMs);
    }

    [Test]
    public async Task MeasureAsync_ClassifiesPerSampleTimeout()
    {
        var probe = CreateProbe(new TimeoutHandler());

        var result = await probe.MeasureAsync(null, 2);

        await result.SuccessCount.Should().BeEqualTo(0);
        await result.MedianMs.Should().BeEqualTo(-1);
        await result.P90Ms.Should().BeEqualTo(-1);
        await result.ErrorKind.Should().BeEqualTo("timeout:2");
    }

    [Test]
    public async Task Percentile_UsesNearestRank()
    {
        var values = new[] { 10, 20, 30, 40, 50 };

        await CodexConnectivityProbe.Percentile(values, 0.5).Should().BeEqualTo(30);
        await CodexConnectivityProbe.Percentile(values, 0.9).Should().BeEqualTo(50);
        await CodexConnectivityProbe.Percentile([], 0.5).Should().BeEqualTo(-1);
    }

    private static CodexConnectivityProbe CreateProbe(HttpMessageHandler handler)
    {
        return new CodexConnectivityProbe(
            new Uri("https://example.invalid/codex"),
            _ => handler,
            TimeSpan.FromSeconds(1),
            TimeSpan.Zero);
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new TaskCanceledException("simulated request timeout");
        }
    }
}
