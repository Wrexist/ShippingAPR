# ShippingAPR — TODO / Deferred Work

## Deferred: Fuel-model + IMO-correct CII recalibration
**Status:** Intentionally deferred — needs real-world ground truth before touching.

**Problem.** `EmissionsEstimatorService` rates Carbon Intensity (CII) with fixed
absolute g-CO₂/(dwt·nm) boundaries rather than the IMO MEPC.353/354(78)
reference-line + dd-vector method (size/type-scaled boundaries). It's also
entangled with the fuel-consumption model: the `Profiles` reference fuel values
appear to overestimate real consumption, which would push most vessels to a "D/E"
rating once a correct reference-line CII is applied.

**Why deferred.** Recalibrating a physics model "by feel" risks *regressing*
accuracy invisibly — unit tests can prove consistency, not correctness. We need:
- (a) sign-off on specific reference fuel figures (t/h at design speed by ship
  class), or
- (b) a citable data source (e.g. IMO Fourth GHG Study fuel tables, real noon
  reports) to calibrate `Profiles` and the DWT proxy against.

**Plan when unblocked.**
1. Recalibrate `Profiles` reference fuel (and revisit the L·B·T·Cb DWT proxy).
2. Implement IMO reference line `CII_ref = a · Capacity^(-c)` per ship type, with
   the dd-vector rating boundaries (A–E).
3. Map the coarse AIS `VesselType` enum to IMO ship categories; keep a simple
   absolute-intensity fallback for non-regulated types (CII only applies to
   certain cargo-carrying ships ≥5000 GT).
4. Tests: assert size-awareness (two ships at their respective reference lines
   both rate "C") and that a ship far above/below its line rates E/A.

See: `src/ShippingAPR.Services/EmissionsEstimatorService.cs`
(`CalculateCiiRating`, `Profiles`, `EstimateFuelConsumption`).

---

## Deferred from MASTER-AUDIT-2026-06-09.md (need design / not safe blind)

### Anti-meridian bounding boxes (audit P0 #5)
`BoundingBox` rejects `MinLongitude > MaxLongitude`, so a tracking area crossing
±180° can't be represented; the viewport clamps and silently subscribes to the
wrong region. A correct fix is **not** a one-liner: `Contains`, the range math
(`Max−Min`), and `ToAisStreamFormat` all assume `min ≤ max`, and AisStream would
misread a wrapped box — it must be **split into two boxes at the subscription
layer** (AisStream accepts multiple `BoundingBoxes`). Touches the flagged
coordinate + feed path and has zero impact on non-Pacific use, so deferred until
it can be designed + tested deliberately. Plan: keep `BoundingBox` as-is; detect
wrap at the subscribe call and emit two boxes; add a `BoundingBox.Split()` helper
with tests (Bering Strait, Fiji).

### Tests for AisStreamClient reconnect/receive loop (audit #24)
The single most critical untested unit (`TryReconnectAsync` / `ReceiveLoopAsync`
/ error-frame handling). Needs a seam: extract an `IWebSocket` (or
`Func<ClientWebSocket>`) so a fake socket can script close frames, errors, and
auth-reject behaviour. Then cover: backoff+jitter, reconnect on remote close,
no infinite loop on repeated failure, error-frame → `Error` status. Also add
coverage for `DatalasticClient`/`DataDockedClient` parsing and
`AisProviderFactory` + `FallbackAisProvider` lifecycle. Refactor-then-test;
don't bolt tests onto the current concrete `ClientWebSocket`.

### Minor / deliberately not done (low value or risk > reward)
- `LocalSettingsStore` bare `catch { return false; }` has no logging — but the
  real gap (silent failure) is now surfaced to the user via a dialog when the
  API-key save returns false. Internal logging in a static IO helper is awkward;
  skipped.
- `GeofenceViewModel` doesn't unsubscribe `GeofencesChanged` / isn't `IDisposable`
  — only leaks if the VM is recreated, which it isn't (singleton per window).
- `SearchViewModel` keystroke search isn't debounced — cheap in-memory search;
  adding a timer would break the synchronous `SearchViewModelTests` contract for
  no present benefit. Revisit if stores grow large.
