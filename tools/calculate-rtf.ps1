param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ $_ -gt 0 })]
    [double]$AudioSeconds,

    [Parameter(Mandatory = $true)]
    [ValidateScript({ $_ -gt 0 })]
    [double]$WallSeconds,

    [ValidateScript({ $_ -gt 0 })]
    [double]$ExampleAudioMinutes = 60
)

$rtf = $AudioSeconds / $WallSeconds
$estimatedWallSeconds = $ExampleAudioMinutes * 60 / $rtf
$duration = [TimeSpan]::FromSeconds([math]::Round($estimatedWallSeconds))
$rtfText = $rtf.ToString('F2', [Globalization.CultureInfo]::InvariantCulture)

"RTF: {0}x (audio seconds / wall-clock seconds)" -f $rtfText
"Estimated transcription time for {0:F0} minutes of audio: {1:N0} minutes {2:N0} seconds" -f `
    $ExampleAudioMinutes, [math]::Floor($duration.TotalMinutes), $duration.Seconds
