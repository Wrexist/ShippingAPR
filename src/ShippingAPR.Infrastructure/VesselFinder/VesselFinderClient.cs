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
        {
            _logger.LogDebug("VesselFinder API key not configured — skipping enrichment for MMSI {Mmsi}", mmsi);
            return null;
        }

        var timeout = TimeSpan.FromSeconds(_options.RequestTimeoutSeconds);

        for (var attempt = 0; attempt <= _options.MaxRetryCount; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                // Use custom header for API key to avoid logging sensitive data in URLs
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    $"/vessels?mmsi={mmsi}&format=json");
                request.Headers.Add("X-Api-Key", _options.ApiKey);
                var response = await _httpClient.SendAsync(request, timeoutCts.Token);

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
                using var doc = JsonDocument.Parse(json);

                // VesselFinder returns an array of vessel objects
                if (doc.RootElement.ValueKind != JsonValueKind.Array ||
                    doc.RootElement.GetArrayLength() == 0)
                {
                    _logger.LogDebug("VesselFinder returned empty response for MMSI {Mmsi}", mmsi);
                    return null;
                }

                var vessel = doc.RootElement[0];

                // Parse each field defensively: the API may send numbers as strings (or
                // omit fields). A type mismatch on one field must not discard the others.
                return new VesselStaticData
                {
                    Name = ReadString(vessel, "AIS_NAME"),
                    ImoNumber = ReadInt(vessel, "IMO"),
                    CallSign = ReadString(vessel, "CALLSIGN"),
                    Destination = ReadString(vessel, "AIS_DESTINATION")
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

    /// <summary>Reads a string field, tolerating a missing field, JSON null, or a numeric value.</summary>
    private static string? ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    /// <summary>Reads an int field, tolerating a missing field, JSON null, or a numeric string.</summary>
    private static int ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value)) return 0;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.TryGetInt32(out var n) ? n : 0,
            JsonValueKind.String => int.TryParse(value.GetString(), out var p) ? p : 0,
            _ => 0
        };
    }
}
