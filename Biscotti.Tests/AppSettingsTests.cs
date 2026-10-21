using System.Text.Json;
using Biscotti.Core;

namespace Biscotti.Tests;

public sealed class AppSettingsTests
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

    [Fact]
    public void LocalVocabulary_RoundTripsAndBuildsWhisperPrompt()
    {
        var changedSettings = new AppSettings { LocalVocabulary = "Biscotti, Whisper.net\nBiscotti" };
        var roundTripped = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(changedSettings));

        Assert.NotNull(roundTripped);
        Assert.Equal(changedSettings.LocalVocabulary, roundTripped.LocalVocabulary);
        Assert.Equal(
            "Names and terms that may appear in the audio: Biscotti, Whisper.net.",
            Biscotti.Core.Transcription.WhisperTranscriber.BuildInitialPrompt(roundTripped.LocalVocabulary));
    }

    [Fact]
    public void EmptyLocalVocabularyDoesNotAddAnInitialPrompt()
    {
        Assert.Null(Biscotti.Core.Transcription.WhisperTranscriber.BuildInitialPrompt("  ;\r\n, "));
    }

    [Fact]
    public void CalendarPreferencesDefaultSafelyAndRoundTrip()
    {
        var defaults = JsonSerializer.Deserialize<AppSettings>("{}");
        Assert.NotNull(defaults);
        Assert.False(defaults.CalendarStartNotifications);
        Assert.True(defaults.CalendarTrayNavigation);

        var settings = new AppSettings { CalendarStartNotifications = true, CalendarTrayNavigation = false };
        var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));
        Assert.True(roundTrip!.CalendarStartNotifications);
        Assert.False(roundTrip.CalendarTrayNavigation);
    }

    [Fact]
    public void MicrophoneEndpointIdRoundTripsAndDefaultsForExistingSettings()
    {
        Assert.Null(JsonSerializer.Deserialize<AppSettings>("{}")!.MicDeviceEndpointId);

        var settings = new AppSettings { MicDeviceEndpointId = "windows-microphone-endpoint" };
        var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));

        Assert.Equal(settings.MicDeviceEndpointId, roundTrip!.MicDeviceEndpointId);
    }
}
