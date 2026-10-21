using System;
using System.ComponentModel;
using Microsoft.Win32;
using System.Windows;

namespace Biscotti.Win;

internal static class ThemePalette
{
    public static void Initialize()
    {
        Apply();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public static void Dispose()
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private static void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            System.Windows.Application.Current.Dispatcher.Invoke(Apply);
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(new Action(() =>
        {
            if (!dispatcher.HasShutdownStarted) Apply();
        }));
    }

    internal static void Apply() => Apply(SystemParameters.HighContrast, IsDarkModeEnabled());

    internal static void Apply(bool highContrast) => Apply(highContrast, darkMode: false);

    internal static void Apply(bool highContrast, bool darkMode)
    {
        var resources = System.Windows.Application.Current.Resources;
        var paletteKeys = new[] { "PaperBrush", "CardBrush", "InkBrush", "MutedBrush",
            "AccentBrush", "AccentTextBrush", "WashBrush", "LineBrush", "DangerBrush",
            "AccentHoverBrush", "AccentPressedBrush" };
        var systemColorKeys = new object[]
        {
            SystemColors.WindowBrushKey, SystemColors.WindowTextBrushKey,
            SystemColors.ControlBrushKey, SystemColors.ControlTextBrushKey,
            SystemColors.GrayTextBrushKey, SystemColors.HighlightBrushKey,
            SystemColors.HighlightTextBrushKey, SystemColors.ControlLightBrushKey,
            SystemColors.ControlLightLightBrushKey, SystemColors.ControlDarkBrushKey,
            SystemColors.ControlDarkDarkBrushKey, SystemColors.ScrollBarBrushKey,
            SystemColors.ActiveBorderBrushKey, SystemColors.InactiveBorderBrushKey,
        };
        // Removing root overrides restores the light palette from Theme.xaml.
        foreach (var key in paletteKeys)
            resources.Remove(key);
        foreach (var key in systemColorKeys)
            resources.Remove(key);
        if (highContrast)
        {
            resources["PaperBrush"] = resources["CardBrush"] = resources["WashBrush"] = SystemColors.WindowBrush;
            resources["InkBrush"] = resources["MutedBrush"] = resources["DangerBrush"] = SystemColors.WindowTextBrush;
            resources["AccentBrush"] = SystemColors.HighlightBrush;
            resources["AccentTextBrush"] = SystemColors.HighlightTextBrush;
            resources["LineBrush"] = SystemColors.WindowTextBrush;
            resources["AccentHoverBrush"] = resources["AccentPressedBrush"] = SystemColors.HighlightBrush;
            return;
        }

        if (!darkMode) return;
        var app = System.Windows.Application.Current;
        foreach (var (targetKey, sourceKey) in new[]
        {
            ("PaperBrush", "DarkPaperBrush"), ("CardBrush", "DarkCardBrush"),
            ("InkBrush", "DarkInkBrush"), ("MutedBrush", "DarkMutedBrush"),
            ("AccentBrush", "DarkAccentBrush"), ("AccentTextBrush", "DarkAccentTextBrush"),
            ("WashBrush", "DarkWashBrush"), ("LineBrush", "DarkLineBrush"),
            ("DangerBrush", "DarkDangerBrush"), ("AccentHoverBrush", "DarkAccentHoverBrush"),
            ("AccentPressedBrush", "DarkAccentPressedBrush"),
        })
            resources[targetKey] = app.FindResource(sourceKey);

        resources[SystemColors.WindowBrushKey] = resources["PaperBrush"];
        resources[SystemColors.WindowTextBrushKey] = resources["InkBrush"];
        resources[SystemColors.ControlBrushKey] = resources["CardBrush"];
        resources[SystemColors.ControlTextBrushKey] = resources["InkBrush"];
        resources[SystemColors.GrayTextBrushKey] = resources["MutedBrush"];
        resources[SystemColors.HighlightBrushKey] = resources["AccentBrush"];
        resources[SystemColors.HighlightTextBrushKey] = resources["AccentTextBrush"];
        resources[SystemColors.ControlLightBrushKey] = resources["LineBrush"];
        resources[SystemColors.ControlLightLightBrushKey] = resources["DarkWashBrush"];
        resources[SystemColors.ControlDarkBrushKey] = resources["LineBrush"];
        resources[SystemColors.ControlDarkDarkBrushKey] = resources["LineBrush"];
        resources[SystemColors.ScrollBarBrushKey] = resources["DarkWashBrush"];
        resources[SystemColors.ActiveBorderBrushKey] = resources["LineBrush"];
        resources[SystemColors.InactiveBorderBrushKey] = resources["LineBrush"];
    }

    private static bool IsDarkModeEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme", 1) is int value && value == 0;
    }
}
