[CmdletBinding()]
param(
    [ValidateSet('private', 'public')][string]$Visibility = 'private',
    [string]$Owner = 'Anheledir'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repo = "$Owner/SidePulse.NET"
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Install and authenticate GitHub CLI first.' }
& gh auth status
if ($LASTEXITCODE -ne 0) { throw 'Authenticate GitHub CLI before publishing.' }
Push-Location $root
try {
    $gitRoot = & git rev-parse --show-toplevel
    if ($LASTEXITCODE -ne 0 -or (Resolve-Path $gitRoot).Path -ne $root) {
        throw 'Run this from the prepared Git checkout, not from an extracted folder inside another repository.'
    }
    & git rev-parse --verify HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Create the prepared initial commit first.' }
    $dirty = & git status --porcelain
    if ($dirty) { throw 'Commit or review local changes before publishing.' }
    & gh repo view $repo --json nameWithOwner 2>$null
    if ($LASTEXITCODE -eq 0) { throw "Repository $repo already exists. It has not been changed." }
    $visibilityFlag = "--$Visibility"
    & gh repo create $repo $visibilityFlag --description 'Unofficial .NET companion for SidePulse with a Spectre.Console terminal UI. Concept stage.' --source $root --remote origin --push
    if ($LASTEXITCODE -ne 0) { throw 'Repository creation/push failed. Inspect the remote before retrying.' }
    & gh repo edit $repo --enable-issues --enable-wiki
    if ($LASTEXITCODE -ne 0) { throw 'Repository was created, but repository settings need review.' }
    Write-Host "Created https://github.com/$repo"
    Write-Host 'Next: initialize the Wiki with one page and follow docs/wiki/Project-Setup.md.'
} finally {
    Pop-Location
}
