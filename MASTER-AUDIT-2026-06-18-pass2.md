# ShippingAPR — Second-Pass Audit (2026-06-18)

**Scope.** A deeper, different-angle sweep run against the *post-fix* code (branch
`claude/shippingapr-audit-checklist-DwXbW`, tip `65478ed`), explicitly excluding
everything already fixed or already documented as a false positive in
`MASTER-AUDIT-2026-06-09.md`. Four parallel read-only audits were run — Security &
untrusted-input, Map-render pipeline, Resource lifecycle & leaks, Domain/calc
correctness — and **every load-bearing claim below was hand-verified against
source** before listing. As with pass 1, roughly a third of the agents' raised
items did not survive verification; those are recorded in the false-positives
ledger at the bottom rather than silently dropped.

**Bottom line.** No new critical or correctness-critical bug was found. The
codebase's high-risk areas (concurrency, coordinate correctness, AIS feed
ingestion) re-verified clean. What remains is a short list of **input-hardening**
and **defensive-tidiness** items plus two genuine-but-cosmetic quality issues.
Nothing here changes the "Connected but 0 ships" diagnosis (still a feed/key
issue, not a render bug — see M-1).

Confidence tags: **VERIFIED** (confirmed against source), **PLAUSIBLE** (real but
not runtime-confirmable in this sandbox), **DOWNGRADED** (claim weaker than the
agent stated). Severity reflects *real-world* impact, not theoretical worst case.

---

## A. Worth doing — cheap, safe, real

### A-1. AIS dimension/draught bounds are unvalidated → overflow / NaN propagation
**VERIFIED · Severity: Low–Medium · Hardening**
- `src/ShippingAPR.Core/Models/VesselStaticData.cs:18-19` — `LengthOverall =>
  DimensionA + DimensionB` and `Beam => DimensionC + DimensionD` are raw `int`
  adds over untrusted AIS JSON. A malicious/compromised provider sending
  `Int32.MaxValue` dimensions overflows to negative length/beam, which then feeds
  size-based logic (achievements, any size display).
- `src/ShippingAPR.Infrastructure/AisStream/Messages/ShipStaticDataMessage.cs:31`
  — `MaximumStaticDraught` is a raw `double`; `NaN`/`Infinity` flow through
  `AisMessageMapper` (÷10) into UI bindings unguarded.
- **Real-world likelihood:** low (requires a hostile feed; aisstream.io clamps to
  AIS spec). **Why fix anyway:** it's the single clearest input-trust gap, and the
  clamp belongs in `AisMessageMapper` next to the existing SOG/COG sanitisation
  we already added in pass 1 — same pattern, same place.
- **Fix:** clamp dimensions to AIS ranges (A/B 0–511, C/D 0–255) and draught to a
  sane finite range in the mapper; reject non-finite doubles.

### A-2. Inline event-lambda subscriptions never unsubscribed (consistency gap)
**VERIFIED · Severity: Low (no current runtime impact) · Tidiness**
- `src/ShippingAPR.App/ViewModels/MapViewModel.cs:153-159` — three subscriptions
  (`GeofencesChanged`, `WeatherUpdated`, `HeatmapUpdated`) use inline lambdas that
  are never stored, so `Dispose` (1414–1419) cannot detach them.
- `src/ShippingAPR.App/ViewModels/MainViewModel.cs:218` — `VesselFeatureClicked`
  subscribed with an inline lambda, never detached in `Dispose`.
- **Honest impact:** *none today.* Publishers (services / `MapViewModel`) and
  subscribers are all window-lifetime singletons that never get recreated, so
  nothing leaks. This is purely a consistency item: we already converted
  `GeofenceViewModel` to the named-handler + unsubscribe pattern in pass 1, and
  these are the remaining instances of the same idiom. Worth aligning so the
  pattern is uniform, but it is **not** a live leak.
- **Fix:** store each as a named field, unsubscribe in `Dispose`.

### A-3. `EncounterJournalService` fire-and-forget `Task.Run(Save)` races shutdown
**PLAUSIBLE · Severity: Low · Tidiness** *(independently flagged by two agents)*
- `src/ShippingAPR.Services/EncounterJournalService.cs:71-73` — every 50th
  encounter spawns `Task.Run(Save)`; `Dispose` (≈230) also calls `Save()`
  synchronously. Two `Save()`s can overlap.
- **Why only Low:** `Save()` writes via `AtomicFile` (unique temp + atomic
  `Move(overwrite)`), so concurrent saves are last-writer-wins, **not**
  corrupting. The risk is a redundant background write touching a possibly-
  disposed logger, not data loss. Verified AtomicFile semantics rule out the
  corruption the agents implied — hence Low, not Medium.
- **Fix:** drop the fire-and-forget (the periodic + Dispose saves already cover
  durability), or gate it behind a `_disposed` check.

---

## B. Real but cosmetic — fix only if polishing the heatmap

### B-1. Heatmap smoothing is asymmetric and integer-truncated
**VERIFIED · Severity: Low · Visual quality**
`src/ShippingAPR.Services/HeatmapService.cs:102-132`
- Border cells (`y/x == 0 || == res-1`) are never smoothed (loop is `1..res-2`).
- The copy-back guard `if (smoothed[y,x] > 0)` means any interior cell that blurs
  to 0 keeps its *un-smoothed* original value — smoothing is applied
  inconsistently across the grid.
- `smoothed[y,x] = sum / 9` is integer division → systematic darkening; an
  isolated hotspot of intensity 10 collapses to `10/9 = 1`.
- **Impact:** purely the look of the density overlay; no data or calc downstream.
- **Fix (if touched):** edge-pad borders, copy all smoothed values, and round
  (`(sum + 4) / 9` or float accumulation).

### B-2. `StatisticsService` mixes "total tracked" with "position-only" metrics
**VERIFIED · Severity: Low · UX clarity**
`src/ShippingAPR.Services/StatisticsService.cs:31-40,64` — `TotalVessels` counts
all vessels incl. position-less; `AverageSpeed` averages only those with a
position. Not wrong, but the two numbers describe different denominators and can
look inconsistent. **Fix:** surface both counts, or label the metric.

### B-3. `TideDataClient` next-high/low scan can miss an extremum at array end
**PLAUSIBLE · Severity: Low · Edge case**
`src/ShippingAPR.Infrastructure/Weather/TideDataClient.cs:86-92` — the
local-extremum loop requires neighbours on both sides (`i < levels.Count - 1`),
so a genuine next high/low in the final hour of the forecast window returns
`null`. Minor; resolves itself on the next fetch. **Fix:** boundary-aware
extremum detection.

### B-4. Port fuzzy-match is positional, not edit-distance
**VERIFIED · Severity: Low · Enhancement (not a bug)**
`src/ShippingAPR.Infrastructure/Ports/PortRepository.cs:159-177` — character-by-
position scoring misses transpositions/insertions ("Hamburg" vs a transposed
typo). It already has a contains-match fallback, so search still works for
prefixes/substrings. **Enhancement:** Damerau–Levenshtein if port search quality
ever matters.

---

## C. Lower-value hardening (defensible to skip)

| Ref | Item | Tag | Why low |
|-----|------|-----|---------|
| C-1 | Unbounded array iteration in `TideDataClient` (59-70) and `Datalastic`/`DataDocked` clients — no cap on response size | PLAUSIBLE | DoS only from a compromised/trusted API; network timeout fires first. Add a sanity cap if hardening. |
| C-2 | `AtomicFile` leaves orphaned `*.tmp` on hard crash mid-write | VERIFIED | Cosmetic; never corrupts the target (Move is atomic). Optional startup sweep. |
| C-3 | DispatcherTimers in `MapViewModel` stopped but not `Dispose()`d (162-175 / 1414-1415) | VERIFIED | Standard WPF pattern; `.Stop()` halts ticks. Negligible. |
| C-4 | `MapViewModel` in-place `GeometryFeature.Geometry` mutation may not bust Mapsui's render cache on updates (705-712) | PLAUSIBLE | Cannot confirm without a live renderer; styles are recreated each update which likely invalidates cache. Flag for runtime QA, not a blind change. |

---

## D. False positives & downgrades (did not survive verification)

- **"Uninitialized layer `Features` → Connected but 0 ships"**
  (`MapViewModel.cs:239-252`). **DOWNGRADED.** Verified: `_vesselLayer.Features`
  is reassigned the moment data arrives (709) and `_clusterLayer.Features` in
  `UpdateClusters` (784), each followed by `DataHasChanged()`; Mapsui 5
  `MemoryLayer.Features` defaults to an empty list, not null. This cannot produce
  `Ships:0` when the store is empty — that symptom is feed/key-side, as already
  diagnosed. Defensive init at most.
- **"Gale + fog double-fire on one vessel"** (`WeatherAlertService.cs:106-173`).
  **FALSE POSITIVE (mechanism).** The fog branch re-checks `ShouldAlert(mmsi)`
  (148); the gale branch already set `_alertedVessels[mmsi]` (133), so fog is
  suppressed in the same pass. *Residual (real but minor):* the two conditions
  share one cooldown key, so a persistent gale can starve fog advisories — a
  design choice, not a bug.
- **`EmissionsEstimatorService` speed check (186-187)** — agent admits the null-
  position path is unreachable (caller returns null at 63). **Not a bug**;
  redundant defensive code at worst.
- **`HeatmapCell.AddPoint` non-atomic update (40-42)** — **DOWNGRADED.**
  `GenerateGrid` is single-threaded; bucket accumulation upstream uses
  `ConcurrentDictionary`. No concurrent caller exists. Document-only.
- **CSV formula-injection "incomplete"** (`ExportService.cs:146-157`) —
  **DOWNGRADED.** The leading-char `'` prefix is the standard Excel mitigation and
  is present; no concrete embedded-`=` attack vector. XML/KML escaping verified
  complete.
- **`LocalSettingsStore` Unix file permissions** — **DOWNGRADED.** Windows-only
  app; `%APPDATA%` is already user-scoped. Non-issue in the shipping target.
- **Anti-meridian `BoundingBox`** (`BoundingBox.cs`) — **already tracked**, not
  new: documented as P0 #5 in `MASTER-AUDIT-2026-06-09.md` and `TODO.md`
  (needs subscription-layer box-splitting; deferred deliberately).
- **Coordinate order, clustering math, concurrency (map pipeline)** — re-verified
  **clean**: every `SphericalMercator.FromLonLat/ToLonLat` passes `(lon, lat)`
  correctly; the measurement tool's `.Y,.X` swap is correct for the Haversine
  signature; all map-feature access is UI-thread-confined.
- **Resource lifecycle (the rest)** — re-verified **clean**: CTS lifecycles,
  `BackgroundService` `stoppingToken` honouring, typed-`HttpClient` factory usage,
  `ArrayPool` rent/return balance, and `WeakReferenceMessenger` unregistration are
  all correct. The IDisposable services fixed in pass 1 remain correct.

---

## Recommended order (if/when given the go)

1. **A-1** — AIS dimension/draught clamps in `AisMessageMapper` (one file, mirrors
   the pass-1 SOG/COG fix; add a couple of mapper tests).
2. **A-3** — remove the `EncounterJournalService` fire-and-forget save.
3. **A-2** — convert the three `MapViewModel` + one `MainViewModel` inline lambdas
   to named handlers (pure consistency with `GeofenceViewModel`).
4. **B-1 / B-2 / B-3 / B-4** — only as part of a deliberate UX/heatmap polish pass.
5. **C-*** — hardening backlog; safe to defer.

Each of A-1…A-3 is an independent, single-purpose, CI-verifiable change. None
touches the deferred fragile paths (anti-meridian, AisStream reconnect) that
still need runtime verification we cannot do in-sandbox.
