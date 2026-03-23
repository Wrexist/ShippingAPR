# ShippingAPR - Real-Time Ship Tracker

A modern WPF desktop application for real-time ship tracking in Swedish waters and beyond, powered by live AIS data.

## Features

- **Real-time AIS streaming** via aisstream.io WebSocket API
- **Interactive map** with OpenStreetMap tiles (Mapsui)
- **Area selection** — click two points to define a tracking zone
- **Smart ETA calculation** — course-deviation-corrected arrival time estimates
- **Ship details** — MMSI, IMO, call sign, speed, heading, destination, dimensions
- **Vessel type color coding** — cargo, tanker, passenger, fishing, tug, etc.
- **Track history** — vessel trail visualization (up to 200 points)
- **Search** — find vessels by name, MMSI, or IMO
- **Filtering** — by vessel type and speed range
- **Area monitoring** — notifications when ships enter/leave your selected area
- **Dark/Light themes**
- **Bilingual UI** — English and Swedish (toggle with one click)
- **Port database** — 80+ major ports with UN/LOCODE for destination resolution

## Quick Start

### Prerequisites
- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Free API key from [aisstream.io](https://aisstream.io)

### Build & Run
```bash
git clone <repo-url>
cd ShippingAPR
dotnet restore
dotnet build
dotnet run --project src/ShippingAPR.App
```

### First Launch
1. The app will show a welcome dialog
2. Click "Open aisstream.io" to sign up for a free API key
3. Paste your API key and click "Get Started"
4. The map defaults to Gothenburg harbor — click "Start Tracking" to begin

## Architecture

```
ShippingAPR.Core           Zero-dependency domain models, calculations, interfaces
ShippingAPR.Infrastructure AIS WebSocket client, VesselFinder REST client, port database
ShippingAPR.Services       VesselStore, TrackingService, AreaMonitor, Notifications
ShippingAPR.App            WPF views, MVVM viewmodels, themes, localization
```

**Key design decisions:**
- **MVVM** with CommunityToolkit.Mvvm source generators
- **Batched UI updates** (250ms intervals) to handle 300+ AIS messages/second
- **ConcurrentDictionary** vessel store with UI-thread event dispatching
- **Auto-reconnect** WebSocket with exponential backoff
- **Course-corrected ETA**: `effectiveSpeed = SOG × cos(courseDeviation)`

## Running Tests
```bash
dotnet test
```

## Technology Stack

| Component | Technology |
|-----------|-----------|
| Framework | WPF / .NET 8 |
| Map | Mapsui 5.x (SkiaSharp) |
| MVVM | CommunityToolkit.Mvvm 8.4 |
| AIS Data | aisstream.io (WebSocket) |
| Enrichment | VesselFinder API (optional) |
| DI | Microsoft.Extensions.Hosting |
| Testing | xUnit + FluentAssertions + Moq |
