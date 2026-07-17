# Brake Filter v0.3.2

Brake Filter is a low-latency OpenTabletDriver 0.6.7 pre-transform filter. It combines the Consistent Aim movement anti-chatter and bounded slow-movement braking with optional advanced endpoint and fast-aim stability.

The plugin is displayed simply as `Brake Filter` inside OpenTabletDriver. Version numbers are shown only on GitHub and in the assembly metadata.

> **Development disclosure:** Parts of this codebase and its documentation were written, reviewed, and refactored with assistance from GPT 5.6-Sol. Contributors should evaluate the implementation and tests as they would for any other third-party plugin rather than assuming AI-assisted code is correct.

## Requirements

- OpenTabletDriver 0.6.7
- .NET 8 SDK to build from source

## Normal settings

| Setting | Default | Range | Effect |
| --- | ---: | ---: | --- |
| Movement Anti-Chatter | 10 | 0-1000 raw units | Removes tiny movement and reduces sideways jitter. Zero disables it. |
| Brake Strength | 0.45 | 0.00-1.00 | Steadies slow movement near a stop. Zero disables it. |
| Brake Start Speed | 90 | 1-10000 raw units/report | Braking fades in below this speed and is off at or above it. |

These settings always work, even when Advanced Features is off. The suggested starting ranges in the tooltips target a Wacom PTH-660 at 200 Hz. Other tablets and report rates may need different values. 

## Advanced settings

Advanced Features is **off by default**. Enable `Advanced Features (default: off)` in OpenTabletDriver before the settings below can affect the cursor.

| Setting | Default | Range | Effect |
| --- | ---: | ---: | --- |
| Stability Radius | 0.05 mm | 0.00-1.00 mm | Holds a confirmed stationary endpoint; it does not filter movement before the stop. |
| Stop Assist | 0.25 | 0.00-1.00 | Adds a brief non-recursive brake during strong endpoint deceleration. |
| Fast Aim Stability | 0.80 | 0.00-2.00 | Adds speed-scaled sideways strength inside the existing Movement Anti-Chatter stage. |
| Fast Aim Threshold | 120 mm/s | 40-5000 mm/s | Sets when fast lateral stability reaches full strength and calibrates Stop Assist. |

The defaults and recommended starting ranges did not change. The expanded upper limits are for unusual tablet resolutions, large areas, report rates, or deliberate experimentation. Very high Movement Anti-Chatter, Stability Radius, or Fast Aim Stability values can suppress intended corrections even though the implementation remains spatially bounded.

## How the settings work together

- **Movement Anti-Chatter** is the only general-purpose chatter filter and owns the maximum spatial leash.
- **Fast Aim Stability** modifies that same anti-chatter calculation during fast movement. It is not another smoothing pass and cannot increase the leash.
- **Brake Strength** acts on ordinary slow movement. **Stop Assist** acts only on a strong speed drop after an approach, so constant-speed movement is unaffected.
- **Stability Radius** remains transparent during continuous movement. It is consulted only to confirm and hold a stationary endpoint.
- Setting every advanced strength to zero makes the enabled advanced path transparent.

## Build and test

From PowerShell in this directory:

```powershell
.\build.ps1
```

The script restores dependencies from NuGet, builds the plugin, runs 14 focused core tests, and produces:

- `release\BrakeFilter.dll`
- `release\Brake-Filter-v0.3.2.zip`

## Install and set up

1. Download `Brake-Filter-v0.3.2.zip` from the [latest release](https://github.com/z9a17/Brake-Filter/releases/latest). Do not extract it.
2. Open OpenTabletDriver and make sure your tablet is detected.
3. Open **Plugins > Open Plugin Manager**.
4. In the Plugin Manager, choose **Install plugin...** and select the downloaded ZIP.
5. Restart OpenTabletDriver if `Brake Filter` does not appear immediately.
6. Open the **Filters** tab, select `Brake Filter`, and check **Enable Brake Filter**.
7. Start with the defaults: Movement Anti-Chatter `10`, Brake Strength `0.45`, and Brake Start Speed `90`.
8. Leave Advanced Features off at first. Enable it only if you want the additional Stop Assist and fast-aim controls.

Use only this filter while tuning it. Stacking multiple smoothing or anti-chatter filters can add latency and make the result difficult to diagnose.

## Quick tuning

- Increase **Movement Anti-Chatter** in steps of 2 if movement still looks shaky. Decrease it if small corrections feel sticky.
- Increase **Brake Strength** in steps of 0.05 if stopping still feels unstable. Decrease it if slow aim feels heavy.
- Increase **Brake Start Speed** in steps of 10-20 if braking engages too late. Decrease it if medium-speed aim feels restrained.
- If the normal settings are not enough at endpoints, enable **Advanced Features** and start with its defaults.
- Increase **Stop Assist** in steps of 0.05 for stronger endpoint braking.
- Increase **Stability Radius** in steps of 0.01 mm if a settled endpoint releases too easily. It no longer affects normal micro-corrections before a stop.
- Increase **Fast Aim Stability** for straighter fast jumps; decrease it if curves feel constrained.
- Lower **Fast Aim Threshold** to engage the added fast stability sooner; raise it to reserve it for faster movement.
- Test one change at a time. Values depend on the tablet resolution and report rate.

## Design notes

- Synchronous pre-transform processing; no timer or worker thread.
- Fast movement disables braking immediately.
- Braking is anchored to the previous raw report, preventing recursive lag accumulation.
- Anti-chatter output remains spatially bounded relative to the raw pen position.
- Movement Anti-Chatter and Fast Aim Stability run in one bounded stage rather than two stacked spatial filters.
- Advanced velocity uses physical distance between changed tablet coordinates as its primary signal.
- Identical X/Y reports still pass through immediately for pressure and button updates, but do not become false zero-speed motion samples.
- The first coordinate change after an unusually long stationary interval passes through without driving velocity-based features; normal sampling resumes on the next change.
- Separate rolling estimates track report arrival time and the interval between changed coordinates, resisting USB batching and host scheduling jitter.
- The period estimate never buffers or averages positions, so it adds no cursor-report latency.
- Stability Radius is endpoint-only and remains transparent during continuous movement.
- Advanced features have a separate off-by-default gate and reset cleanly when toggled.
- Stop Assist uses a bounded two-tap FIR rather than a recursive smoothing tail.
- State resets only on an explicit out-of-range report, not ordinary far-hover reports.
- The impossible-jump guard sits above the full PTK-1240 coordinate diagonal, so valid high-resolution full-area reports do not reset filter state.
- The report hot path is allocation-free under the included regression test.

## Source layout

- `BrakeDeadzoneFilter.cs` contains the report pipeline and state lifecycle.
- `BrakeDeadzoneFilter.Settings.cs` contains the public OTD settings and tooltips.
- `BrakeDeadzoneFilter.Movement.cs` contains directional anti-chatter and braking.
- `BrakeDeadzoneFilter.Advanced.cs` connects tablet scaling and advanced processing.
- `AdvancedAimEngine.cs` contains the advanced processing flow.
- `AdvancedAimEngine.Endpoint.cs`, `.State.cs`, and `.Settings.cs` separate endpoint detection, state transitions, and configuration.
- `PositionMotionEstimator.cs` separates report timing from changed-coordinate velocity samples.
- `ReportPeriodEstimator.cs` provides allocation-free rolling-period calibration.
- `AimMath.cs` contains the shared allocation-free numeric helpers.

## License

MIT. See `LICENSE`.
