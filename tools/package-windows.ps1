param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9A-Za-z.-]+$')]
    [string]$Version,

    [switch]$Sign
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
$publishDirectory = Join-Path $output 'publish'
$payloadDirectory = Join-Path $output 'payload'
$packageDirectory = Join-Path $payloadDirectory 'BiscottiWin'
$zipPath = Join-Path $output "BiscottiWin-$Version-win-x64.zip"

if (Test-Path -LiteralPath $output) {
    throw "Output directory already exists: $output"
}
New-Item -ItemType Directory -Path $output | Out-Null

$project = Join-Path $repoRoot 'Biscotti.Win\Biscotti.Win.csproj'
& dotnet publish $project --configuration Release --runtime win-x64 --self-contained true `
    --output $publishDirectory "-p:Version=$Version" "-p:WindowsAppSDKSelfContained=true"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$executable = Join-Path $publishDirectory 'Biscotti.Win.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Published executable was not found: $executable"
}

if ($Sign) {
    if ([string]::IsNullOrWhiteSpace($env:WINDOWS_SIGNING_PFX_BASE64) -or
        [string]::IsNullOrWhiteSpace($env:WINDOWS_SIGNING_PFX_PASSWORD)) {
        throw 'Before creating a signed release, configure WINDOWS_SIGNING_PFX_BASE64 and WINDOWS_SIGNING_PFX_PASSWORD in the windows-release GitHub Environment.'
    }

    $windowsKits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $signTool = Get-ChildItem -LiteralPath $windowsKits -Recurse -Filter signtool.exe -ErrorAction Stop |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object { [version]$_.Directory.Parent.Name } -Descending |
        Select-Object -First 1
    if (-not $signTool) { throw "SignTool was not found below $windowsKits" }

    $certificatePath = Join-Path $env:RUNNER_TEMP 'biscotti-release-signing.pfx'
    try {
        [IO.File]::WriteAllBytes($certificatePath, [Convert]::FromBase64String($env:WINDOWS_SIGNING_PFX_BASE64))
        & $signTool.FullName sign /fd SHA256 /tr https://timestamp.digicert.com /td SHA256 `
            /f $certificatePath /p $env:WINDOWS_SIGNING_PFX_PASSWORD $executable
        if ($LASTEXITCODE -ne 0) { throw "SignTool signing failed with exit code $LASTEXITCODE" }
        & $signTool.FullName verify /pa /all $executable
        if ($LASTEXITCODE -ne 0) { throw "SignTool verification failed with exit code $LASTEXITCODE" }
        $signature = Get-AuthenticodeSignature -FilePath $executable
        if ($signature.Status -ne 'Valid') { throw "The app signature is not valid: $($signature.StatusMessage)" }
    }
    finally {
        Remove-Item -LiteralPath $certificatePath -Force -ErrorAction SilentlyContinue
    }
}

New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $packageDirectory -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'README.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\windows-release.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\THIRD-PARTY-NOTICES.md') -Destination $packageDirectory
Set-Content -LiteralPath (Join-Path $packageDirectory 'VERSION.txt') -Value $Version -Encoding ascii

$licenseDirectory = Join-Path $packageDirectory 'LICENSES'
$nugetLicenseDirectory = Join-Path $licenseDirectory 'NuGet'
New-Item -ItemType Directory -Path $nugetLicenseDirectory -Force | Out-Null
$dependencyLines = [Collections.Generic.List[string]]::new()
$dependencyLines.Add('# Resolved Windows release dependencies')
$dependencyLines.Add('')
$dependencyLines.Add('Package versions and declared licenses were read from the published application dependency manifest and NuGet package metadata.')
$dependencyLines.Add('')
$dependencyLines.Add('| Package | Version | Declared license | License files |')
$dependencyLines.Add('| --- | --- | --- | --- |')

$dependencyManifestPath = Join-Path $packageDirectory 'Biscotti.Win.deps.json'
$dependencyManifest = Get-Content -LiteralPath $dependencyManifestPath -Raw | ConvertFrom-Json -AsHashtable
$nugetRoot = Join-Path $env:USERPROFILE '.nuget\packages'
foreach ($entry in $dependencyManifest.libraries.GetEnumerator() | Sort-Object Key) {
    if ($entry.Value.type -ne 'package') { continue }
    $packageParts = $entry.Key -split '/', 2
    if ($packageParts.Count -ne 2) { continue }
    $packageName, $packageVersion = $packageParts
    $nugetPackageDirectory = Join-Path (Join-Path $nugetRoot $packageName.ToLowerInvariant()) $packageVersion
    $nuspecPath = Join-Path $nugetPackageDirectory "$($packageName.ToLowerInvariant()).nuspec"
    $licenseName = 'See included notices or package page'
    $licenseFiles = [Collections.Generic.List[string]]::new()
    if (Test-Path -LiteralPath $nuspecPath) {
        [xml]$nuspec = Get-Content -LiteralPath $nuspecPath -Raw
        $metadata = $nuspec.package.metadata
        $licenseElement = $metadata.license
        if ($licenseElement.type -eq 'expression') {
            $licenseName = $licenseElement.'#text'
        }
        elseif ($licenseElement.type -eq 'file') {
            $licenseName = "Package file: $($licenseElement.'#text')"
            $declaredFile = Join-Path $nugetPackageDirectory $licenseElement.'#text'
            if (Test-Path -LiteralPath $declaredFile) { $licenseFiles.Add($declaredFile) }
        }
        elseif ($metadata.licenseUrl) {
            $licenseName = "[Package terms]($($metadata.licenseUrl))"
        }

        Get-ChildItem -LiteralPath $nugetPackageDirectory -File |
            Where-Object { $_.Name -match '^(LICENSE|NOTICE)(\.|$)' } |
            ForEach-Object { if (-not $licenseFiles.Contains($_.FullName)) { $licenseFiles.Add($_.FullName) } }
    }

    $copiedFiles = [Collections.Generic.List[string]]::new()
    foreach ($licenseFile in $licenseFiles | Select-Object -Unique) {
        $destinationName = "$packageName-$packageVersion-$([IO.Path]::GetFileName($licenseFile))"
        Copy-Item -LiteralPath $licenseFile -Destination (Join-Path $nugetLicenseDirectory $destinationName) -Force
        $copiedFiles.Add("`"LICENSES/NuGet/$destinationName`"")
    }
    $packageLink = "https://www.nuget.org/packages/$packageName/$packageVersion"
    $licenseCell = if ($licenseName.StartsWith('[')) { $licenseName } else { $licenseName }
    $filesCell = if ($copiedFiles.Count -eq 0) { '—' } else { $copiedFiles -join ', ' }
    $dependencyLines.Add("| [$packageName]($packageLink) | $packageVersion | $licenseCell | $filesCell |")
}

$dependencyLines | Set-Content -LiteralPath (Join-Path $licenseDirectory 'DEPENDENCIES.md') -Encoding utf8
$runtimeConfigPath = Join-Path $packageDirectory 'Biscotti.Win.runtimeconfig.json'
$runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
$runtimeVersion = $runtimeConfig.runtimeOptions.includedFrameworks |
    Where-Object { $_.name -eq 'Microsoft.NETCore.App' } |
    Select-Object -ExpandProperty version
if ($runtimeVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Could not determine the packaged .NET runtime version from $runtimeConfigPath"
}

# Self-contained publish output does not include the runtime distribution's
# license notices. Fetch the matching source-tagged notices so their versions
# track the runtime included in the ZIP.
$dotnetLicenseBase = "https://raw.githubusercontent.com/dotnet/runtime/v$runtimeVersion"
$wpfLicenseBase = "https://raw.githubusercontent.com/dotnet/wpf/v$runtimeVersion"
Invoke-WebRequest -Uri "$dotnetLicenseBase/LICENSE.TXT" `
    -OutFile (Join-Path $licenseDirectory 'dotnet-runtime-LICENSE.TXT') -ErrorAction Stop
Invoke-WebRequest -Uri "$dotnetLicenseBase/src/installer/pkg/THIRD-PARTY-NOTICES.TXT" `
    -OutFile (Join-Path $licenseDirectory "dotnet-runtime-v$runtimeVersion-THIRD-PARTY-NOTICES.TXT") -ErrorAction Stop
Invoke-WebRequest -Uri "$wpfLicenseBase/LICENSE.TXT" `
    -OutFile (Join-Path $licenseDirectory 'dotnet-wpf-LICENSE.TXT') -ErrorAction Stop
Invoke-WebRequest -Uri "$wpfLicenseBase/THIRD-PARTY-NOTICES.TXT" `
    -OutFile (Join-Path $licenseDirectory "dotnet-wpf-v$runtimeVersion-THIRD-PARTY-NOTICES.TXT") -ErrorAction Stop
Invoke-WebRequest -Uri 'https://dotnet.microsoft.com/dotnet_library_license.htm' `
    -OutFile (Join-Path $licenseDirectory 'dotnet-windows-library-license.htm') -ErrorAction Stop

Compress-Archive -Path (Join-Path $payloadDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Set-Content -LiteralPath "$zipPath.sha256" -Value "$hash  $([IO.Path]::GetFileName($zipPath))" -Encoding ascii
Write-Output "Package: $zipPath"
Write-Output "SHA-256: $hash"
