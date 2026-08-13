#requires -Version 7.4

[CmdletBinding()]
param(
    [string]$BuildDirectory = 'build'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$release = Get-Content -LiteralPath (Join-Path $repositoryRoot 'release.json') -Raw | ConvertFrom-Json
$buildRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $BuildDirectory))
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$version = [string]$release.productVersion
$packageName = "NEXUS-Firmware-ESP32P4-v$version"
$packageRoot = Join-Path $artifactRoot $packageName
$zipPath = Join-Path $artifactRoot "$packageName.zip"

& (Join-Path $PSScriptRoot 'validate-release.ps1') -Component firmware -Channel development

$images = [ordered]@{
    'bootloader.bin' = Join-Path $buildRoot 'bootloader\bootloader.bin'
    'partition-table.bin' = Join-Path $buildRoot 'partition_table\partition-table.bin'
    'nexus_display.bin' = Join-Path $buildRoot 'nexus_display.bin'
}
foreach ($image in $images.Values) {
    if (-not (Test-Path -LiteralPath $image -PathType Leaf)) {
        throw "Missing firmware build image: $image"
    }
}

foreach ($target in @($packageRoot, $zipPath)) {
    if (-not (Test-Path -LiteralPath $target)) { continue }
    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
    $resolvedTarget = [System.IO.Path]::GetFullPath($target)
    if (-not $resolvedTarget.StartsWith($resolvedArtifactRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected artifact path: $resolvedTarget"
    }
    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

foreach ($entry in $images.GetEnumerator()) {
    Copy-Item -LiteralPath $entry.Value -Destination (Join-Path $packageRoot $entry.Key)
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'release.json'),
                       (Join-Path $repositoryRoot 'LICENSE'),
                       (Join-Path $repositoryRoot 'SECURITY.md'),
                       (Join-Path $repositoryRoot 'docs\FLASHING.md') -Destination $packageRoot

$pythonCommand = Get-Command python -ErrorAction SilentlyContinue
if (-not $pythonCommand) {
    throw 'Python is unavailable. Run this script from an initialized ESP-IDF 5.5.x shell.'
}
$fullImage = Join-Path $packageRoot 'nexus-firmware-full.bin'
& $pythonCommand.Source -m esptool --chip esp32p4 merge_bin -o $fullImage `
    0x2000 (Join-Path $packageRoot 'bootloader.bin') `
    0x10000 (Join-Path $packageRoot 'partition-table.bin') `
    0x20000 (Join-Path $packageRoot 'nexus_display.bin')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $fullImage)) {
    throw "python -m esptool merge_bin failed with exit code $LASTEXITCODE."
}

@"
NEXUS Firmware ESP32-P4 v$version development

Protocol-1.x to v2.1.1 migration requires one complete flash erase.

Individual image offsets:
  0x2000   bootloader.bin
  0x10000  partition-table.bin
  0x20000  nexus_display.bin

Merged image:
  0x0      nexus-firmware-full.bin

See FLASHING.md for validated commands and safety notes.
"@ | Set-Content -LiteralPath (Join-Path $packageRoot 'FLASH-OFFSETS.txt') -Encoding ascii

$checksumLines = foreach ($file in Get-ChildItem -LiteralPath $packageRoot -File | Sort-Object Name) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    "$hash  $($file.Name)"
}
$checksumLines | Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') -Encoding ascii
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Firmware package: $zipPath"
Write-Host "Package SHA-256: $((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash)"
