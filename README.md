# Brake Filter v0.1

Brake Filter is a low-latency OpenTabletDriver 0.6.7 pre-transform filter. It combines directional movement anti-chatter with bounded, speed-sensitive braking near the end of pen movement.

The filter only processes tablet reports. It does not read applications, screen contents, beatmaps, targets, clicks, or network data.

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

## Install

Install `release\Brake-Filter-v0.1.zip` through OpenTabletDriver's plugin manager, restart OpenTabletDriver, and add `Brake Filter v0.1` as a filter. Do not stack it with another smoothing or anti-chatter filter until each filter has been tuned independently.

## Design notes

- Synchronous pre-transform processing; no timer or worker thread.
- Fast movement disables braking immediately.
- Braking is anchored to the previous raw report, preventing recursive lag accumulation.
- Anti-chatter output remains spatially bounded relative to the raw pen position.
- State resets only on an explicit out-of-range report, not ordinary far-hover reports.
- The report hot path is allocation-free under the included regression test.

## License

MIT. See `LICENSE`.
