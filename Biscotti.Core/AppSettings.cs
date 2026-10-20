using Biscotti.Core.Transcription;

namespace Biscotti.Core;

public sealed class AppSettings
{
    public int? MicDeviceIndex { get; set; }
    public string? MicDeviceName { get; set; }
    public string? SpeakerDeviceId { get; set; }
    public string? WhisperModelId { get; set; } = ModelStore.LargeV3Turbo.Id;
    public string? OllamaModel { get; set; } = "gemma3:4b";
    /// <summary>
    /// Transcription backend choice (issue #20): null until the first-run
    /// NVIDIA prompt decides, then "cpu" or "gpu". "gpu" only engages when
    /// the lazily downloaded CUDA runtime is installed; CPU is the fallback.
    /// </summary>
    public string? WhisperRuntime { get; set; }
    /// <summary>Global Start/Stop shortcut (issue #18), such as Ctrl+Alt+R.</summary>
    public string? RecordingHotkey { get; set; } = "Ctrl+Alt+R";
    /// <summary>Path to the user's local iCalendar export; no calendar contents are copied or uploaded.</summary>
    public string? CalendarFilePath { get; set; }

    public string GetResolvedModelPath()
    {
        var m = ModelStore.All.FirstOrDefault(x => x.Id == WhisperModelId) ?? ModelStore.LargeV3Turbo;
        return ModelStore.ModelPath(m);
    }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BiscottiWin", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
