using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Core.Validation;
using ShippingAPR.Infrastructure.Providers;

namespace ShippingAPR.Infrastructure.DataDocked;

/// <summary>
/// AIS data provider that polls the Data Docked REST API for vessel positions.
/// </summary>
public sealed class DataDockedClient : PollingAisProviderBase
{
    private readonly HttpClient _httpClient;
    private readonly DataDockedOptions _options;
    private readonly ILogger<DataDockedClient> _logger;

    protected override string ProviderName => "DataDocked";
    protected override string ApiKey => _options.ApiKey;
    protected override string MissingKeyMessage =>
        "Data Docked API key is not configured. Set it in appsettings.json under DataDocked:ApiKey.";

    public DataDockedClient(
        HttpClient httpClient,
        IOptions<DataDockedOptions> options,
        ILogger<DataDockedClient> logger)
        : base(logger, options.Value.PollIntervalSeconds)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task FetchAndEmitAsync(BoundingBox area, CancellationToken ct)
    {
        var url = $"{_options.BaseUrl}/vessels/area" +
                  $"?min_lat={area.MinLatitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&max_lat={area.MaxLatitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&min_lon={area.MinLongitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&max_lon={area.MaxLongitude.ToString(CultureInfo.InvariantCulture)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {_options.ApiKey}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

        var response = await _httpClient.SendAsync(request, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        var doc = JsonDocument.Parse(json);

        JsonElement vessels;
        if (doc.RootElement.TryGetProperty("data", out var dataArray))
            vessels = dataArray;
        else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            vessels = doc.RootElement;
        else
            return;

        foreach (var vessel in vessels.EnumerateArray())
        {
            try
            {
                var mmsi = vessel.TryGetProperty("mmsi", out var mmsiProp) ? mmsiProp.GetInt32() : 0;
                if (!MmsiValidator.IsValid(mmsi)) continue;

                var lat = vessel.TryGetProperty("latitude", out var latProp) ? latProp.GetDouble() :
                          vessel.TryGetProperty("lat", out var latProp2) ? latProp2.GetDouble() : 0;
                var lon = vessel.TryGetProperty("longitude", out var lonProp) ? lonProp.GetDouble() :
                          vessel.TryGetProperty("lon", out var lonProp2) ? lonProp2.GetDouble() : 0;
                var speed = vessel.TryGetProperty("speed", out var speedProp) ? speedProp.GetDouble() : 0;
                var course = vessel.TryGetProperty("course", out var courseProp) ? courseProp.GetDouble() : 0;
                var heading = vessel.TryGetProperty("heading", out var headingProp) ? headingProp.GetDouble() : 0;

                var position = AisPositionNormalizer.TryCreate(lat, lon, speed, course, heading);
                if (position is null) continue; // no usable fix (missing/out-of-range/Null Island)

                EmitMessage(new AisMessageEventArgs
                {
                    MessageType = "PositionReport",
                    Mmsi = mmsi,
                    Position = position
                });

                var name = vessel.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                if (!string.IsNullOrEmpty(name))
                {
                    EmitMessage(new AisMessageEventArgs
                    {
                        MessageType = "ShipStaticData",
                        Mmsi = mmsi,
                        StaticData = new VesselStaticData
                        {
                            Name = name,
                            CallSign = vessel.TryGetProperty("callsign", out var csProp) ? csProp.GetString() : null,
                            ImoNumber = ReadImo(vessel),
                            Destination = vessel.TryGetProperty("destination", out var destProp) ? destProp.GetString() : null
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse DataDocked vessel entry");
            }
        }
    }

    /// <summary>Reads the IMO field tolerantly — APIs send it as a number or a numeric string.</summary>
    private static int ReadImo(JsonElement vessel)
    {
        if (!vessel.TryGetProperty("imo", out var imo)) return 0;
        return imo.ValueKind switch
        {
            JsonValueKind.Number => imo.TryGetInt32(out var n) ? n : 0,
            JsonValueKind.String => int.TryParse(imo.GetString(), out var p) ? p : 0,
            _ => 0
        };
    }
}
