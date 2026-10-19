# The one test/regression check: restore + build + full test suite.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet restore "$root\BiscottiWin.slnx"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build -c Release --no-restore --nologo "$root\BiscottiWin.slnx"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test -c Release --no-build --nologo "$root\Biscotti.Tests\Biscotti.Tests.csproj"
exit $LASTEXITCODE
