using System;
using System.Numerics;

namespace BrakeFilter;

public sealed partial class AdvancedAimEngine
{
    public void Clear()
    {
        _initialized = false;
        _settled = false;
        _stopCandidateArmed = false;
        ClearStationaryWindow();
        _previousInput = Vector2.Zero;
        _holdAnchor = Vector2.Zero;
        _output = Vector2.Zero;
        _peakSpeed = 0f;
    }

    public Vector2 Reset(Vector2 input)
    {
        if (!AimMath.IsFinite(input))
        {
            Clear();
            return input;
        }

        _initialized = true;
        _settled = false;
        _stopCandidateArmed = false;
        ClearStationaryWindow();
        _previousInput = input;
        _holdAnchor = input;
        _output = input;
        _peakSpeed = 0f;
        return input;
    }

    private bool TryHoldSettledPosition(Vector2 input, float holdRadius, out Vector2 output)
    {
        if (!_settled)
        {
            output = default;
            return false;
        }

        if (StabilityRadius > 0f &&
            Vector2.DistanceSquared(input, _holdAnchor) <= holdRadius * holdRadius)
        {
            _previousInput = input;
            _peakSpeed = MathF.Min(_peakSpeed, FastAimThreshold);
            _output = _holdAnchor;
            output = _output;
            return true;
        }

        _settled = false;
        ClearStationaryWindow();
        output = default;
        return false;
    }

    private Vector2 SettleAt(Vector2 input)
    {
        _settled = true;
        _holdAnchor = input;
        ClearStationaryWindow();
        _previousInput = input;
        _output = input;
        return _output;
    }
}
