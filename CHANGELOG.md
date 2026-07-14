# Changelog

## v0.2.3

- Split the two large implementation files into focused pipeline, settings, movement, advanced integration, endpoint, state, and math files.
- Centralized duplicated finite checks, clamping, interpolation, deadzone, and spatial-leash math in `AimMath`.
- Simplified method names and the main processing flow without changing the filter's public type or OTD display name.
- Reduced the regression suite from 33 narrow tests to 11 focused core behavior, safety, allocation, and PTK-1240-scale tests.
- Kept all v0.2.2 settings, limits, defaults, and filter behavior intact.

## v0.2.2

- Expanded Movement Anti-Chatter to 1000 raw units, Brake Strength to 1.00, and Brake Start Speed to 10000 raw units per report.
- Expanded advanced Stability Radius to 1.00 mm, Stop Assist to 1.00, Fast Aim Stability to 2.00, and Fast Aim Threshold to 5000 mm/s.
- Raised the impossible-coordinate-jump guard above the full Wacom PTK-1240 tablet diagonal so legitimate high-resolution arm movement does not reset filter state.
- Kept all defaults and recommended starting ranges unchanged; the additional range is intended for high-resolution tablets and experimentation.
- Added regression coverage for 2000+ raw-unit reports and every expanded limit.

## v0.2.1

- Removed continuous spatial filtering from Stability Radius; it now applies only after a stationary endpoint is confirmed.
- Integrated Fast Aim Stability into the existing Movement Anti-Chatter stage instead of applying a second spatial filter.
- Kept the Movement Anti-Chatter leash as the single maximum positional bound during movement.
- Made Stop Assist transparent at constant speed and independent of Stability Radius.
- Added compatibility tests proving endpoint-only radius behavior, zero-setting transparency, single-stage fast stability, and bounded output.

## v0.2.0

- Combined the Consistent Aim anti-chatter/braking path with optional endpoint and fast-aim stability features.
- Added an off-by-default Advanced Features toggle.
- Added advanced Stability Radius, Stop Assist, Fast Aim Stability, and Fast Aim Threshold controls.
- Changed the OpenTabletDriver display name to `Brake Filter` without a version suffix.
- Preserved the existing `BrakeFilter.BrakeDeadzoneFilter` type identity for v0.1 profile compatibility.
- Expanded regression coverage for feature gating, endpoint behavior, report-rate consistency, spatial bounds, and allocations.

## v0.1.0

- Initial public release with movement anti-chatter and bounded speed-sensitive braking.
