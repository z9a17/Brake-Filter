# Changelog

## v0.3.0

- Removed a mathematically unreachable Stability Radius branch from endpoint detection.
- Clarified the endpoint state machine with explicit stationary-window naming, documented transitions, and named tuning ratios.
- Reused the speed-drop calculation shared by endpoint detection and Stop Assist.
- Skipped Stop Assist calculations when its strength is zero and skipped offset limiting when no braking is active.
- Replaced the settled endpoint distance check with an equivalent squared-distance comparison.
- Removed redundant advanced-state clearing and reused the period estimator's existing return value.
- Documented profile-type compatibility, motion-frame validity, and the report-period ring-buffer invariant.
- Made build artifact versioning and reported test counts derive from their sources of truth.
- Added regression coverage for the exact stop-speed threshold.

## v0.2.6

- Fixed the first small coordinate change after a pause being divided by a stale short report period and therefore appearing much faster than it was.
- Marked that single temporally ambiguous change as unavailable to Fast Aim Stability and Stop Assist while still emitting its report immediately.
- Resumed normal velocity sampling on the next changed coordinate without adding buffering or cursor latency.
- Added regression coverage for the long-pause case and sampling recovery.

## v0.2.5

- Separated transport-report timing from actual changed-coordinate motion samples.
- Prevented identical X/Y reports used for pressure or button updates from appearing as artificial zero-speed deceleration.
- Kept every report synchronous and unbuffered while omitting coordinate duplicates only from velocity and Stop Assist decisions.
- Added a rolling changed-coordinate period estimate so alternating short/long host arrival intervals do not modulate physical speed.
- Added deterministic regression coverage for host-timing jitter, duplicate coordinate reports, pressure/button preservation, and false Stop Assist activation.

## v0.2.4

- Changed Advanced velocity to use physical distance per report as the primary motion signal instead of each individual host-timed interval.
- Added an allocation-free 32-report rolling period estimator to preserve the existing mm/s setting semantics across tablet report rates.
- Made the timing estimate resistant to USB report batching, very short arrival intervals, and host scheduling pauses.
- Kept position reports completely unbuffered; the estimator changes only velocity calibration and adds no cursor-report latency.
- Added deterministic coverage for steady rates, burst delivery, pauses, rate changes, reset behavior, and allocations.

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
