using System.ComponentModel;
using System.Windows;

namespace Biscotti.Win;

internal static class ThemePalette
{
    public static void Initialize()
    {
        Apply();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
    }

    public static void Dispose() => SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;

    private static void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            System.Windows.Application.Current.Dispatcher.Invoke(Apply);
    }

    internal static void Apply() => Apply(SystemParameters.HighContrast);

    internal static void Apply(bool highContrast)
    {
        var resources = System.Windows.Application.Current.Resources;
        // Removing root overrides restores Theme.xaml when high contrast is off.
        foreach (var key in new[] { "PaperBrush", "CardBrush", "InkBrush", "MutedBrush",
                     "AccentBrush", "AccentTextBrush", "WashBrush", "LineBrush", "DangerBrush" })
            resources.Remove(key);
        if (!highContrast) return;
        resources["PaperBrush"] = resources["CardBrush"] = resources["WashBrush"] = SystemColors.WindowBrush;
        resources["InkBrush"] = resources["MutedBrush"] = resources["DangerBrush"] = SystemColors.WindowTextBrush;
        resources["AccentBrush"] = SystemColors.HighlightBrush;
        resources["AccentTextBrush"] = SystemColors.HighlightTextBrush;
        resources["LineBrush"] = SystemColors.WindowTextBrush;
    }
}
