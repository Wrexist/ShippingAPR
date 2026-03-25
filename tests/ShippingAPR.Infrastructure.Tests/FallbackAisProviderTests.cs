using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Core.Models;
using ShippingAPR.Infrastructure;
using Xunit;

namespace ShippingAPR.Infrastructure.Tests;

public class FallbackAisProviderTests
{
    private readonly Mock<IAisDataProvider> _primaryMock;
    private readonly Mock<IAisDataProvider> _fallbackMock;
    private readonly FallbackAisProvider _provider;

    public FallbackAisProviderTests()
    {
        _primaryMock = new Mock<IAisDataProvider>();
        _fallbackMock = new Mock<IAisDataProvider>();
        _provider = new FallbackAisProvider(
            _primaryMock.Object,
            _fallbackMock.Object,
            Mock.Of<ILogger<FallbackAisProvider>>());
    }

    [Fact]
    public async Task ConnectAsync_DelegatesToPrimary()
    {
        var area = BoundingBox.GothenburgDefault;
        _primaryMock.Setup(p => p.ConnectAsync(area, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _provider.ConnectAsync(area);

        _primaryMock.Verify(p => p.ConnectAsync(area, It.IsAny<CancellationToken>()), Times.Once);
        _fallbackMock.Verify(p => p.ConnectAsync(It.IsAny<BoundingBox>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConnectAsync_SwitchesToFallback_WhenPrimaryFails()
    {
        var area = BoundingBox.GothenburgDefault;
        _primaryMock.Setup(p => p.ConnectAsync(area, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("API key missing"));
        _fallbackMock.Setup(p => p.ConnectAsync(area, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _provider.ConnectAsync(area);

        _fallbackMock.Verify(p => p.ConnectAsync(area, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DisconnectAsync_DelegatesToActiveProvider()
    {
        _primaryMock.Setup(p => p.DisconnectAsync()).Returns(Task.CompletedTask);

        await _provider.DisconnectAsync();

        _primaryMock.Verify(p => p.DisconnectAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateSubscription_DelegatesToActiveProvider()
    {
        var area = BoundingBox.Europe;
        _primaryMock.Setup(p => p.UpdateSubscriptionAsync(area, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _provider.UpdateSubscriptionAsync(area);

        _primaryMock.Verify(p => p.UpdateSubscriptionAsync(area, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void MessageReceived_ForwardedFromPrimary()
    {
        AisMessageEventArgs? received = null;
        _provider.MessageReceived += (_, e) => received = e;

        var args = new AisMessageEventArgs
        {
            MessageType = "PositionReport",
            Mmsi = 265000001,
            Position = new VesselPosition
            {
                Latitude = 57.7, Longitude = 11.9,
                SpeedOverGround = 12.5, CourseOverGround = 45
            }
        };

        _primaryMock.Raise(p => p.MessageReceived += null, _primaryMock.Object, args);

        received.Should().NotBeNull();
        received!.Mmsi.Should().Be(265000001);
    }

    [Fact]
    public void ConnectionStatusChanged_ForwardedFromPrimary()
    {
        ConnectionStatus? received = null;
        _provider.ConnectionStatusChanged += (_, s) => received = s;

        _primaryMock.Raise(p => p.ConnectionStatusChanged += null,
            _primaryMock.Object, ConnectionStatus.Connected);

        received.Should().Be(ConnectionStatus.Connected);
    }

    [Fact]
    public void Status_DelegatesFromActiveProvider()
    {
        _primaryMock.SetupGet(p => p.Status).Returns(ConnectionStatus.Connected);
        _provider.Status.Should().Be(ConnectionStatus.Connected);
    }
}
