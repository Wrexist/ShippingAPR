using FluentAssertions;
using ShippingAPR.Core.Validation;
using Xunit;

namespace ShippingAPR.Core.Tests;

public class MmsiValidatorTests
{
    [Theory]
    [InlineData(100_000_000)] // lower bound
    [InlineData(265_000_001)] // typical Swedish MMSI
    [InlineData(799_999_999)]
    [InlineData(800_000_000)] // 8xx block — valid, was wrongly rejected by the old 7xx bound
    [InlineData(970_000_001)] // AIS-SART
    [InlineData(972_000_001)] // MOB
    [InlineData(992_000_001)] // aids-to-navigation
    [InlineData(999_999_999)] // upper bound
    public void IsValid_NineDigitMmsi_ReturnsTrue(int mmsi)
    {
        MmsiValidator.IsValid(mmsi).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99_999_999)]       // 8 digits
    [InlineData(1_000_000_000)]    // 10 digits
    public void IsValid_OutOfRange_ReturnsFalse(int mmsi)
    {
        MmsiValidator.IsValid(mmsi).Should().BeFalse();
    }
}
