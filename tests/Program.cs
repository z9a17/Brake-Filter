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
        Run("Assembly and plugin identify as v0.1", VersionMetadataIsV01);
        Run("Setting labels expose defaults and units", SettingMetadataIsVisible);
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
        Run("Finite randomized input never produces invalid output", RandomizedInputStaysFinite);
        Run("The report hot path performs no managed allocations", HotPathDoesNotAllocate);

        if (Failures.Count == 0)
        {
            Console.WriteLine("All 15 tests passed.");
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
    }

    private static void VersionMetadataIsV01()
    {
        Version? version = typeof(BrakeDeadzoneFilter).Assembly.GetName().Version;
        True(version == new Version(0, 1, 0, 0), $"Unexpected assembly version: {version}");

        PluginNameAttribute? name = typeof(BrakeDeadzoneFilter)
            .GetCustomAttributes(typeof(PluginNameAttribute), inherit: false)
            .Cast<PluginNameAttribute>()
            .SingleOrDefault();
        True(name is not null, "PluginNameAttribute is missing.");
        True(name!.Name == "Brake Filter v0.1", $"Unexpected plugin name: {name.Name}");
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
        Console.WriteLine($"Hot-path benchmark: {nanosecondsPerReport:F1} ns/report, 0 B/report.");
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
