# Changelog

## v0.2.0

- Combined the Consistent Aim anti-chatter/braking path with optional endpoint and fast-aim stability features.
- Added an off-by-default Advanced Features toggle.
- Added advanced Stability Radius, Stop Assist, Fast Aim Stability, and Fast Aim Threshold controls.
- Changed the OpenTabletDriver display name to `Brake Filter` without a version suffix.
- Preserved the existing `BrakeFilter.BrakeDeadzoneFilter` type identity for v0.1 profile compatibility.
- Expanded regression coverage for feature gating, endpoint behavior, report-rate consistency, spatial bounds, and allocations.

## v0.1.0

- Initial public release with movement anti-chatter and bounded speed-sensitive braking.
