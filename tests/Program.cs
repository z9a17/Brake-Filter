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

    private static int Main()
    {
        Run("Public plugin contract and defaults", PublicContractAndDefaults);
        Run("Settings reject invalid values and accept documented limits", SettingsAreBounded);
        Run("Report lifecycle resets only when required", ReportLifecycleIsSafe);
        Run("Movement anti-chatter remains directional and spatially bounded", MovementFilterIsBounded);
        Run("Braking is bounded and fast movement stays direct", BrakingIsBounded);
        Run("PTK-1240-scale reports use the expanded speed range", Ptk1240ScaleReportsWork);
        Run("Rolling report period resists USB bursts and host pauses", ReportPeriodIsStable);
        Run("Advanced gate is transparent when disabled or zeroed", AdvancedGateIsTransparent);
        Run("Endpoint hold is stationary-only and releases on intent", EndpointHoldBehaves);
        Run("Stop Assist reacts without escaping its leash", StopAssistIsBounded);
        Run("Randomized input always stays finite", RandomizedInputStaysFinite);
        Run("Basic and advanced hot paths allocate no managed memory", HotPathsDoNotAllocate);

        if (Failures.Count != 0)
        {
            Console.Error.WriteLine($"{Failures.Count} test(s) failed:");
            foreach (string failure in Failures)
            {
                Console.Error.WriteLine($"- {failure}");
            }

            return 1;
        }

        Console.WriteLine("All 12 core tests passed.");
        PrintBenchmarks();
        return 0;
    }

    private static void PublicContractAndDefaults()
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

        Version? version = typeof(BrakeDeadzoneFilter).Assembly.GetName().Version;
        True(version == new Version(0, 2, 4, 0), $"Unexpected assembly version: {version}");
        True(typeof(BrakeDeadzoneFilter).FullName == "BrakeFilter.BrakeDeadzoneFilter",
            "The saved-profile type identity changed.");
        PluginNameAttribute? name = typeof(BrakeDeadzoneFilter)
            .GetCustomAttributes(typeof(PluginNameAttribute), false)
            .Cast<PluginNameAttribute>()
            .SingleOrDefault();
        True(name?.Name == "Brake Filter", $"Unexpected OTD name: {name?.Name}");
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
        var filter = new BrakeDeadzoneFilter
        {
            MovementAntichatter = 10f,
            BrakeSmoothing = 0f,
            BrakeSpeed = 10000f
        };
        filter.Consume(Report(0f, 0f));

        for (int x = 1; x <= 40; x++)
        {
            FakeTabletReport report = Report(x, 0f);
            filter.Consume(report);
            True(Vector2.Distance(report.Position, new Vector2(x, 0f)) <= 10.001f,
                $"Movement leash escaped at x={x}.");
        }

        filter.Consume(Report(140f, 0f));
        FakeTabletReport diagonal = Report(240f, 5f);
        filter.Consume(diagonal);
        True(diagonal.Position.Y < 3f, "Directional anti-chatter did not reduce lateral jitter.");
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

    private static void AdvancedGateIsTransparent()
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
            FakeTabletReport advancedReport = Report(input.X, input.Y);
            basic.Consume(basicReport);
            zeroed.Consume(advancedReport);
            Equal(basicReport.Position, advancedReport.Position, 0.0001f);
        }
    }

    private static void EndpointHoldBehaves()
    {
        var engine = new AdvancedAimEngine { StopAssist = 0f };
        engine.Reset(Vector2.Zero);

        for (int index = 1; index <= 200; index++)
        {
            Vector2 input = new(index * 0.05f, index * 0.01f);
            Equal(input, engine.Process(input, 0.005f), 0.000001f);
        }
        True(!engine.IsSettled, "Continuous movement was classified as stationary.");

        engine.Reset(Vector2.Zero);
        for (int index = 0; index < 10; index++)
        {
            float chatter = (index & 1) == 0 ? 0.02f : -0.02f;
            engine.Process(new Vector2(chatter, 0f), 0.005f);
        }
        True(engine.IsSettled, "Endpoint hold never settled.");

        Vector2 anchor = engine.Output;
        Equal(anchor, engine.Process(new Vector2(0.04f, 0f), 0.005f), 0.001f);
        Vector2 jump = engine.Process(new Vector2(4f, 0f), 0.005f);
        True(jump.X > 3.98f, "Endpoint hold did not release on new movement.");
    }

    private static void StopAssistIsBounded()
    {
        var assisted = new AdvancedAimEngine { StopAssist = 1f };
        var direct = new AdvancedAimEngine { StopAssist = 0f };
        assisted.Reset(Vector2.Zero);
        direct.Reset(Vector2.Zero);
        for (int index = 1; index <= 8; index++)
        {
            Vector2 input = new(index, 0f);
            assisted.Process(input, 0.005f);
            direct.Process(input, 0.005f);
        }

        Vector2 endpoint = new(8.15f, 0f);
        Vector2 assistedOutput = assisted.Process(endpoint, 0.005f);
        Vector2 directOutput = direct.Process(endpoint, 0.005f);
        True(assistedOutput.X < directOutput.X, "Stop Assist did not react to deceleration.");
        True(Vector2.Distance(assistedOutput, endpoint) <= 0.1001f,
            "Stop Assist escaped its 0.10 mm leash.");
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
        CheckNoAllocations(new BrakeDeadzoneFilter { AdvancedFeatures = true }, "Advanced");
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
        Benchmark(new BrakeDeadzoneFilter { AdvancedFeatures = true }, "Advanced");
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
