# Brake Filter - Low-Latency Radial Dev v0.4.0-dev.1

This development branch tests a standalone OpenTabletDriver 0.6.7 radial smoothing filter. It aims to preserve the slow-movement stability of the supplied AbstractQbit Radial Follow profile while releasing substantially more lag during fast jumps and direction reversals.

> **Experimental prerelease:** Stable Brake Filter remains on `main`. This plugin has a separate assembly, CLR type, OTD name, and saved-profile identity, so it does not silently replace or inherit the stable filter's configuration.

> **Development disclosure:** Parts of this codebase and its documentation were written, reviewed, and refactored with assistance from GPT 5.6-Sol. Review and test the implementation like any other third-party plugin.

## Design

- Physical millimetre-based radial filtering in tablet space.
- No EMA, timer, worker thread, prediction, or report buffer.
- Slow movement retains a bounded inner deadzone and radial smoothing tail.
- Per-report physical movement progressively shrinks both lag radii during fast movement.
- A deliberate direction reversal releases old-direction lag immediately, while reversals contained inside the chatter radius remain held.
- Output never predicts beyond the raw pen position and never exceeds the configured slow-movement Outer Radius.
- The report hot path is allocation-free.

The implementation is original MIT-licensed code. AbstractQbit's public documentation and the user's configured behavior were used as a behavioral reference; no AbstractQbit source code or binary is included.

## Settings

The defaults reproduce the user's supplied profile values and add one low-latency control:

| Setting | Default | Purpose |
| --- | ---: | --- |
| Outer Radius | 0.7039 mm | Maximum slow-movement lag radius. |
| Inner Radius | 0.302 mm | Chatter deadzone around the current output. |
| Smoothing | 0.302 | Slow-movement catch-up strength. Higher is smoother and slower. |
| Soft Knee | 0.603 | Softness of the transition into fast release. |
| Smoothing Leak | 0.201 | Amount of stability retained during fast release. |
| Fast Release | 0.75 | How strongly fast movement sheds radial lag. |

Every OTD setting has a concise hover tooltip containing its range, default, and practical effect.

## Behavioral comparison

A local deterministic sweep compared this branch with the installed AbstractQbit Radial Follow 0.3.0 DLL using the five values above:

| Movement | AbstractQbit average lag | Radial Dev average lag |
| --- | ---: | ---: |
| 0.10 mm/report | 0.3429 mm | 0.3431 mm |
| 0.30 mm/report | 0.4284 mm | 0.3963 mm |
| 1.20 mm/report | 0.7000 mm | 0.2293 mm |

On the same machine and synthetic hot-path workload, AbstractQbit's core measured about 140 ns/report and this processor about 29 ns/report. These are local microbenchmarks, not universal hardware guarantees; the main latency reduction comes from the spatial fast-release behavior rather than CPU time alone.

## Install

1. Download `Brake-Filter-Radial-Dev-v0.4.0-dev.1.zip` from the prerelease. Do not extract it.
2. Open **Plugins > Open Plugin Manager** in OpenTabletDriver.
3. Choose **Install plugin...** and select the ZIP.
4. In **Filters**, enable `Brake Filter - Low-Latency Radial Dev`.
5. Disable other position smoothing filters while comparing them.

Installing the ZIP does not automatically enable or configure the filter.

## Build and test

Run:

```powershell
.\build.ps1
```

The build runs 12 focused tests and produces:

- `release\BrakeFilter.RadialDev.dll`
- `release\Brake-Filter-Radial-Dev-v0.4.0-dev.1.zip`
- `release\SHA256SUMS.txt`

## License

MIT. See `LICENSE`.
