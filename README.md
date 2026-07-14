# Brake Filter v0.2

Brake Filter is a low-latency OpenTabletDriver 0.6.7 pre-transform filter. It combines the Consistent Aim movement anti-chatter and bounded slow-movement braking with optional advanced endpoint and fast-aim stability.

The plugin is displayed simply as `Brake Filter` inside OpenTabletDriver. Version numbers are shown only on GitHub and in the assembly metadata.

## Requirements

- OpenTabletDriver 0.6.7
- .NET 8 SDK to build from source

## Normal settings

| Setting | Default | Range | Effect |
| --- | ---: | ---: | --- |
| Movement Anti-Chatter | 10 | 0-100 raw units | Removes tiny movement and reduces sideways jitter. Zero disables it. |
| Brake Strength | 0.45 | 0.00-0.95 | Steadies slow movement near a stop. Zero disables it. |
| Brake Start Speed | 90 | 1-1000 raw units/report | Braking fades in below this speed and is off at or above it. |

These settings always work, even when Advanced Features is off. The suggested starting ranges in the tooltips target a Wacom PTH-660 at 200 Hz. Other tablets and report rates may need different values.

## Advanced settings

Advanced Features is **off by default**. Enable `Advanced Features (default: off)` in OpenTabletDriver before the settings below can affect the cursor.

| Setting | Default | Range | Effect |
| --- | ---: | ---: | --- |
| Stability Radius | 0.05 mm | 0.00-0.20 mm | Adds a small physical endpoint hold to contain shake after stopping. |
| Stop Assist | 0.25 | 0.00-0.50 | Adds a brief non-recursive brake during strong endpoint deceleration. |
| Fast Aim Stability | 0.80 | 0.00-1.00 | Reduces sideways shake during fast jumps while preserving forward movement. |
| Fast Aim Threshold | 120 mm/s | 40-500 mm/s | Controls when advanced movement becomes nearly raw and direct. |

## Build and test

From PowerShell in this directory:

```powershell
.\build.ps1
```

The script restores dependencies from NuGet, builds the plugin, runs all 28 regression tests, and produces:

- `release\BrakeFilter.dll`
- `release\Brake-Filter-v0.2.zip`

## Install and set up

1. Download `Brake-Filter-v0.2.zip` from the [latest release](https://github.com/z9a17/Brake-Filter/releases/latest). Do not extract it.
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
- Increase **Stability Radius** in steps of 0.01 mm if endpoint shake remains. Decrease it if micro-corrections feel sticky.
- Lower **Fast Aim Threshold** for a snappier advanced response; raise it for more medium-speed stability.
- Test one change at a time. Values depend on the tablet resolution and report rate.

## Design notes

- Synchronous pre-transform processing; no timer or worker thread.
- Fast movement disables braking immediately.
- Braking is anchored to the previous raw report, preventing recursive lag accumulation.
- Anti-chatter output remains spatially bounded relative to the raw pen position.
- Advanced features have a separate off-by-default gate and reset cleanly when toggled.
- Stop Assist uses a bounded two-tap FIR rather than a recursive smoothing tail.
- State resets only on an explicit out-of-range report, not ordinary far-hover reports.
- The report hot path is allocation-free under the included regression test.

## License

MIT. See `LICENSE`.
