using System.Text.Json;
using Biscotti.Core;

namespace Biscotti.Tests;

public sealed class HotkeySettingsTests
{
    [Fact]
    public void RecordingHotkey_DefaultsForExistingSettings_AndRoundTripsCustomValue()
    {
        var existingSettings = JsonSerializer.Deserialize<AppSettings>("{}");
        Assert.NotNull(existingSettings);
        Assert.Equal("Ctrl+Alt+R", existingSettings.RecordingHotkey);

        var changedSettings = new AppSettings { RecordingHotkey = "Ctrl+Shift+M" };
        var roundTripped = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(changedSettings));

        Assert.NotNull(roundTripped);
        Assert.Equal("Ctrl+Shift+M", roundTripped.RecordingHotkey);
    }
}
