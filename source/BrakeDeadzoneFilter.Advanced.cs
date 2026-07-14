using System;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;

namespace BrakeFilter;

public sealed partial class BrakeDeadzoneFilter
{
    private const float MinimumAdvancedDeltaTime = 0.00025f;
    private const float MaximumAdvancedDeltaTime = 0.020f;
    private const float AdvancedResetTime = 0.050f;

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

    private Vector2 ApplyAdvanced(Vector2 basicPosition, float deltaTime)
    {
        if (!AdvancedFeatures)
        {
            return basicPosition;
        }

        Vector2 millimetrePosition = basicPosition * _millimetresPerUnit;
        Vector2 advancedPosition = _advancedEngine.Process(millimetrePosition, deltaTime);
        if (!AimMath.IsFinite(advancedPosition))
        {
            ClearAdvancedState();
            return basicPosition;
        }

        return advancedPosition / _millimetresPerUnit;
    }

    private float MeasureAdvancedSpeed(Vector2 rawDelta, out float physicalSpeed)
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

        float deltaTime = (float)((timestamp - _advancedLastTimestamp) / (double)Stopwatch.Frequency);
        _advancedLastTimestamp = timestamp;
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f || deltaTime > AdvancedResetTime)
        {
            return deltaTime;
        }

        float distanceSquared = (rawDelta * _millimetresPerUnit).LengthSquared();
        if (float.IsFinite(distanceSquared))
        {
            float speedTime = Math.Clamp(
                deltaTime,
                MinimumAdvancedDeltaTime,
                MaximumAdvancedDeltaTime);
            physicalSpeed = MathF.Sqrt(distanceSquared) / speedTime;
        }

        return deltaTime;
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
        _advancedLastTimestamp = 0;
        _advancedHasTimestamp = false;
    }
}
