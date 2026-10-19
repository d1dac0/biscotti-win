# One-time repo setup: create the workflow labels from .github/labels.json.
# Idempotent: creates missing labels, updates existing ones.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$labels = Get-Content "$root\.github\labels.json" -Raw | ConvertFrom-Json
foreach ($l in $labels) {
    gh label create $l.name --color $l.color --description $l.description 2>$null
    if ($LASTEXITCODE -ne 0) {
        gh label edit $l.name --color $l.color --description $l.description
    }
}
Write-Output "Labels synced: $($labels.Count) processed."
