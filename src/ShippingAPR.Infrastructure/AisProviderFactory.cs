using Microsoft.Extensions.DependencyInjection;
using ShippingAPR.Core.Enums;
using ShippingAPR.Core.Interfaces;
using ShippingAPR.Infrastructure.AisStream;
using ShippingAPR.Infrastructure.Datalastic;
using ShippingAPR.Infrastructure.DataDocked;

namespace ShippingAPR.Infrastructure;

/// <summary>
/// Factory that creates <see cref="IAisDataProvider"/> instances by provider type.
/// </summary>
public sealed class AisProviderFactory
{
    private readonly IServiceProvider _serviceProvider;

    public AisProviderFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IAisDataProvider Create(AisProviderType type)
    {
        return type switch
        {
            AisProviderType.AisStream => _serviceProvider.GetRequiredService<AisStreamClient>(),
            AisProviderType.Datalastic => _serviceProvider.GetRequiredService<DatalasticClient>(),
            AisProviderType.DataDocked => _serviceProvider.GetRequiredService<DataDockedClient>(),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"Unsupported AIS provider type: {type}")
        };
    }
}
