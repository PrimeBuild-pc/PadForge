namespace PadForge.Core.Mapping;

public readonly record struct StickTuning(
    float Sensitivity,
    float Curve,
    float Deadzone,
    bool Spring,
    float SpringRate,
    float AntiDeadzone = 0f,
    bool InvertX = false,
    bool InvertY = false,
    float VerticalScale = 1f)
{
    public static StickTuning Default { get; } = new(1f, 1.4f, 0.06f, true, 26f);

    public static StickTuning Clamp(
        double sensitivity,
        double curve,
        double deadzone,
        bool spring,
        double springRate,
        double antiDeadzone = 0,
        bool invertX = false,
        bool invertY = false,
        double verticalScale = 1)
    {
        return new StickTuning(
            (float)Math.Clamp(sensitivity, 0.2, 4),
            (float)Math.Clamp(curve, 0.6, 3),
            (float)Math.Clamp(deadzone, 0, 0.3),
            spring,
            (float)Math.Clamp(springRate, 4, 40),
            (float)Math.Clamp(antiDeadzone, 0, 0.4),
            invertX,
            invertY,
            (float)Math.Clamp(verticalScale, 0.25, 2.5));
    }
}

public static class StickShaper
{
    /// <summary>
    /// Radial deadzone, then a power curve, then an anti-deadzone that lifts
    /// any remaining deflection so a game's own deadzone still sees it.
    /// </summary>
    public static (float X, float Y) Apply(float x, float y, float curve, float deadzone, float antiDeadzone = 0f)
    {
        float magnitude = MathF.Sqrt((x * x) + (y * y));
        if (magnitude < 0.0001f || magnitude < deadzone)
            return (0f, 0f);

        float shaped = (magnitude - deadzone) / MathF.Max(0.0001f, 1f - deadzone);
        shaped = MathF.Pow(Math.Clamp(shaped, 0f, 1f), Math.Clamp(curve, 0.2f, 4f));
        float anti = Math.Clamp(antiDeadzone, 0f, 0.95f);
        if (anti > 0f)
            shaped = anti + ((1f - anti) * shaped);

        float scale = shaped / magnitude;
        return (x * scale, y * scale);
    }
}

/// <summary>
/// Turns mouse counts into a right-stick deflection.
/// Spring on: deflection follows mouse speed and returns to center.
/// Spring off: movement nudges a held stick position.
/// </summary>
public sealed class MouseToStickConverter
{
    public const float FullScaleCountsPerSecond = 3200f;
    public const float HoldGainPerCount = 0.0028f;

    private float _x;
    private float _y;

    /// <summary>Deflection before deadzone and curve, for plotting.</summary>
    public float RawMagnitude => MathF.Min(1f, MathF.Sqrt((_x * _x) + (_y * _y)));

    public void Reset()
    {
        _x = 0f;
        _y = 0f;
    }

    public static (float X, float Y) VelocityTarget(float deltaX, float deltaY, float deltaSeconds, float sensitivity)
    {
        float dt = MathF.Max(deltaSeconds, 0.001f);
        float fullScale = FullScaleCountsPerSecond / MathF.Max(sensitivity, 0.05f);
        float x = (deltaX / dt) / fullScale;
        float y = (-deltaY / dt) / fullScale;
        float magnitude = MathF.Sqrt((x * x) + (y * y));
        if (magnitude > 1f)
        {
            x /= magnitude;
            y /= magnitude;
        }

        return (x, y);
    }

    public (float X, float Y) Update(float deltaX, float deltaY, StickTuning tuning, float deltaSeconds)
    {
        float dt = MathF.Max(deltaSeconds, 0.001f);
        float xCounts = tuning.InvertX ? -deltaX : deltaX;
        float yCounts = (tuning.InvertY ? -deltaY : deltaY) * tuning.VerticalScale;
        if (tuning.Spring)
        {
            var (targetX, targetY) = VelocityTarget(xCounts, yCounts, dt, tuning.Sensitivity);
            float blend = 1f - MathF.Exp(-tuning.SpringRate * dt);
            _x += (targetX - _x) * blend;
            _y += (targetY - _y) * blend;
        }
        else
        {
            float gain = tuning.Sensitivity * HoldGainPerCount;
            _x = Math.Clamp(_x + (xCounts * gain), -1f, 1f);
            _y = Math.Clamp(_y - (yCounts * gain), -1f, 1f);
        }

        return StickShaper.Apply(_x, _y, tuning.Curve, tuning.Deadzone, tuning.AntiDeadzone);
    }
}
