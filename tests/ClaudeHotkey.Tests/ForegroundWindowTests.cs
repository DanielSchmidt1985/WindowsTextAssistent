using ClaudeHotkey.Core.Windows;

namespace ClaudeHotkey.Tests;

public class ForegroundWindowTests
{
    // Work area 0,0 – 1920,1040 (taskbar at the bottom), popup 400 × 300, offset 12.
    private static (int, int) Place(int x, int y) =>
        ForegroundWindow.Clamp(x + 12, y + 12, 400, 300, 0, 0, 1920, 1040, x, y, 12);

    [Fact]
    public void Opens_below_right_of_mouse_when_there_is_room() => Assert.Equal((112, 112), Place(100, 100));

    [Fact]
    public void Opens_left_of_mouse_near_right_edge() => Assert.Equal((1900 - 12 - 400, 112), Place(1900, 100));

    [Fact]
    public void Opens_above_mouse_near_bottom_edge() => Assert.Equal((112, 1000 - 12 - 300), Place(100, 1000));

    [Fact]
    public void Stays_inside_work_area_on_small_screens()
    {
        var (left, top) = ForegroundWindow.Clamp(212, 212, 400, 300, 0, 0, 500, 350, 200, 200, 12);

        Assert.InRange(left, 0, 100);
        Assert.InRange(top, 0, 50);
    }

    [Fact]
    public void Handles_monitors_left_of_the_primary()
    {
        // Second monitor at -1920 … 0.
        var (left, top) = ForegroundWindow.Clamp(-88, 112, 400, 300, -1920, 0, 0, 1040, -100, 100, 12);

        Assert.Equal((-100 - 12 - 400, 112), (left, top));
    }
}
