using System.Diagnostics;

namespace Biscotti.Core.Transcription;

/// <summary>
/// NVIDIA GPU detection using nvidia-smi, which ships with every NVIDIA
/// display driver on Windows. No CUDA Toolkit install is probed and no
/// nvml dependency is linked: if nvidia-smi answers with a device we know
/// the driver is present and CUDA-capable hardware exists (issue #20).
/// </summary>
public static class GpuDetector
{
    public record GpuInfo(string Description);

    public static GpuInfo? DetectNvidia() => DetectWithProbe(() =>
    {
        var psi = new ProcessStartInfo("nvidia-smi", "-L")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return "";
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return text;
        }
        catch
        {
            // nvidia-smi.exe missing (no NVIDIA driver) or launch blocked
            return "";
        }
    });

    /// <summary>
    /// Injectable core so tests can feed synthetic `nvidia-smi -L` output.
    /// The probe must return the tool's stdout (empty string on failure).
    /// Example real line: "GPU 0: NVIDIA GeForce RTX 3080 (UUID: GPU-...)"
    /// </summary>
    internal static GpuInfo? DetectWithProbe(Func<string> probe)
    {
        string output;
        try { output = probe(); } catch { return null; }
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("GPU ", StringComparison.Ordinal)) continue;
            int sep = line.IndexOf(": ", StringComparison.Ordinal);
            if (sep < 0) continue;
            var desc = line[(sep + 2)..].Trim();
            int uuid = desc.IndexOf(" (UUID", StringComparison.Ordinal);
            if (uuid > 0) desc = desc[..uuid].Trim();
            if (desc.Length == 0) continue;
            return new GpuInfo(desc);
        }
        return null;
    }
}
