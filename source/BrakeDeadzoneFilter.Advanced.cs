using System;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;

namespace BrakeFilter;

public sealed partial class BrakeDeadzoneFilter
{
    private const float AdvancedResetTime = 0.050f;

    private readonly PositionMotionEstimator _motionEstimator = new();
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

    private MotionFrame MeasureAdvancedMotion(Vector2 rawPosition)
    {
        if (!AdvancedFeatures)
        {
            return default;
        }

        Vector2 physicalPosition = rawPosition * _millimetresPerUnit;
        long timestamp = Stopwatch.GetTimestamp();
        if (!_advancedHasTimestamp)
        {
            _advancedLastTimestamp = timestamp;
            _advancedHasTimestamp = true;
            _motionEstimator.Reset(physicalPosition);
            return default;
        }

        float measuredPeriod = (float)((timestamp - _advancedLastTimestamp) / (double)Stopwatch.Frequency);
        _advancedLastTimestamp = timestamp;
        if (!float.IsFinite(measuredPeriod) || measuredPeriod <= 0f || measuredPeriod > AdvancedResetTime)
        {
            _motionEstimator.Reset(physicalPosition);
            return new MotionFrame(measuredPeriod, 0f, false);
        }

        return _motionEstimator.Observe(physicalPosition, measuredPeriod);
    }

    private Vector2 ApplyAdvanced(Vector2 basicPosition, MotionFrame motion)
    {
        if (!AdvancedFeatures)
        {
            return basicPosition;
        }

        Vector2 millimetrePosition = basicPosition * _millimetresPerUnit;
        Vector2 advancedPosition = _advancedEngine.Process(
            millimetrePosition,
            motion.ElapsedPeriod,
            motion.Speed,
            motion.HasMotionSample);
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

        Vector2 physicalPosition = rawPosition * _millimetresPerUnit;
        _advancedEngine.Reset(physicalPosition);
        _motionEstimator.Reset(physicalPosition);
        _advancedLastTimestamp = Stopwatch.GetTimestamp();
        _advancedHasTimestamp = true;
    }

    private void ClearAdvancedState()
    {
        _advancedEngine.Clear();
        _motionEstimator.Clear();
        _advancedLastTimestamp = 0;
        _advancedHasTimestamp = false;
    }
}
