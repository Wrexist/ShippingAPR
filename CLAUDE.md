# ShippingAPR - Claude Code Guide

## Project Overview
Real-time AIS ship tracking desktop application built with WPF (.NET 8) and Mapsui maps. Streams live vessel positions via aisstream.io WebSocket API and displays them on an interactive world map.

## Architecture
```
src/
  ShippingAPR.Core/          # Domain models, calculations, interfaces (zero dependencies)
  ShippingAPR.Infrastructure/ # External API clients (AisStream WebSocket, VesselFinder REST), port data
  ShippingAPR.Services/       # Business logic (VesselStore, VesselTrackingService, AreaMonitor)
  ShippingAPR.App/            # WPF UI layer (MVVM ViewModels + XAML Views)
tests/
  ShippingAPR.Core.Tests/     # xUnit + FluentAssertions
  ShippingAPR.Services.Tests/ # xUnit + Moq
```

## Build & Test Commands
```bash
# Build entire solution
dotnet build src/ShippingAPR.sln

# Run all tests
dotnet test tests/ --configuration Release

# Publish single-file executable (Windows x64)
dotnet publish src/ShippingAPR.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Key Conventions
- **MVVM pattern**: CommunityToolkit.Mvvm with source generators (`[ObservableProperty]`, `[RelayCommand]`)
- **Dependency injection**: Microsoft.Extensions.Hosting for IoC container
- **Threading**: Background services via `IHostedService`; UI updates via `SynchronizationContext` and `DispatcherTimer`
- **Batched updates**: Map renders every 250ms, vessel list refreshes every 3s (high-frequency AIS data)
- **Thread safety**: `ConcurrentDictionary` for vessel store, `lock` for track history
- **Async/await**: All network operations are async; WebSocket receive loop runs on background thread

## Data Flow
```
aisstream.io WebSocket → AisStreamClient → VesselTrackingService → VesselStore
  → Events (VesselAdded/Updated) → MapViewModel (batched) → Mapsui render
                                  → VesselListViewModel (debounced) → UI list
```

## API Keys
- **Never commit real API keys**. Use `appsettings.Development.json` or `appsettings.Local.json` (both gitignored)
- aisstream.io key required for ship data; VesselFinder key optional for enrichment
- Keys configured in `appsettings.json` under `AisStream:ApiKey` and `VesselFinder:ApiKey`

## Map Technology
- **Mapsui 5.0.0-beta.1** with SkiaSharp rendering and OpenStreetMap tiles
- Coordinate system: EPSG:3857 (Web Mercator) for display, WGS84 for data
- Use `SphericalMercator.FromLonLat()` / `ToLonLat()` for conversion
- Vessel features stored in `Dictionary<int, IFeature>` keyed by MMSI

## Project-Specific Notes
- Entry point: `Program.cs` → `App.xaml.cs` (DI setup, API key check, main window)
- Vessel identification: MMSI (Maritime Mobile Service Identity) is the unique key
- AIS message types: PositionReport (lat/lon/speed/heading), ShipStaticData (name/type/dimensions)
- Port database: ~80 ports in embedded `ports.json` resource
- Themes: Dark (default) and Light, switchable at runtime via XAML resource dictionaries
- Localization: English and Swedish via resource files
