# ShippingAPR Innovation & Marketing Brainstorm

**Date:** March 2026
**Status:** Draft

## Executive Summary

ShippingAPR is a fully functional WPF (.NET 8) desktop application for real-time AIS ship tracking. It streams live vessel positions via aisstream.io WebSocket, renders them on an interactive OpenStreetMap-powered map, and provides filtering, search, area monitoring, and data export capabilities. Built on a clean layered architecture (Core / Infrastructure / Services / App), the application is well-positioned for significant feature expansion and market growth.

This document explores innovation opportunities across four dimensions: advanced tracking features, UI/UX improvements, third-party integrations, and market positioning strategies.

---

## 1. Advanced Tracking Features

### 1.1 Collision Risk Detection

**Concept:** Detect potential vessel collisions by computing Closest Point of Approach (CPA) and Time to CPA (TCPA) for pairs of nearby vessels.

**Implementation approach:**
- New `CollisionRiskService` in `ShippingAPR.Services` that periodically scans vessel pairs from `VesselStore`
- Use the existing `HaversineCalculator` (`ShippingAPR.Core.Calculations`) for distance computation
- Compute CPA/TCPA from COG and SOG vectors using linear motion projection
- Surface alerts through the existing `NotificationService` and `WeakReferenceMessenger` pattern
- Configurable thresholds: minimum CPA distance (e.g., 0.5 NM) and maximum TCPA window (e.g., 30 minutes)

**Value:** Critical safety feature for port operators and coastal surveillance users.

### 1.2 Route Prediction / Projected Path

**Concept:** Extrapolate a vessel's future path based on current course, speed, and track history.

**Implementation approach:**
- New `RouteProjectionCalculator` in `ShippingAPR.Core.Calculations`
- Leverage the existing circular track buffer (`Vessel._trackBuffer`, 200 points) for trend analysis
- Render projected paths as dashed polylines on the trail layer (`_trailLayer` in `MapViewModel`)
- Simple linear projection initially; later add curve-fitting based on recent heading changes

**Value:** Helps users anticipate vessel movements without waiting for updated AIS data.

### 1.3 Anomaly Detection

**Concept:** Flag unusual vessel behavior such as sudden speed drops, unexpected course changes, loitering patterns, or AIS gaps.

**Implementation approach:**
- Analyze the 200-point track buffer for statistical outliers (speed standard deviation, course variance)
- Define anomaly types: speed anomaly, course anomaly, loitering (repeated positions), AIS dark period
- Extend `NotificationType` enum with `AnomalyDetected` value
- Alert through existing notification infrastructure

**Value:** Valuable for security monitoring, fishing regulation enforcement, and maritime domain awareness.

### 1.4 Multiple Named Geofences

**Concept:** Upgrade from a single monitored area to multiple named geofence zones with per-zone alert rules.

**Implementation approach:**
- Upgrade `AreaMonitorService` from `BoundingBox? _monitoredArea` to `Dictionary<string, GeofenceZone>` supporting named zones
- Support both rectangular (current `BoundingBox`) and polygon-based geofences
- Per-zone configuration: entry alerts, exit alerts, dwell time alerts, vessel type filters
- UI for creating, editing, and managing geofences on the map (extend selection layer in `MapViewModel`)

**Value:** Essential for port monitoring, anchorage management, and restricted area surveillance.

### 1.5 Vessel Watchlist

**Concept:** Allow users to "favorite" specific vessels for priority tracking and notifications.

**Implementation approach:**
- Add `HashSet<int>` (MMSI set) to `VesselStore` for watched vessels
- Priority notifications when watched vessels appear, change status, or arrive at destination
- Visual differentiation on map (highlighted icon, always-visible label)
- Persist watchlist to user preferences file

**Value:** Low implementation effort, high user satisfaction. Critical for logistics users tracking specific shipments.

### 1.6 Historical Playback

**Concept:** Record vessel positions to local storage for offline replay and historical analysis.

**Implementation approach:**
- Add SQLite database via Entity Framework Core for persistent track storage
- Record all received position updates with timestamps
- Timeline scrubber UI component for playback controls (play, pause, speed, seek)
- Render historical data through the existing map layers

**Value:** Enables post-incident analysis, voyage reconstruction, and pattern research.

### 1.7 Fleet Grouping

**Concept:** Define named groups of vessels (by MMSI) with group-level analytics.

**Implementation approach:**
- New `FleetGroup` model with name, description, and MMSI list
- Group analytics: average speed, geographic spread, common destinations, group ETA
- Visual grouping on map (convex hull outline, group label)
- Persistent storage for fleet definitions

**Value:** Essential for shipping companies managing multiple vessels.

---

## 2. UI/UX Improvements

### 2.1 Dashboard / Statistics Panel

**Concept:** Real-time KPI dashboard showing aggregate vessel statistics.

**Proposed metrics:**
- Vessel count by type (using existing `VesselType` enum)
- Average speed across all tracked vessels
- Message rate and parse error rate (from WebSocket stats)
- Area density indicator
- Most common destinations
- Vessels at anchor vs. under way breakdown (using `NavigationalStatus`)

**Implementation:** New `StatisticsViewModel` consuming `VesselStore.Vessels` snapshots, rendered as a collapsible panel or overlay.

### 2.2 Heatmap Layer

**Concept:** Visualize vessel traffic density as a color-gradient heatmap overlay on the map.

**Implementation approach:**
- Add a `MemoryLayer` to `MapViewModel.InitializeMap()` alongside existing layers
- Aggregate vessel positions into a spatial grid (extend the existing cluster grid logic from `UpdateClusters()`)
- Color cells from blue (low density) through yellow to red (high density)
- Toggle visibility via UI control

**Value:** Instantly reveals shipping lanes, congestion areas, and traffic patterns.

### 2.3 Vessel Hover Tooltips

**Concept:** Show vessel name, type, speed, and destination on mouse hover without requiring a click.

**Implementation approach:**
- Leverage Mapsui's `MapInfo` hover events
- Display a lightweight tooltip popup near the cursor
- Keep click-to-select for full detail panel interaction

**Value:** Faster information discovery, especially when browsing dense traffic areas.

### 2.4 Speed-Gradient Track History

**Concept:** Color the track trail based on vessel speed at each point.

**Implementation approach:**
- The track buffer already stores `SpeedOverGround` per `TrackPoint`
- Render trail as segmented polyline with color interpolation: blue (0-5 kn) -> green (5-12 kn) -> yellow (12-18 kn) -> red (18+ kn)
- Add a legend overlay explaining the color scale

**Value:** Instantly reveals where vessels accelerated, slowed, or stopped.

### 2.5 Accessibility Improvements

**Proposed enhancements:**
- Full keyboard navigation for vessel list and filter controls
- High-contrast theme option (extend current dark/light theme system via XAML resource dictionaries)
- Screen reader support for vessel details using UI Automation properties
- Configurable font sizes for vessel list and detail panels
- Color-blind-friendly vessel type color palette option

### 2.6 Additional Localization

**Concept:** Extend beyond English and Swedish to cover the Nordic/European shipping audience.

**Languages to add:** Norwegian (nb), Danish (da), Finnish (fi), German (de)

**Implementation:** Follow the existing pattern of `Strings.resx` / `Strings.sv.resx` resource files. Each new language requires a translated `.resx` file and a flag icon.

### 2.7 Collapsible Panels & Fullscreen Map

**Concept:** Make side panels resizable and collapsible for flexible layouts.

**Proposed features:**
- Drag-to-resize vessel list and detail panels
- Collapse buttons to hide panels entirely
- Fullscreen map mode (F11 shortcut)
- Remember panel sizes across sessions

### 2.8 Notification Center

**Concept:** Replace single toast notifications with a persistent slide-out notification panel.

**Implementation approach:**
- Slide-out panel showing the full `NotificationService.History` list (already maintains up to 100 entries)
- Filter by `NotificationType` (area entry, area exit, anomaly, watchlist)
- Mark as read / clear functionality
- Badge counter on notification icon

### 2.9 Mini-Map Inset

**Concept:** Small overview map in the corner showing global view with current viewport highlighted.

**Implementation:** Second Mapsui `MapControl` instance with a fixed global extent, drawing a rectangle for the main viewport's bounds. Updates on viewport change.

---

## 3. Integration Ideas

### 3.1 Marine Weather Overlay

**Concept:** Display wind, wave height, sea state, and current data on the map.

**Data sources:** OpenWeatherMap Marine API, Windy API, NOAA GFS data

**Implementation approach:**
- New `IWeatherProvider` interface in `ShippingAPR.Core.Interfaces`
- `WeatherClient` implementation in `ShippingAPR.Infrastructure`
- Weather tile overlay or vector arrows on map
- Forecast timeline for planned routes

**Value:** Critical context for understanding vessel behavior and planning.

### 3.2 Port Congestion Data

**Concept:** Display real-time port congestion metrics.

**Proposed data points:**
- Vessels currently at anchor near port
- Average waiting time
- Berth occupancy rate
- Port status indicators on map

**Implementation:** Extend `Port` model and `PortRepository` with live congestion data from MarineTraffic or UN port data feeds.

### 3.3 Satellite Imagery Toggle

**Concept:** Switch between OpenStreetMap and satellite/hybrid tile layers.

**Implementation approach:**
- Currently `MapViewModel.InitializeMap()` calls `OpenStreetMap.CreateTileLayer()`
- Add alternative tile sources: Bing Maps Aerial, Mapbox Satellite, ESRI World Imagery
- UI toggle button for layer switching
- Cache tiles locally for offline use

### 3.4 Historical AIS Data APIs

**Concept:** Query historical vessel positions from external providers.

**Data sources:** MarineTraffic API, Spire Maritime, VesselTracker

**Value:** Enables voyage history lookup even when the app was not running. Complements local historical playback.

### 3.5 Push Notifications / External Alerts

**Concept:** Forward alerts beyond the desktop application.

**Channels:**
- Email via SendGrid or SMTP
- SMS via Twilio
- Windows Action Center via `Microsoft.Toolkit.Uwp.Notifications`
- Webhook callbacks to arbitrary URLs

**Implementation:** Extend `NotificationService` with pluggable notification channels behind a `INotificationChannel` interface.

### 3.6 REST API Server Mode

**Concept:** Expose vessel data as a REST API from a running ShippingAPR instance.

**Implementation approach:**
- Optional ASP.NET Core minimal API hosted alongside the WPF app
- Endpoints: `GET /vessels`, `GET /vessels/{mmsi}`, `GET /vessels/{mmsi}/track`, `GET /area`
- WebSocket endpoint for real-time vessel updates
- API key authentication

**Value:** Enables dashboards, automation scripts, and mobile companion apps to consume live data.

### 3.7 Vessel Photo Integration

**Concept:** Display vessel photographs in the detail panel.

**Data sources:** VesselFinder Photos API, FleetMon, ShipSpotting

**Implementation:** Extend `IVesselEnrichmentClient` with `GetVesselPhotoAsync(int mmsi)`. Cache photos locally.

### 3.8 Full UN/LOCODE Database

**Concept:** Expand from ~80 ports to the full UN/LOCODE database (~100,000 locations).

**Implementation approach:**
- Replace embedded `ports.json` with a SQLite database or downloadable data file
- Fuzzy matching for destination string resolution
- Dramatically improved ETA accuracy for less common ports

---

## 4. Market Positioning

### 4.1 Target Audiences

| Segment | Need | Key Features |
|---------|------|--------------|
| **Port Operators** | Real-time vessel monitoring, anchorage management | Geofencing, collision risk, port congestion |
| **Shipping Dispatchers** | Fleet tracking, ETA monitoring | Watchlist, fleet groups, push alerts |
| **Freight Forwarders** | Container vessel arrival tracking | Vessel search, ETA, email notifications |
| **Ship Spotters / Hobbyists** | Explore live ship traffic | Free tier, interactive map, vessel photos |
| **Maritime Researchers** | Traffic pattern analysis | Historical playback, heatmaps, data export |
| **Coast Guard / Government** | Coastal surveillance, SAR coordination | Anomaly detection, multi-zone geofencing |

### 4.2 Competitive Advantages

- **Desktop-native performance:** WPF with SkiaSharp rendering outperforms browser-based alternatives; no tab overhead, better memory management
- **Open-source and self-hosted:** No subscription lock-in to MarineTraffic ($) or VesselFinder; users control their data
- **Extensible architecture:** Clean Core/Infrastructure/Services/App layers make custom integrations straightforward
- **Low resource footprint:** Smaller memory and CPU usage compared to Electron/web-based trackers
- **Smart ETA algorithm:** Course-deviation-corrected ETA calculation is a unique differentiator
- **Offline capability:** Map tile caching enables operation in limited connectivity environments
- **Privacy:** No data sent to third parties beyond the AIS stream subscription

### 4.3 Monetization Models

| Model | Description | Target |
|-------|-------------|--------|
| **Freemium** | Free: basic tracking + map. Paid: analytics, alerts, historical playback | Individual users |
| **Enterprise License** | Self-hosted deployment with fleet management, webhooks, multi-user | Shipping companies |
| **Data Services** | Aggregated traffic pattern reports, port analytics | Port authorities, logistics |
| **API-as-a-Service** | REST API access to live vessel data from running instances | Developers, integrators |
| **Premium Data Bundles** | Bundled VesselFinder enrichment, weather, satellite imagery | Power users |

### 4.4 Growth Strategy

1. **Distribution:** Publish to WinGet and Chocolatey for developer-friendly installation
2. **Cross-platform:** Port to Avalonia UI or .NET MAUI for macOS and Linux users
3. **Mobile companion:** Read-only vessel tracker app with push notifications (Xamarin/MAUI)
4. **Plugin ecosystem:** Define extension points for community-contributed features (map layers, data sources, export formats)
5. **Community building:** GitHub Discussions, Discord server, contributor guidelines
6. **Content marketing:** Blog posts on maritime tech, AIS data analysis tutorials, YouTube demos

---

## 5. Technical Foundation Work

### 5.1 Persistent Storage

**Priority: High** — Prerequisite for historical playback, watchlists, fleet groups, and user preferences.

- Add SQLite via Entity Framework Core
- Schema: `Positions` (MMSI, lat, lon, SOG, COG, timestamp), `Watchlist` (MMSI, label), `Geofences` (name, geometry), `FleetGroups` (name, MMSIs)
- Data retention policy: configurable max age (30/60/90 days)

### 5.2 Plugin Architecture

- Define `IMapLayerPlugin`, `IDataSourcePlugin`, `IExportPlugin` interfaces
- MEF (Managed Extensibility Framework) or custom assembly loading
- Plugin manifest format and discovery mechanism
- Sandboxed execution for third-party plugins

### 5.3 Performance Profiling

- Stress-test with 5,000+ simultaneous vessels (global view scenario)
- Consider spatial indexing (R-tree) for `VesselStore` to optimize proximity queries
- Profile memory allocation in the 250ms map batch update cycle
- Optimize `UpdateClusters()` for large vessel counts

### 5.4 Automated UI Testing

- Add FlaUI or Appium-based UI test suite
- Cover critical flows: startup, API key entry, vessel selection, filtering, export
- Integration with CI pipeline

---

## 6. Priority Matrix

| Priority | Feature | Effort | Impact | Audience |
|----------|---------|--------|--------|----------|
| **P0** | Vessel Watchlist | Low | High | All users |
| **P0** | Multiple Geofences | Medium | High | Port operators, coast guard |
| **P0** | Dashboard / Statistics Panel | Medium | High | All users |
| **P1** | Collision Risk Detection | Medium | High | Safety, port operators |
| **P1** | Weather Overlay | Medium | Medium | Maritime professionals |
| **P1** | Notification Center | Low | Medium | All users |
| **P1** | Full UN/LOCODE Database | Low | Medium | ETA accuracy |
| **P1** | Hover Tooltips | Low | Medium | UX improvement |
| **P2** | Anomaly Detection | Medium | High | Security, research |
| **P2** | Historical Playback + SQLite | High | High | Research, analysis |
| **P2** | Heatmap Layer | Medium | Medium | Research, port planning |
| **P2** | Speed-Gradient Tracks | Low | Medium | Visual enhancement |
| **P2** | REST API Server Mode | Medium | Medium | Developers, integrations |
| **P2** | Vessel Photo Integration | Low | Low | Ship spotters |
| **P3** | Route Prediction | Medium | Medium | Advanced tracking |
| **P3** | Fleet Grouping | Medium | Medium | Shipping companies |
| **P3** | Cross-Platform Port | Very High | High | macOS/Linux users |
| **P3** | Plugin Architecture | High | Medium | Community growth |
| **P3** | Satellite Imagery Toggle | Low | Low | Visual preference |
| **P3** | Mobile Companion App | Very High | Medium | On-the-go users |

---

*This document is a living brainstorm. Ideas should be validated with user research and refined into formal specifications before implementation.*
