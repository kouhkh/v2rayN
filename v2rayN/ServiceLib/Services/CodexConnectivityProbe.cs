namespace ServiceLib.Services;

public sealed class CodexConnectivityProbe
{
    internal static readonly Uri Endpoint = new("https://chatgpt.com/backend-api/codex/responses");
    private readonly Uri _endpoint;
    private readonly Func<IWebProxy?, HttpMessageHandler> _handlerFactory;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _sampleDelay;

    public CodexConnectivityProbe()
        : this(
            Endpoint,
            proxy => new SocketsHttpHandler
            {
                Proxy = proxy,
                UseProxy = proxy is not null,
                ConnectTimeout = TimeSpan.FromSeconds(5),
                PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            },
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(150))
    {
    }

    internal CodexConnectivityProbe(
        Uri endpoint,
        Func<IWebProxy?, HttpMessageHandler> handlerFactory,
        TimeSpan requestTimeout,
        TimeSpan sampleDelay)
    {
        _endpoint = endpoint;
        _handlerFactory = handlerFactory;
        _requestTimeout = requestTimeout;
        _sampleDelay = sampleDelay;
    }

    public async Task<CodexConnectivityResult> MeasureAsync(
        IWebProxy? proxy,
        int sampleCount,
        CancellationToken cancellationToken = default)
    {
        sampleCount = Math.Clamp(sampleCount, 1, 10);
        using var handler = _handlerFactory(proxy);
        using var client = new HttpClient(handler)
        {
            Timeout = _requestTimeout,
        };

        var timings = new List<int>(sampleCount);
        var statuses = new Dictionary<int, int>();
        var errors = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < sampleCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            var timer = Stopwatch.StartNew();
            try
            {
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                timer.Stop();
                var status = (int)response.StatusCode;
                statuses[status] = statuses.GetValueOrDefault(status) + 1;
                if (status < 500)
                {
                    timings.Add((int)timer.ElapsedMilliseconds);
                }
                else
                {
                    errors["http_5xx"] = errors.GetValueOrDefault("http_5xx") + 1;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                errors["timeout"] = errors.GetValueOrDefault("timeout") + 1;
            }
            catch (HttpRequestException ex)
            {
                var kind = ex.HttpRequestError switch
                {
                    HttpRequestError.NameResolutionError => "dns",
                    HttpRequestError.ConnectionError => "connect",
                    HttpRequestError.SecureConnectionError => "tls",
                    _ => "transport",
                };
                errors[kind] = errors.GetValueOrDefault(kind) + 1;
            }

            if (i + 1 < sampleCount)
            {
                await Task.Delay(_sampleDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        timings.Sort();
        var median = Percentile(timings, 0.5);
        var p90 = Percentile(timings, 0.9);
        var statusSummary = string.Join(",", statuses.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"));
        var errorSummary = string.Join(",", errors.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value}"));
        return new(sampleCount, timings.Count, median, p90, statusSummary, errorSummary);
    }

    internal static int Percentile(IReadOnlyList<int> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return -1;
        }
        var index = Math.Clamp((int)Math.Ceiling(sortedValues.Count * percentile) - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }
}
