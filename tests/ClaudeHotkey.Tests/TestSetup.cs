using System.Runtime.CompilerServices;
using ClaudeHotkey.Core.Localization;

namespace ClaudeHotkey.Tests;

internal static class TestSetup
{
    /// <summary>
    /// The tests check the German texts, whatever the display language of the machine. Tests for
    /// English call the language-specific functions directly instead of switching Lang.Current,
    /// because xUnit runs test classes in parallel.
    /// </summary>
    [ModuleInitializer]
    internal static void UseGerman() => Lang.Current = UiLanguage.German;
}
