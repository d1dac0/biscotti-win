# Regenerates the transcription test fixture (issue #10):
# Biscotti.Tests/Fixtures/test-meeting-16k.wav — a short scripted sentence
# spoken by the Windows SAPI "David" voice at exactly the format the app
# records (16 kHz, mono, 16-bit PCM), so WhisperTranscriber can read the
# fixture directly without resampling. Run from the repo root:
#   powershell -ExecutionPolicy Bypass -File tools/make-fixture.ps1
Add-Type -AssemblyName System.Speech

$phrase = "Hello, this is a meeting transcription test."
$dest = Join-Path $PSScriptRoot "..\Biscotti.Tests\Fixtures\test-meeting-16k.wav"

New-Item -ItemType Directory -Force -Path (Split-Path $dest) | Out-Null
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$synth.SelectVoice("Microsoft David Desktop")
$synth.Rate = 0                         # slowest natural clarity
$synth.Volume = 100
$synth.SetOutputToWaveFile($dest, (New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)))
$synth.Speak($phrase)
$synth.Dispose()

Get-Item $dest | Select-Object FullName, Length
