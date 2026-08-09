#requires -Version 7.4

[CmdletBinding()]
param(
    [string]$DotNetPath = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$publishRoot = Join-Path $artifactRoot 'publish'
$containerRoot = Join-Path $artifactRoot 'NEXUS-Display-Windows-x64'
$packageRoot = Join-Path $containerRoot 'NEXUS Display'
$zipPath = Join-Path $artifactRoot 'NEXUS-Display-Windows-x64.zip'

& (Join-Path $PSScriptRoot 'validate-release.ps1') -Component desktop -Channel stable
& (Join-Path $PSScriptRoot 'restore-lhm.ps1')

foreach ($target in @($publishRoot, $containerRoot, $zipPath)) {
    if (-not (Test-Path -LiteralPath $target)) {
        continue
    }
    $resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
    $resolvedTarget = [System.IO.Path]::GetFullPath($target)
    if (-not $resolvedTarget.StartsWith($resolvedArtifactRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected artifact path: $resolvedTarget"
    }
    Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot,$packageRoot -Force | Out-Null
& $DotNetPath publish (Join-Path $repositoryRoot 'NexusDisplayAgent.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedExecutable = Join-Path $publishRoot 'Nexus Display.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw 'Published output does not contain Nexus Display.exe.'
}
Copy-Item -LiteralPath $publishedExecutable -Destination $packageRoot
Copy-Item -Path (Join-Path $repositoryRoot 'package\*.cmd') -Destination $packageRoot -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'package\使用说明.txt'),
                       (Join-Path $repositoryRoot 'package\update-channel.json') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE'),
                       (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md'),
                       (Join-Path $repositoryRoot 'COMPATIBILITY.md'),
                       (Join-Path $repositoryRoot 'release.json') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'licenses') -Destination $packageRoot -Recurse

Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Write-Host "Release package: $zipPath"
Write-Host "SHA-256: $hash"
