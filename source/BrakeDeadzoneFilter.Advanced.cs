using System;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;

namespace BrakeFilter;

public sealed partial class BrakeDeadzoneFilter
{
    private const float AdvancedResetTime = 0.050f;

    private readonly ReportPeriodEstimator _reportPeriod = new();
    private Vector2 _millimetresPerUnit = Vector2.One;
    private long _advancedLastTimestamp;
    private bool _advancedHasTimestamp;

    [TabletReference]
    public TabletReference TabletReference
    {
        set
        {
            DigitizerSpecifications? digitizer = value?.Properties?.Specifications?.Digitizer;
            _millimetresPerUnit = digitizer is null
                ? Vector2.One
                : new Vector2(
                    AimMath.SafeScale(digitizer.Width, digitizer.MaxX),
                    AimMath.SafeScale(digitizer.Height, digitizer.MaxY));
            ClearState();
        }
    }

    private float MeasureAdvancedMotion(Vector2 rawDelta, out float physicalSpeed)
    {
        physicalSpeed = 0f;
        if (!AdvancedFeatures)
        {
            return 0f;
        }

        long timestamp = Stopwatch.GetTimestamp();
        if (!_advancedHasTimestamp)
        {
            _advancedLastTimestamp = timestamp;
            _advancedHasTimestamp = true;
            return 0f;
        }

        float measuredPeriod = (float)((timestamp - _advancedLastTimestamp) / (double)Stopwatch.Frequency);
        _advancedLastTimestamp = timestamp;
        if (!float.IsFinite(measuredPeriod) || measuredPeriod <= 0f || measuredPeriod > AdvancedResetTime)
        {
            return measuredPeriod;
        }

        float stablePeriod = _reportPeriod.Observe(measuredPeriod);
        float distanceSquared = (rawDelta * _millimetresPerUnit).LengthSquared();
        if (float.IsFinite(distanceSquared))
        {
            // Distance per report is the primary motion signal. Host time is
            // only a rolling conversion factor from mm/report to mm/s.
            physicalSpeed = MathF.Sqrt(distanceSquared) / stablePeriod;
        }

        return stablePeriod;
    }

    private Vector2 ApplyAdvanced(Vector2 basicPosition, float reportPeriod)
    {
        if (!AdvancedFeatures)
        {
            return basicPosition;
        }

        Vector2 millimetrePosition = basicPosition * _millimetresPerUnit;
        Vector2 advancedPosition = _advancedEngine.Process(millimetrePosition, reportPeriod);
        if (!AimMath.IsFinite(advancedPosition))
        {
            ClearAdvancedState();
            return basicPosition;
        }

        return advancedPosition / _millimetresPerUnit;
    }

    private void ResetAdvanced(Vector2 rawPosition)
    {
        ClearAdvancedState();
        if (!AdvancedFeatures)
        {
            return;
        }

        _advancedEngine.Reset(rawPosition * _millimetresPerUnit);
        _advancedLastTimestamp = Stopwatch.GetTimestamp();
        _advancedHasTimestamp = true;
    }

    private void ClearAdvancedState()
    {
        _advancedEngine.Clear();
        _reportPeriod.Clear();
        _advancedLastTimestamp = 0;
        _advancedHasTimestamp = false;
    }
}
