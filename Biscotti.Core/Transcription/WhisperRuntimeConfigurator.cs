using Biscotti.Core.Diagnostics;
using Whisper.net.LibraryLoader;

namespace Biscotti.Core.Transcription;

/// <summary>
/// Applies the Whisper runtime selection once at app start, before any
/// WhisperFactory is created (Whisper.net's RuntimeOptions are read lazily
/// at first native load and are effectively write-once). See issue #20:
/// the user's explicit opt-in ("gpu") only engages when the lazily
/// downloaded CUDA runtime files are actually installed; otherwise the app
/// silently stays on the bundled CPU runtime — the hard crash lesson of
/// issue #3 is never repeated.
/// </summary>
public static class WhisperRuntimeConfigurator
{
    /// <summary>Pure mapping — no static whisper.net state is touched.</summary>
    internal static List<RuntimeLibrary> SelectOrder(string? whisperRuntime, bool gpuFilesInstalled)
        => string.Equals(whisperRuntime, "gpu", StringComparison.OrdinalIgnoreCase) && gpuFilesInstalled
            ? [RuntimeLibrary.Cuda, RuntimeLibrary.Cpu]
            : [RuntimeLibrary.Cpu];

    /// <summary>
    /// Returns "gpu" when the CUDA runtime is engaged, "cpu" otherwise.
    /// </summary>
    public static string Apply(AppSettings settings)
    {
        var store = new GpuRuntimeStore();
        var installed = store.IsInstalled();
        RuntimeOptions.RuntimeLibraryOrder = SelectOrder(settings.WhisperRuntime, installed);
        if (settings.WhisperRuntime == "gpu" && installed)
        {
            RuntimeOptions.LibraryPath = store.LibraryPathMarker;
            store.PrimeCudaRuntime();
            SessionDiagnostics.Info("whisper", "runtime engaged=gpu (CUDA)");
            return "gpu";
        }
        SessionDiagnostics.Info("whisper",
            settings.WhisperRuntime == "gpu"
                ? "runtime engaged=cpu (gpu requested but CUDA runtime files not installed)"
                : "runtime engaged=cpu");
        return "cpu";
    }
}
