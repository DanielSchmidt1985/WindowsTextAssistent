using ClaudeHotkey.Core.Config;

namespace ClaudeHotkey.Tests;

public class UiStateTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("claudehotkey-ui");

    public void Dispose() => _dir.Delete(recursive: true);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var state = new UiStateStore(_dir.FullName).Load();

        Assert.Equal(574, state.PopupWidth);
        Assert.Equal(125, state.Scale);
        Assert.Null(state.SettingsWindow);
    }

    [Fact]
    public void Computed_scale_factor_is_not_written()
    {
        var store = new UiStateStore(_dir.FullName);
        store.Update(s => s.Scale = 150);

        Assert.DoesNotContain("scaleFactor", File.ReadAllText(store.FilePath));
        Assert.Equal(1.5, store.Current.ScaleFactor);
    }

    [Fact]
    public void Update_is_saved_and_loaded_again()
    {
        new UiStateStore(_dir.FullName).Update(s =>
        {
            s.PopupWidth = 700;
            s.Scale = 130;
            s.SettingsWindow = new WindowBounds(10, 20, 900, 700);
        });

        var loaded = new UiStateStore(_dir.FullName).Load();

        Assert.Equal(700, loaded.PopupWidth);
        Assert.Equal(130, loaded.Scale);
        Assert.Equal(new WindowBounds(10, 20, 900, 700), loaded.SettingsWindow);
    }

    [Fact]
    public void Values_out_of_range_are_clamped()
    {
        File.WriteAllText(Path.Combine(_dir.FullName, UiStateStore.FileName),
            """{ "popupWidth": 50, "scale": 999, "settingsWindow": { "left": 0, "top": 0, "width": 10, "height": 10 } }""");

        var state = new UiStateStore(_dir.FullName).Load();

        Assert.Equal(UiState.MinPopupWidth, state.PopupWidth);
        Assert.Equal(UiState.MaxScale, state.Scale);
        Assert.Null(state.SettingsWindow);
    }

    [Fact]
    public void Broken_file_falls_back_to_defaults()
    {
        File.WriteAllText(Path.Combine(_dir.FullName, UiStateStore.FileName), "{ kaputt");

        Assert.Equal(UiState.DefaultScale, new UiStateStore(_dir.FullName).Load().Scale);
    }

    [Theory]
    [InlineData(100, 1, 110)]
    [InlineData(100, -1, 90)]
    [InlineData(195, 1, 200)]
    [InlineData(200, 1, 200)]
    [InlineData(80, -1, 75)]
    [InlineData(75, -1, 75)]
    [InlineData(137, 1, 150)]  // back onto the 10 % grid
    [InlineData(150, 0, 100)]  // Ctrl+0
    public void Scale_steps_in_ten_percent(int current, int direction, int expected)
    {
        Assert.Equal(expected, UiState.StepScale(current, direction));
    }
}
