#requires -Version 7.4

[CmdletBinding()]
param(
    [ValidatePattern('^[A-Z]$')]
    [string]$DriveLetter,

    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$BuildDirectory = 'build-short'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not (Get-Command idf.py -ErrorAction SilentlyContinue)) {
    throw 'idf.py is unavailable. Run this script from an initialized ESP-IDF 5.5.x shell.'
}

if (-not $DriveLetter) {
    $DriveLetter = @('N', 'R', 'Q', 'P') |
        Where-Object { -not (Test-Path -LiteralPath "${_}:\") } |
        Select-Object -First 1
}
if (-not $DriveLetter -or (Test-Path -LiteralPath "${DriveLetter}:\")) {
    throw 'No free short-path drive letter is available. Pass -DriveLetter with an unused letter.'
}

$mappedDrive = "${DriveLetter}:"
$mappedRoot = "${mappedDrive}\"
$locationChanged = $false
try {
    & subst.exe $mappedDrive $repositoryRoot
    if ($LASTEXITCODE -ne 0) { throw "Could not map $mappedDrive to the source directory." }

    Push-Location -LiteralPath $mappedRoot
    $locationChanged = $true
    & idf.py -B (Join-Path $mappedRoot $BuildDirectory) build
    if ($LASTEXITCODE -ne 0) { throw "ESP-IDF build failed with exit code $LASTEXITCODE." }
}
finally {
    if ($locationChanged) { Pop-Location }
    & subst.exe $mappedDrive /D 2>$null
}
