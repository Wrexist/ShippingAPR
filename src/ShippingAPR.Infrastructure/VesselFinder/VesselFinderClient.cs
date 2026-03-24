using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;

namespace ShippingAPR.Infrastructure.VesselFinder;

public sealed class VesselFinderClient : IVesselEnrichmentClient
{
    private readonly HttpClient _httpClient;
    private readonly VesselFinderOptions _options;
    private readonly ILogger<VesselFinderClient> _logger;

    public VesselFinderClient(
        HttpClient httpClient,
        IOptions<VesselFinderOptions> options,
        ILogger<VesselFinderClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(_options.BaseUrl);
    }

    public async Task<VesselStaticData?> GetVesselDetailsAsync(
        int mmsi, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.ApiKey))
            return null;

        var timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);

        for (var attempt = 0; attempt <= _options.MaxRetryCount; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                // VesselFinder API requires the key as a query parameter (no header auth supported)
                var response = await _httpClient.GetAsync(
                    $"/vessels?userkey={_options.ApiKey}&mmsi={mmsi}&format=json",
                    timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    if (statusCode == 401 || statusCode == 403)
                        _logger.LogError("VesselFinder API authentication failed ({Status}) — check ApiKey configuration", response.StatusCode);
                    else if (statusCode == 404)
                        _logger.LogDebug("VesselFinder: MMSI {Mmsi} not found", mmsi);
                    else if (statusCode >= 500 && attempt < _options.MaxRetryCount)
                    {
                        var delay = _options.RetryBaseDelayMs * (1 << attempt);
                        _logger.LogWarning("VesselFinder API returned {Status} for MMSI {Mmsi}, retrying in {Delay}ms (attempt {Attempt}/{Max})",
                            response.StatusCode, mmsi, delay, attempt + 1, _options.MaxRetryCount);
                        await Task.Delay(delay, cancellationToken);
                        continue;
                    }
                    else
                        _logger.LogWarning("VesselFinder API returned {Status} for MMSI {Mmsi}", response.StatusCode, mmsi);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                var doc = JsonDocument.Parse(json);

                // VesselFinder returns an array of vessel objects
                if (doc.RootElement.ValueKind != JsonValueKind.Array ||
                    doc.RootElement.GetArrayLength() == 0)
                    return null;

                var vessel = doc.RootElement[0];

                return new VesselStaticData
                {
                    Name = vessel.TryGetProperty("AIS_NAME", out var name) ? name.GetString() : null,
                    ImoNumber = vessel.TryGetProperty("IMO", out var imo) ? imo.GetInt32() : 0,
                    CallSign = vessel.TryGetProperty("CALLSIGN", out var cs) ? cs.GetString() : null,
                    Destination = vessel.TryGetProperty("AIS_DESTINATION", out var dest) ? dest.GetString() : null
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < _options.MaxRetryCount)
            {
                var delay = _options.RetryBaseDelayMs * (1 << attempt);
                _logger.LogWarning("VesselFinder request timed out for MMSI {Mmsi}, retrying in {Delay}ms (attempt {Attempt}/{Max})",
                    mmsi, delay, attempt + 1, _options.MaxRetryCount);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch vessel details for MMSI {Mmsi}", mmsi);
                return null;
            }
        }

        _logger.LogWarning("VesselFinder: all {MaxRetry} retries exhausted for MMSI {Mmsi}", _options.MaxRetryCount, mmsi);
        return null;
    }
}
