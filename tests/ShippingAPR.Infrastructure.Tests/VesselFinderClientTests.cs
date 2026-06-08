using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ShippingAPR.Infrastructure.VesselFinder;
using Xunit;

namespace ShippingAPR.Infrastructure.Tests;

public class VesselFinderClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;

        public StubHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        {
            _body = body;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
    }

    private static VesselFinderClient Create(string body)
    {
        var http = new HttpClient(new StubHandler(body));
        var options = Options.Create(new VesselFinderOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://api.vesselfinder.test",
            MaxRetryCount = 0,
            RequestTimeoutSeconds = 5
        });
        return new VesselFinderClient(http, options, Mock.Of<ILogger<VesselFinderClient>>());
    }

    [Fact]
    public async Task ImoAsString_StillParsesAllFields()
    {
        // The API sometimes returns IMO as a JSON string — this must not discard the rest.
        var json = """[{"AIS_NAME":"EVER GIVEN","IMO":"9811000","CALLSIGN":"H3RC","AIS_DESTINATION":"ROTTERDAM"}]""";

        var result = await Create(json).GetVesselDetailsAsync(123456789);

        result.Should().NotBeNull();
        result!.Name.Should().Be("EVER GIVEN");
        result.ImoNumber.Should().Be(9811000);
        result.CallSign.Should().Be("H3RC");
        result.Destination.Should().Be("ROTTERDAM");
    }

    [Fact]
    public async Task ImoNull_DefaultsToZero_KeepsOtherFields()
    {
        var json = """[{"AIS_NAME":"NORDIC","IMO":null,"CALLSIGN":"SABC"}]""";

        var result = await Create(json).GetVesselDetailsAsync(123456789);

        result.Should().NotBeNull();
        result!.Name.Should().Be("NORDIC");
        result.ImoNumber.Should().Be(0);
        result.CallSign.Should().Be("SABC");
        result.Destination.Should().BeNull();
    }

    [Fact]
    public async Task GarbageImoString_DefaultsToZero()
    {
        var json = """[{"AIS_NAME":"MYSTERY","IMO":"n/a"}]""";

        var result = await Create(json).GetVesselDetailsAsync(123456789);

        result.Should().NotBeNull();
        result!.ImoNumber.Should().Be(0);
        result.Name.Should().Be("MYSTERY");
    }

    [Fact]
    public async Task EmptyArray_ReturnsNull()
    {
        (await Create("[]").GetVesselDetailsAsync(123456789)).Should().BeNull();
    }

    [Fact]
    public async Task NoApiKey_ReturnsNullWithoutCallingApi()
    {
        var http = new HttpClient(new StubHandler("[]"));
        var options = Options.Create(new VesselFinderOptions
        {
            ApiKey = "",
            BaseUrl = "https://api.vesselfinder.test"
        });
        var client = new VesselFinderClient(http, options, Mock.Of<ILogger<VesselFinderClient>>());

        (await client.GetVesselDetailsAsync(1)).Should().BeNull();
    }
}
