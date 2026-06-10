# ShippingAPR — Master Audit (2026-06-09)

Full-codebase audit: 5 parallel deep sweeps (Core+Infrastructure, Services, App
ViewModels/config/startup, Views/XAML/themes/resources, tests/CI/build), with
the highest-impact claims re-verified by hand against source. Each finding
carries a **confidence** tag:

- **VERIFIED** — confirmed by direct source inspection this session.
- **PLAUSIBLE** — agent-reported with cited lines, consistent with code read, not independently re-executed.
- **DOWNGRADED** — agent reported it higher; hand-analysis reduced severity (reason given).

Findings already fixed earlier this session (boundary-flap hysteresis in
area/chokepoint/port services, VesselFinder parsing, AlertRule positionless
guard, polygon geofences, resx/Designer drift, localization of 12 panels, etc.)
are **not** re-listed.

---

## P0 — Bugs to fix first (user-facing or correctness)

1. **`src/ShippingAPR.App/ViewModels/MainViewModel.cs:371` — API key dialog ignores selected provider — VERIFIED — High**
   `OpenApiKeyDialog` calls `SaveApiKey(dialog.ApiKey)` which hardwires the
   `"AisStream"` section and forces `AisProvider:Active = AisStream`. If the
   user picks Datalastic/DataDocked in the dialog, the key is saved under the
   wrong assumption and the app activates AisStream with an empty key.
   The welcome-dialog path (App.xaml.cs) correctly uses
   `SaveProviderApiKey(welcomeDialog.SelectedProvider, …)`.
   **Fix:** `SaveProviderApiKey(dialog.SelectedProvider, dialog.ApiKey)`.

2. **`src/ShippingAPR.App/ViewModels/MainViewModel.cs:372` + `App.xaml.cs:~279` — `HasApiKey = true` set even when save fails — VERIFIED-by-read — High**
   Both key-entry paths set `HasApiKey = true` unconditionally; if
   `LocalSettingsStore.Update` returns false (I/O error), UI enables tracking
   with no key on disk. **Fix:** gate on the save result; show error otherwise.

3. **`.github/workflows/release.yml:28` — release builds default branch, not the tag — VERIFIED — High**
   `ref: ${{ github.event.repository.default_branch }}` means a tagged release
   compiles whatever the default branch holds at build time; binaries may not
   match the tag (non-reproducible releases). **Fix:** `ref: ${{ github.ref }}`
   (keep `workflow_dispatch` input override).

4. **`tests/ShippingAPR.Services.Tests/AlertEngineTests.cs:19-22` — tests delete the developer's real AppData file — VERIFIED — High (test safety)**
   Setup deletes `%LOCALAPPDATA%/ShippingAPR/alert-rules.json` and AlertEngine
   then persists to that real path during tests — mutates real user state and
   breaks isolation under parallel runs. **Fix:** make AlertEngine's storage
   path injectable; use a temp dir per test.

5. **`src/ShippingAPR.Core/Models/BoundingBox.cs:14-15` — anti-meridian boxes unrepresentable — VERIFIED-by-read — Medium**
   Constructor rejects `MinLongitude > MaxLongitude`, so any tracking area
   crossing ±180° (Pacific routes, Bering Strait, Fiji) cannot be expressed;
   viewport code clamps instead, silently subscribing to the wrong region.
   CPA math already normalizes anti-meridian — the model is the gap.
   **Fix:** support wrap-around boxes (`Contains` handles min>max) or split
   into two boxes at the subscription layer.

6. **`src/ShippingAPR.Services/PortActivityService.cs:103` — departure lost if vessel leaves scan radius between updates — VERIFIED-by-analysis — Medium**
   The `distNm > PortRadiusNm * 3` prefilter `continue`s without state cleanup:
   a vessel jumping from in-port to >9 NM (sparse AIS updates) never records a
   departure until it returns or drops from the feed. **Fix:** before
   `continue`, if the vessel is in this port's set, record departure and remove.

## P1 — Robustness / correctness (verified or strongly plausible)

7. **`src/ShippingAPR.Infrastructure/Providers/PollingAisProviderBase.cs:107` — `_currentArea!` null-deref path — PLAUSIBLE — Medium**
   If `UpdateSubscriptionAsync` runs before `ConnectAsync` ever set the area,
   the poll loop dereferences null. Current callers connect first, but
   `MapViewModel` viewport changes can call `ChangeAreaAsync` pre-tracking.
   **Fix:** null-check and skip the poll iteration.

8. **`src/ShippingAPR.Infrastructure/Mapping/AisMessageMapper.cs:99` vs `AisPositionNormalizer.cs:26` — inconsistent SOG validation — VERIFIED-by-read — Medium**
   Normalizer rejects negative SOG; mapper does not (`sog >= 102.3 ? 0 : sog`
   passes negatives through). **Fix:** add `or < 0` in `BuildPosition`.

9. **`src/ShippingAPR.Infrastructure/Persistence/SqliteTrackHistoryStore.cs:154` — `SpecifyKind` masks non-UTC input — VERIFIED-by-read — Medium (latent)**
   A `DateTime.Now`-kind value would be silently treated as UTC. Current
   callers pass UTC; one future caller breaks it invisibly.
   **Fix:** `dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : …`.

10. **`src/ShippingAPR.Core/IO/AtomicFile.cs:22-24` — fixed temp filename collides under concurrent writers — VERIFIED-by-read — Medium-Low**
    Two concurrent saves to the same path share `path + ".tmp"`; second `Move`
    can throw or interleave. **Fix:** unique temp name (`Guid`) + retry/catch.

11. **`src/ShippingAPR.App/Configuration/LocalSettingsStore.cs` — bare `catch { return false; }` with no logging — VERIFIED-by-read — Medium**
    A failed API-key save is undiagnosable (no log, and with #2, no UI signal).
    **Fix:** catch specific exceptions, log via injected/static logger.

12. **`src/ShippingAPR.Services/VoyageNarrativeService.cs:47-64` — baseline speed/status set after event added — PLAUSIBLE — Medium-Low**
    Window where the first `OnVesselUpdated` reads default(0) baselines and
    logs a false speed/status-change event. **Fix:** seed `_lastSpeed/_lastStatus`
    before publishing the vessel-added event.

13. **`src/ShippingAPR.Services/AchievementService.cs:140` — file I/O (`Save`) under `_lock` — PLAUSIBLE — Medium-Low**
    Blocks all achievement updates for the duration of a disk write on the AIS
    hot path. **Fix:** set a dirty flag inside the lock, save outside.

14. **`src/ShippingAPR.Services/EncounterJournalService.cs:72-73` — `Task.Run(Save)` may run post-disposal — PLAUSIBLE — Medium-Low**
    Count-triggered background save races service teardown. **Fix:** timer-based
    flush with cancellation, or guard with a disposed flag.

15. **`src/ShippingAPR.Infrastructure/Datalastic|DataDocked clients` — IMO accepted only as JSON number — PLAUSIBLE — Low**
    Same class of bug as the fixed VesselFinder one (string IMO drops the value).
    **Fix:** reuse the tolerant `ReadInt` pattern.

16. **`src/ShippingAPR.App/ViewModels/GeofenceViewModel.cs:27` — event subscription never unsubscribed, no `IDisposable` — VERIFIED-by-read — Low**
    Leak only if VM is ever recreated (currently singleton-per-window). Add
    `IDisposable` for symmetry with sibling VMs.

17. **`src/ShippingAPR.App/Views/MapView.xaml.cs:102-105` — redundant second `GetVesselSummary` in tooltip color path — PLAUSIBLE — Low (perf/clarity)**

18. **`src/ShippingAPR.Infrastructure/Mapping/AisMessageMapper.cs:219` — ETA year-rollover heuristic ambiguity — VERIFIED-by-read — Low**
    AIS ETA has no year; the 1-day grace + AddYears(1) is a reasonable
    heuristic; Dec/Jan boundary cases are inherently ambiguous. Document it.

## P2 — UI / localization / theming residue

19. **23 hardcoded tooltips — VERIFIED-by-agent-enumeration — Medium**
    `MainWindow.xaml` (16: nav-tab tooltips at 352-379, notification/settings
    at 85/120, status-bar at 545/600) and `MapView.xaml` (7: map tool buttons
    at 107-144) still bypass `Strings.*`. Also their `AutomationProperties.Name`
    values. **Fix:** add the keys to resx/sv/Designer (use `tools/add_loc_keys.py`)
    and bind. Also add `AutomationProperties.Name` to MapView's 3 icon buttons.

20. **LightTheme missing keyed scrollbar styles — VERIFIED earlier, DOWNGRADED to Low**
    `GlassVertical/HorizontalScrollBarStyle` (+thumb styles per the XAML agent)
    exist only in DarkTheme — but **nothing references them** (dead resources).
    Either delete from DarkTheme or mirror in LightTheme.

21. **Dashboard vessel-type dots / CII badge hex colors — DOWNGRADED to Won't-fix-as-reported**
    Agents re-flagged these; verified earlier this session that vessel-category
    colors intentionally mirror the canonical `VesselTypeColors` constants
    (theme-independent, consistent with the map), and CII grade colors are
    semantic. Optional cleanup: source the dots from a shared resource so the
    legend can never drift from `VesselTypeColors` again (one such drift was
    already fixed: the fishing dot).

22. **`SearchViewModel` — no debounce on keystroke search — VERIFIED-by-read — Low**
    In-memory search over the store; cheap today, debounce (à la journal panel)
    if stores grow. Optional `IsSearching` flag for spinner.

23. **`VesselListViewModel` — full list rebuild every 3 s — Low (by design)**
    Documented batching strategy; delta updates are a perf option for 1000+
    vessel fleets, not a bug.

## P3 — Tests / CI / build hygiene

24. **Coverage gaps on the flagged high-risk units — VERIFIED-by-absence — High value**
    No tests for: `AisStreamClient` receive/reconnect loop (the single most
    critical untested unit), `DatalasticClient`, `DataDockedClient`,
    `AisProviderFactory`, `FallbackAisProvider` lifecycle, `LocalSettingsStore`
    I/O, `MainViewModel`/`MapViewModel` logic (incl. viewport→bbox math, which
    is testable headlessly). Priority order as listed.

25. **Shared `VesselStore` fields across test classes — DOWNGRADED to Low**
    xUnit creates a new class instance per test method, so `private readonly
    VesselStore _store = new()` is per-test, not shared. (Cross-class parallel
    runs only conflict via real-filesystem state — which is finding #4.)

26. **"Flaky DateTime.UtcNow tests" — DOWNGRADED to Won't-fix**
    Re-checked `EtaCalculatorTests:103` (`BeAfter(UtcNow)` on an arrival hours
    in the future) and the before/after timestamp-window patterns: not
    realistically flaky. No action.

27. **Release workflow lacks `--no-restore` consistency / explicit test-gate — PLAUSIBLE — Low**
    `dotnet test` exit code does fail the job by default; the inconsistency is
    cosmetic. Tag-checkout (#3) is the real issue in that file.

28. **`Mapsui.Wpf 5.0.0-beta.1` production dependency — Known — Medium (watch)**
    Pin and track for 5.0 final; API churn risk acknowledged in CLAUDE.md.

29. **CI `cancel-in-progress: true` on all refs — Low**
    Can hide intermittent failures on rapid pushes to a shared branch; consider
    scoping cancellation to PR refs only.

## Deferred (tracked in TODO.md)

- **Fuel-model + IMO-correct CII recalibration** — blocked on ground-truth
  calibration data; plan documented in `TODO.md`.

## Explicit false-positives (so future audits don't rediscover them)

- `CollisionRiskService:111` / `WeatherAlertService:92` "TOCTOU races": scans
  run on a single BackgroundService loop; no concurrent caller exists.
- `AreaMonitorService` StoreCleared `Clear()` "NullRef/corruption": per-op
  thread-safe; post-clear updates just re-baseline. Benign.
- `OpenMeteoMarineClient:62-65` "double await": awaiting completed tasks after
  `WhenAll` is the idiomatic result-retrieval pattern; whole block is in
  try/catch. Style only.
- `VesselComparisonViewModel` "missing NotifyPropertyChangedFor":
  `OnPropertyChanged(string.Empty)` refreshes all bindings. Correct.
- AisStream reconnect backoff "off-by-one": first retry after 1 s + jitter,
  doubling per failure, clamped — standard exponential backoff. Not a bug.
- `RouteProjectionCalculator` longitude normalization: formula verified correct.
- MapViewModel `SphericalMercator` conversions and lat/lon ordering: verified
  correct at all call sites (lines ~269, ~301-309, ~418, ~1193).

## Recommended fix order

1. P0 #1-#2 (API-key flow) — one commit; directly user-facing.
2. P0 #3 (release tag checkout) — one-line CI fix; restores release integrity.
3. P0 #4 (test AppData isolation) — injectable path + temp dir.
4. P0 #6 + P1 #7-#11 — small, independently committable robustness fixes.
5. P2 #19 (tooltip localization) — mechanical, tooling exists.
6. P3 #24 — add tests around AisStreamClient reconnect + provider factory
   before touching that code further.
7. P0 #5 (anti-meridian) — design decision needed (wrap-around box vs split);
   propose split-at-subscription as lowest-risk.
