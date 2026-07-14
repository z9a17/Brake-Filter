using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;
using BrakeFilter;

internal static class Program
{
    private static readonly List<string> Failures = new();

    private static int Main()
    {
        Run("Documented defaults are real object defaults", DefaultsAreInitialized);
        Run("Assembly is v0.2.1 and OTD name has no version", VersionMetadataIsV021);
        Run("Setting labels expose defaults and units", SettingMetadataIsVisible);
        Run("Advanced features are off by default and visibly gated", AdvancedMetadataIsGated);
        Run("First tablet report passes through unchanged", FirstReportPassesThrough);
        Run("Tiny jitter is held but cannot drift beyond the deadzone", TinyJitterIsBounded);
        Run("Slow braking stays bounded instead of accumulating lag", SlowBrakeDoesNotAccumulateLag);
        Run("Fast jumps bypass braking immediately", FastJumpBypassesBrake);
        Run("Out-of-range reports clear stale history", OutOfRangeClearsHistory);
        Run("Far-hover proximity reports remain filtered", FarHoverReportsRemainFiltered);
        Run("Large coordinate jumps reset safely", LargeJumpResetsSafely);
        Run("Invalid settings cannot poison the filter", InvalidSettingsAreSanitized);
        Run("Invalid positions reset state before the next valid report", InvalidPositionResetsState);
        Run("Anti-chatter no longer depends on Brake Start Speed", AntichatterIsIndependentFromBrakeSpeed);
        Run("Disabled advanced settings cannot change basic output", DisabledAdvancedSettingsAreTransparent);
        Run("Enabling advanced features starts without a position jump", EnablingAdvancedStartsCleanly);
        Run("Incomplete OTD tablet references fall back safely", IncompleteTabletReferenceIsSafe);
        Run("Stability Radius is transparent during continuous movement", StabilityRadiusIsEndpointOnly);
        Run("Advanced stationary endpoint chatter is held", AdvancedStationaryChatterIsHeld);
        Run("Advanced fast jumps stay direct", AdvancedFastJumpsStayDirect);
        Run("Fast Aim Stability shares the bounded anti-chatter stage", FastAimStabilityUsesSingleBoundedStage);
        Run("Zero advanced settings are transparent", ZeroAdvancedSettingsAreTransparent);
        Run("Stop Assist is transparent at constant speed", StopAssistIsConstantSpeedTransparent);
        Run("Advanced stop assist reacts to endpoint deceleration", AdvancedStopAssistReacts);
        Run("Advanced endpoint hold releases on new intent", AdvancedHoldReleases);
        Run("Advanced output remains spatially bounded", AdvancedOutputIsLeashed);
        Run("Advanced report-rate changes stay consistent", AdvancedReportRatesStayConsistent);
        Run("Advanced hot path performs no managed allocations", AdvancedHotPathDoesNotAllocate);
        Run("Integrated advanced plugin path performs no managed allocations", AdvancedPluginHotPathDoesNotAllocate);
        Run("Finite randomized input never produces invalid output", RandomizedInputStaysFinite);
        Run("The report hot path performs no managed allocations", HotPathDoesNotAllocate);

        if (Failures.Count == 0)
        {
            Console.WriteLine("All 31 tests passed.");
            PrintHotPathBenchmark();
            return 0;
        }

        Console.Error.WriteLine($"{Failures.Count} test(s) failed:");
        foreach (string failure in Failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }

        return 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception exception)
        {
            Failures.Add($"{name}: {exception.Message}");
            Console.Error.WriteLine($"FAIL: {name}");
        }
    }

    private static void DefaultsAreInitialized()
    {
        var filter = new BrakeDeadzoneFilter();
        Equal(10f, filter.MovementAntichatter);
        Equal(0.45f, filter.BrakeSmoothing);
        Equal(90f, filter.BrakeSpeed);
        True(!filter.AdvancedFeatures, "Advanced features must default to off.");
        Equal(0.05f, filter.StabilityRadius);
        Equal(0.25f, filter.StopAssist);
        Equal(0.80f, filter.FastAimStability);
        Equal(120f, filter.FastAimThreshold);
    }

    private static void VersionMetadataIsV021()
    {
        Version? version = typeof(BrakeDeadzoneFilter).Assembly.GetName().Version;
        True(version == new Version(0, 2, 1, 0), $"Unexpected assembly version: {version}");

        PluginNameAttribute? name = typeof(BrakeDeadzoneFilter)
            .GetCustomAttributes(typeof(PluginNameAttribute), inherit: false)
            .Cast<PluginNameAttribute>()
            .SingleOrDefault();
        True(name is not null, "PluginNameAttribute is missing.");
        True(name!.Name == "Brake Filter", $"Unexpected plugin name: {name.Name}");
    }

    private static void SettingMetadataIsVisible()
    {
        CheckSettingMetadata(
            nameof(BrakeDeadzoneFilter.MovementAntichatter),
            expectedLabelText: "default: 10",
            expectedDefault: 10f,
            expectedUnit: "raw units");
        CheckSettingMetadata(
            nameof(BrakeDeadzoneFilter.BrakeSmoothing),
            expectedLabelText: "default: 0.45",
            expectedDefault: 0.45f,
            expectedUnit: "ratio");
        CheckSettingMetadata(
            nameof(BrakeDeadzoneFilter.BrakeSpeed),
            expectedLabelText: "default: 90",
            expectedDefault: 90f,
            expectedUnit: "raw units/report");
    }

    private static void AdvancedMetadataIsGated()
    {
        PropertyInfo toggle = typeof(BrakeDeadzoneFilter).GetProperty(nameof(BrakeDeadzoneFilter.AdvancedFeatures))
            ?? throw new InvalidOperationException("AdvancedFeatures is missing.");
        BooleanPropertyAttribute boolean = toggle.GetCustomAttribute<BooleanPropertyAttribute>()
            ?? throw new InvalidOperationException("AdvancedFeatures is not a visible boolean setting.");
        DefaultPropertyValueAttribute defaultValue = toggle.GetCustomAttribute<DefaultPropertyValueAttribute>()
            ?? throw new InvalidOperationException("AdvancedFeatures has no serialized default.");
        True(boolean.DisplayName.Contains("default: off", StringComparison.OrdinalIgnoreCase),
            "The advanced toggle does not visibly show its default.");
        True(defaultValue.Value is false, "AdvancedFeatures does not serialize as off by default.");

        string[] advancedSettings =
        {
            nameof(BrakeDeadzoneFilter.StabilityRadius),
            nameof(BrakeDeadzoneFilter.StopAssist),
            nameof(BrakeDeadzoneFilter.FastAimStability),
            nameof(BrakeDeadzoneFilter.FastAimThreshold)
        };
        foreach (string setting in advancedSettings)
        {
            PropertyInfo property = typeof(BrakeDeadzoneFilter).GetProperty(setting)
                ?? throw new InvalidOperationException($"Missing advanced setting: {setting}");
            SliderPropertyAttribute slider = property.GetCustomAttribute<SliderPropertyAttribute>()
                ?? throw new InvalidOperationException($"{setting} is not a slider.");
            ToolTipAttribute tooltip = property.GetCustomAttribute<ToolTipAttribute>()
                ?? throw new InvalidOperationException($"{setting} has no tooltip.");
            True(slider.DisplayName.StartsWith("Advanced -", StringComparison.Ordinal),
                $"{setting} is not visibly marked as advanced.");
            True(tooltip.ToolTip.Contains("MUST BE ON", StringComparison.Ordinal),
                $"{setting} does not explain its gate.");
        }
    }

    private static void FirstReportPassesThrough()
    {
        var filter = CreateFilter(out List<IDeviceReport> emitted);
        FakeTabletReport input = Report(123f, 456f);

        filter.Consume(input);

        Equal(new Vector2(123f, 456f), input.Position);
        True(ReferenceEquals(input, emitted.Single()), "The original report object was not emitted.");
    }

    private static void TinyJitterIsBounded()
    {
        var filter = CreateFilter(out _);
        filter.MovementAntichatter = 10f;
        filter.BrakeSmoothing = 0f;
        FakeTabletReport report = Report(0f, 0f);
        filter.Consume(report);

        for (int x = 1; x <= 40; x++)
        {
            report = Report(x, 0f);
            filter.Consume(report);
            float lag = x - report.Position.X;
            True(lag >= -0.001f && lag <= 10.001f, $"Lag escaped the 10-unit bound at x={x}: {lag}");
        }
    }

    private static void SlowBrakeDoesNotAccumulateLag()
    {
        var filter = CreateFilter(out _);
        filter.MovementAntichatter = 0f;
        filter.BrakeSmoothing = 0.45f;
        filter.BrakeSpeed = 90f;
        filter.Consume(Report(0f, 0f));

        FakeTabletReport report = Report(0f, 0f);
        for (int step = 1; step <= 100; step++)
        {
            report = Report(step * 11f, 0f);
            filter.Consume(report);
        }

        float lag = 1100f - report.Position.X;
        True(lag > 0f && lag < 5.1f, $"Expected bounded one-report lag, got {lag}.");
    }

    private static void FastJumpBypassesBrake()
    {
        var filter = CreateFilter(out _);
        filter.MovementAntichatter = 0f;
        filter.BrakeSmoothing = 0.95f;
        filter.BrakeSpeed = 90f;
        filter.Consume(Report(0f, 0f));

        FakeTabletReport jump = Report(200f, 0f);
        filter.Consume(jump);

        Equal(new Vector2(200f, 0f), jump.Position);
    }

    private static void OutOfRangeClearsHistory()
    {
        var filter = CreateFilter(out _);
        filter.MovementAntichatter = 0f;
        filter.BrakeSmoothing = 0.95f;
        filter.BrakeSpeed = 1000f;
        filter.Consume(Report(0f, 0f));
        filter.Consume(Report(20f, 0f));

        filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));
        FakeTabletReport reentry = Report(25f, 0f);
        filter.Consume(reentry);

        Equal(new Vector2(25f, 0f), reentry.Position);
    }

    private static void FarHoverReportsRemainFiltered()
    {
        var filter = CreateFilter(out _);
        filter.MovementAntichatter = 0f;
        filter.BrakeSmoothing = 0.95f;
        filter.BrakeSpeed = 1000f;

        var first = new FakeProximityTabletReport { Position = Vector2.Zero, NearProximity = false };
        filter.Consume(first);

        var second = new FakeProximityTabletReport
        {
            Position = new Vector2(20f, 0f),
            NearProximity = false
        };
        filter.Consume(second);

        True(second.Position.X < 20f, "Far-hover input bypassed filtering or reset filter history.");
    }

    private static void LargeJumpResetsSafely()
    {
        var filter = CreateFilter(out _);
        filter.Consume(Report(0f, 0f));

        FakeTabletReport jump = Report(5001f, 0f);
        filter.Consume(jump);

        Equal(new Vector2(5001f, 0f), jump.Position);
    }

    private static void InvalidSettingsAreSanitized()
    {
        var filter = new BrakeDeadzoneFilter
        {
            MovementAntichatter = float.NaN,
            BrakeSmoothing = float.PositiveInfinity,
            BrakeSpeed = float.NegativeInfinity
        };

        Equal(10f, filter.MovementAntichatter);
        Equal(0.45f, filter.BrakeSmoothing);
        Equal(90f, filter.BrakeSpeed);

        filter.MovementAntichatter = 10000f;
        filter.BrakeSmoothing = 2f;
        filter.BrakeSpeed = 0f;
        Equal(100f, filter.MovementAntichatter);
        Equal(0.95f, filter.BrakeSmoothing);
        Equal(1f, filter.BrakeSpeed);
    }

    private static void InvalidPositionResetsState()
    {
        var filter = CreateFilter(out _);
        filter.Consume(Report(0f, 0f));
        filter.Consume(Report(20f, 0f));
        filter.Consume(Report(float.NaN, 0f));

        FakeTabletReport valid = Report(21f, 0f);
        filter.Consume(valid);

        Equal(new Vector2(21f, 0f), valid.Position);
    }

    private static void AntichatterIsIndependentFromBrakeSpeed()
    {
        var filter = CreateFilter(out _);
        filter.MovementAntichatter = 10f;
        filter.BrakeSmoothing = 0f;
        filter.BrakeSpeed = 1000f;
        filter.Consume(Report(0f, 0f));
        filter.Consume(Report(100f, 0f));

        FakeTabletReport diagonal = Report(200f, 5f);
        filter.Consume(diagonal);

        True(diagonal.Position.Y < 3f, $"Sideways jitter was not reduced: Y={diagonal.Position.Y}");
    }

    private static void DisabledAdvancedSettingsAreTransparent()
    {
        var baseline = new BrakeDeadzoneFilter();
        var changed = new BrakeDeadzoneFilter
        {
            AdvancedFeatures = false,
            StabilityRadius = 0.20f,
            StopAssist = 0.50f,
            FastAimStability = 1f,
            FastAimThreshold = 40f
        };

        Vector2[] points =
        {
            Vector2.Zero,
            new(5f, 2f),
            new(100f, 3f),
            new(111f, 3.5f),
            new(111.5f, 3.3f),
            new(-200f, 50f)
        };
        foreach (Vector2 point in points)
        {
            FakeTabletReport baselineReport = Report(point.X, point.Y);
            FakeTabletReport changedReport = Report(point.X, point.Y);
            baseline.Consume(baselineReport);
            changed.Consume(changedReport);
            Equal(baselineReport.Position, changedReport.Position);
        }
    }

    private static void EnablingAdvancedStartsCleanly()
    {
        var filter = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 0f,
            BrakeSmoothing = 0f
        };
        filter.Consume(Report(0f, 0f));
        filter.AdvancedFeatures = true;

        FakeTabletReport firstAdvancedReport = Report(100f, 25f);
        filter.Consume(firstAdvancedReport);
        Equal(new Vector2(100f, 25f), firstAdvancedReport.Position);
    }

    private static void IncompleteTabletReferenceIsSafe()
    {
        var filter = new BrakeDeadzoneFilter { AdvancedFeatures = true };
        TabletReference incomplete = Activator.CreateInstance<TabletReference>();
        filter.TabletReference = incomplete;

        FakeTabletReport first = Report(123f, 456f);
        filter.Consume(first);
        Equal(new Vector2(123f, 456f), first.Position);
    }

    private static void StabilityRadiusIsEndpointOnly()
    {
        var noRadius = new AdvancedAimEngine { StabilityRadius = 0f, StopAssist = 0f };
        var largeRadius = new AdvancedAimEngine { StabilityRadius = 0.20f, StopAssist = 0f };
        noRadius.Reset(Vector2.Zero);
        largeRadius.Reset(Vector2.Zero);

        for (int index = 1; index <= 400; index++)
        {
            Vector2 input = new(index * 0.05f, index * 0.01f);
            Equal(input, noRadius.Process(input, 0.005f), 0.000001f);
            Equal(input, largeRadius.Process(input, 0.005f), 0.000001f);
        }

        True(!largeRadius.IsSettled,
            "Directed continuous movement was incorrectly classified as an endpoint.");
    }

    private static void AdvancedStationaryChatterIsHeld()
    {
        var engine = new AdvancedAimEngine();
        engine.Reset(Vector2.Zero);
        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;
        for (int index = 0; index < 200; index++)
        {
            float x = (index & 1) == 0 ? 0.020f : -0.020f;
            Vector2 output = engine.Process(new Vector2(x, 0f), 0.005f);
            if (index >= 20)
            {
                minimum = MathF.Min(minimum, output.X);
                maximum = MathF.Max(maximum, output.X);
            }
        }

        True(engine.IsSettled, "Advanced endpoint hold never settled.");
        True(maximum - minimum < 0.000001f,
            $"Settled endpoint still moved by {maximum - minimum} mm.");
    }

    private static void AdvancedFastJumpsStayDirect()
    {
        var engine = new AdvancedAimEngine();
        engine.Reset(Vector2.Zero);
        Vector2 output = engine.Process(new Vector2(8f, 0f), 0.005f);
        True(8f - output.X <= 0.012f, $"Advanced fast jump lagged by {8f - output.X} mm.");
    }

    private static void FastAimStabilityUsesSingleBoundedStage()
    {
        var baseline = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 10f,
            BrakeSmoothing = 0f,
            AdvancedFeatures = true,
            StabilityRadius = 0f,
            StopAssist = 0f,
            FastAimStability = 0f,
            FastAimThreshold = 40f
        };
        var stabilized = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 10f,
            BrakeSmoothing = 0f,
            AdvancedFeatures = true,
            StabilityRadius = 0f,
            StopAssist = 0f,
            FastAimStability = 1f,
            FastAimThreshold = 40f
        };

        baseline.Consume(Report(0f, 0f));
        stabilized.Consume(Report(0f, 0f));
        float baselineEnergy = 0f;
        float stabilizedEnergy = 0f;
        for (int index = 1; index <= 100; index++)
        {
            float y = (index & 1) == 0 ? 15f : -15f;
            Vector2 raw = new(index * 100f, y);
            FakeTabletReport baselineReport = Report(raw.X, raw.Y);
            FakeTabletReport stabilizedReport = Report(raw.X, raw.Y);
            baseline.Consume(baselineReport);
            stabilized.Consume(stabilizedReport);
            baselineEnergy += MathF.Abs(baselineReport.Position.Y);
            stabilizedEnergy += MathF.Abs(stabilizedReport.Position.Y);
            True(Vector2.Distance(stabilizedReport.Position, raw) <= 10.001f,
                "Fast Aim Stability escaped the normal Movement Anti-Chatter leash.");
        }

        True(stabilizedEnergy < baselineEnergy,
            $"Fast Aim Stability did not reduce lateral energy: {stabilizedEnergy} vs {baselineEnergy}.");
    }

    private static void ZeroAdvancedSettingsAreTransparent()
    {
        var basic = new BrakeDeadzoneFilter();
        var advancedZero = new BrakeDeadzoneFilter
        {
            AdvancedFeatures = true,
            StabilityRadius = 0f,
            StopAssist = 0f,
            FastAimStability = 0f
        };

        for (int index = 0; index < 200; index++)
        {
            Vector2 raw = new(index * 17f, (index % 7) * 3f);
            FakeTabletReport basicReport = Report(raw.X, raw.Y);
            FakeTabletReport advancedReport = Report(raw.X, raw.Y);
            basic.Consume(basicReport);
            advancedZero.Consume(advancedReport);
            Equal(basicReport.Position, advancedReport.Position, 0.0001f);
        }
    }

    private static void StopAssistIsConstantSpeedTransparent()
    {
        var assisted = new AdvancedAimEngine { StabilityRadius = 0f, StopAssist = 0.50f };
        assisted.Reset(Vector2.Zero);
        for (int index = 1; index <= 200; index++)
        {
            Vector2 input = new(index, 0f);
            Equal(input, assisted.Process(input, 0.005f), 0.000001f);
        }
    }

    private static void AdvancedStopAssistReacts()
    {
        var assisted = new AdvancedAimEngine { StopAssist = 0.50f };
        var unassisted = new AdvancedAimEngine { StopAssist = 0f };
        assisted.Reset(Vector2.Zero);
        unassisted.Reset(Vector2.Zero);
        for (int index = 1; index <= 8; index++)
        {
            Vector2 raw = new(index, 0f);
            assisted.Process(raw, 0.005f);
            unassisted.Process(raw, 0.005f);
        }

        Vector2 endpoint = new(8.15f, 0f);
        Vector2 assistedOutput = assisted.Process(endpoint, 0.005f);
        Vector2 unassistedOutput = unassisted.Process(endpoint, 0.005f);
        True(assistedOutput.X < unassistedOutput.X - 0.003f,
            $"Stop Assist did not add endpoint braking: {assistedOutput.X} vs {unassistedOutput.X}.");
    }

    private static void AdvancedHoldReleases()
    {
        var engine = new AdvancedAimEngine();
        engine.Reset(Vector2.Zero);
        for (int index = 0; index < 5; index++)
        {
            engine.Process(Vector2.Zero, 0.005f);
        }

        True(engine.IsSettled, "Advanced endpoint hold did not settle.");
        Equal(Vector2.Zero, engine.Process(new Vector2(0.04f, 0f), 0.005f), 0.001f);
        Vector2 jump = engine.Process(new Vector2(4f, 0f), 0.005f);
        True(jump.X > 3.98f, $"Advanced hold did not release on new intent: {jump.X}.");
    }

    private static void AdvancedOutputIsLeashed()
    {
        var engine = new AdvancedAimEngine();
        engine.Reset(Vector2.Zero);
        uint state = 0x51ED270Bu;
        Vector2 raw = Vector2.Zero;
        for (int index = 0; index < 100_000; index++)
        {
            raw += new Vector2(NextSigned(ref state), NextSigned(ref state)) * 0.13f;
            Vector2 output = engine.Process(raw, 0.001f + (index % 9) * 0.001f);
            True(Vector2.Distance(output, raw) <= 0.1001f,
                $"Advanced leash escaped at report {index}.");
        }
    }

    private static void AdvancedReportRatesStayConsistent()
    {
        foreach (float rate in new[] { 125f, 200f, 500f, 1000f })
        {
            var engine = new AdvancedAimEngine();
            engine.Reset(Vector2.Zero);
            Vector2 output = Vector2.Zero;
            for (int index = 1; index <= (int)rate; index++)
            {
                output = engine.Process(new Vector2(100f * index / rate, 0f), 1f / rate);
            }

            True(MathF.Abs(100f - output.X) <= 0.07f,
                $"Advanced output at {rate} Hz ended at {output.X} mm.");
        }
    }

    private static void AdvancedHotPathDoesNotAllocate()
    {
        var engine = new AdvancedAimEngine();
        engine.Reset(Vector2.Zero);
        for (int index = 0; index < 10_000; index++)
        {
            engine.Process(new Vector2(index * 0.1f, index % 5 * 0.01f), 0.005f);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100_000; index++)
        {
            engine.Process(new Vector2(index * 0.1f, index % 5 * 0.01f), 0.005f);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        True(allocated == 0, $"Advanced path allocated {allocated} bytes.");
    }

    private static void AdvancedPluginHotPathDoesNotAllocate()
    {
        var filter = new BrakeDeadzoneFilter { AdvancedFeatures = true };
        FakeTabletReport report = Report(0f, 0f);
        for (int index = 0; index < 10_000; index++)
        {
            report.Position = new Vector2(index * 2f, index % 7);
            filter.Consume(report);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100_000; index++)
        {
            report.Position = new Vector2(index * 2f, index % 7);
            filter.Consume(report);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        True(allocated == 0, $"Integrated advanced plugin path allocated {allocated} bytes.");
    }

    private static void RandomizedInputStaysFinite()
    {
        var filter = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 35f,
            BrakeSmoothing = 0.95f,
            BrakeSpeed = 90f
        };
        FakeTabletReport report = Report(0f, 0f);
        filter.Consume(report);

        uint state = 0xA341316Cu;
        Vector2 raw = Vector2.Zero;
        for (int index = 0; index < 100_000; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            float dx = (int)(state & 0xFF) - 127f;

            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            float dy = (int)(state & 0xFF) - 127f;

            raw += new Vector2(dx, dy);
            report.Position = raw;
            filter.Consume(report);

            True(
                float.IsFinite(report.Position.X) && float.IsFinite(report.Position.Y),
                $"Invalid output at randomized report {index}.");
        }
    }

    private static void HotPathDoesNotAllocate()
    {
        var filter = new BrakeDeadzoneFilter();
        FakeTabletReport report = Report(0f, 0f);
        for (int index = 0; index < 10_000; index++)
        {
            report.Position = new Vector2(index * 2f, index % 7);
            filter.Consume(report);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100_000; index++)
        {
            report.Position = new Vector2(index * 2f, index % 7);
            filter.Consume(report);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        True(allocated == 0, $"Hot path allocated {allocated} bytes.");
    }

    private static void PrintHotPathBenchmark()
    {
        const int warmupReports = 100_000;
        const int measuredReports = 2_000_000;
        var filter = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 22f,
            BrakeSmoothing = 0.35f,
            BrakeSpeed = 75f
        };
        FakeTabletReport report = Report(0f, 0f);

        for (int index = 0; index < warmupReports; index++)
        {
            report.Position = new Vector2(index * 3f, index % 11);
            filter.Consume(report);
        }

        var stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < measuredReports; index++)
        {
            report.Position = new Vector2(index * 3f, index % 11);
            filter.Consume(report);
        }

        stopwatch.Stop();
        double nanosecondsPerReport = stopwatch.Elapsed.TotalNanoseconds / measuredReports;
        Console.WriteLine($"Basic hot-path benchmark: {nanosecondsPerReport:F1} ns/report, 0 B/report.");

        var advancedFilter = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 22f,
            BrakeSmoothing = 0.35f,
            BrakeSpeed = 75f,
            AdvancedFeatures = true
        };
        for (int index = 0; index < warmupReports; index++)
        {
            report.Position = new Vector2(index * 3f, index % 11);
            advancedFilter.Consume(report);
        }

        stopwatch.Restart();
        for (int index = 0; index < measuredReports; index++)
        {
            report.Position = new Vector2(index * 3f, index % 11);
            advancedFilter.Consume(report);
        }
        stopwatch.Stop();
        nanosecondsPerReport = stopwatch.Elapsed.TotalNanoseconds / measuredReports;
        Console.WriteLine($"Advanced hot-path benchmark: {nanosecondsPerReport:F1} ns/report, 0 B/report.");
    }

    private static BrakeDeadzoneFilter CreateFilter(out List<IDeviceReport> emitted)
    {
        var filter = new BrakeDeadzoneFilter();
        emitted = new List<IDeviceReport>();
        filter.Emit += emitted.Add;
        return filter;
    }

    private static void CheckSettingMetadata(
        string propertyName,
        string expectedLabelText,
        float expectedDefault,
        string expectedUnit)
    {
        PropertyInfo property = typeof(BrakeDeadzoneFilter).GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Missing property: {propertyName}");
        SliderPropertyAttribute slider = property.GetCustomAttribute<SliderPropertyAttribute>()
            ?? throw new InvalidOperationException($"{propertyName} is missing SliderPropertyAttribute.");
        DefaultPropertyValueAttribute defaultValue = property.GetCustomAttribute<DefaultPropertyValueAttribute>()
            ?? throw new InvalidOperationException($"{propertyName} is missing DefaultPropertyValueAttribute.");
        UnitAttribute unit = property.GetCustomAttribute<UnitAttribute>()
            ?? throw new InvalidOperationException($"{propertyName} is missing UnitAttribute.");

        True(
            slider.DisplayName.Contains(expectedLabelText, StringComparison.OrdinalIgnoreCase),
            $"{propertyName} does not show its default in the label.");
        Equal(expectedDefault, Convert.ToSingle(defaultValue.Value));
        True(unit.Unit == expectedUnit, $"Expected unit '{expectedUnit}', got '{unit.Unit}'.");
    }

    private static FakeTabletReport Report(float x, float y)
    {
        return new FakeTabletReport
        {
            Position = new Vector2(x, y),
            Raw = Array.Empty<byte>(),
            PenButtons = Array.Empty<bool>()
        };
    }

    private static float NextSigned(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return ((state & 0xFFFF) / 32767.5f) - 1f;
    }

    private static void Equal(float expected, float actual, float tolerance = 0.0001f)
    {
        True(MathF.Abs(expected - actual) <= tolerance, $"Expected {expected}, got {actual}.");
    }

    private static void Equal(Vector2 expected, Vector2 actual, float tolerance = 0.0001f)
    {
        True(Vector2.Distance(expected, actual) <= tolerance, $"Expected {expected}, got {actual}.");
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeTabletReport : ITabletReport
    {
        public byte[] Raw { get; set; } = Array.Empty<byte>();

        public Vector2 Position { get; set; }

        public uint Pressure { get; set; }

        public bool[] PenButtons { get; set; } = Array.Empty<bool>();
    }

    private sealed class FakeProximityTabletReport : ITabletReport, IProximityReport
    {
        public byte[] Raw { get; set; } = Array.Empty<byte>();

        public Vector2 Position { get; set; }

        public uint Pressure { get; set; }

        public bool[] PenButtons { get; set; } = Array.Empty<bool>();

        public bool NearProximity { get; set; }

        public uint HoverDistance { get; set; }
    }
}
