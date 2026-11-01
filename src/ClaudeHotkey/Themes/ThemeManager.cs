using System.Windows;
using ClaudeHotkey.Core.Config;
using ClaudeHotkey.Views;
using Microsoft.Win32;

namespace ClaudeHotkey.Themes;

/// <summary>
/// Puts the dark or light palette into the application resources. Windows bind every colour with
/// DynamicResource, so open windows follow at once. theme.json overrides go on top of either palette.
/// </summary>
public static class ThemeManager
{
    public static bool IsLight { get; private set; }

    /// <summary>Windows "app mode" (Settings > Personalization > Colors). Dark when it cannot be read.</summary>
    public static bool WindowsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <param name="setting">settings.theme: "system", "dark" or "light".</param>
    /// <returns>Problems in theme.json; empty when it is missing or fine.</returns>
    public static IReadOnlyList<string> Apply(ResourceDictionary resources, string? setting, string overridesDirectory)
    {
        var light = ThemeSetting.IsLight(setting, WindowsUsesLightTheme());
        var palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Colors.{(light ? "Light" : "Dark")}.xaml", UriKind.Absolute),
        };
        // Top-level entries win over the merged start palette.
        foreach (var key in palette.Keys)
            resources[key] = palette[key];
        var problems = ThemeOverrides.Apply(resources, overridesDirectory);

        if (light != IsLight)
        {
            IsLight = light;
            foreach (Window window in Application.Current.Windows)
                ThemedTitleBar.Apply(window);
        }
        return problems;
    }
}
