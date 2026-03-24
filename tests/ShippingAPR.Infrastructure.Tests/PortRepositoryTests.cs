using FluentAssertions;
using ShippingAPR.Infrastructure.Ports;
using Xunit;

namespace ShippingAPR.Infrastructure.Tests;

public class PortRepositoryTests
{
    private readonly PortRepository _repo;

    public PortRepositoryTests()
    {
        _repo = new PortRepository();
    }

    // --- FindByLocode ---

    [Fact]
    public void FindByLocode_ExistingPort_ReturnsPort()
    {
        var port = _repo.FindByLocode("SEGOT");
        port.Should().NotBeNull();
        port!.Name.Should().Contain("Gothenburg").Or.Contain("Göteborg").Or.Contain("GOTHENBURG");
    }

    [Fact]
    public void FindByLocode_CaseInsensitive()
    {
        var upper = _repo.FindByLocode("SEGOT");
        var lower = _repo.FindByLocode("segot");

        upper.Should().NotBeNull();
        lower.Should().NotBeNull();
        upper!.Locode.Should().Be(lower!.Locode);
    }

    [Fact]
    public void FindByLocode_WithSpace_FindsPort()
    {
        var port = _repo.FindByLocode("SE GOT");
        port.Should().NotBeNull();
    }

    [Fact]
    public void FindByLocode_NonExistent_ReturnsNull()
    {
        _repo.FindByLocode("ZZZZZ").Should().BeNull();
    }

    [Fact]
    public void FindByLocode_NullOrEmpty_ReturnsNull()
    {
        _repo.FindByLocode(null!).Should().BeNull();
        _repo.FindByLocode("").Should().BeNull();
        _repo.FindByLocode("  ").Should().BeNull();
    }

    // --- FindByName ---

    [Fact]
    public void FindByName_ExactMatch_ReturnsPort()
    {
        var port = _repo.FindByName("Gothenburg");
        port.Should().NotBeNull();
    }

    [Fact]
    public void FindByName_NullOrEmpty_ReturnsNull()
    {
        _repo.FindByName(null!).Should().BeNull();
        _repo.FindByName("").Should().BeNull();
    }

    // --- FindNearest ---

    [Fact]
    public void FindNearest_GothenburgCoords_ReturnsGothenburg()
    {
        var port = _repo.FindNearest(57.7089, 11.9746);
        port.Should().NotBeNull();
        port!.Locode.Should().Contain("GOT");
    }

    // --- SearchPorts ---

    [Fact]
    public void SearchPorts_PartialName_ReturnsMatches()
    {
        var results = _repo.SearchPorts("Stockholm").ToList();
        results.Should().NotBeEmpty();
    }

    [Fact]
    public void SearchPorts_NullOrEmpty_ReturnsEmpty()
    {
        _repo.SearchPorts(null!).Should().BeEmpty();
        _repo.SearchPorts("").Should().BeEmpty();
    }

    [Fact]
    public void SearchPorts_ByCountry_ReturnsMatches()
    {
        var results = _repo.SearchPorts("Sweden").ToList();
        results.Should().NotBeEmpty();
    }

    // --- ResolveDestination ---

    [Fact]
    public void ResolveDestination_Locode_ResolvesPort()
    {
        var port = _repo.ResolveDestination("SEGOT");
        port.Should().NotBeNull();
    }

    [Fact]
    public void ResolveDestination_LocodeWithSpace_ResolvesPort()
    {
        var port = _repo.ResolveDestination("SE GOT");
        port.Should().NotBeNull();
    }

    [Fact]
    public void ResolveDestination_ByName_ResolvesPort()
    {
        var port = _repo.ResolveDestination("GOTHENBURG");
        port.Should().NotBeNull();
    }

    [Fact]
    public void ResolveDestination_NullOrEmpty_ReturnsNull()
    {
        _repo.ResolveDestination(null).Should().BeNull();
        _repo.ResolveDestination("").Should().BeNull();
        _repo.ResolveDestination("  ").Should().BeNull();
    }

    [Fact]
    public void ResolveDestination_NonExistent_ReturnsNull()
    {
        _repo.ResolveDestination("XYZNONEXISTENT").Should().BeNull();
    }
}
