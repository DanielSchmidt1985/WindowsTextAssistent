using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;

namespace ClaudeHotkey.Tests;

/// <summary>Loads Theme.Neptun.xaml and shows its controls in a real (off-screen) window.</summary>
public class ThemeTests
{
    private static string ThemePath([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "src", "ClaudeHotkey", "Themes", "Theme.Neptun.xaml"));

    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            // Without this, an exception inside a window message (e.g. during Show) ends the test host.
            Dispatcher.CurrentDispatcher.UnhandledException += (_, e) => { error ??= e.Exception; e.Handled = true; };
            try { action(); }
            catch (Exception ex) { error ??= ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
            throw new Xunit.Sdk.XunitException(error.ToString());
    }

    /// <summary>Runs queued dispatcher work (layout, Loaded, triggers); errors land in OnSta.</summary>
    private static void Pump(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    private static string PalettePath(string name) => Path.Combine(Path.GetDirectoryName(ThemePath())!, $"Colors.{name}.xaml");

    private static ResourceDictionary Load(string path)
    {
        using var stream = File.OpenRead(path);
        return (ResourceDictionary)XamlReader.Load(stream);
    }

    /// <summary>Styles plus one palette, as in App.xaml.</summary>
    private static ResourceDictionary LoadTheme(string palette = "Dark")
    {
        var theme = Load(ThemePath());
        theme.MergedDictionaries.Add(Load(PalettePath(palette)));
        return theme;
    }

    [Fact]
    public void Both_palettes_define_the_same_tokens()
    {
        OnSta(() =>
        {
            var dark = Load(PalettePath("Dark")).Keys.Cast<string>().Order().ToList();
            var light = Load(PalettePath("Light")).Keys.Cast<string>().Order().ToList();

            Assert.Equal(dark, light);
            Assert.All(dark, key => Assert.EndsWith("Brush", key)); // theme.json only accepts *Brush tokens
        });
    }

    [Fact]
    public void Styles_use_no_colour_that_is_not_a_token()
    {
        // Every colour must come from the palette, otherwise it would not switch with the theme.
        var styles = File.ReadAllText(ThemePath());
        Assert.DoesNotMatch(@"StaticResource \w+Brush", styles);
        Assert.DoesNotMatch(@"Color=""#", styles);
    }

    private static Window OffScreenWindow(ResourceDictionary theme, UIElement content) => new()
    {
        Resources = theme,
        Content = content,
        Left = -20000,
        Top = -20000,
        Width = 400,
        Height = 700,
        ShowActivated = false,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
    };

    [Fact]
    public void Busy_bar_that_becomes_visible_after_load_does_not_throw()
    {
        // Popup flow: the bar is collapsed when the window opens and shown once an action runs.
        OnSta(() =>
        {
            var theme = LoadTheme();
            var bar = new ProgressBar { Style = (Style)theme["BusyBar"], Visibility = Visibility.Collapsed };
            var window = OffScreenWindow(theme, new StackPanel { Children = { bar } });
            try
            {
                window.Show();
                Pump(window.Dispatcher);

                bar.Visibility = Visibility.Visible;
                Pump(window.Dispatcher);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Busy_bar_visible_from_the_start_does_not_throw()
    {
        OnSta(() =>
        {
            var theme = LoadTheme();
            var window = OffScreenWindow(theme, new StackPanel { Children = { new ProgressBar { Style = (Style)theme["BusyBar"] } } });
            try
            {
                window.Show();
                Pump(window.Dispatcher);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Every_control_style_applies_without_errors(string palette)
    {
        OnSta(() =>
        {
            var theme = LoadTheme(palette);
            var panel = new StackPanel
            {
                Children =
                {
                    new Button { Content = "Standard" },
                    new Button { Content = "Primär", Style = (Style)theme["PrimaryButton"] },
                    new Button { Content = "✕", Style = (Style)theme["IconButton"] },
                    new Button { Content = "Details", Style = (Style)theme["LinkButton"] },
                    new TextBox { Text = "Eingabe" },
                    new TextBox { Text = "Nur lesen", Style = (Style)theme["ReadOnlyText"] },
                    new ScrollViewer { Height = 40, Content = new TextBlock { Text = string.Join("\n", Enumerable.Range(1, 20)) } },
                    new TextBlock { Text = "Überschrift", Style = (Style)theme["Heading"] },
                    new CheckBox { Content = "Schalter", IsChecked = true },
                    new ComboBox { IsEditable = true, ItemsSource = new[] { "haiku", "sonnet" }, Text = "haiku" },
                    new ComboBox { ItemsSource = new[] { "a", "b" }, SelectedIndex = 0 },
                    new ListBox { ItemsSource = new[] { "Eins", "Zwei" }, SelectedIndex = 0, Height = 60 },
                    new TabControl { Height = 60, Items = { new TabItem { Header = "A", Content = "1" }, new TabItem { Header = "B", Content = "2" } } },
                },
            };
            var window = OffScreenWindow(theme, panel);
            try
            {
                window.Show();
                Pump(window.Dispatcher);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
