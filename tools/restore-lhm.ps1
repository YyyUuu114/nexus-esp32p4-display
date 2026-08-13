#requires -Version 7.4

[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$version = '0.9.6'
$archiveSha256 = '29739C4959B01B348FDDAD87664066634BCFD4F46E9BF41E4E916C318BCFDB99'
$librarySha256 = '984B539048880D37257A5EDEAB36378152C54943069F400D68A34565292D35DA'
$windowsPortsSha256 = '331FF3528809BF54382461FDF0FBDFDCB6C221D770DA7C755EF531801A80554A'
$windowsManagementSha256 = '01F9360D110863F810431C4D29ADA0FCA89F267343D030E98AA823EA4C0C0EBB'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$vendorRoot = Join-Path $repositoryRoot 'vendor'
$targetRoot = Join-Path $vendorRoot 'LibreHardwareMonitor'
$windowsRuntimeRoot = Join-Path $vendorRoot 'NexusRuntime'
$archivePath = Join-Path $vendorRoot "LibreHardwareMonitor-$version.NET.10.zip"
$downloadUri = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/download/v$version/LibreHardwareMonitor.NET.10.zip"

function Assert-FileHash([string]$Path, [string]$Expected) {
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -ne $Expected) {
        throw "SHA-256 mismatch for '$Path'. Expected $Expected, received $actual."
    }
}

function Initialize-WindowsRuntime {
    $portsSource = Join-Path $targetRoot 'runtimes\win\lib\net10.0\System.IO.Ports.dll'
    $managementSource = Join-Path $targetRoot 'runtimes\win\lib\net10.0\System.Management.dll'
    Assert-FileHash -Path $portsSource -Expected $windowsPortsSha256
    Assert-FileHash -Path $managementSource -Expected $windowsManagementSha256

    if (Test-Path -LiteralPath $windowsRuntimeRoot) {
        $resolvedVendor = [System.IO.Path]::GetFullPath($vendorRoot)
        $resolvedRuntime = [System.IO.Path]::GetFullPath($windowsRuntimeRoot)
        if (-not $resolvedRuntime.StartsWith($resolvedVendor + [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to replace unexpected runtime path: $resolvedRuntime"
        }
        Remove-Item -LiteralPath $windowsRuntimeRoot -Recurse -Force
    }

    New-Item -ItemType Directory -Path $windowsRuntimeRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $targetRoot -File -Filter '*.dll' |
        Where-Object { $_.Name -notin @('System.IO.Ports.dll', 'System.Management.dll') } |
        Copy-Item -Destination $windowsRuntimeRoot
    Copy-Item -LiteralPath $portsSource,$managementSource -Destination $windowsRuntimeRoot

    Assert-FileHash -Path (Join-Path $windowsRuntimeRoot 'LibreHardwareMonitorLib.dll') `
        -Expected $librarySha256
    Assert-FileHash -Path (Join-Path $windowsRuntimeRoot 'System.IO.Ports.dll') `
        -Expected $windowsPortsSha256
    Assert-FileHash -Path (Join-Path $windowsRuntimeRoot 'System.Management.dll') `
        -Expected $windowsManagementSha256
}

$libraryPath = Join-Path $targetRoot 'LibreHardwareMonitorLib.dll'
if ((Test-Path -LiteralPath $libraryPath) -and -not $Force) {
    Assert-FileHash -Path $libraryPath -Expected $librarySha256
    Initialize-WindowsRuntime
    Write-Host "LibreHardwareMonitor $version and isolated Windows runtime are verified."
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
Initialize-WindowsRuntime
Write-Host "LibreHardwareMonitor $version and isolated Windows runtime restored and verified."
