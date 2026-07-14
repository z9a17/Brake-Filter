# Changelog

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
