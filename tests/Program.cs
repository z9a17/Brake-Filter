using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;
using BrakeFilter;

internal static class Program
{
    private static readonly List<string> Failures = new();
    private static int TestsRun;

    private static int Main()
    {
        Run("Public plugin contract and defaults", PublicContractAndDefaults);
        Run("Settings reject invalid values and accept documented limits", SettingsAreBounded);
        Run("Report lifecycle resets only when required", ReportLifecycleIsSafe);
        Run("Movement anti-chatter is rotationally symmetric and bounded", MovementFilterIsBounded);
        Run("Braking is bounded and fast movement stays direct", BrakingIsBounded);
        Run("PTK-1240-scale reports use the expanded speed range", Ptk1240ScaleReportsWork);
        Run("Rolling report period resists USB bursts and host pauses", ReportPeriodIsStable);
        Run("Position sampling ignores host jitter and coordinate duplicates", PositionSamplingIsStable);
        Run("Coordinate duplicates stay transparent", DuplicateReportsStayTransparent);
        Run("Additional stabilization is transparent when disabled or zeroed", AdditionalStabilizationIsTransparent);
        Run("Endpoint hold is stationary-only and releases on intent", EndpointHoldBehaves);
        Run("Endpoint Brake reacts without escaping its leash", EndpointBrakeIsBounded);
        Run("Randomized input always stays finite", RandomizedInputStaysFinite);
        Run("Basic and additional-stabilization hot paths allocate no managed memory", HotPathsDoNotAllocate);

        if (Failures.Count != 0)
        {
            Console.Error.WriteLine($"{Failures.Count} test(s) failed:");
            foreach (string failure in Failures)
            {
                Console.Error.WriteLine($"- {failure}");
            }

            return 1;
        }

        Console.WriteLine($"All {TestsRun} core tests passed.");
        PrintBenchmarks();
        return 0;
    }

    private static void PublicContractAndDefaults()
    {
        var filter = new BrakeDeadzoneFilter();
        Equal(10f, filter.MovementAntichatter);
        Equal(0.45f, filter.BrakeSmoothing);
        Equal(90f, filter.BrakeSpeed);
        True(!filter.AdvancedFeatures, "Additional stabilization must default to off.");
        Equal(0.05f, filter.StabilityRadius);
        Equal(0.25f, filter.StopAssist);
        Equal(0.80f, filter.FastAimStability);
        Equal(120f, filter.FastAimThreshold);

        Version? version = typeof(BrakeDeadzoneFilter).Assembly.GetName().Version;
        True(version == new Version(0, 3, 7, 0), $"Unexpected assembly version: {version}");
        True(typeof(BrakeDeadzoneFilter).FullName == "BrakeFilter.BrakeDeadzoneFilter",
            "The saved-profile type identity changed.");
        var assembly = typeof(BrakeDeadzoneFilter).Assembly;
        True(assembly.GetType("BrakeFilter.MotionStabilityProcessor") is not null,
            "MotionStabilityProcessor is missing.");
        True(assembly.GetType("BrakeFilter.MotionMath") is not null,
            "MotionMath is missing.");
        True(assembly.GetType("BrakeFilter.AdvancedAimEngine") is null,
            "The retired AdvancedAimEngine type is still present.");
        True(assembly.GetType("BrakeFilter.AimMath") is null,
            "The retired AimMath type is still present.");
        PluginNameAttribute? name = typeof(BrakeDeadzoneFilter)
            .GetCustomAttributes(typeof(PluginNameAttribute), false)
            .Cast<PluginNameAttribute>()
            .SingleOrDefault();
        True(name?.Name == "Brake Filter", $"Unexpected OTD name: {name?.Name}");

        var expectedDisplayNames = new Dictionary<string, string>
        {
            [nameof(BrakeDeadzoneFilter.MovementAntichatter)] = "Movement Anti-Chatter",
            [nameof(BrakeDeadzoneFilter.BrakeSmoothing)] = "Brake Strength",
            [nameof(BrakeDeadzoneFilter.BrakeSpeed)] = "Brake Start Speed",
            [nameof(BrakeDeadzoneFilter.AdvancedFeatures)] = "Additional Stabilization",
            [nameof(BrakeDeadzoneFilter.StabilityRadius)] = "Stability Radius",
            [nameof(BrakeDeadzoneFilter.StopAssist)] = "Endpoint Brake",
            [nameof(BrakeDeadzoneFilter.FastAimStability)] = "Fast-Motion Stability",
            [nameof(BrakeDeadzoneFilter.FastAimThreshold)] = "Motion-Speed Threshold"
        };
        foreach (KeyValuePair<string, string> setting in expectedDisplayNames)
        {
            var property = typeof(BrakeDeadzoneFilter).GetProperty(setting.Key);
            PropertyAttribute? propertyAttribute = property?
                .GetCustomAttributes(typeof(PropertyAttribute), false)
                .Cast<PropertyAttribute>()
                .SingleOrDefault();
            True(propertyAttribute?.DisplayName == setting.Value,
                $"{setting.Key} has unexpected OTD display name: {propertyAttribute?.DisplayName}");

            ModifierAttribute[] modifiers = property?
                .GetCustomAttributes(typeof(ModifierAttribute), false)
                .Cast<ModifierAttribute>()
                .ToArray() ?? Array.Empty<ModifierAttribute>();
            ToolTipAttribute? tooltip = modifiers
                .OfType<ToolTipAttribute>()
                .SingleOrDefault();
            True(!string.IsNullOrWhiteSpace(tooltip?.ToolTip), $"{setting.Key} has no tooltip.");
            True(tooltip!.ToolTip.Contains("\n\n", StringComparison.Ordinal),
                $"{setting.Key} tooltip lacks structured paragraphs.");
            True(tooltip.ToolTip.Length <= 500, $"{setting.Key} tooltip is too long.");

            int tooltipIndex = Array.FindIndex(modifiers, attribute => attribute is ToolTipAttribute);
            int unitIndex = Array.FindIndex(modifiers, attribute => attribute is UnitAttribute);
            True(unitIndex < 0 || tooltipIndex < unitIndex,
                $"{setting.Key} tooltip must be applied before Unit wraps the OTD input control.");
        }
    }

    private static void SettingsAreBounded()
    {
        var filter = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 1000f,
            BrakeSmoothing = 1f,
            BrakeSpeed = 10000f,
            StabilityRadius = 1f,
            StopAssist = 1f,
            FastAimStability = 2f,
            FastAimThreshold = 5000f
        };

        Equal(1000f, filter.MovementAntichatter);
        Equal(1f, filter.BrakeSmoothing);
        Equal(10000f, filter.BrakeSpeed);
        Equal(1f, filter.StabilityRadius);
        Equal(1f, filter.StopAssist);
        Equal(2f, filter.FastAimStability);
        Equal(5000f, filter.FastAimThreshold);

        filter.MovementAntichatter = float.NaN;
        filter.BrakeSmoothing = float.PositiveInfinity;
        filter.BrakeSpeed = 0f;
        filter.FastAimThreshold = 10000f;
        Equal(10f, filter.MovementAntichatter);
        Equal(0.45f, filter.BrakeSmoothing);
        Equal(1f, filter.BrakeSpeed);
        Equal(5000f, filter.FastAimThreshold);
    }

    private static void ReportLifecycleIsSafe()
    {
        var filter = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 0f,
            BrakeSmoothing = 1f,
            BrakeSpeed = 1000f
        };

        FakeTabletReport first = Report(10f, 20f);
        filter.Consume(first);
        Equal(new Vector2(10f, 20f), first.Position);

        filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));
        FakeTabletReport reentry = Report(30f, 20f);
        filter.Consume(reentry);
        Equal(new Vector2(30f, 20f), reentry.Position);

        var hoverFilter = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 0f,
            BrakeSmoothing = 1f,
            BrakeSpeed = 1000f
        };
        hoverFilter.Consume(new FakeProximityReport { Position = Vector2.Zero });
        var hover = new FakeProximityReport { Position = new Vector2(20f, 0f) };
        hoverFilter.Consume(hover);
        True(hover.Position.X < 20f, "A valid far-hover report reset or bypassed state.");

        filter.Consume(Report(float.NaN, 0f));
        FakeTabletReport valid = Report(31f, 20f);
        filter.Consume(valid);
        Equal(new Vector2(31f, 20f), valid.Position);
    }

    private static void MovementFilterIsBounded()
    {
        var horizontal = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 10f,
            BrakeSmoothing = 0f,
            BrakeSpeed = 10000f
        };
        var vertical = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 10f,
            BrakeSmoothing = 0f,
            BrakeSpeed = 10000f
        };
        horizontal.Consume(Report(0f, 0f));
        vertical.Consume(Report(0f, 0f));

        foreach (float distance in new[] { 5f, 12f, 24f, 40f })
        {
            FakeTabletReport xReport = Report(distance, 0f);
            FakeTabletReport yReport = Report(0f, distance);
            horizontal.Consume(xReport);
            vertical.Consume(yReport);

            Equal(xReport.Position.X, yReport.Position.Y, 0.0001f);
            Equal(xReport.Position.Y, -yReport.Position.X, 0.0001f);
            True(Vector2.Distance(xReport.Position, new Vector2(distance, 0f)) <= 10.001f,
                $"Horizontal leash escaped at {distance}.");
            True(Vector2.Distance(yReport.Position, new Vector2(0f, distance)) <= 10.001f,
                $"Vertical leash escaped at {distance}.");

            if (distance == 5f)
            {
                Equal(Vector2.Zero, xReport.Position);
                Equal(Vector2.Zero, yReport.Position);
            }
            else if (distance == 12f)
            {
                True(xReport.Position.X > 0f && xReport.Position.X < distance,
                    "Slow radial movement did not release gradually.");
            }
        }

        var turns = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 10f,
            BrakeSmoothing = 0f
        };
        turns.Consume(Report(0f, 0f));
        FakeTabletReport right = Report(100f, 0f);
        turns.Consume(right);
        Equal(new Vector2(100f, 0f), right.Position);

        FakeTabletReport up = Report(100f, 100f);
        turns.Consume(up);
        Equal(new Vector2(100f, 100f), up.Position);
    }

    private static void BrakingIsBounded()
    {
        var filter = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 0f,
            BrakeSmoothing = 0.45f,
            BrakeSpeed = 90f
        };
        filter.Consume(Report(0f, 0f));

        FakeTabletReport slow = Report(0f, 0f);
        for (int step = 1; step <= 100; step++)
        {
            slow = Report(step * 11f, 0f);
            filter.Consume(slow);
        }

        float lag = 1100f - slow.Position.X;
        True(lag > 0f && lag < 5.1f, $"Slow brake accumulated {lag} units of lag.");

        FakeTabletReport jump = Report(1400f, 0f);
        filter.Consume(jump);
        Equal(new Vector2(1400f, 0f), jump.Position);
    }

    private static void Ptk1240ScaleReportsWork()
    {
        var filter = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 0f,
            BrakeSmoothing = 1f,
            BrakeSpeed = 10000f
        };
        filter.Consume(Report(0f, 0f));

        FakeTabletReport first = Report(2001f, 0f);
        filter.Consume(first);
        Equal(Vector2.Zero, first.Position);

        FakeTabletReport second = Report(4002f, 0f);
        filter.Consume(second);
        Equal(new Vector2(2001f, 0f), second.Position);
    }

    private static void ReportPeriodIsStable()
    {
        var estimator = new ReportPeriodEstimator();
        Equal(0.005f, estimator.PeriodSeconds, 0.000001f);

        for (int index = 0; index < 32; index++)
        {
            estimator.Observe(0.005f);
        }
        Equal(0.005f, estimator.PeriodSeconds, 0.000001f);

        // Two reports delivered as a short/long pair still represent 5 ms each.
        for (int index = 0; index < 32; index++)
        {
            estimator.Observe((index & 1) == 0 ? 0.001f : 0.009f);
        }
        Equal(0.005f, estimator.PeriodSeconds, 0.000001f);

        // A scheduler pause is not a tablet report-period change.
        estimator.Observe(0.040f);
        Equal(0.005f, estimator.PeriodSeconds, 0.000001f);

        // A real rate change converges after one small window.
        for (int index = 0; index < 32; index++)
        {
            estimator.Observe(0.001f);
        }
        Equal(0.001f, estimator.PeriodSeconds, 0.000001f);

        estimator.Clear();
        Equal(0.005f, estimator.PeriodSeconds, 0.000001f);
    }

    private static void PositionSamplingIsStable()
    {
        var baseline = new PositionMotionEstimator();
        var duplicates = new PositionMotionEstimator();
        var jittered = new PositionMotionEstimator();
        baseline.Reset(Vector2.Zero);
        duplicates.Reset(Vector2.Zero);
        jittered.Reset(Vector2.Zero);

        MotionFrame baselineMotion = default;
        MotionFrame duplicateMotion = default;
        MotionFrame jitteredMotion = default;
        for (int step = 1; step <= 96; step++)
        {
            Vector2 previous = new((step - 1) * 0.5f, 0f);
            Vector2 current = new(step * 0.5f, 0f);
            baselineMotion = baseline.Observe(current, 0.005f);

            MotionFrame duplicate = duplicates.Observe(previous, 0.002f);
            True(!duplicate.HasMotionSample, "An unchanged coordinate became a motion sample.");
            duplicateMotion = duplicates.Observe(current, 0.003f);

            float jitteredPeriod = (step & 1) == 0 ? 0.007f : 0.003f;
            jitteredMotion = jittered.Observe(current, jitteredPeriod);
        }

        True(baselineMotion.HasMotionSample && duplicateMotion.HasMotionSample,
            "A changed coordinate was not recognized as motion.");
        Equal(100f, baselineMotion.Speed, 0.01f);
        Equal(baselineMotion.Speed, duplicateMotion.Speed, 0.01f);
        Equal(baselineMotion.Speed, jitteredMotion.Speed, 0.01f);

        var afterPause = new PositionMotionEstimator();
        afterPause.Reset(Vector2.Zero);
        Vector2 pausePosition = Vector2.Zero;
        for (int step = 1; step <= 8; step++)
        {
            pausePosition = new Vector2(step * 0.5f, 0f);
            afterPause.Observe(pausePosition, 0.005f);
        }

        for (int report = 0; report < 5; report++)
        {
            afterPause.Observe(pausePosition, 0.005f);
        }

        pausePosition += new Vector2(0.1f, 0f);
        MotionFrame uncertainMotion = afterPause.Observe(pausePosition, 0.005f);
        True(!uncertainMotion.HasMotionSample,
            "The first coordinate change after a long pause was treated as reliable velocity.");

        pausePosition += new Vector2(0.1f, 0f);
        MotionFrame resumedMotion = afterPause.Observe(pausePosition, 0.005f);
        True(resumedMotion.HasMotionSample, "Velocity sampling did not resume after the long pause.");
        Equal(20f, resumedMotion.Speed, 0.01f);
    }

    private static void DuplicateReportsStayTransparent()
    {
        var processor = new MotionStabilityProcessor
        {
            StabilityRadius = 0f,
            EndpointBrake = 1f
        };
        processor.Reset(Vector2.Zero);
        for (int step = 1; step <= 8; step++)
        {
            Vector2 input = new(step, 0f);
            processor.Process(input, 0.005f, 200f, true);
        }

        Vector2 duplicate = new(8f, 0f);
        Equal(duplicate, processor.Process(duplicate, 0.005f, 0f, false), 0.000001f);

        var filter = new BrakeDeadzoneFilter { AdvancedFeatures = true };
        IDeviceReport? emitted = null;
        int emittedCount = 0;
        filter.Emit += report =>
        {
            emitted = report;
            emittedCount++;
        };

        filter.Consume(Report(50f, 75f));
        FakeTabletReport pressureReport = Report(50f, 75f);
        pressureReport.Pressure = 123;
        pressureReport.PenButtons = new[] { true, false };
        filter.Consume(pressureReport);

        True(ReferenceEquals(emitted, pressureReport), "The duplicate report was not emitted immediately.");
        True(emittedCount == 2, $"Expected 2 emitted reports, got {emittedCount}.");
        True(pressureReport.Pressure == 123 && pressureReport.PenButtons[0],
            "Pressure or button data changed on a coordinate duplicate.");
    }

    private static void AdditionalStabilizationIsTransparent()
    {
        var basic = new BrakeDeadzoneFilter();
        var zeroed = new BrakeDeadzoneFilter
        {
            AdvancedFeatures = true,
            StabilityRadius = 0f,
            StopAssist = 0f,
            FastAimStability = 0f
        };

        for (int index = 0; index < 200; index++)
        {
            Vector2 input = new(index * 17f, (index % 7) * 3f);
            FakeTabletReport basicReport = Report(input.X, input.Y);
            FakeTabletReport stabilizedReport = Report(input.X, input.Y);
            basic.Consume(basicReport);
            zeroed.Consume(stabilizedReport);
            Equal(basicReport.Position, stabilizedReport.Position, 0.0001f);
        }
    }

    private static void EndpointHoldBehaves()
    {
        var processor = new MotionStabilityProcessor { EndpointBrake = 0f };
        processor.Reset(Vector2.Zero);

        for (int index = 1; index <= 200; index++)
        {
            Vector2 input = new(index * 0.05f, index * 0.01f);
            Equal(input, processor.Process(input, 0.005f), 0.000001f);
        }
        True(!processor.IsSettled, "Continuous movement was classified as stationary.");

        processor.Reset(Vector2.Zero);
        for (int index = 0; index < 10; index++)
        {
            float chatter = (index & 1) == 0 ? 0.02f : -0.02f;
            processor.Process(new Vector2(chatter, 0f), 0.005f);
        }
        True(processor.IsSettled, "Endpoint hold never settled.");

        Vector2 anchor = processor.Output;
        Equal(anchor, processor.Process(new Vector2(0.04f, 0f), 0.005f), 0.001f);
        Vector2 jump = processor.Process(new Vector2(4f, 0f), 0.005f);
        True(jump.X > 3.98f, "Endpoint hold did not release on new movement.");

        processor.Reset(Vector2.Zero);
        Vector2 thresholdPosition = new(0.30f, 0f);
        processor.Process(thresholdPosition, 0.005f, 60f, true);
        processor.Process(thresholdPosition, 0.005f, 0f, false);
        processor.Process(thresholdPosition, 0.005f, 0f, false);
        True(!processor.IsSettled, "Movement at the stop threshold entered the stationary window.");
        processor.Process(thresholdPosition, 0.005f, 0f, false);
        True(processor.IsSettled, "A real stationary dwell after threshold movement did not settle.");
    }

    private static void EndpointBrakeIsBounded()
    {
        var braked = new MotionStabilityProcessor { EndpointBrake = 1f };
        var direct = new MotionStabilityProcessor { EndpointBrake = 0f };
        braked.Reset(Vector2.Zero);
        direct.Reset(Vector2.Zero);
        for (int index = 1; index <= 8; index++)
        {
            Vector2 input = new(index, 0f);
            braked.Process(input, 0.005f);
            direct.Process(input, 0.005f);
        }

        Vector2 endpoint = new(8.15f, 0f);
        Vector2 brakedOutput = braked.Process(endpoint, 0.005f);
        Vector2 directOutput = direct.Process(endpoint, 0.005f);
        True(brakedOutput.X < directOutput.X, "Endpoint Brake did not react to deceleration.");
        True(Vector2.Distance(brakedOutput, endpoint) <= 0.1001f,
            "Endpoint Brake escaped its 0.10 mm leash.");
    }

    private static void RandomizedInputStaysFinite()
    {
        var filter = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 35f,
            BrakeSmoothing = 0.95f,
            BrakeSpeed = 90f,
            AdvancedFeatures = true
        };
        FakeTabletReport report = Report(0f, 0f);
        filter.Consume(report);

        uint state = 0xA341316Cu;
        Vector2 raw = Vector2.Zero;
        for (int index = 0; index < 25_000; index++)
        {
            raw += new Vector2(NextSigned(ref state), NextSigned(ref state)) * 127f;
            report.Position = raw;
            filter.Consume(report);
            True(float.IsFinite(report.Position.X) && float.IsFinite(report.Position.Y),
                $"Invalid output at randomized report {index}.");
        }
    }

    private static void HotPathsDoNotAllocate()
    {
        CheckNoAllocations(new BrakeDeadzoneFilter(), "Basic");
        CheckNoAllocations(new BrakeDeadzoneFilter { AdvancedFeatures = true }, "Additional stabilization");
    }

    private static void CheckNoAllocations(BrakeDeadzoneFilter filter, string name)
    {
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
        True(allocated == 0, $"{name} path allocated {allocated} bytes.");
    }

    private static void PrintBenchmarks()
    {
        Benchmark(new BrakeDeadzoneFilter(), "Basic");
        Benchmark(new BrakeDeadzoneFilter { AdvancedFeatures = true }, "Additional stabilization");
    }

    private static void Benchmark(BrakeDeadzoneFilter filter, string name)
    {
        const int warmupReports = 100_000;
        const int measuredReports = 2_000_000;
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

        Console.WriteLine(
            $"{name} hot-path benchmark: {stopwatch.Elapsed.TotalNanoseconds / measuredReports:F1} ns/report, 0 B/report.");
    }

    private static void Run(string name, Action test)
    {
        TestsRun++;
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

    private static FakeTabletReport Report(float x, float y) => new()
    {
        Position = new Vector2(x, y)
    };

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

    private class FakeTabletReport : ITabletReport
    {
        public byte[] Raw { get; set; } = Array.Empty<byte>();
        public Vector2 Position { get; set; }
        public uint Pressure { get; set; }
        public bool[] PenButtons { get; set; } = Array.Empty<bool>();
    }

    private sealed class FakeProximityReport : FakeTabletReport, IProximityReport
    {
        public bool NearProximity { get; set; }
        public uint HoverDistance { get; set; }
    }
}
