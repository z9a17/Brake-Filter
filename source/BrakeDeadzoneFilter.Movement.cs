using System;
using System.Numerics;

namespace BrakeFilter;

public sealed partial class BrakeDeadzoneFilter
{
    private const float DirectionUpdateMinimumSpeed = 20f;
    private const float DirectionSmoothing = 0.35f;
    private const float FullBrakeSpeedRatio = 0.35f;

    private Vector2 ApplyMovementFilter(
        Vector2 rawPosition,
        Vector2 rawDelta,
        float speed,
        float physicalSpeed)
    {
        UpdateMovementDirection(rawDelta, speed);

        Vector2 filteredDelta = ApplyDirectionalDeadzone(rawDelta, speed, physicalSpeed);
        Vector2 target = _antichatterPosition + filteredDelta;
        return MotionMath.LimitOffset(target, rawPosition, MovementAntichatter);
    }

    private void UpdateMovementDirection(Vector2 rawDelta, float speed)
    {
        if (speed <= DirectionUpdateMinimumSpeed)
        {
            return;
        }

        Vector2 currentDirection = rawDelta / speed;
        if (_movementDirection == Vector2.Zero)
        {
            _movementDirection = currentDirection;
            return;
        }

        Vector2 blended = Vector2.Lerp(_movementDirection, currentDirection, DirectionSmoothing);
        float lengthSquared = blended.LengthSquared();
        _movementDirection = float.IsFinite(lengthSquared) && lengthSquared > 1e-12f
            ? blended / MathF.Sqrt(lengthSquared)
            : currentDirection;
    }

    private Vector2 ApplyDirectionalDeadzone(Vector2 rawDelta, float speed, float physicalSpeed)
    {
        float deadzone = MovementAntichatter;
        if (deadzone <= 0f || speed <= 0f)
        {
            return rawDelta;
        }

        if (speed <= deadzone)
        {
            return Vector2.Zero;
        }

        if (_movementDirection == Vector2.Zero || speed < DirectionUpdateMinimumSpeed)
        {
            return rawDelta;
        }

        float forwardDistance = Vector2.Dot(rawDelta, _movementDirection);
        Vector2 forwardMovement = _movementDirection * forwardDistance;
        Vector2 sidewaysMovement = rawDelta - forwardMovement;
        float fastBlend = AdditionalStabilizationEnabled
            ? MotionMath.SmoothStep(physicalSpeed, MotionSpeedThreshold * 0.50f, MotionSpeedThreshold)
            : 0f;
        float lateralDeadzone = deadzone * (1f + FastMotionStability * fastBlend);

        return forwardMovement + MotionMath.ApplyDeadzone(sidewaysMovement, lateralDeadzone);
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
