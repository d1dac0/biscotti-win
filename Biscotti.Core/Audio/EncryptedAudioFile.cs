using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Biscotti.Core.Audio;

internal static class EncryptedAudioFile
{
    public static bool IsEncrypted(string path)
    {
        if (!File.Exists(path)) return false;
        Span<byte> magic = stackalloc byte[EncryptedAudioWriter.Magic.Length];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return stream.Read(magic) == magic.Length && magic.SequenceEqual(EncryptedAudioWriter.Magic);
    }
}

/// <summary>Protects a meeting key for the current Windows user.</summary>
internal interface IMeetingKeyProtector
{
    byte[] Protect(byte[] key);
    byte[] Unprotect(byte[] protectedKey);
}

/// <summary>Opens saved meeting audio as an authenticated, decrypt-on-read WAV stream.</summary>
public static class EncryptedAudioPlayback
{
    public static Stream OpenWavStream(string path) => OpenWavStream(path, protector: null);

    internal static Stream OpenWavStream(string path, IMeetingKeyProtector? protector)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"No saved audio was found for {MeetingAudioMigration.AudioIdentity(path)}.", path);
        if (!EncryptedAudioFile.IsEncrypted(path))
            throw new InvalidDataException($"{MeetingAudioMigration.AudioIdentity(path)} does not have an encrypted audio header.");

        EncryptedAudioReader reader;
        try { reader = new EncryptedAudioReader(path, protector); }
        catch (CryptographicException ex) { throw MeetingAudioMigration.KeyAccessFailure(path, ex); }

        try { return reader.OpenWavStream(); }
        catch
        {
            reader.Dispose();
            throw;
        }
    }
}

internal sealed class DpapiMeetingKeyProtector : IMeetingKeyProtector
{
    public byte[] Protect(byte[] key)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Meeting audio encryption requires Windows DPAPI.");
        return ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
    }

    public byte[] Unprotect(byte[] protectedKey)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Meeting audio decryption requires Windows DPAPI.");
        return ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
    }
}

/// <summary>Versioned AES-256-GCM frame container used at the existing WAV paths.</summary>
internal sealed class EncryptedAudioWriter : IDisposable
{
    internal const int FrameSize = 4096;
    internal const int TagSize = 16;
    internal const int HeaderPrefixSize = 8 + 1 + 4 + 2 + 2 + 4 + 4 + 2;
    internal static readonly byte[] Magic = "BISCWAV1"u8.ToArray();

    private readonly FileStream _stream;
    private readonly byte[] _key;
    private readonly byte[] _header;
    private readonly byte[] _pending = new byte[FrameSize];
    private int _pendingCount;
    private ulong _frameIndex;
    private long _pcmBytes;
    private bool _closed;

    public long PcmBytes => Interlocked.Read(ref _pcmBytes);

    public EncryptedAudioWriter(string path, byte[] meetingKey, uint channelId, IMeetingKeyProtector? protector = null)
    {
        if (meetingKey.Length != 32) throw new ArgumentException("The meeting key must be 256 bits.", nameof(meetingKey));
        protector ??= new DpapiMeetingKeyProtector();
        var wrapped = protector.Protect(meetingKey);
        _header = BuildHeader(channelId, wrapped);
        _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        _key = meetingKey.ToArray();
        try
        {
            _stream.Write(_header);
            _stream.Flush(true);
        }
        catch
        {
            _stream.Dispose();
            CryptographicOperations.ZeroMemory(_key);
            throw;
        }
    }

    public void Append(ReadOnlyMemory<byte> pcm)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        Interlocked.Add(ref _pcmBytes, pcm.Length);
        var input = pcm.Span;
        while (!input.IsEmpty)
        {
            var copy = Math.Min(FrameSize - _pendingCount, input.Length);
            input[..copy].CopyTo(_pending.AsSpan(_pendingCount));
            _pendingCount += copy;
            input = input[copy..];
            if (_pendingCount == FrameSize) WriteFrame(_pendingCount);
        }
    }

    private void WriteFrame(int length)
    {
        Span<byte> lengthBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBytes, length);
        var cipher = new byte[length];
        Span<byte> tag = stackalloc byte[TagSize];
        var nonce = MakeNonce(BinaryPrimitives.ReadUInt32LittleEndian(_header.AsSpan(17, 4)), _frameIndex);
        var aad = MakeAad(_header, _frameIndex, length);
        using (var aes = new AesGcm(_key, TagSize)) aes.Encrypt(nonce, _pending.AsSpan(0, length), cipher, tag, aad);
        _stream.Write(lengthBytes);
        _stream.Write(cipher);
        _stream.Write(tag);
        _stream.Flush();
        CryptographicOperations.ZeroMemory(_pending.AsSpan(0, length));
        _pendingCount = 0;
        _frameIndex++;
    }

    public void Close()
    {
        if (_closed) return;
        try
        {
            if (_pendingCount > 0) WriteFrame(_pendingCount);
            _stream.Flush(true);
            _stream.Dispose();
        }
        finally
        {
            try { _stream.Dispose(); } catch { }
            CryptographicOperations.ZeroMemory(_pending);
            CryptographicOperations.ZeroMemory(_key);
            _closed = true;
        }
    }

    public void Dispose() => Close();

    internal static byte[] BuildHeader(uint channelId, byte[] wrappedKey)
    {
        if (wrappedKey.Length > ushort.MaxValue) throw new CryptographicException("DPAPI wrapped key is too large.");
        var header = new byte[HeaderPrefixSize + wrappedKey.Length];
        Magic.CopyTo(header, 0);
        header[8] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(9, 4), AudioConfig.SampleRate);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(13, 2), AudioConfig.Channels);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(15, 2), (ushort)AudioConfig.BitsPerSample);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(17, 4), channelId);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(21, 4), FrameSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(25, 2), (ushort)wrappedKey.Length);
        wrappedKey.CopyTo(header, HeaderPrefixSize);
        return header;
    }

    internal static byte[] MakeNonce(uint channelId, ulong index)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(0, 4), channelId);
        BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4, 8), index);
        return nonce;
    }

    internal static byte[] MakeAad(byte[] header, ulong index, int length)
    {
        var aad = new byte[header.Length + 12];
        header.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt64BigEndian(aad.AsSpan(header.Length, 8), index);
        BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(header.Length + 8, 4), length);
        return aad;
    }
}

internal sealed class EncryptedAudioReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly byte[] _header;
    private readonly byte[] _key;
    private readonly uint _channelId;
    private readonly long _headerLength;
    private readonly List<FrameInfo> _frames = new();
    private long _scanPosition;
    private long _pcmLength;

    public long PcmLength { get; private set; }
    public bool HasTruncatedTail { get; private set; }
    public uint ChannelId => _channelId;

    public EncryptedAudioReader(string path, IMeetingKeyProtector? protector = null)
    {
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[]? unprotectedKey = null;
        try
        {
            var prefix = new byte[EncryptedAudioWriter.HeaderPrefixSize];
            _stream.ReadExactly(prefix);
            if (!prefix.AsSpan(0, 8).SequenceEqual(EncryptedAudioWriter.Magic) || prefix[8] != 1)
                throw new InvalidDataException("Unsupported encrypted audio header.");
            if (BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(9, 4)) != AudioConfig.SampleRate ||
                BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(13, 2)) != AudioConfig.Channels ||
                BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(15, 2)) != AudioConfig.BitsPerSample ||
                BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(21, 4)) != EncryptedAudioWriter.FrameSize)
                throw new InvalidDataException("Encrypted audio format does not match the supported PCM format.");
            _channelId = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(17, 4));
            var wrappedLength = BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(25, 2));
            if (wrappedLength == 0) throw new InvalidDataException("Encrypted audio header has no protected meeting key.");
            var wrapped = new byte[wrappedLength];
            _stream.ReadExactly(wrapped);
            _header = prefix.Concat(wrapped).ToArray();
            _headerLength = _stream.Position;
            unprotectedKey = (protector ?? new DpapiMeetingKeyProtector()).Unprotect(wrapped);
            if (unprotectedKey.Length != 32) throw new CryptographicException("The protected meeting key is invalid.");
            _key = unprotectedKey;
            _scanPosition = _headerLength;
            ScanFrames();
        }
        catch
        {
            if (unprotectedKey is not null) CryptographicOperations.ZeroMemory(unprotectedKey);
            _stream.Dispose();
            throw;
        }
    }

    private void ScanFrames()
    {
        _stream.Position = _scanPosition;
        ulong index = (ulong)_frames.Count;
        var lengthBytes = new byte[4];
        HasTruncatedTail = false;
        while (_stream.Position < _stream.Length)
        {
            var recordStart = _stream.Position;
            if (_stream.Length - recordStart < 4) { HasTruncatedTail = true; _scanPosition = recordStart; break; }
            _stream.ReadExactly(lengthBytes);
            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (length <= 0 || length > EncryptedAudioWriter.FrameSize)
                throw new InvalidDataException($"Encrypted audio frame {index} has an invalid length.");
            var recordLength = 4L + length + EncryptedAudioWriter.TagSize;
            if (_stream.Length - recordStart < recordLength) { HasTruncatedTail = true; _scanPosition = recordStart; break; }
            _frames.Add(new FrameInfo(recordStart, length, index));
            _pcmLength += length;
            _stream.Position = recordStart + recordLength;
            _scanPosition = _stream.Position;
            index++;
        }
        PcmLength = _pcmLength;
    }

    public void Refresh() => ScanFrames();

    public byte[] CopyMeetingKey() => _key.ToArray();

    public Stream OpenWavStream() => new DecryptingWavStream(this);

    public byte[] ReadPcmRange(long offset, int count)
    {
        if (offset < 0 || count < 0 || offset + count > PcmLength) throw new ArgumentOutOfRangeException(nameof(offset));
        var result = new byte[count];
        try
        {
            var dest = 0;
            var firstFrame = (int)(offset / EncryptedAudioWriter.FrameSize);
            long frameBase = (long)firstFrame * EncryptedAudioWriter.FrameSize;
            for (var framePosition = firstFrame; framePosition < _frames.Count; framePosition++)
            {
                var frame = _frames[framePosition];
                var end = frameBase + frame.Length;
                if (end > offset && frameBase < offset + count)
                {
                    var cipher = new byte[frame.Length];
                    var tag = new byte[EncryptedAudioWriter.TagSize];
                    _stream.Position = frame.RecordOffset + 4;
                    _stream.ReadExactly(cipher);
                    _stream.ReadExactly(tag);
                    var plain = new byte[frame.Length];
                    try
                    {
                        using (var aes = new AesGcm(_key, EncryptedAudioWriter.TagSize))
                            aes.Decrypt(EncryptedAudioWriter.MakeNonce(_channelId, frame.Index), cipher, tag, plain,
                                EncryptedAudioWriter.MakeAad(_header, frame.Index, frame.Length));
                        var from = (int)Math.Max(0, offset - frameBase);
                        var to = (int)Math.Min(frame.Length, offset + count - frameBase);
                        plain.AsSpan(from, to - from).CopyTo(result.AsSpan(dest));
                        dest += to - from;
                    }
                    finally { CryptographicOperations.ZeroMemory(plain); }
                }
                frameBase = end;
                if (dest == count) break;
            }
            if (dest != count) throw new EndOfStreamException("Requested audio range is incomplete.");
            return result;
        }
        catch { CryptographicOperations.ZeroMemory(result); throw; }
    }

    public Stream CreateWavStream(long offset, int count)
    {
        var pcm = ReadPcmRange(offset, count);
        var output = new MemoryStream(44 + pcm.Length);
        using (var writer = new WavStreamWriter(output, leaveOpen: true))
        {
            writer.Append(pcm);
            writer.Close();
        }
        CryptographicOperations.ZeroMemory(pcm);
        output.Position = 0;
        return output;
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
        _stream.Dispose();
    }

    private sealed record FrameInfo(long RecordOffset, int Length, ulong Index);

    private sealed class DecryptingWavStream(EncryptedAudioReader reader) : Stream
    {
        private readonly byte[] _wavHeader = BuildWavHeader(reader.PcmLength);
        private long _position;
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => 44 + reader.PcmLength;
        public override long Position { get => _position; set => Seek(value, SeekOrigin.Begin); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var available = (int)Math.Min(buffer.Length, Length - _position);
            if (available <= 0) return 0;
            var written = 0;
            if (_position < 44)
            {
                var headerCount = Math.Min(available, 44 - (int)_position);
                _wavHeader.AsSpan((int)_position, headerCount).CopyTo(buffer);
                _position += headerCount;
                written += headerCount;
            }
            if (written < available)
            {
                var pcmOffset = _position - 44;
                var pcm = reader.ReadPcmRange(pcmOffset, available - written);
                pcm.CopyTo(buffer[written..]);
                CryptographicOperations.ZeroMemory(pcm);
                _position += pcm.Length;
                written += pcm.Length;
            }
            return written;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var next = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _position + offset, SeekOrigin.End => Length + offset, _ => throw new ArgumentOutOfRangeException(nameof(origin)) };
            if (next < 0 || next > Length) throw new IOException("Seek is outside the encrypted audio stream.");
            return _position = next;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) reader.Dispose();
            base.Dispose(disposing);
        }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private static byte[] BuildWavHeader(long pcmLength)
        {
            var header = new byte[44];
            using var stream = new MemoryStream(header, writable: true);
            using (var writer = new WavStreamWriter(stream, leaveOpen: true)) writer.Close();
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), checked((uint)(36 + pcmLength)));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40, 4), checked((uint)pcmLength));
            return header;
        }
    }
}

internal static class MeetingAudioMigration
{
    public static void EnsureEncrypted(string meetingDirectory, IMeetingKeyProtector? protector = null)
    {
        var paths = new[] { Path.Combine(meetingDirectory, "mic.wav"), Path.Combine(meetingDirectory, "sys.wav") };
        var encrypted = paths.Where(EncryptedAudioFile.IsEncrypted).ToArray();
        var legacy = paths.Where(p => File.Exists(p) && !EncryptedAudioFile.IsEncrypted(p)).ToArray();
        if (legacy.Length == 0) return;
        byte[]? key = null;
        var channelIds = new HashSet<uint>();
        try
        {
            if (encrypted.Length > 0)
            {
                try
                {
                    using var reader = new EncryptedAudioReader(encrypted[0], protector);
                    key = reader.CopyMeetingKey();
                    channelIds.Add(reader.ChannelId);
                }
                catch (CryptographicException ex)
                {
                    throw KeyAccessFailure(encrypted[0], ex);
                }
            }
            foreach (var path in legacy)
            {
                key ??= RandomNumberGenerator.GetBytes(32);
                uint channelId;
                do { channelId = RandomChannelId(); } while (!channelIds.Add(channelId));
                try { MigrateFile(path, key, channelId, protector); }
                catch (Exception ex) when (ex is CryptographicException or IOException)
                {
                    throw new IOException($"Could not migrate {AudioIdentity(path)}. The original audio file was preserved. {ex.Message}", ex);
                }
            }
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
    }

    internal static CryptographicException KeyAccessFailure(string path, Exception inner) =>
        new($"Could not unlock {AudioIdentity(path)} with the current Windows user profile. Sign in to the original profile; an administrator password reset may make its DPAPI-protected key unavailable.", inner);

    internal static string AudioIdentity(string path) =>
        $"meeting {Path.GetFileName(Path.GetDirectoryName(path))}, {Path.GetFileNameWithoutExtension(path)} channel";

    private static void MigrateFile(string path, byte[] key, uint channelId, IMeetingKeyProtector? protector)
    {
        var temp = path + ".enc-migrate";
        try
        {
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ValidateWaveHeader(source);
                source.Position = 44;
                using (var writer = new EncryptedAudioWriter(temp, key, channelId, protector))
                {
                    var buffer = new byte[EncryptedAudioWriter.FrameSize * 4];
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0) writer.Append(buffer.AsMemory(0, read));
                }
            }

            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var encrypted = new EncryptedAudioReader(temp, protector))
            {
                source.Position = 44;
                if (encrypted.PcmLength != source.Length - 44) throw new InvalidDataException("Encrypted audio migration verification found a length mismatch.");
                var buffer = new byte[EncryptedAudioWriter.FrameSize * 2];
                long offset = 0;
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    var actual = encrypted.ReadPcmRange(offset, read);
                    if (!buffer.AsSpan(0, read).SequenceEqual(actual))
                        throw new InvalidDataException("Encrypted audio migration verification failed; the original audio was preserved.");
                    offset += read;
                }
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static void ValidateWaveHeader(Stream source)
    {
        if (source.Length < 44) throw new InvalidDataException("Legacy meeting audio has a truncated WAV header.");
        Span<byte> header = stackalloc byte[44];
        source.ReadExactly(header);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8) ||
            !header[12..16].SequenceEqual("fmt "u8) || !header[36..40].SequenceEqual("data"u8) ||
            BinaryPrimitives.ReadInt16LittleEndian(header[20..22]) != 1 ||
            BinaryPrimitives.ReadInt32LittleEndian(header[24..28]) != AudioConfig.SampleRate ||
            BinaryPrimitives.ReadInt16LittleEndian(header[22..24]) != AudioConfig.Channels ||
            BinaryPrimitives.ReadInt16LittleEndian(header[34..36]) != AudioConfig.BitsPerSample)
            throw new InvalidDataException("Legacy meeting audio is not the supported 16 kHz mono PCM WAV format.");
    }

    private static uint RandomChannelId()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }
}
