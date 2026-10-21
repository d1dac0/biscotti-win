using System.Security.Cryptography;
using System.Text.Json;
using Biscotti.Core.Transcription;

namespace Biscotti.Core.Storage;

/// <summary>One protected store. All operations reload under a process-wide lock to avoid stale-window writes.</summary>
public sealed class SpeakerMemoryStore
{
    private static readonly object Gate = new();
    private readonly string _path;
    public SpeakerMemoryStore(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BiscottiWin", "speaker-memory.dat");

    public sealed class State
    {
        public string ModelVersion { get; set; } = SpeakerMemory.ModelVersion;
        public bool Enabled { get; set; }
        public List<RememberedVoice> Voices { get; set; } = [];
    }

    public State Load() { lock (Gate) return Read(); }

    private State Read()
    {
        byte[] envelope;
        try { envelope = File.ReadAllBytes(_path); }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        // This store has never had a plaintext format. Do not migrate unknown bytes.
        if (!envelope.AsSpan().StartsWith("BISCOTTI-DPAPI:1\n"u8))
            throw new InvalidDataException("Speaker memory has an unsupported format; the file was preserved.");
        var bytes = ProtectedMeetingText.ReadAndMigrate(_path);
        try
        {
            var state = JsonSerializer.Deserialize<State>(bytes) ?? throw new InvalidDataException("Speaker memory is empty.");
            if (state.ModelVersion != SpeakerMemory.ModelVersion || state.Voices is null ||
                state.Voices.Any(voice => voice is null || voice.PersonId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(voice.Name) || voice.Samples is null || voice.Samples.Length < 2 ||
                    voice.Samples.Length > 5 || voice.Samples.Any(sample => !SpeakerMemory.IsUsable(sample))))
                throw new InvalidDataException("Speaker memory is incompatible or damaged; the file was preserved.");
            return state;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public void SetEnabled(bool enabled)
    {
        lock (Gate)
        {
            if (!enabled) { File.Delete(_path); return; }
            var state = Read();
            state.Enabled = true;
            Save(state);
        }
    }

    public Guid Remember(Guid meetingId, string speakerKey, string name, IEnumerable<float[]?> samples,
        Guid? personId = null)
    {
        name = SpeakerMemory.ValidateName(name);
        if (!speakerKey.StartsWith("system:", StringComparison.Ordinal))
            throw new ArgumentException("Only distinct system-audio speaker groups can be remembered.");
        var prepared = SpeakerMemory.PrepareSamples(samples);
        if (prepared.Length < 2 || prepared.Select(sample => sample.Length).Distinct().Count() != 1)
            throw new ArgumentException("At least two clear speech segments are required to remember a voice.");
        lock (Gate)
        {
            var state = Read();
            if (!state.Enabled) throw new InvalidOperationException("Enable local speaker memory first.");
            if (personId is not null && !state.Voices.Any(voice => voice.PersonId == personId))
                throw new ArgumentException("That remembered person no longer exists. Refresh the people list.");
            var id = personId ?? Guid.NewGuid();
            // The name of an existing person comes from the store, never a stale window.
            if (personId is not null) name = state.Voices.First(voice => voice.PersonId == id).Name;
            state.Voices.RemoveAll(voice => voice.MeetingId == meetingId && voice.SpeakerKey == speakerKey);
            state.Voices.Add(new(id, name, meetingId, speakerKey, prepared));
            Save(state);
            return id;
        }
    }

    public void Forget(Guid personId)
    {
        lock (Gate)
        {
            var state = Read();
            state.Voices.RemoveAll(voice => voice.PersonId == personId);
            Save(state);
        }
    }

    private void Save(State state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state);
        try { ProtectedMeetingText.WriteProtectedAtomically(_path, bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
