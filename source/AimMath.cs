using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace BrakeFilter;

internal static class AimMath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 ApplyDeadzone(Vector2 value, float deadzone)
    {
        if (deadzone <= 0f)
        {
            return value;
        }

        float lengthSquared = value.LengthSquared();
        if (lengthSquared <= deadzone * deadzone)
        {
            return Vector2.Zero;
        }

        float length = MathF.Sqrt(lengthSquared);
        return value * ((length - deadzone) / length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 LimitOffset(Vector2 value, Vector2 input, float maximumOffset)
    {
        if (maximumOffset <= 0f)
        {
            return input;
        }

        Vector2 offset = value - input;
        float lengthSquared = offset.LengthSquared();
        if (lengthSquared <= maximumOffset * maximumOffset)
        {
            return value;
        }

        float length = MathF.Sqrt(lengthSquared);
        return input + offset * (maximumOffset / length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SmoothStep(float value, float start, float end)
    {
        if (end <= start)
        {
            return value >= end ? 1f : 0f;
        }

        float amount = Math.Clamp((value - start) / (end - start), 0f, 1f);
        return amount * amount * (3f - 2f * amount);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ClampFinite(float value, float minimum, float maximum, float fallback)
    {
        return float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float start, float end, float amount)
    {
        return start + (end - start) * amount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SafeScale(float millimetres, float maximumRaw)
    {
        float scale = millimetres / maximumRaw;
        return float.IsFinite(scale) && scale > 0f ? scale : 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
