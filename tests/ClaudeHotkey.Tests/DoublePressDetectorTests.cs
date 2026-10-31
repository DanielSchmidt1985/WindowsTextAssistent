using ClaudeHotkey.Core.Hotkeys;

namespace ClaudeHotkey.Tests;

public class DoublePressDetectorTests
{
    private const int C = 'C';
    private const int X = 'X';
    private const int Ctrl = 0xA2; // left Ctrl as delivered by the low-level hook
    private static readonly HotkeyGesture CtrlXX = new(HotkeyModifiers.Ctrl, X, IsDoublePress: true);

    private static HotkeyGesture? Press(DoublePressDetector detector, int key, uint time, HotkeyModifiers modifiers = HotkeyModifiers.Ctrl)
    {
        var fired = detector.OnKeyDown(key, modifiers, time);
        detector.OnKeyUp(key);
        return fired;
    }

    [Fact]
    public void Two_copies_within_window_fire_once()
    {
        var detector = new DoublePressDetector(400);

        Assert.Null(Press(detector, C, 1000));
        Assert.Equal(HotkeyGesture.DoubleCopy, Press(detector, C, 1300));
    }

    [Fact]
    public void Third_copy_starts_a_new_pair()
    {
        var detector = new DoublePressDetector(400);
        Press(detector, C, 1000);
        Press(detector, C, 1100);

        Assert.Null(Press(detector, C, 1200));
        Assert.NotNull(Press(detector, C, 1300));
    }

    [Fact]
    public void Presses_too_far_apart_do_not_fire()
    {
        var detector = new DoublePressDetector(400);

        Assert.Null(Press(detector, C, 1000));
        Assert.Null(Press(detector, C, 1401));
    }

    [Fact]
    public void Holding_the_key_auto_repeat_does_not_fire()
    {
        var detector = new DoublePressDetector(400);

        Assert.Null(detector.OnKeyDown(C, HotkeyModifiers.Ctrl, 1000));
        Assert.Null(detector.OnKeyDown(C, HotkeyModifiers.Ctrl, 1030));
        Assert.Null(detector.OnKeyDown(C, HotkeyModifiers.Ctrl, 1060));
    }

    [Fact]
    public void Lost_key_up_does_not_block_forever()
    {
        var detector = new DoublePressDetector(400);
        detector.OnKeyDown(C, HotkeyModifiers.Ctrl, 1000); // key-up never arrives

        Assert.Null(detector.OnKeyDown(C, HotkeyModifiers.Ctrl, 5000));
        detector.OnKeyUp(C);
        Assert.NotNull(Press(detector, C, 5200));
    }

    [Fact]
    public void Other_key_in_between_resets()
    {
        var detector = new DoublePressDetector(400);
        Press(detector, C, 1000);
        Press(detector, 'A', 1100); // Ctrl+A

        Assert.Null(Press(detector, C, 1200));
    }

    [Fact]
    public void Modifier_events_in_between_do_not_reset()
    {
        var detector = new DoublePressDetector(400);
        Press(detector, C, 1000);
        detector.OnKeyDown(Ctrl, HotkeyModifiers.Ctrl, 1100); // Ctrl auto-repeat while held

        Assert.NotNull(Press(detector, C, 1200));
    }

    [Fact]
    public void Modifiers_must_match_exactly()
    {
        var detector = new DoublePressDetector(400);

        Press(detector, C, 1000, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
        Assert.Null(Press(detector, C, 1100, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift)); // Ctrl+Shift+C+C
        Press(detector, C, 2000, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt);
        Assert.Null(Press(detector, C, 2100, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt));   // Ctrl+Alt+C+C
        Press(detector, C, 3000, HotkeyModifiers.None);
        Assert.Null(Press(detector, C, 3100, HotkeyModifiers.None));                         // C C while typing
    }

    [Fact]
    public void Configured_gesture_fires_and_unknown_does_not()
    {
        var detector = new DoublePressDetector(400) { Gestures = [HotkeyGesture.DoubleCopy, CtrlXX] };

        Press(detector, X, 1000);
        Assert.Equal(CtrlXX, Press(detector, X, 1200));

        Press(detector, 'Q', 2000);
        Assert.Null(Press(detector, 'Q', 2100)); // Ctrl+Q+Q not configured
    }

    [Fact]
    public void Different_double_gestures_do_not_mix()
    {
        var detector = new DoublePressDetector(400) { Gestures = [HotkeyGesture.DoubleCopy, CtrlXX] };

        Press(detector, C, 1000);
        Assert.Null(Press(detector, X, 1100));
        Assert.Equal(CtrlXX, Press(detector, X, 1200));
    }

    [Fact]
    public void Single_press_gestures_are_ignored()
    {
        var detector = new DoublePressDetector(400) { Gestures = [new HotkeyGesture(HotkeyModifiers.Ctrl, X)] };

        Press(detector, X, 1000);
        Assert.Null(Press(detector, X, 1100));
    }

    [Fact]
    public void Works_across_tick_wraparound()
    {
        var detector = new DoublePressDetector(400);

        Assert.Null(Press(detector, C, uint.MaxValue - 100));
        Assert.NotNull(Press(detector, C, 100));
    }

    [Fact]
    public void Window_can_be_changed_at_runtime()
    {
        var detector = new DoublePressDetector(400) { WindowMilliseconds = 600 };
        Press(detector, C, 1000);

        Assert.NotNull(Press(detector, C, 1550));
    }
}
