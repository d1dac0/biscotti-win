using System.Buffers.Binary;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Biscotti.Core.Models;
using Biscotti.Win;
using NAudio.Wave;
using Xunit;

namespace Biscotti.Win.Tests;

internal static class AudioTransportChecks
{
    internal static void Run()
    {
        const long wavLength = 44 + 64_000;
        Assert.Equal(44, MeetingWindow.PositionForSeconds(-3, wavLength));
        Assert.Equal(44, MeetingWindow.PositionForSeconds(double.NaN, wavLength));
        Assert.Equal(44 + 40_000, MeetingWindow.PositionForSeconds(1.25, wavLength));
        Assert.Equal(wavLength, MeetingWindow.PositionForSeconds(900, wavLength));

        var paths = new List<string>();
        var streams = new List<TrackingMemoryStream>();
        var outputs = new List<FakeWavePlayer>();
        TrackingMemoryStream Open(string path)
        {
            paths.Add(Path.GetFileName(path));
            var stream = new TrackingMemoryStream(CreateWav(64_000));
            streams.Add(stream);
            return stream;
        }
        FakeWavePlayer CreateOutput()
        {
            var output = new FakeWavePlayer();
            outputs.Add(output);
            return output;
        }

        var window = new MeetingWindow(DesignPreview.CreateMeeting(),
            audioStreamFactory: Open, audioOutputFactory: CreateOutput);
        var transport = (Border)window.FindName("AudioTransportPanel");
        var tabs = (TabControl)window.FindName("ReviewTabs");
        Assert.Equal(Visibility.Visible, transport.Visibility);
        tabs.SelectedItem = window.FindName("NotesTab");
        Assert.Equal(Visibility.Visible, transport.Visibility);
        tabs.SelectedItem = window.FindName("TranscriptTab");
        Assert.Equal(Visibility.Visible, transport.Visibility);
        Assert.Equal(5, ((Slider)window.FindName("AudioPositionSlider")).SmallChange);
        Assert.Equal(30, ((Slider)window.FindName("AudioPositionSlider")).LargeChange);
        ((Button)window.FindName("AudioPlayPauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { "mic.wav" }, paths);
        Assert.Equal(PlaybackState.Playing, outputs[0].PlaybackState);

        ((ComboBox)window.FindName("AudioChannelSelector")).SelectedIndex = 1;
        Assert.Equal(new[] { "mic.wav", "sys.wav" }, paths);
        Assert.Equal(PlaybackState.Stopped, outputs[0].PlaybackState);
        Assert.True(outputs[0].Disposed);
        Assert.Equal(PlaybackState.Playing, outputs[1].PlaybackState);
        Assert.True(streams[0].Disposed);

        window.NavigateToTimestamp(1.25);
        Assert.Equal(44 + 40_000, ((WaveFileReader)outputs[1].Provider!).Position);
        ((Slider)window.FindName("AudioPositionSlider")).Value = 0.5;
        Assert.Equal(44 + 16_000, ((WaveFileReader)outputs[1].Provider!).Position);
        window.NavigateToTimestamp(1.25);
        ((Button)window.FindName("AudioPlayPauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(PlaybackState.Paused, outputs[1].PlaybackState);
        ((Button)window.FindName("AudioPlayPauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(PlaybackState.Playing, outputs[1].PlaybackState);

        var deleted = false;
        Assert.True(window.TryDeleteMeeting(() => deleted = true));
        Assert.True(deleted);
        Assert.True(outputs[1].Disposed);
        Assert.True(streams[1].Disposed);
        window.Close();

        var failingOutput = new FakeWavePlayer { ThrowOnStop = true };
        TrackingMemoryStream? failingStream = null;
        var guardedWindow = new MeetingWindow(DesignPreview.CreateMeeting(),
            audioStreamFactory: _ => failingStream = new TrackingMemoryStream(CreateWav(64_000)),
            audioOutputFactory: () => failingOutput);
        ((Button)guardedWindow.FindName("AudioPlayPauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var deletionAttempted = false;
        Assert.False(guardedWindow.TryDeleteMeeting(() => deletionAttempted = true));
        Assert.False(deletionAttempted);
        Assert.True(failingOutput.Disposed);
        Assert.True(failingStream!.Disposed);
        guardedWindow.Close();

        var disposeOutput = new FakeWavePlayer();
        TrackingMemoryStream? disposeStream = null;
        var disposalWindow = new MeetingWindow(DesignPreview.CreateMeeting(),
            audioStreamFactory: _ => disposeStream = new TrackingMemoryStream(CreateWav(64_000)),
            audioOutputFactory: () => disposeOutput);
        ((Button)disposalWindow.FindName("AudioPlayPauseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        disposalWindow.Close();
        Assert.True(disposeOutput.Disposed);
        Assert.True(disposeStream!.Disposed);
    }

    private static byte[] CreateWav(int pcmBytes)
    {
        var wav = new byte[44 + pcmBytes];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + pcmBytes);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), 16_000);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), 32_000);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), pcmBytes);
        return wav;
    }

    private sealed class TrackingMemoryStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class FakeWavePlayer : IWavePlayer
    {
        public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;
        public float Volume { get; set; } = 1;
        public WaveFormat OutputWaveFormat { get; private set; } = new WaveFormat(16_000, 16, 1);
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;
        public bool Disposed { get; private set; }
        public bool ThrowOnStop { get; init; }
        public IWaveProvider? Provider { get; private set; }
        public void Init(IWaveProvider waveProvider)
        {
            Provider = waveProvider;
            OutputWaveFormat = waveProvider.WaveFormat;
        }
        public void Play() => PlaybackState = PlaybackState.Playing;
        public void Pause() => PlaybackState = PlaybackState.Paused;
        public void Stop()
        {
            if (ThrowOnStop) throw new IOException("Synthetic output stop failure.");
            PlaybackState = PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs());
        }
        public void Dispose()
        {
            Disposed = true;
            PlaybackState = PlaybackState.Stopped;
        }
    }
}
