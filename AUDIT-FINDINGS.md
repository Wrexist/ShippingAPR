# ShippingAPR — Audit Findings (Triage Pass)

> **Status: READ-ONLY triage complete. No application code was changed in this pass.**
> Produced against the framework in `AUDIT.md`. Every item below was checked against the
> current source on branch `claude/shippingapr-audit-checklist-DwXbW` with concrete
> `file:line` citations. Statuses: **OK** / **Fix now** / **Fix later** / **N/A**.
> Severity = user-impact × likelihood (High / Med / Low).

Audit method: five parallel read-only specialist passes, one per section group, each
required to cite the exact file/line it inspected. Findings consolidated and severity-sorted
below.

---

## Executive summary

The codebase is in **good structural health**. The hard things are done right:

- **Concurrency model is sound** — `ConcurrentDictionary` store, `SynchronizationContext`-marshalled
  events, `DispatcherTimer` batching, lock-guarded track ring buffer, bounded enrichment `Channel`.
  No High-severity threading bug found.
- **Coordinate handling is correct** — all projection goes through `SphericalMercator`, lon/lat
  argument order is right at every call site, distance math is consistently in NM.
- **Git history is clean** — a rigorous scan of all 1058 historical blobs found **no API key was
  ever committed**. **No key rotation is required.**
- **Layering is strict** — Core has zero dependencies, references point inward only, DI lifetimes
  are correct.
- **Multi-source AIS failover already exists** (`FallbackAisProvider` + `AisProviderFactory`),
  contradicting the "single source" assumption in the audit framework — though it's disabled by default.

The issues that matter cluster into **three themes**: (1) AIS sentinel/edge-value handling,
(2) safety-relevant collision math shipped untested, and (3) repo/release hygiene (79 MB binary in git, no CI on push).

### "Fix now" — severity-sorted (do these first)

| # | Severity | Finding | Where |
|---|----------|---------|-------|
| 1 | **High** | **Vessels reporting the AIS "no-fix" sentinel (lat=91/lon=181) are silently dropped.** `VesselPosition` setters *throw* `ArgumentOutOfRangeException` on out-of-range values; `AisStreamClient.ProcessMessage` swallows it and increments `ParseErrorCount`. Should treat as "position unknown", not discard. | `VesselPosition.cs:17-32`, `AisMessageMapper.cs:75-76`, `AisStreamClient.cs:324-329` |
| 2 | **High** | **SOG=102.3 (raw 1023 = "not available") reaches the map and UI literally.** Plotted, shown as "102.3 kn", and passes the `>= 0.5 kn` collision gate. Only incidentally filtered in ETA/projection via `MaxPlausibleSpeedKnots=50`. | `AisMessageMapper.cs:77` |
| 3 | **High** | **`CpaCalculator` has zero direct tests.** The core collision math (TCPA projection, parallel-course guard, past-CPA rejection) is entirely unverified. | `CpaCalculator.cs`; no test file |
| 4 | **High** | **`CollisionRiskServiceTests` are hollow.** All four tests only assert `History.Should().BeEmpty()`; `ExecuteAsync` is never invoked, so no scan runs. There is no test that two converging vessels produce a warning — false confidence. | `CollisionRiskServiceTests.cs:36-60` |
| 5 | **High** | **79 MB `ShippingAPR-Setup.exe` is committed to the repo** (explicitly un-ignored in `.gitignore:18`) **and re-committed by the release workflow on every release.** Unbounded git-history bloat; the binary is already published as a Release asset. | `ShippingAPR-Setup.exe`, `.gitignore:18`, `release.yml:65-75` |
| 6 | **Med** | **No build/test CI on push or PR.** Only `release.yml` exists (tags / manual dispatch). A broken build or failing test is only caught at release time. | `.github/workflows/` |
| 7 | **Med** | **CLAUDE.md / README say "single AIS source" — it's actually three** (AisStream, Datalastic, DataDocked) behind `AisProviderFactory` + `FallbackAisProvider`. Materially misleads maintainers. | `CLAUDE.md`, `App.xaml.cs:102-124` |

> **Note on #1/#2:** these are the highest-value fixes — they're cheap (a sentinel guard in
> `AisMessageMapper` / non-throwing `VesselPosition`), unit-testable, and directly affect what the
> operator sees on the map. Recommended ordering: fix sentinel handling *and* add the missing tests
> in the same pass so the regression is locked in.

### Structural decisions to make (Section 0 — decide before the fix pass)

- **Mapsui 5.0.0-beta.1**: exactly pinned (good) but a beta with documented API fragility
  (see commit `349702f`). With `TreatWarningsAsErrors=true`, any upstream churn hard-breaks the
  build on upgrade. **Decide:** freeze-with-rationale, upgrade to stable, or accept-and-document.
  Currently no rationale is written anywhere.
- **AIS source resilience**: failover *exists* but ships disabled (`appsettings.json` `Fallback:""`),
  and there is **no "data is N seconds old" freshness indicator** — a silent stream stall shows
  "Connected" with frozen positions. **Decide:** enable a default fallback and/or add a data-age cue.

---

## Section 0 — Structural risks

| Item | Status | Severity | File:Line | Note |
|------|--------|----------|-----------|------|
| Mapsui beta pinned, no rationale | Fix later | Med | `ShippingAPR.App.csproj:22` | Beta pre-release w/ documented API instability (commit `349702f`); pinned but undocumented |
| Multi-source failover exists | OK | — | `FallbackAisProvider.cs:66` | Real auto-failover w/ `SemaphoreSlim` guard |
| Failover disabled by default | Fix later | Med | `appsettings.json:2-5` | `Fallback:""` → single-source out of the box |
| No "data N sec old" indicator | Fix later | Med | `VesselDetailViewModel.cs:131` | Static timestamp, not elapsed age; silent stall not flagged |

## Section 1 — Concurrency & threading

| Item | Status | Severity | File:Line | Note |
|------|--------|----------|-----------|------|
| No unsafe shared collections | OK | Low | `VesselStore.cs:10`; `AreaMonitorService.cs:30-35`; `MapViewModel.cs:42,641` | All concurrent or lock-guarded; plain `List`/`Dict` guarded by `_pendingLock`/UI-thread affinity |
| AIS-loop writes vs UI reads | Fix later | Low | `VesselTrackingService.cs:190`; `Vessel.cs:78-79` | `CalculatedEta`/`StaticData`/`LastUpdated` written on bg thread, read by UI unsynchronized; reference atomicity prevents crashes (transient visual inconsistency only) |
| UI mutations marshalled | OK | Low | `VesselStore.cs:130-149`; `App.xaml.cs:196`; `MainViewModel.cs:544` | Events posted via captured `SynchronizationContext`; status change explicitly `Dispatcher.Invoke`'d |
| SyncContext capture fragility | Fix later | Low | `VesselStore.cs:135-136`; `App.xaml.cs:196` | Null `SynchronizationContext.Current` would silently fall back to cross-thread invoke |
| `async void` handlers | Fix later | Low | `App.xaml.cs:307-314` | `OnExit`'s `await StopAsync` not wrapped in try/catch |
| No `.Result`/`.Wait()` on UI thread | OK | Low | `AisStreamClient.cs:371`; `PollingAisProviderBase.cs:144` | Only bounded `task.Wait(timeout)` in `Dispose`, after cancellation signaled |
| `CancellationToken` threading | OK | Low | `AisStreamClient.cs:70,143`; `PollingAisProviderBase.cs:48` | Tokens linked & honored throughout receive/poll/reconnect loops |
| Fire-and-forget fallback switch | Fix later | Low | `FallbackAisProvider.cs:71,94` | Switch runs with `CancellationToken.None`, uncancellable on shutdown |
| Backpressure / unbounded growth | Fix later | Low | `MapViewModel.cs:42,643` | `_pendingUpdates` unbounded between 250ms flushes; enrichment queue & track buffer are bounded |

## Section 2 — AIS data integrity

| Item | Status | Severity | File:Line | Note |
|------|--------|----------|-----------|------|
| Static/position desync tolerance | OK | Low | `IAisDataProvider.cs:16-20`; `VesselStore.cs:52-59`; `Vessel.cs:48-52` | Both nullable, applied independently; fallbacks present |
| Nullable static fields guarded / `!` usage | OK | Low | `StatisticsService.cs:32-54`; `FleetService.cs:56-57`; `VesselDetailViewModel.cs:88-204` | All `!` preceded by `Where`-filters; display uses `?.`/`??` |
| Stale vessel eviction (TTL) | OK | Low | `TrackingOptions.cs:7-8`; `VesselTrackingService.cs:141-157`; `VesselStore.cs:113-128` | Configurable, timer-driven, fires `VesselRemoved` |
| MMSI validation (9-digit) | Fix later | Med | `VesselStore.cs:35-42`; `VesselTrackingService.cs:204-206` | Range-only check; **throws** on bad MMSI (swallowed silently); rejects valid 8xx/97x MIDs; exceptions for control flow on hot path |
| Duplicate MMSI updates not duplicates | OK | Low | `VesselStore.cs:46-61` | `ConcurrentDictionary.AddOrUpdate` updates in place |
| Sentinel lat=91 / lon=181 | **Fix now** | **High** | `VesselPosition.cs:17-32`; `AisMessageMapper.cs:75-76`; `AisStreamClient.cs:324-329` | Setters throw; exception swallowed → vessel silently dropped, not "unknown" |
| Sentinel speed=102.3 (1023) | **Fix now** | **High** | `AisMessageMapper.cs:77` | Passed literally; plotted & shown; only ETA/projection filter via Max=50 |
| Sentinel course=360 | Fix later | Med | `AisMessageMapper.cs:78` | Passed literally; `cos(360)=cos(0)` coincidentally harmless but semantically wrong |
| Sentinel heading=511 | OK | Low | `AisMessageMapper.cs:79`; `NavigationConstants.cs:32` | Falls back to COG |
| Timestamp source/consistency | Fix later | Med | `AisMessageMapper.cs:82`; `VesselPosition.cs:41`; `Vessel.cs:72` | Always receipt-time UTC; message `Timestamp`/`time_utc` ignored. Consistent for staleness |

## Section 3 — Coordinate system (EPSG:3857 ↔ WGS84)

| Item | Status | Severity | File:Line | Note |
|------|--------|----------|-----------|------|
| All conversions via `SphericalMercator` | OK | Low | `MapViewModel.cs` (24 sites); `MapView.xaml.cs:46` | No manual mercator math, no raw lat/lon to Mapsui |
| Argument order (lon/lat) | OK | Low | `MapViewModel.cs:532-533,667,812-813,1287`; `MapView.xaml.cs:46-54` | Correct everywhere; `ToLonLat` destructured `.lon`/`.lat` |
| Anti-meridian / high latitude | Fix later | Med | `BoundingBox.cs:14-15`; `MapViewModel.cs:301-302`; `RouteProjectionCalculator.cs:52-56` | Box cannot cross ±180 (ctor throws); lat clamped ±85; projection lon-wrap OK |
| Distance/range units (m vs deg) | Fix later | Med | `HaversineCalculator.cs`; `CpaCalculator.cs:25-33`; `EtaCalculator.cs:33` | All consistently NM; no mixing. CPA equirectangular projection breaks across ±180 seam (missed risk) |

## Section 4 — Reliability & long-running behaviour

| Item | Status | Severity | File:Line | Note |
|------|--------|----------|-----------|------|
| Reconnect backoff | OK | Low | `AisStreamClient.cs:238-298`; `PollingAisProviderBase.cs:124-132` | Capped exponential backoff + jitter + attempt limit; no spin/spam |
| Sleep/resume / network recovery | Fix later | Med | `AisStreamClient.cs:162-214,306` | Reactive only (waits for socket failure); no power/network-change hook → delayed post-sleep reconnect |
| Weather/enrichment degrade | OK | Low | `OpenMeteoMarineClient.cs:94-98`; `VesselFinderClient.cs:99-103`; `WeatherOverlayService.cs:44-47` | All failures caught → null + log; cannot affect map |
| Global unhandled-exception handler | Fix later | Med | `App.xaml.cs:33-48,285-305` | Handler exists but only `MessageBox` — does not log the exception to `ILogger`/file (no crash trail) |
| Clean shutdown | OK | Low | `App.xaml.cs:307-318`; `AisStreamClient.cs:362-380` | Correct order: VM dispose → `StopAsync(5s)` → host `Dispose` cancels tokens & disposes sockets |

## Section 5 — Performance & rendering

| Item | Status | Severity | File:Line | Note |
|------|--------|----------|-----------|------|
| High-count behaviour / culling | Fix later | Med | `MapViewModel.cs:647-708,710-774` | Clustering present; no per-vessel viewport culling; cluster full-scan + redundant `GetByMmsi` every 250ms tick |
| Skia paint/bitmap reuse | Fix later | Med | `MapViewModel.cs:677-678,776-802` | No raw Skia (N/A); but `SymbolStyle`/`Brush`/`Pen` re-allocated per vessel per tick, not cached |
| Throttled/coalesced UI updates | OK | Low | `MapViewModel.cs:159-164`; `VesselListViewModel.cs:56-79` | Map 250ms batch, list 3s debounce, viewport 500ms; per-MMSI dedup |
| List virtualization | OK | Low | `VesselListPanel.xaml:76-84` | `IsVirtualizing` + `Recycling`; per-item shadow/animations minor cost |
| Invalidate only changed features | Fix later | Low | `MapViewModel.cs:688,695-700` | Feature-list rebuild avoided via flag; `DataHasChanged()` still whole-layer (Mapsui limitation) |

## Section 6 — Architecture & layering integrity

| Item | Status | Severity | File:Line | Note |
|------|--------|----------|-----------|------|
| No logic in Views | OK | Low | `MapView.xaml.cs:102-105`; `MainWindow.xaml.cs:104-120` | Only view wiring; prefs-mapping in `OnWindowClosing` is a mild smell |
| Core zero deps | OK | N/A | `ShippingAPR.Core.csproj` | No Package/Project references |
| Inward-only dependencies | OK | N/A | all `.csproj` | App→Services→Infrastructure→Core, acyclic, no Core leak |
| Config via `IOptions` | Fix later | Low | `MainViewModel.cs:546`; `SettingsViewModel.cs:37-50` | `IOptions` for main surface; raw `IConfiguration[...]` reads in a VM and settings editor |
| VMs headless-testable | Fix now | Med | `ShippingAPR.App.csproj:4`; `MapViewModel.cs:39-40,151,453` | VMs in `net8.0-windows`/WPF assembly; depend on `DispatcherTimer`/`Application.Current`/`MessageBox` |
| DI lifetimes | OK | Low | `App.xaml.cs:97-184` | Stores/clients/services singletons; HTTP clients via `AddHttpClient` |

## Section 7 — Security & secrets

| Item | Status | Severity | File:Line | Note |
|------|--------|----------|-----------|------|
| appsettings blank keys | OK | Low | `appsettings.json:7,16,20,26` | All key fields empty |
| .gitignore covers secrets | OK | Low | `.gitignore:26-28` | Local/Development ignored |
| **Git-history secret scan** | **OK / N/A** | N/A | (all 1058 blobs) | **No real key ever committed; only `secrets.GITHUB_TOKEN` ref — no rotation needed** |
| Runtime key persisted plaintext | Fix later | Med | `MainViewModel.cs:377` | User key written unencrypted to install-dir `appsettings.json`; consider DPAPI / `%APPDATA%` |
| Secrets in logs | OK | Low | `VesselFinderClient.cs:55` | Logs the word "ApiKey", never the value |
| TLS / cert validation | OK | Low | (no matches) | All endpoints `wss`/`https`; no validation disabled |
| Installer integrity check | Fix later | Med | `Install.ps1:31`, `Install.bat:18` | Downloaded `.exe` run without checksum/signature verify; 82MB binary committed |

## Section 8 — Error handling & logging

| Item | Status | Severity | File:Line | Note |
|------|--------|----------|-----------|------|
| `ILogger` consistency | OK | Low | `MainViewModel.cs:386`, `WelcomeDialog.xaml.cs:40` | 2 `Debug.WriteLine` in non-DI contexts; otherwise structured `ILogger<T>` |
| Reconnect log levels | OK | Low | `AisStreamClient.cs:240,288,296` | Info/Warn for retries, Error once on exhaustion |
| Empty catch blocks | OK | Low | `PollingAisProviderBase.cs:69-70`; `SettingsDialog.xaml.cs:21` | All justified (cancel/timeout/DragMove) |
| User-visible errors | OK | Low | `MainViewModel.cs:554,220,412` | Status text + toasts |
| Crash handlers exist | Fix later | Low/Med | `App.xaml.cs:33-48` | All 3 wired but no persisted crash log (dialog only) |

## Section 9 — Testing & CI

| Item | Status | Severity | File:Line | Note |
|------|--------|----------|-----------|------|
| `CpaCalculator` untested | **Fix now** | **High** | `CpaCalculator.cs:17` | Core collision math has zero direct tests |
| `CollisionRiskService` tests hollow | **Fix now** | **High** | `CollisionRiskServiceTests.cs:36-60` | All assert empty history; `ExecuteAsync` never invoked; no converging-vessel warning test |
| AIS sentinels lat=91 / sog=1023 unhandled & untested | **Fix now** | **High** | `AisMessageMapper.cs:73-83` | Raw pass-through; invalid positions/speeds plotted; only heading=511 handled |
| `AisMessageMapper` coverage | OK | Low | `AisMessageMapperTests.cs` | Strong: types, MID, ETA, padding, draught |
| Stale eviction tested | OK | Low | `VesselStoreVesselRemovedTests.cs` | `PurgeStale` covered |
| Tests use concrete services, not interface mocks | Fix later | Low | `tests/` (≈50 `new <Service>`) | Contradicts stated mock-only convention |
| `TreatWarningsAsErrors` enforced | OK | — | `Directory.Build.props:6` | Solution-wide, no overrides/NoWarn/pragmas |
| No build/test CI on push/PR | **Fix now** | Med | `.github/workflows/` | Only `release.yml`; tests run only at tag time |
| Workflow commits 79MB installer to repo | **Fix now** | **High** | `release.yml:65-75` | Pushes binary to default branch each release |
| Release builds `default_branch`, not tag ref | Fix later | Med | `release.yml:28` | Can ship ≠ tagged commit |
| Clean self-contained publish | OK | Low | `release.yml:46-57` | No machine-local assumptions |
| No startup/boot smoke test | Fix later | Med | `tests/ShippingAPR.App.Tests/` | DI graph / VM init crash ships undetected |

## Section 10 — UX / product polish

| Item | Status | Severity | File:Line | Note |
|------|--------|----------|-----------|------|
| First-run welcome guidance | OK | Low | `WelcomeDialog.xaml`; `App.xaml.cs:221` | Steps, skip mode, helper text |
| API key in plain `TextBox` | Fix later | Low | `WelcomeDialog.xaml:147` | Not masked |
| "Test connection" doesn't test | Fix later | Low | `WelcomeDialog.xaml.cs:44` | Only non-empty check, misleading failure msg |
| Connection states wired | OK | Low | `MainViewModel.cs:542-568` | All 6 states text+color |
| Disconnected uses error color | Fix later | Low | `MainViewModel.cs:566` | Idle looks like a real error |
| Status colors not theme-aware | Fix later | Low | `UiOptions.cs:39-41` | Config hex, ignores light/dark |
| No live-vs-stale visual cue | Fix later | Med | `VesselDetailViewModel.cs:131` | Only a timestamp; stale data not flagged |
| Empty states designed | OK | Low | `VesselListPanel.xaml:60`; `SearchBar.xaml:104` | Lists/panels covered |
| EN/SV key parity | OK | Low | `Strings.resx` / `Strings.sv.resx` | 126=126, zero missing |
| Theme dictionaries parity | OK | Low | `Dark/LightTheme.xaml` | Brushes match; only dead scrollbar keys differ |
| Hardcoded colors in Views | Fix later | Med | `DashboardPanel.xaml`, `MainWindow.xaml:59` etc. | White/hex text risks unreadable on light theme |

## Section 11 — Documentation & maintainability

| Item | Status | Severity | File:Line | Note |
|------|--------|----------|-----------|------|
| CLAUDE.md "single AIS source" wrong | **Fix now** | Med | `CLAUDE.md`; `App.xaml.cs:102-124` | 3 providers + fallback factory |
| CLAUDE.md services/test layout stale | Fix later | Low | `CLAUDE.md:11-15` | ~20 services + 4 test projects undocumented |
| ~80 ports / themes / EN-SV claims | OK | Low | `ports.json` (=80) | Verified accurate |
| LEARNINGS.md / TASK.md | N/A | Low | (repo root) | Do not exist and are not referenced anywhere (audit-framework premise) |
| README setup coverage | OK | Low | `README.md` | Install/keys/build/run/tests present |
| README hardcoded branch ZIP link | Fix later | Low | `README.md:7` | Fragile branch reference (`claude/ship-tracking-app-qdNAb`) |
| 79MB installer committed to repo | **Fix now** | **High** | `ShippingAPR-Setup.exe`; `.gitignore:18` | 82MB binary in git, rewritten each release |
| Dependencies lean/justified | OK | Low | all `.csproj` | No unused/heavy packages |
| Mapsui beta / AIS strategy rationale undocumented | Fix later | Low | `README.md`; `CLAUDE.md:51` | Beta dep + multi-source decision not written down |

---

## Recommended fix-pass ordering

Per the audit framework's discipline (one concern per commit, `dotnet test` before each):

1. **AIS sentinel handling + tests** (#1, #2, #3, #4 together) — make `AisMessageMapper` treat
   91/181/1023/3600 as "unknown", make `VesselPosition` non-throwing for those, and add the
   `CpaCalculator` + converging-vessel `CollisionRiskService` tests in the same pass. Highest
   user impact, fully unit-testable, locks in the regression.
2. **Repo/release hygiene** (#5, #6) — stop committing the installer (rely on Release assets),
   add a build+test CI workflow on push/PR. Cheap, high leverage.
3. **Doc accuracy** (#7) — correct the "single AIS source" claim in CLAUDE.md / README.
4. **Structural decisions** (Section 0) — write down the Mapsui-beta stance; decide on default
   fallback + a data-freshness indicator.
5. **Med items** — MMSI non-throwing validation, message-timestamp option, sleep/resume reconnect
   hook, global handler → log to file, per-tick style caching / viewport culling, headless-testable VMs.
6. **Low items** — as time permits.
