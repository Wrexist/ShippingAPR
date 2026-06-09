using System.Net;
using FluentAssertions;
using ShippingAPR.Infrastructure.Http;
using Xunit;

namespace ShippingAPR.Infrastructure.Tests;

public class ResilientHttpHandlerTests
{
    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _codes;
        public int Calls { get; private set; }

        public SequenceHandler(params HttpStatusCode[] codes) => _codes = new Queue<HttpStatusCode>(codes);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var code = _codes.Count > 0 ? _codes.Dequeue() : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(code));
        }
    }

    private static HttpClient Client(SequenceHandler inner, int maxRetries) =>
        new(new ResilientHttpHandler(maxRetries, TimeSpan.FromMilliseconds(1)) { InnerHandler = inner });

    [Fact]
    public async Task Retries_On503_ThenSucceeds()
    {
        var inner = new SequenceHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var client = Client(inner, maxRetries: 3);

        var resp = await client.GetAsync("https://example.test/");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Calls.Should().Be(3);
    }

    [Fact]
    public async Task Retries_On429()
    {
        var inner = new SequenceHandler((HttpStatusCode)429, HttpStatusCode.OK);
        using var client = Client(inner, maxRetries: 3);

        var resp = await client.GetAsync("https://example.test/");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        inner.Calls.Should().Be(2);
    }

    [Fact]
    public async Task GivesUp_AfterMaxRetries()
    {
        var inner = new SequenceHandler(
            HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError);
        using var client = Client(inner, maxRetries: 2);

        var resp = await client.GetAsync("https://example.test/");

        resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        inner.Calls.Should().Be(3); // initial + 2 retries
    }

    [Fact]
    public async Task DoesNotRetry_OnClientError()
    {
        var inner = new SequenceHandler(HttpStatusCode.BadRequest);
        using var client = Client(inner, maxRetries: 3);

        var resp = await client.GetAsync("https://example.test/");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        inner.Calls.Should().Be(1);
    }
}
