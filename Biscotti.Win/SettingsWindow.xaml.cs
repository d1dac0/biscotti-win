using System.Windows;
using System.Windows.Input;

namespace Biscotti.Win;

public partial class SettingsWindow : Window
{
    private readonly Func<string, string, string?> _saveSettings;
    private string _candidate;
    private bool _capturing;

    public SettingsWindow(string currentShortcut, string currentVocabulary,
        Func<string, string, string?> saveSettings, string? startupError = null)
    {
        InitializeComponent();
        _candidate = currentShortcut;
        _saveSettings = saveSettings;
        ShortcutBox.Text = currentShortcut;
        VocabularyBox.Text = currentVocabulary;
        if (!string.IsNullOrWhiteSpace(startupError))
        {
            MessageText.Text = startupError;
            MessageText.SetResourceReference(ForegroundProperty, "DangerBrush");
        }
    }

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        MessageText.Text = "Press a key together with Ctrl, Alt, Shift, or Win. Escape cancels.";
        CaptureButton.Content = "Press keys…";
        Focus();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturing) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _capturing = false;
            CaptureButton.Content = "Record shortcut";
            MessageText.Text = "Shortcut capture cancelled.";
            return;
        }

        if (!HotkeyChord.TryCreate(key, Keyboard.Modifiers, out var chord))
        {
            MessageText.Text = "Use a key with at least one modifier. F12 is reserved by Windows.";
            return;
        }

        _candidate = chord.Display;
        ShortcutBox.Text = _candidate;
        MessageText.Text = "Shortcut selected. Save to register it.";
        _capturing = false;
        CaptureButton.Content = "Record shortcut";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var error = _saveSettings(_candidate, VocabularyBox.Text);
        if (error is not null)
        {
            MessageText.Text = error;
            MessageText.SetResourceReference(ForegroundProperty, "DangerBrush");
            return;
        }
        DialogResult = true;
    }
}
