using FluentAssertions;
using ShippingAPR.Core.Diagnostics;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class DataFreshnessTests
{
    private static readonly DateTime Now = new(2026, 6, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(30);

    [Fact]
    public void Evaluate_NotConnected_IsIdle()
    {
        DataFreshness.Evaluate(false, Now.AddSeconds(-5), Now, Threshold)
            .Should().Be(DataFreshnessState.Idle);
    }

    [Fact]
    public void Evaluate_ConnectedNoDataYet_IsWaiting()
    {
        DataFreshness.Evaluate(true, null, Now, Threshold)
            .Should().Be(DataFreshnessState.Waiting);
    }

    [Fact]
    public void Evaluate_RecentData_IsLive()
    {
        DataFreshness.Evaluate(true, Now.AddSeconds(-5), Now, Threshold)
            .Should().Be(DataFreshnessState.Live);
    }

    [Fact]
    public void Evaluate_AtThreshold_IsLive()
    {
        DataFreshness.Evaluate(true, Now.AddSeconds(-30), Now, Threshold)
            .Should().Be(DataFreshnessState.Live);
    }

    [Fact]
    public void Evaluate_BeyondThreshold_IsStale()
    {
        DataFreshness.Evaluate(true, Now.AddSeconds(-31), Now, Threshold)
            .Should().Be(DataFreshnessState.Stale);
    }

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(45, "45s")]
    [InlineData(90, "1m")]
    [InlineData(3600, "1h")]
    [InlineData(7200, "2h")]
    public void DescribeAge_FormatsCompactly(int seconds, string expected)
    {
        DataFreshness.DescribeAge(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public void DescribeAge_NegativeClampsToZero()
    {
        DataFreshness.DescribeAge(TimeSpan.FromSeconds(-5)).Should().Be("0s");
    }
}
