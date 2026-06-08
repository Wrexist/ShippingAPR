using System.Net;
using System.Net.Http.Headers;

namespace ShippingAPR.Infrastructure.Http;

/// <summary>
/// Retries transient HTTP failures (5xx, 429, and connection exceptions) for safe,
/// content-less requests with exponential backoff + jitter, honouring a Retry-After
/// header when present. Keeps the provider clients from hammering an API or failing
/// permanently on a transient blip.
/// </summary>
public sealed class ResilientHttpHandler : DelegatingHandler
{
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;

    public ResilientHttpHandler(int maxRetries = 3, TimeSpan? baseDelay = null)
    {
        _maxRetries = Math.Max(0, maxRetries);
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(500);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        // Only retry idempotent, content-less requests (all provider calls are GET).
        var canRetry = request.Content is null &&
                       (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var toSend = attempt == 0 ? request : Clone(request);
                var response = await base.SendAsync(toSend, ct).ConfigureAwait(false);

                if (!canRetry || attempt >= _maxRetries || !ShouldRetry(response.StatusCode))
                    return response;

                var delay = ComputeDelay(attempt, response);
                response.Dispose();
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (canRetry && attempt < _maxRetries)
            {
                await Task.Delay(ComputeDelay(attempt, null), ct).ConfigureAwait(false);
            }
        }
    }

    private static bool ShouldRetry(HttpStatusCode status)
    {
        var code = (int)status;
        return code == 429 || (code >= 500 && code <= 599);
    }

    private TimeSpan ComputeDelay(int attempt, HttpResponseMessage? response)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta is { } delta && delta > TimeSpan.Zero)
                return delta;
            if (retryAfter.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;
                if (wait > TimeSpan.Zero) return wait;
            }
        }

        var backoffMs = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var jitterMs = Random.Shared.NextDouble() * backoffMs * 0.25;
        return TimeSpan.FromMilliseconds(backoffMs + jitterMs);
    }

    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }
}
