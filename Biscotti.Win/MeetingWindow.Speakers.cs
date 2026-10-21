using System.IO;
using System.Windows;
using System.Windows.Controls;
using Biscotti.Core.Audio;
using Biscotti.Core.Storage;
using Biscotti.Core.Transcription;

namespace Biscotti.Win;

public partial class MeetingWindow
{
    private readonly SpeakerMemoryStore _speakerMemory = new();
    private SpeakerMemoryStore.State _speakerMemoryState = new();
    private float[][] _voiceSamples = [];
    private SpeakerSuggestion? _suggestion;
    private bool _analyzingVoice;
    private sealed record PersonChoice(Guid? Id, string Name);

    private void ReviewTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, ReviewTabs) || ReviewTabs.SelectedItem != SpeakersTab || _meeting is null) return;
        if (_analyzingVoice) return;
        RefreshSpeakerMemory();
        SpeakerGroupBox.ItemsSource = SpeakerMemory.Groups(_meeting);
        SpeakerGroupBox.SelectedIndex = SpeakerGroupBox.Items.Count == 0 ? -1 : 0;
    }

    private void RefreshSpeakerMemory()
    {
        try
        {
            if (!_designPreview) _speakerMemoryState = _speakerMemory.Load();
            SpeakerMemoryEnabled.IsChecked = _speakerMemoryState.Enabled;
            var choices = new List<PersonChoice> { new(null, "New person — use corrected label above") };
            choices.AddRange(_speakerMemoryState.Voices.GroupBy(voice => voice.PersonId)
                .Select(group => new PersonChoice(group.Key, group.First().Name)).OrderBy(person => person.Name));
            RememberedPersonBox.ItemsSource = choices;
            RememberedPersonBox.SelectedIndex = 0;
            SpeakerStatus.Text = _speakerMemoryState.Enabled
                ? $"Local memory enabled · {choices.Count - 1} remembered people."
                : "Local memory is off. Correcting transcript labels does not enroll a voice.";
        }
        catch (Exception ex)
        {
            _speakerMemoryState = new();
            SpeakerMemoryEnabled.IsChecked = false;
            RememberedPersonBox.ItemsSource = null;
            SpeakerStatus.Text = "Could not read speaker memory; it was preserved. " + ex.Message;
        }
        UpdateSpeakerControls();
    }

    private void SpeakerGroup_Changed(object sender, SelectionChangedEventArgs e)
    {
        _voiceSamples = [];
        _suggestion = null;
        SpeakerNameBox.Text = (SpeakerGroupBox.SelectedItem as SpeakerGroup)?.Label ?? "";
        SpeakerSuggestionText.Text = "Analyze saved speech to find a suggestion or explicitly remember this voice.";
        UpdateSpeakerControls();
    }

    private void UpdateSpeakerControls()
    {
        var editable = !_isLiveRecording && !_isProcessing && !_analyzingVoice;
        SpeakersTab.IsEnabled = !_isLiveRecording && !_isProcessing;
        SpeakerGroupBox.IsEnabled = editable;
        SpeakerNameBox.IsEnabled = editable;
        CorrectSpeakerButton.IsEnabled = editable && SpeakerGroupBox.SelectedItem is not null;
        AnalyzeVoiceButton.IsEnabled = editable && _speakerMemoryState.Enabled &&
            SpeakerGroupBox.SelectedItem is SpeakerGroup { CanRemember: true };
        RememberVoiceButton.IsEnabled = editable && _speakerMemoryState.Enabled && _voiceSamples.Length >= 2;
        UseSuggestionButton.IsEnabled = editable && _suggestion is not null;
        SpeakerMemoryEnabled.IsEnabled = editable;
        RememberedPersonBox.IsEnabled = editable && _speakerMemoryState.Enabled;
        ForgetPersonButton.IsEnabled = editable && _speakerMemoryState.Enabled;
        ForgetAllButton.IsEnabled = editable;
    }

    private void CorrectSpeaker_Click(object sender, RoutedEventArgs e)
    {
        if (SpeakerGroupBox.SelectedItem is not SpeakerGroup group) return;
        try
        {
            var old = _meeting.Transcript.Select(line => (Line: line, line.Speaker, line.SpeakerKey)).ToList();
            try
            {
                SpeakerMemory.Correct(_meeting, group.Key, SpeakerNameBox.Text);
                if (!_designPreview) MeetingStore.Save(_meeting);
            }
            catch
            {
                foreach (var item in old) { item.Line.Speaker = item.Speaker; item.Line.SpeakerKey = item.SpeakerKey; }
                throw;
            }
            var samples = _voiceSamples;
            RefreshTranscript();
            SpeakerGroupBox.ItemsSource = SpeakerMemory.Groups(_meeting);
            SpeakerGroupBox.SelectedItem = ((List<SpeakerGroup>)SpeakerGroupBox.ItemsSource).First(item => item.Key == group.Key);
            _voiceSamples = samples;
            SpeakerStatus.Text = "Transcript label saved. Summary text was kept as written.";
            UpdateSpeakerControls();
        }
        catch (Exception ex) { SpeakerStatus.Text = "Could not save the speaker label. " + ex.Message; }
    }

    private async void AnalyzeVoice_Click(object sender, RoutedEventArgs e) => await AnalyzeSelectedVoiceAsync();

    internal async Task AnalyzeSelectedVoiceAsync()
    {
        if (SpeakerGroupBox.SelectedItem is not SpeakerGroup { CanRemember: true } group || _analyzingVoice) return;
        _analyzingVoice = true;
        _voiceSamples = [];
        _suggestion = null;
        UpdateSpeakerControls();
        SpeakerStatus.Text = "Analyzing saved system speech locally on CPU…";
        try
        {
            _voiceSamples = await Task.Run(() =>
            {
                if (_designPreview) return SpeakerMemory.PrepareSamples(new float[][] { [1, 0, 0], [0.999f, 0.01f, 0] });
                if (!SpeakerEmbeddingModelStore.IsInstalled())
                    throw new InvalidOperationException("The speaker model is not installed. Process a meeting with system speech first.");
                var lines = _meeting.Transcript.Where(line => SpeakerMemory.GroupKey(line) == group.Key &&
                    line.EndSeconds - line.StartSeconds >= 2).Take(5).ToList();
                if (lines.Count < 2) return Array.Empty<float[]>();
                using var wav = EncryptedAudioPlayback.OpenWavStream(Path.Combine(MeetingStore.MeetingDir(_meeting.Id), "sys.wav"));
                return SpeakerMemory.PrepareSamples(SpeakerDiarizer.ExtractEmbeddings(wav, lines, SpeakerEmbeddingModelStore.ModelPath));
            });
            // Re-read in case another meeting window forgot a person during analysis.
            RefreshSpeakerMemory();
            if (!_speakerMemoryState.Enabled) { _voiceSamples = []; return; }
            _suggestion = SpeakerMemory.Suggest(_meeting.Id, _voiceSamples, _speakerMemoryState.Voices);
            SpeakerSuggestionText.Text = _suggestion is null
                ? "No confident suggestion. The transcript label stays unchanged."
                : $"Suggested person: {_suggestion.Name}. Confirm by choosing Use suggested label; this can be wrong.";
            SpeakerStatus.Text = _voiceSamples.Length < 2 ? "Not enough clear speech to remember a voice. At least two segments are required."
                : "Analysis complete. Remember voice saves these samples only when you choose it.";
        }
        catch (Exception ex) { SpeakerStatus.Text = "Voice analysis unavailable. " + ex.Message; }
        finally { _analyzingVoice = false; UpdateSpeakerControls(); }
    }

    private void UseSuggestion_Click(object sender, RoutedEventArgs e)
    {
        if (_suggestion is null) return;
        try
        {
            var current = _designPreview ? _speakerMemoryState : _speakerMemory.Load();
            if (!current.Enabled || !current.Voices.Any(voice => voice.PersonId == _suggestion.PersonId))
                throw new InvalidOperationException("That person was forgotten. Analyze the voice again.");
            SpeakerNameBox.Text = _suggestion.Name;
            CorrectSpeaker_Click(sender, e);
        }
        catch (Exception ex) { SpeakerStatus.Text = ex.Message; }
    }

    private void SpeakerMemoryEnabled_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var enabled = SpeakerMemoryEnabled.IsChecked == true;
            if (_designPreview) _speakerMemoryState = new() { Enabled = enabled };
            else _speakerMemory.SetEnabled(enabled);
            _voiceSamples = [];
            _suggestion = null;
            RefreshSpeakerMemory();
            SpeakerSuggestionText.Text = "Analyze a voice to find a suggestion. No voice is enrolled automatically.";
        }
        catch (Exception ex) { SpeakerMemoryEnabled.IsChecked = _speakerMemoryState.Enabled; SpeakerStatus.Text = ex.Message; }
    }

    private void RememberVoice_Click(object sender, RoutedEventArgs e)
    {
        if (SpeakerGroupBox.SelectedItem is not SpeakerGroup group) return;
        try
        {
            var person = RememberedPersonBox.SelectedItem as PersonChoice;
            if (_designPreview) SpeakerStatus.Text = "Preview only. No voice was saved.";
            else
            {
                _speakerMemory.Remember(_meeting.Id, group.Key, SpeakerNameBox.Text, _voiceSamples, person?.Id);
                RefreshSpeakerMemory();
                SpeakerStatus.Text = "Voice remembered locally. Choose Save label separately to correct the transcript.";
            }
        }
        catch (Exception ex) { SpeakerStatus.Text = "Could not remember voice. " + ex.Message; }
    }

    private void ForgetPerson_Click(object sender, RoutedEventArgs e)
    {
        if (RememberedPersonBox.SelectedItem is not PersonChoice { Id: not null } person)
        { SpeakerStatus.Text = "Select a remembered person to forget."; return; }
        try
        {
            if (!_designPreview) _speakerMemory.Forget(person.Id.Value);
            _suggestion = null;
            _voiceSamples = [];
            RefreshSpeakerMemory();
            SpeakerSuggestionText.Text = "Person forgotten. Past transcript labels are preserved.";
        }
        catch (Exception ex) { SpeakerStatus.Text = "Could not forget person. " + ex.Message; }
    }

    private void ForgetAll_Click(object sender, RoutedEventArgs e)
    {
        SpeakerMemoryEnabled.IsChecked = false;
        SpeakerMemoryEnabled_Click(sender, e);
    }
}
