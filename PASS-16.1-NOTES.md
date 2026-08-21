# DISRoute — Pass 16.1 Notes

**Date:** 2026-08-20  
**Base:** Pass 16 (`a675debfe443799979cbdb33ee5102554b57128f6816142deb9dfb8d01643159`)  
**Theme:** Station delivery-path cross-station isolation regression hardening

## Change

- No production behavior was changed.
- Strengthened `DISRoute.SelfTest` so station scoping is proven with two real stations rather than inferred from a single-station result set.
- The test now creates a Station 2 incident, commits Station Display / Run Sheet / Station Alert delivery paths for Station 2, and verifies:
  - Station 1's delivery-path projection contains none of Station 2's incident paths;
  - Station 2 receives exactly the expected three paths for its own incident; and
  - Station 2's delivery-path projection contains none of Station 1's incident paths.

## Verification

- Affected .NET 10 Release build (`DISRoute.SelfTest` and dependencies): **PASS — 0 warnings / 0 errors**.
- `DISRoute.SelfTest`: **PASS — incidents=6, units=6, audit=82, alerts=2**.
- Source verifier: **PASS — 98 hand-written C# files, 17 projects, 233 API routes, 13 migrations**.
- Production source is unchanged from Pass 16; therefore the previously green Pass 16 runtime and four-client startup baselines are not invalidated by this test-only checkpoint.

## Scope

This is a verification-only checkpoint. It does not consume the planned Pass 17 Supervisor / Mayday / radio-emergency implementation pass.
