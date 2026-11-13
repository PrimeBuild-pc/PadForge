using PadForge.Core.Mapping;
using PadForge.Core.Runtime;

namespace PadForge.Core.Tests;

public class MappingTests
{
    [Fact]
    public void AxisExtremes_UseXboxRange()
    {
        Assert.Equal(32767, AxisMath.ToAxis(1f));
        Assert.Equal(-32768, AxisMath.ToAxis(-1f));
        Assert.Equal(0, AxisMath.ToAxis(0f));
        Assert.Equal(32767, AxisMath.ToAxis(4f));
    }

    [Fact]
    public void VelocityTarget_ReachesFullScaleAtReferenceSpeed()
    {
        const float dt = 0.02f;
        float dx = MouseToStickConverter.FullScaleCountsPerSecond * dt;
        var (x, y) = MouseToStickConverter.VelocityTarget(dx, 0f, dt, sensitivity: 1f);

        Assert.Equal(1f, x, 3);
        Assert.Equal(0f, y, 3);
    }

    [Fact]
    public void VelocityTarget_HigherSensitivityNeedsLessSpeed()
    {
        const float dt = 0.02f;
        float dx = MouseToStickConverter.FullScaleCountsPerSecond * dt / 2f;
        var (x, _) = MouseToStickConverter.VelocityTarget(dx, 0f, dt, sensitivity: 2f);

        Assert.Equal(1f, x, 3);
    }

    [Fact]
    public void VelocityTarget_InvertsMouseY()
    {
        const float dt = 0.02f;
        float dy = MouseToStickConverter.FullScaleCountsPerSecond * dt;
        var (_, y) = MouseToStickConverter.VelocityTarget(0f, dy, dt, sensitivity: 1f);

        Assert.Equal(-1f, y, 3);
    }

    [Theory]
    [InlineData(0.05f, 0.10f)]
    [InlineData(0f, 0.20f)]
    public void Shaper_DropsInputInsideDeadzone(float value, float deadzone)
    {
        var (x, y) = StickShaper.Apply(value, 0f, curve: 1f, deadzone);

        Assert.Equal(0f, x);
        Assert.Equal(0f, y);
    }

    [Fact]
    public void Shaper_CurveSquaresLinearDeflection()
    {
        var (x, _) = StickShaper.Apply(0.5f, 0f, curve: 2f, deadzone: 0f);

        Assert.Equal(0.25f, x, 3);
    }

    [Fact]
    public void Shaper_RescalesAfterDeadzone()
    {
        var (x, _) = StickShaper.Apply(0.5f, 0f, curve: 1f, deadzone: 0.25f);

        Assert.Equal(1f / 3f, x, 3);
    }

    [Fact]
    public void Shaper_AntiDeadzoneLiftsSmallDeflectionAndKeepsTheEdge()
    {
        var (lifted, _) = StickShaper.Apply(0.5f, 0f, curve: 1f, deadzone: 0f, antiDeadzone: 0.2f);
        var (full, _) = StickShaper.Apply(1f, 0f, curve: 1f, deadzone: 0f, antiDeadzone: 0.2f);
        var (inside, _) = StickShaper.Apply(0.05f, 0f, curve: 1f, deadzone: 0.1f, antiDeadzone: 0.2f);

        Assert.Equal(0.6f, lifted, 3);
        Assert.Equal(1f, full, 3);
        Assert.Equal(0f, inside);
    }

    [Fact]
    public void Update_InvertXFlipsHoldDirection()
    {
        var converter = new MouseToStickConverter();
        var tuning = new StickTuning(1f, 1f, 0f, Spring: false, SpringRate: 26f, InvertX: true);

        var sample = converter.Update(100f, 0f, tuning, 0.02f);

        Assert.Equal(-100f * MouseToStickConverter.HoldGainPerCount, sample.X, 3);
    }

    [Fact]
    public void Update_VerticalScaleReachesFullStickAtHalfSpeed()
    {
        var converter = new MouseToStickConverter();
        var tuning = new StickTuning(1f, 1f, 0f, Spring: true, SpringRate: 40f, VerticalScale: 2f);
        float step = MouseToStickConverter.FullScaleCountsPerSecond * 0.02f / 2f;

        (float X, float Y) latest = default;
        for (int i = 0; i < 40; i++)
            latest = converter.Update(0f, step, tuning, 0.02f);

        Assert.True(latest.Y < -0.75f);
    }

    [Fact]
    public void Calibration_MapsReferenceSpeedToUnitSensitivity()
    {
        Assert.True(FlickCalibration.TrySensitivity(MouseToStickConverter.FullScaleCountsPerSecond, out float sensitivity));
        Assert.Equal(1f, sensitivity, 3);
        Assert.False(FlickCalibration.TrySensitivity(100f, out _));
    }

    [Fact]
    public void Shaper_KeepsDiagonalDirection()
    {
        var (x, y) = StickShaper.Apply(0.4f, 0.4f, curve: 2f, deadzone: 0f);

        Assert.Equal(x, y, 3);
        Assert.True(x > 0f);
    }

    [Fact]
    public void Spring_ReturnsTowardCenterAfterMovementStops()
    {
        var converter = new MouseToStickConverter();
        var tuning = new StickTuning(1f, 1f, 0f, Spring: true, SpringRate: 30f);
        float step = MouseToStickConverter.FullScaleCountsPerSecond * 0.02f;

        (float X, float Y) latest = default;
        for (int i = 0; i < 40; i++)
            latest = converter.Update(step, 0f, tuning, 0.02f);

        Assert.True(latest.X > 0.75f);

        for (int i = 0; i < 40; i++)
            latest = converter.Update(0f, 0f, tuning, 0.02f);

        Assert.True(latest.X < 0.05f);
    }

    [Fact]
    public void HoldMode_KeepsPositionWhenMouseStops()
    {
        var converter = new MouseToStickConverter();
        var tuning = new StickTuning(1f, 1f, 0f, Spring: false, SpringRate: 26f);

        var first = converter.Update(100f, 0f, tuning, 0.02f);
        var second = converter.Update(0f, 0f, tuning, 0.02f);

        Assert.Equal(100f * MouseToStickConverter.HoldGainPerCount, first.X, 3);
        Assert.Equal(first.X, second.X, 3);
    }

    [Fact]
    public void Composer_NormalizesDiagonalLeftStickAndReadsButtons()
    {
        var keys = new int[ControlTargetCatalog.Count];
        keys[(int)ControlTarget.LeftStickUp] = 1;
        keys[(int)ControlTarget.LeftStickLeft] = 2;
        keys[(int)ControlTarget.A] = 32;
        keys[(int)ControlTarget.RightTrigger] = 7;
        var settings = new ActiveSettings
        {
            RightStick = new StickTuning(1f, 1f, 0f, true, 26f),
            Keys = keys,
        };

        var composed = PadComposer.Compose(
            new MouseToStickConverter(),
            settings,
            mouseDeltaX: 0,
            mouseDeltaY: 0,
            isKeyDown: key => key is 1 or 2 or 32 or 7,
            deltaSeconds: 0.02f);

        Assert.InRange(composed.LeftX, -0.75f, -0.65f);
        Assert.InRange(composed.LeftY, 0.65f, 0.75f);
        Assert.Equal(GamepadButtons.A, composed.Buttons);
        Assert.Equal(255, composed.RightTrigger);
        Assert.Equal(0, composed.LeftTrigger);
    }

    [Theory]
    [InlineData((short)0, false, 128)]
    [InlineData((short)32767, false, 255)]
    [InlineData((short)-32768, false, 0)]
    [InlineData((short)32767, true, 0)]
    [InlineData((short)-32768, true, 255)]
    [InlineData((short)0, true, 128)]
    public void DualShock4Axis_CentersAt128AndFlipsY(short value, bool invert, int expected)
    {
        Assert.Equal(expected, DualShock4Mapping.Axis(value, invert));
    }

    [Fact]
    public void DualShock4Hat_CombinesDiagonalsAndCancelsOpposites()
    {
        Assert.Equal(-1, DualShock4Mapping.Hat(GamepadButtons.None));
        Assert.Equal(0, DualShock4Mapping.Hat(GamepadButtons.DpadUp));
        Assert.Equal(1, DualShock4Mapping.Hat(GamepadButtons.DpadUp | GamepadButtons.DpadRight));
        Assert.Equal(5, DualShock4Mapping.Hat(GamepadButtons.DpadDown | GamepadButtons.DpadLeft));
        Assert.Equal(6, DualShock4Mapping.Hat(GamepadButtons.DpadLeft | GamepadButtons.DpadUp | GamepadButtons.DpadDown));
    }
}
