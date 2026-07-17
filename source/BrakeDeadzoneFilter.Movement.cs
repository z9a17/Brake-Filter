using System;
using System.Numerics;

namespace BrakeFilter;

public sealed partial class BrakeDeadzoneFilter
{
    private const float FullBrakeSpeedRatio = 0.35f;
    private const float FullRadialReleaseRatio = 4f;
    private const float FastStabilityReleaseExtension = 2f;

    private Vector2 ApplyMovementFilter(
        Vector2 rawPosition,
        float speed,
        float physicalSpeed)
    {
        float maximumRadius = MovementAntichatter;
        if (maximumRadius <= 0f)
        {
            return rawPosition;
        }

        // Fast-Motion Stability extends the radial release range without
        // introducing a preferred direction or increasing the positional leash.
        float fastBlend = AdditionalStabilizationEnabled
            ? MotionMath.SmoothStep(physicalSpeed, MotionSpeedThreshold * 0.50f, MotionSpeedThreshold)
            : 0f;
        float releaseRatio = FullRadialReleaseRatio +
            FastMotionStability * fastBlend * FastStabilityReleaseExtension;
        float fullReleaseSpeed = maximumRadius * releaseRatio;
        float release = MotionMath.SmoothStep(speed, maximumRadius, fullReleaseSpeed);
        float radius = maximumRadius * (1f - release);

        // Applying the deadzone to the accumulated raw-to-filtered offset makes
        // the filter rotationally symmetric and keeps it exactly radius-bounded.
        Vector2 offset = rawPosition - _antichatterPosition;
        return _antichatterPosition + MotionMath.ApplyDeadzone(offset, radius);
    }

    private float GetBrakeFactor(float speed)
    {
        float fullBrakeSpeed = BrakeSpeed * FullBrakeSpeedRatio;
        if (speed <= fullBrakeSpeed)
        {
            return 1f;
        }

        if (speed >= BrakeSpeed)
        {
            return 0f;
        }

        return 1f - MotionMath.SmoothStep(speed, fullBrakeSpeed, BrakeSpeed);
    }
}
