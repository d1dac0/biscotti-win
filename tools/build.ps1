# The one build command. Restores if needed, then builds Release.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet restore "$root\BiscottiWin.slnx"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build -c Release --no-restore --nologo "$root\BiscottiWin.slnx"
exit $LASTEXITCODE
