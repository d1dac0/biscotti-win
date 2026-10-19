param([string]$Repository = 'd1dac0/biscotti-win')
$ErrorActionPreference = 'Stop'
$repositoryLabels = Get-Content (Join-Path $PSScriptRoot 'labels.json') -Raw | ConvertFrom-Json
foreach ($repositoryLabel in $repositoryLabels) {
  & gh label create $repositoryLabel.name --repo $Repository --color $repositoryLabel.color --description $repositoryLabel.description --force
  if ($LASTEXITCODE -ne 0) { throw "Could not synchronize label $($repositoryLabel.name)" }
}
Write-Output "Synchronized $($repositoryLabels.Count) labels without deleting other labels."
