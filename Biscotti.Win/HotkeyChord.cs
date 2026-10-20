using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;

namespace Biscotti.Win;

internal readonly record struct HotkeyChord(uint Modifiers, uint VirtualKey, string Display)
{
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModWin = 0x0008;
    internal const uint ModNoRepeat = 0x4000;

    internal static bool TryParse(string? value, out HotkeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        uint modifiers = 0;
        Key? key = null;
        foreach (var part in parts)
        {
            var modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModControl,
                "alt" => ModAlt,
                "shift" => ModShift,
                "win" or "windows" => ModWin,
                _ => 0u,
            };
            if (modifier != 0)
            {
                if ((modifiers & modifier) != 0) return false;
                modifiers |= modifier;
                continue;
            }

            if (key is not null || !TryParseKey(part, out var parsedKey)) return false;
            key = parsedKey;
        }

        if (key is null || modifiers == 0 || key == Key.None || key == Key.System) return false;
        var virtualKeyValue = KeyInterop.VirtualKeyFromKey(key.Value);
        if (virtualKeyValue is <= 0 or > 0xFF || key == Key.F12) return false;
        var virtualKey = (uint)virtualKeyValue;
        chord = new HotkeyChord(modifiers, virtualKey, Format(modifiers, key.Value));
        return true;
    }

    internal static bool TryCreate(Key key, ModifierKeys modifiers, out HotkeyChord chord)
    {
        var names = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) names.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) names.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) names.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) names.Add("Win");
        if (key is Key.None or Key.System or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            chord = default;
            return false;
        }
        names.Add(DisplayKey(key));
        return TryParse(string.Join('+', names), out chord);
    }

    private static bool TryParseKey(string value, out Key key)
    {
        if (value.Length == 1 && char.IsAsciiLetter(value[0]))
        {
            key = Enum.Parse<Key>(char.ToUpperInvariant(value[0]).ToString());
            return true;
        }
        if (value.Length == 1 && char.IsAsciiDigit(value[0]))
        {
            key = Enum.Parse<Key>("D" + value);
            return true;
        }
        return Enum.TryParse(value, ignoreCase: true, out key) && key != Key.None;
    }

    private static string Format(uint modifiers, Key key)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(DisplayKey(key));
        return string.Join('+', parts);
    }

    private static string DisplayKey(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => "NumPad" + ((int)key - (int)Key.NumPad0),
        _ => key.ToString(),
    };
}

internal sealed class GlobalHotkeyRegistration : IDisposable
{
    private const int HotkeyId = 0xB157;
    private const int WmHotkey = 0x0312;
    private readonly HwndSource _source;
    private HotkeyChord? _registered;

    internal event EventHandler? Pressed;

    internal GlobalHotkeyRegistration(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Window message source is unavailable.");
        _source.AddHook(WindowProc);
    }

    internal bool TrySet(string? value, out string error)
    {
        if (!HotkeyChord.TryParse(value, out var requested))
        {
            error = "Enter a shortcut with at least one modifier, such as Ctrl+Alt+R.";
            return false;
        }

        var previous = _registered;
        UnregisterCurrent();
        if (Register(requested))
        {
            _registered = requested;
            error = "";
            return true;
        }

        var lastError = Marshal.GetLastWin32Error();
        if (previous is { } old && Register(old)) _registered = old;
        error = lastError == 1409
            ? $"{requested.Display} is already registered by another app. Choose a different shortcut."
            : $"Windows could not register {requested.Display} (error {lastError}). Choose a different shortcut.";
        return false;
    }

    internal string? Current => _registered?.Display;

    internal void Clear() => UnregisterCurrent();

    private bool Register(HotkeyChord chord) =>
        RegisterHotKey(_source.Handle, HotkeyId, chord.Modifiers | HotkeyChord.ModNoRepeat, chord.VirtualKey);

    private void UnregisterCurrent()
    {
        if (_registered is null) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = null;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterCurrent();
        _source.RemoveHook(WindowProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
