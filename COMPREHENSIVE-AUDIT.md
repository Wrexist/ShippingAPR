# ShippingAPR — Comprehensive Audit & Implementation Roadmap

> **Scope.** A full-application audit covering every layer: Core domain & calculations,
> Infrastructure (AIS providers, enrichment, weather, ports), all 23 Services, all 18
> ViewModels and 19 panels, the map/rendering engine, the app shell/DI/config/lifecycle,
> and cross-cutting quality (tests, performance, accessibility, localization, theming).
> Produced by 10 parallel specialist audits, each reading the actual source and citing
> `file:line`. **This document changes no code** — it is the prioritized plan.
>
> **Companion file.** `AUDIT-FINDINGS.md` holds the earlier 11-section structural audit
> (concurrency, secrets, coordinates, etc.) whose High/Med items were already fixed on this
> branch. This document is broader: it covers **every feature and page**, finds bugs **and**
> missing features, and lays out **phased steps to fix/implement everything**.

---

## 0. Executive summary

ShippingAPR is **far more capable than its README** — collision risk, geofences, heatmap,
emissions, chokepoints, route projection, encounters, shipments, achievements, vessel
comparison and more are already built. The backend/service/calculation layers are
well-structured, well-tested, and have disciplined error handling. The concentrated risk
is in three places: **(1) a cluster of high-severity functional bugs**, **(2) features that
are implemented but dead/unwired**, and **(3) data-integrity issues where the app fabricates
data a user would trust.** The single biggest *structural* gap is **no persistence/database**,
which gates nearly every premium analytics capability.

### By the numbers
- **2 Critical** (data fabricated and presented as real), **~25 High**, **~60 Medium**, plus many Low.
- **8 features are built but unreachable or inert**: Vessel Comparison, map Bookmarks, track Playback, Satellite/Nautical basemaps, Weather Alerts, Min-speed/Status filters, several progress bars, `RarestTypeText`.
- **3 services fabricate data**: Maritime Incidents (hardcoded), Tides (wave-height proxy), CII rating (invented bands).
- **Presentation debt**: ~230+ hardcoded UI strings (12 panels 0% localized despite 127-key EN/SV parity), 47 hardcoded hex colors that break light theme, near-zero accessibility metadata.
- **Strategic gap**: no database → no durable history, no replay, no time-series analytics, no anomaly/dark-ship detection.

### The 6 things to do first (Phase 0)
1. **Fix ETA-diverging** — vessels sailing *away* report a finite "arriving" ETA.
2. **Make Class B vessels visible** — only 2 of 25 AIS message types are handled; most small/coastal craft are invisible.
3. **Surface AIS auth/subscription errors** — a bad key looks like "Connected, 0 vessels".
4. **Stop fabricating data** — label or replace hardcoded incidents, wave-as-tide, and the non-IMO CII rating.
5. **Fix the settings/first-run/persistence trio** — "Save & Restart" doesn't restart; non-AisStream first-run writes the key to the wrong place; theme/language/filters never re-apply on restart.
6. **Fix the map marker leak** — purged vessels are never removed from the map (`VesselRemoved` unsubscribed).

---

## 1. How to use this document

- **Severity** = user-impact × likelihood: **Critical** (trust/safety), **High**, **Medium**, **Low**.
- **Effort** = rough size: **S** (<½ day), **M** (1–3 days), **L** (1–2 weeks), **XL** (>2 weeks).
- Work **Phase by Phase**; within a phase, do items top-down. Run `dotnet test tests/ --configuration Release` before each commit; one concern per commit.
- Every bug below has a concrete fix; every feature has an implementation approach. Phases 0–4 are mostly *fixing/completing what exists*; Phases 5–10 are *new capability*.

---

## 2. Critical & High bug register (fix-first)

| # | Sev | Area | File:Line | Problem | Fix | Phase |
|---|-----|------|-----------|---------|-----|-------|
| B01 | **Crit** | Incidents | `MaritimeIncidentService.cs:135-188` | 5 hardcoded incidents seeded with real authority labels (UKMTO/NAVAREA) + `UtcNow`, shown as a live attributed feed; drives real 50 NM proximity alerts | Add `IIncidentDataSource` (NGA NAVAREA/UKMTO/NOAA/RSS); until then label clearly as "sample reference zones" | 0 |
| B02 | **Crit** | Tides | `TideDataClient.cs:47-110` | Uses `wave_height` as tide level; high/low/state/next-tide all derived from waves — physically meaningless | Use Open-Meteo `sea_level_height_msl`, or integrate NOAA CO-OPS/WorldTides | 0 |
| B03 | **High** | ETA | `EtaCalculator.cs:50-55` | Vessel heading 90–180° away from destination reports a finite "arriving" ETA (negative cos floored to +0.1) | Return `null`/flag diverging when `cosDeviation <= 0` | 0 |
| B04 | **High** | AIS coverage | `AisMessageMapper.cs:51-59`, `AisStreamClient.cs:26,391` | Only PositionReport + ShipStaticData handled/subscribed; **all Class B vessels invisible** | Add `StandardClassBPositionReport`/`ExtendedClassBPositionReport`/`StaticDataReport` to filter + mapper; default Class B nav-status to Undefined(15) not 0 | 0 |
| B05 | **High** | AIS errors | `AisStreamClient.cs:363-383` | aisstream `{"error":...}` frames (bad key / malformed sub) silently dropped; socket stays open → "Connected, 0 vessels" | Detect `error` property in `ProcessMessage`, log + set `Error` status | 0 |
| B06 | **High** | Weather alerts | `WeatherAlertService.cs:44`; `App.xaml.cs:226` | `CheckVesselWeatherAsync` is never called in production — feature inert | Convert to `BackgroundService` + `PeriodicTimer`; `AddHostedService` | 0 |
| B07 | **High** | Collision | `CollisionRiskService.cs:66-72,100` | Risk dedup timestamp set once at first-seen, never refreshed; cleanup uses first-seen → fixed 5-min re-alert cadence + suppresses recurring risk | Track/refresh `LastSeen`; expire on N missed scans; re-alert only on clear→risk transition | 0 |
| B08 | **High** | Settings | `SettingsDialog.xaml.cs:30-32` / `.xaml:173` | "Save & Restart" never restarts; `IOptions` snapshots stay stale so key/provider/poll changes don't apply | Actually restart (`Process.Start`+`Shutdown`) **or** adopt `IOptionsMonitor` + relabel | 0 |
| B09 | **High** | First-run | `App.xaml.cs:241,249`; `MainViewModel.cs:375-377`; `WelcomeDialog.xaml.cs:58-63` | First-run key check & save hardcoded to AisStream; `SelectedProvider` discarded → non-AisStream key written to wrong section, wrong active provider | Save to selected provider's section + set `AisProvider:Active` | 0 |
| B10 | **High** | Persistence | `App.xaml.cs:210`; `App.xaml:8` | `IsDarkTheme` loaded but never applied; startup always Dark | Apply theme dictionary from prefs before showing window | 0 |
| B11 | **High** | Persistence | `App.xaml.cs:210`; `MainViewModel.cs:62,307` | `prefs.Language` loaded but culture/VM never set at boot; always English | Set culture + `CurrentLanguage` from prefs at startup | 0 |
| B12 | **High** | Persistence | `MainWindow.xaml.cs:112-118`; `FilterViewModel.cs:44` | Type filters + MaxSpeed saved on close but never restored into the VM → lost every restart | Load prefs into `FilterViewModel` at startup | 0 |
| B13 | **High** | Map leak | `MapViewModel.cs:147-149`; `VesselStore.cs:124` | Never subscribes `VesselRemoved`; purged vessels stay on the map forever (unbounded marker leak) | Subscribe `VesselRemoved`; remove feature; rebuild; unsubscribe in Dispose | 0 |
| B14 | **High** | Map memory | `EncounterJournalService.cs:18,42` | `_seenMmsis` only `TryAdd`, never pruned; the one truly unbounded per-MMSI set (24/7 leak) | Subscribe `VesselRemoved` → `TryRemove`, or time-windowed set | 0 |
| B15 | **High** | Map playback | `MapViewModel.cs:1254-1274`; `MapView.xaml:214` | Scrubbing mutates the live feature, overwritten every 250ms (jitter); no UI button enters playback → feature dead | Render playback on a dedicated layer; pause live writes for that MMSI; add toggle button | 1 |
| B16 | **High** | Basemaps | `MapViewModel.cs:1219-1223`; `MapView.xaml:134-143` | "Satellite", "SeaMap", "Standard" all fall through to OSM — 3 advertised buttons are silent no-ops; `CurrentMapLayer` lies | Add real cases (ESRI imagery; OpenSeaMap transparent overlay; Standard→OSM) | 1 |
| B17 | **High** | Comparison | `MainWindow.xaml` (absent); `VesselComparisonViewModel.cs:26` | Panel never hosted in any window; `SetVessels` never called → feature unreachable | Host panel + tab; wire two-vessel selection to `SetVessels` | 1 |
| B18 | **High** | Vessel list | `VesselListPanel.xaml:35-44` | `SelectedItem` binds `string SortBy` to `ComboBoxItem` items → binding never updates; sort dropdown no-ops | Use `SelectedValue`/`SelectedValuePath` with string items | 1 |
| B19 | **High** | Vessel list | `VesselListViewModel.cs:99-140` | Collection updated in place / appended; never reordered → list never re-sorts | Reorder via `Move`, or rebuild on sort/refresh | 1 |
| B20 | **High** | Vessel detail | `VesselDetailViewModel.cs:35`; `Vessel.cs:5` | `Vessel` is a POCO (no INPC); detail props refresh only on reselection → live position/speed/ETA frozen | Subscribe `VesselUpdated`; if MMSI==selected, re-raise computed props on UI thread | 1 |
| B21 | **High** | Shipment | `ShipmentPanel.xaml:139-143` | Progress bar uses `MultiBinding Converter={x:Null}` (can't produce a value) + 0-100 used as raw px | Replace with `ProgressBar` or percent-of-parent converter | 1 |
| B22 | **High** | Emissions/CII | `EmissionsEstimatorService.cs:170-203` | CII A–E bands are flat global guesses (not per-type IMO reference lines); DWT is a geometric guess; 'C' when stationary | Implement MEPC.354(78) per-type dd-boundaries **or** rename to "relative carbon-intensity estimate" | 0 |
| B23 | **High** | Datalastic | `DatalasticClient.cs:38-45` | `/vessel_find?params.min_latitude=…` + `X-Api-Key` header don't match Datalastic's contract (query-param `api-key`, `vessel_inarea`) → 401/400 every poll → Failed | Verify against real API; fix endpoint/params/auth | 4 |
| B24 | **High** | REST sentinels | `DatalasticClient.cs:67-71`, `DataDockedClient.cs:71-77` | No lat/lon/sog/cog sentinel handling: missing fields → `(0,0)` Null Island plotted; 102.3/360 literal; out-of-range throws→swallowed→dropped | Route REST providers through shared sentinel/validation (same as AisMessageMapper) | 4 |

> The remaining High items are **presentation-layer breadth** (localization, theming, accessibility) and are batched in **Phase 8**.

---

## 3. Special section: data-integrity (fabricated data)

Three features present fabricated or physically-invalid data with authoritative framing. These
are the **highest-trust-risk** items and should be fixed or clearly labelled before anything else
ships to users.

| Feature | What's fabricated | Where | Action |
|---------|-------------------|-------|--------|
| Maritime Incidents / News | 5 hardcoded incidents with `Source="UKMTO"/"NAVAREA I"/"MDAT-GoG"/"SMHI"` and `Timestamp=UtcNow`, rendered as "just now" live advisories; drives real proximity alerts | `MaritimeIncidentService.cs:135-188`; `MaritimeNewsViewModel.cs:94-97` | Back with a real source **or** relabel as "static reference zones / sample data" and stop attributing to authorities |
| Tides | Tide level/high/low/state derived from **wave height**, not sea level | `TideDataClient.cs:47-110` | Use `sea_level_height_msl`; for accuracy integrate NOAA CO-OPS/WorldTides. Also delete the false "falls back to a simple tidal model" doc comment (`:14`) |
| CII rating | Flat A–E bands invented for all ship types; DWT is a geometric guess (5000t default); 'C' when stationary | `EmissionsEstimatorService.cs:170-203` | Implement IMO MEPC.354(78) per-type reference lines, or rename to a clearly-estimated metric |
| Port congestion | 0–1 "congestion" score from an arbitrary heuristic (`min(1, inPort/max(traffic*0.3,5))`) with no capacity baseline | `PortActivityService.cs:191-194` | Anchor to berth/anchorage capacity or label as a rough heuristic |
| Weather overlay | Missing `wave_height`/`visibility` fields silently become 0 → renders "0 m waves" / "0 km visibility (fog)" as real | `OpenMeteoMarineClient.cs:46,75,146` | Distinguish missing-field from value-0; render as unknown |

---

## 4. Inventory of dead / unwired features

These are **already implemented** and need only wiring/surfacing — exceptionally high ROI.

| Feature | State | Where | To enable |
|---------|-------|-------|-----------|
| Vessel Comparison | Panel + VM exist; never hosted, `SetVessels` never called | `VesselComparisonViewModel.cs:26`; `MainWindow.xaml` | Add tab/slot; wire multi-select "Compare" → `SetVessels` |
| Map Bookmarks | Model, 3 commands, persistence all exist & correct; **zero XAML references** | `MapViewModel.cs:113,1280-1312`; `UserPreferences.Bookmarks` | Add a bookmarks dropdown/panel bound to `Bookmarks` + commands |
| Track Playback | Slider exists but no button to enter mode; fights the live loop | `MapViewModel.cs:1243,1254-1274`; `MapView.xaml:214` | Add toggle button; dedicated playback layer (B15) |
| Satellite / Nautical basemaps | Buttons exist; `SwitchMapLayer` ignores them | `MapView.xaml:134-143`; `MapViewModel.cs:1219` | Add tile-layer cases (B16) |
| Weather Alerts | Service built, registered, eagerly constructed; scan never invoked | `WeatherAlertService.cs:44` | Make it a hosted service (B06) |
| Min-speed / Status filter | VM properties + filter logic exist; no UI controls | `FilterViewModel.cs:30,42`; `FilterPanel.xaml:95` | Add range slider + status dropdown |
| `RarestTypeText` (Encounters) | Computed, never bound | `EncounterJournalViewModel.cs:47` | Bind in the stats bar |
| `AlertRule` enable/disable | `ToggleRule` command exists; no UI control | `AlertRulePanel.xaml` | Add per-row toggle |
| Multi-box / global subscription | `BoundingBox.WorldRegions` (5 boxes) defined; only one box ever sent | `AisStreamClient.cs:390`; `BoundingBox.cs:40-59` | Support `IReadOnlyList<BoundingBox>` end-to-end |

---

## 5. Full findings by domain

> Condensed but complete; each row retains `file:line`. Items already in §2/§3/§4 are not repeated.

### 5.1 Core domain & calculations
**Bugs:** anti-meridian `BoundingBox` unconstructable (`BoundingBox.cs:13-15,27`); `BoundingBox.Global`
caps at 70°N/-60°S, dropping Arctic/Southern routes (`:53`); `VesselPosition` validates lat/lon but
**not** SOG/COG/heading/ROT (`VesselPosition.cs:36-40`); `Vessel.UpdateStaticData` mutates without the
`_trackLock` used elsewhere (`Vessel.cs:76-80`); `HeatmapGrid.AddPoint` off-by-one at max edge +
divide-by-zero on degenerate grid (`HeatmapCell.cs:25-31`); `CiiRating` is an unvalidated `char`
(`EmissionEstimate.cs:17,44`); two Earth models (3440.065 nm vs 60.0 nm/°) and duplicated radius const;
ETA "arrived" branch returns hardcoded `0` distance/course (`EtaCalculator.cs:38`).
**Features:** AIS ROT decode (`4.733·√` formula, −128=null); nav-status helper predicates;
vessel dimension reference point (A/B/C/D offsets) for accurate icon footprint; draught/air-draught +
under-keel via tide; position dead-reckoning between fixes; `VesselPosition.Age()`/`IsStale()`;
rhumb-line calculator; centralize thresholds; immutable `Vessel` with snapshot reads.

### 5.2 AIS providers & mapping
**Bugs (beyond B04/B05/B23/B24):** `RateOfTurn` mapped as decoded °/min but is raw AIS int
(`PositionReportMessage.cs:35`; `AisMessageMapper.cs:96`); REST providers only check `mmsi<=0`, no
`MmsiValidator`, and **re-emit full position+static every poll** with no change-detection
(`DatalasticClient.cs:65`, `DataDockedClient.cs:69`); **no pagination** → truncated vessel sets on big
boxes (`DatalasticClient.cs:60`); `FallbackAisProvider` never restores primary and is terminal if
fallback also fails (`FallbackAisProvider.cs:68,115`); same-instance primary==fallback double-subscribes
events (`AisProviderFactory.cs:26`); `ActiveProviderName` returns "Primary"/"Fallback" not the real name
(`:26`); `MessageCount` counts only *mapped* frames (`AisStreamClient.cs:370-374`); only one bbox ever
sent (`:390`); ETA day-29-31 for short months throws→null (`AisMessageMapper.cs:110-127`).
**Features:** ATON/BaseStation/Safety handling; per-provider capability matrix + auto primary-restore;
multi-area subscription; health metrics + `Retry-After`/rate-limit awareness; configurable server-side
type/MMSI filters; raw-frame persistence/replay; message-timestamp propagation; fail-fast on 401/403.

### 5.3 Vessel data services
**Bugs:** ETA recalc gated on `TrueHeading` but ETA consumes `CourseOverGround`
(`VesselTrackingService.cs:194,261-269`); port cache eviction is hash-order not LRU (`:287-307`);
**CSV/formula injection** — AIS name/dest starting `=+-@` executes in Excel (`ExportService.cs:146-151`);
CSV `Escape` ignores `\r` (`:148`); CSV `ShipType` unescaped + uses `StaticData?.ShipType` not `Vessel.Type`
(`:29,39`); CSV written UTF-8 **no BOM** → non-ASCII mojibake; whole export built in memory on UI thread
(`:26,50`); enrichment `DropOldest` leaves `_enrichmentRequested=true` → vessel **never re-enriched**
(`VesselTrackingService.cs:186,250`); **watchlist/fleet non-atomic write → corrupt-on-crash → `Load`
silently starts empty (total loss)** (`WatchlistService.cs:148`, `FleetService.cs:108`); fleet `AddGroup`
accepts auto-groups but `Save` writes Custom-only → silent loss (`FleetService.cs:38,106`); spotlight
returns orphaned purged vessel ≤1h (`SpotlightService.cs:16,29`); **track ring buffer never contains the
current position → GPX/KML export lags one fix; 1-update vessel exports empty `<trkseg>`** (`Vessel.cs:54-74`;
`ExportService.cs:87`); search is O(n) per keystroke, unthrottled, numeric query won't match names
(`VesselStore.cs:77-103`; `SearchViewModel.cs:42`); mass-purge posts thousands of `VesselRemoved` events
(`VesselStore.cs:114-129`); statistics multi-pass not self-consistent (`StatisticsService.cs:31-60`).
**Features:** **persistence layer (SQLite/LiteDB)** for track history & vessel state; trip/voyage recording;
per-vessel tags & notes; saved searches / advanced filtering; CSV import; scheduled export; time-window
analytics; fleet export/import; IMO-based identity merge; atomic `Vessel.TrySnapshot()`; inject
`IFileStore`/`TimeProvider` for testability; `MemoryCache` for ports.

### 5.4 Safety, alerting & monitoring (beyond B06/B07)
**Bugs:** collision is O(n²) full pairwise with no spatial index (`CollisionRiskService.cs:74-86`); no
position-age filter → stale vessels projected as current → phantom alerts (`:55-58`); AlertEngine is
single-vessel AND-only — no OR/NOT, no temporal/dwell, no speed-anomaly/dark-ship/loitering
(`AlertEngine.cs:57-64`; `AlertRule.cs:22-56`); AlertRule zone bbox can't span anti-meridian & speed
rules "pass" when position null → speed alert on position-less add (`AlertRule.cs:29-37,50-52`); geofence
keyed by non-unique `Name` (empty/dup overwrite) (`AreaMonitorService.cs:69-70,97`); **no debounce/hysteresis
→ edge-hovering vessel flaps enter/leave** (`:109,132,166,180`); chokepoint transit counted on entry,
jitter re-entry double-counts (`ChokepointMonitorService.cs:90-94`); chokepoint boxes coarse (`:32-58`);
weather-alert dedup per-vessel not per-area + `_activeAlerts` uncapped (`WeatherAlertService.cs:79-96,140`);
notification history single FIFO(100), no dedup/ack (`NotificationService.cs:70-80`);
`SetMonitoredArea(null)` throws despite nullable param (`AreaMonitorService.cs:61-64`); CPA "0m" for
sub-minute TCPA (`CollisionRiskService.cs:111`).
**Features:** richer rule logic (AND/OR/NOT, schedules, watchlist-only); speed-anomaly/loitering/dark-ship
(needs history); restricted-area + draught/UKC rules; CPA bow-crossing range; **polygon geofences**;
alert acknowledgement & snooze; external channels (toast/email/webhook); alert persistence & history export;
configurable collision params in UI; shared spatial index for all monitors.

### 5.5 Enrichment & external data (beyond B01/B02/B22)
**Bugs:** SOx/NOx single hardcoded constants regardless of fuel/Tier (`EmissionsEstimatorService.cs:17,19`);
emissions instantaneous-only, fleet "Total*PerHour" sums rates (`EmissionEstimate.cs:11-17`); **heatmap
buckets `(int)`-truncate negative coords (~11 km S/W bias) and `AddPoint` ignores accumulated count
→ measures cells-visited not density** (`HeatmapService.cs:39-89`); heatmap 9-cell sum ÷5 inflates ~1.8×
(`:112`); OpenMeteo no 429/5xx mapping, unthrottled `steps²` fan-out trips rate limits (`:59-98,108-117`);
VesselFinder 429 not retried (`VesselFinderClient.cs:58-67`), `imo.GetInt32()` throws on string/absent →
drops all enrichment (`:84-90`); port arrival/departure no hysteresis/dwell/speed-gate → flapping
(`PortActivityService.cs:27,102-119`); **voyage narrative hardcodes "°N/°E" — wrong for S/W positions**
(`VoyageNarrativeService.cs:181,234`); achievements unlocked by unsanitized SOG incl. 102.3 sentinel
(`AchievementService.cs:84-87`) and `Save()` on every update (`:134`); shipment auto-assign on fuzzy
destination-name only → wrong-vessel hijack (`ShipmentTrackingService.cs:54-66`), straight-line progress/ETA
(`:157-199`); **port DB only 80 ports → `FindNearest` attributes far-away ports** (`ports.json`);
`RemoveExpired()` never called (`MaritimeIncidentService.cs:125-133`).
**Features:** real incident/news source; weather raster tiles; real tides; per-voyage emissions totals;
IMO-correct CII; persistent enrichment cache; full UN/LOCODE port DB + max-distance guard; more achievements
+ anti-cheat; shipment ETA via sea-route/provider; Polly resilience policies on all HTTP clients.

### 5.6 Map & rendering (beyond B13/B15/B16)
**Bugs:** highlight trail/projection drawn once from stale snapshot, never refreshed (`MapViewModel.cs:500-507`);
cluster click does nothing (no MMSI/centroid → no zoom-expand) (`:239,750-769`); measurement reports
great-circle NM but draws straight Mercator rhumb line — label ≠ path, single-segment only (`:1139-1169`);
heatmap/weather grids use `SelectedArea` not live viewport → offset after pan (`:1049-1055,966-968`);
**vessel rotation uses `TrueHeading ?? 0` (no COG fallback) → many ships point due north** (`:812`);
`TrailAlpha` config dead (`:204-206`); selection label hardcodes °N/°E (`:418`); `SwitchMapLayer` removes
positional `First()` (fragile) (`:1216`); Map/TileLayers never disposed + lambda overlay handlers
unsubscribable (`:150-156,1314`).
**Features:** vessel labels at zoom; **ship-shape polygons from real LOA×Beam** (dims unused); COG/heading
toggle; multi-vessel time replay; range rings; viewport culling for 10k+; follow-vessel; map screenshot;
historical density layer; mini-map; weather refresh on pan.
**Perf:** full re-render of *all* features every 250ms (no culling); `UpdateClusters` O(N) + per-feature
store lookup each tick; per-vessel `Point`/`SymbolStyle` alloc (40k/s at 10k vessels); no spatial index.

### 5.7 Feature ViewModels & panels (beyond B17–B21)
**Bugs:** AlertRule type ComboBox binds `VesselType?` to `string` collection (fragile coercion)
(`AlertRulePanel.xaml:60-63`); Achievement progress bar is an empty `Border` (no fill) (`:33-36`); Port
congestion bar has no width binding (`PortDashboardPanel.xaml:47-52`); VesselComparison hint uses
unsupported `ConverterParameter=Invert` on `BoolToVis` (`:18`); NotificationCenter has no per-item read/ack,
`ClearAll` desyncs from service history, `UnreadCount` counts while open (`NotificationCenterViewModel.cs:40-50`);
PortDashboard unobserved fire-and-forget tide refresh (`:86`); list panels `Clear()`+rebuild every tick
→ selection loss/flicker (Encounter/Shipment/News/AlertRule/Geofence).
**Features:** make Comparison work + metric deltas; sortable columns + "watched only"; filter persistence +
saved presets; VesselDetail live refresh + actions (center/watch/export); notification ack + click-to-locate;
achievement progress bars (data already computed); shipment CRUD + port autocomplete; geofence editing UX
(true viewport bounds, rename/color/edit); AlertRule enable/disable + last-fired; clickable news/chokepoint →
center map; empty/error states for all panels.

### 5.8 App shell, config & lifecycle (beyond B08–B12)
**Bugs:** `reloadOnChange:true` configured but only `IOptions` consumed → reload is dead, no setting applies
live (`App.xaml.cs:81,92`); poll-interval unvalidated → 0 busy-loops/ban, negative throws→Failed
(`SettingsDialog.xaml:89-103`; `PollingAisProviderBase.cs:104`); `DestinationFilter`/`FlagFilter` persisted
but never written (`MainWindow.xaml.cs:112-118`); window maximized state not persisted → oversized restore
(`:104-107`); restored Left/Top not bounds-checked → off-screen (`:25-30`); keyboard `+/-` unmodified, numpad
`+` unbound, `Ctrl+W` overrides "close" (`:36-39`); no single-instance guard; no PerMonitorV2 DPI manifest;
non-atomic prefs/local-settings writes (`UserPreferences.cs:109`; `LocalSettingsStore.cs:37`);
`async void OnExit` may not drain hosted services (`App.xaml.cs:354`).
**Features:** expose full options in Settings (collision/stale/batch/cluster/colors); `IOptionsMonitor`
live-reload; real "Test connection"; settings import/export + profiles; session restore (viewport/tab/selection);
auto-update; open-logs/diagnostics; command palette; resolve SettingsVM via DI.

### 5.9 Cross-cutting quality
**Memory (24/7):** B14 plus `WeatherAlertService._alertedVessels` (`:20`) and `AreaMonitorService._previousState`
(`:30`) not pruned on removal.
**Tests:** Infrastructure REST clients (Datalastic/DataDocked) and weather/tide clients **untested**;
`PollingAisProviderBase` state machine, `AisProviderFactory`, `WindSpeedToBeaufort`/`GetDouble` untested; 14/18
ViewModels untested; **no coverage gate in CI**.
**Errors/Logging:** one stray `Debug.WriteLine` (`WelcomeDialog.xaml.cs:40`); otherwise disciplined.
**Accessibility (High):** only 7 `AutomationProperties.Name` (all in title bar); ~10 emoji icon-only nav
buttons unreadable to screen readers (`MainWindow.xaml:262-289`); no `TabIndex`/access keys anywhere.
**Localization (High):** EN/SV parity perfect (127 keys) but **only 7/19 views use it** — ~230+ hardcoded
literals across 12 fully-unlocalized panels.
**Theming (High):** **47 hardcoded hex + 6 `Foreground="White"`** break light theme; `DashboardPanel.xaml`
hardcodes `#4CAF50` etc. though the theme already defines distinct `CargoColor`/`TankerColor` per theme;
dead `Glass*ScrollBarStyle` keys in Dark only.
**CI:** add coverage collection + threshold; code signing tracked separately.

---

## 6. The phased implementation roadmap

Each phase lists **goal → steps (with how-to) → acceptance**. Effort tags are per-step.

### Phase 0 — Critical correctness & data integrity *(highest priority; ~1–2 weeks)*
**Goal:** stop wrong/fabricated data and the highest-impact functional failures.
1. **ETA diverging (B03, S).** In `EtaCalculator.Calculate`, after computing `cosDeviation`, `if (cosDeviation <= 0) return null;` (or return an `EtaResult` flagged `IsDiverging`). Add tests for 90/120/170/180° deviations.
2. **Class B vessels (B04, M).** Add `StandardClassBPositionReport`, `ExtendedClassBPositionReport`, `StaticDataReport` to `DefaultMessageTypeFilters` and to `AisMessageMapper.Map`'s switch with dedicated mappers (reuse sentinel logic). Default Class B nav-status to `Undefined`. Add mapper tests with sample Class B JSON.
3. **AIS error frames (B05, S).** In `ProcessMessage`, before mapping, `if (root.TryGetProperty("error", out var e)) { _logger.LogError(...); SetStatus(Error); return; }`. Surface a distinct "stream rejected / check key" status.
4. **Data fabrication (B01/B02/B22, M–L).** Either integrate real sources or **relabel**: incidents → "sample zones" + remove authority `Source` strings; tides → switch to `sea_level_height_msl`; CII → rename to "carbon-intensity estimate (non-IMO)". Each is independently shippable; do the relabel now, real-source later (Phase 6).
5. **Weather alerts inert (B06, S).** Convert `WeatherAlertService` to `BackgroundService` with a `PeriodicTimer` (5–10 min) calling `CheckVesselWeatherAsync`; register `AddHostedService`. Add a scan unit test (already mostly there).
6. **Collision re-alert (B07, M).** Store `LastSeen` per pair; refresh every scan a pair is still at risk; expire after N missed scans; re-alert only on a clear→risk transition. Inject `TimeProvider` for testability. Add a converging-then-clearing-then-converging test.
7. **Settings/first-run/persistence trio (B08–B12, M).** (a) Make "Save" honest — either restart or adopt `IOptionsMonitor` (see Phase 3) and relabel; (b) route first-run key to the selected provider's section + set `AisProvider:Active`; (c) after `prefs.Load()`, apply the theme dictionary and thread culture and set `MainViewModel.IsDarkTheme/CurrentLanguage` before `MainWindow.Show()`; (d) restore `FilterViewModel` from prefs at startup and write `Destination/Flag` filters on close.
8. **Map marker leak + EncounterJournal leak (B13/B14, S).** Subscribe `VesselRemoved` in `MapViewModel` (remove feature, `_featuresNeedRebuild=true`, rebuild, unsubscribe in Dispose) and in `EncounterJournalService`/`WeatherAlertService`/`AreaMonitorService` (`TryRemove(mmsi)`).
9. **CSV injection + watchlist/fleet atomic writes (S).** Prefix dangerous leading chars (`= + - @ \t \r`) with `'` in `ExportService.Escape`; switch `WatchlistService`/`FleetService`/`UserPreferences`/`LocalSettingsStore` to temp-write+`File.Replace` with corrupt-file quarantine.

**Acceptance:** no feature presents fabricated data without a label; ETA/Class B/error-state covered by tests; a long session no longer accumulates map markers or per-MMSI state; settings & restart behave as labelled.

### Phase 1 — Wire up dead features & quick wins *(~1 week, mostly S)*
Surface what already exists: **Vessel Comparison** (B17), **Bookmarks** (§4), **Satellite/Nautical basemaps** (B16), **Track Playback** (B15), **Min-speed/Status filters**, **AlertRule toggle**, **`RarestTypeText`**, **clickable news/chokepoint → center map**. Fix the **vessel-list sort** (B18/B19), **vessel-detail live refresh** (B20), and the broken **progress bars** (B21, Achievement, Port congestion). Each is a localized XAML/VM change; add a smoke test where a VM is involved.

**Acceptance:** every panel reachable; every advertised button does something; sort & live refresh work; no decorative-only progress bars.

### Phase 2 — Persistence foundation (SQLite/EF Core) *(L; unlocks Phases 6–7)*
**Goal:** durable storage — the central constraint.
1. Add `Microsoft.Data.Sqlite` (or LiteDB) behind an `IPositionStore`/`IHistoryStore` interface in Infrastructure.
2. Background-flush track points + last-known vessel snapshots (batch every N s); retention policy (configurable days).
3. Persist watchlist/fleets/encounters/shipments/alerts/notifications to the DB (replacing fragile JSON snapshot files); migrate existing JSON on first run.
4. Reload last-known vessels + recent tracks on startup.

**Acceptance:** restart preserves track history, watchlist, fleets, encounters, shipments, fired alerts; DB size bounded by retention.

### Phase 3 — Robustness & reliability hardening *(M)*
`IOptionsMonitor` live-reload for hot options (poll intervals, UI timings, collision thresholds) → settings apply without restart; clamp/validate poll intervals & colors/URLs/enums in `ValidateConfiguration`; single-instance mutex; PerMonitorV2 DPI manifest; window maximized-state + off-screen bounds checks; Polly resilience (timeout + jittered retry on 429/5xx honoring `Retry-After` + circuit breaker) on **all** HTTP clients; bound OpenMeteo grid parallelism; fail-fast on 401/403; atomic writes everywhere; explicit VM disposal in `OnExit`.

### Phase 4 — AIS completeness & providers *(M–L)*
Fix Datalastic contract (B23) + REST sentinel/validation/dedup/pagination (B24); FallbackAisProvider primary-restore + capability matrix + real `ActiveProviderName`; multi-box/global subscription; ATON/BaseStation/Safety handling; configurable server-side type/MMSI filters; message-timestamp propagation; raw-frame ring buffer for replay/tests; count raw vs mapped frames.

### Phase 5 — Map & visualization upgrades *(M–L)*
Viewport culling for 10k+ (the dominant scalability fix); ship-shape polygons from real dimensions; vessel labels at zoom; COG/heading toggle; cluster click-to-expand; correct measurement (geodesic polyline or rhumb label); range rings; follow-vessel; map screenshot; weather/heatmap from live viewport; OpenSeaMap ENC overlay; per-tick allocation cuts (mutate point coords in place, spatial index shared by culling/clustering/hit-test).

### Phase 6 — Analytics & intelligence *(L; needs Phase 2)*
Anomaly detection suite — **AIS gaps/dark periods, loitering, abnormal speed/course, identity/position-jump spoofing, STS-transfer (paired loitering)**; true multi-vessel historical playback; density heatmap from history (fix binning/weighting first); port-call analytics over time (turnaround/dwell/waiting, capacity-based congestion); per-voyage emissions totals + IMO-correct CII; time-window statistics & per-vessel analytics; real incident/news + tidal sources.

### Phase 7 — Alerting & notifications *(M–L)*
Rich rule logic (AND/OR/NOT, schedules, watchlist-only, speed-anomaly/dark-ship/loitering using Phase 6 state); **polygon geofences** (point-in-polygon — the map already renders polygons); CPA bow-crossing range; alert acknowledgement & snooze + per-item read state; external channels behind `INotificationChannel` (Windows toast, webhook, SMTP); geofence/area debounce-hysteresis & confirmed-exit logic; alert/notification persistence & export; configurable collision params in UI.

### Phase 8 — Presentation polish: localization, theming, accessibility *(M–High effort, breadth)*
Move ~230+ hardcoded strings (start: Dashboard, Settings, AlertRule, PortDashboard) into `Strings.resx`/`.sv.resx` and bind via `x:Static`; replace 47 hardcoded hex + `White` brushes with `DynamicResource` theme colors (Dashboard first — the theme already defines `CargoColor` etc.); add localized `AutomationProperties.Name` to every icon-only button + logical `TabIndex`/access keys; remove dead `Glass*ScrollBarStyle`; keyed-diff list refresh (no flicker/selection loss); empty/error states everywhere.

### Phase 9 — Platform & ops *(M–XL, strategic)*
Auto-update (Velopack/Squirrel) + WinGet/Chocolatey; optional in-process REST API / headless mode (feeds dashboards/automation/future mobile); observability (message-rate dashboard, structured metrics); **(XL)** cross-platform port to Avalonia (lifts the WPF/Windows-only ceiling); web/mobile read-only companion + push; multi-source AIS fusion (terrestrial + satellite).

### Phase 10 — Testing & quality gates *(ongoing, M)*
Add tests for Infrastructure REST/weather/tide clients (mocked `HttpMessageHandler` + sample payloads), `PollingAisProviderBase` state machine, `AisProviderFactory`, pure helpers (`WindSpeedToBeaufort`, `MapShipType` boundaries, ETA roll-forward); inject `TimeProvider`/`IFileStore` for deterministic, machine-clean tests; add a coverage collector + threshold gate to `ci.yml`; a headless VM-init smoke test; consider the headless-testable-ViewModels refactor (move VMs to a `net8.0` library).

---

## 7. Suggested sequencing & dependencies

```
Phase 0 (critical) ─┬─> Phase 1 (wire dead features)
                    ├─> Phase 3 (robustness)
                    └─> Phase 4 (AIS completeness)
Phase 2 (DB) ───────┬─> Phase 6 (analytics/anomaly)
                    └─> Phase 7 (alerting depth)
Phase 5 (map) independent (do alongside 4)
Phase 8 (presentation) independent (do alongside anything)
Phase 9/10 ongoing
```
- **Do Phase 0 first, in full.** It is correctness and trust.
- **Phase 1 is the best ROI** — high user-visible value for small effort (just wiring).
- **Phase 2 is the strategic unlock** — schedule it early even though it's large, because Phases 6–7 depend on it.
- Phases 5, 8, 10 can proceed in parallel tracks.

---

## 8. Appendix — metrics to verify the work

- **Coverage:** add Coverlet; target ≥70% on Core + Services, with Infrastructure REST clients explicitly covered.
- **Memory soak:** 24 h run in a busy area; working set flat (no per-MMSI growth; no map-marker growth).
- **High-count render:** 10k vessels stays interactive after viewport culling (Phase 5).
- **Localization:** 0 hardcoded user-facing literals in the 12 currently-unlocalized panels.
- **Theming:** light theme has 0 hardcoded `#hex`/`White` in views; legend colors match theme.
- **Accessibility:** every interactive control has an `AutomationProperties.Name`; full keyboard traversal.
- **Data integrity:** no screen attributes fabricated data to a real authority without a "sample/estimated" label.
