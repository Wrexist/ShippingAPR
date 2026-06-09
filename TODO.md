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
