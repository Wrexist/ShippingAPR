# ShippingAPR - Real-Time Ship Tracker

A modern desktop application for real-time ship tracking in Swedish waters and beyond, powered by live AIS data.

## Download

**[Download latest release](../../releases/latest)**

1. Download the `.zip` file from the latest release
2. Extract to any folder
3. Run `ShippingAPR.exe`

No .NET installation required — everything is included. Requires Windows 10 or later (64-bit).

## Getting Started

1. Launch ShippingAPR — you'll see the welcome screen
2. Sign up for a free API key at [aisstream.io](https://aisstream.io)
3. Paste your API key and click **Get Started**
4. The map shows Gothenburg harbor by default
5. Click **Start Tracking** in the bottom bar to begin receiving live ship data

You can also click **Explore without API key** to browse the app interface first. You can add your key later from within the app.

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

## Developer Guide

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

### Running Tests
```bash
dotnet test
```

### Publishing a Release
```bash
git tag v1.0.0
git push origin v1.0.0
```
The GitHub Actions workflow will automatically build, test, and create a release with a self-contained Windows executable.

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
