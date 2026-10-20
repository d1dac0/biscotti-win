using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Biscotti.Core.Storage;

internal interface IMeetingTextProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

internal sealed class DpapiMeetingTextProtector : IMeetingTextProtector
{
    public byte[] Protect(byte[] plaintext)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Meeting text protection requires Windows DPAPI.");
        return ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] ciphertext)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Meeting text protection requires Windows DPAPI.");
        return ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }
}

internal static class ProtectedMeetingText
{
    private static readonly byte[] Marker = "BISCOTTI-DPAPI:1\n"u8.ToArray();
    private static ReadOnlySpan<byte> MarkerFamily => "BISCOTTI-DPAPI:"u8;
    private static readonly IMeetingTextProtector DefaultProtector = new DpapiMeetingTextProtector();

    internal static IMeetingTextProtector? ProtectorOverride { get; set; }

    public static byte[] ReadAndMigrate(string path, Action<byte[]>? validateLegacy = null)
    {
        var protector = ProtectorOverride ?? DefaultProtector;
        byte[] source;
        try { source = File.ReadAllBytes(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw MeetingTextStorageException.For(path, "read", ex);
        }

        if (HasMarker(source))
            return Unprotect(path, source.AsSpan(Marker.Length).ToArray(), protector);
        if (source.AsSpan().StartsWith(MarkerFamily))
            throw MeetingTextStorageException.For(path, "read", new InvalidDataException(
                "The meeting data uses an unsupported DPAPI envelope version and was not changed."));

        try
        {
            validateLegacy?.Invoke(source);
            WriteProtectedAtomically(path, source, protector);
            return source;
        }
        catch (Exception ex)
        {
            CryptographicOperations.ZeroMemory(source);
            if (ex is MeetingTextStorageException) throw;
            if (ex is JsonException or InvalidDataException)
                throw MeetingTextStorageException.For(path, "validate legacy content", ex);
            throw;
        }
    }

    public static void WriteProtectedAtomically(string path, byte[] plaintext)
    {
        WriteProtectedAtomically(path, plaintext, ProtectorOverride ?? DefaultProtector);
    }

    private static void WriteProtectedAtomically(string path, byte[] plaintext, IMeetingTextProtector protector)
    {
        var temp = path + ".tmp";
        byte[] protectedBytes;
        try { protectedBytes = protector.Protect(plaintext); }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            throw MeetingTextStorageException.For(path, "protect", ex);
        }

        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(Marker);
                stream.Write(protectedBytes);
                stream.Flush(flushToDisk: true);
            }

            var envelope = File.ReadAllBytes(temp);
            if (!HasMarker(envelope))
                throw new InvalidDataException("The temporary protected file is missing its format marker.");
            var verified = Unprotect(path, envelope.AsSpan(Marker.Length).ToArray(), protector);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(plaintext, verified))
                    throw new InvalidDataException("The temporary protected file did not match the original content.");
            }
            finally { CryptographicOperations.ZeroMemory(verified); }

            File.Move(temp, path, overwrite: true);
        }
        catch (MeetingTextStorageException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw MeetingTextStorageException.For(path, "write and verify protected replacement", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static byte[] Unprotect(string path, byte[] ciphertext, IMeetingTextProtector protector)
    {
        try { return protector.Unprotect(ciphertext); }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            throw MeetingTextStorageException.For(path, "unprotect", ex);
        }
        finally { CryptographicOperations.ZeroMemory(ciphertext); }
    }

    private static bool HasMarker(ReadOnlySpan<byte> bytes) => bytes.StartsWith(Marker);
}

public sealed class MeetingTextStorageException : IOException
{
    private MeetingTextStorageException(string message, Exception? inner) : base(message, inner) { }

    internal static MeetingTextStorageException For(string path, string operation, Exception inner)
    {
        var meetingId = Path.GetFileName(Path.GetDirectoryName(path));
        var fileName = Path.GetFileName(path);
        var detail = inner is CryptographicException or PlatformNotSupportedException
            ? " The protected source was preserved. Sign in to the Windows profile that created this data. An administrator password reset may make DPAPI-protected data unavailable."
            : " Any existing source file was preserved.";
        return new MeetingTextStorageException(
            $"Could not {operation} {fileName} for meeting {meetingId}.{detail} {inner.Message}", inner);
    }
}
