using ClaudeHotkey.Core.Hotkeys;

namespace ClaudeHotkey.Tests;

public class DoubleCopyDetectorTests
{
    private const int C = 0x43;
    private const int Ctrl = 0xA2; // left Ctrl as delivered by the low-level hook

    private static bool CtrlC(DoubleCopyDetector detector, uint time)
    {
        var fired = detector.OnKeyDown(C, ctrlDown: true, altDown: false, time);
        detector.OnKeyUp(C);
        return fired;
    }

    [Fact]
    public void Two_copies_within_window_fire_once()
    {
        var detector = new DoubleCopyDetector(400);

        Assert.False(CtrlC(detector, 1000));
        Assert.True(CtrlC(detector, 1300));
    }

    [Fact]
    public void Third_copy_starts_a_new_pair()
    {
        var detector = new DoubleCopyDetector(400);
        CtrlC(detector, 1000);
        CtrlC(detector, 1100);

        Assert.False(CtrlC(detector, 1200));
        Assert.True(CtrlC(detector, 1300));
    }

    [Fact]
    public void Copies_too_far_apart_do_not_fire()
    {
        var detector = new DoubleCopyDetector(400);

        Assert.False(CtrlC(detector, 1000));
        Assert.False(CtrlC(detector, 1401));
    }

    [Fact]
    public void Holding_c_auto_repeat_does_not_fire()
    {
        var detector = new DoubleCopyDetector(400);

        Assert.False(detector.OnKeyDown(C, true, false, 1000));
        Assert.False(detector.OnKeyDown(C, true, false, 1030));
        Assert.False(detector.OnKeyDown(C, true, false, 1060));
    }

    [Fact]
    public void Other_key_in_between_resets()
    {
        var detector = new DoubleCopyDetector(400);
        CtrlC(detector, 1000);
        detector.OnKeyDown(0x41, true, false, 1100); // Ctrl+A
        detector.OnKeyUp(0x41);

        Assert.False(CtrlC(detector, 1200));
    }

    [Fact]
    public void Modifier_events_in_between_do_not_reset()
    {
        var detector = new DoubleCopyDetector(400);
        CtrlC(detector, 1000);
        detector.OnKeyDown(Ctrl, true, false, 1100); // Ctrl auto-repeat while held

        Assert.True(CtrlC(detector, 1200));
    }

    [Fact]
    public void C_without_ctrl_resets()
    {
        var detector = new DoubleCopyDetector(400);
        CtrlC(detector, 1000);
        detector.OnKeyDown(C, false, false, 1100);
        detector.OnKeyUp(C);

        Assert.False(CtrlC(detector, 1200));
    }

    [Fact]
    public void Ctrl_alt_c_does_not_count()
    {
        var detector = new DoubleCopyDetector(400);
        detector.OnKeyDown(C, true, altDown: true, 1000);
        detector.OnKeyUp(C);

        Assert.False(detector.OnKeyDown(C, true, altDown: true, 1100));
    }

    [Fact]
    public void Works_across_tick_wraparound()
    {
        var detector = new DoubleCopyDetector(400);

        Assert.False(CtrlC(detector, uint.MaxValue - 100));
        Assert.True(CtrlC(detector, 100));
    }

    [Fact]
    public void Window_can_be_changed_at_runtime()
    {
        var detector = new DoubleCopyDetector(400) { WindowMilliseconds = 600 };
        CtrlC(detector, 1000);

        Assert.True(CtrlC(detector, 1550));
    }
}
