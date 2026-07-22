using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using BrakeFilter.RadialDev;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;

internal static class Program
{
    private static readonly List<string> Failures = new();
    private static int TestsRun;

    private static int Main()
    {
        Run("Public plugin contract and profile defaults", PublicContractAndDefaults);
        Run("Settings clamp invalid and out-of-range values", SettingsAreBounded);
        Run("Report lifecycle resets safely", ReportLifecycleIsSafe);
        Run("Inner radius contains slow chatter", InnerRadiusContainsChatter);
        Run("Radial response is rotationally symmetric", ResponseIsRotationallySymmetric);
        Run("Output never escapes the active radial leash", OutputIsSpatiallyBounded);
        Run("Fast Release reduces large-movement lag", FastReleaseReducesLag);
        Run("Intentional reversals release old-direction lag", ReversalReleasesLag);
        Run("Duplicate coordinates settle without buffering", DuplicateCoordinatesSettle);
        Run("Zero radius is transparent", ZeroRadiusIsTransparent);
        Run("Randomized input remains finite and bounded", RandomizedInputStaysFinite);
        Run("Hot path allocates no managed memory", HotPathDoesNotAllocate);

        if (Failures.Count != 0)
        {
            Console.Error.WriteLine($"{Failures.Count} test(s) failed:");
            foreach (string failure in Failures)
            {
                Console.Error.WriteLine($"- {failure}");
            }

            return 1;
        }

        Console.WriteLine($"All {TestsRun} radial-dev tests passed.");
        PrintBenchmark();
        return 0;
    }

    private static void PublicContractAndDefaults()
    {
        var filter = new LowLatencyRadialFilter();
        Equal(0.7039f, filter.OuterRadius);
        Equal(0.302f, filter.InnerRadius);
        Equal(0.302f, filter.Smoothing);
        Equal(0.603f, filter.SoftKnee);
        Equal(0.201f, filter.SmoothingLeak);
        Equal(0.75f, filter.FastRelease);

        Version? version = typeof(LowLatencyRadialFilter).Assembly.GetName().Version;
        True(version == new Version(0, 4, 0, 1), $"Unexpected assembly version: {version}");
        True(typeof(LowLatencyRadialFilter).FullName == "BrakeFilter.RadialDev.LowLatencyRadialFilter",
            "The experimental plugin must have a profile identity separate from stable Brake Filter.");
        True(typeof(LowLatencyRadialFilter).Assembly.GetType("BrakeFilter.BrakeDeadzoneFilter") is null,
            "The dev assembly still exposes the stable Brake Filter type.");

        PluginNameAttribute? name = typeof(LowLatencyRadialFilter)
            .GetCustomAttributes(typeof(PluginNameAttribute), false)
            .Cast<PluginNameAttribute>()
            .SingleOrDefault();
        True(name?.Name == "Brake Filter - Low-Latency Radial Dev", $"Unexpected OTD name: {name?.Name}");

        var settings = typeof(LowLatencyRadialFilter).GetProperties()
            .Where(property => property.GetCustomAttributes(typeof(PropertyAttribute), false).Length != 0)
            .ToArray();
        True(settings.Length == 6, $"Expected 6 radial settings, found {settings.Length}.");
        foreach (var property in settings)
        {
            ModifierAttribute[] modifiers = property
                .GetCustomAttributes(typeof(ModifierAttribute), false)
                .Cast<ModifierAttribute>()
                .ToArray();
            ToolTipAttribute? tooltip = modifiers.OfType<ToolTipAttribute>().SingleOrDefault();
            True(!string.IsNullOrWhiteSpace(tooltip?.ToolTip), $"{property.Name} has no tooltip.");
            True(tooltip!.ToolTip.Contains("default:", StringComparison.OrdinalIgnoreCase),
                $"{property.Name} tooltip does not state its default.");
            int tooltipIndex = Array.FindIndex(modifiers, attribute => attribute is ToolTipAttribute);
            int unitIndex = Array.FindIndex(modifiers, attribute => attribute is UnitAttribute);
            True(unitIndex < 0 || tooltipIndex < unitIndex,
                $"{property.Name} tooltip must be applied before Unit wraps the OTD input control.");
        }
    }

    private static void SettingsAreBounded()
    {
        var filter = new LowLatencyRadialFilter
        {
            OuterRadius = 100f,
            InnerRadius = -1f,
            Smoothing = 2f,
            SoftKnee = 100f,
            SmoothingLeak = -1f,
            FastRelease = 2f
        };

        Equal(5f, filter.OuterRadius);
        Equal(0f, filter.InnerRadius);
        Equal(1f, filter.Smoothing);
        Equal(10f, filter.SoftKnee);
        Equal(0f, filter.SmoothingLeak);
        Equal(1f, filter.FastRelease);

        filter.OuterRadius = float.NaN;
        filter.InnerRadius = float.PositiveInfinity;
        filter.Smoothing = float.NaN;
        Equal(0.7039f, filter.OuterRadius);
        Equal(0.302f, filter.InnerRadius);
        Equal(0.302f, filter.Smoothing);
    }

    private static void ReportLifecycleIsSafe()
    {
        var filter = new LowLatencyRadialFilter();
        FakeTabletReport first = Report(10f, 20f);
        filter.Consume(first);
        Equal(new Vector2(10f, 20f), first.Position);

        FakeTabletReport chatter = Report(10.1f, 20f);
        filter.Consume(chatter);
        Equal(new Vector2(10f, 20f), chatter.Position);

        filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));
        FakeTabletReport reentry = Report(30f, 40f);
        filter.Consume(reentry);
        Equal(new Vector2(30f, 40f), reentry.Position);

        filter.Consume(Report(float.NaN, 0f));
        FakeTabletReport valid = Report(31f, 40f);
        filter.Consume(valid);
        Equal(new Vector2(31f, 40f), valid.Position);
    }

    private static void InnerRadiusContainsChatter()
    {
        var processor = new LowLatencyRadialProcessor();
        processor.Reset(Vector2.Zero);
        foreach (float x in new[] { 0.10f, -0.10f, 0.14f, -0.14f, 0.08f })
        {
            Equal(Vector2.Zero, processor.Process(new Vector2(x, 0f)), 0.000001f);
        }
    }

    private static void ResponseIsRotationallySymmetric()
    {
        var horizontal = new LowLatencyRadialProcessor();
        var vertical = new LowLatencyRadialProcessor();
        horizontal.Reset(Vector2.Zero);
        vertical.Reset(Vector2.Zero);

        foreach (float distance in new[] { 0.1f, 0.35f, 0.8f, 1.4f, 0.2f })
        {
            Vector2 x = horizontal.Process(new Vector2(distance, 0f));
            Vector2 y = vertical.Process(new Vector2(0f, distance));
            Equal(x.X, y.Y, 0.00001f);
            Equal(x.Y, -y.X, 0.00001f);
        }
    }

    private static void OutputIsSpatiallyBounded()
    {
        var processor = new LowLatencyRadialProcessor();
        Vector2 input = Vector2.Zero;
        processor.Reset(input);
        uint random = 0x42A31C9Du;

        for (int index = 0; index < 20_000; index++)
        {
            input += new Vector2(NextSigned(ref random), NextSigned(ref random)) * 2f;
            Vector2 output = processor.Process(input);
            True(Vector2.Distance(output, input) <= processor.OuterRadius + 0.0001f,
                $"Radial leash escaped at report {index}.");
        }
    }

    private static void FastReleaseReducesLag()
    {
        var directRelease = new LowLatencyRadialProcessor { FastRelease = 0.75f };
        var slowRelease = new LowLatencyRadialProcessor { FastRelease = 0f };
        directRelease.Reset(Vector2.Zero);
        slowRelease.Reset(Vector2.Zero);

        Vector2 input = Vector2.Zero;
        for (int step = 0; step < 12; step++)
        {
            input += new Vector2(1.2f, 0.1f);
            directRelease.Process(input);
            slowRelease.Process(input);
        }

        float directLag = Vector2.Distance(directRelease.Output, input);
        float slowLag = Vector2.Distance(slowRelease.Output, input);
        True(directLag < slowLag * 0.65f,
            $"Fast Release did not materially reduce lag: {directLag} vs {slowLag} mm.");
        True(directLag < 0.35f, $"Fast movement retained {directLag} mm of lag.");
    }

    private static void ReversalReleasesLag()
    {
        var processor = new LowLatencyRadialProcessor();
        processor.Reset(Vector2.Zero);
        Vector2 input = Vector2.Zero;
        for (int step = 0; step < 8; step++)
        {
            input += new Vector2(0.8f, 0f);
            processor.Process(input);
        }

        input -= new Vector2(0.8f, 0f);
        Vector2 reversed = processor.Process(input);
        Equal(input, reversed, 0.0001f);
    }

    private static void DuplicateCoordinatesSettle()
    {
        var processor = new LowLatencyRadialProcessor { FastRelease = 0f };
        processor.Reset(Vector2.Zero);
        Vector2 input = new(2f, 0f);
        Vector2 first = processor.Process(input);
        float firstLag = Vector2.Distance(first, input);

        for (int report = 0; report < 8; report++)
        {
            processor.Process(input);
        }

        float settledLag = Vector2.Distance(processor.Output, input);
        True(settledLag < firstLag, "Duplicate coordinates did not settle the radial tail.");
        True(settledLag <= processor.InnerRadius + 0.001f,
            $"Settled lag remained outside Inner Radius: {settledLag} mm.");
    }

    private static void ZeroRadiusIsTransparent()
    {
        var filter = new LowLatencyRadialFilter { OuterRadius = 0f };
        for (int index = 0; index < 100; index++)
        {
            var report = Report(index * 7f, index % 9);
            Vector2 input = report.Position;
            filter.Consume(report);
            Equal(input, report.Position);
        }
    }

    private static void RandomizedInputStaysFinite()
    {
        var filter = new LowLatencyRadialFilter();
        FakeTabletReport report = Report(0f, 0f);
        filter.Consume(report);
        uint random = 0xA341316Cu;
        Vector2 input = Vector2.Zero;

        for (int index = 0; index < 25_000; index++)
        {
            input += new Vector2(NextSigned(ref random), NextSigned(ref random)) * 1.5f;
            report.Position = input;
            filter.Consume(report);
            True(float.IsFinite(report.Position.X) && float.IsFinite(report.Position.Y),
                $"Invalid output at report {index}.");
            True(Vector2.Distance(report.Position, input) <= filter.OuterRadius + 0.0001f,
                $"Filter output escaped its leash at report {index}.");
        }
    }

    private static void HotPathDoesNotAllocate()
    {
        var processor = new LowLatencyRadialProcessor();
        processor.Reset(Vector2.Zero);
        for (int index = 0; index < 10_000; index++)
        {
            processor.Process(new Vector2(index * 0.3f, index % 5));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100_000; index++)
        {
            processor.Process(new Vector2(index * 0.3f, index % 5));
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        True(allocated == 0, $"Hot path allocated {allocated} bytes.");
    }

    private static void PrintBenchmark()
    {
        const int reports = 2_000_000;
        var processor = new LowLatencyRadialProcessor();
        processor.Reset(Vector2.Zero);
        for (int index = 0; index < 100_000; index++)
        {
            processor.Process(new Vector2(index * 0.3f, index % 5));
        }

        var stopwatch = Stopwatch.StartNew();
        for (int index = 0; index < reports; index++)
        {
            processor.Process(new Vector2(index * 0.3f, index % 5));
        }
        stopwatch.Stop();

        Console.WriteLine(
            $"Radial-dev hot-path benchmark: {stopwatch.Elapsed.TotalNanoseconds / reports:F1} ns/report, 0 B/report.");
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
}
