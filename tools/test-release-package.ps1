param(
    [Parameter(Mandatory = $true)]
    [string]$PackagePath
)

$ErrorActionPreference = 'Stop'
$zipPath = [IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $zipPath)) { throw "Release package was not found: $zipPath" }

$testId = [guid]::NewGuid().ToString('N')
$temporaryRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { $env:TEMP } else { $env:RUNNER_TEMP }
$runnerTemp = [IO.Path]::GetFullPath($temporaryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$testRoot = Join-Path $runnerTemp "BiscottiReleaseSmoke-$testId"
$programsDirectory = Join-Path $testRoot 'Programs'
$installDirectory = Join-Path $programsDirectory 'BiscottiWin'
$dataDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'BiscottiWin'
$dataMarker = Join-Path $dataDirectory "release-retention-$testId.txt"
$application = Join-Path $installDirectory 'Biscotti.Win.exe'
$process = $null

function Start-PackagedApp {
    $running = Start-Process -FilePath $script:application -PassThru
    Start-Sleep -Seconds 8
    $running.Refresh()
    if ($running.HasExited) {
        throw "Biscotti.Win.exe exited during launch with code $($running.ExitCode)."
    }
    return $running
}

function Stop-PackagedApp {
    param([Diagnostics.Process]$Running)
    if ($Running -and -not $Running.HasExited) {
        Stop-Process -Id $Running.Id -Force
        $Running.WaitForExit(10000) | Out-Null
    }
}

try {
    New-Item -ItemType Directory -Path $programsDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
    Set-Content -LiteralPath $dataMarker -Value 'preserve across app updates and uninstall' -Encoding ascii

    # Install: extract to the per-user application directory.
    Expand-Archive -LiteralPath $zipPath -DestinationPath $programsDirectory -Force
    if (-not (Test-Path -LiteralPath $application)) { throw 'Install did not produce Biscotti.Win.exe.' }
    $installedRuntimeConfig = Get-Content -LiteralPath (Join-Path $installDirectory 'Biscotti.Win.runtimeconfig.json') -Raw | ConvertFrom-Json
    $installedRuntimeVersion = $installedRuntimeConfig.runtimeOptions.includedFrameworks |
        Where-Object { $_.name -eq 'Microsoft.NETCore.App' } |
        Select-Object -ExpandProperty version
    $requiredPackageFiles = @(
        'THIRD-PARTY-NOTICES.md',
        'LICENSES\DEPENDENCIES.md',
        'LICENSES\dotnet-windows-library-license.htm',
        "LICENSES\dotnet-runtime-v$installedRuntimeVersion-THIRD-PARTY-NOTICES.TXT",
        "LICENSES\dotnet-wpf-v$installedRuntimeVersion-THIRD-PARTY-NOTICES.TXT"
    )
    foreach ($requiredFile in $requiredPackageFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $installDirectory $requiredFile))) {
            throw "The release package is missing $requiredFile."
        }
    }
    $process = Start-PackagedApp
    Stop-PackagedApp $process
    $process = $null

    # Upgrade: overlay the next release archive with the app closed.
    Set-Content -LiteralPath (Join-Path $installDirectory 'VERSION.txt') -Value 'previous-version' -Encoding ascii
    Expand-Archive -LiteralPath $zipPath -DestinationPath $programsDirectory -Force
    if ((Get-Content -LiteralPath (Join-Path $installDirectory 'VERSION.txt') -Raw).Trim() -eq 'previous-version') {
        throw 'Upgrade did not replace the installed version metadata.'
    }
    if (-not (Test-Path -LiteralPath $dataMarker)) { throw 'Upgrade removed user data.' }
    $process = Start-PackagedApp
    Stop-PackagedApp $process
    $process = $null

    # Uninstall: delete only the program folder; preserve the app data root.
    $resolvedInstall = [IO.Path]::GetFullPath($installDirectory)
    $safeRoot = $testRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedInstall.StartsWith($safeRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove an install path outside the smoke-test root: $resolvedInstall"
    }
    Remove-Item -LiteralPath $resolvedInstall -Recurse -Force
    if (Test-Path -LiteralPath $resolvedInstall) { throw 'Uninstall left application files behind.' }
    if (-not (Test-Path -LiteralPath $dataMarker)) { throw 'Uninstall removed user data.' }

    Write-Output 'Install, launch, overlay upgrade, uninstall, and user-data-retention checks passed.'
}
finally {
    Stop-PackagedApp $process
    Remove-Item -LiteralPath $dataMarker -Force -ErrorAction SilentlyContinue
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTestRoot.StartsWith($runnerTemp, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
