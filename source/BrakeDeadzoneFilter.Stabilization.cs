using System;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;

namespace BrakeFilter;

public sealed partial class BrakeDeadzoneFilter
{
    private const float MotionResetTime = 0.050f;

    private readonly PositionMotionEstimator _motionEstimator = new();
    private Vector2 _millimetresPerUnit = Vector2.One;
    private long _motionLastTimestamp;
    private bool _motionHasTimestamp;

    [TabletReference]
    public TabletReference TabletReference
    {
        set
        {
            DigitizerSpecifications? digitizer = value?.Properties?.Specifications?.Digitizer;
            _millimetresPerUnit = digitizer is null
                ? Vector2.One
                : new Vector2(
                    MotionMath.SafeScale(digitizer.Width, digitizer.MaxX),
                    MotionMath.SafeScale(digitizer.Height, digitizer.MaxY));
            ClearState();
        }
    }

    private MotionFrame MeasurePhysicalMotion(Vector2 rawPosition)
    {
        if (!AdditionalStabilizationEnabled)
        {
            return default;
        }

        Vector2 physicalPosition = rawPosition * _millimetresPerUnit;
        long timestamp = Stopwatch.GetTimestamp();
        if (!_motionHasTimestamp)
        {
            _motionLastTimestamp = timestamp;
            _motionHasTimestamp = true;
            _motionEstimator.Reset(physicalPosition);
            return default;
        }

        float measuredPeriod = (float)((timestamp - _motionLastTimestamp) / (double)Stopwatch.Frequency);
        _motionLastTimestamp = timestamp;
        if (!float.IsFinite(measuredPeriod) || measuredPeriod <= 0f || measuredPeriod > MotionResetTime)
        {
            _motionEstimator.Reset(physicalPosition);
            return new MotionFrame(measuredPeriod, 0f, false);
        }

        return _motionEstimator.Observe(physicalPosition, measuredPeriod);
    }

    private Vector2 ApplyAdditionalStabilization(Vector2 basicPosition, MotionFrame motion)
    {
        if (!AdditionalStabilizationEnabled)
        {
            return basicPosition;
        }

        Vector2 millimetrePosition = basicPosition * _millimetresPerUnit;
        Vector2 stabilizedPosition = _stabilityProcessor.Process(
            millimetrePosition,
            motion.ElapsedPeriod,
            motion.Speed,
            motion.HasMotionSample);
        if (!MotionMath.IsFinite(stabilizedPosition))
        {
            ClearAdditionalStabilizationState();
            return basicPosition;
        }

        return stabilizedPosition / _millimetresPerUnit;
    }

    private void ResetAdditionalStabilization(Vector2 rawPosition)
    {
        if (!AdditionalStabilizationEnabled)
        {
            ClearAdditionalStabilizationState();
            return;
        }

        Vector2 physicalPosition = rawPosition * _millimetresPerUnit;
        _stabilityProcessor.Reset(physicalPosition);
        _motionEstimator.Reset(physicalPosition);
        _motionLastTimestamp = Stopwatch.GetTimestamp();
        _motionHasTimestamp = true;
    }

    private void ClearAdditionalStabilizationState()
    {
        _stabilityProcessor.Clear();
        _motionEstimator.Clear();
        _motionLastTimestamp = 0;
        _motionHasTimestamp = false;
    }
}
