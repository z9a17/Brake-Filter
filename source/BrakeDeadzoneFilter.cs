using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace BrakeFilter;

/// <summary>
/// Low-latency raw-position anti-chatter and bounded braking for OTD.
/// </summary>
// Keep this CLR type name stable: OTD stores it in saved filter profiles.
[PluginName("Brake Filter")]
public sealed partial class BrakeDeadzoneFilter : IPositionedPipelineElement<IDeviceReport>
{
    // Above the full ~115k-unit diagonal of a Wacom PTK-1240.
    private const float ResetDistance = 131072f;
    private const float ResetDistanceSquared = ResetDistance * ResetDistance;
    private const float SpeedSmoothing = 0.35f;

    private readonly MotionStabilityProcessor _stabilityProcessor = new();
    private bool _initialized;
    private Vector2 _previousRawPosition;
    private Vector2 _antichatterPosition;
    private float _smoothedSpeed;

    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport>? Emit;

    public void Consume(IDeviceReport report)
    {
        if (report is OutOfRangeReport)
        {
            ClearState();
            Emit?.Invoke(report);
            return;
        }

        if (report is not ITabletReport tabletReport)
        {
            Emit?.Invoke(report);
            return;
        }

        Vector2 rawPosition = tabletReport.Position;
        if (!MotionMath.IsFinite(rawPosition))
        {
            ClearState();
            Emit?.Invoke(report);
            return;
        }

        if (!_initialized)
        {
            Reset(rawPosition);
            ResetAdditionalStabilization(rawPosition);
            Emit?.Invoke(report);
            return;
        }

        Vector2 rawDelta = rawPosition - _previousRawPosition;
        float distanceSquared = rawDelta.LengthSquared();
        if (!float.IsFinite(distanceSquared) || distanceSquared > ResetDistanceSquared)
        {
            Reset(rawPosition);
            ResetAdditionalStabilization(rawPosition);
            tabletReport.Position = rawPosition;
            Emit?.Invoke(report);
            return;
        }

        float speed = MathF.Sqrt(distanceSquared);
        MotionFrame motion = MeasurePhysicalMotion(rawPosition);
        Vector2 antichatterTarget = ApplyMovementFilter(rawPosition, speed, motion.Speed);

        _smoothedSpeed = MotionMath.Lerp(_smoothedSpeed, speed, SpeedSmoothing);
        float brakeAmount = BrakeSmoothing * GetBrakeFactor(MathF.Max(speed, _smoothedSpeed));

        // Previous raw input is a bounded one-report anchor, never recursive output.
        Vector2 output = brakeAmount > 0f
            ? Vector2.Lerp(antichatterTarget, _previousRawPosition, brakeAmount)
            : antichatterTarget;

        if (!MotionMath.IsFinite(output))
        {
            Reset(rawPosition);
            output = rawPosition;
        }
        else
        {
            _previousRawPosition = rawPosition;
            _antichatterPosition = antichatterTarget;
        }

        tabletReport.Position = ApplyAdditionalStabilization(output, motion);
        Emit?.Invoke(report);
    }

    private void ClearState()
    {
        _initialized = false;
        _previousRawPosition = Vector2.Zero;
        _antichatterPosition = Vector2.Zero;
        _smoothedSpeed = 0f;
        ClearAdditionalStabilizationState();
    }

    private void Reset(Vector2 rawPosition)
    {
        _previousRawPosition = rawPosition;
        _antichatterPosition = rawPosition;
        _smoothedSpeed = 0f;
        _initialized = true;
    }
}
