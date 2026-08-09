#requires -Version 7.4

[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$version = '0.9.6'
$archiveSha256 = '29739C4959B01B348FDDAD87664066634BCFD4F46E9BF41E4E916C318BCFDB99'
$librarySha256 = '984B539048880D37257A5EDEAB36378152C54943069F400D68A34565292D35DA'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$vendorRoot = Join-Path $repositoryRoot 'vendor'
$targetRoot = Join-Path $vendorRoot 'LibreHardwareMonitor'
$archivePath = Join-Path $vendorRoot "LibreHardwareMonitor-$version.NET.10.zip"
$downloadUri = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v$version/LibreHardwareMonitor.NET.10.zip"

function Assert-FileHash([string]$Path, [string]$Expected) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -ne $Expected) {
        throw "SHA-256 mismatch for '$Path'. Expected $Expected, received $actual."
    }
}

$libraryPath = Join-Path $targetRoot 'LibreHardwareMonitorLib.dll'
if ((Test-Path -LiteralPath $libraryPath) -and -not $Force) {
    Assert-FileHash -Path $libraryPath -Expected $librarySha256
    Write-Host "LibreHardwareMonitor $version is already restored and verified."
    return
}

New-Item -ItemType Directory -Path $vendorRoot -Force | Out-Null
Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath
Assert-FileHash -Path $archivePath -Expected $archiveSha256

if (Test-Path -LiteralPath $targetRoot) {
    $resolvedVendor = [System.IO.Path]::GetFullPath($vendorRoot)
    $resolvedTarget = [System.IO.Path]::GetFullPath($targetRoot)
    if (-not $resolvedTarget.StartsWith($resolvedVendor + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace unexpected dependency path: $resolvedTarget"
    }
    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
Expand-Archive -LiteralPath $archivePath -DestinationPath $targetRoot -Force
if (-not (Test-Path -LiteralPath $libraryPath)) {
    throw 'The official archive did not contain LibreHardwareMonitorLib.dll at the expected path.'
}
Assert-FileHash -Path $libraryPath -Expected $librarySha256
Write-Host "LibreHardwareMonitor $version restored and verified."
