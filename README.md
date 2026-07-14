# Brake Filter v0.1

Brake Filter is a low-latency OpenTabletDriver 0.6.7 pre-transform filter. It combines directional movement anti-chatter with bounded, speed-sensitive braking near the end of pen movement.

## Requirements

- OpenTabletDriver 0.6.7
- .NET 8 SDK to build from source

## Settings

| Setting | Default | Range | Effect |
| --- | ---: | ---: | --- |
| Movement Anti-Chatter | 10 | 0-100 raw units | Removes tiny movement and reduces sideways jitter. Zero disables it. |
| Brake Strength | 0.45 | 0.00-0.95 | Steadies slow movement near a stop. Zero disables it. |
| Brake Start Speed | 90 | 1-1000 raw units/report | Braking fades in below this speed and is off at or above it. |

The suggested starting ranges in the tooltips target a Wacom PTH-660 at 200 Hz. Other tablets and report rates may need different values.

## Build and test

From PowerShell in this directory:

```powershell
.\build.ps1
```

The script restores dependencies from NuGet, builds the plugin, runs all 15 regression tests, and produces:

- `release\BrakeFilter.dll`
- `release\Brake-Filter-v0.1.zip`

## Install and set up

1. Download `Brake-Filter-v0.1.zip` from the [latest release](https://github.com/z9a17/Brake-Filter/releases/latest). Do not extract it.
2. Open OpenTabletDriver and make sure your tablet is detected.
3. Open **Plugins > Open Plugin Manager**.
4. In the Plugin Manager, choose **Install plugin...** and select the downloaded ZIP.
5. Restart OpenTabletDriver if `Brake Filter v0.1` does not appear immediately.
6. Open the **Filters** tab, select `Brake Filter v0.1`, and check **Enable Brake Filter v0.1**.
7. Start with the defaults: Movement Anti-Chatter `10`, Brake Strength `0.45`, and Brake Start Speed `90`.

Use only this filter while tuning it. Stacking multiple smoothing or anti-chatter filters can add latency and make the result difficult to diagnose.

## Quick tuning

- Increase **Movement Anti-Chatter** in steps of 2 if movement still looks shaky. Decrease it if small corrections feel sticky.
- Increase **Brake Strength** in steps of 0.05 if stopping still feels unstable. Decrease it if slow aim feels heavy.
- Increase **Brake Start Speed** in steps of 10-20 if braking engages too late. Decrease it if medium-speed aim feels restrained.
- Test one change at a time. Values depend on the tablet resolution and report rate.

## Design notes

- Synchronous pre-transform processing; no timer or worker thread.
- Fast movement disables braking immediately.
- Braking is anchored to the previous raw report, preventing recursive lag accumulation.
- Anti-chatter output remains spatially bounded relative to the raw pen position.
- State resets only on an explicit out-of-range report, not ordinary far-hover reports.
- The report hot path is allocation-free under the included regression test.

## License

MIT. See `LICENSE`.
