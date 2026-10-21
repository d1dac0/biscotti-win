namespace Biscotti.Core.Audio;

internal sealed record CaptureEndpointInfo(string Id, string Name);

internal static class MicrophoneEndpointMatcher
{
    /// <summary>
    /// Associates a legacy WaveIn choice with a Core Audio endpoint only when
    /// the names identify exactly one device on both sides.
    /// </summary>
    public static IReadOnlyList<WaveInDeviceInfo> Attach(
        IReadOnlyList<WaveInDeviceInfo> microphones,
        IReadOnlyList<CaptureEndpointInfo> endpoints)
    {
        var microphoneNames = microphones.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var endpointGroups = endpoints.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.OrdinalIgnoreCase);
        return microphones.Select(m =>
        {
            var matched = endpointGroups.TryGetValue(m.Name, out var candidates) && candidates.Length == 1
                && microphoneNames[m.Name] == 1 ? candidates[0].Id : null;
            return m with { EndpointId = matched };
        }).ToArray();
    }
}

internal static class WindowsEchoCancellationPolicy
{
    public const int MinimumReferenceControlBuild = 22621;

    public static bool CanUseReferenceControl(int osBuild, string? microphoneEndpointId) =>
        osBuild >= MinimumReferenceControlBuild && !string.IsNullOrWhiteSpace(microphoneEndpointId);

    public static string UnavailableStatus(int osBuild, string? microphoneEndpointId) =>
        osBuild < MinimumReferenceControlBuild
            ? "Windows AEC reference control requires Windows 11 22H2 or later. Microphone audio is recorded without echo cancellation."
            : string.IsNullOrWhiteSpace(microphoneEndpointId)
                ? "The selected microphone could not be matched to a Windows audio endpoint. Microphone audio is recorded without echo cancellation."
                : "The selected microphone does not provide Windows AEC reference control. Microphone audio is recorded without echo cancellation.";
}
