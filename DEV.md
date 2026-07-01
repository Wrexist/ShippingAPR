# ShippingAPR - Claude Code Guide

## Project Overview
Real-time AIS ship tracking desktop application built with WPF (.NET 8) and Mapsui maps. Streams live vessel positions from a selectable AIS provider (aisstream.io WebSocket by default, with Datalastic and DataDocked REST polling alternatives) and displays them on an interactive world map. Providers sit behind `AisProviderFactory`, and `FallbackAisProvider` can auto-switch to a configured backup if the primary fails.

## Architecture
```
src/
  ShippingAPR.Core/          # Domain models, calculations, interfaces (zero dependencies)
  ShippingAPR.Infrastructure/ # AIS providers (AisStream WebSocket, Datalastic/DataDocked REST),
                              #   provider factory + fallback, VesselFinder enrichment, weather, port data
  ShippingAPR.Services/       # Business logic (VesselStore, VesselTrackingService, AreaMonitor, +~20 more)
  ShippingAPR.App/            # WPF UI layer (MVVM ViewModels + XAML Views)
tests/
  ShippingAPR.Core.Tests/           # xUnit + FluentAssertions
  ShippingAPR.Infrastructure.Tests/ # xUnit + Moq
  ShippingAPR.Services.Tests/       # xUnit + Moq
  ShippingAPR.App.Tests/            # xUnit (headless ViewModel tests)
```

## Build & Test Commands
```bash
# Build entire solution (solution file is at the repo root)
dotnet build ShippingAPR.sln

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
AIS provider (AisStreamClient / Datalastic / DataDocked, via AisProviderFactory + FallbackAisProvider)
  → VesselTrackingService → VesselStore
  → Events (VesselAdded/Updated) → MapViewModel (batched) → Mapsui render
                                  → VesselListViewModel (debounced) → UI list
```

## API Keys
- **Never commit real API keys**. Use `appsettings.Development.json` or `appsettings.Local.json` (both gitignored)
- A key for the active AIS provider is required for ship data; VesselFinder key optional for enrichment
- Provider keys live in `appsettings.json` under `AisStream:ApiKey`, `Datalastic:ApiKey`, `DataDocked:ApiKey`, and `VesselFinder:ApiKey`
- Keys entered at runtime (welcome dialog / settings) are saved to `%APPDATA%/ShippingAPR/appsettings.Local.json` (off the install dir) and override `appsettings.json`
- `AisProvider:Active` selects the primary provider; `AisProvider:Fallback` (empty by default) optionally names a backup `FallbackAisProvider` switches to on failure

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
